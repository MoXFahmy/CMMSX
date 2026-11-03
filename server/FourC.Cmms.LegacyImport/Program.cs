// 4CSQL → 4C CMMS cloud importer.
// Reads a legacy 4CSQL (SQL Server) database and upserts sites, departments, locations, asset categories,
// assets (incl. nameplate fields, parent/child links) and transfer history into a tenant.
// Idempotent: every imported record carries a LegacyRef, so re-running updates instead of duplicating.
using System.Diagnostics;
using Dapper;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var opts = Options.Parse(args);
if (opts is null) { Options.PrintUsage(); return 2; }
var sw = Stopwatch.StartNew();
var report = new Report();

await using var legacy = new SqlConnection(opts.Source);
await legacy.OpenAsync();
Console.WriteLine($"Source: {legacy.Database} (4CSQL {await Legacy.VersionAsync(legacy) ?? "unknown version"})");

var dbOptions = new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer(opts.Target, s => s.CommandTimeout(300)).Options;
Guid tenantId;
await using (var probe = new CmmsDbContext(dbOptions, new FixedTenantContext(null)))
{
    await probe.Database.MigrateAsync();
    if (opts.CreateTenant is not null)
    {
        tenantId = Guid.CreateVersion7();
        await using var tdb = new CmmsDbContext(dbOptions, new FixedTenantContext(tenantId));
        await TenantProvisioning.CreateAsync(tdb, new PasswordHasher<AppUser>(), tenantId, opts.CreateTenant, opts.AdminName ?? "Administrator",
            opts.AdminEmail!, opts.AdminPassword!, seedCategories: false);
        Console.WriteLine($"Created tenant '{opts.CreateTenant}' ({tenantId})");
    }
    else
    {
        var t = await probe.Tenants.FirstOrDefaultAsync(x => x.Slug == opts.Tenant || x.Id.ToString() == opts.Tenant);
        if (t is null) { Console.Error.WriteLine($"Tenant '{opts.Tenant}' not found."); return 1; }
        tenantId = t.Id;
        Console.WriteLine($"Target tenant: {t.Name} ({t.Slug})");
    }
}

await using var db = new CmmsDbContext(dbOptions, new FixedTenantContext(tenantId, null, "4CSQL import"));
await using var tx = opts.DryRun ? await db.Database.BeginTransactionAsync() : null;
var data = await Legacy.LoadAsync(legacy);
Console.WriteLine($"Legacy rows: {data.Sites.Count} sites, {data.Departments.Count} departments, {data.Locations.Count} locations, " +
                  $"{data.Categories.Count} categories, {data.Assets.Count} assets, {data.Siblings.Count} parent links, {data.Transfers.Count} transfers");

var ci = StringComparer.OrdinalIgnoreCase;
string K(string? s) => (s ?? "").Trim();

// ---------- Terminology (legacy "relable" table) ----------
var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
foreach (var (from, to) in data.Relabels)
{
    var target = K(to);
    if (target.Length == 0) continue;
    switch (K(from).ToLowerInvariant())
    {
        case "site": tenant.Settings.Terminology.Site = target; tenant.Settings.Terminology.Sites = target + "s"; break;
        case "location": tenant.Settings.Terminology.Location = target; tenant.Settings.Terminology.Locations = target + "s"; break;
        case "asset": tenant.Settings.Terminology.Asset = target; tenant.Settings.Terminology.Assets = target + "s"; break;
        case "department" or "departments" or "dept" or "section":
            report.Warn($"Legacy label '{from}' was renamed '{to}'; departments are imported as top-level Area locations."); break;
        default: break; // branding or other UI relabels do not affect the data model

    }
}
tenant.Settings = Clone(tenant.Settings);

