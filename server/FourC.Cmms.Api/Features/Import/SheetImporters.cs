using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>The importer for each sheet, and the run's sheet definitions that depend on the organization (Assets' custom fields).</summary>
internal static class SheetImporters
{
    public static IEnumerable<ISheetImporter> All(IReadOnlyList<CustomFieldDefinition> customFields) =>
    [
        new SiteImporter(), new LocationImporter(), new CategoryImporter(), new TradeImporter(), new PartGroupImporter(),
        new SymptomImporter(), new CauseImporter(), new RemedyImporter(),
        new SupplierImporter(), new StoreImporter(), new AssetImporter(), new PeopleImporter(), new PartImporter(), new StockImporter(),
        new SupplierCatalogueImporter(), new AssetPartImporter(), new PmTaskImporter(), new MeasurementPointImporter(), new PmStepImporter(), new PmPartImporter(), new PmScheduleImporter(),
    ];

    /// <summary>The Assets sheet with one more column per custom field, headed by the field's label.</summary>
    public static SheetDef AssetSheet(IReadOnlyList<CustomFieldDefinition> customFields) =>
        ImportSheets.All.First(s => s.Key == "Assets").With(customFields.Select(f => new ColumnDef("cf:" + f.Key, f.Label, f.Label, ValueKind.Text)));
}

/// <summary>
/// The common part of every record importer. A record is seen through its sheet's columns (a dictionary of column key →
/// value, references as codes), so a row can be laid over it (blank cells keep the current value) and compared with it.
/// </summary>
internal abstract class RecordImporter<TEntity> : ISheetImporter where TEntity : class
{
    public abstract string Sheet { get; }
    public abstract IReadOnlyList<string> KeyColumns { get; }
    public virtual string KeyOf(ImportRow row) => string.Join('|', KeyColumns.Select(k => row.Get<string>(k)?.Trim() ?? ""));

    /// <summary>The records the sheet's keys refer to, tracked, by key.</summary>
    protected Dictionary<string, TEntity> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys taken by records in sites the person can't see (codes unique across the organization): those rows are problems.</summary>
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Notes keys found past the person's site limits that aren't among the visible records.</summary>
    protected void Hide(IEnumerable<string> keys) { foreach (var k in keys) if (!Existing.ContainsKey(k)) _hidden.Add(k); }

    public abstract Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct);

    /// <summary>The order to take the rows in; the sheet's own by default.</summary>
    public virtual IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) => rows;

    /// <summary>Work left after the last row (posting opening stock, replacing task lists…).</summary>
    public virtual Task FinishAsync(ImportContext c, CancellationToken ct) => Task.CompletedTask;

    /// <summary>A stored record's values, by column key.</summary>
    protected abstract Dictionary<string, object?> Values(ImportContext c, TEntity e);

    /// <summary>The same values, for "Download current data" (so an export imports back unchanged).</summary>
    internal Dictionary<string, object?> Export(ImportContext c, TEntity e) => Values(c, e);

    /// <summary>A new record's values before the row is laid over them (the screen's defaults).</summary>
    protected virtual Dictionary<string, object?> Defaults() => new() { ["active"] = true };

    /// <summary>
    /// Checks the values (adding any problem to the row, and changing nothing then) and saves them: a new record when
    /// <paramref name="existing"/> is null (returned), else an update.
    /// </summary>
    protected abstract Task<TEntity?> SaveAsync(ImportContext c, ImportRow row, TEntity? existing, Dictionary<string, object?> values, CancellationToken ct);

    /// <summary>Makes a new record's code known to later rows and sheets.</summary>
    protected virtual void Remember(ImportContext c, TEntity created, Dictionary<string, object?> values) { }

    public virtual async Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var key = KeyOf(row);
        Existing.TryGetValue(key, out var existing);
        if (existing is null && _hidden.Contains(key))
        {
            row.Problem(Translator.T($"{Shown(row)} is used in a site you can't see."));
            return new RowResult(row.Number, Shown(row), RowOutcome.Problem, [], row.Problems);
        }
        var current = existing is null ? Defaults() : Values(c, existing);
        var next = new Dictionary<string, object?>(current);
        foreach (var k in row.Keys.Where(row.Has)) next[k] = row.Value(k);
        if (existing is not null) foreach (var k in KeyColumns) next[k] = current.GetValueOrDefault(k); // the stored code stays as it is
        var changes = existing is null ? [] : Changes(row.Sheet, current, next);
        var shown = Shown(row);
        if (existing is not null && changes.Count == 0) return new RowResult(row.Number, shown, RowOutcome.Unchanged, [], []);

        var created = await SaveAsync(c, row, existing, next, ct);
        if (row.HasProblems) return new RowResult(row.Number, shown, RowOutcome.Problem, [], row.Problems);
        if (existing is not null) return new RowResult(row.Number, shown, RowOutcome.Changed, changes, []);
        // A new row is either saved or a problem; never "New" with nothing behind it.
        if (created is null) throw new InvalidOperationException($"{Sheet} row {row.Number} was neither saved nor given a problem.");
        if (key.Trim('|').Length > 0) Existing[key] = created!; // a blank key (an asset to be numbered) is always new
        Remember(c, created!, next);
        return new RowResult(row.Number, shown, RowOutcome.New, [], []);
    }

    /// <summary>How the row's key reads in the results: its last key column (the code).</summary>
    protected virtual string Shown(ImportRow row) => row.Get<string>(KeyColumns[^1]) ?? "";

    /// <summary>The columns whose shown value differs, in the sheet's order. Lists compare as sets.</summary>
    private static List<FieldChange> Changes(SheetDef sheet, Dictionary<string, object?> before, Dictionary<string, object?> after) =>
        sheet.Columns.Where(col => after.ContainsKey(col.Key) || before.ContainsKey(col.Key))
            .Select(col => (col, Old: Show(before.GetValueOrDefault(col.Key)), New: Show(after.GetValueOrDefault(col.Key))))
            .Where(x => x.Old != x.New)
            .Select(x => new FieldChange(x.col.Heading, x.Old, x.New)).ToList();

    private static string? Show(object? value) => value switch
    {
        string s => s.Replace("\r\n", "\n").Clean(), // a workbook brings line breaks back as \n; stored text may hold \r\n
        IEnumerable<string> list => ImportValues.Show(list.Select(x => x.Trim()).Where(x => x.Length > 0).Order(StringComparer.OrdinalIgnoreCase).ToList()).Clean(),
        IEnumerable<Enum> choices => ImportValues.Show(choices),
        _ => ImportValues.Show(value),
    };

    // ---- reading values ----
    protected static string? S(Dictionary<string, object?> v, string key) => (v.GetValueOrDefault(key) as string).Clean();
    protected static decimal? N(Dictionary<string, object?> v, string key) => v.GetValueOrDefault(key) as decimal?;
    protected static int? I(Dictionary<string, object?> v, string key) => v.GetValueOrDefault(key) as int?;
    protected static DateOnly? D(Dictionary<string, object?> v, string key) => v.GetValueOrDefault(key) as DateOnly?;
    protected static bool B(Dictionary<string, object?> v, string key, bool otherwise = false) => v.GetValueOrDefault(key) as bool? ?? otherwise;
    protected static T E<T>(Dictionary<string, object?> v, string key, T otherwise) where T : struct, Enum => v.GetValueOrDefault(key) is T t ? t : otherwise;
    protected static List<string> L(Dictionary<string, object?> v, string key) => v.GetValueOrDefault(key) as List<string> ?? [];

    /// <summary>The id behind a code, or null with "Site XX not found." on the row (<paramref name="kind"/> is "Site", "Store"…).</summary>
    protected static Guid? Ref(ImportRow row, Dictionary<string, Guid> map, string? code, string kind)
    {
        if (code is null) return null;
        if (map.TryGetValue(code.Trim(), out var id)) return id;
        row.Problem(Translator.T($"{kind} {code.Trim()} not found."));
        return null;
    }

    /// <summary>Adds the validator's messages to the row; true when there were none.</summary>
    protected static bool Accept(ImportRow row, Validator v)
    {
        foreach (var m in v.Messages) row.Problem(Translator.T(m));
        return !row.HasProblems;
    }

    protected static async Task<Dictionary<string, TEntity>> ByKey(IQueryable<TEntity> query, Func<TEntity, string> key, CancellationToken ct) =>
        (await query.ToListAsync(ct)).GroupBy(key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    protected void Keep(Dictionary<string, TEntity> found) { foreach (var (k, e) in found) Existing[k] = e; }

    /// <summary>
    /// Rows whose parent is another row of the sheet come after that row. Rows in a loop get "{code} can't be under itself."
    /// </summary>
    protected static IReadOnlyList<ImportRow> ParentsFirst(IReadOnlyList<ImportRow> rows, Func<ImportRow, string> keyOf, Func<ImportRow, string?> parentKeyOf, string codeColumn)
    {
        var byKey = new Dictionary<string, ImportRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Where(r => !r.HasProblems)) if (keyOf(r).Trim('|').Length > 0) byKey.TryAdd(keyOf(r), r);
        var ordered = new List<ImportRow>();
        var state = new Dictionary<ImportRow, int>(); // 1 = visiting, 2 = done
        void Visit(ImportRow r, List<ImportRow> path)
        {
            if (state.GetValueOrDefault(r) == 2) return;
            if (state.GetValueOrDefault(r) == 1)
            {
                foreach (var x in path.SkipWhile(x => x != r)) x.Problem(Translator.T($"{x.Get<string>(codeColumn)} can't be under itself."));
                return;
            }
            state[r] = 1;
            path.Add(r);
            if (parentKeyOf(r) is { } pk && byKey.TryGetValue(pk, out var parent) && parent != r) Visit(parent, path);
            path.RemoveAt(path.Count - 1);
            state[r] = 2;
            ordered.Add(r);
        }
        foreach (var r in rows) if (!r.HasProblems) Visit(r, []);
        var placed = ordered.ToHashSet();
        return [.. ordered, .. rows.Where(r => !placed.Contains(r))];
    }

    protected static List<string> Codes(IReadOnlyList<ImportRow> rows, string key) =>
        rows.Select(r => r.Get<string>(key)?.Trim()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

// ---------------- Sites, locations ----------------

internal sealed class SiteImporter : RecordImporter<Site>
{
    public override string Sheet => "Sites";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.Sites.Where(s => codes.Contains(s.Code)), s => s.Code, ct));
        if (c.Db.CurrentSiteIds is not null) Hide(await c.Db.Sites.AllSites().Where(s => codes.Contains(s.Code)).Select(s => s.Code).ToListAsync(ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Site s) => new()
    {
        ["code"] = s.Code, ["name"] = s.Name, ["address"] = s.Address, ["city"] = s.City, ["country"] = s.Country, ["timeZone"] = s.TimeZone, ["active"] = s.IsActive,
    };

    protected override Task<Site?> SaveAsync(ImportContext c, ImportRow row, Site? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new SiteEndpoints.SiteInput(S(v, "code")!, S(v, "name") ?? "", S(v, "address"), S(v, "city"), S(v, "country"), S(v, "timeZone"),
            existing?.Latitude, existing?.Longitude, existing?.Notes, B(v, "active", true));
        var rules = SiteEndpoints.SiteRules(input);
        if (row.Has("timeZone") && !TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZone!, out _))
            rules.Add("timeZone", $"{input.TimeZone} isn't a time zone CMX knows (for example Africa/Cairo).");
        if (existing is null && c.Db.CurrentSiteIds is not null) rules.Add("code", SiteEndpoints.OnlyEveryoneAddsSites);
        if (existing is not null && c.Db.CurrentBranchPaths is not null) rules.Add("code", SiteEndpoints.OnlyWholeSiteChangesIt);
        if (!Accept(row, rules)) return Task.FromResult<Site?>(null);
        if (existing is not null) { SiteEndpoints.UpdateSite(existing, input); return Task.FromResult<Site?>(null); }
        return Task.FromResult<Site?>(SiteEndpoints.CreateSite(c.Db, input));
    }

    protected override void Remember(ImportContext c, Site s, Dictionary<string, object?> v) => c.Remember(c.Sites, s.Code, s.Id);
}

