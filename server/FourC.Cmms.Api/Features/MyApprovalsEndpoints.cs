using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// "My approvals" (0.16.0): the requests and orders waiting for my signature, and what was sent back to me. Uses the same checks
/// as the approve buttons, so it never lists something I can't act on. Site access applies through the queries' site filters.
/// </summary>
public static class MyApprovals
{
    public sealed record RequestItem(Guid Id, string Number, string SiteName, string? RequestedByName, DateOnly? NeededBy, int Lines, decimal? EstimatedTotal, DateTimeOffset? SubmittedAt);
    public sealed record OrderItem(Guid Id, string Number, string SiteName, string SupplierName, decimal Total, string Currency, decimal TotalInBase, string? LevelName,
        List<string> SignedBefore, List<string> Next, DateTimeOffset? SubmittedAt);
    public sealed record SentBackItem(string Kind, Guid Id, string Number, string? Reason, DateTimeOffset? At);
    public sealed record Lists(List<RequestItem> Requests, List<OrderItem> Orders, List<SentBackItem> SentBack);

    private const int Cap = 200;

    /// <summary>Whether this person may approve anything at all (else the bell shows no count).</summary>
    public static bool Approves(IReadOnlySet<string> mine) => mine.Contains(Permissions.PurchasingApprove) || mine.Contains(Permissions.PurchasingApproveRequests);

    public static async Task<Lists> ListsAsync(CmmsDbContext db, PermissionService perms, AccessService access, Guid me, IReadOnlySet<string> mine, bool includeSentBack,
        CancellationToken ct)
    {
        var requests = new List<RequestItem>();
        if (mine.Contains(Permissions.PurchasingApproveRequests))
            requests = (await db.PurchaseRequests.AsNoTracking().Where(r => r.Status == PurchaseRequestStatus.Submitted && r.RequestedById != me)
                .OrderBy(r => r.SubmittedAt).Take(Cap)
                .Select(r => new
                {
                    r.Id, r.Number, SiteName = r.Site!.Name, r.RequestedByName, r.NeededBy, Lines = r.Lines.Count, r.SubmittedAt,
                    Estimate = r.Lines.Where(l => l.EstimatedUnitPrice != null).Sum(l => (decimal?)(l.EstimatedUnitPrice * l.Quantity)),
                }).ToListAsync(ct))
                .Select(r => new RequestItem(r.Id, r.Number, r.SiteName, r.RequestedByName, r.NeededBy, r.Lines, r.Estimate is { } e ? Math.Round(e, 2) : null, r.SubmittedAt)).ToList();

        var orders = new List<OrderItem>();
        if (mine.Contains(Permissions.PurchasingApprove))
        {
            var submitted = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Supplier).Include(o => o.Site)
                .Where(o => o.Status == PurchaseOrderStatus.Submitted).OrderBy(o => o.SubmittedAt).Take(Cap).ToListAsync(ct);
            var ids = submitted.Select(o => o.Id).ToList();
            var steps = (await db.PurchaseOrderApprovalSteps.AsNoTracking().Where(s => ids.Contains(s.PurchaseOrderId)).ToListAsync(ct)).ToLookup(s => s.PurchaseOrderId);
            var signatures = (await db.PurchaseOrderSignatures.AsNoTracking().Where(s => ids.Contains(s.PurchaseOrderId)).ToListAsync(ct)).ToLookup(s => s.PurchaseOrderId);
            var limit = await db.Users.Where(u => u.Id == me).Select(u => u.PurchaseApprovalLimit).FirstOrDefaultAsync(ct);
            var userIds = steps.SelectMany(g => g).SelectMany(s => s.ApproverIds).Distinct().ToList();
            var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            foreach (var o in submitted)
            {
                var current = steps[o.Id].Where(s => s.Round == o.ApprovalRound && o.ApprovalRound > 0).OrderBy(s => s.Step).ToList();
                var totalBase = PurchaseOrderMath.TotalInBase(o);
                var waiting = current.FirstOrDefault(s => s.Status == ApprovalStepStatus.Waiting);
                if (current.Count == 0)
                {
                    // Today's rule: anyone with "Approve purchases" within their limit.
                    if (limit is null || totalBase <= limit)
                        orders.Add(new OrderItem(o.Id, o.Number, o.Site!.Name, o.Supplier!.Name, PurchaseOrderMath.Total(o), o.Currency, totalBase, null, [], [], o.SubmittedAt));
                    continue;
                }
                if (waiting is null || !waiting.ApproverIds.Contains(me) || o.SubmittedById == me) continue;
                var sigs = signatures[o.Id].Where(s => s.Round == o.ApprovalRound).ToList();
                if (sigs.Any(s => s.Step == waiting.Step && s.UserId == me)) continue;
                if (!await PurchaseApprovals.MaySignAsync(db, perms, access, o, me, ct)) continue;
                var next = current.FirstOrDefault(s => s.Step > waiting.Step);
                orders.Add(new OrderItem(o.Id, o.Number, o.Site!.Name, o.Supplier!.Name, PurchaseOrderMath.Total(o), o.Currency, totalBase, waiting.Name,
                    sigs.Where(s => s.Approved).OrderBy(s => s.At).Select(s => s.UserName).ToList(),
                    next is null ? [] : next.ApproverIds.Select(a => names.GetValueOrDefault(a) ?? "").Where(n => n.Length > 0).ToList(), o.SubmittedAt));
            }
        }

        var back = new List<SentBackItem>();
        if (includeSentBack)
        {
            back.AddRange((await db.PurchaseRequests.AsNoTracking().Where(r => r.RequestedById == me && r.Status == PurchaseRequestStatus.Rejected)
                .OrderByDescending(r => r.DecidedAt).Take(Cap).Select(r => new { r.Id, r.Number, r.RejectReason, r.DecidedAt }).ToListAsync(ct))
                .Select(r => new SentBackItem("request", r.Id, r.Number, r.RejectReason, r.DecidedAt)));
            var mineDrafts = await db.PurchaseOrders.AsNoTracking()
                .Where(o => o.Status == PurchaseOrderStatus.Draft && o.ApprovalRound > 0 && (o.SubmittedById == me || o.CreatedById == me))
                .Select(o => new { o.Id, o.Number, o.ApprovalRound }).Take(Cap).ToListAsync(ct);
            var draftIds = mineDrafts.Select(d => d.Id).ToList();
            var rejections = (await db.PurchaseOrderSignatures.AsNoTracking().Where(s => draftIds.Contains(s.PurchaseOrderId) && !s.Approved).ToListAsync(ct))
                .ToLookup(s => s.PurchaseOrderId);
            foreach (var d in mineDrafts)
                if (rejections[d.Id].Where(s => s.Round == d.ApprovalRound).OrderByDescending(s => s.At).FirstOrDefault() is { } no)
                    back.Add(new SentBackItem("order", d.Id, d.Number, no.Comment, no.At));
        }
        return new Lists(requests, orders, back.OrderByDescending(b => b.At).ToList());
    }

    public static void MapMyApprovalsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/approvals/mine", async (HttpContext http, CmmsDbContext db, PermissionService perms, AccessService access, CancellationToken ct) =>
        {
            var me = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var mine = await perms.GetAsync(me);
            return Results.Ok(await ListsAsync(db, perms, access, me, mine, includeSentBack: true, ct));
        }).WithTags("Approvals").RequireAuthorization();
    }
}
