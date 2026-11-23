using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Assets;

/// <summary>
/// A named set of assets anywhere in the tree (0.21.0), for one site or all; an asset can be in several. Used as a filter on
/// lists and reports, and as a PM target that follows its members.
/// </summary>
public class AssetGroup : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid? SiteId { get; set; }
    public bool IsActive { get; set; } = true;
    public List<AssetGroupMember> Members { get; set; } = [];
}

public class AssetGroupMember
{
    public Guid AssetGroupId { get; set; }
    public Guid AssetId { get; set; }
}
