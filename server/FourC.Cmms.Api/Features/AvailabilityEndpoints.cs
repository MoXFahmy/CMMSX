using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Planning;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Availability (0.19.0): types of time away and extra time, entries per person or team, and the hours they leave.</summary>
public static class AvailabilityEndpoints
{
    public sealed record AvailabilityTypeInput(string Code, string Name, string? Color, AvailabilityKind Kind, bool IsActive = true);
    /// <param name="To">The last day; none = the same day as <paramref name="From"/>.</param>
    /// <param name="Days">Weekdays it applies on; none = every day in the range.</param>
    /// <param name="Hours">Hours a day; none = the whole working day (time away only).</param>
    public sealed record AvailabilityEntryInput(Guid TypeId, Guid? EmployeeId, Guid? TeamId, DateOnly From, DateOnly? To, List<DayOfWeek>? Days, decimal? Hours, string? Notes);

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Planners and people managers enter time away and extra time.</summary>
    private static async Task<bool> MayEditAsync(HttpContext http, PermissionService perms)
    {
        var mine = await perms.GetAsync(UserId(http));
        return mine.Contains(Permissions.PeopleManage) || mine.Contains(Permissions.WorkOrdersEdit);
    }

    /// <summary>People the viewer may see: those who work at one of their sites, or everywhere (the planning board's rule).</summary>
    internal static async Task<HashSet<Guid>> VisiblePeopleAsync(CmmsDbContext db, CancellationToken ct)
    {
        var visible = db.CurrentSiteIds;
        return (await db.Employees.AsNoTracking().Select(e => new { e.Id, e.SiteIds }).ToListAsync(ct))
            .Where(e => visible is null || e.SiteIds.Count == 0 || e.SiteIds.Any(visible.Contains)).Select(e => e.Id).ToHashSet();
    }

