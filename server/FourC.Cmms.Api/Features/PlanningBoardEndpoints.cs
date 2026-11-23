using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// The planning board: open work by day and person (or asset), with trays for work from earlier days and work without a date,
/// and upcoming PMs. Dragging a card moves its planned start or its person.
/// </summary>
public static class PlanningBoardEndpoints
{
    /// <param name="Hours">The person's planned hours, else their share of the estimate.</param>
    /// <param name="PlannedHours">Their own planned hours only (Undo puts these back).</param>
    public sealed record BoardAssignee(Guid EmployeeId, decimal? Hours, decimal? PlannedHours);
    public sealed record BoardJob(Guid Id, string Number, string Title, WorkPriority Priority, WorkOrderStatus Status, Guid SiteId, Guid? AssetId, string? AssetNumber,
        string? AssetName, DateOnly? Day, DateOnly? PlannedStart, DateTimeOffset? DueAt, SlaView Sla, decimal? EstimatedHours, List<BoardAssignee> Assignees,
        List<AccessWindow>? AccessWindows = null, bool OutsideWindows = false, bool OnlyWhenStopped = false, decimal? ExpectedStopHours = null);
    /// <param name="Hours">Working hours on each day of the week (index = day, Sunday first), from their working pattern.</param>
    /// <param name="Days">Each day of the board's range (0.19.0): hours available after time away and extra time, and hours planned.</param>
    public sealed record BoardPerson(Guid Id, string Name, List<Guid> TradeIds, decimal[] Hours, List<BoardDay>? Days = null);
    /// <summary>A person's day on the board (0.19.0): available hours, planned hours (all their visible work) and the time away or extra time that applies.</summary>
    public sealed record BoardDay(DateOnly Day, decimal Available, decimal Planned, List<SlotView> Slots);
    public sealed record TakeInput(Guid WorkOrderId);
    /// <summary>Day: the new planned start. From → To: move that person's assignment; To alone adds a person; From alone removes one.</summary>
    /// <param name="PlannedHours">With To alone: the person's planned hours on the job (Undo puts back what they had).</param>
    public sealed record MoveInput(Guid WorkOrderId, DateOnly? Day, Guid? FromEmployeeId, Guid? ToEmployeeId, decimal? PlannedHours = null);

    /// <summary>The trays show at most this many cards (newest first).</summary>
    private const int MaxTray = 200;
    private const int MaxDays = 42;

    private sealed class JobRow
    {
        public Guid Id { get; init; }
        public required string Number { get; init; }
        public required string Title { get; init; }
        public WorkPriority Priority { get; init; }
        public WorkOrderStatus Status { get; init; }
        public Guid SiteId { get; init; }
        public Guid? AssetId { get; init; }
        public string? AssetNumber { get; init; }
        public string? AssetName { get; init; }
        public DateOnly? Day { get; init; }
        public DateOnly? PlannedStart { get; init; }
        public DateTimeOffset? DueAt { get; init; }
        public bool DueFromTarget { get; init; }
        public DateTimeOffset? StartBy { get; init; }
        public DateTimeOffset? RespondedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public decimal? EstimatedHours { get; init; }
        public List<AssigneeRow> Assignees { get; init; } = [];
        public List<AccessWindow> AccessWindows { get; init; } = [];
        public bool OnlyWhenStopped { get; init; }
        public decimal? ExpectedStopHours { get; init; }
    }
    private sealed class AssigneeRow { public Guid EmployeeId { get; init; } public decimal? PlannedHours { get; init; } }

