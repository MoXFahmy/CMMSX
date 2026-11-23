namespace FourC.Cmms.Core.Purchasing;

/// <summary>Which approval levels an order needs (0.16.0).</summary>
public static class ApprovalLevelRules
{
    /// <summary>
    /// The set is the site's own levels if it has any, else the organization's. Within it, the levels for the order's type if any
    /// exist for that type, else the levels for every type. Then every level whose minimum the base-currency total reaches,
    /// lowest first. Empty: none apply, and the order is approved as before levels existed (one approver within their limit).
    /// </summary>
    public static IReadOnlyList<ApprovalLevel> Required(IReadOnlyCollection<ApprovalLevel> all, Guid siteId, Guid? orderTypeId, decimal totalInBase)
    {
        var set = all.Where(l => l.SiteId == siteId).ToList();
        if (set.Count == 0) set = all.Where(l => l.SiteId is null).ToList();
        var typed = orderTypeId is { } type ? set.Where(l => l.OrderTypeId == type).ToList() : [];
        var levels = typed.Count > 0 ? typed : set.Where(l => l.OrderTypeId is null).ToList();
        return levels.Where(l => l.MinAmount <= totalInBase).OrderBy(l => l.MinAmount).ToList();
    }
}