internal sealed class LocationImporter : RecordImporter<Location>
{
    public override string Sheet => "Locations";
    public override IReadOnlyList<string> KeyColumns => ["siteCode", "code"];

    /// <summary>Parents before their children (when both are in the sheet); a loop makes its rows problems.</summary>
    public override IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) => ParentsFirst(rows, KeyOf,
        r => r.Get<string>("parentCode") is { } p ? $"{r.Get<string>("siteCode")?.Trim()}|{p.Trim()}" : null, "code");

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        var siteIds = Codes(rows, "siteCode").Select(s => c.Sites.TryGetValue(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var found = await c.Db.Locations.Where(l => siteIds.Contains(l.SiteId) && codes.Contains(l.Code)).ToListAsync(ct);
        foreach (var l in found) Existing[$"{c.CodeOf.GetValueOrDefault(l.SiteId)}|{l.Code}"] = l;
        if (c.Db.CurrentBranchPaths is not null) // other branches of the same site
            Hide((await c.Db.Locations.AllSites().Where(l => siteIds.Contains(l.SiteId) && codes.Contains(l.Code)).Select(l => new { l.SiteId, l.Code }).ToListAsync(ct))
                .Select(l => $"{c.CodeOf.GetValueOrDefault(l.SiteId)}|{l.Code}"));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Location l) => new()
    {
        ["siteCode"] = c.CodeOf.GetValueOrDefault(l.SiteId), ["code"] = l.Code, ["name"] = l.Name,
        ["parentCode"] = l.ParentId is { } p ? c.CodeOf.GetValueOrDefault(p) : null, ["type"] = l.Type, ["criticality"] = l.Criticality,
        ["description"] = l.Description, ["notesToTechnician"] = l.NotesToTechnician, ["active"] = l.IsActive,
    };

    protected override Dictionary<string, object?> Defaults() => new() { ["type"] = LocationType.Space, ["criticality"] = Criticality.Medium, ["active"] = true };

    protected override async Task<Location?> SaveAsync(ImportContext c, ImportRow row, Location? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var siteId = Ref(row, c.Sites, S(v, "siteCode"), "Site");
        Guid? parentId = null;
        if (siteId is { } site && S(v, "parentCode") is { } parentCode)
        {
            if (c.Locations.TryGetValue(ImportContext.LocationKey(site, parentCode), out var pid)) parentId = pid;
            else if (c.Locations.Keys.Any(k => k.EndsWith("|" + parentCode, StringComparison.OrdinalIgnoreCase)))
                row.Problem(Translator.T("Parent location must belong to the selected site."));
            else row.Problem(Translator.T($"Location {parentCode} not found."));
        }
        // A parent above the person's branch (not in their data, so blank in their file) stays.
        if (parentId is null && existing?.ParentId is { } kept && S(v, "parentCode") is null && !c.CodeOf.ContainsKey(kept)) parentId = kept;
        if (c.Db.CurrentBranchPaths is not null && parentId is null) row.Problem(Translator.T("Choose a parent place in your part of the site."));
        if (existing is not null && parentId is { } start)
        {
            // Walk up from the new parent: meeting this location means it would sit under itself.
            Guid? cursor = start;
            for (var depth = 0; cursor is { } at && depth < 50; depth++)
            {
                if (at == existing.Id) { row.Problem(Translator.T($"{existing.Code} can't be under itself.")); break; }
                cursor = c.LocationTree.TryGetValue(at, out var node) ? node.ParentId : null;
            }
        }
        if (row.HasProblems || siteId is null) return null;
        var input = new LocationEndpoints.LocationInput(siteId.Value, parentId, S(v, "code")!, S(v, "name") ?? "", E(v, "type", LocationType.Space),
            E(v, "criticality", Criticality.Medium), S(v, "description"), S(v, "notesToTechnician"), existing?.Latitude, existing?.Longitude, B(v, "active", true));
        if (!Accept(row, LocationEndpoints.LocationRules(input, existing?.Id))) return null;
        if (existing is not null)
        {
            var (oldPath, oldDepth) = (existing.Path, existing.Depth);
            await LocationEndpoints.UpdateLocationAsync(c.Db, existing, input, ct);
            c.LocationTree[existing.Id] = (existing.SiteId, existing.ParentId);
            if (existing.Path != oldPath) Repath(c, existing, oldPath, existing.Depth - oldDepth);
            return null;
        }
        return await LocationEndpoints.CreateLocationAsync(c.Db, input, ct);
    }

    /// <summary>
    /// A move rewrote the paths below the location in the database; the locations this run holds in memory get the same new
    /// paths (without being written again), so rows further down the sheet build on the right ones.
    /// </summary>
    private static void Repath(ImportContext c, Location moved, string oldPath, int depthDelta)
    {
        foreach (var entry in c.Db.ChangeTracker.Entries<Location>().Where(e => e.Entity != moved && e.Entity.Path.StartsWith(oldPath, StringComparison.Ordinal)).ToList())
        {
            var path = moved.Path + entry.Entity.Path[oldPath.Length..];
            var depth = entry.Entity.Depth + depthDelta;
            entry.Property(x => x.Path).CurrentValue = path;
            entry.Property(x => x.Depth).CurrentValue = depth;
            if (entry.State != EntityState.Added)
            {
                // Already right in the database (and its version stamp moved on): not a change to write.
                entry.Property(x => x.Path).OriginalValue = path;
                entry.Property(x => x.Depth).OriginalValue = depth;
                entry.Property(x => x.Path).IsModified = false;
                entry.Property(x => x.Depth).IsModified = false;
            }
        }
    }

    protected override void Remember(ImportContext c, Location l, Dictionary<string, object?> v)
    {
        c.Locations[ImportContext.LocationKey(l.SiteId, l.Code)] = l.Id;
        c.CodeOf[l.Id] = l.Code;
        c.LocationTree[l.Id] = (l.SiteId, l.ParentId);
    }
}

// ---------------- Lookups ----------------

internal sealed class CategoryImporter : RecordImporter<AssetCategory>
{
    public override string Sheet => "Categories";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.AssetCategories.Where(x => codes.Contains(x.Code)), x => x.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, AssetCategory x) => new()
    { ["code"] = x.Code, ["name"] = x.Name, ["color"] = x.Color, ["downtimeCostPerHour"] = x.DowntimeCostPerHour, ["active"] = x.IsActive };

    protected override Task<AssetCategory?> SaveAsync(ImportContext c, ImportRow row, AssetCategory? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new AssetCategoryEndpoints.CategoryInput(S(v, "code")!, S(v, "name") ?? "", existing?.Description, S(v, "color"), B(v, "active", true), N(v, "downtimeCostPerHour"));
        if (!Accept(row, AssetCategoryEndpoints.CategoryRules(input))) return Task.FromResult<AssetCategory?>(null);
        if (existing is not null) { AssetCategoryEndpoints.UpdateCategory(existing, input); return Task.FromResult<AssetCategory?>(null); }
        return Task.FromResult<AssetCategory?>(AssetCategoryEndpoints.CreateCategory(c.Db, input));
    }

    protected override void Remember(ImportContext c, AssetCategory x, Dictionary<string, object?> v) => c.Remember(c.Categories, x.Code, x.Id);
}

internal sealed class TradeImporter : RecordImporter<Trade>
{
    public override string Sheet => "Trades";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.Trades.Where(x => codes.Contains(x.Code)), x => x.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Trade x) => new()
    { ["code"] = x.Code, ["name"] = x.Name, ["color"] = x.Color, ["active"] = x.IsActive };

    protected override Task<Trade?> SaveAsync(ImportContext c, ImportRow row, Trade? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new PeopleEndpoints.LookupInput(S(v, "code")!, S(v, "name") ?? "", S(v, "color"), B(v, "active", true));
        if (!Accept(row, PeopleEndpoints.LookupRules(input))) return Task.FromResult<Trade?>(null);
        var item = existing ?? new Trade { Code = "", Name = "" };
        PeopleEndpoints.ApplyLookup(item, input, (t, color) => t.Color = color);
        if (existing is not null) return Task.FromResult<Trade?>(null);
        c.Db.Trades.Add(item);
        return Task.FromResult<Trade?>(item);
    }

    protected override void Remember(ImportContext c, Trade x, Dictionary<string, object?> v) => c.Remember(c.Trades, x.Code, x.Id);
}

/// <summary>A code list sheet (0.14.0 symptoms, causes, remedies): code, name, active; added and updated by code.</summary>
internal abstract class CodeImporter<T> : RecordImporter<T> where T : TenantEntity, ICodeLookup
{
    protected abstract DbSet<T> Set(ImportContext c);
    protected abstract Dictionary<string, Guid> Map(ImportContext c);
    protected abstract T Create();
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(Set(c).Where(x => codes.Contains(x.Code)), x => x.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, T x) => new() { ["code"] = x.Code, ["name"] = x.Name, ["active"] = x.IsActive };

    protected override Task<T?> SaveAsync(ImportContext c, ImportRow row, T? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new PeopleEndpoints.LookupInput(S(v, "code")!, S(v, "name") ?? "", null, B(v, "active", true));
        if (!Accept(row, PeopleEndpoints.LookupRules(input))) return Task.FromResult<T?>(null);
        var item = existing ?? Create();
        PeopleEndpoints.ApplyLookup(item, input, null);
        if (existing is not null) return Task.FromResult<T?>(null);
        Set(c).Add(item);
        return Task.FromResult<T?>(item);
    }

    protected override void Remember(ImportContext c, T x, Dictionary<string, object?> v) => c.Remember(Map(c), x.Code, x.Id);
}

internal sealed class SymptomImporter : CodeImporter<Symptom>
{
    public override string Sheet => "Symptoms";
    protected override DbSet<Symptom> Set(ImportContext c) => c.Db.Symptoms;
    protected override Dictionary<string, Guid> Map(ImportContext c) => c.Symptoms;
    protected override Symptom Create() => new() { Code = "", Name = "" };
}

/// <summary>Causes are the failure codes.</summary>
internal sealed class CauseImporter : CodeImporter<FailureCode>
{
    public override string Sheet => "Causes";
    protected override DbSet<FailureCode> Set(ImportContext c) => c.Db.FailureCodes;
    protected override Dictionary<string, Guid> Map(ImportContext c) => c.Causes;
    protected override FailureCode Create() => new() { Code = "", Name = "" };
}

