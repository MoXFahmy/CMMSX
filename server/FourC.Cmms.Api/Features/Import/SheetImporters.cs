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
        new SupplierImporter(), new StoreImporter(), new AssetImporter(), new PeopleImporter(),
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
        return [.. ordered, .. rows.Where(r => !ordered.Contains(r))];
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
    public override IReadOnlyList<ImportRow> Order(IReadOnlyList<ImportRow> rows, ImportContext c) => ParentsFirst(rows, KeyOf,
        r => r.Get<string>("parentCode") is { } p ? $"{r.Get<string>("siteCode")?.Trim()}|{p.Trim()}" : null, "code");

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

    public override async Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var numbers = Codes(rows, "assetNumber");
        Keep(await ByKey(c.Db.Assets.Where(a => numbers.Contains(a.AssetNumber)), a => a.AssetNumber, ct));
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
            D(v, "warrantyExpiresOn"), S(v, "warrantyNotes"), S(v, "notes"), S(v, "notesToTechnician"), custom);
        var rules = AssetEndpoints.AssetRules(input, existing?.Id, requireNumber);
        var values = AssetEndpoints.CustomFieldValues(rules, c.AssetFields, input);
        if (!Accept(row, rules)) return null;
        // A new category is added only once the row is known to be good.
        if (categoryCode is not null && categoryId is null) input = input with { CategoryId = c.EnsureLookup(c.Categories, categoryCode, "Categories") };

        if (existing is not null)
        {
            AssetEndpoints.UpdateAsset(c.Db, existing, input, values, c.Tenant);
            c.AssetParents[existing.Id] = existing.ParentAssetId;
            return null;
        }
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
