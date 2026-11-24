using System.Diagnostics;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Import;

public enum RowOutcome { New, Changed, Unchanged, Problem }

public sealed record FieldChange(string Column, string? Old, string? New);

public sealed record RowResult(int Row, string Key, RowOutcome Outcome, IReadOnlyList<FieldChange> Changes, IReadOnlyList<string> Problems);

/// <summary>One sheet's outcome. <see cref="Rows"/> lists the new, changed and problem rows (not the unchanged ones).</summary>
public sealed record SheetResult(string Sheet, int New, int Changed, int Unchanged, int Problems, IReadOnlyList<RowResult> Rows, IReadOnlyList<string> IgnoredColumns);

public sealed record ImportResult(bool Saved, IReadOnlyList<SheetResult> Sheets, IReadOnlyList<string> NewLookups, IReadOnlyList<string> Notes, string? Stopped);

/// <summary>A data row of a sheet: its cells already read as their columns expect, and the problems found so far.</summary>
public sealed class ImportRow(int number, SheetDef sheet, Dictionary<string, object?> values, Dictionary<string, string?> raw)
{
    private readonly List<string> _problems = [];

    /// <summary>The row's number in the sheet (the heading row is 1).</summary>
    public int Number => number;
    public SheetDef Sheet => sheet;
    public bool Has(string key) => values.TryGetValue(key, out var v) && v is not null;

    /// <summary>The cell's value: ask with the nullable type (Get&lt;decimal?&gt;, Get&lt;string&gt;, Get&lt;AssetStatus?&gt;).</summary>
    public T? Get<T>(string key) => values.TryGetValue(key, out var v) && v is not null ? (T?)v : default;

    public object? Value(string key) => values.GetValueOrDefault(key);

    /// <summary>Replaces a cell's value (an importer putting it in the form the screen stores).</summary>
    public void Set(string key, object? value) => values[key] = value;
    public IEnumerable<string> Keys => values.Keys;
    public string? Raw(string key) => raw.GetValueOrDefault(key);
    public void Problem(string message) { if (!_problems.Contains(message)) _problems.Add(message); }
    public IReadOnlyList<string> Problems => _problems;
    public bool HasProblems => _problems.Count > 0;
}

/// <summary>What a run shares between sheets: the database, today, and every code known so far (kept up to date as rows are added).</summary>
public sealed class ImportContext(CmmsDbContext db, ITenantContext tenant, TenantSettings settings, DateOnly today)
{
    private static Dictionary<string, Guid> Codes() => new(StringComparer.OrdinalIgnoreCase);

    public CmmsDbContext Db => db;
    public ITenantContext Tenant => tenant;

    /// <summary>An import (numbers are taken for good) or a check (numbers are only shown).</summary>
    public bool Saving { get; set; }

    /// <summary>
    /// Takes <paramref name="count"/> numbers of a sequence (0.26.0) in a short step of its own, kept at once, so a long import
    /// never holds the numbering others need; a check only shows the next number. If the import then fails, they're skipped.
    /// </summary>
    public async Task<long> TakeNumbersAsync(string name, int count, CancellationToken ct)
    {
        if (!Saving) return await db.PeekNextNumberAsync(name, ct);
        await using var own = Own();
        return await own.NextNumbersAsync(name, count, ct);
    }

    /// <summary>Gives back the end of a block nobody used, if no one has taken a number since (imports only).</summary>
    public async Task ReturnNumbersAsync(string name, long firstUnused, long end, CancellationToken ct)
    {
        if (!Saving) return;
        await using var own = Own();
        await own.ReturnNumbersAsync(name, firstUnused, end, ct);
    }

    /// <summary>A context on its own connection, outside the import's transaction.</summary>
    private CmmsDbContext Own() =>
        new((DbContextOptions<CmmsDbContext>)Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>(db),
            new FixedTenantContext(db.CurrentTenantId));
    public TenantSettings Settings => settings;
    public DateOnly Today => today;

