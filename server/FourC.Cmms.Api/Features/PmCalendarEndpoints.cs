using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Pm;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Shared PM calendars (0.12.0): named sets of dates many PMs use as triggers.</summary>
public static class PmCalendarEndpoints
{
    public sealed record CalendarInput(string Code, string Name, CalendarKind Kind, int Frequency = 1, FrequencyUnit Unit = FrequencyUnit.Month, DateOnly? AnchorDate = null,
        List<DateOnly>? Dates = null, bool IsActive = true, string? RowVersion = null);

    public const int MaxDates = 500;
    public const string InUse = "In use by {0} PM(s).";
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The calendar's next <paramref name="count"/> dates from today.</summary>
    public static List<DateOnly> NextDates(ScheduleCalendar c, int count)
    {
        var list = new List<DateOnly>();
        var after = Today.AddDays(-1);
        while (list.Count < count && PmTriggers.CalendarNext(c, after) is { } d) { list.Add(d); after = d; }
        return list;
    }

    public static void MapPmCalendarEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/pm-calendars").WithTags("PM calendars");
        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var used = await db.PmScheduleCalendars.GroupBy(x => x.ScheduleCalendarId).Select(x => new { x.Key, C = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            var rows = await db.ScheduleCalendars.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
            return rows.Select(c => new
            {
                c.Id, c.Code, c.Name, c.Kind, c.Frequency, c.Unit, c.AnchorDate, DateCount = c.Dates.Count, c.IsActive, ScheduleCount = used.GetValueOrDefault(c.Id),
                Next = NextDates(c, 1).Select(d => (DateOnly?)d).FirstOrDefault(),
            });
        }).RequirePermission(Permissions.PmView);
        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.ScheduleCalendars.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var usedBy = await db.PmSchedules.AsNoTracking().Where(s => s.Calendars.Any(x => x.ScheduleCalendarId == id)).OrderBy(s => s.Code)
                .Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct);
            return Results.Ok(new
            {
                c.Id, c.Code, c.Name, c.Kind, c.Frequency, c.Unit, c.AnchorDate, Dates = c.Dates.Order(), c.IsActive, Next = NextDates(c, 12), UsedBy = usedBy,
                RowVersion = Convert.ToBase64String(c.RowVersion),
            });
        }).RequirePermission(Permissions.PmView);
        g.MapPost("/", async (CalendarInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var c = new ScheduleCalendar { Code = input.Code.Trim(), Name = input.Name.Trim() };
            Apply(c, input);
            db.ScheduleCalendars.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pm-calendars/{c.Id}", new { c.Id });
        }).RequirePermission(Permissions.PmManage);
        g.MapPut("/{id:guid}", async (Guid id, CalendarInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.ScheduleCalendars.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(c).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            c.Code = input.Code.Trim(); c.Name = input.Name.Trim();
            Apply(c, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.PmManage);
        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.ScheduleCalendars.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            // Every PM using it counts, also those in sites the person can't see.
            var users = await db.PmScheduleCalendars.IgnoreQueryFilters().CountAsync(x => x.ScheduleCalendarId == id
                && db.PmSchedules.IgnoreQueryFilters().Any(s => s.Id == x.PmScheduleId && !s.IsDeleted && s.TenantId == db.CurrentTenantId), ct);
            if (users > 0) throw new DomainException(string.Format(InUse, users));
            db.ScheduleCalendars.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);
    }

    private static void Apply(ScheduleCalendar c, CalendarInput i)
    {
        c.Kind = i.Kind; c.IsActive = i.IsActive;
        c.Frequency = i.Kind == CalendarKind.Interval ? i.Frequency : 1; c.Unit = i.Unit;
        c.AnchorDate = i.Kind == CalendarKind.Interval ? i.AnchorDate : null;
        c.Dates = i.Kind == CalendarKind.Dates ? (i.Dates ?? []).Distinct().Order().ToList() : [];
    }

    private static async Task<Validator> ValidateAsync(CalendarInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var dates = (i.Dates ?? []).Distinct().Count();
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200)
            .When(!Enum.IsDefined(i.Kind), "kind", "Choose a kind of calendar.")
            .When(i.Kind == CalendarKind.Interval && i.AnchorDate is null, "anchorDate", "Choose the first date.")
            .When(i.Kind == CalendarKind.Interval && (i.Frequency is < 1 or > 999 || !Enum.IsDefined(i.Unit)), "frequency", "Enter how often (1–999).")
            .When(i.Kind == CalendarKind.Dates && dates == 0, "dates", "Add at least one date.")
            .When(i.Kind == CalendarKind.Dates && dates > MaxDates, "dates", "At most 500 dates.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.ScheduleCalendars.AnyAsync(c => c.Code == i.Code.Trim() && c.Id != id, ct)) v.Add("code", "Another calendar uses this code.");
        return v;
    }
}
