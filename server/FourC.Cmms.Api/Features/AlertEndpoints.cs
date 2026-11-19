using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Counts for the header's alert bell. Each count is null when the user may not see that kind of record.</summary>
public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/alerts", async (CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            int? overdue = null, myOverdue = null, triage = null, lowStock = null;
            if (mine.Contains(Permissions.WorkOrdersView))
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                // The same rule as the work order list's "overdue" filter.
                var late = db.WorkOrders.Where(w => w.DueDate < today && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold));
                overdue = await late.CountAsync(ct);
                var me = await db.Employees.Where(e => e.UserId == ctx.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
                myOverdue = me is null ? 0 : await late.CountAsync(w => w.Assignees.Any(a => a.EmployeeId == me), ct);
            }
            if (mine.Contains(Permissions.RequestsManage))
                triage = await db.WorkRequests.CountAsync(r => r.Status == WorkRequestStatus.New, ct);
            if (mine.Contains(Permissions.InventoryView))
                // The same rule as the parts list's "below re-order level" filter.
                lowStock = await db.Parts.CountAsync(p => db.StockLevels.Any(l => l.PartId == p.Id && l.ReorderQuantity > 0 && l.QuantityOnHand <= l.ReorderLevel), ct);
            return Results.Ok(new { OverdueWorkOrders = overdue, MyOverdueWorkOrders = myOverdue, RequestsToTriage = triage, PartsBelowReorder = lowStock });
        }).WithTags("Alerts").RequireAuthorization();
    }
}