    private static IQueryable<JobRow> Project(IQueryable<WorkOrder> q, IQueryable<FourC.Cmms.Core.Pm.PmTask> tasks) => q.Select(w => new JobRow
    {
        Id = w.Id, Number = w.Number, Title = w.Title, Priority = w.Priority, Status = w.Status, SiteId = w.SiteId, AssetId = w.AssetId,
        AssetNumber = w.Asset!.AssetNumber, AssetName = w.Asset.Name, Day = w.PlannedStart ?? w.DueDate, PlannedStart = w.PlannedStart, DueAt = w.DueAt,
        DueFromTarget = w.DueFromTarget, StartBy = w.StartBy, RespondedAt = w.RespondedAt, CompletedAt = w.CompletedAt, EstimatedHours = w.EstimatedHours,
        Assignees = w.Assignees.Select(a => new AssigneeRow { EmployeeId = a.EmployeeId, PlannedHours = a.PlannedHours }).ToList(),
        AccessWindows = w.AccessWindows, OnlyWhenStopped = w.OnlyWhenStopped,
        // The expected stop of the work template it came from (0.19.0), for agreeing shutdowns on asset rows.
        ExpectedStopHours = tasks.Where(t => t.Id == w.PmTaskId).Select(t => t.ExpectedStopHours).FirstOrDefault(),
    });

    public static void MapPlanningBoardEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/planning").WithTags("Planning board");

