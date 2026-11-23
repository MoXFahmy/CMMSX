using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Costs;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>The cost-centre tree (0.20.0): parents without cycles, for one site or all; seen by everyone, edited with Manage settings.</summary>
public static class CostCentreEndpoints
{
    public sealed record CostCentreInput(string Code, string Name, Guid? ParentId, Guid? SiteId, bool IsActive = true);
    public sealed record FillInInput(DateOnly From);

    /// <summary>"Production › Line 1 › Packing" for every cost centre (names of hidden parents included), by id.</summary>
    internal static async Task<Dictionary<Guid, string>> PathsAsync(CmmsDbContext db, CancellationToken ct)
    {
        var all = await db.CostCentres.AllSites().AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId }).ToDictionaryAsync(c => c.Id, ct);
        var paths = new Dictionary<Guid, string>();
        foreach (var c in all.Values)
        {
            var names = new List<string>();
            var seen = new HashSet<Guid>();
            for (var x = c; x is not null && seen.Add(x.Id); x = x.ParentId is { } p ? all.GetValueOrDefault(p) : null) names.Add(x.Name);
            names.Reverse();
            paths[c.Id] = string.Join(" › ", names);
        }
        return paths;
    }

    public static void MapCostCentreEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/cost-centres").WithTags("Cost centres");
        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var paths = await PathsAsync(db, ct);
            var sites = await db.Sites.AllSites().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            return (await db.CostCentres.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct)).Select(c => new
            {
                c.Id, c.Code, c.Name, c.ParentId, c.SiteId, SiteName = c.SiteId is { } s ? sites.GetValueOrDefault(s) : null, c.IsActive, Path = paths.GetValueOrDefault(c.Id),
            }).OrderBy(c => c.Path);
        }).RequireAuthorization();

        // Fill in past work (0.20.0): work orders created on or after a date, and direct issues dated on or after it, with no cost
        // centre get the one in force today. Never overwrites; the GET counts what the POST would change.
        g.MapGet("/fill-in", async (DateOnly from, CmmsDbContext db, CancellationToken ct) =>
            Results.Ok(await FillInAsync(db, from, apply: false, ct))).RequirePermission(Permissions.SettingsManage);
        g.MapPost("/fill-in", async (FillInInput i, CmmsDbContext db, CancellationToken ct) =>
            Results.Ok(await FillInAsync(db, i.From, apply: true, ct))).RequirePermission(Permissions.SettingsManage);

        g.MapPost("/", async (CostCentreInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await CheckAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var c = new CostCentre { Code = "", Name = "" };
            Apply(c, i);
            db.CostCentres.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cost-centres/{c.Id}", new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, CostCentreInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.CostCentres.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await CheckAsync(db, i, c, ct);
            if (!v.IsValid) return v.Problem();
            Apply(c, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.CostCentres.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await InUseAsync(db, id, ct)) throw new DomainException("This cost centre is in use. Make it inactive instead.");
            db.CostCentres.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<object> FillInAsync(CmmsDbContext db, DateOnly from, bool apply, CancellationToken ct)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var orders = await db.WorkOrders.Where(w => w.CostCentreId == null && w.CreatedAt >= since).ToListAsync(ct);
        var issues = await db.StockTransactions.Where(t => t.CostCentreId == null && t.WorkOrderId == null && t.LocationId != null && t.Date >= from
            && (t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Issue || t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Return)).ToListAsync(ct);
        var cache = new Dictionary<(Guid?, Guid?), Guid?>();
        async Task<Guid?> Resolve(Guid? asset, Guid? location)
        {
            if (!cache.TryGetValue((asset, location), out var cc)) cache[(asset, location)] = cc = await CostCentreQueries.InForceAsync(db, asset, location, ct);
            return cc;
        }
        int workOrders = 0, directIssues = 0;
        foreach (var w in orders) if (await Resolve(w.AssetId, w.LocationId) is { } cc) { workOrders++; if (apply) w.CostCentreId = cc; }
        foreach (var t in issues) if (await Resolve(null, t.LocationId) is { } cc) { directIssues++; if (apply) t.CostCentreId = cc; }
        if (apply) await db.SaveChangesAsync(ct);
        return new { WorkOrders = workOrders, Issues = directIssues };
    }

    /// <summary>Referenced anywhere, at any site: a child, a location, an asset, a request, a PM, a work order, an issue or a budget.</summary>
    private static async Task<bool> InUseAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        await db.CostCentres.AllSites().AnyAsync(x => x.ParentId == id, ct)
        || await db.Locations.AllSites().AnyAsync(x => x.CostCentreId == id, ct) || await db.Assets.AllSites().AnyAsync(x => x.CostCentreId == id, ct)
        || await db.WorkRequests.AllSites().AnyAsync(x => x.CostCentreId == id, ct) || await db.PmSchedules.AllSites().AnyAsync(x => x.CostCentreId == id, ct)
        || await db.WorkOrders.AllSites().AnyAsync(x => x.CostCentreId == id, ct) || await db.StockTransactions.AllSites().AnyAsync(x => x.CostCentreId == id, ct)
        || await db.Budgets.AnyAsync(x => x.CostCentreId == id, ct);

    private static async Task<Validator> CheckAsync(CmmsDbContext db, CostCentreInput i, CostCentre? existing, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200);
        var code = i.Code.Clean();
        if (code is not null && await db.CostCentres.AllSites().AnyAsync(c => c.Code == code && c.Id != (existing == null ? Guid.Empty : existing.Id), ct))
            v.Add("code", "This code is already used.");
        if (i.SiteId is { } site && !await db.Sites.AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site.");
        if (i.ParentId is { } parentId)
        {
            var all = await db.CostCentres.AllSites().AsNoTracking().Select(c => new { c.Id, c.ParentId, c.SiteId, c.IsActive }).ToDictionaryAsync(c => c.Id, ct);
            var parent = all.GetValueOrDefault(parentId);
            if (parent is null || (!parent.IsActive && existing?.ParentId != parentId)) v.Add("parentId", "Choose an active parent.");
            else
            {
                // Walking up from the parent must never reach this cost centre.
                var seen = new HashSet<Guid>();
                for (Guid? x = parentId; x is { } cur && seen.Add(cur); x = all.GetValueOrDefault(cur)?.ParentId)
                    if (existing is not null && cur == existing.Id) { v.Add("parentId", "A cost centre can't be its own parent or under one of its own."); break; }
                if (parent.SiteId is { } ps && ps != i.SiteId)
                    v.Add("parentId", "A site's cost centre can only sit under a cost centre of the same site or for all sites.");
            }
        }
        return v;
    }

    private static void Apply(CostCentre c, CostCentreInput i)
    {
        c.Code = i.Code.Clean()!; c.Name = i.Name.Clean()!; c.ParentId = i.ParentId; c.SiteId = i.SiteId; c.IsActive = i.IsActive;
    }
}
