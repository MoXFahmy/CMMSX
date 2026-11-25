using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Order lines made from purchase-request lines (0.16.0-4, 0.16.0-5, 0.29.1). A link is checked when an order is saved: the request line must be one
/// the person can see (their organization and sites), its request approved, and the line not on another live order. Cancelled orders keep their
/// links, so there is no unique index; instead the buyer who links a line also writes to its request under the request's row version, and when
/// two buyers do it together the second save finds the version changed and is told so.
/// </summary>
internal static class RequestLinks
{
    public const string Lost = "This request line was just ordered by someone else.";

    /// <summary>409 with the specific message; the caller returns it from the endpoint.</summary>
    public static IResult LostResult() => Results.Problem(title: Translator.T(Lost), statusCode: 409);

    /// <summary>A failed save is the lost race (rather than the order's own row version) when it names a purchase request.</summary>
    public static bool IsLost(DbUpdateConcurrencyException ex) => ex.Entries.Any(e => e.Entity is PurchaseRequest);

    /// <summary>
    /// Checks the links that are new on an order's lines (a line that already carried its link on this order stays as it is) and, for the ones that pass,
    /// puts their requests in <paramref name="claims"/>. The requests are loaded (tracked) BEFORE the live-order check, so the version they are
    /// claimed under is older than anything the check saw.
    /// </summary>
    public static async Task ValidateAsync(CmmsDbContext db, Validator v, IReadOnlyList<PurchasingEndpoints.LineInput> lines, Guid? orderId, List<PurchaseRequest> claims, CancellationToken ct)
    {
        var linked = lines.Where(l => l.PurchaseRequestLineId is not null).ToList();
        if (linked.Count == 0) return;
        if (linked.Select(l => l.PurchaseRequestLineId).Distinct().Count() != linked.Count) { v.Add("lines", "List each request line once."); return; }
        var thisOrder = orderId ?? Guid.Empty;
        var kept = orderId is null ? [] : await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == thisOrder && l.PurchaseRequestLineId != null)
            .ToDictionaryAsync(l => l.Id, l => l.PurchaseRequestLineId!.Value, ct);
        var fresh = linked.Where(l => !(l.Id is { } id && kept.TryGetValue(id, out var was) && was == l.PurchaseRequestLineId)).Select(l => l.PurchaseRequestLineId!.Value).ToList();
        if (fresh.Count == 0) return;

        // What the person can see: another organization's line, or one at a site they can't see, is "unknown".
        var found = await db.PurchaseRequestLines.AsNoTracking().Where(l => fresh.Contains(l.Id)).ToListAsync(ct);
        var requestIds = found.Select(l => l.PurchaseRequestId).Distinct().ToList();
        var requests = await db.PurchaseRequests.Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        // Where those lines already are, on any order at any site (a live one blocks); the names go only to people who can see that order.
        var onOrders = await db.PurchaseOrderLines.AllSites().AsNoTracking().Where(l => l.PurchaseRequestLineId != null && fresh.Contains(l.PurchaseRequestLineId.Value) && l.PurchaseOrderId != thisOrder)
            .Select(l => new { RequestLineId = l.PurchaseRequestLineId!.Value, l.PurchaseOrderId }).ToListAsync(ct);
        var orderIds = onOrders.Select(x => x.PurchaseOrderId).Distinct().ToList();
        var live = await db.PurchaseOrders.AllSites().AsNoTracking().Where(o => orderIds.Contains(o.Id) && o.Status != PurchaseOrderStatus.Cancelled).Select(o => o.Id).ToListAsync(ct);
        var named = await db.PurchaseOrders.AsNoTracking().Where(o => live.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);

        foreach (var id in fresh)
        {
            var line = found.FirstOrDefault(l => l.Id == id);
            if (line is null || !requests.TryGetValue(line.PurchaseRequestId, out var request)) { v.Add("lines", "Unknown request line."); continue; }
            var taken = onOrders.Where(x => x.RequestLineId == id && live.Contains(x.PurchaseOrderId)).Select(x => (Guid?)x.PurchaseOrderId).FirstOrDefault();
            if (taken is { } on)
            {
                v.Add("lines", named.TryGetValue(on, out var number) ? $"{request.Number} line {line.LineNumber} is already on {number}." : $"{request.Number} line {line.LineNumber} is already on an order you can't see.");
                continue;
            }
            // A request whose every line is on an order is "Ordered": "already on" says it better than "isn't approved", so it is checked first.
            if (request.Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartlyOrdered)) { v.Add("lines", $"{request.Number} isn't approved."); continue; }
            if (!claims.Contains(request)) claims.Add(request);
        }
    }

    /// <summary>
    /// Writes to each request (its "updated" stamp) so the save is checked against the version read in <see cref="ValidateAsync"/>; the caller saves.
    /// </summary>
    public static void Claim(CmmsDbContext db, IEnumerable<PurchaseRequest> requests)
    {
        foreach (var r in requests) db.Entry(r).Property(x => x.UpdatedAt).IsModified = true;
    }
}
