using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;

namespace FourC.Cmms.Core.Purchasing;

public enum PurchaseOrderStatus { Draft = 1, Submitted = 2, Approved = 3, PartiallyReceived = 4, Received = 5, Closed = 6, Cancelled = 7 }

public class Supplier : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TaxNumber { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Currency { get; set; }
    public bool IsContractor { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PurchaseOrder : TenantEntity
{
    public required string Number { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public string? RequestedBy { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Base-currency value of one unit of the order currency.</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public string? PaymentTerms { get; set; }
    public string? ShippingMethod { get; set; }
    public string? DeliverTo { get; set; }
    public string? SupplierQuoteRef { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal ShippingCost { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public class PurchaseOrderLine : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    /// <summary>Stock part; null for services / direct purchases.</summary>
    public Guid? PartId { get; set; }
    public Part? Part { get; set; }
    public required string Description { get; set; }
    public string? UnitOfMeasure { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal QuantityReceived { get; set; }
    /// <summary>Store that stock lines are received into.</summary>
    public Guid? StoreId { get; set; }
    /// <summary>Direct purchase for a work order: received cost is charged to it.</summary>
    public Guid? WorkOrderId { get; set; }
    public string? AccountCode { get; set; }

    public decimal NetUnitPrice => Math.Round(UnitPrice * (1 - DiscountPercent / 100m), 4);
    public decimal LineTotal => Math.Round(Quantity * NetUnitPrice, 2);
}

public static class PurchaseOrderMath
{
    public static decimal Subtotal(PurchaseOrder po) => po.Lines.Sum(l => l.LineTotal);
    public static decimal Tax(PurchaseOrder po) => Math.Round(Subtotal(po) * po.TaxPercent / 100m, 2);
    public static decimal Total(PurchaseOrder po) => Subtotal(po) + Tax(po) + po.ShippingCost;
    public static decimal TotalInBase(PurchaseOrder po) => Math.Round(Total(po) * po.ExchangeRate, 2);
}
