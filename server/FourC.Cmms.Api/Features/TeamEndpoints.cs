using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Named technician teams (0.9.0): work orders and PM schedules can be given to a team, and its members see that work.</summary>
public static class TeamEndpoints
{
    public sealed record TeamInput(string Code, string Name, Guid? SiteId, string? Color, List<Guid>? MemberIds, bool IsActive = true, string? RowVersion = null);

    public static void MapTeamEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/teams").WithTags("Teams");

        // Anyone who can see work orders can pick a team; siteId = the teams usable there (its own and those of every site).
        g.MapGet("/", async (CmmsDbContext db, Guid? siteId, bool? active, CancellationToken ct) =>
        {
            var q = db.Teams.AsNoTracking();
            if (siteId is { } s) q = q.Where(t => t.SiteId == null || t.SiteId == s);
            if (active == true) q = q.Where(t => t.IsActive);
            return await q.OrderBy(t => t.Name).Select(t => new
            {
                t.Id, t.Code, t.Name, t.SiteId, SiteName = t.Site == null ? null : t.Site.Name, t.Color, t.MemberIds, t.IsActive,
                RowVersion = Convert.ToBase64String(t.RowVersion),
            }).ToListAsync(ct);
        }).RequireAuthorization();

        g.MapPost("/", async (TeamInput input, CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct, Access.Of(http)?.Limited == true);
            if (!v.IsValid) return v.Problem();
            var t = new Team { Code = input.Code.Trim(), Name = input.Name.Trim() };
            Apply(t, input);
            db.Teams.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/teams/{t.Id}", new { t.Id });
        }).RequirePermission(Permissions.PeopleManage);

        g.MapPut("/{id:guid}", async (Guid id, TeamInput input, CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var t = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct, Access.Of(http)?.Limited == true);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(t).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            t.Code = input.Code.Trim(); t.Name = input.Name.Trim();
            Apply(t, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id });
        }).RequirePermission(Permissions.PeopleManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var t = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var open = new[] { WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold };
            if (await db.WorkOrders.AllSites().AnyAsync(w => w.TeamId == id && open.Contains(w.Status), ct) || await db.PmSchedules.AllSites().AnyAsync(s => s.TeamId == id, ct))
                throw new DomainException("This team has open work or PM schedules. Give them to someone else first, or mark the team inactive.");
            db.Teams.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PeopleManage);
    }

    private static void Apply(Team t, TeamInput i)
    {
        t.SiteId = i.SiteId; t.Color = i.Color.Clean(); t.MemberIds = (i.MemberIds ?? []).Distinct().ToList(); t.IsActive = i.IsActive;
    }

    /// <param name="limited">The person's own access is limited (not just the site switch).</param>
    private static async Task<Validator> ValidateAsync(TeamInput i, Guid? id, CmmsDbContext db, CancellationToken ct, bool limited = false)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("color", i.Color, "Colour", 20);
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.Teams, t => t.Code == i.Code.Trim() && t.Id != id,
                "Another team uses this code.", "This code is already used by a team in a site you can't see.", ct) is { } clash)
            v.Add("code", clash);
        if (i.SiteId is { } site && !await db.Sites.AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site.");
        var currentSite = id is { } tid ? await db.Teams.AllSites().Where(t => t.Id == tid).Select(t => new { t.SiteId }).FirstOrDefaultAsync(ct) : null;
        // People with limited access give their teams a site (a team of every site would reach sites they can't see).
        if (limited && i.SiteId is null && !(currentSite is not null && currentSite.SiteId is null))
            v.Add("siteId", "Choose the team's site.");
        // A team's site can't move away from its open work and schedules.
        if (i.SiteId is { } newSite && id is { } team && currentSite?.SiteId != newSite)
        {
            var open = new[] { WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold };
            if (await db.WorkOrders.AllSites().AnyAsync(w => w.TeamId == team && w.SiteId != newSite && open.Contains(w.Status), ct)
                || await db.PmSchedules.AllSites().AnyAsync(s => s.TeamId == team && s.SiteId != newSite, ct))
                v.Add("siteId", "This team has open work or PM schedules at another site.");
        }
        var members = (i.MemberIds ?? []).Distinct().ToList();
        if (members.Count > 0 && await db.Employees.CountAsync(e => members.Contains(e.Id), ct) != members.Count) v.Add("memberIds", "Unknown person.");
        return v;
    }

    /// <summary>Checks a team chosen for work at <paramref name="siteId"/>: active (unless it's the one already there) and of that site or of every site.</summary>
    internal static async Task CheckForWorkAsync(CmmsDbContext db, Validator v, Guid? teamId, Guid siteId, Guid? current, CancellationToken ct)
    {
        if (teamId is not { } team) return;
        if (!await db.Teams.AnyAsync(t => t.Id == team && (t.IsActive || t.Id == current) && (t.SiteId == null || t.SiteId == siteId), ct))
            v.Add("teamId", "Choose an active team of this site.");
    }

    /// <summary>The active teams an employee belongs to.</summary>
    internal static async Task<List<Guid>> TeamsOfAsync(CmmsDbContext db, Guid employeeId, CancellationToken ct) =>
        await db.Teams.AsNoTracking().Where(t => t.IsActive && t.MemberIds.Contains(employeeId)).Select(t => t.Id).ToListAsync(ct);
}
