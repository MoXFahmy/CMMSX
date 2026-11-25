using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Removing a company's contacts (0.17.0-1, 0.29.1).</summary>
internal static class CompanyContacts
{
    /// <summary>
    /// A contact named on a contractor line of a work order that isn't finished can't be removed: the line would lose the person and the technician could
    /// not record hours on it. Returns the refusal, or null when the contacts can go. The lines are looked up at every site (they are the company's, not the
    /// person's), but the message names only the work orders the person can see, then counts the rest: a hidden site's work-order number is never shown.
    /// </summary>
    public static async Task<string?> RefusalAsync(CmmsDbContext db, Supplier company, List<PurchasingEndpoints.SupplierContactInput>? sent, CancellationToken ct)
    {
        if (sent is null) return null; // the import sends none: contacts stay as they are
        var removed = company.Contacts.Where(c => !sent.Any(n => n.Id == c.Id)).ToList();
        if (removed.Count == 0) return null;
        var removedIds = removed.Select(c => c.Id).ToList();
        var named = await db.WorkOrderContractors.AllSites().AsNoTracking().Where(l => l.ContactId != null && removedIds.Contains(l.ContactId.Value))
            .Select(l => new { l.WorkOrderId, ContactId = l.ContactId!.Value }).ToListAsync(ct);
        if (named.Count == 0) return null;
        var workOrderIds = named.Select(l => l.WorkOrderId).Distinct().ToList();
        var open = await db.WorkOrders.AllSites().AsNoTracking().Where(w => workOrderIds.Contains(w.Id) && w.Status < WorkOrderStatus.Completed).Select(w => w.Id).ToListAsync(ct);
        if (open.Count == 0) return null;
        var seen = await db.WorkOrders.AsNoTracking().Where(w => open.Contains(w.Id)).OrderBy(w => w.Number).Select(w => w.Number).ToListAsync(ct);
        var hidden = open.Count - seen.Count;
        var who = string.Join(", ", removed.Where(c => named.Any(l => l.ContactId == c.Id)).Select(c => c.Name));
        const string then = "Pick someone else on those lines first.";
        if (seen.Count == 0) return $"Can't remove {who}: named on open work orders, {hidden} of them you can't see. {then}";
        var list = string.Join(", ", seen);
        return hidden == 0 ? $"Can't remove {who}: named on open work orders {list}. {then}"
            : $"Can't remove {who}: named on open work orders {list} and {hidden} more you can't see. {then}";
    }
}
