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
        string? AssetName, DateOnly? Day, DateOnly? PlannedStart, DateTimeOffset? DueAt, SlaView Sla, decimal? EstimatedHours, List<BoardAssignee> Assignees);
    public sealed record BoardPerson(Guid Id, string Name, List<Guid> TradeIds);
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
    }
    private sealed class AssigneeRow { public Guid EmployeeId { get; init; } public decimal? PlannedHours { get; init; } }

    private static IQueryable<JobRow> Project(IQueryable<WorkOrder> q) => q.Select(w => new JobRow
    {
        Id = w.Id, Number = w.Number, Title = w.Title, Priority = w.Priority, Status = w.Status, SiteId = w.SiteId, AssetId = w.AssetId,
        AssetNumber = w.Asset!.AssetNumber, AssetName = w.Asset.Name, Day = w.PlannedStart ?? w.DueDate, PlannedStart = w.PlannedStart, DueAt = w.DueAt,
        DueFromTarget = w.DueFromTarget, StartBy = w.StartBy, RespondedAt = w.RespondedAt, CompletedAt = w.CompletedAt, EstimatedHours = w.EstimatedHours,
        Assignees = w.Assignees.Select(a => new AssigneeRow { EmployeeId = a.EmployeeId, PlannedHours = a.PlannedHours }).ToList(),
    });

    public static void MapPlanningBoardEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/planning").WithTags("Planning board");

        g.MapGet("/board", async (DateOnly from, DateOnly to, Guid? siteId, Guid? tradeId, Guid? teamId, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            if (to < from || to.DayNumber - from.DayNumber >= MaxDays) throw new DomainException("Choose a range of up to six weeks.");
            var settings = await SlaQueries.SettingsAsync(db, ct);
            var now = DateTimeOffset.UtcNow;
            var today = Sla.LocalDay(now, Sla.Zone(settings.TimeZone));

            var employees = await db.Employees.AsNoTracking().Where(e => e.IsActive).Select(e => new { e.Id, e.FullName, e.TradeIds, e.SiteIds }).ToListAsync(ct);
            var members = teamId is { } tm ? await db.Teams.AsNoTracking().Where(x => x.Id == tm).Select(x => x.MemberIds).FirstOrDefaultAsync(ct) ?? [] : null;
            var people = employees.Where(e => (siteId is not { } s || e.SiteIds.Count == 0 || e.SiteIds.Contains(s)) && (tradeId is not { } t || e.TradeIds.Contains(t))
                    && (members is null || members.Contains(e.Id)))
                .OrderBy(e => e.FullName).Select(e => new BoardPerson(e.Id, e.FullName, e.TradeIds)).ToList();

            var q = db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold);
            if (siteId is { } site) q = q.Where(w => w.SiteId == site);
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
            var rows = (await Project(q.Where(w => (w.PlannedStart ?? w.DueDate) >= from && (w.PlannedStart ?? w.DueDate) <= to)).ToListAsync(ct))
                .Concat(await Project(q.Where(w => (w.PlannedStart ?? w.DueDate) < leftoverBefore).OrderByDescending(w => w.PlannedStart ?? w.DueDate).Take(MaxTray)).ToListAsync(ct))
                .Concat(await Project(q.Where(w => w.PlannedStart == null && w.DueDate == null).OrderByDescending(w => w.Priority).Take(MaxTray)).ToListAsync(ct))
                .ToList();
            var jobs = rows.Select(w => new BoardJob(w.Id, w.Number, w.Title, w.Priority, w.Status, w.SiteId, w.AssetId, w.AssetNumber, w.AssetName, w.Day, w.PlannedStart, w.DueAt,
                SlaQueries.Work(settings, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt, now), w.EstimatedHours,
                // Each person's share: their own planned hours, else the estimate split between the people on it.
                w.Assignees.Select(a => new BoardAssignee(a.EmployeeId, a.PlannedHours
                    ?? (w.EstimatedHours is { } est && w.Assignees.Count > 0 ? Math.Round(est / w.Assignees.Count, 2) : null), a.PlannedHours)).ToList())).ToList();

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
            return Results.Ok(new { warning });
        }).RequirePermission(Permissions.WorkOrdersEdit);
    }
}
