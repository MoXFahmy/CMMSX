using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Planned hours per person and day (0.19.0), worked out on the server from every active work order the viewer can see — the
/// board's filters don't hide a person's other work — so the board's figures and its warnings agree.
/// </summary>
public static class PlanningLoad
{
    public static async Task<Dictionary<(Guid Employee, DateOnly Day), decimal>> HoursAsync(CmmsDbContext db, IReadOnlyCollection<Guid> employeeIds,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, DateOnly), decimal>();
        if (employeeIds.Count == 0 || to < from) return result;
        var ids = employeeIds.Distinct().ToList();
        var jobs = await db.WorkOrders.AsNoTracking()
            .Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
                        && (w.PlannedStart ?? w.DueDate) >= from && (w.PlannedStart ?? w.DueDate) <= to && w.Assignees.Any(a => ids.Contains(a.EmployeeId)))
            .Select(w => new { Day = (w.PlannedStart ?? w.DueDate)!.Value, w.EstimatedHours, Assignees = w.Assignees.Select(a => new { a.EmployeeId, a.PlannedHours }).ToList() })
            .ToListAsync(ct);
        foreach (var j in jobs)
            foreach (var a in j.Assignees.Where(a => ids.Contains(a.EmployeeId)))
            {
                // Their own planned hours, else the estimate split between the people on it (the board's rule since 0.5.0).
                var hours = a.PlannedHours ?? (j.EstimatedHours is { } est && j.Assignees.Count > 0 ? Math.Round(est / j.Assignees.Count, 2) : 0);
                result[(a.EmployeeId, j.Day)] = result.GetValueOrDefault((a.EmployeeId, j.Day)) + hours;
            }
        return result;
    }
}
