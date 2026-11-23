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
    /// <summary>The company sells to us: purchase orders and requests may pick it (0.17.0; existing records are suppliers).</summary>
    public bool IsSupplier { get; set; } = true;
    /// <summary>The Contractor role: contractor lines, contracts and employees may name it.</summary>
    public bool IsContractor { get; set; }
    /// <summary>The company makes equipment or parts (0.17.0).</summary>
    public bool IsManufacturer { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>People beyond the main contact (0.17.0).</summary>
    public List<SupplierContact> Contacts { get; set; } = [];
    /// <summary>Addresses beyond the main one (0.17.0).</summary>
    public List<SupplierAddress> Addresses { get; set; } = [];
    /// <summary>The company's own hourly rate per contractor category (0.17.0).</summary>
    public List<SupplierCategoryRate> CategoryRates { get; set; } = [];
}

/// <summary>A person at a company (0.17.0).</summary>
public class SupplierContact : Entity
{
    public Guid SupplierId { get; set; }
    public required string Name { get; set; }
    public string? Role { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int Sort { get; set; }
}

/// <summary>A further address of a company (0.17.0).</summary>
public class SupplierAddress : Entity
{
    public Guid SupplierId { get; set; }
    public string? Label { get; set; }
    public required string Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public int Sort { get; set; }
}

/// <summary>A kind of contractor work (electrician, lift engineer…) with a default hourly rate in base currency (0.17.0).</summary>
public class ContractorCategory : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal DefaultRate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A contractor company's own hourly rate for a category (0.17.0).</summary>
public class SupplierCategoryRate : Entity
{
    public Guid SupplierId { get; set; }
    public Guid ContractorCategoryId { get; set; }
    public decimal Rate { get; set; }
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
    /// <summary>Parts, Services, Capital… (0.16.0); approval levels can differ by type.</summary>
    public Guid? OrderTypeId { get; set; }
    /// <summary>Who submitted it for approval (0.16.0): with approval levels they can't sign it themselves.</summary>
    public Guid? SubmittedById { get; set; }
    /// <summary>How many times it was submitted under approval levels (0 = never); steps and signatures belong to a round.</summary>
    public int ApprovalRound { get; set; }
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
    /// <summary>The purchase request line this order line was made from (0.16.0).</summary>
    public Guid? PurchaseRequestLineId { get; set; }

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