internal sealed class RemedyImporter : CodeImporter<Remedy>
{
    public override string Sheet => "Remedies";
    protected override DbSet<Remedy> Set(ImportContext c) => c.Db.Remedies;
    protected override Dictionary<string, Guid> Map(ImportContext c) => c.Remedies;
    protected override Remedy Create() => new() { Code = "", Name = "" };
}

internal sealed class PartGroupImporter : RecordImporter<PartGroup>
{
    public override string Sheet => "PartGroups";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.PartGroups.Where(x => codes.Contains(x.Code)), x => x.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, PartGroup x) => new() { ["code"] = x.Code, ["name"] = x.Name, ["active"] = x.IsActive };

    protected override Task<PartGroup?> SaveAsync(ImportContext c, ImportRow row, PartGroup? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new PeopleEndpoints.LookupInput(S(v, "code")!, S(v, "name") ?? "", null, B(v, "active", true));
        if (!Accept(row, PeopleEndpoints.LookupRules(input))) return Task.FromResult<PartGroup?>(null);
        var item = existing ?? new PartGroup { Code = "", Name = "" };
        PeopleEndpoints.ApplyLookup(item, input, null);
        if (existing is not null) return Task.FromResult<PartGroup?>(null);
        c.Db.PartGroups.Add(item);
        return Task.FromResult<PartGroup?>(item);
    }

    protected override void Remember(ImportContext c, PartGroup x, Dictionary<string, object?> v) => c.Remember(c.PartGroups, x.Code, x.Id);
}

// ---------------- Suppliers, stores ----------------

internal sealed class SupplierImporter : RecordImporter<Supplier>
{
    public override string Sheet => "Suppliers";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.Suppliers.Where(x => codes.Contains(x.Code)), x => x.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Supplier x) => new()
    {
        ["code"] = x.Code, ["name"] = x.Name, ["contact"] = x.ContactName, ["email"] = x.Email, ["phone"] = x.Phone, ["address"] = x.Address,
        ["city"] = x.City, ["country"] = x.Country, ["taxNumber"] = x.TaxNumber, ["paymentTerms"] = x.PaymentTerms, ["currency"] = x.Currency,
        ["contractor"] = x.IsContractor, ["notes"] = x.Notes, ["active"] = x.IsActive,
    };

    protected override Dictionary<string, object?> Defaults() => new() { ["contractor"] = false, ["active"] = true };

    protected override Task<Supplier?> SaveAsync(ImportContext c, ImportRow row, Supplier? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new PurchasingEndpoints.SupplierInput(S(v, "code")!, S(v, "name") ?? "", S(v, "contact"), S(v, "email"), S(v, "phone"), S(v, "address"),
            S(v, "city"), S(v, "country"), S(v, "taxNumber"), S(v, "paymentTerms"), S(v, "currency"), B(v, "contractor"), S(v, "notes"), B(v, "active", true));
        if (!Accept(row, PurchasingEndpoints.SupplierRules(input))) return Task.FromResult<Supplier?>(null);
        if (existing is not null) { PurchasingEndpoints.UpdateSupplier(existing, input); return Task.FromResult<Supplier?>(null); }
        return Task.FromResult<Supplier?>(PurchasingEndpoints.CreateSupplier(c.Db, input));
    }

    protected override void Remember(ImportContext c, Supplier x, Dictionary<string, object?> v) => c.Remember(c.Suppliers, x.Code, x.Id);
}

internal sealed class StoreImporter : RecordImporter<Store>
{
    public override string Sheet => "Stores";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.Stores.Where(x => codes.Contains(x.Code)), x => x.Code, ct));
        if (c.Db.CurrentSiteIds is not null) Hide(await c.Db.Stores.AllSites().Where(x => codes.Contains(x.Code)).Select(x => x.Code).ToListAsync(ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Store x) => new()
    {
        ["code"] = x.Code, ["name"] = x.Name, ["siteCode"] = c.CodeOf.GetValueOrDefault(x.SiteId), ["description"] = x.Description, ["active"] = x.IsActive,
    };

    protected override async Task<Store?> SaveAsync(ImportContext c, ImportRow row, Store? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var siteId = Ref(row, c.Sites, S(v, "siteCode"), "Site");
        if (siteId is null && S(v, "siteCode") is null) row.Problem(Translator.T("Choose a site."));
        if (row.HasProblems) return null;
        var input = new InventoryEndpoints.StoreInput(S(v, "code")!, S(v, "name") ?? "", siteId, S(v, "description"), B(v, "active", true));
        var rules = InventoryEndpoints.StoreRules(input);
        if (existing is not null && existing.SiteId != siteId && await c.Db.StockLevels.AnyAsync(l => l.StoreId == existing.Id && l.QuantityOnHand != 0, ct))
            rules.Add("siteId", "Move the stock out before changing the store's site.");
        if (!Accept(row, rules)) return null;
        if (existing is not null) { InventoryEndpoints.UpdateStore(existing, input); return null; }
        return InventoryEndpoints.CreateStore(c.Db, input);
    }

    protected override void Remember(ImportContext c, Store x, Dictionary<string, object?> v) => c.Remember(c.Stores, x.Code, x.Id);
}

// ---------------- Assets, people ----------------

internal sealed class AssetImporter : RecordImporter<Asset>
{
    public override string Sheet => "Assets";

    /// <summary>No key column is required: a blank asset number means a new asset, numbered automatically.</summary>
    public override IReadOnlyList<string> KeyColumns => [];
    public override string KeyOf(ImportRow row) => row.Get<string>("assetNumber")?.Trim() ?? "";
    protected override string Shown(ImportRow row) => row.Get<string>("assetNumber") ?? row.Get<string>("name") ?? "";

    public override IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) =>
        ParentsFirst(rows, KeyOf, r => r.Get<string>("parentAssetNumber")?.Trim(), "assetNumber");

    // Numbers for rows without one are taken from the organization's sequence in one block, not one round trip a row.
    private int _toNumber;
    private long _next, _end;

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var numbers = Codes(rows, "assetNumber");
        Keep(await ByKey(c.Db.Assets.Where(a => numbers.Contains(a.AssetNumber)), a => a.AssetNumber, ct));
        if (c.Db.CurrentSiteIds is not null) Hide(await c.Db.Assets.AllSites().Where(a => numbers.Contains(a.AssetNumber)).Select(a => a.AssetNumber).ToListAsync(ct));
        _toNumber = c.Settings.AutoNumberAssets ? rows.Count(r => !r.HasProblems && !r.Has("assetNumber")) : 0;
    }

    /// <summary>The next free number of the reserved block (taken on first use), skipping any already used.</summary>
    private async Task<string?> ReservedNumberAsync(ImportContext c, CancellationToken ct)
    {
        if (_next == _end && _toNumber > 0)
        {
            _next = await c.Db.NextNumbersAsync("Asset", _toNumber, ct);
            _end = _next + _toNumber;
            _toNumber = 0;
        }
        while (_next < _end)
        {
            var number = AssetEndpoints.FormatAssetNumber(c.Settings, _next++);
            if (!c.Assets.ContainsKey(number) && !c.HiddenAssetNumbers.Contains(number)) return number;
        }
        return null;
    }

    /// <summary>Numbers of the block that no row needed (rows that turned out to be problems) go back to the sequence.</summary>
    public override async Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        if (_next < _end) await c.Db.ReturnNumbersAsync("Asset", _next, _end, ct);
        _next = _end = 0;
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Asset a)
    {
        var v = new Dictionary<string, object?>
        {
            ["assetNumber"] = a.AssetNumber, ["name"] = a.Name, ["siteCode"] = c.CodeOf.GetValueOrDefault(a.SiteId),
            ["locationCode"] = a.LocationId is { } l ? c.CodeOf.GetValueOrDefault(l) : null,
            ["parentAssetNumber"] = a.ParentAssetId is { } p ? c.CodeOf.GetValueOrDefault(p) : null,
            ["categoryCode"] = a.CategoryId is { } cat ? c.CodeOf.GetValueOrDefault(cat) : null,
            ["status"] = a.Status, ["criticality"] = a.Criticality, ["manufacturer"] = a.Manufacturer, ["model"] = a.Model, ["serialNumber"] = a.SerialNumber,
            ["barcode"] = a.Barcode, ["operationNumber"] = a.OperationNumber, ["commissionedOn"] = a.CommissionedOn, ["disposedOn"] = a.DisposedOn,
            ["purchasePrice"] = a.PurchasePrice, ["currentValue"] = a.CurrentValue, ["salvageValue"] = a.SalvageValue, ["expectedLifeYears"] = a.ExpectedLifeYears,
            ["downtimeCostPerHour"] = a.DowntimeCostPerHour, ["warrantyType"] = a.WarrantyType, ["warrantyExpiresOn"] = a.WarrantyExpiresOn,
            ["warrantyNotes"] = a.WarrantyNotes, ["description"] = a.Description, ["notes"] = a.Notes, ["notesToTechnician"] = a.NotesToTechnician,
            ["runsHoursPerDay"] = a.RunsHoursPerDay, ["runsOn"] = a.RunsOn.Cast<Enum>().ToList(),
        };
        foreach (var f in c.AssetFields) v["cf:" + f.Key] = a.CustomFields.GetValueOrDefault(f.Key);
        return v;
    }

    protected override Dictionary<string, object?> Defaults() => new()
    { ["status"] = AssetStatus.Active, ["criticality"] = Criticality.Medium, ["warrantyType"] = WarrantyType.None };

    /// <summary>A custom-field cell as the screen stores it: Yes/No become true/false for Yes/No fields.</summary>
    private static string? Custom(CustomFieldDefinition f, string? text)
    {
        if (text is null || f.DataType != CustomFieldType.Boolean) return text;
        var low = text.Trim().ToLowerInvariant();
        return low is "yes" or "true" or "1" or "نعم" ? "true" : low is "no" or "false" or "0" or "لا" ? "false" : text;
    }

    public override Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        // Running hours compare as stored (2 decimals), so 8.333 loaded twice isn't a change the second time.
        if (row.Get<decimal?>("runsHoursPerDay") is { } hours) row.Set("runsHoursPerDay", Math.Round(hours, 2));
        // Yes/No custom fields compare as the screen stores them.
        foreach (var f in c.AssetFields.Where(f => f.DataType == CustomFieldType.Boolean && row.Has("cf:" + f.Key)))
            row.Set("cf:" + f.Key, Custom(f, row.Get<string>("cf:" + f.Key)));
        return base.ImportAsync(c, row, ct);
    }

    protected override async Task<Asset?> SaveAsync(ImportContext c, ImportRow row, Asset? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var siteCode = S(v, "siteCode");
        var siteId = Ref(row, c.Sites, siteCode, "Site");
        if (siteCode is null) row.Problem(Translator.T("Choose a site."));
        Guid? locationId = null;
        if (siteId is { } site && S(v, "locationCode") is { } locationCode)
        {
            if (c.Locations.TryGetValue(ImportContext.LocationKey(site, locationCode), out var lid)) locationId = lid;
            else row.Problem(Translator.T($"Location {locationCode} not found."));
        }
        if (c.Db.CurrentBranchPaths is not null && locationId is null && !row.HasProblems) row.Problem(Translator.T("Choose a location in your part of the site."));
        var parentId = Ref(row, c.Assets, S(v, "parentAssetNumber"), "Asset");
        if (existing is not null && parentId is { } start)
        {
            Guid? cursor = start;
            for (var depth = 0; cursor is { } at && depth < 50; depth++)
            {
                if (at == existing.Id) { row.Problem(Translator.T($"{existing.AssetNumber} can't be under itself.")); break; }
                cursor = c.AssetParents.GetValueOrDefault(at);
            }
        }
        var categoryCode = S(v, "categoryCode");
        Guid? categoryId = categoryCode is not null && c.Categories.TryGetValue(categoryCode, out var known) ? known : null;
        if (row.HasProblems || siteId is null) return null;

        var number = S(v, "assetNumber");
        var requireNumber = existing is not null || !(number is null && c.Settings.AutoNumberAssets);
        var custom = c.AssetFields.ToDictionary(f => f.Key, f => S(v, "cf:" + f.Key));
        var input = new AssetEndpoints.AssetInput(number, S(v, "name") ?? "", S(v, "description"), categoryId, siteId.Value, locationId, parentId,
            E(v, "status", AssetStatus.Active), E(v, "criticality", Criticality.Medium), S(v, "manufacturer"), S(v, "model"), S(v, "serialNumber"),
            S(v, "barcode"), S(v, "operationNumber"), D(v, "commissionedOn"), D(v, "disposedOn"), N(v, "purchasePrice"), N(v, "currentValue"),
            N(v, "salvageValue"), N(v, "expectedLifeYears"), N(v, "downtimeCostPerHour"), E(v, "warrantyType", WarrantyType.None),
            D(v, "warrantyExpiresOn"), S(v, "warrantyNotes"), S(v, "notes"), S(v, "notesToTechnician"), custom,
            RunsHoursPerDay: N(v, "runsHoursPerDay"), RunsOn: (v.GetValueOrDefault("runsOn") as IEnumerable<Enum>)?.Cast<DayOfWeek>().ToList());
        var rules = AssetEndpoints.AssetRules(input, existing?.Id, requireNumber);
        var values = AssetEndpoints.CustomFieldValues(rules, c.AssetFields, input);
        if (!Accept(row, rules)) return null;
        // A new category is added only once the row is known to be good.
        if (categoryCode is not null && categoryId is null) input = input with { CategoryId = c.EnsureLookup(c.Categories, categoryCode, "Categories") };

        if (existing is not null)
        {
            AssetEndpoints.UpdateAsset(c.Db, existing, input, values, c.Tenant);
            c.AssetParents[existing.Id] = existing.ParentAssetId;
            c.AssetSites[existing.Id] = existing.SiteId;
            return null;
        }
        if (input.AssetNumber is null && c.Settings.AutoNumberAssets && await ReservedNumberAsync(c, ct) is { } reserved)
            input = input with { AssetNumber = reserved };
        var asset = await AssetEndpoints.CreateAssetAsync(c.Db, input, values, c.Settings, ct);
        // A number given automatically may already be taken by a row of this sheet that isn't saved yet.
        for (var tries = 0; c.Assets.ContainsKey(asset.AssetNumber) && tries < 20; tries++)
            asset.AssetNumber = await AssetEndpoints.NextAssetNumberAsync(c.Db, c.Settings, ct);
        return asset;
    }

    protected override void Remember(ImportContext c, Asset a, Dictionary<string, object?> v)
    {
        c.Remember(c.Assets, a.AssetNumber, a.Id);
        c.AssetParents[a.Id] = a.ParentAssetId;
        c.AssetSites[a.Id] = a.SiteId;
    }
}

