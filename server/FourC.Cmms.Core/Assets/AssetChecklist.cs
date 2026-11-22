using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Assets;

/// <summary>A work template whose steps are added to every new work order on an asset, or on any asset of a category (0.12.0).</summary>
public class AssetChecklist : TenantEntity
{
    public Guid? AssetId { get; set; }
    public Guid? AssetCategoryId { get; set; }
    public Guid PmTaskId { get; set; }
}
