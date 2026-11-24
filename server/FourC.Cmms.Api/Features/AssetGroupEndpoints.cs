using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Asset groups (0.21.0): named sets of assets anywhere in the tree, for one site or all. A site-limited person sees the groups
/// of their sites and the shared ones, only the members they can see, and never changes members they can't see.
/// </summary>
public static class AssetGroupEndpoints
{
    public sealed record GroupInput(string Code, string Name, Guid? SiteId, bool IsActive = true);
    public sealed record MembersInput(List<Guid>? Add, List<Guid>? Remove);

    public static void MapAssetGroupEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/asset-groups").WithTags("Asset groups");

        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var sites = await db.Sites.AllSites().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var visible = db.Assets.Select(a => a.Id);
            return (await db.AssetGroups.AsNoTracking().OrderBy(x => x.Code)
                    .Select(x => new { x.Id, x.Code, x.Name, x.SiteId, x.IsActive, MemberCount = db.AssetGroupMembers.Count(m => m.AssetGroupId == x.Id && visible.Contains(m.AssetId)) })
                    .ToListAsync(ct))
                .Select(x => new { x.Id, x.Code, x.Name, x.SiteId, SiteName = x.SiteId is { } s ? sites.GetValueOrDefault(s) : null, x.IsActive, x.MemberCount });
        }).RequirePermission(Permissions.AssetsView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.AssetGroups.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var members = await (from m in db.AssetGroupMembers where m.AssetGroupId == id
                                 join a in db.Assets on m.AssetId equals a.Id
                                 orderby a.AssetNumber
                                 select new { a.Id, a.AssetNumber, Name = Bilingual.Arabic && a.NameAr != null ? a.NameAr : a.Name, a.SiteId }).ToListAsync(ct);
            return Results.Ok(new { x.Id, x.Code, x.Name, x.SiteId, x.IsActive, Members = members });
        }).RequirePermission(Permissions.AssetsView);

        g.MapPost("/", async (GroupInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await CheckAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var group = new AssetGroup { Code = i.Code.Trim(), Name = i.Name.Trim(), SiteId = i.SiteId, IsActive = i.IsActive };
            db.AssetGroups.Add(group);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/asset-groups/{group.Id}", new { group.Id });
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapPut("/{id:guid}", async (Guid id, GroupInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var group = await db.AssetGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return Results.NotFound();
            var v = await CheckAsync(db, i, group, ct);
            if (!v.IsValid) return v.Problem();
            group.Code = i.Code.Trim(); group.Name = i.Name.Trim(); group.SiteId = i.SiteId; group.IsActive = i.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { group.Id });
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var group = await db.AssetGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return Results.NotFound();
            // A shared group reaches sites a limited person can't see: only people who see every site delete one.
            if (group.SiteId is null && db.CurrentSiteIds is not null) throw new DomainException("Only people who see every site can delete a shared asset group.");
            if (await db.PmSchedules.AllSites().AnyAsync(s => s.AssetGroupId == id, ct)) throw new DomainException("This asset group is used by a PM. Make it inactive instead.");
            db.AssetGroupMembers.RemoveRange(await db.AssetGroupMembers.Where(m => m.AssetGroupId == id).ToListAsync(ct)); // each logged (0.25.0)
            db.AssetGroups.Remove(group);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapPost("/{id:guid}/members", async (Guid id, MembersInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var group = await db.AssetGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return Results.NotFound();
            var add = (i.Add ?? []).Distinct().ToList();
            var remove = (i.Remove ?? []).Distinct().ToList();
            if (add.Count + remove.Count > 1000) throw new DomainException("Change at most 1,000 members at a time.");
            // Only assets the person can see: others are neither added nor removed.
            var adding = await db.Assets.AsNoTracking().Where(a => add.Contains(a.Id)).Select(a => new { a.Id, a.SiteId }).ToListAsync(ct);
            if (adding.Count != add.Count) throw new DomainException("An asset wasn't found.");
            if (group.SiteId is { } site && adding.Any(a => a.SiteId != site))
            {
                var name = await db.Sites.AllSites().Where(s => s.Id == site).Select(s => s.Name).FirstAsync(ct);
                throw new DomainException(string.Format(Translator.T("This group is for {0} only."), name));
            }
            var existing = (await db.AssetGroupMembers.Where(m => m.AssetGroupId == id).Select(m => m.AssetId).ToListAsync(ct)).ToHashSet();
            var added = 0;
            foreach (var a in adding.Where(a => !existing.Contains(a.Id))) { db.AssetGroupMembers.Add(new AssetGroupMember { AssetGroupId = id, AssetId = a.Id }); added++; }
            var seen = db.Assets.Where(a => remove.Contains(a.Id)).Select(a => a.Id);
            var leaving = await db.AssetGroupMembers.Where(m => m.AssetGroupId == id && seen.Contains(m.AssetId)).ToListAsync(ct);
            db.AssetGroupMembers.RemoveRange(leaving); // each logged (0.25.0)
            var removed = leaving.Count;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Added = added, Removed = removed });
        }).RequirePermission(Permissions.AssetsEdit);
    }

    private static async Task<Validator> CheckAsync(CmmsDbContext db, GroupInput i, AssetGroup? existing, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200);
        // People with limited access give their groups a site, as their teams (a shared group would reach sites they can't see).
        if (i.SiteId is null && db.CurrentSiteIds is not null) v.Add("siteId", "People with limited access give their asset groups a site.");
        var code = i.Code.Clean();
        if (code is not null && await db.AssetGroups.AllSites().AnyAsync(x => x.Code == code && x.Id != (existing == null ? Guid.Empty : existing.Id), ct))
            v.Add("code", "This code is already used.");
        if (i.SiteId is { } site)
        {
            if (!await db.Sites.AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site.");
            else if (existing is not null && await (from m in db.AssetGroupMembers where m.AssetGroupId == existing.Id
                                                    join a in db.Assets.AllSites() on m.AssetId equals a.Id
                                                    where a.SiteId != site select m).AnyAsync(ct))
                v.Add("siteId", "This group has assets of other sites.");
        }
        return v;
    }

    /// <summary>
    /// The asset form's groups (0.21.0): the asset joins the chosen groups and leaves the others — only among groups the person
    /// can see, so groups of other sites keep it.
    /// </summary>
    internal static async Task SetForAssetAsync(CmmsDbContext db, Guid assetId, Guid siteId, IReadOnlyCollection<Guid> wanted, CancellationToken ct)
    {
        var visible = await db.AssetGroups.Select(x => new { x.Id, x.SiteId }).ToListAsync(ct);
        if (wanted.Any(w => visible.All(x => x.Id != w))) throw new DomainException("An asset group wasn't found.");
        var ids = visible.Select(x => x.Id).ToList();
        var current = await db.AssetGroupMembers.Where(m => m.AssetId == assetId && ids.Contains(m.AssetGroupId)).Select(m => m.AssetGroupId).ToListAsync(ct);
        // Only groups the asset joins now must be of its site (one moved to another site keeps the groups it's already in).
        if (visible.Any(x => wanted.Contains(x.Id) && !current.Contains(x.Id) && x.SiteId is { } s && s != siteId)) throw new DomainException("Choose groups of the asset's site or shared groups.");
        foreach (var g in wanted.Except(current)) db.AssetGroupMembers.Add(new AssetGroupMember { AssetGroupId = g, AssetId = assetId });
        var leaving = current.Except(wanted).ToList();
        if (leaving.Count > 0) db.AssetGroupMembers.RemoveRange(await db.AssetGroupMembers.Where(m => m.AssetId == assetId && leaving.Contains(m.AssetGroupId)).ToListAsync(ct)); // the caller saves; each logged (0.25.0)
    }

    /// <summary>The assets of a group, for list and report filters.</summary>
    internal static IQueryable<Guid> Members(CmmsDbContext db, Guid groupId) => db.AssetGroupMembers.Where(m => m.AssetGroupId == groupId).Select(m => m.AssetId);
}
