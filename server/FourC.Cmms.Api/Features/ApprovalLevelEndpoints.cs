using System.Globalization;
using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Approval level settings (0.16.0): the organization's set and sites' own sets, optionally per order type.</summary>
public static class ApprovalLevelEndpoints
{
    public sealed record ApprovalLevelInput(Guid? SiteId, Guid? OrderTypeId, string? Name, decimal MinAmount, ApprovalRule Rule, List<Guid>? ApproverIds);

    public const int MaxApprovers = 5;

    /// <summary>Why a listed approver can't sign orders for the level's sites today, or null when they can.</summary>
    internal static async Task<Dictionary<(Guid Level, Guid User), string?>> ProblemsAsync(CmmsDbContext db, PermissionService perms, IReadOnlyCollection<ApprovalLevel> levels, CancellationToken ct)
    {
        var userIds = levels.SelectMany(l => l.ApproverIds).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.TenantId == db.CurrentTenantId)
            .Select(u => new { u.Id, u.IsActive, u.SiteIds }).ToDictionaryAsync(u => u.Id, ct);
        // Sites using the organization's set: active sites without levels of their own.
        var ownSets = await db.ApprovalLevels.AsNoTracking().Where(l => l.SiteId != null).Select(l => l.SiteId!.Value).Distinct().ToListAsync(ct);
        var orgSites = await db.Sites.AllSites().AsNoTracking().Where(s => s.IsActive && !ownSets.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
        var result = new Dictionary<(Guid Level, Guid User), string?>();
        foreach (var level in levels)
            foreach (var id in level.ApproverIds)
            {
                var key = (level.Id, id);
                if (!users.TryGetValue(id, out var u) || !u.IsActive) { result[key] = "inactive"; continue; }
                if (!(await perms.GetAsync(id)).Contains(Permissions.PurchasingApprove)) { result[key] = "no-permission"; continue; }
                var limited = u.SiteIds.Count > 0;
                var covers = level.SiteId is { } site ? !limited || u.SiteIds.Contains(site) : !limited || orgSites.All(s => u.SiteIds.Contains(s));
                result[key] = covers ? null : "no-site-access";
            }
        return result;
    }

    public static void MapApprovalLevelEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/approval-levels").WithTags("Approval levels").RequireAuthorization();

        g.MapGet("/", async (HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            if (!mine.Contains(Permissions.SettingsManage) && !mine.Contains(Permissions.PurchasingApprove)) return Results.Forbid();
            var levels = await db.ApprovalLevels.AsNoTracking().ToListAsync(ct);
            var problems = await ProblemsAsync(db, perms, levels, ct);
            var userIds = levels.SelectMany(l => l.ApproverIds).Distinct().ToList();
            var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            var sites = await db.Sites.AllSites().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var types = await db.OrderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            return Results.Ok(levels.OrderBy(l => l.SiteId is null ? "" : sites.GetValueOrDefault(l.SiteId.Value)).ThenBy(l => l.OrderTypeId is null ? "" : types.GetValueOrDefault(l.OrderTypeId.Value))
                .ThenBy(l => l.MinAmount).Select(l => new
                {
                    l.Id, l.SiteId, SiteName = l.SiteId is { } s ? sites.GetValueOrDefault(s) : null, l.OrderTypeId, OrderTypeName = l.OrderTypeId is { } t ? types.GetValueOrDefault(t) : null,
                    l.Name, l.MinAmount, l.Rule,
                    Approvers = l.ApproverIds.Select(a => new { Id = a, Name = names.GetValueOrDefault(a), Ok = problems.GetValueOrDefault((l.Id, a)) is null, Problem = problems.GetValueOrDefault((l.Id, a)) }),
                    RowVersion = Convert.ToBase64String(l.RowVersion),
                }));
        });

        g.MapPost("/", async (ApprovalLevelInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var l = new ApprovalLevel { Name = "" };
            Apply(l, i);
            db.ApprovalLevels.Add(l);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/approval-levels/{l.Id}", new { l.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, ApprovalLevelInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var l = await db.ApprovalLevels.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            var v = await ValidateAsync(db, i, id, ct);
            if (!v.IsValid) return v.Problem();
            Apply(l, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { l.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var l = await db.ApprovalLevels.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            db.ApprovalLevels.Remove(l);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static void Apply(ApprovalLevel l, ApprovalLevelInput i)
    {
        l.SiteId = i.SiteId; l.OrderTypeId = i.OrderTypeId; l.Name = i.Name!.Trim(); l.MinAmount = Math.Round(i.MinAmount, 2); l.Rule = i.Rule;
        l.ApproverIds = (i.ApproverIds ?? []).Distinct().ToList();
    }

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, ApprovalLevelInput i, Guid? id, CancellationToken ct)
    {
        var who = (i.ApproverIds ?? []).Distinct().ToList();
        var v = new Validator().MaxLength("name", i.Name, "Name", 200)
            .When(string.IsNullOrWhiteSpace(i.Name), "name", "Enter a name.")
            .When(who.Count == 0, "approverIds", "Choose at least one approver.")
            .When(who.Count > MaxApprovers, "approverIds", "A level can have at most 5 approvers.")
            .When(i.MinAmount < 0, "minAmount", "The minimum can't be below 0.")
            .When(!Enum.IsDefined(i.Rule), "rule", "Choose whether all or any one of the approvers must sign.");
        if (who.Count is > 0 and <= MaxApprovers && await db.Users.People().CountAsync(u => who.Contains(u.Id) && u.TenantId == db.CurrentTenantId && u.IsActive, ct) != who.Count)
            v.Add("approverIds", "Choose active users.");
        if (i.SiteId is { } site && !await db.Sites.AllSites().AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site.");
        if (i.OrderTypeId is { } type && !await db.OrderTypes.AnyAsync(t => t.Id == type && t.IsActive, ct)) v.Add("orderTypeId", "Choose an active order type.");
        var min = Math.Round(i.MinAmount, 2);
        if (i.MinAmount >= 0 && await db.ApprovalLevels.AnyAsync(l => l.SiteId == i.SiteId && l.OrderTypeId == i.OrderTypeId && l.MinAmount == min && l.Id != id, ct))
            v.Add("minAmount", $"Another level in this set already starts at {min.ToString("#,0.##", CultureInfo.InvariantCulture)}.");
        return v;
    }
}
