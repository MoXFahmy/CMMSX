using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Purchasing;

namespace FourC.Cmms.Core.Contracts;

/// <summary>Where a contract stands today; worked out from its dates, never stored.</summary>
public enum ContractStatus { Upcoming = 1, Active = 2, Expired = 3, Terminated = 4 }

/// <summary>What the expected number of work orders is counted per.</summary>
public enum ExpectedPer { Year = 1, Quarter = 2, Month = 3 }

/// <summary>Kind of contract (Maintenance, Inspection, Lease, Warranty — an editable list, 0.17.0).</summary>
public class ContractType : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A service contract with a contractor company (0.17.0). <see cref="EndDate"/> is the end of the latest period: automatic renewal
/// adds periods and moves it. A contract with a site covers only that site's assets and work; without one it is organization-wide.
/// </summary>
public class Contract : TenantEntity
{
    public required string Number { get; set; }
    public required string Title { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>The user who looks after the contract (gets its alerts).</summary>
    public Guid? ManagerId { get; set; }
    public Guid? ContractTypeId { get; set; }
    public ContractType? ContractType { get; set; }
    public Guid? TradeId { get; set; }
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
    public string? Notes { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public string? PaymentTerms { get; set; }
    public int? ExpectedWorkOrders { get; set; }
    public ExpectedPer? ExpectedPer { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalMonths { get; set; }
    /// <summary>Days before the end by which the renewal must be stopped.</summary>
    public int NoticeDays { get; set; }
    /// <summary>Days before the notice deadline (or the end) that the contract needs attention.</summary>
    public int AlertDays { get; set; } = 60;
    public DateOnly? TerminatedOn { get; set; }
    public string? TerminationReason { get; set; }
    /// <summary>The deadline an alert e-mail was already sent for; a new deadline (after a renewal) alerts again.</summary>
    public DateOnly? AlertSentFor { get; set; }
    public List<ContractPeriod> Periods { get; set; } = [];
    public List<ContractAmendment> Amendments { get; set; } = [];
    public List<ContractAsset> Assets { get; set; } = [];
}

/// <summary>The first term of a contract, or a renewal.</summary>
public class ContractPeriod : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ContractId { get; set; }
    public int Number { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal? Value { get; set; }
    public bool IsRenewal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A change to a contract; a new value applies to the current period and later renewals.</summary>
public class ContractAmendment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ContractId { get; set; }
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal? NewValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>An asset a contract covers, with an optional yearly cost.</summary>
public class ContractAsset : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ContractId { get; set; }
    public Guid AssetId { get; set; }
    public decimal? YearlyCost { get; set; }
}