internal sealed class PeopleImporter : RecordImporter<Employee>
{
    public override string Sheet => "People";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.Employees.Where(e => codes.Contains(e.Code)), e => e.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, Employee e) => new()
    {
        ["code"] = e.Code, ["fullName"] = e.FullName, ["jobTitle"] = e.JobTitle, ["email"] = e.Email, ["phone"] = e.Phone,
        ["trades"] = e.TradeIds.Select(id => c.CodeOf.GetValueOrDefault(id)).OfType<string>().ToList(),
        ["sites"] = e.SiteIds.Select(id => c.CodeOf.GetValueOrDefault(id)).OfType<string>().ToList(),
        ["hourlyRate"] = e.HourlyRate, ["overtimeRate"] = e.OvertimeRate, ["contractor"] = e.IsContractor, ["company"] = e.Company,
        ["notes"] = e.Notes, ["active"] = e.IsActive,
    };

    protected override Dictionary<string, object?> Defaults() => new() { ["contractor"] = false, ["active"] = true };

    protected override Task<Employee?> SaveAsync(ImportContext c, ImportRow row, Employee? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var siteIds = L(v, "sites").Select(code => Ref(row, c.Sites, code, "Site")).OfType<Guid>().ToList();
        var tradeCodes = L(v, "trades");
        var tradeIds = tradeCodes.Where(c.Trades.ContainsKey).Select(code => c.Trades[code]).ToList();
        var input = new PeopleEndpoints.EmployeeInput(S(v, "code")!, S(v, "fullName") ?? "", S(v, "jobTitle"), S(v, "email"), S(v, "phone"), tradeIds, siteIds,
            N(v, "hourlyRate"), N(v, "overtimeRate"), B(v, "contractor"), S(v, "company"), existing?.UserId, S(v, "notes"), B(v, "active", true));
        if (row.HasProblems || !Accept(row, PeopleEndpoints.EmployeeRules(input))) return Task.FromResult<Employee?>(null);
        // New trades are added only once the row is known to be good.
        input = input with { TradeIds = tradeCodes.Select(code => c.EnsureLookup(c.Trades, code, "Trades")).Distinct().ToList() };
        if (existing is not null) { PeopleEndpoints.UpdateEmployee(existing, input); return Task.FromResult<Employee?>(null); }
        return Task.FromResult<Employee?>(PeopleEndpoints.CreateEmployee(c.Db, input));
    }

    protected override void Remember(ImportContext c, Employee e, Dictionary<string, object?> v) => c.Remember(c.Employees, e.Code, e.Id);
}

// ---------------- Parts, stock ----------------

internal sealed class PartImporter : RecordImporter<Part>
{
    public override string Sheet => "Parts";
    public override IReadOnlyList<string> KeyColumns => ["partNumber"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var numbers = Codes(rows, "partNumber");
        Keep(await ByKey(c.Db.Parts.Where(p => numbers.Contains(p.PartNumber)), p => p.PartNumber, ct));
        // The preferred suppliers' catalogue rows (0.18.0): the pairs there are and every company's currency, read once for the sheet.
        _pairs = [.. (await c.Db.SupplierParts.AsNoTracking().Where(r => numbers.Contains(r.Part!.PartNumber)).Select(r => new { r.PartId, r.SupplierId }).ToListAsync(ct))
            .Select(r => (r.PartId, r.SupplierId))];
        _currencies = await c.Db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => (string?)s.Currency, ct);
    }

    private HashSet<(Guid PartId, Guid SupplierId)> _pairs = [];
    private Dictionary<Guid, string?> _currencies = [];

    protected override Dictionary<string, object?> Values(ImportContext c, Part p) => new()
    {
        ["partNumber"] = p.PartNumber, ["name"] = p.Name, ["unit"] = p.UnitOfMeasure, ["groupCode"] = p.GroupId is { } g ? c.CodeOf.GetValueOrDefault(g) : null,
        ["specification"] = p.Specification, ["manufacturer"] = p.Manufacturer, ["manufacturerPartNumber"] = p.ManufacturerPartNumber,
        ["standardPrice"] = p.StandardPrice, ["leadTimeDays"] = p.LeadTimeDays, ["critical"] = p.IsCritical, ["shelfLife"] = p.IsShelfItem,
        ["allowNegative"] = p.AllowNegativeStock, ["preferredSupplierCode"] = p.PreferredSupplierId is { } s ? c.CodeOf.GetValueOrDefault(s) : null,
        ["barcode"] = p.Barcode, ["notes"] = p.Notes, ["active"] = p.IsActive,
    };

    protected override Dictionary<string, object?> Defaults() => new()
    { ["standardPrice"] = 0m, ["critical"] = false, ["shelfLife"] = false, ["allowNegative"] = false, ["active"] = true };

    protected override Task<Part?> SaveAsync(ImportContext c, ImportRow row, Part? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var supplierId = Ref(row, c.Suppliers, S(v, "preferredSupplierCode"), "Supplier");
        var groupCode = S(v, "groupCode");
        Guid? groupId = groupCode is not null && c.PartGroups.TryGetValue(groupCode, out var known) ? known : null;
        var input = new InventoryEndpoints.PartInput(S(v, "partNumber")!, S(v, "name") ?? "", S(v, "specification"), S(v, "manufacturer"),
            S(v, "manufacturerPartNumber"), S(v, "unit") ?? "", groupId, N(v, "standardPrice") ?? 0, I(v, "leadTimeDays"), B(v, "critical"), B(v, "shelfLife"),
            B(v, "allowNegative"), supplierId, S(v, "barcode"), existing?.AlternatePartIds, S(v, "notes"), B(v, "active", true));
        if (row.HasProblems || !Accept(row, InventoryEndpoints.PartRules(input))) return Task.FromResult<Part?>(null);
        // A new group is added only once the row is known to be good.
        if (groupCode is not null && groupId is null) input = input with { GroupId = c.EnsureLookup(c.PartGroups, groupCode, "PartGroups") };
        var part = existing ?? InventoryEndpoints.CreatePart(c.Db, input);
        if (existing is not null) InventoryEndpoints.UpdatePart(existing, input);
        return Task.FromResult(WithCatalogue(c, part, existing is null ? part : null));
    }

    /// <summary>The preferred supplier always has a catalogue row (0.18.0); a company added earlier in this run is found among the new ones.</summary>
    private Part? WithCatalogue(ImportContext c, Part part, Part? created)
    {
        if (part.PreferredSupplierId is { } preferred && _pairs.Add((part.Id, preferred)))
        {
            if (!_currencies.TryGetValue(preferred, out var currency)) currency = c.Db.Suppliers.Local.FirstOrDefault(s => s.Id == preferred)?.Currency;
            c.Db.SupplierParts.Add(CatalogueEndpoints.PreferredRow(part, preferred, currency, c.Settings.Currency));
        }
        return created;
    }

    protected override void Remember(ImportContext c, Part p, Dictionary<string, object?> v) => c.Remember(c.Parts, p.PartNumber, p.Id);
}

/// <summary>
/// Stock levels by part and store. The opening quantity goes in only for stock that is new in this run (posted to the
/// ledger as an opening balance at the part's average cost), so an old export can't move quantities back.
/// </summary>
internal sealed class StockImporter : RecordImporter<StockLevel>
{
    public const string OpeningNote = "Opening balance (import)";
    public const string IgnoredNote = "Opening quantity ignored for existing stock: use a stock take to change quantities.";

    private readonly List<(Guid PartId, Guid StoreId, StockLevel Level, decimal Quantity, string? Batch, DateOnly? Expiry)> _opening = [];
    private Dictionary<Guid, Part> _parts = [];
    private Dictionary<Guid, Store> _stores = [];