// ---------- Sites ----------
var sites = await db.Sites.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && !s.IsDeleted).ToListAsync();
var siteByRef = sites.Where(s => s.LegacyRef != null).ToDictionary(s => s.LegacyRef!, ci);
Site UpsertSite(string code, string? name)
{
    var key = $"site:{code}";
    if (!siteByRef.TryGetValue(key, out var s))
    {
        s = sites.FirstOrDefault(x => ci.Equals(x.Code, code)) ?? new Site { Code = Trunc(code, 50)!, Name = "" };
        if (s.TenantId == Guid.Empty) { db.Sites.Add(s); report.Created("Site"); } else report.Updated("Site");
        s.LegacyRef = key;
        siteByRef[key] = s;
    }
    else report.Updated("Site");
    s.Name = Trunc(string.IsNullOrWhiteSpace(name) ? code : name.Trim(), 200)!;
    return s;
}
foreach (var s in data.Sites) UpsertSite(K(s.SiteCode), s.Sitename);
Site? fallbackSite = null;
Site DefaultSite() => fallbackSite ??= siteByRef.Values.FirstOrDefault() ?? UpsertSite("LEGACY", "Imported (no site)");
Site SiteFor(string? code) => !string.IsNullOrWhiteSpace(code) && siteByRef.TryGetValue($"site:{K(code)}", out var s) ? s : DefaultSite();
await db.SaveChangesAsync();

// ---------- Departments → top-level "Area" locations; Locations → children ----------
var locs = await db.Locations.IgnoreQueryFilters().Where(l => l.TenantId == tenantId && !l.IsDeleted).ToListAsync();
var locByRef = locs.Where(l => l.LegacyRef != null).ToDictionary(l => l.LegacyRef!, ci);
Location UpsertLocation(string key, Site site, Location? parent, string code, string name, LocationType type, Action<Location>? apply = null)
{
    if (!locByRef.TryGetValue(key, out var l))
    {
        l = new Location { SiteId = site.Id, Code = "", Name = "" , LegacyRef = key };
        db.Locations.Add(l); locByRef[key] = l; report.Created(type == LocationType.Area ? "Department" : "Location");
    }
    else report.Updated(type == LocationType.Area ? "Department" : "Location");
    l.SiteId = site.Id; l.ParentId = parent?.Id; l.Type = type;
    l.Code = Trunc(code, 100)!; l.Name = Trunc(string.IsNullOrWhiteSpace(name) ? code : name.Trim(), 300)!;
    l.Path = (parent?.Path ?? "/") + Location.Segment(l.Id); l.Depth = parent is null ? 0 : parent.Depth + 1;
    apply?.Invoke(l);
    return l;
}
var deptByCode = new Dictionary<string, Location>(ci);
foreach (var d in data.Departments)
{
    var site = SiteFor(d.SiteCode);
    var node = UpsertLocation($"dept:{K(d.SiteCode)}|{K(d.DepartmentNo)}", site, null, K(d.DepartmentNo), d.DepartmentDescription ?? "", LocationType.Area,
        l => { l.IsActive = d.Active ?? true; l.Latitude = ParseDouble(d.Latitude); l.Longitude = ParseDouble(d.Longitude); });
    deptByCode.TryAdd($"{K(d.SiteCode)}|{K(d.DepartmentNo)}", node);
    deptByCode.TryAdd(K(d.DepartmentNo), node);
}
Location? Dept(string? site, string? dept) =>
    string.IsNullOrWhiteSpace(dept) ? null : deptByCode.GetValueOrDefault($"{K(site)}|{K(dept)}") ?? deptByCode.GetValueOrDefault(K(dept));

var locByCode = new Dictionary<string, Location>(ci);
var usedCodes = new HashSet<string>(ci);
foreach (var l in data.Locations)
{
    var dept = Dept(l.SiteCode, l.DeptNo);
    var site = string.IsNullOrWhiteSpace(l.SiteCode) && dept is not null ? sites.Concat(siteByRef.Values).First(s => s.Id == dept.SiteId) : SiteFor(l.SiteCode);
    if (dept is not null && dept.SiteId != site.Id) { report.Warn($"Location {l.LocationNo}: department {l.DeptNo} belongs to another site; placed at site root."); dept = null; }
    var code = K(l.LocationNo);
    if (!usedCodes.Add($"{site.Id}|{code}")) { report.Warn($"Duplicate location code {code} in site {site.Code}; suffixed."); code += "-2"; }
    locByCode[K(l.LocationNo)] = UpsertLocation($"loc:{K(l.LocationNo)}", site, dept, code, l.LocationDescription ?? "", LocationType.Space, x =>
    {
        x.NotesToTechnician = Trunc(l.NotetoTech, 4000);
        x.Criticality = MapCriticality(l.LocCriticality);
    });
}
await db.SaveChangesAsync();

