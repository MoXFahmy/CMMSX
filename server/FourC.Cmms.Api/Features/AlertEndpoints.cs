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
        api.MapGet("/alerts", async (CmmsDbContext db, HttpContext http, PermissionService perms, AccessService access, ITenantContext ctx, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            int? overdue = null, myOverdue = null, triage = null, lowStock = null, approvals = null, contracts = null, endingLife = null;
            if (mine.Contains(Permissions.WorkOrdersView))
            {
                // The same rule as the work order list's "late" filter.
                var late = SlaQueries.LateWork(db.WorkOrders, DateTimeOffset.UtcNow);
                overdue = await late.CountAsync(ct);
                var me = await db.Employees.Where(e => e.UserId == ctx.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
                myOverdue = me is null ? 0 : await late.CountAsync(w => w.Assignees.Any(a => a.EmployeeId == me), ct);
            }
            if (mine.Contains(Permissions.RequestsManage))
                triage = await db.WorkRequests.CountAsync(r => r.Status == WorkRequestStatus.New, ct);
            if (mine.Contains(Permissions.InventoryView))
            {
                // The same rule as the parts list's "below re-order level" filter.
                lowStock = await db.Parts.CountAsync(p => p.IsActive && db.StockLevels.Any(l => l.PartId == p.Id && l.ReorderQuantity > 0 && l.QuantityOnHand - (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0) <= l.ReorderLevel), ct);
                // Units reaching the end of their life within 30 days (0.24.0), not scrapped: the units list's "ending within 30 days".
                var limit = (await ContractEndpoints.TodayAsync(db, ct)).AddDays(30);
                endingLife = await db.Assets.CountAsync(a => a.PartId != null && a.EndOfLifeOn != null && a.EndOfLifeOn <= limit
                    && a.Status != FourC.Cmms.Core.Assets.AssetStatus.Disposed, ct);
            }
            if (MyApprovals.Approves(mine))
            {
                // Requests and orders waiting for my signature (0.16.0), by the same rules as My approvals.
                var lists = await MyApprovals.ListsAsync(db, perms, access, Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!), mine, includeSentBack: false, ct);
                approvals = lists.Requests.Count + lists.Orders.Count;
            }
            if (mine.Contains(Permissions.ContractsView))
                // Contracts in their alert window (0.17.0), the ones the person's sites let them see.
                contracts = await ContractEndpoints.NeedingAttention(db.Contracts, await ContractEndpoints.TodayAsync(db, ct)).CountAsync(ct);
            return Results.Ok(new { OverdueWorkOrders = overdue, MyOverdueWorkOrders = myOverdue, RequestsToTriage = triage, PartsBelowReorder = lowStock, WaitingForMyApproval = approvals,
                ContractsNeedingAttention = contracts, UnitsEndingLife = endingLife });
        }).WithTags("Alerts").RequireAuthorization();
    }
}