    public Dictionary<string, Guid> Sites { get; } = Codes();
    /// <summary>Locations by <see cref="LocationKey"/> (site + code).</summary>
    public Dictionary<string, Guid> Locations { get; } = Codes();
    public Dictionary<string, Guid> Categories { get; } = Codes();
    public Dictionary<string, Guid> Trades { get; } = Codes();
    public Dictionary<string, Guid> PartGroups { get; } = Codes();
    public Dictionary<string, Guid> Symptoms { get; } = Codes();
    /// <summary>Causes are the failure codes (0.14.0).</summary>
    public Dictionary<string, Guid> Causes { get; } = Codes();
    public Dictionary<string, Guid> Remedies { get; } = Codes();
    public Dictionary<string, Guid> Suppliers { get; } = Codes();
    public Dictionary<string, Guid> CostCentres { get; } = Codes();
    public Dictionary<string, Guid> AssetGroups { get; } = Codes();
    public Dictionary<string, Guid> Stores { get; } = Codes();
    public Dictionary<string, Guid> Assets { get; } = Codes();
    public Dictionary<string, Guid> Employees { get; } = Codes();
    public Dictionary<string, Guid> Parts { get; } = Codes();
    public Dictionary<string, Guid> PmTasks { get; } = Codes();
    public Dictionary<string, Guid> MeasurementPoints { get; } = Codes();

    /// <summary>Code by id, for showing a record's current references ("old" values) as codes.</summary>
    public Dictionary<Guid, string> CodeOf { get; } = [];

    /// <summary>The organization's custom fields on assets (columns of the Assets sheet).</summary>
    public IReadOnlyList<CustomFieldDefinition> AssetFields { get; set; } = [];

    /// <summary>Each asset's parent, to catch an asset put under its own child.</summary>
    public Dictionary<Guid, Guid?> AssetParents { get; } = [];

    /// <summary>Each asset's site, to check a PM schedule's asset is in the schedule's site.</summary>
    public Dictionary<Guid, Guid> AssetSites { get; } = [];
    /// <summary>Asset numbers in sites the person can't see (for limited people only).</summary>
    public HashSet<string> HiddenAssetNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each location's site and parent, to catch a location put under its own descendant.</summary>
    public Dictionary<Guid, (Guid SiteId, Guid? ParentId)> LocationTree { get; } = [];

    public List<string> NewLookups { get; } = [];
    public List<string> Notes { get; } = [];

    public static string LocationKey(Guid siteId, string code) => $"{siteId:N}|{code.Trim()}";

    public void Remember(Dictionary<string, Guid> map, string code, Guid id) { map[code.Trim()] = id; CodeOf[id] = code.Trim(); }

    public void Note(string note) { if (!Notes.Contains(note)) Notes.Add(note); }

    /// <summary>The id of a category, trade or part group with this code; an unknown code is added, named after itself, and listed.</summary>
    public Guid EnsureLookup(Dictionary<string, Guid> map, string code, string sheetKey)
    {
        code = code.Trim();
        if (map.TryGetValue(code, out var id)) return id;
        Guid newId;
        switch (sheetKey)
        {
            case "Categories":
                newId = AssetCategoryEndpoints.CreateCategory(db, new AssetCategoryEndpoints.CategoryInput(code, code, null, null)).Id;
                break;
            case "Trades":
                var trade = new FourC.Cmms.Core.People.Trade { Code = "", Name = "" };
                PeopleEndpoints.ApplyLookup(trade, new PeopleEndpoints.LookupInput(code, code, null), (t, color) => t.Color = color);
                db.Trades.Add(trade);
                newId = trade.Id;
                break;
            case "PartGroups":
                var group = new FourC.Cmms.Core.Inventory.PartGroup { Code = "", Name = "" };
                PeopleEndpoints.ApplyLookup(group, new PeopleEndpoints.LookupInput(code, code, null), null);
                db.PartGroups.Add(group);
                newId = group.Id;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(sheetKey));
        }
        Remember(map, code, newId);
        var sheet = ImportSheets.All.First(s => s.Key == sheetKey);
        NewLookups.Add($"{sheet.Name} {code}");
        return newId;
    }
}

