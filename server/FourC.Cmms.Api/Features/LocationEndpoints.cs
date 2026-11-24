using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class SiteEndpoints
{
    public sealed record SiteInput(string Code, string Name, string? Address, string? City, string? Country, string? TimeZone,
        double? Latitude, double? Longitude, string? Notes, bool IsActive = true, string? RowVersion = null, SiteSettings? Settings = null);

    public static void MapSiteEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/sites").WithTags("Sites");

        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var locCounts = await db.Locations.GroupBy(l => l.SiteId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            // Spare serial units (0.24.0) aren't counted as the site's assets, as on the asset list.
            var assetCounts = await db.Assets.Where(AssetEndpoints.NotSpareUnit).GroupBy(a => a.SiteId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
            return sites.Select(s => ToDto(s, locCounts.GetValueOrDefault(s.Id), assetCounts.GetValueOrDefault(s.Id)));
        }).RequirePermission(Permissions.LocationsView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) is { } s
                ? Results.Ok(ToDto(s, await db.Locations.CountAsync(l => l.SiteId == id, ct), await db.Assets.Where(AssetEndpoints.NotSpareUnit).CountAsync(a => a.SiteId == id, ct)))
                : Results.NotFound()).RequirePermission(Permissions.LocationsView);

        g.MapPost("/", async (SiteInput input, CmmsDbContext db, AccessService access, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct, access);
            if (db.CurrentSiteIds is not null) v.Add("code", OnlyEveryoneAddsSites); // they couldn't see the new site
            if (!v.IsValid) return v.Problem();
            var site = CreateSite(db, input);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sites/{site.Id}", ToDto(site, 0, 0));
        }).RequirePermission(Permissions.LocationsManage);

        g.MapPut("/{id:guid}", async (Guid id, SiteInput input, CmmsDbContext db, AccessService access, CancellationToken ct) =>
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (site is null) return Results.NotFound();
            if (db.CurrentBranchPaths is not null) throw new DomainException(OnlyWholeSiteChangesIt);
            var v = await ValidateAsync(input, id, db, ct, access);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(site).Property(s => s.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            UpdateSite(site, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(site, 0, 0));
        }).RequirePermission(Permissions.LocationsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (site is null) return Results.NotFound();
            if (db.CurrentBranchPaths is not null) throw new DomainException(OnlyWholeSiteChangesIt);
            if (await db.Locations.AnyAsync(l => l.SiteId == id, ct) || await db.Assets.AnyAsync(a => a.SiteId == id, ct))
                throw new DomainException("This site still has locations or assets. Move or delete them first, or mark the site inactive.");
            db.Sites.Remove(site);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.LocationsManage);
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal const string OnlyEveryoneAddsSites = "Only people who see every site can add sites.";
    /// <summary>People limited to a branch see only part of the site: its record and settings are for those who see all of it.</summary>
    internal const string OnlyWholeSiteChangesIt = "Only people who see the whole site can change it.";

    internal static Validator SiteRules(SiteInput i) => new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200)
        .MaxLength("address", i.Address, "Address", 500).MaxLength("city", i.City, "City", 100).MaxLength("country", i.Country, "Country", 100)
        .When(i.Latitude is < -90 or > 90, "latitude", "Latitude must be between -90 and 90.")
        .When(i.Longitude is < -180 or > 180, "longitude", "Longitude must be between -180 and 180.");

    internal static Site CreateSite(CmmsDbContext db, SiteInput i)
    {
        var site = new Site { Code = i.Code.Trim(), Name = i.Name.Trim() };
        Apply(site, i);
        if (i.Settings is not null) site.Settings = Clean(i.Settings);
        db.Sites.Add(site);
        return site;
    }

    internal static void UpdateSite(Site site, SiteInput i)
    {
        site.Code = i.Code.Trim(); site.Name = i.Name.Trim();
        Apply(site, i);
        if (i.Settings is not null) site.Settings = Clean(i.Settings); // null (older apps, the import) keeps them
    }

    private static SiteSettings Clean(SiteSettings s) => new()
    {
        AssetNumberPrefix = s.AssetNumberPrefix.Clean(), WorkOrderNumberPrefix = s.WorkOrderNumberPrefix.Clean(),
        WorkRequestNumberPrefix = s.WorkRequestNumberPrefix.Clean(), PurchaseOrderNumberPrefix = s.PurchaseOrderNumberPrefix.Clean(),
        PurchaseRequestNumberPrefix = s.PurchaseRequestNumberPrefix.Clean(), PurchaseRequestRecipientIds = (s.PurchaseRequestRecipientIds ?? []).Distinct().ToList(),
        WorkingHours = s.WorkingHours is { Count: > 0 } h ? h.OrderBy(d => d.Day).ToList() : null, DefaultStoreId = s.DefaultStoreId,
        RequestRecipientIds = (s.RequestRecipientIds ?? []).Distinct().ToList(), EmailSenderName = s.EmailSenderName.Clean(),
    };

    private static async Task<Validator> ValidateAsync(SiteInput i, Guid? id, CmmsDbContext db, CancellationToken ct, AccessService? access = null)
    {
        var v = SiteRules(i);
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.Sites, s => s.Code == i.Code.Trim() && s.Id != id,
                "Another site already uses this code.", "This code is already used by a site you can't see.", ct) is { } siteClash)
            v.Add("code", siteClash);
        if (i.Settings is { } st)
        {
            v.MaxLength("settings.assetNumberPrefix", st.AssetNumberPrefix, "Prefix", 20).MaxLength("settings.workOrderNumberPrefix", st.WorkOrderNumberPrefix, "Prefix", 20)
                .MaxLength("settings.workRequestNumberPrefix", st.WorkRequestNumberPrefix, "Prefix", 20)
                .MaxLength("settings.purchaseOrderNumberPrefix", st.PurchaseOrderNumberPrefix, "Prefix", 20)
                .MaxLength("settings.purchaseRequestNumberPrefix", st.PurchaseRequestNumberPrefix, "Prefix", 20)
                .MaxLength("settings.emailSenderName", st.EmailSenderName, "Sender name", 100);
            if (WorkingHours.Problem(st.WorkingHours) is { } hours) v.Add("settings.workingHours", hours);
            if (st.DefaultStoreId is { } store && (id is null || !await db.Stores.AnyAsync(s => s.Id == store && s.SiteId == id && s.IsActive, ct)))
                v.Add("settings.defaultStoreId", "Choose one of this site's stores.");
            var buyers = (st.PurchaseRequestRecipientIds ?? []).Distinct().ToList();
            if (buyers.Count > 0 && await db.Users.People().CountAsync(u => buyers.Contains(u.Id) && u.TenantId == db.CurrentTenantId && u.IsActive, ct) != buyers.Count)
                v.Add("settings.purchaseRequestRecipientIds", "Choose active users.");
            var who = (st.RequestRecipientIds ?? []).Distinct().ToList();
            if (who.Count > 0 && await db.Users.People().CountAsync(u => who.Contains(u.Id) && u.TenantId == db.CurrentTenantId && u.IsActive, ct) != who.Count)
                v.Add("settings.requestRecipientIds", "Choose active users.");
            else if (who.Count > 0 && access is not null)
            {
                // Recipients who can't see the site would never get the e-mail (it only goes to people who can).
                // ...and must see all of it: someone limited to part of the site would miss requests elsewhere in it.
                foreach (var person in who)
                {
                    var theirs = await access.GetAsync(person, ct);
                    if (!Access.CoversSite(theirs, id ?? Guid.Empty) || theirs.Branches is not null)
                    {
                        v.Add("settings.requestRecipientIds", "Choose people who can see all of this site."); break;
                    }
                }
            }
        }
        return v;
    }

    internal static void Apply(Site s, SiteInput i)
    {
        s.Address = i.Address.Clean(); s.City = i.City.Clean(); s.Country = i.Country.Clean(); s.TimeZone = i.TimeZone.Clean();
        s.Latitude = i.Latitude; s.Longitude = i.Longitude; s.Notes = i.Notes.Clean(); s.IsActive = i.IsActive;
    }

    private static object ToDto(Site s, int locations, int assets) => new
    {
        s.Id, s.Code, s.Name, s.Address, s.City, s.Country, s.TimeZone, s.Latitude, s.Longitude, s.Notes, s.IsActive, s.Settings,
        LocationCount = locations, AssetCount = assets, RowVersion = Convert.ToBase64String(s.RowVersion),
    };
}