// ---------- Asset categories ----------
var cats = await db.AssetCategories.IgnoreQueryFilters().Where(c => c.TenantId == tenantId && !c.IsDeleted).ToListAsync();
var catByCode = new Dictionary<string, AssetCategory>(ci);
foreach (var c in cats) catByCode.TryAdd(c.Code, c);
foreach (var c in data.Categories)
{
    var code = Trunc(K(c.AssetCatCode), 50)!;
    if (!catByCode.TryGetValue(code, out var cat)) { cat = new AssetCategory { Code = code, Name = "" }; db.AssetCategories.Add(cat); catByCode[code] = cat; report.Created("Category"); }
    else report.Updated("Category");
    cat.LegacyRef = $"cat:{code}";
    cat.Name = Trunc(string.IsNullOrWhiteSpace(c.AssetCategory) ? code : c.AssetCategory.Trim(), 200)!;
}
await db.SaveChangesAsync();

// ---------- Custom fields from nameplate columns (labels from the customer's own captions) ----------
var fields = await db.CustomFields.IgnoreQueryFilters().Where(f => f.TenantId == tenantId && !f.IsDeleted && f.EntityType == "Asset").ToListAsync();
var usedNameplates = Enumerable.Range(1, 30).Where(n => data.Assets.Any(a => !string.IsNullOrWhiteSpace(a.Nameplate(n)))).ToList();
foreach (var n in usedNameplates)
{
    var key = $"nameplate_{n}";
    var label = data.NameplateCaptions.GetValueOrDefault(n) is { Length: > 0 } cap ? cap : $"Additional Info {n}";
    var f = fields.FirstOrDefault(x => x.Key == key);
    if (f is null) { f = new CustomFieldDefinition { EntityType = "Asset", Key = key, Label = "" }; db.CustomFields.Add(f); report.Created("Custom field"); }
    f.Label = Trunc(label, 200)!; f.SortOrder = n; f.LegacyRef = $"nameplate:{n}";
}
await db.SaveChangesAsync();

// ---------- Assets ----------
var statusNames = data.Statuses.ToDictionary(s => s.AssetStatusID, s => (s.AssetStatusDesc ?? "").Trim().ToLowerInvariant());
var warrantyNames = data.Warranty.ToDictionary(w => w.Warranty_contractID, w => (w.Warranty_contract ?? "").Trim().ToLowerInvariant());
var assets = await db.Assets.IgnoreQueryFilters().Where(a => a.TenantId == tenantId && !a.IsDeleted).ToListAsync();
var assetByNumber = new Dictionary<string, Asset>(ci);
foreach (var a in assets) assetByNumber.TryAdd(a.AssetNumber, a);
var batch = 0;
foreach (var r in data.Assets)
{
    var number = Trunc(K(r.AssetNumber), 100)!;
    if (number.Length == 0) { report.Warn("Skipped an asset with an empty asset number."); continue; }
    var loc = locByCode.GetValueOrDefault(K(r.LocationNo)) ?? Dept(r.SiteCode, r.DepartmentNo);
    var site = loc is not null ? siteByRef.Values.First(s => s.Id == loc.SiteId) : SiteFor(r.SiteCode);
    if (!string.IsNullOrWhiteSpace(r.LocationNo) && !locByCode.ContainsKey(K(r.LocationNo))) report.Warn($"Asset {number}: location '{r.LocationNo}' not found.");
    if (!assetByNumber.TryGetValue(number, out var a)) { a = new Asset { AssetNumber = number, Name = "" }; db.Assets.Add(a); assetByNumber[number] = a; report.Created("Asset"); }
    else report.Updated("Asset");
    a.LegacyRef = $"asset:{number}";
    a.Name = Trunc(FirstLine(r.AssetDescription) ?? number, 300)!;
    a.Description = r.AssetDescription is { } desc && desc.Trim() != a.Name ? Trunc(desc.Trim(), 4000) : null;
    a.SiteId = site.Id; a.LocationId = loc?.Id;
    a.CategoryId = !string.IsNullOrWhiteSpace(r.AssetCatCode) && catByCode.TryGetValue(K(r.AssetCatCode), out var cat) ? cat.Id : null;
    a.Status = MapStatus(r.Status is { } st ? statusNames.GetValueOrDefault(st) : null);
    a.Criticality = MapCriticality(r.Criticality);
    a.Manufacturer = Trunc(r.Manufacturer, 200); a.Model = Trunc(r.ModelNumber, 200); a.SerialNumber = Trunc(r.SerialNumber, 200);
    a.OperationNumber = Trunc(r.OpNo, 50);
    a.CommissionedOn = ToDate(r.DateAcquired); a.DisposedOn = ToDate(r.DataDisposed) ?? ToDate(r.DateSold);
    a.PurchasePrice = Money(r.PurchasePrice ?? r.Price); a.CurrentValue = Money(r.CurrentValue); a.SalvageValue = Money(r.SalvageValue);
    a.ExpectedLifeYears = Money(r.EstLife); a.DowntimeCostPerHour = Money((double?)r.DowntimeCostPerHour);
    var w = r.Warranty_Contract is { } wc ? warrantyNames.GetValueOrDefault(wc) : null;
    a.WarrantyType = w is null || w == "none" ? (r.Warranty_ContractExpiry is null ? WarrantyType.None : WarrantyType.Warranty) : w.StartsWith("contract") ? WarrantyType.ServiceContract : WarrantyType.Warranty;
    a.WarrantyExpiresOn = ToDate(r.Warranty_ContractExpiry); a.WarrantyNotes = Trunc(r.Warranty_ContractNotes, 4000);
    a.Notes = Trunc(r.Comments, 4000); a.NotesToTechnician = Trunc(r.NotesToTech, 4000);
    var custom = new Dictionary<string, string?>(a.CustomFields);
    foreach (var n in usedNameplates) { var v = r.Nameplate(n); if (!string.IsNullOrWhiteSpace(v)) custom[$"nameplate_{n}"] = Trunc(v.Trim(), 4000); }
    a.CustomFields = custom;
    if (++batch % 1000 == 0) { await db.SaveChangesAsync(); Console.WriteLine($"  … {batch} assets"); }
}
await db.SaveChangesAsync();

