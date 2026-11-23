using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;

namespace FourC.Cmms.Core.Purchasing;

/// <summary>Where a purchase request is (0.16.0). Rejected requests go back to the requester to fix and submit again.</summary>
public enum PurchaseRequestStatus { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, PartlyOrdered = 5, Ordered = 6, Closed = 7, Cancelled = 8 }

/// <summary>
/// Someone asks for parts or services to be bought (0.16.0). One person approves or rejects it ("do we need this?"); buyers
/// then turn its approved lines into draft purchase orders, where the amount levels decide who signs ("can we afford it?").
/// </summary>
public class PurchaseRequest : TenantEntity
{
    public required string Number { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public Guid? RequestedById { get; set; }
    public string? RequestedByName { get; set; }
    /// <summary>When the request is needed; the default for its lines.</summary>
    public DateOnly? NeededBy { get; set; }
    public string? Notes { get; set; }
    /// <summary>Users told when it is submitted, on top of the site's purchase-request recipients.</summary>
    public List<Guid> RecipientIds { get; set; } = [];
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<PurchaseRequestLine> Lines { get; set; } = [];
}

/// <summary>One thing asked for: a stock part, or a free-text item or service.</summary>
public class PurchaseRequestLine : Entity
{
    public Guid PurchaseRequestId { get; set; }
    public int LineNumber { get; set; }
    public Guid? PartId { get; set; }
    public Part? Part { get; set; }
    public required string Description { get; set; }
    public string? UnitOfMeasure { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly? NeededBy { get; set; }
    /// <summary>The job it is for: received parts are issued straight to it (the order line carries it).</summary>
    public Guid? WorkOrderId { get; set; }
    public Guid? AssetId { get; set; }
    /// <summary>Where a part should arrive.</summary>
    public Guid? StoreId { get; set; }
    /// <summary>Optional estimate in the base currency, to help the approver.</summary>
    public decimal? EstimatedUnitPrice { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A message on a purchase request's thread.</summary>
public class PurchaseRequestComment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PurchaseRequestId { get; set; }
    public required string Body { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class PurchaseRequestRules
{
    /// <summary>
    /// A request's status once <paramref name="orderedLines"/> of its <paramref name="lines"/> are on live orders. Only moves among
    /// Approved, Partly ordered and Ordered (an order cancelled or deleted can move it back); any other status stays.
    /// </summary>
    public static PurchaseRequestStatus StatusAfterOrdering(PurchaseRequestStatus current, int lines, int orderedLines)
    {
        if (current is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartlyOrdered or PurchaseRequestStatus.Ordered)) return current;
        if (orderedLines <= 0) return PurchaseRequestStatus.Approved;
        return orderedLines >= lines ? PurchaseRequestStatus.Ordered : PurchaseRequestStatus.PartlyOrdered;
    }
}
