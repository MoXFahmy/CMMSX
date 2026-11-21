using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>The importer for each sheet, and the run's sheet definitions that depend on the organization (Assets' custom fields).</summary>
internal static class SheetImporters
{
    public static IEnumerable<ISheetImporter> All(IReadOnlyList<CustomFieldDefinition> customFields) =>
    [
        new SiteImporter(), new LocationImporter(), new CategoryImporter(), new TradeImporter(), new PartGroupImporter(),
        new SupplierImporter(), new StoreImporter(),
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

    public abstract Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct);

    /// <summary>The order to take the rows in; the sheet's own by default.</summary>
    public virtual IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) => rows;

    /// <summary>Work left after the last row (posting opening stock, replacing task lists…).</summary>
    public virtual Task FinishAsync(ImportContext c, CancellationToken ct) => Task.CompletedTask;

    /// <summary>A stored record's values, by column key.</summary>
    protected abstract Dictionary<string, object?> Values(ImportContext c, TEntity e);

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
        Existing[key] = created!;
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
        string s => s.Clean(),
        IEnumerable<string> list => ImportValues.Show(list.Select(x => x.Trim()).Where(x => x.Length > 0).Order(StringComparer.OrdinalIgnoreCase).ToList()).Clean(),
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
    public override IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c)
    {
        var byKey = new Dictionary<string, ImportRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Where(r => !r.HasProblems)) byKey.TryAdd(KeyOf(r), r);
        string? ParentKey(ImportRow r) => r.Get<string>("parentCode") is { } p ? $"{r.Get<string>("siteCode")?.Trim()}|{p.Trim()}" : null;
        var ordered = new List<ImportRow>();
        var state = new Dictionary<ImportRow, int>(); // 1 = visiting, 2 = done
        void Visit(ImportRow r, List<ImportRow> path)
        {
            if (state.GetValueOrDefault(r) == 2) return;
            if (state.GetValueOrDefault(r) == 1)
            {
                foreach (var x in path.SkipWhile(x => x != r)) x.Problem(Translator.T($"{x.Get<string>("code")} can't be under itself."));
                return;
            }
            state[r] = 1;
            path.Add(r);
            if (ParentKey(r) is { } pk && byKey.TryGetValue(pk, out var parent) && parent != r) Visit(parent, path);
            path.RemoveAt(path.Count - 1);
            state[r] = 2;
            ordered.Add(r);
        }
        foreach (var r in rows) if (!r.HasProblems) Visit(r, []);
        return [.. ordered, .. rows.Where(r => !ordered.Contains(r))];
    }

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var codes = Codes(rows, "code");
        var siteIds = Codes(rows, "siteCode").Select(s => c.Sites.TryGetValue(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var found = await c.Db.Locations.Where(l => siteIds.Contains(l.SiteId) && codes.Contains(l.Code)).ToListAsync(ct);
        foreach (var l in found) Existing[$"{c.CodeOf.GetValueOrDefault(l.SiteId)}|{l.Code}"] = l;
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
            await LocationEndpoints.UpdateLocationAsync(c.Db, existing, input, ct);
            c.LocationTree[existing.Id] = (existing.SiteId, existing.ParentId);
            return null;
        }
        return await LocationEndpoints.CreateLocationAsync(c.Db, input, ct);
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
    { ["code"] = x.Code, ["name"] = x.Name, ["color"] = x.Color, ["active"] = x.IsActive };

    protected override Task<AssetCategory?> SaveAsync(ImportContext c, ImportRow row, AssetCategory? existing, Dictionary<string, object?> v, CancellationToken ct)
    {
        var input = new AssetCategoryEndpoints.CategoryInput(S(v, "code")!, S(v, "name") ?? "", existing?.Description, S(v, "color"), B(v, "active", true));
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
