using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AssetEndpoints
{
    public sealed record AssetInput(
        string? AssetNumber, string Name, string? Description, Guid? CategoryId, Guid SiteId, Guid? LocationId, Guid? ParentAssetId,
        AssetStatus Status, Criticality Criticality, string? Manufacturer, string? Model, string? SerialNumber, string? Barcode, string? OperationNumber,
        DateOnly? CommissionedOn, DateOnly? DisposedOn, decimal? PurchasePrice, decimal? CurrentValue, decimal? SalvageValue,
        decimal? ExpectedLifeYears, decimal? DowntimeCostPerHour, WarrantyType WarrantyType, DateOnly? WarrantyExpiresOn, string? WarrantyNotes,
        string? Notes, string? NotesToTechnician, Dictionary<string, string?>? CustomFields, string? RowVersion = null);

    public sealed record TransferInput(Guid SiteId, Guid? LocationId, string? Reason, bool IncludeChildren = true);
    public sealed record BulkInput(Guid[] Ids, AssetStatus? Status, Criticality? Criticality, Guid? CategoryId);

    public sealed class AssetFilter
    {
        public string? Search { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? LocationId { get; set; }
        public bool? IncludeSubLocations { get; set; }
        public Guid? CategoryId { get; set; }
        public string? Status { get; set; }
        public string? Criticality { get; set; }
        public Guid? ParentAssetId { get; set; }
        public bool? TopLevelOnly { get; set; }
        public int? WarrantyExpiringDays { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    private static readonly Dictionary<string, Expression<Func<Asset, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assetNumber"] = a => a.AssetNumber, ["name"] = a => a.Name, ["category"] = a => a.Category!.Name, ["site"] = a => a.Site!.Name,
        ["location"] = a => a.Location!.Name, ["status"] = a => a.Status, ["criticality"] = a => a.Criticality, ["manufacturer"] = a => a.Manufacturer,
        ["model"] = a => a.Model, ["serialNumber"] = a => a.SerialNumber, ["commissionedOn"] = a => a.CommissionedOn,
        ["warrantyExpiresOn"] = a => a.WarrantyExpiresOn, ["createdAt"] = a => a.CreatedAt, ["updatedAt"] = a => a.UpdatedAt,
    };

    public static void MapAssetEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/assets").WithTags("Assets");

        g.MapGet("/", async ([AsParameters] AssetFilter f, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = await FilterAsync(db, f, ct);
            var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "assetNumber").Select(ListProjection).ToPagedAsync(f.Page ?? 1, f.PageSize ?? 25, ct);
            var names = await LocationEndpoints.ResolvePathNamesAsync(db, paged.Items.Select(i => i.LocationPath), ct);
            return new PagedResult<object>(paged.Items.Select(i => (object)(i with { LocationPath = names(i.LocationPath) })).ToList(), paged.Total, paged.Page, paged.PageSize);
        }).RequirePermission(Permissions.AssetsView);

        g.MapGet("/lookup", async (CmmsDbContext db, string? search, Guid? excludeId, CancellationToken ct) =>
        {
            var q = db.Assets.AsNoTracking();
            if (search.Clean() is { } s) q = q.Where(a => a.AssetNumber.Contains(s) || a.Name.Contains(s));
            if (excludeId is not null) q = q.Where(a => a.Id != excludeId);
            return await q.OrderBy(a => a.AssetNumber).Take(30).Select(a => new { a.Id, a.AssetNumber, a.Name, a.SiteId, a.LocationId }).ToListAsync(ct);
        }).RequirePermission(Permissions.AssetsView);

        g.MapGet("/export", async ([AsParameters] AssetFilter f, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = await FilterAsync(db, f, ct);
            var rows = await q.SortBy(f.Sort, f.Dir, SortMap, "assetNumber").Take(50_000).Select(ListProjection).ToListAsync(ct);
            var names = await LocationEndpoints.ResolvePathNamesAsync(db, rows.Select(i => i.LocationPath), ct);
            var fields = await db.CustomFields.AsNoTracking().Where(c => c.EntityType == "Asset").OrderBy(c => c.SortOrder).ToListAsync(ct);
            var sb = new StringBuilder();
            string[] header = ["Asset Number", "Name", "Category", "Site", "Location", "Parent Asset", "Status", "Criticality", "Manufacturer", "Model", "Serial Number", "Commissioned On", "Warranty Expires On", .. fields.Select(c => c.Label)];
            sb.AppendLine(string.Join(",", header.Select(Csv)));
            foreach (var r in rows)
            {
                string?[] cells = [r.AssetNumber, r.Name, r.CategoryName, r.SiteName, names(r.LocationPath), r.ParentAssetNumber, r.Status.ToString(), r.Criticality.ToString(),
                    r.Manufacturer, r.Model, r.SerialNumber, r.CommissionedOn?.ToString("yyyy-MM-dd"), r.WarrantyExpiresOn?.ToString("yyyy-MM-dd"),
                    .. fields.Select(c => r.CustomFields.GetValueOrDefault(c.Key))];
                sb.AppendLine(string.Join(",", cells.Select(Csv)));
            }
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"assets-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }).RequirePermission(Permissions.AssetsView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var a = await db.Assets.AsNoTracking().Include(x => x.Category).Include(x => x.Site).Include(x => x.Location).Include(x => x.ParentAsset)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var locationTrail = new List<object>();
            if (a.Location is not null)
            {
                var ids = LocationEndpoints.PathIds(a.Location.Path).ToList();
                var locs = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.Code, l.Name, l.Depth }).ToListAsync(ct);
                locationTrail.AddRange(locs.OrderBy(l => l.Depth).Select(l => new { l.Id, l.Code, l.Name }));
            }
            var userIds = new[] { a.CreatedById, a.UpdatedById }.Where(x => x is not null).ToList();
            var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            var image = await db.Attachments.AsNoTracking().Where(x => x.EntityType == "Asset" && x.EntityId == id && x.ContentType.StartsWith("image/"))
                .OrderByDescending(x => x.IsPrimaryImage).ThenBy(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                a.Id, a.AssetNumber, a.Name, a.Description, a.CategoryId, Category = a.Category is null ? null : new { a.Category.Id, a.Category.Code, a.Category.Name, a.Category.Color },
                a.SiteId, Site = new { a.Site!.Id, a.Site.Code, a.Site.Name }, a.LocationId, LocationTrail = locationTrail,
                a.ParentAssetId, ParentAsset = a.ParentAsset is null ? null : new { a.ParentAsset.Id, a.ParentAsset.AssetNumber, a.ParentAsset.Name },
                a.Status, a.Criticality, a.Manufacturer, a.Model, a.SerialNumber, a.Barcode, a.OperationNumber,
                a.CommissionedOn, a.DisposedOn, a.PurchasePrice, a.CurrentValue, a.SalvageValue, a.ExpectedLifeYears, a.DowntimeCostPerHour,
                a.WarrantyType, a.WarrantyExpiresOn, a.WarrantyNotes, a.Notes, a.NotesToTechnician, a.CustomFields,
                ChildCount = await db.Assets.CountAsync(x => x.ParentAssetId == id, ct),
                PrimaryImageId = image,
                a.CreatedAt, CreatedBy = a.CreatedById is { } c ? users.GetValueOrDefault(c) : null,
                a.UpdatedAt, UpdatedBy = a.UpdatedById is { } u ? users.GetValueOrDefault(u) : null,
                a.LegacyRef, RowVersion = Convert.ToBase64String(a.RowVersion),
            });
        }).RequirePermission(Permissions.AssetsView);

        g.MapGet("/{id:guid}/children", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.Assets.AsNoTracking().Where(a => a.ParentAssetId == id).OrderBy(a => a.AssetNumber).Select(ListProjection).ToListAsync(ct))
            .RequirePermission(Permissions.AssetsView);

        g.MapGet("/{id:guid}/transfers", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var rows = await db.AssetTransfers.AsNoTracking().Where(t => t.AssetId == id).OrderByDescending(t => t.TransferredAt).ToListAsync(ct);
            var siteIds = rows.SelectMany(r => new[] { r.FromSiteId, (Guid?)r.ToSiteId }).OfType<Guid>().Distinct().ToList();
            var locIds = rows.SelectMany(r => new[] { r.FromLocationId, r.ToLocationId }).OfType<Guid>().Distinct().ToList();
            var userIds = rows.Select(r => r.TransferredById).OfType<Guid>().Distinct().ToList();
            var sites = await db.Sites.IgnoreQueryFilters().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var locs = await db.Locations.IgnoreQueryFilters().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            return rows.Select(r => new
            {
                r.Id, r.TransferredAt, r.Reason,
                FromSite = r.FromSiteId is { } fs ? sites.GetValueOrDefault(fs) : null, FromLocation = r.FromLocationId is { } fl ? locs.GetValueOrDefault(fl) : null,
                ToSite = sites.GetValueOrDefault(r.ToSiteId), ToLocation = r.ToLocationId is { } tl ? locs.GetValueOrDefault(tl) : null,
                TransferredBy = r.TransferredById is { } tb ? users.GetValueOrDefault(tb) : null,
            });
        }).RequirePermission(Permissions.AssetsView);

        g.MapPost("/", async (AssetInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
            var auto = string.IsNullOrWhiteSpace(input.AssetNumber) && tenant.Settings.AutoNumberAssets;
            var (v, custom) = await ValidateAsync(input, null, db, requireNumber: !auto, ct);
            if (!v.IsValid) return v.Problem();
            var number = auto ? await NextAssetNumberAsync(db, tenant.Settings, ct) : input.AssetNumber!.Trim();
            var asset = new Asset { AssetNumber = number, Name = input.Name.Trim(), SiteId = input.SiteId };
            Apply(asset, input, custom);
            db.Assets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/assets/{asset.Id}", new { asset.Id, asset.AssetNumber });
        }).RequirePermission(Permissions.AssetsCreate);

        g.MapPut("/{id:guid}", async (Guid id, AssetInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            var (v, custom) = await ValidateAsync(input, id, db, requireNumber: true, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(asset).Property(a => a.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            if (asset.SiteId != input.SiteId || asset.LocationId != input.LocationId)
                db.AssetTransfers.Add(NewTransfer(asset, input.SiteId, input.LocationId, "Location changed while editing the asset", ctx));
            asset.AssetNumber = input.AssetNumber!.Trim(); asset.Name = input.Name.Trim(); asset.SiteId = input.SiteId;
            Apply(asset, input, custom);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { asset.Id, RowVersion = Convert.ToBase64String(asset.RowVersion) });
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            if (await db.Assets.AnyAsync(a => a.ParentAssetId == id, ct))
                throw new DomainException("This asset has child assets. Re-assign or delete them first.");
            db.Assets.Remove(asset);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AssetsDelete);

        g.MapPost("/{id:guid}/transfer", async (Guid id, TransferInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            var v = new Validator().MaxLength("reason", input.Reason, "Reason", 1000);
            if (!await db.Sites.AnyAsync(s => s.Id == input.SiteId, ct)) v.Add("siteId", "Choose a site.");
            if (input.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == input.LocationId && l.SiteId == input.SiteId, ct))
                v.Add("locationId", "Location must belong to the selected site.");
            if (asset.SiteId == input.SiteId && asset.LocationId == input.LocationId) v.Add("locationId", "The asset is already at this location.");
            if (!v.IsValid) return v.Problem();

            var moving = new List<Asset> { asset };
            if (input.IncludeChildren)
            {
                var frontier = new List<Guid> { id };
                for (var depth = 0; depth < 10 && frontier.Count > 0; depth++)
                {
                    var kids = await db.Assets.Where(a => a.ParentAssetId != null && frontier.Contains(a.ParentAssetId.Value)).ToListAsync(ct);
                    moving.AddRange(kids);
                    frontier = kids.Select(k => k.Id).ToList();
                }
            }
            foreach (var a in moving)
            {
                db.AssetTransfers.Add(NewTransfer(a, input.SiteId, input.LocationId, input.Reason.Clean(), ctx));
                a.SiteId = input.SiteId; a.LocationId = input.LocationId;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { moved = moving.Count });
        }).RequirePermission(Permissions.AssetsTransfer);

        g.MapPost("/{id:guid}/duplicate", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var src = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (src is null) return Results.NotFound();
            var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
            var copy = new Asset
            {
                AssetNumber = await NextAssetNumberAsync(db, tenant.Settings, ct), Name = src.Name, Description = src.Description, CategoryId = src.CategoryId,
                SiteId = src.SiteId, LocationId = src.LocationId, ParentAssetId = src.ParentAssetId, Status = src.Status, Criticality = src.Criticality,
                Manufacturer = src.Manufacturer, Model = src.Model, OperationNumber = src.OperationNumber, PurchasePrice = src.PurchasePrice,
                ExpectedLifeYears = src.ExpectedLifeYears, DowntimeCostPerHour = src.DowntimeCostPerHour, WarrantyType = src.WarrantyType,
                Notes = src.Notes, NotesToTechnician = src.NotesToTechnician, CustomFields = new(src.CustomFields),
            };
            db.Assets.Add(copy);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { copy.Id, copy.AssetNumber });
        }).RequirePermission(Permissions.AssetsCreate);

        g.MapPost("/bulk", async (BulkInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            if (input.Ids is not { Length: > 0 and <= 1000 }) throw new DomainException("Select between 1 and 1000 assets.");
            if (input.CategoryId is not null && !await db.AssetCategories.AnyAsync(c => c.Id == input.CategoryId, ct)) throw new DomainException("Category not found.");
            var assets = await db.Assets.Where(a => input.Ids.Contains(a.Id)).ToListAsync(ct);
            foreach (var a in assets)
            {
                if (input.Status is { } s) a.Status = s;
                if (input.Criticality is { } c) a.Criticality = c;
                if (input.CategoryId is { } cat) a.CategoryId = cat;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = assets.Count });
        }).RequirePermission(Permissions.AssetsEdit);
    }

    public sealed record AssetListItem(Guid Id, string AssetNumber, string Name, Guid? CategoryId, string? CategoryName, string? CategoryColor,
        Guid SiteId, string SiteName, Guid? LocationId, string? LocationName, string? LocationPath, Guid? ParentAssetId, string? ParentAssetNumber,
        AssetStatus Status, Criticality Criticality, string? Manufacturer, string? Model, string? SerialNumber, DateOnly? CommissionedOn,
        DateOnly? WarrantyExpiresOn, Dictionary<string, string?> CustomFields, DateTimeOffset? UpdatedAt);

    private static readonly Expression<Func<Asset, AssetListItem>> ListProjection = a => new AssetListItem(
        a.Id, a.AssetNumber, a.Name, a.CategoryId, a.Category!.Name, a.Category.Color, a.SiteId, a.Site!.Name, a.LocationId, a.Location!.Name,
        a.Location.Path, a.ParentAssetId, a.ParentAsset!.AssetNumber, a.Status, a.Criticality, a.Manufacturer, a.Model, a.SerialNumber,
        a.CommissionedOn, a.WarrantyExpiresOn, a.CustomFields, a.UpdatedAt ?? a.CreatedAt);

    private static async Task<IQueryable<Asset>> FilterAsync(CmmsDbContext db, AssetFilter f, CancellationToken ct)
    {
        var q = db.Assets.AsNoTracking();
        if (f.Search.Clean() is { } s)
            q = q.Where(a => a.AssetNumber.Contains(s) || a.Name.Contains(s) || a.SerialNumber!.Contains(s) || a.Barcode!.Contains(s)
                             || a.Model!.Contains(s) || a.Manufacturer!.Contains(s) || a.OperationNumber!.Contains(s));
        if (f.SiteId is not null) q = q.Where(a => a.SiteId == f.SiteId);
        if (f.LocationId is not null)
        {
            if (f.IncludeSubLocations != false)
            {
                var path = await db.Locations.Where(l => l.Id == f.LocationId).Select(l => l.Path).FirstOrDefaultAsync(ct) ?? "/none/";
                q = q.Where(a => a.LocationId != null && db.Locations.Any(l => l.Id == a.LocationId && l.Path.StartsWith(path)));
            }
            else q = q.Where(a => a.LocationId == f.LocationId);
        }
        if (f.CategoryId is not null) q = q.Where(a => a.CategoryId == f.CategoryId);
        if (ParseEnums<AssetStatus>(f.Status) is { Count: > 0 } st) q = q.Where(a => st.Contains(a.Status));
        if (ParseEnums<Criticality>(f.Criticality) is { Count: > 0 } cr) q = q.Where(a => cr.Contains(a.Criticality));
        if (f.ParentAssetId is not null) q = q.Where(a => a.ParentAssetId == f.ParentAssetId);
        if (f.TopLevelOnly == true) q = q.Where(a => a.ParentAssetId == null);
        if (f.WarrantyExpiringDays is { } days)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var until = today.AddDays(days);
            q = q.Where(a => a.WarrantyExpiresOn != null && a.WarrantyExpiresOn >= today && a.WarrantyExpiresOn <= until);
        }
        return q;
    }

    private static List<T> ParseEnums<T>(string? csv) where T : struct, Enum =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<T>(x, true, out var e) ? (T?)e : null).OfType<T>().ToList();

    private static async Task<(Validator, Dictionary<string, string?>)> ValidateAsync(AssetInput i, Guid? id, CmmsDbContext db, bool requireNumber, CancellationToken ct)
    {
        var v = new Validator().Required("name", i.Name, "Name", 300)
            .MaxLength("manufacturer", i.Manufacturer, "Manufacturer", 200).MaxLength("model", i.Model, "Model", 200)
            .MaxLength("serialNumber", i.SerialNumber, "Serial number", 200).MaxLength("barcode", i.Barcode, "Barcode", 200)
            .MaxLength("operationNumber", i.OperationNumber, "Operation number", 50)
            .When(!Enum.IsDefined(i.Status), "status", "Choose a valid status.")
            .When(!Enum.IsDefined(i.Criticality), "criticality", "Choose a valid criticality.")
            .When(i.PurchasePrice < 0 || i.CurrentValue < 0 || i.SalvageValue < 0, "purchasePrice", "Amounts cannot be negative.")
            .When(i.DisposedOn is not null && i.CommissionedOn is not null && i.DisposedOn < i.CommissionedOn, "disposedOn", "Disposal date cannot be before the commissioning date.");
        if (requireNumber) v.Required("assetNumber", i.AssetNumber, "Asset number", 100);
        if (!string.IsNullOrWhiteSpace(i.AssetNumber) && await db.Assets.AnyAsync(a => a.AssetNumber == i.AssetNumber.Trim() && a.Id != id, ct))
            v.Add("assetNumber", "Another asset already uses this number.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct))
            v.Add("locationId", "Location must belong to the selected site.");
        if (i.CategoryId is not null && !await db.AssetCategories.AnyAsync(c => c.Id == i.CategoryId, ct)) v.Add("categoryId", "Category not found.");
        if (i.ParentAssetId is not null)
        {
            if (i.ParentAssetId == id) v.Add("parentAssetId", "An asset cannot be its own parent.");
            else
            {
                // Walk up from the proposed parent; reaching this asset would create a cycle.
                Guid? cursor = i.ParentAssetId;
                for (var depth = 0; cursor is not null && depth < 20; depth++)
                {
                    var next = await db.Assets.Where(a => a.Id == cursor).Select(a => new { a.Id, a.ParentAssetId }).FirstOrDefaultAsync(ct);
                    if (next is null) { v.Add("parentAssetId", "Parent asset not found."); break; }
                    if (id is not null && next.ParentAssetId == id) { v.Add("parentAssetId", "The parent cannot be one of this asset's own children."); break; }
                    cursor = next.ParentAssetId;
                }
            }
        }

        // Custom fields: keep only defined keys, enforce required + type rules.
        var custom = new Dictionary<string, string?>();
        var defs = await db.CustomFields.AsNoTracking().Where(c => c.EntityType == "Asset").ToListAsync(ct);
        foreach (var d in defs.Where(d => d.CategoryIds.Count == 0 || (i.CategoryId is { } cat && d.CategoryIds.Contains(cat))))
        {
            var raw = i.CustomFields?.GetValueOrDefault(d.Key).Clean();
            var key = $"customFields.{d.Key}";
            if (raw is null) { if (d.IsRequired) v.Add(key, $"{d.Label} is required."); continue; }
            var ok = d.DataType switch
            {
                CustomFieldType.Number => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
                CustomFieldType.Date => DateOnly.TryParseExact(raw, "yyyy-MM-dd", out _),
                CustomFieldType.Boolean => raw is "true" or "false",
                CustomFieldType.Select => d.Options.Contains(raw),
                _ => raw.Length <= 4000,
            };
            if (!ok) v.Add(key, $"{d.Label} has an invalid value.");
            custom[d.Key] = raw;
        }
        return (v, custom);
    }

    private static void Apply(Asset a, AssetInput i, Dictionary<string, string?> custom)
    {
        a.Description = i.Description.Clean(); a.CategoryId = i.CategoryId; a.LocationId = i.LocationId; a.ParentAssetId = i.ParentAssetId;
        a.Status = i.Status; a.Criticality = i.Criticality; a.Manufacturer = i.Manufacturer.Clean(); a.Model = i.Model.Clean();
        a.SerialNumber = i.SerialNumber.Clean(); a.Barcode = i.Barcode.Clean(); a.OperationNumber = i.OperationNumber.Clean();
        a.CommissionedOn = i.CommissionedOn; a.DisposedOn = i.DisposedOn; a.PurchasePrice = i.PurchasePrice; a.CurrentValue = i.CurrentValue;
        a.SalvageValue = i.SalvageValue; a.ExpectedLifeYears = i.ExpectedLifeYears; a.DowntimeCostPerHour = i.DowntimeCostPerHour;
        a.WarrantyType = i.WarrantyType; a.WarrantyExpiresOn = i.WarrantyExpiresOn; a.WarrantyNotes = i.WarrantyNotes.Clean();
        a.Notes = i.Notes.Clean(); a.NotesToTechnician = i.NotesToTechnician.Clean();
        // Preserve values of fields that don't apply to the current category (e.g. imported legacy data).
        foreach (var kv in custom) a.CustomFields[kv.Key] = kv.Value;
        foreach (var k in a.CustomFields.Keys.Where(k => i.CustomFields?.ContainsKey(k) == true && !custom.ContainsKey(k)).ToList())
            if (i.CustomFields![k].Clean() is null) a.CustomFields.Remove(k);
        a.CustomFields = new Dictionary<string, string?>(a.CustomFields);
    }

    private static AssetTransfer NewTransfer(Asset a, Guid toSite, Guid? toLocation, string? reason, ITenantContext ctx) => new()
    {
        TenantId = a.TenantId, AssetId = a.Id, FromSiteId = a.SiteId, FromLocationId = a.LocationId, ToSiteId = toSite, ToLocationId = toLocation,
        Reason = reason, TransferredAt = DateTimeOffset.UtcNow, TransferredById = ctx.UserId,
    };

    public static async Task<string> NextAssetNumberAsync(CmmsDbContext db, FourC.Cmms.Core.Tenancy.TenantSettings settings, CancellationToken ct)
    {
        var prefix = settings.AssetNumberPrefix ?? "";
        var format = new string('0', Math.Clamp(settings.AssetNumberPadding, 1, 12));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = prefix + (await db.NextNumberAsync("Asset", ct)).ToString(format);
            if (!await db.Assets.AnyAsync(a => a.AssetNumber == number, ct)) return number;
            // Collision (e.g. numbers entered manually or imported): jump past the highest existing number with this prefix.
            var existing = await db.Assets.Where(a => a.AssetNumber.StartsWith(prefix)).Select(a => a.AssetNumber).ToListAsync(ct);
            var max = existing.Select(n => long.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
            await db.SetSequenceAtLeastAsync("Asset", max + 1, ct);
        }
        throw new DomainException("Could not generate a unique asset number. Check the numbering settings.");
    }

    private static string Csv(string? s) =>
        s is null ? "" : s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