    public static void MapAvailabilityEndpoints(this RouteGroupBuilder api)
    {
        var types = api.MapGroup("/availability-types").WithTags("Availability");
        types.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.AvailabilityTypes.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Code, t.Name, t.Color, t.Kind, t.IsActive }).ToListAsync(ct))
            .RequireAuthorization();
        types.MapPost("/", async (AvailabilityTypeInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await CheckTypeAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var t = new AvailabilityType { Code = "", Name = "" };
            Apply(t, i);
            db.AvailabilityTypes.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/availability-types/{t.Id}", new { t.Id });
        }).RequirePermission(Permissions.SettingsManage);
        types.MapPut("/{id:guid}", async (Guid id, AvailabilityTypeInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var t = await db.AvailabilityTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var v = await CheckTypeAsync(db, i, id, ct);
            if (!v.IsValid) return v.Problem();
            Apply(t, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id });
        }).RequirePermission(Permissions.SettingsManage);
        types.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var t = await db.AvailabilityTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (await db.AvailabilityEntries.AnyAsync(e => e.TypeId == id, ct)) throw new DomainException("This type is in use. Make it inactive instead.");
            db.AvailabilityTypes.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        var g = api.MapGroup("/availability").WithTags("Availability");
        g.MapGet("/", async (Guid? employeeId, Guid? teamId, Guid? typeId, DateOnly? from, DateOnly? to, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = db.AvailabilityEntries.AsNoTracking().AsQueryable();
            if (employeeId is { } e) q = q.Where(x => x.EmployeeId == e);
            if (teamId is { } t) q = q.Where(x => x.TeamId == t);
            if (typeId is { } ty) q = q.Where(x => x.TypeId == ty);
            if (from is { } f) q = q.Where(x => x.To >= f);
            if (to is { } u) q = q.Where(x => x.From <= u);
            var rows = await q.OrderBy(x => x.From).Take(1000).Select(x => new
            {
                x.Id, x.TypeId, TypeName = x.Type!.Name, TypeColor = x.Type.Color, TypeKind = x.Type.Kind, x.EmployeeId, x.TeamId, x.From, x.To, x.Days, x.Hours, x.Notes,
            }).ToListAsync(ct);
            // Entries of people and teams the viewer can see.
            var people = await VisiblePeopleAsync(db, ct);
            var names = await db.Employees.AsNoTracking().Where(x => people.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
            var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            return rows.Where(r => r.EmployeeId is { } p ? people.Contains(p) : r.TeamId is { } tm && teams.ContainsKey(tm))
                .Select(r => new
                {
                    r.Id, r.TypeId, r.TypeName, r.TypeColor, r.TypeKind, r.EmployeeId, EmployeeName = r.EmployeeId is { } p ? names.GetValueOrDefault(p) : null,
                    r.TeamId, TeamName = r.TeamId is { } tm ? teams.GetValueOrDefault(tm) : null, r.From, r.To, r.Days, r.Hours, r.Notes,
                });
        }).RequirePermission(Permissions.WorkOrdersView);

        // A person's hours per day: the pattern, the hours available and what applies.
        g.MapGet("/hours", async (Guid employeeId, DateOnly from, DateOnly to, CmmsDbContext db, CancellationToken ct) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 366) throw new DomainException("Use at most 366 days.");
            if (!(await VisiblePeopleAsync(db, ct)).Contains(employeeId)) return Results.NotFound();
            var days = (await AvailabilityQueries.DaysAsync(db, [employeeId], from, to, ct)).GetValueOrDefault(employeeId) ?? [];
            return Results.Ok(days.OrderBy(d => d.Key).Select(d => new { Day = d.Key, d.Value.Pattern, d.Value.Available, d.Value.Slots }));
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/", async (AvailabilityEntryInput i, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var v = await CheckEntryAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var e = new AvailabilityEntry();
            Apply(e, i);
            db.AvailabilityEntries.Add(e);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/availability/{e.Id}", new { e.Id });
        }).RequireAuthorization();
        g.MapPut("/{id:guid}", async (Guid id, AvailabilityEntryInput i, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var e = await db.AvailabilityEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            var v = await CheckEntryAsync(db, i, e, ct);
            if (!v.IsValid) return v.Problem();
            Apply(e, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { e.Id });
        }).RequireAuthorization();
        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var e = await db.AvailabilityEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            db.AvailabilityEntries.Remove(e);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<Validator> CheckTypeAsync(CmmsDbContext db, AvailabilityTypeInput i, Guid? id, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("color", i.Color, "Colour", 20)
            .When(!Enum.IsDefined(i.Kind), "kind", "Choose time away or extra time.");
        var code = i.Code.Clean();
        if (code is not null && await db.AvailabilityTypes.AnyAsync(t => t.Code == code && t.Id != id, ct)) v.Add("code", "This code is already used.");
        return v;
    }

    private static void Apply(AvailabilityType t, AvailabilityTypeInput i)
    {
        t.Code = i.Code.Clean()!; t.Name = i.Name.Clean()!; t.Color = i.Color.Clean(); t.Kind = i.Kind; t.IsActive = i.IsActive;
    }

    private static async Task<Validator> CheckEntryAsync(CmmsDbContext db, AvailabilityEntryInput i, AvailabilityEntry? existing, CancellationToken ct)
    {
        var to = i.To ?? i.From;
        var v = new Validator()
            .When(i.EmployeeId is null == i.TeamId is null, "employeeId", "Choose a person or a team, not both.")
            .When(to < i.From, "to", "The end can't be before the start.")
            .When(to.DayNumber - i.From.DayNumber >= 366, "to", "Use at most 366 days.")
            .When(i.Hours is { } h && (h <= 0 || h > 24), "hours", "Hours must be more than 0 and at most 24.")
            .MaxLength("notes", i.Notes, "Notes", 1000);
        // Unchanged type, person or team stays valid on an existing entry even if it became inactive since.
        var type = await db.AvailabilityTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == i.TypeId, ct);
        if (type is null || (!type.IsActive && existing?.TypeId != i.TypeId)) v.Add("typeId", "Choose an active type.");
        else if (type.Kind == AvailabilityKind.Extra && i.Hours is null) v.Add("hours", "Extra time needs hours.");
        if (i.EmployeeId is { } p && existing?.EmployeeId != p && !await db.Employees.AnyAsync(e => e.Id == p && e.IsActive, ct)) v.Add("employeeId", "Choose an active person.");
        if (i.TeamId is { } t && existing?.TeamId != t && !await db.Teams.AnyAsync(x => x.Id == t && x.IsActive, ct)) v.Add("teamId", "Choose an active team.");
        return v;
    }

    private static void Apply(AvailabilityEntry e, AvailabilityEntryInput i)
    {
        e.TypeId = i.TypeId; e.EmployeeId = i.EmployeeId; e.TeamId = i.TeamId; e.From = i.From; e.To = i.To ?? i.From;
        e.Days = (i.Days ?? []).Distinct().Order().ToList(); e.Hours = i.Hours; e.Notes = i.Notes.Clean();
    }
}
