using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>The workbooks people download: the empty template, the current data in the import layout, and problem rows.</summary>
public static class ImportWorkbook
{
    public const int MaxExportRows = ImportRun.MaxRowsPerSheet;

    /// <summary>Every sheet with its headings only (custom fields included), in the request's language, and a Help sheet.</summary>
    public static async Task<byte[]> TemplateAsync(CmmsDbContext db, CancellationToken ct)
    {
        var fields = await db.CustomFields.AsNoTracking().Where(f => f.EntityType == "Asset").OrderBy(f => f.SortOrder).ToListAsync(ct);
        var sheets = Defs(fields).Select(d => new ExportSheet(d.Name, Columns(d), [])).ToList();
        sheets.Add(Help(Defs(fields), []));
        return TableExport.Xlsx(sheets);
    }

    /// <summary>
    /// Every sheet filled with the organization's records (not deleted ones; inactive ones too), with codes rather than
    /// names, so it imports back with no changes. At most 5,000 rows a sheet; the Help sheet names any sheet cut off.
    /// </summary>
    public static async Task<byte[]> CurrentAsync(CmmsDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        var c = await ImportRun.ContextAsync(db, ctx, ct);
        var defs = Defs(c.AssetFields);
        var cut = new List<string>();
        var sheets = new List<ExportSheet>();
        foreach (var def in defs)
        {
            var rows = await RowsAsync(c, def.Key, ct);
            var wasCut = false;
            // A task's steps or parts go out whole or not at all: loading a list back replaces the task's list.
            if (def.Key is "PmSteps" or "PmParts") (rows, wasCut) = WholeLists(rows, MaxExportRows, "taskCode");
            else if (def.Key is "AssetParts") (rows, wasCut) = WholeLists(rows, MaxExportRows, "assetNumber");
            else if (rows.Count > MaxExportRows) { wasCut = true; rows = rows.Take(MaxExportRows).ToList(); }
            if (wasCut) cut.Add(def.Name);
            sheets.Add(new ExportSheet(def.Name, Columns(def), rows.Select(r => def.Columns.Select(col => Cell(r.GetValueOrDefault(col.Key))).ToArray()).ToList()));
        }
        sheets.Add(Help(defs, cut));
        return TableExport.Xlsx(sheets);
    }

    /// <summary>For each sheet with problems: its heading row and the problem rows as they were in the file, plus "Problem".</summary>
    public static byte[] Problems(SpreadsheetBook book, ImportResult result)
    {
        var sheets = new List<ExportSheet>();
        foreach (var sheet in result.Sheets.Where(s => s.Problems > 0))
        {
            var source = book.Sheets.FirstOrDefault(s => ImportSheets.Find(s.Name)?.Key == sheet.Sheet);
            if (source is null || source.Rows.Count == 0) continue;
            var headings = source.Rows[0].Select(h => h switch { null => "", string s => s, var v => ImportValues.Show(v) ?? "" }).ToList();
            var width = Math.Max(headings.Count, source.Rows.Max(r => r.Count));
            while (headings.Count < width) headings.Add("");
            var columns = headings.Select((h, i) => new ExportColumn($"c{i}", h)).Append(new ExportColumn("problem", Translator.T("Problem"))).ToList();
            var rows = sheet.Rows.Where(r => r.Outcome == RowOutcome.Problem && r.Row - 1 < source.Rows.Count)
                .Select(r => Enumerable.Range(0, width).Select(i => Cell(source.Rows[r.Row - 1].ElementAtOrDefault(i))).Append(string.Join(" ", r.Problems)).ToArray())
                .ToList();
            sheets.Add(new ExportSheet(source.Name, columns, rows));
        }
        return TableExport.Xlsx(sheets);
    }

