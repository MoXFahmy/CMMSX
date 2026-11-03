using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;

namespace FourC.Cmms.Core.Assets;

public enum AssetStatus { Active = 1, Inactive = 2, InStorage = 3, UnderRepair = 4, Disposed = 5 }

public enum WarrantyType { None = 0, Warranty = 1, ServiceContract = 2 }

public class AssetCategory : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Asset : TenantEntity
{
    public required string AssetNumber { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public AssetCategory? Category { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? ParentAssetId { get; set; }
    public Asset? ParentAsset { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public Criticality Criticality { get; set; } = Criticality.Medium;

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Barcode { get; set; }
    /// <summary>Legacy "Op No" – operation/position number on a line.</summary>
    public string? OperationNumber { get; set; }

    public DateOnly? CommissionedOn { get; set; }
    public DateOnly? DisposedOn { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? CurrentValue { get; set; }
    public decimal? SalvageValue { get; set; }
    public decimal? ExpectedLifeYears { get; set; }
    public decimal? DowntimeCostPerHour { get; set; }

    public WarrantyType WarrantyType { get; set; }
    public DateOnly? WarrantyExpiresOn { get; set; }
    public string? WarrantyNotes { get; set; }

    public string? Notes { get; set; }
    public string? NotesToTechnician { get; set; }
    public Dictionary<string, string?> CustomFields { get; set; } = [];
}

/// <summary>History of asset moves between sites/locations (legacy assetstransfer).</summary>
public class AssetTransfer : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AssetId { get; set; }
    public Guid? FromSiteId { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid ToSiteId { get; set; }
    public Guid? ToLocationId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset TransferredAt { get; set; }
    public Guid? TransferredById { get; set; }
    public string? LegacyRef { get; set; }
}

public enum CustomFieldType { Text = 1, Number = 2, Date = 3, Select = 4, Boolean = 5, LongText = 6 }

/// <summary>Tenant-defined extra fields (replaces legacy NameplateInfo1..30 and per-customer show/hide flags).</summary>
public class CustomFieldDefinition : TenantEntity
{
    /// <summary>Entity the field applies to, e.g. "Asset".</summary>
    public required string EntityType { get; set; }
    public required string Key { get; set; }
    public required string Label { get; set; }
    public CustomFieldType DataType { get; set; } = CustomFieldType.Text;
    public List<string> Options { get; set; } = [];
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Restrict to asset categories; empty = all categories.</summary>
    public List<Guid> CategoryIds { get; set; } = [];
    public bool ShowInList { get; set; }
}