public static class LocationEndpoints
{
    public sealed record LocationInput(Guid SiteId, Guid? ParentId, string Code, string Name, LocationType Type, Criticality Criticality,
        string? Description, string? NotesToTechnician, double? Latitude, double? Longitude, bool IsActive = true, string? RowVersion = null,
        Guid? CostCentreId = null, bool? ManagesCostCentre = null);

    public sealed record LocationListItem(Guid Id, Guid SiteId, string SiteName, Guid? ParentId, string Code, string Name, LocationType Type,
        Criticality Criticality, bool IsActive, string PathName);

    public static void MapLocationEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/locations").WithTags("Locations");

        // Whole hierarchy in one call; the client builds and searches the tree.
        g.MapGet("/hierarchy", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var assetCounts = await db.Assets.Where(a => a.LocationId != null).GroupBy(a => a.LocationId!.Value)
                .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var siteAssetCounts = await db.Assets.Where(a => a.LocationId == null).Where(AssetEndpoints.NotSpareUnit).GroupBy(a => a.SiteId)
                .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Code, s.Name, s.IsActive }).ToListAsync(ct);
            var locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name)
                .Select(l => new { l.Id, l.SiteId, l.ParentId, l.Code, l.Name, l.Type, l.Criticality, l.IsActive }).ToListAsync(ct);
            return new
            {
                sites = sites.Select(s => new { s.Id, s.Code, s.Name, s.IsActive, AssetCount = siteAssetCounts.GetValueOrDefault(s.Id) }),
                locations = locations.Select(l => new { l.Id, l.SiteId, l.ParentId, l.Code, l.Name, l.Type, l.Criticality, l.IsActive, AssetCount = assetCounts.GetValueOrDefault(l.Id) }),
            };
        }).RequirePermission(Permissions.LocationsView);

        g.MapGet("/", async (CmmsDbContext db, string? search, Guid? siteId, Guid? parentId, bool? activeOnly, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            var q = db.Locations.AsNoTracking();
            if (siteId is not null) q = q.Where(l => l.SiteId == siteId);
            if (parentId is not null) q = q.Where(l => l.ParentId == parentId);
            if (activeOnly == true) q = q.Where(l => l.IsActive);
            if (search.Clean() is { } s) q = q.Where(l => l.Name.Contains(s) || l.Code.Contains(s));
            var paged = await q.OrderBy(l => l.Name).Select(l => new { l.Id, l.SiteId, SiteName = l.Site!.Name, l.ParentId, l.Code, l.Name, l.Type, l.Criticality, l.IsActive, l.Path })
                .ToPagedAsync(page, pageSize, ct);
            var names = await ResolvePathNamesAsync(db, paged.Items.Select(i => i.Path), ct);
            return new PagedResult<LocationListItem>(
                paged.Items.Select(i => new LocationListItem(i.Id, i.SiteId, i.SiteName, i.ParentId, i.Code, i.Name, i.Type, i.Criticality, i.IsActive, names(i.Path))).ToList(),
                paged.Total, paged.Page, paged.PageSize);
        }).RequirePermission(Permissions.LocationsView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var l = await db.Locations.AsNoTracking().Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            var ancestorIds = PathIds(l.Path).Where(x => x != l.Id).ToList();
            var ancestors = await db.Locations.AsNoTracking().Where(x => ancestorIds.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name, x.Depth }).ToListAsync(ct);
            var subtreeAssets = await db.Assets.CountAsync(a => a.LocationId != null && db.Locations.Any(x => x.Id == a.LocationId && x.Path.StartsWith(l.Path)), ct);
            return Results.Ok(new
            {
                l.Id, l.SiteId, Site = new { l.Site!.Id, l.Site.Code, l.Site.Name }, l.ParentId, l.Code, l.Name, l.Type, l.Criticality,
                l.Description, l.NotesToTechnician, l.Latitude, l.Longitude, l.IsActive, l.Depth, l.CreatedAt, l.UpdatedAt, l.CostCentreId,
                Ancestors = ancestors.OrderBy(a => a.Depth).Select(a => new { a.Id, a.Code, a.Name }),
                ChildCount = await db.Locations.CountAsync(x => x.ParentId == id, ct),
                AssetCount = await db.Assets.CountAsync(a => a.LocationId == id, ct),
                SubtreeAssetCount = subtreeAssets,
                RowVersion = Convert.ToBase64String(l.RowVersion),
            });
        }).RequirePermission(Permissions.LocationsView);

        g.MapPost("/", async (LocationInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var loc = await CreateLocationAsync(db, input, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/locations/{loc.Id}", new { loc.Id });
        }).RequirePermission(Permissions.LocationsManage);

        g.MapPut("/{id:guid}", async (Guid id, LocationInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var loc = await db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (loc is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(loc).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();

            await db.InTransactionAsync(async token =>
            {
                await UpdateLocationAsync(db, loc, input, token);
                await db.SaveChangesAsync(token);
                return true;
            }, ct);
            AccessService.LocationsChanged(); // a move changes the paths of everything under it
            return Results.Ok(new { loc.Id });
        }).RequirePermission(Permissions.LocationsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var loc = await db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (loc is null) return Results.NotFound();
            if (await db.Locations.AnyAsync(x => x.ParentId == id, ct)) throw new DomainException("This location has sub-locations. Move or delete them first.");
            if (await db.Assets.AnyAsync(a => a.LocationId == id, ct)) throw new DomainException("This location has assets. Transfer them to another location first.");
            db.Locations.Remove(loc);
            await db.SaveChangesAsync(ct);
            AccessService.LocationsChanged(); // people limited to it now see nothing there
            return Results.NoContent();
        }).RequirePermission(Permissions.LocationsManage);
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator LocationRules(LocationInput i, Guid? id) => new Validator().Required("code", i.Code, "Code", 100).Required("name", i.Name, "Name", 300)
        .When(!Enum.IsDefined(i.Type), "type", "Choose a valid location type.")
        .When(!Enum.IsDefined(i.Criticality), "criticality", "Choose a valid criticality.")
        .When(i.ParentId is not null && i.ParentId == id, "parentId", "A location cannot be its own parent.");

    /// <summary>Adds a location under its parent (the parent may itself be new and not saved yet).</summary>
    internal static async Task<Location> CreateLocationAsync(CmmsDbContext db, LocationInput i, CancellationToken ct)
    {
        var loc = new Location { SiteId = i.SiteId, ParentId = i.ParentId, Code = i.Code.Trim(), Name = i.Name.Trim() };
        Apply(loc, i);
        await LocationTree.AssignPathAsync(db, loc, ct);
        db.Locations.Add(loc);
        return loc;
    }

    /// <summary>
    /// Updates a location. Moving it (another parent or site) saves, rewrites the paths below it and, across sites, moves the
    /// assets in it; call it inside a transaction. Without a move nothing is saved yet.
    /// </summary>
    internal static async Task UpdateLocationAsync(CmmsDbContext db, Location loc, LocationInput i, CancellationToken ct)
    {
        var moved = loc.ParentId != i.ParentId || loc.SiteId != i.SiteId;
        var siteChanged = loc.SiteId != i.SiteId;
        var (oldPath, oldDepth) = (loc.Path, loc.Depth);
        loc.Code = i.Code.Trim(); loc.Name = i.Name.Trim();
        Apply(loc, i);
        if (!moved) return;
        loc.SiteId = i.SiteId; loc.ParentId = i.ParentId;
        await LocationTree.AssignPathAsync(db, loc, ct);
        await db.SaveChangesAsync(ct);
        await LocationTree.RewriteDescendantsAsync(db, oldPath, loc.Path, loc.Depth - oldDepth, loc.SiteId, ct);
        if (siteChanged)
        {
            var followed = await db.Assets.Where(a => a.LocationId != null && db.Locations.Any(x => x.Id == a.LocationId && x.Path.StartsWith(loc.Path)))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SiteId, loc.SiteId), ct);
            if (followed > 0)
            {
                FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "Location", loc.Id, "Updated", $"Moved to another site: {FourC.Cmms.Core.Auditing.AuditLog.Count(followed, "asset", "assets")} followed");
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task<Validator> ValidateAsync(LocationInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = LocationRules(i, id);
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        // A place keeping its parent and site isn't moved (for people limited to a branch, its parent may be above what they see).
        var current = id is { } lid ? await db.Locations.AllSites().AsNoTracking().Where(l => l.Id == lid).Select(l => new { l.ParentId, l.SiteId }).FirstOrDefaultAsync(ct) : null;
        var unmoved = current is not null && current.ParentId == i.ParentId && current.SiteId == i.SiteId;
        if (i.ParentId is not null && !unmoved && !await db.Locations.AnyAsync(l => l.Id == i.ParentId && l.SiteId == i.SiteId, ct))
            v.Add("parentId", "Parent location must belong to the selected site.");
        if (db.CurrentBranchPaths is not null && i.ParentId is null && !unmoved) v.Add("parentId", "Choose a parent place in your part of the site.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.Locations, l => l.SiteId == i.SiteId && l.Code == i.Code.Trim() && l.Id != id,
                "Another location in this site already uses this code.", "This code is already used in a part of the site you can't see.", ct) is { } locationClash)
            v.Add("code", locationClash);
        return v;
    }

    internal static void Apply(Location l, LocationInput i)
    {
        l.Type = i.Type; l.Criticality = i.Criticality; l.Description = i.Description.Clean(); l.NotesToTechnician = i.NotesToTechnician.Clean();
        // Pages and sheets from before cost centres (0.20.0) leave it as it is; a new location takes what was sent.
        if (i.ManagesCostCentre == true || l.CreatedAt == default) l.CostCentreId = i.CostCentreId;
        l.Latitude = i.Latitude; l.Longitude = i.Longitude; l.IsActive = i.IsActive;
    }

    public static IEnumerable<Guid> PathIds(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParseExact(s, "N", out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty);

    /// <summary>Returns a function mapping a materialized path to "Building › Floor › Room".</summary>
    public static async Task<Func<string?, string>> ResolvePathNamesAsync(CmmsDbContext db, IEnumerable<string?> paths, CancellationToken ct)
    {
        var ids = paths.Where(p => p is not null).SelectMany(p => PathIds(p!)).Distinct().ToList();
        // Names of the places above a visible one (people limited to a branch see where it sits, not what else is there).
        var names = ids.Count == 0 ? [] : await db.Locations.AllSites().AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return p => p is null ? "" : string.Join(" › ", PathIds(p).Select(id => names.GetValueOrDefault(id, "?")));
    }
}