    /// <summary>
    /// At most <paramref name="max"/> rows, ending where one owner's list ends (rows come grouped by <paramref name="ownerKey"/>:
    /// a task's code, an asset's number), and whether any were left out.
    /// </summary>
    public static (List<Dictionary<string, object?>> Rows, bool Cut) WholeLists(List<Dictionary<string, object?>> rows, int max, string ownerKey)
    {
        if (rows.Count <= max) return (rows, false);
        var keep = max;
        var boundary = rows[max][ownerKey] as string;
        while (keep > 0 && string.Equals(rows[keep - 1][ownerKey] as string, boundary, StringComparison.OrdinalIgnoreCase)) keep--;
        return (rows.Take(keep).ToList(), true);
    }

    public static (List<Dictionary<string, object?>> Rows, bool Cut) WholeTasks(List<Dictionary<string, object?>> rows, int max) => WholeLists(rows, max, "taskCode");

    private static List<SheetDef> Defs(IReadOnlyList<Core.Assets.CustomFieldDefinition> fields) =>
        ImportSheets.All.Select(d => d.Key == "Assets" ? SheetImporters.AssetSheet(fields) : d).ToList();

    private static List<ExportColumn> Columns(SheetDef def) => def.Columns.Select(c => new ExportColumn(c.Key, c.Heading)).ToList();

    /// <summary>A value as a cell: numbers stay numbers and dates dates; words, Yes/No and lists in the request's language.</summary>
    private static object? Cell(object? value) => value switch
    {
        null => null,
        int i => (decimal)i,
        decimal or DateOnly => value,
        bool b => Translator.T(b ? "Yes" : "No"),
        Enum e => ImportSheets.ChoiceText(e),
        IEnumerable<Enum> choices => ImportValues.Show(choices),
        string s => s,
        IEnumerable<string> list => string.Join(", ", list),
        _ => ImportValues.Show(value),
    };

    /// <summary>Each sheet's columns: heading, required on new rows, allowed values. First line: sheets cut off at 5,000 rows.</summary>
    private static ExportSheet Help(List<SheetDef> defs, List<string> cut)
    {
        var rows = new List<object?[]>();
        if (cut.Count > 0) rows.Add([Translator.T($"Cut off at 5,000 rows: {string.Join(", ", cut)}."), null, null, null]);
        foreach (var def in defs)
            foreach (var col in def.Columns)
                rows.Add([def.Name, col.Heading, Translator.T(col.RequiredOnNew ? "Yes" : "No"), Allowed(col)]);
        string[] titles = ["Sheet", "Column", "Required on new rows", "Allowed values"];
        return new ExportSheet(Translator.T("Help"), titles.Select((t, i) => new ExportColumn($"h{i}", Translator.T(t))).ToList(), rows);
    }

    private static string? Allowed(ColumnDef col) => col.Kind switch
    {
        ValueKind.Choice or ValueKind.ChoiceList => string.Join(", ", Enum.GetValues(col.Choices!).Cast<Enum>().Select(ImportSheets.ChoiceText)),
        ValueKind.YesNo => $"{Translator.T("Yes")}, {Translator.T("No")}",
        ValueKind.Date => "yyyy-mm-dd",
        ValueKind.Number => Translator.T("A number"),
        ValueKind.Integer => Translator.T("A whole number"),
        ValueKind.CodeList => Translator.T("Codes separated by commas"),
        _ => null,
    };

