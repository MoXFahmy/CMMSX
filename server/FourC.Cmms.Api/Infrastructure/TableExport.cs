using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>A column of an exported list: the property to read (camelCase, dots reach into objects) and its heading.</summary>
public sealed record ExportColumn(string Key, string Title);

/// <summary>
/// Turns list rows into CSV or Excel (.xlsx) files. Values read like the screen: codes in words, "Yes"/"No", lists joined
/// with ", ", date-times in the organization's time zone; in Excel, dates are real dates and numbers are numbers.
/// The .xlsx is written by hand (SpreadsheetML in a zip), so no package is needed.
/// </summary>
public static class TableExport
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Props = new();

    /// <summary>"InProgress" → "In progress"; runs of capitals ("PM") stay together.</summary>
    public static string Words(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var startsWord = i > 0 && char.IsUpper(c) && (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]) && char.IsUpper(value[i - 1])));
            if (startsWord) sb.Append(' ');
            sb.Append(c);
        }
        var words = sb.ToString().Split(' ');
        for (var i = 1; i < words.Length; i++)
            if (words[i].Length > 1 && words[i].Skip(1).All(char.IsLower)) words[i] = char.ToLowerInvariant(words[i][0]) + words[i][1..];
        return string.Join(' ', words);
    }

    /// <summary>Every simple property of <paramref name="type"/> except ids, headed by its key.</summary>
    public static IReadOnlyList<ExportColumn> DefaultColumns(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => !IsId(p) && !typeof(IDictionary).IsAssignableFrom(p.PropertyType))
        .Select(p => { var key = char.ToLowerInvariant(p.Name[0]) + p.Name[1..]; return new ExportColumn(key, key); }).ToList();

    private static bool IsId(PropertyInfo p) =>
        (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(Guid) || p.Name == "Id" || p.Name.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>The cell values of each item, in column order. Throws <see cref="ArgumentException"/> for a key the rows don't have.</summary>
    public static IReadOnlyList<object?[]> Rows(IEnumerable<object> items, IReadOnlyList<ExportColumn> cols, TimeZoneInfo tz)
    {
        var list = items.ToList();
        if (list.Count > 0)
            foreach (var c in cols)
                if (Prop(list[0].GetType(), c.Key.Split('.')[0]) is null) throw new ArgumentException($"Unknown column: {c.Key}");
        return list.Select(item => cols.Select(c => Cell(Read(item, c.Key), tz)).ToArray()).ToList();
    }

    private static PropertyInfo? Prop(Type t, string name) =>
        Props.GetOrAdd((t, name), k => k.Item1.GetProperty(k.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

    private static object? Read(object item, string key)
    {
        object? current = item;
        foreach (var part in key.Split('.'))
        {
            current = current switch
            {
                null => null,
                IDictionary<string, string?> d => d.TryGetValue(part, out var v) ? v : null,
                IDictionary d => d.Contains(part) ? d[part] : null,
                _ => Prop(current.GetType(), part)?.GetValue(current),
            };
        }
        return current;
    }

    private static object? Cell(object? v, TimeZoneInfo tz) => v switch
    {
        null => null,
        string s => s,
        Enum e => Words(e.ToString()),
        bool b => b ? "Yes" : "No",
        DateOnly d => d,
        DateTimeOffset o => TimeZoneInfo.ConvertTime(o, tz).DateTime,
        DateTime dt => dt,
        decimal or double or float or int or long or short or byte => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        IEnumerable seq => string.Join(", ", seq.Cast<object?>().Where(x => x is not null).Select(x => x is Enum en ? Words(en.ToString()) : Convert.ToString(x, CultureInfo.InvariantCulture))),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    };

    private static string Plain(object? v) => v switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>UTF-8 with a byte-order mark (so Excel reads Arabic), CRLF lines, RFC 4180 quoting; text that Excel would run as a formula gets a leading '.</summary>
    public static byte[] Csv(IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', cols.Select(c => Field(c.Title)))).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join(',', r.Select(v => v is string s ? Field(s) : Plain(v)))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];

        static string Field(string s)
        {
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
    }

    /// <summary>One sheet: bold, frozen header row with filters, dates and numbers as real values, widths from the text.</summary>
    public static byte[] Xlsx(string sheetName, IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)
    {
        var name = new string(sheetName.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        name = name.Length == 0 ? "Sheet1" : name[..Math.Min(31, name.Length)];
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            const string head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
            Add("[Content_Types].xml", head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Add("_rels/.rels", head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", head + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + $"<sheets><sheet name=\"{Esc(name)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            // Cell styles: 0 normal, 1 bold header, 2 date (built-in format 14), 3 date and time.
            Add("xl/styles.xml", head + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/></numFmts>"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>"
                + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            Add("xl/worksheets/sheet1.xml", Sheet(cols, rows));
        }
        return ms.ToArray();
    }

    private static string Sheet(IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)
    {
        var last = Col(Math.Max(cols.Count, 1) - 1);
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (cols.Count > 0)
        {
            sb.Append("<cols>");
            for (var i = 0; i < cols.Count; i++)
            {
                var len = rows.Take(500).Select(r => Plain(r[i]).Length).Append(cols[i].Title.Length).Max();
                sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Math.Clamp(len + 2, 8, 60)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData><row r=\"1\">");
        for (var i = 0; i < cols.Count; i++) sb.Append($"<c r=\"{Col(i)}1\" s=\"1\" t=\"inlineStr\"><is><t>{Esc(cols[i].Title)}</t></is></c>");
        sb.Append("</row>");
        for (var r = 0; r < rows.Count; r++)
        {
            var n = r + 2;
            sb.Append($"<row r=\"{n}\">");
            for (var i = 0; i < cols.Count; i++)
            {
                var at = $"{Col(i)}{n}";
                switch (rows[r][i])
                {
                    case null: break;
                    case decimal m: sb.Append($"<c r=\"{at}\"><v>{m.ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case DateOnly d: sb.Append($"<c r=\"{at}\" s=\"2\"><v>{d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case DateTime dt: sb.Append($"<c r=\"{at}\" s=\"3\"><v>{dt.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case var v: sb.Append($"<c r=\"{at}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Plain(v))}</t></is></c>"); break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (cols.Count > 0) sb.Append($"<autoFilter ref=\"A1:{last}{rows.Count + 1}\"/>");
        return sb.Append("</worksheet>").ToString();
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    private static string Col(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    /// <summary>XML-escaped, with characters XML can't hold dropped.</summary>
    private static string Esc(string s) => System.Security.SecurityElement.Escape(new string(s.Where(XmlConvert.IsXmlChar).ToArray())) ?? "";
}
