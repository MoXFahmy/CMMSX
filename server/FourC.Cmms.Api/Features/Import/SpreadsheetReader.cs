using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>One sheet as read: row 1 is the headings. A cell is a string, decimal, DateOnly, bool or null.</summary>
public sealed record SpreadsheetSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record SpreadsheetBook(IReadOnlyList<SpreadsheetSheet> Sheets);

/// <summary>
/// Reads an Excel workbook (.xlsx) or one CSV file, by hand like <see cref="Infrastructure.TableExport"/> writes them: no
/// package. Works with files saved by Excel, LibreOffice and Google Sheets (shared or inline strings, rich text, styles).
/// Rows keep their place (list index + 1 = the sheet's row number); trailing empty rows are left out.
/// </summary>
public static class SpreadsheetReader
{
    /// <summary>Cells past this column are ignored (a stray value far to the right would otherwise make every row huge).</summary>
    private const int MaxColumns = 500;

    /// <summary>
    /// Reads a workbook. Past <paramref name="maxRows"/> data rows in all (0.26.0) it stops and refuses the file, so a huge file
    /// is never held whole in memory before its row limit is checked.
    /// </summary>
    public static SpreadsheetBook Read(Stream content, string fileName, int maxRows = int.MaxValue)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var counter = new RowCounter(maxRows);
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(content, counter),
            ".csv" => new SpreadsheetBook([ReadCsv(content, Path.GetFileNameWithoutExtension(fileName)!, counter)]),
            _ => throw new DomainException("Save the file as .xlsx (Excel workbook) or .csv first."),
        };
    }

    // ---------------- Excel ----------------

    /// <summary>Counts data rows (every row after a sheet's heading row that holds a value) across the whole file.</summary>
    private sealed class RowCounter(int max)
    {
        private int _rows;
        public void Add()
        {
            if (++_rows > max) throw new DomainException(Translator.T($"This file has more than {max:N0} rows. Split it into smaller files."));
        }
    }

    private static SpreadsheetBook ReadXlsx(Stream content, RowCounter counter)
    {
        try
        {
            var ms = new MemoryStream();
            content.CopyTo(ms);
            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var strings = SharedStrings(zip);
            var dateStyles = DateStyles(zip);
            var targets = Relationships(zip, "xl/_rels/workbook.xml.rels");
            var sheets = new List<SpreadsheetSheet>();
            foreach (var (name, relId) in SheetList(zip))
            {
                if (!targets.TryGetValue(relId, out var target)) continue;
                var entry = zip.GetEntry(target);
                if (entry is null) continue;
                using var s = entry.Open();
                sheets.Add(new SpreadsheetSheet(name, SheetRows(s, strings, dateStyles, counter)));
            }
            return new SpreadsheetBook(sheets);
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or FormatException or OverflowException or ArgumentException or IOException)
        {
            throw new DomainException("This file can't be read as an Excel workbook.");
        }
    }

    private static XmlReader Xml(Stream s) => XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false, XmlResolver = null });

    private static List<string> SharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;
        using var s = entry.Open();
        using var r = Xml(s);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                list.Add(Text(r));
        return list;
    }

    /// <summary>
    /// The text of an &lt;si&gt; or &lt;is&gt; element: all &lt;t&gt; runs joined, phonetic hints (&lt;rPh&gt;) left out.
    /// Leaves the reader on the element's end tag (or on it, when empty), so the caller's next Read moves past it.
    /// </summary>
    private static string Text(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var sb = new StringBuilder();
        var depth = r.Depth;
        var phonetic = 0;
        r.Read();
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh" && !r.IsEmptyElement) phonetic++;
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "rPh") phonetic--;
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "t" && phonetic == 0 && !r.IsEmptyElement)
            {
                sb.Append(r.ReadElementContentAsString()); // moves past </t> by itself
                continue;
            }
            r.Read();
        }
        return sb.ToString();
    }

    /// <summary>For each cell style (its index in cellXfs), whether its number format shows a date.</summary>
    private static List<bool> DateStyles(ZipArchive zip)
    {
        var result = new List<bool>();
        var entry = zip.GetEntry("xl/styles.xml");
        if (entry is null) return result;
        var custom = new Dictionary<int, string>();
        using var s = entry.Open();
        using var r = Xml(s);
        var inCellXfs = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "numFmt"
                && int.TryParse(r.GetAttribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                custom[id] = r.GetAttribute("formatCode") ?? "";
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "cellXfs") inCellXfs = !r.IsEmptyElement;
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
            else if (inCellXfs && r.NodeType == XmlNodeType.Element && r.LocalName == "xf")
            {
                int.TryParse(r.GetAttribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fmt);
                result.Add(IsDateFormat(fmt, custom.GetValueOrDefault(fmt)));
            }
        }
        return result;
    }

    /// <summary>Built-in date formats are 14–22 and 45–47; a custom one is a date when it has d, m or y outside quotes and brackets.</summary>
    public static bool IsDateFormat(int id, string? code)
    {
        if (id is >= 14 and <= 22 or >= 45 and <= 47) return true;
        if (string.IsNullOrEmpty(code)) return false;
        var quoted = false; var bracket = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '\\') { i++; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '[') { bracket = true; continue; }
            if (c == ']') { bracket = false; continue; }
            if (bracket) continue;
            if (c is 'd' or 'D' or 'm' or 'M' or 'y' or 'Y') return true;
        }
        return false;
    }

    private static Dictionary<string, string> Relationships(ZipArchive zip, string path)
    {
        var map = new Dictionary<string, string>();
        var entry = zip.GetEntry(path);
        if (entry is null) return map;
        using var s = entry.Open();
        using var r = Xml(s);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "Relationship") continue;
            var id = r.GetAttribute("Id"); var target = r.GetAttribute("Target");
            if (id is null || target is null) continue;
            map[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        return map;
    }

    private static List<(string Name, string RelId)> SheetList(ZipArchive zip)
    {
        var list = new List<(string, string)>();
        var entry = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException();
        using var s = entry.Open();
        using var r = Xml(s);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "sheet") continue;
            var name = r.GetAttribute("name") ?? "";
            var id = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            if (id is not null) list.Add((name, id));
        }
        return list;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> SheetRows(Stream s, List<string> strings, List<bool> dateStyles, RowCounter counter)
    {
        var rows = new SortedDictionary<int, List<object?>>();
        using var r = Xml(s);
        var rowNumber = 0;
        var counted = false;
        List<object?>? current = null;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "row")
            {
                rowNumber = int.TryParse(r.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : rowNumber + 1;
                current = [];
                counted = false;
                rows[rowNumber] = current;
                if (r.IsEmptyElement) current = null;
            }
            else if (r.LocalName == "c" && current is not null)
            {
                var col = ColumnIndex(r.GetAttribute("r")) ?? current.Count;
                var type = r.GetAttribute("t");
                int.TryParse(r.GetAttribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var style);
                var value = CellValue(r, type, style, strings, dateStyles);
                if (col >= MaxColumns || value is null) continue;
                if (!counted && rowNumber > 1) { counted = true; counter.Add(); }
                while (current.Count < col) current.Add(null);
                if (current.Count == col) current.Add(value); else current[col] = value;
            }
        }
        // Dense from row 1 to the last row holding a value, so list index + 1 is the row number.
        var last = rows.Where(kv => kv.Value.Any(v => v is not null)).Select(kv => kv.Key).DefaultIfEmpty(0).Max();
        var result = new List<IReadOnlyList<object?>>(last);
        for (var i = 1; i <= last; i++) result.Add(rows.TryGetValue(i, out var row) ? row : []);
        return result;
    }

    private static object? CellValue(XmlReader r, string? type, int style, List<string> strings, List<bool> dateStyles)
    {
        if (r.IsEmptyElement) return null;
        string? v = null; string? inline = null;
        var depth = r.Depth;
        r.Read();
        // Stop on the cell's end tag, so the sheet loop's next Read moves to the next cell.
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "v") { v = r.ReadElementContentAsString(); continue; }
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "is") inline = Text(r);
            r.Read();
        }
        switch (type)
        {
            case "s":
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < strings.Count ? strings[i] : null;
            case "inlineStr": return inline;
            case "str" or "e": return v;
            case "b": return v is null ? null : v.Trim() == "1";
            default:
                if (string.IsNullOrEmpty(v)) return null;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return v;
                if (style >= 0 && style < dateStyles.Count && dateStyles[style] && d is >= 1 and < 2958466)
                    return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(d)));
                return decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : (decimal)d;
        }
    }

    /// <summary>"C12" → 2 (zero-based column).</summary>
    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var n = 0; var any = false;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z') { n = n * 26 + (ch - 'A' + 1); any = true; }
            else if (ch is >= 'a' and <= 'z') { n = n * 26 + (ch - 'a' + 1); any = true; }
            else break;
            if (n > 20000) return MaxColumns;
        }
        return any ? n - 1 : null;
    }

    // ---------------- CSV ----------------

    /// <summary>UTF-8 (with or without BOM), comma or semicolon separated (whichever the heading line uses more), quotes as in RFC 4180.</summary>
    private static SpreadsheetSheet ReadCsv(Stream content, string name, RowCounter counter)
    {
        string text;
        using (var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true)) text = reader.ReadToEnd();
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n')[0];
        var sep = Count(firstLine, ';') > Count(firstLine, ',') ? ';' : ',';

        var rows = new List<IReadOnlyList<object?>>();
        var row = new List<object?>();
        var cell = new StringBuilder();
        var quoted = false; var cellQuoted = false;
        void EndCell() { var s = cell.ToString(); row.Add(s.Length == 0 && !cellQuoted ? null : s); cell.Clear(); cellQuoted = false; }
        void EndRow() { EndCell(); if (rows.Count > 0 && row.Any(v => v is not null)) counter.Add(); rows.Add(row); row = []; }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) { quoted = true; cellQuoted = true; }
            else if (c == sep) EndCell();
            else if (c == '\r') { }
            else if (c == '\n') EndRow();
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0 || cellQuoted) EndRow();
        while (rows.Count > 0 && rows[^1].All(v => v is null)) rows.RemoveAt(rows.Count - 1);
        return new SpreadsheetSheet(name, rows);
    }

    private static int Count(string line, char c)
    {
        var n = 0; var quoted = false;
        foreach (var ch in line) { if (ch == '"') quoted = !quoted; else if (ch == c && !quoted) n++; }
        return n;
    }
}
