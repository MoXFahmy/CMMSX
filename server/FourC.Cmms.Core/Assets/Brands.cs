using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Assets;

/// <summary>A brand (0.21.0), picked on assets; its models belong to it. Names are unique per organization (any case).</summary>
public class Brand : TenantEntity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public List<BrandModel> Models { get; set; } = [];
}

/// <summary>A model of a brand (0.21.0); names are unique within the brand.</summary>
public class BrandModel : TenantEntity
{
    public Guid BrandId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}