// ---------- Parent/child links (legacy sibling: AssetNo = parent, Sibling = child) ----------
var parentOf = new Dictionary<Guid, Guid?>();
foreach (var a in assetByNumber.Values) parentOf[a.Id] = a.ParentAssetId;
bool CreatesCycle(Guid child, Guid parent)
{
    Guid? cursor = parent;
    for (var i = 0; cursor is not null && i < 50; i++) { if (cursor == child) return true; cursor = parentOf.GetValueOrDefault(cursor.Value); }
    return false;
}
foreach (var s in data.Siblings)
{
    if (!assetByNumber.TryGetValue(K(s.AssetNo), out var parent) || !assetByNumber.TryGetValue(K(s.Sibling), out var child)) { report.Warn($"Parent link {s.AssetNo} → {s.Sibling}: asset missing."); continue; }
    if (child.Id == parent.Id || CreatesCycle(child.Id, parent.Id)) { report.Warn($"Parent link {s.AssetNo} → {s.Sibling}: would create a cycle, skipped."); continue; }
    if (child.ParentAssetId != parent.Id) { child.ParentAssetId = parent.Id; parentOf[child.Id] = parent.Id; report.Updated("Parent link"); }
}
await db.SaveChangesAsync();

// ---------- Transfer history ----------
var existingTransfers = (await db.AssetTransfers.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && t.LegacyRef != null).Select(t => t.LegacyRef!).ToListAsync()).ToHashSet(ci);
foreach (var group in data.Transfers.GroupBy(t => K(t.AssetNo), ci))
{
    if (!assetByNumber.TryGetValue(group.Key, out var asset)) continue;
    (Guid? site, Guid? loc) previous = (null, null);
    foreach (var t in group.OrderBy(t => t.TransferDate).ThenBy(t => t.TransferId))
    {
        var toLoc = locByCode.GetValueOrDefault(K(t.LocationNo));
        var toSite = toLoc is not null ? toLoc.SiteId : SiteFor(t.SiteCode).Id;
        var key = $"transfer:{t.TransferId}";
        if (!existingTransfers.Contains(key))
        {
            db.AssetTransfers.Add(new AssetTransfer
            {
                TenantId = tenantId, AssetId = asset.Id, FromSiteId = previous.site, FromLocationId = previous.loc, ToSiteId = toSite, ToLocationId = toLoc?.Id,
                Reason = Trunc(string.Join(" · ", new[] { t.TransferType, t.Remarks }.Where(x => !string.IsNullOrWhiteSpace(x))), 1000),
                TransferredAt = t.TransferDate is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : DateTimeOffset.UtcNow, LegacyRef = key,
            });
            report.Created("Transfer");
        }
        previous = (toSite, toLoc?.Id);
    }
}
await db.SaveChangesAsync();

