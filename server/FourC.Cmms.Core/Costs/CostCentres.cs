using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Costs;

/// <summary>
/// A cost centre (0.20.0): a tree (parent), for one site or for all. Set on locations and assets and given to the work raised
/// for them; every cost of a work order follows the work order's cost centre.
/// </summary>
public class CostCentre : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? SiteId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A budget (0.20.0): one amount per cost centre per month, in the organization's currency.</summary>
public class Budget : TenantEntity
{
    public Guid CostCentreId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}