/// <summary>Imports the rows of one sheet. One instance per run.</summary>
internal interface ISheetImporter
{
    string Sheet { get; }
    /// <summary>The columns that make a row's key; a sheet without one of them can't be imported.</summary>
    IReadOnlyList<string> KeyColumns { get; }
    string KeyOf(ImportRow row);
    /// <summary>The order to take the rows in (parents before children); the default keeps the sheet's order.</summary>
    IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) => rows;
    Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct);
    Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct);

    /// <summary>A last look at the sheet's results before <see cref="FinishAsync"/> (a list importer blocks a task with a problem row).</summary>
    IReadOnlyList<RowResult> Review(ImportContext c, IReadOnlyList<ImportRow> rows, IReadOnlyList<RowResult> results) => results;

    Task FinishAsync(ImportContext c, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Runs an import workbook: every sheet in order, inside one transaction. A check rolls back, an import commits, so a
/// preview shows exactly what an import does. A run that goes over its time budget stops and rolls back.
/// </summary>
public static class ImportRun
{
    public const int MaxRowsPerSheet = 20_000;
    /// <summary>Measured (0.26.0): 20,000 asset rows add about 280 MB while they import, so a file holds at most that, to keep a 1 GB server safe.</summary>
    public const int MaxRowsPerFile = 20_000;
    public const string TooBig = "This import took too long. Split it into smaller files.";

    private sealed class Stopped : Exception;

    public static Task<ImportResult> RunAsync(CmmsDbContext db, ITenantContext ctx, SpreadsheetBook book, bool save, TimeSpan budget, CancellationToken ct) =>
        RunAsync(db, ctx, book, save, budget, null, ct);

    /// <summary>The row limits (0.26.0): 20,000 a sheet, 50,000 in one file. Checked when a file is uploaded and again when it runs.</summary>
    public static int CheckLimits(SpreadsheetBook book)
    {
        var total = 0;
        foreach (var (def, sheet) in Match(book))
        {
            var count = sheet.Rows.Skip(1).Count(r => !IsBlank(r));
            if (count > MaxRowsPerSheet)
                throw new DomainException(Translator.T($"{def.Name} has {count:N0} rows; the limit is 20,000 per sheet. Split it into smaller files."));
            total += count;
        }
        if (total > MaxRowsPerFile) throw new DomainException(Translator.T($"This file has {total:N0} rows; the limit is 20,000 in one file. Split it into smaller files."));
        return total;
    }

    /// <summary>
    /// As above, with the time taken so far read from <paramref name="elapsed"/> (tests stand in a clock) and progress reported
    /// as (sheet, rows done, rows in the sheet) at least every 100 rows. An import runs <paramref name="beforeCommit"/> with its
    /// result inside its own transaction, just before it commits (0.26.1: the job is marked done in the same save).
    /// </summary>
    public static async Task<ImportResult> RunAsync(CmmsDbContext db, ITenantContext ctx, SpreadsheetBook book, bool save, TimeSpan budget,
        Func<TimeSpan>? elapsed, CancellationToken ct, Func<string, int, int, Task>? progress = null, Func<ImportResult, CancellationToken, Task>? beforeCommit = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var clock = elapsed ?? (() => stopwatch.Elapsed);
        ImportSheets.Organization = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var plan = Match(book);
        CheckLimits(book);

        // Database work still running when the time is up is cancelled; the run then stops like any other over-time run.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = budget - clock();
        if (remaining > TimeSpan.Zero) deadline.CancelAfter(remaining);
        var token = deadline.Token;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            try
            {
                var result = await RunSheetsAsync(db, ctx, plan, clock, budget, token, progress, save);
                // New brands, models and units from the rows (0.21.0), counted in the check and kept only when the import saves.
                var (brands, models, units) = await ReferenceLists.ReconcileAsync(db, db.CurrentTenantId!.Value, token);
                var listNotes = new List<string>();
                if (brands > 0) listNotes.Add(Translator.T($"New brands: {brands}"));
                if (models > 0) listNotes.Add(Translator.T($"New models: {models}"));
                if (units > 0) listNotes.Add(Translator.T($"New units: {units}"));
                if (listNotes.Count > 0) result = result with { Notes = [.. result.Notes, .. listNotes] };
                // The last word on time: nothing is committed once the page may have given up waiting.
                if (clock() > budget) throw new Stopped();
                if (save)
                {
                    if (beforeCommit is not null) await beforeCommit(result with { Saved = true }, token);
                    await tx.CommitAsync(CancellationToken.None);
                    Infrastructure.AccessService.LocationsChanged(); // locations may have moved
                }
                else await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return result with { Saved = save };
            }
            catch (Exception e) when (e is Stopped || e is OperationCanceledException && deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return new ImportResult(false, [], [], [], Translator.T(TooBig));
            }
        });
    }

    /// <summary>The workbook's sheets that CMX knows, in import order (the first sheet of each kind).</summary>
    private static List<(SheetDef Def, SpreadsheetSheet Sheet)> Match(SpreadsheetBook book)
    {
        var found = new Dictionary<string, SpreadsheetSheet>();
        foreach (var s in book.Sheets)
            if (ImportSheets.Find(s.Name) is { } def && !found.ContainsKey(def.Key)) found[def.Key] = s;
        return ImportSheets.All.Where(d => found.ContainsKey(d.Key)).Select(d => (d, found[d.Key])).ToList();
    }

    private static bool IsBlank(IReadOnlyList<object?> row) => row.All(v => v is null || v is string s && string.IsNullOrWhiteSpace(s));

    private static async Task<ImportResult> RunSheetsAsync(CmmsDbContext db, ITenantContext ctx, List<(SheetDef Def, SpreadsheetSheet Sheet)> plan,
        Func<TimeSpan> clock, TimeSpan budget, CancellationToken ct, Func<string, int, int, Task>? progress, bool save)
    {
        void Check() { if (clock() > budget) throw new Stopped(); }
        Check();
        var c = await ContextAsync(db, ctx, ct);
        c.Saving = save;
        var customFields = c.AssetFields;
        var importers = SheetImporters.All(customFields).ToDictionary(i => i.Sheet);
        var results = new List<SheetResult>();

        foreach (var (baseDef, sheet) in plan)
        {
            Check();
            var def = baseDef.Key == "Assets" ? SheetImporters.AssetSheet(customFields) : baseDef;
            if (!importers.TryGetValue(def.Key, out var importer)) continue;
            var (rows, ignored) = ReadRows(def, sheet, importer);
            await importer.LoadAsync(c, rows, ct);
            var outcomes = new List<RowResult>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (progress is not null) await progress(def.Key, 0, rows.Count);
            foreach (var row in importer.Order(rows, c))
            {
                Check();
                if (progress is not null && outcomes.Count > 0 && outcomes.Count % 100 == 0) await progress(def.Key, outcomes.Count, rows.Count);
                var key = row.HasProblems ? "" : importer.KeyOf(row);
                if (!row.HasProblems && key.Length > 0)
                {
                    if (seen.TryGetValue(key, out var first)) row.Problem(Translator.T($"{Shown(key)} appears twice in this sheet (row {first})."));
                    else seen[key] = row.Number;
                }
                var result = row.HasProblems ? Problem(row, key) : await importer.ImportAsync(c, row, ct);
                outcomes.Add(result);
            }
            if (progress is not null) await progress(def.Key, rows.Count, rows.Count);
            outcomes = [.. importer.Review(c, rows, outcomes)];
            Check();
            await importer.FinishAsync(c, ct);
            Check();
            await db.SaveChangesAsync(ct);
            Check();
            outcomes = [.. outcomes.OrderBy(r => r.Row)];
            results.Add(new SheetResult(def.Key,
                outcomes.Count(r => r.Outcome == RowOutcome.New), outcomes.Count(r => r.Outcome == RowOutcome.Changed),
                outcomes.Count(r => r.Outcome == RowOutcome.Unchanged), outcomes.Count(r => r.Outcome == RowOutcome.Problem),
                outcomes.Where(r => r.Outcome != RowOutcome.Unchanged).ToList(), ignored));
        }
        return new ImportResult(false, results, c.NewLookups, c.Notes, null);
    }

    /// <summary>A key as people read it: the last part of a compound key ("site|code" → "code").</summary>
    private static string Shown(string key) => key.Contains('|') ? key[(key.LastIndexOf('|') + 1)..] : key;

    internal static RowResult Problem(ImportRow row, string key) => new(row.Number, Shown(key), RowOutcome.Problem, [], row.Problems);

    /// <summary>Maps the heading row to columns and reads each non-blank data row's cells.</summary>
    private static (List<ImportRow> Rows, List<string> Ignored) ReadRows(SheetDef def, SpreadsheetSheet sheet, ISheetImporter importer)
    {
        var headings = sheet.Rows.Count > 0 ? sheet.Rows[0] : [];
        var columns = new ColumnDef?[headings.Count];
        var ignored = new List<string>();
        var taken = new HashSet<string>();
        for (var i = 0; i < headings.Count; i++)
        {
            var text = headings[i] switch { null => null, string s => s, var v => ImportValues.Show(v) };
            if (string.IsNullOrWhiteSpace(text)) continue;
            var col = def.FindColumn(text);
            if (col is null || !taken.Add(col.Key)) { ignored.Add(text.Trim()); continue; }
            columns[i] = col;
        }
        var missingKeys = importer.KeyColumns.Where(k => !taken.Contains(k)).Select(k => def.Column(k).Heading).ToList();

        var rows = new List<ImportRow>();
        for (var r = 1; r < sheet.Rows.Count; r++)
        {
            var cells = sheet.Rows[r];
            if (IsBlank(cells)) continue;
            var values = new Dictionary<string, object?>();
            var raw = new Dictionary<string, string?>();
            var problems = new List<string>();
            for (var i = 0; i < columns.Length && i < cells.Count; i++)
            {
                if (columns[i] is not { } col) continue;
                raw[col.Key] = cells[i] is null ? null : ImportValues.Show(cells[i]);
                if (ImportValues.TryRead(cells[i], col, out var value, out var problem)) values[col.Key] = value;
                else problems.Add(problem!);
            }
            var row = new ImportRow(r + 1, def, values, raw);
            foreach (var p in problems) row.Problem(p);
            foreach (var m in missingKeys) row.Problem(Translator.T($"{m} column is missing."));
            foreach (var k in importer.KeyColumns.Where(taken.Contains).Where(k => !row.Has(k)))
                row.Problem(Translator.T($"{def.Column(k).Heading} is required."));
            rows.Add(row);
        }
        return (rows, ignored);
    }

    /// <summary>A run's context: the organization's settings, custom fields and every code it has.</summary>
    internal static async Task<ImportContext> ContextAsync(CmmsDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
        var c = new ImportContext(db, ctx, tenant.Settings, DateOnly.FromDateTime(DateTime.UtcNow));
        await LoadCodesAsync(c, ct);
        c.AssetFields = await db.CustomFields.AsNoTracking().Where(f => f.EntityType == "Asset").OrderBy(f => f.SortOrder).ToListAsync(ct);
        return c;
    }

    /// <summary>Every code in the organization, by kind, for references between sheets.</summary>
    private static async Task LoadCodesAsync(ImportContext c, CancellationToken ct)
    {
        var db = c.Db;
        foreach (var x in await db.Sites.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Sites, x.Code, x.Id);
        foreach (var x in await db.CostCentres.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.CostCentres, x.Code, x.Id);
        foreach (var x in await db.AssetGroups.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.AssetGroups, x.Code, x.Id);
        foreach (var x in await db.Locations.AsNoTracking().Select(x => new { x.Id, x.SiteId, x.ParentId, x.Code }).ToListAsync(ct))
        {
            c.Locations[ImportContext.LocationKey(x.SiteId, x.Code)] = x.Id;
            c.CodeOf[x.Id] = x.Code;
            c.LocationTree[x.Id] = (x.SiteId, x.ParentId);
        }
        foreach (var x in await db.AssetCategories.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Categories, x.Code, x.Id);
        foreach (var x in await db.Trades.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Trades, x.Code, x.Id);
        foreach (var x in await db.PartGroups.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.PartGroups, x.Code, x.Id);
        foreach (var x in await db.Suppliers.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Suppliers, x.Code, x.Id);
        foreach (var x in await db.Stores.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Stores, x.Code, x.Id);
        foreach (var x in await db.Assets.AsNoTracking().Select(x => new { x.Id, x.AssetNumber, x.ParentAssetId, x.SiteId }).ToListAsync(ct))
        {
            c.Remember(c.Assets, x.AssetNumber, x.Id);
            c.AssetParents[x.Id] = x.ParentAssetId;
            c.AssetSites[x.Id] = x.SiteId;
        }
        // People with limited access: numbers of assets they can't see are taken too (asset numbers are unique across the organization).
        if (db.CurrentSiteIds is not null)
            foreach (var n in await db.Assets.AllSites().AsNoTracking().Select(x => x.AssetNumber).ToListAsync(ct))
                if (!c.Assets.ContainsKey(n)) c.HiddenAssetNumbers.Add(n);
        foreach (var x in await db.Employees.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.Employees, x.Code, x.Id);
        foreach (var x in await db.Parts.AsNoTracking().Select(x => new { x.Id, x.PartNumber }).ToListAsync(ct)) c.Remember(c.Parts, x.PartNumber, x.Id);
        foreach (var x in await db.PmTasks.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.PmTasks, x.Code, x.Id);
        foreach (var x in await db.MeasurementPoints.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)) c.Remember(c.MeasurementPoints, x.Code, x.Id);
    }
}