    public override string Sheet => "Stock";
    public override IReadOnlyList<string> KeyColumns => ["partNumber", "storeCode"];
    protected override string Shown(ImportRow row) => $"{row.Get<string>("partNumber")} · {row.Get<string>("storeCode")}";

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var partIds = Codes(rows, "partNumber").Select(n => c.Parts.GetValueOrDefault(n)).Where(id => id != Guid.Empty).ToList();
        var storeIds = Codes(rows, "storeCode").Select(n => c.Stores.GetValueOrDefault(n)).Where(id => id != Guid.Empty).ToList();
        foreach (var l in await c.Db.StockLevels.Where(l => partIds.Contains(l.PartId) && storeIds.Contains(l.StoreId)).ToListAsync(ct))
            Existing[$"{c.CodeOf.GetValueOrDefault(l.PartId)}|{c.CodeOf.GetValueOrDefault(l.StoreId)}"] = l;
        // Loaded once for the shelf-life, store and cost checks, not once a row.
        _parts = await c.Db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        _stores = await c.Db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
    }

    protected override Dictionary<string, object?> Values(ImportContext c, StockLevel l) => new()
    {
        ["partNumber"] = c.CodeOf.GetValueOrDefault(l.PartId), ["storeCode"] = c.CodeOf.GetValueOrDefault(l.StoreId), ["bin"] = l.Bin,
        ["min"] = l.MinLevel, ["reorderLevel"] = l.ReorderLevel, ["max"] = l.MaxLevel, ["reorderQuantity"] = l.ReorderQuantity,
    };

    protected override Dictionary<string, object?> Defaults() => new() { ["min"] = 0m, ["reorderLevel"] = 0m, ["max"] = 0m, ["reorderQuantity"] = 0m };

    public override Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        // On stock that exists, the opening quantity (and its batch) is set aside: quantities change through a stock take.
        if (Existing.ContainsKey(KeyOf(row)) && (row.Has("openingQuantity") || row.Has("batch") || row.Has("expiry")))
        {
            if (row.Has("openingQuantity")) c.Note(Translator.T(IgnoredNote));
            foreach (var k in new[] { "openingQuantity", "batch", "expiry" }) row.Set(k, null);
        }
        return base.ImportAsync(c, row, ct);
    }

    protected override async Task<StockLevel?> SaveAsync(ImportContext c, ImportRow row, StockLevel? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var partId = Ref(row, c.Parts, S(v, "partNumber"), "Part");
        var storeId = Ref(row, c.Stores, S(v, "storeCode"), "Store");
        if (row.HasProblems || partId is null || storeId is null) return null;
        var level = new InventoryEndpoints.LevelInput(storeId.Value, S(v, "bin"), N(v, "min") ?? 0, N(v, "reorderLevel") ?? 0, N(v, "max") ?? 0, N(v, "reorderQuantity") ?? 0);
        var rules = new Validator().MaxLength("bin", level.Bin, "Bin", 50);
        InventoryEndpoints.LevelRules(rules, [level]);
        var opening = existing is null ? N(v, "openingQuantity") : null;
        if (opening < 0) rules.Add("levels", "Levels cannot be negative.");
        if (opening > 0 && _parts.GetValueOrDefault(partId.Value) is { IsShelfItem: true } part && (S(v, "batch") is null || D(v, "expiry") is null))
            rules.Add("batch", $"{part.PartNumber} is a shelf-life item: enter the batch and expiry.");
        if (opening > 0 && _stores.GetValueOrDefault(storeId.Value) is { IsActive: false } store)
            rules.Add("storeId", $"Store {store.Code} is inactive.");
        if (!Accept(row, rules)) return null;

        var target = existing ?? new StockLevel { PartId = partId.Value, StoreId = storeId.Value };
        target.Bin = level.Bin.Clean(); target.MinLevel = level.MinLevel; target.ReorderLevel = level.ReorderLevel; target.MaxLevel = level.MaxLevel;
        target.ReorderQuantity = level.ReorderQuantity;
        if (existing is not null) return null;
        c.Db.StockLevels.Add(target);
        if (opening > 0) _opening.Add((partId.Value, storeId.Value, target, opening.Value, S(v, "batch"), D(v, "expiry")));
        return target;
    }

    public override Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        // All at once through the ledger: the levels are new in this run, so nobody else can be moving them.
        StockLedger.PostOpeningBalances(c.Db, c.Tenant, _opening.Select(o =>
            new OpeningBalance(_parts[o.PartId], _stores[o.StoreId], o.Level, o.Quantity, c.Today, o.Batch, o.Expiry, OpeningNote)));
        return Task.CompletedTask;
    }
}

// ---------------- PM ----------------

internal sealed class PmTaskImporter : RecordImporter<PmTask>
{
    public override string Sheet => "PmTasks";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.PmTasks.Include(t => t.Steps).Include(t => t.Parts).Where(t => codes.Contains(t.Code)), t => t.Code, ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, PmTask t) => new()
    {
        ["code"] = t.Code, ["name"] = t.Name, ["description"] = t.Description, ["tradeCode"] = t.TradeId is { } tr ? c.CodeOf.GetValueOrDefault(tr) : null,
        ["estimatedHours"] = t.EstimatedHours, ["safetyNotes"] = t.SafetyNotes, ["active"] = t.IsActive,
        ["title"] = t.Title, ["workType"] = t.WorkType, ["priority"] = t.Priority, ["expectedStopHours"] = t.ExpectedStopHours,
    };

    protected override Task<PmTask?> SaveAsync(ImportContext c, ImportRow row, PmTask? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var tradeCode = S(v, "tradeCode");
        Guid? tradeId = tradeCode is not null && c.Trades.TryGetValue(tradeCode, out var known) ? known : null;
        var steps = existing?.Steps.OrderBy(s => s.Sequence).Select(s => s.Description).ToList() ?? [];
        var input = new PmEndpoints.TaskInput(S(v, "code")!, S(v, "name") ?? "", S(v, "description"), tradeId, N(v, "estimatedHours"), S(v, "safetyNotes"),
            steps, B(v, "active", true), Title: S(v, "title"), WorkType: v.GetValueOrDefault("workType") as WorkType?, Priority: v.GetValueOrDefault("priority") as WorkPriority?,
            ExpectedStopHours: N(v, "expectedStopHours"));
        if (!Accept(row, PmEndpoints.PmTaskRules(input))) return Task.FromResult<PmTask?>(null);
        if (tradeCode is not null && tradeId is null) input = input with { TradeId = c.EnsureLookup(c.Trades, tradeCode, "Trades") };
        if (existing is not null) { Replace(c, existing, input); return Task.FromResult<PmTask?>(null); }
        return Task.FromResult<PmTask?>(PmEndpoints.CreateTask(c.Db, input));
    }

    /// <summary>
    /// Updates a task through the screen's code. Step and part rows it creates have their ids set already, so a task saved
    /// earlier in the same run would take them for existing rows: they are marked new here.
    /// </summary>
    internal static void Replace(ImportContext c, PmTask task, PmEndpoints.TaskInput input)
    {
        var steps = task.Steps.ToHashSet();
        var parts = task.Parts.ToHashSet();
        PmEndpoints.UpdateTask(c.Db, task, input);
        foreach (var s in task.Steps.Where(s => !steps.Contains(s))) c.Db.Entry(s).State = EntityState.Added;
        foreach (var p in task.Parts.Where(p => !parts.Contains(p))) c.Db.Entry(p).State = EntityState.Added;
    }

    protected override void Remember(ImportContext c, PmTask t, Dictionary<string, object?> v) => c.Remember(c.PmTasks, t.Code, t.Id);
}

/// <summary>Measurement points (0.13.0): add and update by code, with the screen's checks; level templates by template code, assets by number.</summary>
internal sealed class MeasurementPointImporter : RecordImporter<MeasurementPoint>
{
    private static readonly string[] TemplateColumns = ["alarmLowTemplate", "alertLowTemplate", "alertHighTemplate", "alarmHighTemplate"];

    public override string Sheet => "MeasurementPoints";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.MeasurementPoints.Include(p => p.Assets).Where(p => codes.Contains(p.Code)), p => p.Code, ct));
    }

    protected override Dictionary<string, object?> Defaults() => new() { ["active"] = true, ["kind"] = MeasurementKind.Other, ["decimals"] = 1 };

    protected override Dictionary<string, object?> Values(ImportContext c, MeasurementPoint p) => new()
    {
        ["code"] = p.Code, ["name"] = p.Name, ["kind"] = p.Kind, ["unit"] = p.Unit, ["decimals"] = p.Decimals, ["validMin"] = p.ValidMin, ["validMax"] = p.ValidMax,
        ["alarmLow"] = p.AlarmLow, ["alertLow"] = p.AlertLow, ["target"] = p.Target, ["alertHigh"] = p.AlertHigh, ["alarmHigh"] = p.AlarmHigh,
        ["alarmLowTemplate"] = Code(c, p.AlarmLowTemplateId), ["alertLowTemplate"] = Code(c, p.AlertLowTemplateId),
        ["alertHighTemplate"] = Code(c, p.AlertHighTemplateId), ["alarmHighTemplate"] = Code(c, p.AlarmHighTemplateId),
        ["assets"] = p.Assets.Select(a => c.CodeOf.GetValueOrDefault(a.AssetId)).OfType<string>().ToList(), ["active"] = p.IsActive,
    };

    private static string? Code(ImportContext c, Guid? id) => id is { } x ? c.CodeOf.GetValueOrDefault(x) : null;

    protected override Task<MeasurementPoint?> SaveAsync(ImportContext c, ImportRow row, MeasurementPoint? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var templates = TemplateColumns.Select(k => Ref(row, c.PmTasks, S(v, k), "Work template")).ToList();
        var assets = L(v, "assets").Select(n => Ref(row, c.Assets, n, "Asset")).OfType<Guid>().Distinct().ToList();
        var input = new MeasurementPointEndpoints.PointInput(S(v, "code") ?? "", S(v, "name") ?? "", E(v, "kind", MeasurementKind.Other), S(v, "unit"), I(v, "decimals") ?? 1,
            N(v, "validMin"), N(v, "validMax"), N(v, "alarmLow"), N(v, "alertLow"), N(v, "target"), N(v, "alertHigh"), N(v, "alarmHigh"),
            templates[0], templates[1], templates[2], templates[3], assets, B(v, "active", true));
        // Checked on a copy, so a refused row changes nothing.
        var probe = new MeasurementPoint { Code = input.Code, Name = input.Name };
        MeasurementPointEndpoints.Apply(probe, input);
        if (!Accept(row, MeasurementPointEndpoints.PointRules(probe, input)) || row.HasProblems) return Task.FromResult<MeasurementPoint?>(null);
        var p = existing ?? new MeasurementPoint { Code = input.Code.Trim(), Name = input.Name.Trim() };
        p.Name = input.Name.Trim();
        MeasurementPointEndpoints.Apply(p, input);
        foreach (var link in p.Assets.Where(a => !assets.Contains(a.AssetId)).ToList()) { p.Assets.Remove(link); c.Db.MeasurementPointAssets.Remove(link); }
        foreach (var a in assets.Where(a => p.Assets.All(x => x.AssetId != a))) p.Assets.Add(new MeasurementPointAsset { PointId = p.Id, AssetId = a });
        if (existing is not null) return Task.FromResult<MeasurementPoint?>(null);
        c.Db.MeasurementPoints.Add(p);
        return Task.FromResult<MeasurementPoint?>(p);
    }

    protected override void Remember(ImportContext c, MeasurementPoint p, Dictionary<string, object?> v) => c.Remember(c.MeasurementPoints, p.Code, p.Id);
}