        g.MapGet("/board", async (DateOnly from, DateOnly to, Guid? siteId, Guid? tradeId, Guid? teamId, bool? onlyWhenStopped, CmmsDbContext db, ITenantContext ctx,
            PermissionService perms, CancellationToken ct) =>
        {
            if (to < from || to.DayNumber - from.DayNumber >= MaxDays) throw new DomainException("Choose a range of up to six weeks.");
            var settings = await SlaQueries.SettingsAsync(db, ct);
            var now = DateTimeOffset.UtcNow;
            var zone = Sla.Zone(settings.TimeZone);
            var today = Sla.LocalDay(now, zone);

            var employees = await db.Employees.AsNoTracking().Where(e => e.IsActive).Select(e => new { e.Id, e.FullName, e.TradeIds, e.SiteIds, e.WorkingHours }).ToListAsync(ct);
            var siteHours = (await db.Sites.AllSites().AsNoTracking().Select(s => new { s.Id, s.Settings }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Settings.WorkingHours);
            var members = teamId is { } tm ? await db.Teams.AsNoTracking().Where(x => x.Id == tm).Select(x => x.MemberIds).FirstOrDefaultAsync(ct) ?? [] : null;
            // People limited to some sites see the technicians who work there (or everywhere), not those of sites they can't see.
            var visible = db.CurrentSiteIds;
            var people = employees.Where(e => (siteId is not { } s || e.SiteIds.Count == 0 || e.SiteIds.Contains(s)) && (tradeId is not { } t || e.TradeIds.Contains(t))
                    && (visible is null || e.SiteIds.Count == 0 || e.SiteIds.Any(visible.Contains))
                    && (members is null || members.Contains(e.Id)))
                .OrderBy(e => e.FullName).Select(e =>
                {
                    var week = FourC.Cmms.Core.People.WorkingHours.ForEmployee(e.WorkingHours, e.SiteIds, siteHours, settings);
                    return new BoardPerson(e.Id, e.FullName, e.TradeIds, Enum.GetValues<DayOfWeek>().Select(d => FourC.Cmms.Core.People.WorkingHours.HoursOn(week, d)).ToArray());
                }).ToList();

            var q = db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold);
            if (siteId is { } site) q = q.Where(w => w.SiteId == site);
            if (onlyWhenStopped == true) q = q.Where(w => w.OnlyWhenStopped);
            if (tradeId is { } trade)
            {
                var ofTrade = employees.Where(e => e.TradeIds.Contains(trade)).Select(e => e.Id).ToList();
                q = q.Where(w => w.TradeId == trade || w.Assignees.Any(a => ofTrade.Contains(a.EmployeeId)));
            }
            if (teamId is { } team)
            {
                // The team's work, and work its members are on.
                var crew = members ?? [];
                q = q.Where(w => w.TeamId == team || w.Assignees.Any(a => crew.Contains(a.EmployeeId)));
            }
            // Three bounded reads: the range, the newest leftovers before it, and work without a date (not every open job there is).
            var leftoverBefore = from < today ? from : today;
            var tasks = db.PmTasks.AsNoTracking();
            var rows = (await Project(q.Where(w => (w.PlannedStart ?? w.DueDate) >= from && (w.PlannedStart ?? w.DueDate) <= to), tasks).ToListAsync(ct))
                .Concat(await Project(q.Where(w => (w.PlannedStart ?? w.DueDate) < leftoverBefore).OrderByDescending(w => w.PlannedStart ?? w.DueDate).Take(MaxTray), tasks).ToListAsync(ct))
                .Concat(await Project(q.Where(w => w.PlannedStart == null && w.DueDate == null).OrderByDescending(w => w.Priority).Take(MaxTray), tasks).ToListAsync(ct))
                .ToList();
            var jobs = rows.Select(w => new BoardJob(w.Id, w.Number, w.Title, w.Priority, w.Status, w.SiteId, w.AssetId, w.AssetNumber, w.AssetName, w.Day, w.PlannedStart, w.DueAt,
                SlaQueries.Work(settings, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt, now), w.EstimatedHours,
                // Each person's share: their own planned hours, else the estimate split between the people on it.
                w.Assignees.Select(a => new BoardAssignee(a.EmployeeId, a.PlannedHours
                    ?? (w.EstimatedHours is { } est && w.Assignees.Count > 0 ? Math.Round(est / w.Assignees.Count, 2) : null), a.PlannedHours)).ToList(),
                w.AccessWindows, w.Day is { } day && !AccessWindows.Covers(w.AccessWindows, day, zone), w.OnlyWhenStopped, w.ExpectedStopHours)).ToList();

            // Each person's days (0.19.0): available hours after time away and extra time, and planned hours from all their visible work.
            var ids = people.Select(p => p.Id).ToList();
            var available = await AvailabilityQueries.DaysAsync(db, ids, from, to, ct);
            var planned = await PlanningLoad.HoursAsync(db, ids, from, to, ct);
            people = people.Select(p => p with
            {
                Days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).Select(d =>
                {
                    var a = available.GetValueOrDefault(p.Id)?.GetValueOrDefault(d);
                    return new BoardDay(d, a?.Available ?? 0, planned.GetValueOrDefault((p.Id, d)), a?.Slots ?? []);
                }).ToList(),
            }).ToList();

            var pms = (await perms.GetAsync(ctx.UserId!.Value)).Contains(Permissions.PmView)
                ? (await PmEndpoints.ForecastAsync(db, from, to, siteId, ct)).Where(x => x.Date >= from && x.Date <= to).ToList()
                : [];
            return Results.Ok(new
            {
                CapacityHours = settings.DailyCapacityHours,
                People = people,
                Jobs = jobs.Where(j => j.Day >= from && j.Day <= to).ToList(),
                // Not done on its day, before today and before the board's range: to be planned again.
                Earlier = jobs.Where(j => j.Day < from && j.Day < today).OrderByDescending(j => j.Day).Take(MaxTray).ToList(),
                Unscheduled = jobs.Where(j => j.Day is null).OrderByDescending(j => j.Priority).Take(MaxTray).ToList(),
                Pms = pms,
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/move", async (MoveInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == input.WorkOrderId, ct);
            if (w is null) return Results.NotFound();
            if (!WorkOrderWorkflow.IsActive(w.Status))
                return Results.Problem(title: Translator.T($"This work order is {w.Status.ToString().ToLowerInvariant()}, so it isn't planned any more."), statusCode: 400);
            if (input.ToEmployeeId is { } to && !await db.Employees.AnyAsync(e => e.Id == to && e.IsActive, ct))
                return Results.Problem(title: Translator.T("Choose an active person."), statusCode: 400);
            if (input.FromEmployeeId is { } gone && w.Assignees.All(a => a.EmployeeId != gone))
                return Results.Problem(title: Translator.T("That person is no longer on this work order. Refresh the board."), statusCode: 400);

            string? warning = null;
            if (input.Day is { } day)
            {
                // The form refuses a planned start after the due date; on the board it only warns (the supervisor sees the clash).
                w.PlannedStart = day;
                if (w.DueDate is { } due && day > due) warning = $"Planned after its due date ({due:d MMM yyyy}).";
            }
            var before = w.Assignees.Select(a => a.EmployeeId).ToHashSet();
            if (input.FromEmployeeId is { } from)
            {
                var moving = w.Assignees.First(a => a.EmployeeId == from);
                w.Assignees.Remove(moving);
                if (input.ToEmployeeId is { } target && w.Assignees.All(a => a.EmployeeId != target))
                    w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = target, PlannedHours = moving.PlannedHours });
            }
            else if (input.ToEmployeeId is { } added && w.Assignees.All(a => a.EmployeeId != added))
                w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = added, PlannedHours = input.PlannedHours is >= 0 ? input.PlannedHours : null });
            await mail.AssigneesAddedAsync(db, ctx, w, w.Assignees.Select(a => a.EmployeeId).Where(e => !before.Contains(e)).ToList(), ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { warning = Join(warning, await PeopleWarningAsync(db, w, ct)) });
        }).RequirePermission(Permissions.WorkOrdersEdit);

        // "Assign to me" (0.19.0): someone who can do the work takes it while nobody is on it.
        g.MapPost("/take", async (TakeInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = await db.Employees.AsNoTracking().Where(e => e.UserId == ctx.UserId && e.IsActive).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct)
                     ?? throw new DomainException("Your account isn't linked to an employee.");
            var w = await db.WorkOrders.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == input.WorkOrderId, ct);
            if (w is null) return Results.NotFound();
            if (!WorkOrderWorkflow.IsActive(w.Status))
                return Results.Problem(title: Translator.T($"This work order is {w.Status.ToString().ToLowerInvariant()}, so it isn't planned any more."), statusCode: 400);
            const string Taken = "Someone was assigned meanwhile. Refresh the board.";
            if (w.Assignees.Count > 0) throw new DomainException(Taken);
            w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = me });
            // Touching the work order makes its row version guard the take: a second person pressing at the same moment fails.
            w.UpdatedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new DomainException(Taken); }
            catch (DbUpdateException) { throw new DomainException(Taken); }
            return Results.Ok(new { warning = await PeopleWarningAsync(db, w, ct) });
        }).RequirePermission(Permissions.WorkOrdersExecute);
    }

    private static string? Join(params string?[] parts) => parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() is { Count: > 0 } l ? string.Join(" ", l) : null;

    /// <summary>
    /// For each person on the work order, on its day (0.19.0): away all day, or over their available hours. Warnings only — the
    /// board never refuses a move for them.
    /// </summary>
    private static async Task<string?> PeopleWarningAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        if ((w.PlannedStart ?? w.DueDate) is not { } day || w.Assignees.Count == 0) return null;
        var ids = w.Assignees.Select(a => a.EmployeeId).ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var available = await AvailabilityQueries.DaysAsync(db, ids, day, day, ct);
        var planned = await PlanningLoad.HoursAsync(db, ids, day, day, ct);
        var notes = new List<string>();
        foreach (var id in ids)
        {
            if (available.GetValueOrDefault(id)?.GetValueOrDefault(day) is not { } a) continue;
            var name = names.GetValueOrDefault(id) ?? "";
            if (a.Available == 0 && a.Slots.FirstOrDefault(x => x.Kind == FourC.Cmms.Core.Planning.AvailabilityKind.Away) is { } away)
                notes.Add(Translator.T($"{name} is away on that day ({away.Name})."));
            else if (planned.GetValueOrDefault((id, day)) is var hours && hours > a.Available)
                notes.Add(Translator.T($"This takes {name} to {hours:0.##} of {a.Available:0.##} hours on {day:d MMM}."));
        }
        return Join([.. notes]);
    }
}