Console.WriteLine();
report.Print();
if (tx is not null) { await tx.RollbackAsync(); Console.WriteLine("\nDry run: all changes were rolled back."); }
Console.WriteLine($"Finished in {sw.Elapsed.TotalSeconds:0.0}s");
return 0;

static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
static string? FirstLine(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Split('\n')[0].Trim();
static DateOnly? ToDate(DateTime? d) => d is { Year: > 1900 } v ? DateOnly.FromDateTime(v) : null;
static decimal? Money(double? v) => v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value) || Math.Abs(v.Value) > 1e12 ? null : Math.Round((decimal)v.Value, 2);
static double? ParseDouble(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
static T Clone<T>(T v) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(v))!;
static AssetStatus MapStatus(string? s) => s switch
{
    null or "" => AssetStatus.Active,
    var x when x.Contains("repair") => AssetStatus.UnderRepair,
    var x when x.Contains("storage") => AssetStatus.InStorage,
    var x when x.Contains("dispos") => AssetStatus.Disposed,
    var x when x.Contains("in-active") || x.Contains("inactive") => AssetStatus.Inactive,
    _ => AssetStatus.Active,
};
static Criticality MapCriticality(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
{
    "critical" => Criticality.Critical, "high" => Criticality.High, "low" => Criticality.Low, _ => Criticality.Medium,
};

sealed class Report
{
    private readonly SortedDictionary<string, (int Created, int Updated)> _counts = new();
    private readonly List<string> _warnings = [];
    public void Created(string kind) { var c = _counts.GetValueOrDefault(kind); _counts[kind] = (c.Created + 1, c.Updated); }
    public void Updated(string kind) { var c = _counts.GetValueOrDefault(kind); _counts[kind] = (c.Created, c.Updated + 1); }
    public void Warn(string message) => _warnings.Add(message);
    public void Print()
    {
        Console.WriteLine($"{"Record",-16}{"Created",10}{"Updated",10}");
        foreach (var (k, v) in _counts) Console.WriteLine($"{k,-16}{v.Created,10}{v.Updated,10}");
        if (_warnings.Count == 0) return;
        Console.WriteLine($"\n{_warnings.Count} warning(s):");
        foreach (var w in _warnings.Take(50)) Console.WriteLine($"  - {w}");
        if (_warnings.Count > 50) Console.WriteLine($"  … and {_warnings.Count - 50} more");
    }
}

sealed record Options(string Source, string Target, string? Tenant, string? CreateTenant, string? AdminEmail, string? AdminName, string? AdminPassword, bool DryRun)
{
    public static Options? Parse(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dry = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dry-run") { dry = true; continue; }
            if (args[i].StartsWith("--") && i + 1 < args.Length) d[args[i][2..]] = args[++i];
        }
        string? Get(string k) => d.GetValueOrDefault(k) ?? Environment.GetEnvironmentVariable("CMMS_IMPORT_" + k.Replace('-', '_').ToUpperInvariant());
        var o = new Options(Get("source") ?? "", Get("target") ?? "", Get("tenant"), Get("create-tenant"), Get("admin-email"), Get("admin-name"), Get("admin-password"), dry);
        if (o.Source == "" || o.Target == "" || (o.Tenant is null && o.CreateTenant is null)) return null;
        if (o.CreateTenant is not null && (o.AdminEmail is null || o.AdminPassword is null || dry)) return null;
        return o;
    }

    public static void PrintUsage() => Console.WriteLine("""
        Usage:
          LegacyImport --source <4CSQL connection string> --target <CMMS connection string> --tenant <slug|id> [--dry-run]
          LegacyImport --source ... --target ... --create-tenant "Org name" --admin-email a@b.com --admin-password ... [--admin-name "Name"]
        Values can also be supplied as environment variables: CMMS_IMPORT_SOURCE, CMMS_IMPORT_TARGET, CMMS_IMPORT_ADMIN_PASSWORD, ...
        """);
}