/// <summary>
/// A sheet of lists, one row per item, each list belonging to an owner (a PM task's steps or parts, an asset's spare parts).
/// Each row says whether its item is new, changed or the same; at the end every owner named on the sheet gets the sheet's
/// items as its whole list.
/// </summary>
internal abstract class ListSheetImporter : ISheetImporter
{
    /// <summary>The owner of the row's list, when its code is known.</summary>
    protected abstract Guid? OwnerOf(ImportContext c, ImportRow row);

    /// <summary>The problem put on the owner's other rows: "Not saved: fix row N of this … first.", in the request's language.</summary>
    protected abstract string Blocked(int row);

    /// <summary>
    /// An owner with a problem row keeps its list as it is: its other rows become problems too ("fix row N first"). An owner
    /// whose list loses items (or is renumbered) shows as changed, with its old and new list, on its first row.
    /// </summary>
    public IReadOnlyList<RowResult> Review(ImportContext c, IReadOnlyList<ImportRow> rows, IReadOnlyList<RowResult> results)
    {
        var ownerOf = new Dictionary<int, Guid>();
        foreach (var r in rows)
            if (OwnerOf(c, r) is { } id) ownerOf[r.Number] = id;
        var blocked = new Dictionary<Guid, int>();
        foreach (var res in results.Where(r => r.Outcome == RowOutcome.Problem))
            if (ownerOf.TryGetValue(res.Row, out var id)) blocked[id] = Math.Min(blocked.GetValueOrDefault(id, int.MaxValue), res.Row);
        var output = results.Select(res =>
            ownerOf.TryGetValue(res.Row, out var id) && blocked.TryGetValue(id, out var first) && res.Outcome != RowOutcome.Problem
                ? res with { Outcome = RowOutcome.Problem, Changes = [], Problems = [Blocked(first)] }
                : res).ToList();
        foreach (var id in blocked.Keys) Forget(id);
        foreach (var (id, change) in ListChanges(c))
        {
            var firstRow = ownerOf.Where(x => x.Value == id).Select(x => x.Key).DefaultIfEmpty(-1).Min();
            var i = output.FindIndex(r => r.Row == firstRow);
            if (i >= 0) output[i] = output[i] with { Outcome = RowOutcome.Changed, Changes = [.. output[i].Changes, change] };
        }
        return output;
    }

    /// <summary>Drops an owner's collected rows: its list isn't replaced.</summary>
    protected abstract void Forget(Guid ownerId);

    /// <summary>The owners whose list loses items (or is renumbered), with the old and new list.</summary>
    protected abstract IEnumerable<(Guid OwnerId, FieldChange Change)> ListChanges(ImportContext c);

    public abstract string Sheet { get; }
    public abstract IReadOnlyList<string> KeyColumns { get; }
    public string KeyOf(ImportRow row) => string.Join('|', KeyColumns.Select(k => ImportValues.Show(row.Value(k))?.Trim() ?? ""));
    public virtual Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct) => Task.CompletedTask;
    public abstract Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct);
    public abstract Task FinishAsync(ImportContext c, CancellationToken ct);

    protected static RowResult Result(ImportRow row, string key, RowOutcome outcome, List<FieldChange>? changes = null) =>
        row.HasProblems ? ImportRun.Problem(row, key) : new RowResult(row.Number, key, outcome, changes ?? [], []);
}

/// <summary>A list belonging to a PM task (its steps, its parts), one row per item.</summary>
internal abstract class PmTaskListImporter : ListSheetImporter
{
    private readonly Dictionary<Guid, PmTask> _tasks = [];

    protected override Guid? OwnerOf(ImportContext c, ImportRow row) =>
        row.Get<string>("taskCode")?.Trim() is { } code && c.PmTasks.TryGetValue(code, out var id) ? id : null;

    protected override string Blocked(int row) => Translator.T($"Not saved: fix row {row} of this task first.");

    /// <summary>The row's task, loaded once with its steps and parts; null with "PM task X not found." on the row.</summary>
    protected async Task<PmTask?> TaskAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var code = row.Get<string>("taskCode")?.Trim();
        if (code is null || !c.PmTasks.TryGetValue(code, out var id)) { row.Problem(Translator.T($"PM task {code} not found.")); return null; }
        if (!_tasks.TryGetValue(id, out var task))
            _tasks[id] = task = await c.Db.PmTasks.Include(t => t.Steps).Include(t => t.Parts).FirstAsync(t => t.Id == id, ct);
        return task;
    }

    protected PmTask TaskById(Guid id) => _tasks[id];

    protected static PmEndpoints.TaskInput InputOf(PmTask t, List<string> steps, List<PmEndpoints.TaskPartInput>? parts, List<PmEndpoints.StepInput>? details = null) =>
        new(t.Code, t.Name, t.Description, t.TradeId, t.EstimatedHours, t.SafetyNotes, steps, t.IsActive, null, parts, StepDetails: details);

    protected static List<string> StepsOf(PmTask t) => t.Steps.OrderBy(s => s.Sequence).Select(s => s.Description).ToList();

    /// <summary>A task's typed steps (0.13.0), in order.</summary>
    protected static List<PmEndpoints.StepInput> DetailsOf(PmTask t) => t.Steps.OrderBy(s => s.Sequence)
        .Select(s => new PmEndpoints.StepInput(s.Description, s.Type, s.Unit, s.Decimals, s.MeasurementPointId, s.PhotoRequired, s.BeforeStart, s.FollowUp, s.FollowUpTemplateId)).ToList();

    /// <summary>Replaces a task's lists through the screen's code, and makes sure the new step and part rows are inserted.</summary>
    internal static void Replace(ImportContext c, PmTask task, PmEndpoints.TaskInput input) => PmTaskImporter.Replace(c, task, input);
}

internal sealed class PmStepImporter : PmTaskListImporter
{
    private readonly Dictionary<Guid, SortedDictionary<int, PmEndpoints.StepInput>> _wanted = [];
    private readonly Dictionary<Guid, List<PmEndpoints.StepInput>> _before = [];

    public override string Sheet => "PmSteps";
    public override IReadOnlyList<string> KeyColumns => ["taskCode", "step"];

