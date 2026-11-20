using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Where a record stands against its deadlines (shown on lists and pages).</summary>
public sealed record SlaView(SlaState Response, SlaState Completion, DateTimeOffset? RespondBy, DateTimeOffset? DueAt, DateTimeOffset? RespondedAt);

/// <summary>
/// "Late" and "due soon", the same everywhere (lists, dashboard, alert bell, reports). Late work: still open and past its
/// due time, or past its start-by with nobody having acted on it yet.
/// </summary>
internal static class SlaQueries
{
    private static bool Active(WorkOrderStatus s) => WorkOrderWorkflow.IsActive(s);

    internal static IQueryable<WorkOrder> LateCompletion(IQueryable<WorkOrder> q, DateTimeOffset now) =>
        q.Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold) && w.DueAt < now);

    internal static IQueryable<WorkOrder> LateResponse(IQueryable<WorkOrder> q, DateTimeOffset now) =>
        q.Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
                     && w.StartBy < now && w.RespondedAt == null);

    internal static IQueryable<WorkOrder> LateWork(IQueryable<WorkOrder> q, DateTimeOffset now) =>
        q.Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
                     && (w.DueAt < now || (w.StartBy < now && w.RespondedAt == null)));

    /// <summary>Requests still waiting for a decision after their response or completion deadline.</summary>
    internal static IQueryable<WorkRequest> LateRequests(IQueryable<WorkRequest> q, DateTimeOffset now) =>
        q.Where(r => r.Status == WorkRequestStatus.New && (r.RespondBy < now || r.DueAt < now));

    /// <summary>The target's length: what "due soon" is a quarter of. None for hand-set and PM deadlines (they use the 2-hour rule).</summary>
    private static TimeSpan? Window(int? minutes) => minutes is { } m ? TimeSpan.FromMinutes(m) : null;

    internal static SlaView Work(TenantSettings s, WorkPriority priority, WorkOrderStatus status, DateTimeOffset? startBy, DateTimeOffset? respondedAt,
        DateTimeOffset? dueAt, bool dueFromTarget, DateTimeOffset? completedAt, DateTimeOffset now)
    {
        var target = Sla.For(s, priority);
        var cancelled = status == WorkOrderStatus.Cancelled;
        var response = cancelled ? SlaState.None : Sla.State(startBy, respondedAt, now, Window(target?.RespondMinutes));
        var completion = cancelled ? SlaState.None : Sla.State(dueAt, completedAt, now, dueFromTarget ? Window(target?.CompleteMinutes) : null);
        return new SlaView(response, completion, startBy, dueAt, respondedAt);
    }

    internal static SlaView Request(TenantSettings s, WorkPriority priority, WorkRequestStatus status, DateTimeOffset? respondBy, DateTimeOffset? decidedAt,
        DateTimeOffset? dueAt, bool dueFromTarget, SlaState? workCompletion, DateTimeOffset now)
    {
        var target = Sla.For(s, priority);
        var response = status == WorkRequestStatus.Cancelled ? SlaState.None : Sla.State(respondBy, decidedAt, now, Window(target?.RespondMinutes));
        // Approved: its work order's completion. Rejected or cancelled: nothing to complete.
        var completion = status switch
        {
            WorkRequestStatus.New => Sla.State(dueAt, null, now, dueFromTarget ? Window(target?.CompleteMinutes) : null),
            WorkRequestStatus.Approved => workCompletion ?? SlaState.None,
            _ => SlaState.None,
        };
        return new SlaView(response, completion, respondBy, dueAt, decidedAt);
    }

    /// <summary>Open work orders whose due time is coming up (less than a quarter of the target, or 2 hours, left).</summary>
    internal static async Task<int> DueSoonAsync(IQueryable<WorkOrder> q, TenantSettings s, DateTimeOffset now, CancellationToken ct)
    {
        var longest = s.SlaTargets.Select(t => t.CompleteMinutes ?? 0).DefaultIfEmpty(0).Max() / 4.0;
        var horizon = now.AddMinutes(Math.Max(longest, Sla.DueSoonWithoutTarget.TotalMinutes));
        var rows = await q.Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
                                      && w.DueAt >= now && w.DueAt < horizon)
            .Select(w => new { w.Priority, w.DueAt, w.DueFromTarget }).ToListAsync(ct);
        return rows.Count(w => Sla.State(w.DueAt, null, now, w.DueFromTarget ? Window(Sla.For(s, w.Priority)?.CompleteMinutes) : null) == SlaState.DueSoon);
    }

    internal static async Task<TenantSettings> SettingsAsync(CmmsDbContext db, CancellationToken ct) =>
        (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
}
