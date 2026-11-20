using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Sets response and completion deadlines on requests and work orders from the organization's targets (<see cref="Sla"/>).
/// A deadline typed by hand always wins; a date-only one means the end of that day in the organization's time zone.
/// </summary>
internal static class Deadlines
{
    /// <summary>A deadline sent back within the same minute (the form keeps minutes only) is not a change.</summary>
    private static readonly TimeSpan SameMinute = TimeSpan.FromMinutes(1);

    /// <summary>A deadline typed by hand: a date and time, else a date (the end of that day).</summary>
    private static (DateTimeOffset? At, DateOnly? Day) HandSet(DateTimeOffset? at, DateOnly? day, TimeZoneInfo zone) =>
        at is { } a ? (a, Sla.LocalDay(a, zone)) : day is { } d ? (Sla.EndOfDay(d, zone), d) : (null, null);

    private static void SetDue(WorkOrder w, DateTimeOffset? at, bool fromTarget, TimeZoneInfo zone)
    {
        w.DueAt = at;
        w.DueDate = at is { } a ? Sla.LocalDay(a, zone) : null;
        w.DueFromTarget = fromTarget && at is not null;
    }

    /// <summary>A new work order. <paramref name="applyTargets"/> is false for PM work (its window comes from the schedule).</summary>
    internal static void OnCreate(WorkOrder w, DateTimeOffset? dueAt, DateOnly? dueDate, TenantSettings s, DateTimeOffset raisedAt, bool fromRequest, bool applyTargets)
    {
        var zone = Sla.Zone(s.TimeZone);
        var (at, _) = HandSet(dueAt, dueDate, zone);
        if (at is not null) SetDue(w, at, false, zone);
        else SetDue(w, applyTargets ? Sla.CompleteBy(s, w.Priority, raisedAt) : null, true, zone);
        // A request's response is its decision; PM work has no response target.
        w.StartBy = applyTargets && !fromRequest ? Sla.RespondBy(s, w.Priority, raisedAt) : null;
    }

    /// <summary>
    /// An edited work order: a changed deadline becomes hand-set; otherwise a priority change recomputes target deadlines
    /// from when the work was raised. Saving other fields leaves the deadlines alone.
    /// </summary>
    internal static void OnUpdate(WorkOrder w, DateTimeOffset? dueAt, DateOnly? dueDate, WorkPriority priorityBefore, TenantSettings s, DateTimeOffset raisedAt)
    {
        var zone = Sla.Zone(s.TimeZone);
        var (at, day) = HandSet(dueAt, dueDate, zone);
        var changed = dueAt is { } a ? w.DueAt is not { } current || (a - current).Duration() >= SameMinute : day != w.DueDate;
        var priorityChanged = w.Priority != priorityBefore;
        if (changed)
        {
            // Cleared: back to the priority's target ("leave empty to use the target"), PM work excepted.
            if (at is null && w.PmScheduleId is null && Sla.CompleteBy(s, w.Priority, raisedAt) is { } target) SetDue(w, target, true, zone);
            else SetDue(w, at, false, zone);
        }
        else if (priorityChanged && w.DueFromTarget)
        {
            // The new priority has no target: keep the deadline the job already has, now as if set by hand.
            if (Sla.CompleteBy(s, w.Priority, raisedAt) is { } due) SetDue(w, due, true, zone);
            else w.DueFromTarget = false;
        }
        // The response deadline follows the priority, whether or not the due time was changed in the same edit.
        if (priorityChanged && w.WorkRequestId is null && w.PmScheduleId is null && w.RespondedAt is null) w.StartBy = Sla.RespondBy(s, w.Priority, raisedAt);
    }

    /// <summary>A new request: its response deadline, and its completion deadline (the target, else the end of the "Needed by" day).</summary>
    internal static void OnCreate(WorkRequest r, TenantSettings s, DateTimeOffset raisedAt)
    {
        var zone = Sla.Zone(s.TimeZone);
        r.RespondBy = Sla.RespondBy(s, r.Priority, raisedAt);
        var due = Sla.CompleteBy(s, r.Priority, raisedAt);
        r.DueAt = due ?? (r.RequiredBy is { } d ? Sla.EndOfDay(d, zone) : null);
        r.DueFromTarget = due is not null;
    }

    /// <summary>
    /// The work order's deadline when a request is approved: typed by the approver, else the target of the (possibly new)
    /// priority counted from when the request was raised, else the request's own deadline.
    /// </summary>
    internal static (DateTimeOffset? At, bool FromTarget) OnApprove(WorkRequest r, WorkPriority priority, DateTimeOffset? dueAt, DateOnly? dueDate, TenantSettings s)
    {
        var zone = Sla.Zone(s.TimeZone);
        var (at, _) = HandSet(dueAt, dueDate, zone);
        if (at is not null) return (at, false);
        var target = Sla.CompleteBy(s, priority, r.CreatedAt);
        if (priority != r.Priority)
        {
            if (target is not null) return (target, true);
            // The new priority has no completion target: the requester's "Needed by", else the deadline it already had.
            return (r.RequiredBy is { } d ? Sla.EndOfDay(d, zone) : r.DueAt, false);
        }
        if (r.DueAt is null && target is not null) return (target, true); // targets set after the request was raised
        return (r.DueAt, r.DueFromTarget);
    }
}