    public override async Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var shown = $"{row.Get<string>("taskCode")} · {row.Get<int?>("step")}";
        var task = await TaskAsync(c, row, ct);
        var step = row.Get<int?>("step") ?? 0;
        var text = row.Get<string>("description");
        if (step < 1) row.Problem(Translator.T("Steps are numbered from 1."));
        if (text is null) row.Problem(Translator.T($"{row.Sheet.Column("description").Heading} is required."));
        // Step types (0.13.0): codes resolved; blank cells keep what the step with the same number and text has.
        var pointCode = row.Get<string>("measurementPoint")?.Trim();
        var templateCode = row.Get<string>("followUpTemplate")?.Trim();
        Guid? pointId = null, templateId = null;
        if (pointCode is { Length: > 0 }) { if (c.MeasurementPoints.TryGetValue(pointCode, out var pid)) pointId = pid; else row.Problem(Translator.T($"Measurement point {pointCode} not found.")); }
        if (templateCode is { Length: > 0 }) { if (c.PmTasks.TryGetValue(templateCode, out var tid)) templateId = tid; else row.Problem(Translator.T($"Work template {templateCode} not found.")); }
        if (task is null || row.HasProblems) return Result(row, shown, RowOutcome.Problem);
        var before = _before.TryGetValue(task.Id, out var b) ? b : _before[task.Id] = DetailsOf(task);
        var wanted = _wanted.TryGetValue(task.Id, out var w) ? w : _wanted[task.Id] = [];
        if (wanted.Count >= 300) { row.Problem(Translator.T("At most 300 steps.")); return Result(row, shown, RowOutcome.Problem); }
        var old = step <= before.Count ? before[step - 1] : null;
        var basis = old is not null && old.Description == text ? old : new PmEndpoints.StepInput(text!);
        var def = basis with
        {
            Description = text!, Type = row.Get<StepType?>("type") ?? basis.Type, Unit = row.Get<string>("unit").Clean() ?? basis.Unit,
            Decimals = row.Get<int?>("decimals") ?? basis.Decimals, MeasurementPointId = pointCode is { Length: > 0 } ? pointId : basis.MeasurementPointId,
            PhotoRequired = row.Get<bool?>("photoRequired") ?? basis.PhotoRequired, BeforeStart = row.Get<bool?>("beforeStart") ?? basis.BeforeStart,
            FollowUp = row.Get<FollowUpKind?>("followUp") ?? basis.FollowUp, FollowUpTemplateId = templateCode is { Length: > 0 } ? templateId : basis.FollowUpTemplateId,
        };
        if (def.MeasurementPointId is not null && def.Type != StepType.Number) row.Problem(Translator.T("Only number steps can use a measurement point."));
        if (def.FollowUp == FollowUpKind.WorkOrder && def.FollowUpTemplateId is null) row.Problem(Translator.T(PmEndpoints.NeedsTemplate));
        if (def.Unit is { Length: > 20 }) row.Problem(Translator.T("Unit can be at most 20 characters."));
        if (def.Decimals is < 0 or > 4) row.Problem(Translator.T("Decimals can be 0 to 4."));
        if (row.HasProblems) return Result(row, shown, RowOutcome.Problem);
        wanted[step] = def;
        if (old is null) return Result(row, shown, RowOutcome.New);
        if (old == def) return Result(row, shown, RowOutcome.Unchanged);
        return old.Description != def.Description
            ? Result(row, shown, RowOutcome.Changed, [new FieldChange(row.Sheet.Column("description").Heading, old.Description, def.Description)])
            : Result(row, shown, RowOutcome.Changed, [new FieldChange(Translator.T("Step"), Summary(old), Summary(def))]);
    }

    private static string Summary(PmEndpoints.StepInput s) => string.Join(", ", new[]
    {
        ImportSheets.ChoiceText(s.Type), s.Unit, s.PhotoRequired ? Translator.T("Photo required") : null, s.BeforeStart ? Translator.T("Before starting") : null,
        ImportSheets.ChoiceText(s.FollowUp),
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    protected override void Forget(Guid taskId) => _wanted.Remove(taskId);

    protected override IEnumerable<(Guid OwnerId, FieldChange Change)> ListChanges(ImportContext c)
    {
        foreach (var (id, wanted) in _wanted)
        {
            var before = _before[id];
            var renumbered = wanted.Keys.Select((step, i) => step != i + 1).Any(x => x);
            if (before.Count > wanted.Count || renumbered)
                yield return (id, new FieldChange(Translator.T("Steps"), string.Join("; ", before.Select(s => s.Description)), string.Join("; ", wanted.Values.Select(s => s.Description))));
        }
    }

    public override Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        foreach (var (id, wanted) in _wanted)
        {
            var task = TaskById(id);
            var steps = wanted.Values.ToList();
            if (steps.SequenceEqual(_before[id])) continue;
            Replace(c, task, InputOf(task, steps.Select(s => s.Description).ToList(), null, steps));
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

internal sealed class PmPartImporter : PmTaskListImporter
{
    private readonly Dictionary<Guid, Dictionary<Guid, decimal>> _wanted = [];
    private readonly Dictionary<Guid, Dictionary<Guid, decimal>> _before = [];

    public override string Sheet => "PmParts";
    public override IReadOnlyList<string> KeyColumns => ["taskCode", "partNumber"];

    public override async Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var shown = $"{row.Get<string>("taskCode")} · {row.Get<string>("partNumber")}";
        var task = await TaskAsync(c, row, ct);
        var partNumber = row.Get<string>("partNumber")?.Trim();
        Guid partId = Guid.Empty;
        if (partNumber is not null && !c.Parts.TryGetValue(partNumber, out partId)) row.Problem(Translator.T($"Part {partNumber} not found."));
        var quantity = Math.Round(row.Get<decimal?>("quantity") ?? 0, 3);
        if (quantity is <= 0 or > 1_000_000_000m) row.Problem(Translator.T("Each part needs a quantity above zero."));
        if (task is null || row.HasProblems) return Result(row, shown, RowOutcome.Problem);
        var before = _before.TryGetValue(task.Id, out var b) ? b : _before[task.Id] = task.Parts.ToDictionary(p => p.PartId, p => p.Quantity);
        var wanted = _wanted.TryGetValue(task.Id, out var w) ? w : _wanted[task.Id] = [];
        if (wanted.Count >= 200) { row.Problem(Translator.T("At most 200 parts.")); return Result(row, shown, RowOutcome.Problem); }
        wanted[partId] = quantity;
        if (!before.TryGetValue(partId, out var old)) return Result(row, shown, RowOutcome.New);
        return old == quantity ? Result(row, shown, RowOutcome.Unchanged)
            : Result(row, shown, RowOutcome.Changed, [new FieldChange(row.Sheet.Column("quantity").Heading, ImportValues.Plain(old), ImportValues.Plain(quantity))]);
    }

    protected override void Forget(Guid taskId) => _wanted.Remove(taskId);

    protected override IEnumerable<(Guid OwnerId, FieldChange Change)> ListChanges(ImportContext c)
    {
        string Show(Dictionary<Guid, decimal> list) =>
            string.Join("; ", list.Select(x => $"{c.CodeOf.GetValueOrDefault(x.Key)} × {ImportValues.Plain(x.Value)}").Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (id, wanted) in _wanted)
        {
            var before = _before[id];
            if (before.Keys.Any(k => !wanted.ContainsKey(k)))
                yield return (id, new FieldChange(Translator.T("Parts"), Show(before), Show(wanted)));
        }
    }

    public override Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        foreach (var (id, wanted) in _wanted)
        {
            var before = _before[id];
            if (wanted.Count == before.Count && wanted.All(x => before.TryGetValue(x.Key, out var q) && q == x.Value)) continue;
            var task = TaskById(id);
            Replace(c, task, InputOf(task, StepsOf(task), wanted.Select(x => new PmEndpoints.TaskPartInput(x.Key, x.Value)).ToList()));
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

/// <summary>Each asset's spare-parts list (the Asset parts sheet), one row per part, like the PM parts sheet.</summary>
internal sealed class AssetPartImporter : ListSheetImporter
{
    public const int MaxLines = AssetPartEndpoints.MaxLines;
    private readonly Dictionary<Guid, Dictionary<Guid, (decimal Quantity, string? Note)>> _wanted = [];
    private readonly Dictionary<Guid, Dictionary<Guid, (decimal Quantity, string? Note)>> _before = [];

    public override string Sheet => "AssetParts";
    public override IReadOnlyList<string> KeyColumns => ["assetNumber", "partNumber"];

    protected override Guid? OwnerOf(ImportContext c, ImportRow row) =>
        row.Get<string>("assetNumber")?.Trim() is { } number && c.Assets.TryGetValue(number, out var id) ? id : null;

    protected override string Blocked(int row) => Translator.T($"Not saved: fix row {row} of this asset first.");

    /// <summary>The lists of the assets named on the sheet, in one query.</summary>
    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => OwnerOf(c, r)).OfType<Guid>().Distinct().ToList();
        var lines = await c.Db.AssetParts.AsNoTracking().Where(x => ids.Contains(x.AssetId)).Select(x => new { x.AssetId, x.PartId, x.Quantity, x.Note }).ToListAsync(ct);
        foreach (var id in ids) _before[id] = lines.Where(l => l.AssetId == id).ToDictionary(l => l.PartId, l => (l.Quantity, l.Note));
    }

    public override Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var number = row.Get<string>("assetNumber")?.Trim();
        var partNumber = row.Get<string>("partNumber")?.Trim();
        var shown = $"{number} · {partNumber}";
        var assetId = OwnerOf(c, row);
        if (number is null) row.Problem(Translator.T($"{row.Sheet.Column("assetNumber").Heading} is required."));
        else if (assetId is null) row.Problem(Translator.T($"Asset {number} not found."));
        Guid partId = Guid.Empty;
        if (partNumber is null) row.Problem(Translator.T($"{row.Sheet.Column("partNumber").Heading} is required."));
        else if (!c.Parts.TryGetValue(partNumber, out partId)) row.Problem(Translator.T($"Part {partNumber} not found."));
        var before = assetId is { } a ? _before.TryGetValue(a, out var b) ? b : _before[a] = [] : [];
        // No Note column in the file: notes stay as they are.
        var note = row.Keys.Contains("note") ? row.Get<string>("note").Clean() : before.TryGetValue(partId, out var kept) ? kept.Note : null;
        var line = new AssetPartInput(partId, Math.Round(row.Get<decimal?>("quantity") ?? 0, 3), note);
        foreach (var m in AssetPartEndpoints.AssetPartRules([line]).Messages) row.Problem(Translator.T(m));
        if (assetId is null || row.HasProblems) return System.Threading.Tasks.Task.FromResult(Result(row, shown, RowOutcome.Problem));
        var wanted = _wanted.TryGetValue(assetId.Value, out var w) ? w : _wanted[assetId.Value] = [];
        if (wanted.Count >= MaxLines)
        {
            row.Problem(Translator.T($"At most {MaxLines} parts."));
            return System.Threading.Tasks.Task.FromResult(Result(row, shown, RowOutcome.Problem));
        }
        wanted[partId] = (line.Quantity, line.Note);
        if (!before.TryGetValue(partId, out var old)) return System.Threading.Tasks.Task.FromResult(Result(row, shown, RowOutcome.New));
        var changes = new List<FieldChange>();
        if (old.Quantity != line.Quantity) changes.Add(new(row.Sheet.Column("quantity").Heading, ImportValues.Plain(old.Quantity), ImportValues.Plain(line.Quantity)));
        if (old.Note != line.Note) changes.Add(new(row.Sheet.Column("note").Heading, old.Note, line.Note));
        return System.Threading.Tasks.Task.FromResult(Result(row, shown, changes.Count == 0 ? RowOutcome.Unchanged : RowOutcome.Changed, changes));
    }

    protected override void Forget(Guid ownerId) => _wanted.Remove(ownerId);

    protected override IEnumerable<(Guid OwnerId, FieldChange Change)> ListChanges(ImportContext c)
    {
        string Show(Dictionary<Guid, (decimal Quantity, string? Note)> list) =>
            string.Join("; ", list.Select(x => $"{c.CodeOf.GetValueOrDefault(x.Key)} × {ImportValues.Plain(x.Value.Quantity)}").Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (id, wanted) in _wanted)
            if (_before[id].Keys.Any(k => !wanted.ContainsKey(k)))
                yield return (id, new FieldChange(Translator.T("Parts"), Show(_before[id]), Show(wanted)));
    }

    /// <summary>Each asset whose list differs gets the sheet's list, through the screen's code.</summary>
    public override async Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        var changed = _wanted.Where(x => x.Value.Count != _before[x.Key].Count || x.Value.Any(l => !_before[x.Key].TryGetValue(l.Key, out var o) || o != l.Value))
            .Select(x => x.Key).ToList();
        if (changed.Count == 0) return;
        var tracked = (await c.Db.AssetParts.Where(x => changed.Contains(x.AssetId)).ToListAsync(ct)).ToLookup(x => x.AssetId);
        foreach (var id in changed)
            AssetPartEndpoints.Replace(c.Db, tracked[id].ToList(), id, _wanted[id].Select(x => new AssetPartInput(x.Key, x.Value.Quantity, x.Value.Note)).ToList());
    }
}

/// <summary>
/// The Supplier catalogue sheet (0.18.0): one row per part and supplier, checked as on the part page. Rows are added or changed; a supplier
/// left off the sheet keeps its row (removing is done on the part page, so a sheet never wipes the prices paid).
/// </summary>
internal sealed class SupplierCatalogueImporter : ISheetImporter
{
    private readonly Dictionary<(Guid Part, Guid Supplier), SupplierPart> _before = [];
    private readonly Dictionary<(Guid Part, Guid Supplier), CatalogueEndpoints.SupplierPartInput> _wanted = [];
    private Dictionary<Guid, string> _units = [];
    private Dictionary<Guid, (bool IsSupplier, string? Currency)> _companies = [];

    public string Sheet => "SupplierCatalogue";
    public IReadOnlyList<string> KeyColumns => ["partNumber", "supplierCode"];
    public string KeyOf(ImportRow row) => string.Join('|', KeyColumns.Select(k => ImportValues.Show(row.Value(k))?.Trim() ?? ""));

    public async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var parts = rows.Select(r => r.Get<string>("partNumber")?.Trim()).OfType<string>().Select(n => c.Parts.TryGetValue(n, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToList();
        _units = await c.Db.Parts.AsNoTracking().Where(p => parts.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.UnitOfMeasure, ct);
        foreach (var r in await c.Db.SupplierParts.AsNoTracking().Where(r => parts.Contains(r.PartId)).ToListAsync(ct)) _before[(r.PartId, r.SupplierId)] = r;
        _companies = await c.Db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => (s.IsSupplier, s.Currency), ct);
    }

