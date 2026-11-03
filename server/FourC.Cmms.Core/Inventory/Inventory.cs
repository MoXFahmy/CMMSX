using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;

namespace FourC.Cmms.Core.Inventory;

public enum StockTransactionType { Receipt = 1, Issue = 2, Return = 3, Adjustment = 4, TransferOut = 5, TransferIn = 6 }

/// <summary>Store / warehouse within a site (legacy substore).</summary>
public class Store : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Part grouping (legacy "Attribute Group": Electrical, Mechanical, Consumables...).</summary>
public class PartGroup : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Stock item (legacy stockcode).</summary>
public class Part : TenantEntity
{
    public required string PartNumber { get; set; }
    public required string Name { get; set; }
    public string? Specification { get; set; }
    public string? Manufacturer { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public required string UnitOfMeasure { get; set; }
    public Guid? GroupId { get; set; }
    public PartGroup? Group { get; set; }
    public decimal StandardPrice { get; set; }
    /// <summary>Weighted average cost, recalculated on every receipt (4CSQL formula).</summary>
    public decimal AverageCost { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public int? LeadTimeDays { get; set; }
    public bool IsCritical { get; set; }
    /// <summary>Shelf-life item: receipts need a batch and expiry date; expired batches cannot be issued.</summary>
    public bool IsShelfItem { get; set; }
    public bool AllowNegativeStock { get; set; }
    public Guid? PreferredSupplierId { get; set; }
    public string? Barcode { get; set; }
    public List<Guid> AlternatePartIds { get; set; } = [];
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A part's presence in a store: bin, re-order levels and the running balance.</summary>
public class StockLevel : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PartId { get; set; }
    public Part? Part { get; set; }
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public string? Bin { get; set; }
    public decimal MinLevel { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal MaxLevel { get; set; }
    /// <summary>0 excludes the part from purchase proposals (as in 4CSQL).</summary>
    public decimal ReorderQuantity { get; set; }
    public decimal QuantityOnHand { get; set; }
    public string? LegacyRef { get; set; }
}

/// <summary>Immutable stock ledger entry. Quantity is signed: positive into the store, negative out of it.</summary>
public class StockTransaction : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public StockTransactionType Type { get; set; }
    public Guid PartId { get; set; }
    public Part? Part { get; set; }
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateOnly Date { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    /// <summary>PO / delivery note / GRN reference.</summary>
    public string? Reference { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public Guid? RelatedTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public string? LegacyRef { get; set; }
}