    /// <summary>A sheet's records as the import sees them (the importers' own values, so the two can't drift apart).</summary>
    private static async Task<List<Dictionary<string, object?>>> RowsAsync(ImportContext c, string sheet, CancellationToken ct)
    {
        var db = c.Db;
        var take = MaxExportRows + 1;
        switch (sheet)
        {
            case "Sites": return Of(new SiteImporter(), c, await db.Sites.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Locations": return Of(new LocationImporter(), c, await db.Locations.AsNoTracking().OrderBy(x => x.Path).Take(take).ToListAsync(ct));
            case "Categories": return Of(new CategoryImporter(), c, await db.AssetCategories.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Trades": return Of(new TradeImporter(), c, await db.Trades.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "PartGroups": return Of(new PartGroupImporter(), c, await db.PartGroups.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Suppliers": return Of(new SupplierImporter(), c, await db.Suppliers.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Stores": return Of(new StoreImporter(), c, await db.Stores.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Assets": return Of(new AssetImporter(), c, await db.Assets.AsNoTracking().OrderBy(x => x.AssetNumber).Take(take).ToListAsync(ct));
            case "People": return Of(new PeopleImporter(), c, await db.Employees.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "Parts": return Of(new PartImporter(), c, await db.Parts.AsNoTracking().OrderBy(x => x.PartNumber).Take(take).ToListAsync(ct));
            case "Stock":
                return Of(new StockImporter(), c, await db.StockLevels.AsNoTracking().OrderBy(x => x.Part!.PartNumber).ThenBy(x => x.Store!.Code).Take(take).ToListAsync(ct));
            case "PmTasks": return Of(new PmTaskImporter(), c, await db.PmTasks.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "MeasurementPoints":
                return Of(new MeasurementPointImporter(), c, await db.MeasurementPoints.AsNoTracking().Include(x => x.Assets).OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            case "PmSteps":
                // Ordered by task code, with room to finish the task at the 5,000-row boundary (at most 300 steps a task).
                return (await (from s in db.PmTaskSteps.AsNoTracking() join t in db.PmTasks on s.PmTaskId equals t.Id
                               orderby t.Code, s.Sequence select new { t.Code, s.Sequence, s.Description, s.Type, s.Unit, s.Decimals, s.MeasurementPointId, s.PhotoRequired,
                                   s.BeforeStart, s.FollowUp, s.FollowUpTemplateId }).Take(take + 300).ToListAsync(ct))
                    .Select(s => new Dictionary<string, object?>
                    {
                        ["taskCode"] = s.Code, ["step"] = s.Sequence, ["description"] = s.Description, ["type"] = s.Type, ["unit"] = s.Unit, ["decimals"] = s.Decimals,
                        ["measurementPoint"] = s.MeasurementPointId is { } mp ? c.CodeOf.GetValueOrDefault(mp) : null, ["photoRequired"] = s.PhotoRequired,
                        ["beforeStart"] = s.BeforeStart, ["followUp"] = s.FollowUp,
                        ["followUpTemplate"] = s.FollowUpTemplateId is { } ft ? c.CodeOf.GetValueOrDefault(ft) : null,
                    }).ToList();
            case "PmParts":
                return (await (from p in db.PmTaskParts.AsNoTracking() join t in db.PmTasks on p.PmTaskId equals t.Id join part in db.Parts on p.PartId equals part.Id
                               orderby t.Code, part.PartNumber select new { t.Code, part.PartNumber, p.Quantity }).Take(take + 200).ToListAsync(ct))
                    .Select(p => new Dictionary<string, object?> { ["taskCode"] = p.Code, ["partNumber"] = p.PartNumber, ["quantity"] = p.Quantity }).ToList();
            case "AssetParts":
                // Ordered by asset number, with room to finish the asset's list at the 5,000-row boundary.
                return (await (from l in db.AssetParts.AsNoTracking() join a in db.Assets on l.AssetId equals a.Id join p in db.Parts on l.PartId equals p.Id
                               orderby a.AssetNumber, p.PartNumber select new { a.AssetNumber, p.PartNumber, l.Quantity, l.Note }).Take(take + AssetPartImporter.MaxLines).ToListAsync(ct))
                    .Select(l => new Dictionary<string, object?> { ["assetNumber"] = l.AssetNumber, ["partNumber"] = l.PartNumber, ["quantity"] = l.Quantity, ["note"] = l.Note })
                    .ToList();
            case "PmSchedules": return Of(new PmScheduleImporter(), c, await db.PmSchedules.AsNoTracking().OrderBy(x => x.Code).Take(take).ToListAsync(ct));
            default: return [];
        }
    }

    private static List<Dictionary<string, object?>> Of<T>(RecordImporter<T> importer, ImportContext c, List<T> records) where T : class =>
        records.Select(r => importer.Export(c, r)).ToList();
}
