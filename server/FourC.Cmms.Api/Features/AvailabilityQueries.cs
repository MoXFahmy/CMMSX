using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Planning;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>A type of time away or extra time that applies to a person on a day.</summary>
public sealed record SlotView(Guid TypeId, string Name, string? Color, AvailabilityKind Kind, decimal? Hours);

/// <summary>A person's day: the hours of their working pattern, the hours available after time away and extra time, and what applies.</summary>
public sealed record DayAvailability(decimal Pattern, decimal Available, List<SlotView> Slots);

/// <summary>
/// Hours available per person and day (0.19.0), the single source for the planning board, its warnings, the timesheet and the
/// phone: the working pattern (their own, else their single site's, else the organization's) through <see cref="Availability.Available"/>.
/// </summary>
public static class AvailabilityQueries
{
    public static async Task<Dictionary<Guid, Dictionary<DateOnly, DayAvailability>>> DaysAsync(CmmsDbContext db, IReadOnlyCollection<Guid> employeeIds,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Dictionary<DateOnly, DayAvailability>>();
        if (employeeIds.Count == 0 || to < from) return result;
        var ids = employeeIds.Distinct().ToList();
        var settings = await SlaQueries.SettingsAsync(db, ct);
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.SiteIds, e.WorkingHours }).ToListAsync(ct);
        var siteHours = (await db.Sites.AllSites().AsNoTracking().Select(s => new { s.Id, s.Settings }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Settings.WorkingHours);
        // A team entry applies to whoever is a member when it is read — every team, whatever sites the viewer sees, so a person's
        // hours are the same for everyone looking.
        var teams = (await db.Teams.AllSites().AsNoTracking().Select(t => new { t.Id, t.MemberIds }).ToListAsync(ct)).Where(t => t.MemberIds.Any(ids.Contains)).ToList();
        var teamIds = teams.Select(t => t.Id).ToList();
        var entries = await db.AvailabilityEntries.AsNoTracking().Include(e => e.Type)
            .Where(e => e.From <= to && e.To >= from && ((e.EmployeeId != null && ids.Contains(e.EmployeeId.Value)) || (e.TeamId != null && teamIds.Contains(e.TeamId.Value))))
            .ToListAsync(ct);
        foreach (var p in people)
        {
            var week = FourC.Cmms.Core.People.WorkingHours.ForEmployee(p.WorkingHours, p.SiteIds, siteHours, settings);
            var myTeams = teams.Where(t => t.MemberIds.Contains(p.Id)).Select(t => t.Id).ToHashSet();
            var mine = entries.Where(e => e.EmployeeId == p.Id || (e.TeamId is { } t && myTeams.Contains(t))).ToList();
            var days = new Dictionary<DateOnly, DayAvailability>();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var pattern = FourC.Cmms.Core.People.WorkingHours.HoursOn(week, day.DayOfWeek);
                var slots = mine.Where(e => Availability.Covers(e.From, e.To, e.Days, day))
                    .Select(e => new SlotView(e.TypeId, e.Type!.Name, e.Type.Color, e.Type.Kind, e.Hours)).ToList();
                days[day] = new DayAvailability(pattern, Availability.Available(pattern, slots.Select(s => (s.Kind, s.Hours))), slots);
            }
            result[p.Id] = days;
        }
        return result;
    }
}
