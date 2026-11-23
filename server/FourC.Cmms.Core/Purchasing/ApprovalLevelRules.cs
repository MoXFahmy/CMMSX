namespace FourC.Cmms.Core.Purchasing;

/// <summary>Which approval levels an order needs (0.16.0).</summary>
public static class ApprovalLevelRules
{
    /// <summary>
    /// The most specific levels win: the site's levels for the order's type, else the site's levels for every type, else the
    /// organization's levels for the type, else the organization's levels for every type. Then every level whose minimum the
    /// base-currency total reaches, lowest first. Empty: none apply, and the order is approved as before levels existed (one
    /// approver within their limit). So a site that adds a Capital rule keeps the organization's levels for its Parts orders.
    /// </summary>
    public static IReadOnlyList<ApprovalLevel> Required(IReadOnlyCollection<ApprovalLevel> all, Guid siteId, Guid? orderTypeId, decimal totalInBase)
    {
        Func<ApprovalLevel, bool>[] sets =
        [
            l => l.SiteId == siteId && orderTypeId != null && l.OrderTypeId == orderTypeId,
            l => l.SiteId == siteId && l.OrderTypeId == null,
            l => l.SiteId == null && orderTypeId != null && l.OrderTypeId == orderTypeId,
            l => l.SiteId == null && l.OrderTypeId == null,
        ];
        var levels = sets.Select(match => all.Where(match).ToList()).FirstOrDefault(s => s.Count > 0) ?? [];
        return levels.Where(l => l.MinAmount <= totalInBase).OrderBy(l => l.MinAmount).ToList();
    }
}
