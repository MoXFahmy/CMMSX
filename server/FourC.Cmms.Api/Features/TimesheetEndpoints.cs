using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// The daily timesheet (0.19.0): one person's day over the labour records the work orders already hold (entries are added,
/// changed and removed through the work order's labour routes), and the week per person for supervisors.
/// </summary>
public static class TimesheetEndpoints
{
    private static async Task<bool> SeesOthersAsync(ITenantContext ctx, PermissionService perms)
    {
        var mine = await perms.GetAsync(ctx.UserId!.Value);
        return mine.Contains(Permissions.WorkOrdersEdit) || mine.Contains(Permissions.WorkOrdersLogForOthers);
    }

    public static void MapTimesheetEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/timesheet").WithTags("Timesheet");

        g.MapGet("/day", async (DateOnly date, Guid? employeeId, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            var me = await db.Employees.AsNoTracking().Where(e => e.UserId == ctx.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            var who = employeeId ?? me ?? throw new DomainException("Your account isn't linked to an employee.");
            if (who != me && !await SeesOthersAsync(ctx, perms)) return Results.Forbid();
            if (!(await AvailabilityEndpoints.VisiblePeopleAsync(db, ct)).Contains(who)) return Results.NotFound();
            var name = await db.Employees.AsNoTracking().Where(e => e.Id == who).Select(e => e.FullName).FirstAsync(ct);

            // "Mine", as on the phone: assigned to me or to a team I'm in, dated that day — plus every work order I logged hours on that day.
            var teams = (await db.Teams.AsNoTracking().Select(t => new { t.Id, t.MemberIds }).ToListAsync(ct)).Where(t => t.MemberIds.Contains(who)).Select(t => t.Id).ToList();
            var labour = await db.WorkOrderLabor.AsNoTracking().Where(l => l.EmployeeId == who && l.WorkDate == date).OrderBy(l => l.CreatedAt)
                .Select(l => new { l.Id, l.WorkOrderId, l.Hours, l.OvertimeHours, l.Notes }).ToListAsync(ct);
            var logged = labour.Select(l => l.WorkOrderId).Distinct().ToList();
            var orders = await db.WorkOrders.AsNoTracking()
                .Where(w => logged.Contains(w.Id) || (w.Status != WorkOrderStatus.Cancelled && (w.PlannedStart ?? w.DueDate) == date
                            && (w.Assignees.Any(a => a.EmployeeId == who) || (w.TeamId != null && teams.Contains(w.TeamId.Value)))))
                .OrderBy(w => w.Number).Select(w => new { w.Id, w.Number, Title = Bilingual.Arabic && w.TitleAr != null ? w.TitleAr : w.Title, w.Status }).ToListAsync(ct);
            var available = (await AvailabilityQueries.DaysAsync(db, [who], date, date, ct)).GetValueOrDefault(who)?.GetValueOrDefault(date);
            return Results.Ok(new
            {
                Employee = new { Id = who, Name = name }, Date = date, Available = available,
                Rows = orders.Select(w => new
                {
                    WorkOrderId = w.Id, w.Number, w.Title, w.Status, Closed = w.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled,
                    Entries = labour.Where(l => l.WorkOrderId == w.Id).Select(l => new { l.Id, l.Hours, l.OvertimeHours, l.Notes }).ToList(),
                }),
                Hours = labour.Sum(l => l.Hours), OvertimeHours = labour.Sum(l => l.OvertimeHours),
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapGet("/week", async (DateOnly from, Guid? siteId, Guid? teamId, Guid? tradeId, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            if (!await SeesOthersAsync(ctx, perms)) return Results.Forbid();
            var to = from.AddDays(6);
            var visible = await AvailabilityEndpoints.VisiblePeopleAsync(db, ct);
            var members = teamId is { } tm ? await db.Teams.AsNoTracking().Where(x => x.Id == tm).Select(x => x.MemberIds).FirstOrDefaultAsync(ct) ?? [] : null;
            var people = (await db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName, e.SiteIds, e.TradeIds }).ToListAsync(ct))
                .Where(e => visible.Contains(e.Id) && (siteId is not { } s || e.SiteIds.Count == 0 || e.SiteIds.Contains(s)) && (tradeId is not { } t || e.TradeIds.Contains(t))
                            && (members is null || members.Contains(e.Id)))
                .ToList();
            var ids = people.Select(p => p.Id).ToList();
            var logged = (await db.WorkOrderLabor.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.WorkDate >= from && l.WorkDate <= to)
                    .GroupBy(l => new { l.EmployeeId, l.WorkDate }).Select(x => new { x.Key.EmployeeId, x.Key.WorkDate, Hours = x.Sum(l => l.Hours), Overtime = x.Sum(l => l.OvertimeHours) })
                    .ToListAsync(ct))
                .ToDictionary(x => (x.EmployeeId, x.WorkDate));
            var available = await AvailabilityQueries.DaysAsync(db, ids, from, to, ct);
            var days = Enumerable.Range(0, 7).Select(i => from.AddDays(i)).ToList();
            return Results.Ok(new
            {
                Days = days,
                People = people.Select(p => new
                {
                    p.Id, Name = p.FullName,
                    Days = days.Select(d => new
                    {
                        Day = d, Hours = logged.GetValueOrDefault((p.Id, d))?.Hours ?? 0, OvertimeHours = logged.GetValueOrDefault((p.Id, d))?.Overtime ?? 0,
                        Available = available.GetValueOrDefault(p.Id)?.GetValueOrDefault(d)?.Available ?? 0,
                    }),
                }),
            });
        }).RequirePermission(Permissions.WorkOrdersView);
    }
}