    public Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)
    {
        var partNumber = row.Get<string>("partNumber")?.Trim();
        var code = row.Get<string>("supplierCode")?.Trim();
        var shown = $"{partNumber} · {code}";
        Guid partId = Guid.Empty, supplierId = Guid.Empty;
        if (partNumber is null) row.Problem(Translator.T($"{row.Sheet.Column("partNumber").Heading} is required."));
        else if (!c.Parts.TryGetValue(partNumber, out partId)) row.Problem(Translator.T($"Part {partNumber} not found."));
        if (code is null) row.Problem(Translator.T($"{row.Sheet.Column("supplierCode").Heading} is required."));
        else if (!c.Suppliers.TryGetValue(code, out supplierId)) row.Problem(Translator.T($"Supplier {code} not found."));
        var old = _before.GetValueOrDefault((partId, supplierId));
        if (supplierId != Guid.Empty && old is null && !_companies.GetValueOrDefault(supplierId).IsSupplier) row.Problem(Translator.T("Choose a company with the Supplier role."));
        if (row.HasProblems) return Task.FromResult(Result(row, shown, RowOutcome.Problem));
        // Left out (no column): kept as it is; a new row starts from the part's unit, a pack of 1 and the company's (else the base) currency.
        // A blank cell keeps what is there, as on every other sheet; a new row takes the default.
        T Keep<T>(string key, T kept, T fresh) => row.Has(key) ? row.Get<T>(key)! : old is null ? fresh : kept;
        var currency = (row.Get<string>("currency")?.Trim().ToUpperInvariant()) ?? old?.Currency ?? _companies[supplierId].Currency.Clean()?.ToUpperInvariant() ?? c.Settings.Currency.ToUpperInvariant();
        var input = new CatalogueEndpoints.SupplierPartInput(supplierId,
            Keep<string?>("supplierPartNumber", old?.SupplierPartNumber, null), Keep<string?>("packName", old?.PackName, null) ?? old?.PackName ?? _units.GetValueOrDefault(partId),
            Keep<decimal?>("packSize", old?.PackSize, 1m) ?? 1m, Keep<decimal?>("packPrice", old?.PackPrice, null), currency,
            Keep<decimal?>("discountPercent", old?.DiscountPercent, 0m) ?? 0m, Keep<int?>("leadTimeDays", old?.LeadTimeDays, null), Keep<int?>("minPacks", old?.MinPacks, 1) ?? 1,
            Keep<string?>("notes", old?.Notes, null), old?.IsActive ?? true);
        foreach (var m in CatalogueEndpoints.Rules(input).Messages) row.Problem(Translator.T(m));
        if (row.HasProblems) return Task.FromResult(Result(row, shown, RowOutcome.Problem));
        _wanted[(partId, supplierId)] = input;
        if (old is null) return Task.FromResult(Result(row, shown, RowOutcome.New));
        var changes = new List<FieldChange>();
        void Diff<TV>(string key, TV before, TV after) { if (!Equals(before, after)) changes.Add(new(row.Sheet.Column(key).Heading, ImportValues.Show(before), ImportValues.Show(after))); }
        Diff("supplierPartNumber", old.SupplierPartNumber, input.SupplierPartNumber.Clean()); Diff("packName", old.PackName, input.PackName.Clean() ?? old.PackName);
        Diff("packSize", old.PackSize, input.PackSize); Diff("packPrice", old.PackPrice, input.PackPrice); Diff("currency", old.Currency, input.Currency);
        Diff("discountPercent", old.DiscountPercent, input.DiscountPercent); Diff("leadTimeDays", old.LeadTimeDays, input.LeadTimeDays); Diff("minPacks", old.MinPacks, input.MinPacks);
        Diff("notes", old.Notes, input.Notes.Clean());
        return Task.FromResult(Result(row, shown, changes.Count == 0 ? RowOutcome.Unchanged : RowOutcome.Changed, changes));
    }

    /// <summary>Adds and changes the rows the sheet asked for.</summary>
    public async Task FinishAsync(ImportContext c, CancellationToken ct)
    {
        if (_wanted.Count == 0) return;
        var partIds = _wanted.Keys.Select(k => k.Part).Distinct().ToList();
        var tracked = await c.Db.SupplierParts.Where(r => partIds.Contains(r.PartId)).ToListAsync(ct);
        foreach (var ((partId, supplierId), i) in _wanted)
        {
            var row = tracked.FirstOrDefault(r => r.PartId == partId && r.SupplierId == supplierId);
            if (row is null) { row = new SupplierPart { PartId = partId, SupplierId = supplierId, PackName = "", Currency = i.Currency! }; c.Db.SupplierParts.Add(row); }
            row.SupplierPartNumber = i.SupplierPartNumber.Clean(); row.PackName = i.PackName.Clean() ?? _units.GetValueOrDefault(partId) ?? row.PackName;
            row.PackSize = i.PackSize; row.PackPrice = i.PackPrice; row.Currency = i.Currency!; row.DiscountPercent = i.DiscountPercent; row.LeadTimeDays = i.LeadTimeDays;
            row.MinPacks = i.MinPacks; row.Notes = i.Notes.Clean();
        }
    }

    private static RowResult Result(ImportRow row, string key, RowOutcome outcome, List<FieldChange>? changes = null) =>
        row.HasProblems ? ImportRun.Problem(row, key) : new RowResult(row.Number, key, outcome, changes ?? [], []);
}

internal sealed class PmScheduleImporter : RecordImporter<PmSchedule>
{
    public override string Sheet => "PmSchedules";
    public override IReadOnlyList<string> KeyColumns => ["code"];

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        Keep(await ByKey(c.Db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Assets).Include(s => s.Calendars).AsSplitQuery().Where(s => codes.Contains(s.Code)), s => s.Code, ct));
        if (c.Db.CurrentSiteIds is not null) Hide(await c.Db.PmSchedules.AllSites().Where(s => codes.Contains(s.Code)).Select(s => s.Code).ToListAsync(ct));
    }

    protected override Dictionary<string, object?> Values(ImportContext c, PmSchedule s) => new()
    {
        ["code"] = s.Code, ["name"] = s.Name, ["taskCode"] = c.CodeOf.GetValueOrDefault(s.PmTaskId), ["siteCode"] = c.CodeOf.GetValueOrDefault(s.SiteId),
        ["locationCode"] = s.LocationId is { } l ? c.CodeOf.GetValueOrDefault(l) : null, ["assetNumber"] = s.AssetId is { } a ? c.CodeOf.GetValueOrDefault(a) : null,
        ["tradeCode"] = s.TradeId is { } t ? c.CodeOf.GetValueOrDefault(t) : null, ["priority"] = s.Priority,
        ["schedule"] = s.Type == PmScheduleType.Meter ? null : s.Type, ["every"] = s.Frequency, ["unit"] = s.Unit, ["countFrom"] = s.Basis,
        ["weekOfMonth"] = s.WeekOfMonth is { } w ? (WeekOfMonth)w : null, ["weekday"] = s.DayOfWeek, ["dayOfMonth"] = s.DayOfMonth,
        ["startDate"] = s.StartDate, ["leadDays"] = s.LeadDays, ["workPeriodDays"] = s.WorkPeriodDays,
        ["assignees"] = s.AssigneeIds.Select(id => c.CodeOf.GetValueOrDefault(id)).OfType<string>().ToList(), ["description"] = s.Description, ["active"] = s.IsActive,
        ["multiAssetMode"] = s.Assets.Count > 0 ? s.MultiAssetMode : null,
    };

    protected override Dictionary<string, object?> Defaults() => new()
    {
        ["priority"] = WorkPriority.Medium, ["every"] = 1, ["unit"] = FrequencyUnit.Month, ["countFrom"] = PmBasis.Scheduled, ["leadDays"] = 7,
        ["workPeriodDays"] = 1, ["active"] = true,
    };

    protected override Task<PmSchedule?> SaveAsync(ImportContext c, ImportRow row, PmSchedule? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var type = v.GetValueOrDefault("schedule") as PmScheduleType? ?? existing?.Type;
        if (existing is null && type is null or PmScheduleType.Meter) row.Problem(Translator.T("Set up meter-based schedules on screen."));
        if (existing is null && D(v, "startDate") is null) row.Problem(Translator.T($"{row.Sheet.Column("startDate").Heading} is required."));
        if (S(v, "taskCode") is null) row.Problem(Translator.T($"{row.Sheet.Column("taskCode").Heading} is required."));
        if (S(v, "siteCode") is null) row.Problem(Translator.T("Choose a site."));
        var taskId = Ref(row, c.PmTasks, S(v, "taskCode"), "PM task");
        var siteId = Ref(row, c.Sites, S(v, "siteCode"), "Site");
        Guid? locationId = null;
        if (siteId is { } site && S(v, "locationCode") is { } locationCode)
        {
            if (c.Locations.TryGetValue(ImportContext.LocationKey(site, locationCode), out var lid)) locationId = lid;
            else row.Problem(Translator.T($"Location {locationCode} not found."));
        }
        if (c.Db.CurrentBranchPaths is not null && locationId is null && !row.HasProblems) row.Problem(Translator.T("Choose a location in your part of the site."));
        var assetId = Ref(row, c.Assets, S(v, "assetNumber"), "Asset");
        if (assetId is { } asset && siteId is { } s2 && c.AssetSites.TryGetValue(asset, out var assetSite) && assetSite != s2)
            row.Problem(Translator.T("Asset must belong to the selected site."));
        var assignees = L(v, "assignees").Select(code => Ref(row, c.Employees, code, "Person")).OfType<Guid>().ToList();
        var tradeCode = S(v, "tradeCode");
        Guid? tradeId = tradeCode is not null && c.Trades.TryGetValue(tradeCode, out var known) ? known : null;
        if (row.HasProblems || taskId is null || siteId is null || type is null) return Task.FromResult<PmSchedule?>(null);

        var round = existing?.Assets.OrderBy(a => a.Sequence).Select(a => a.AssetId).ToList();
        var input = new PmEndpoints.ScheduleInput(S(v, "code"), S(v, "name") ?? "", S(v, "description"), siteId.Value, locationId, assetId, tradeId,
            E(v, "priority", WorkPriority.Medium), assignees, taskId.Value, existing?.ExtraTasks.Select(e => new PmEndpoints.ExtraTaskInput(e.PmTaskId, e.EveryNth)).ToList(),
            type.Value, E(v, "countFrom", PmBasis.Scheduled), I(v, "every") ?? 1, E(v, "unit", FrequencyUnit.Month),
            v.GetValueOrDefault("weekOfMonth") is WeekOfMonth w ? (int)w : null, v.GetValueOrDefault("weekday") as DayOfWeek?, I(v, "dayOfMonth"),
            I(v, "workPeriodDays") ?? 1, existing?.WholeMonth ?? false, I(v, "leadDays") ?? 7, D(v, "startDate") ?? existing!.StartDate, null,
            existing?.MeterId, existing?.MeterTrigger, existing?.MeterInterval, null, existing?.LowerLimit, existing?.UpperLimit,
            existing?.LowerTaskId, existing?.UpperTaskId, existing?.TimeBased ?? false, B(v, "active", true), null, round is { Count: > 0 } ? round : null,
            existing?.TeamId, // the sheet has no team column: the schedule keeps its team
            // 0.12.0: the sheet doesn't hold calendars, so a PM keeps its own; its multi-asset mode follows the screen's rules.
            CalendarIds: existing?.Calendars.Select(x => x.ScheduleCalendarId).ToList(), MultiAssetMode: v.GetValueOrDefault("multiAssetMode") as MultiAssetMode?);
        var rules = PmEndpoints.PmScheduleRules(input, existing?.Id);
        if (existing is not null) rules.Merge(PmEndpoints.ScheduleChangeRules(existing, input));
        if (!Accept(row, rules)) return Task.FromResult<PmSchedule?>(null);
        if (tradeCode is not null && tradeId is null) input = input with { TradeId = c.EnsureLookup(c.Trades, tradeCode, "Trades") };
        if (existing is not null) { PmEndpoints.UpdateSchedule(existing, input); return Task.FromResult<PmSchedule?>(null); }
        return Task.FromResult<PmSchedule?>(PmEndpoints.CreateSchedule(c.Db, input, input.Code!.Trim()));
    }
}
