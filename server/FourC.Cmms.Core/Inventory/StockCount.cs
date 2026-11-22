using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Inventory;

public enum StockCountStatus { Open = 1, Posted = 2, Cancelled = 3 }

/// <summary>
/// A stock count of one store (0.11.0): the office opens it, people count on the phone (offline too), the office reviews the
/// differences and posts them as adjustments (the same way as a one-step stock-take).
/// </summary>
public class StockCount : TenantEntity
{
    public Guid StoreId { get; set; }
    public Store? Store { get; set; }
    public required string Name { get; set; }
    public StockCountStatus Status { get; set; } = StockCountStatus.Open;
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedById { get; set; }
    public List<StockCountLine> Lines { get; set; } = [];
}

/// <summary>One part's counted quantity in a count; the latest count of a part replaces the earlier one.</summary>
public class StockCountLine : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid StockCountId { get; set; }
    public Guid PartId { get; set; }
    public Part? Part { get; set; }
    public decimal CountedQuantity { get; set; }
    public Guid? CountedById { get; set; }
    public string? CountedByName { get; set; }
    public DateTimeOffset CountedAt { get; set; }
}
