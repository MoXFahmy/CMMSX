using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Locations;

/// <summary>Top level of the physical hierarchy (a plant, campus, hospital, store...).</summary>
public class Site : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>This site's own settings; anything left empty uses the organization's.</summary>
    public SiteSettings Settings { get; set; } = new();
}

/// <summary>Per-site overrides of the organization's settings (stored as JSON on the site). Null or empty = the organization's.</summary>
public class SiteSettings
{
    public string? AssetNumberPrefix { get; set; }
    public string? WorkOrderNumberPrefix { get; set; }
    public string? WorkRequestNumberPrefix { get; set; }
    public string? PurchaseOrderNumberPrefix { get; set; }
    public string? PurchaseRequestNumberPrefix { get; set; }
    /// <summary>The site's weekly working hours, the default for people who work only here.</summary>
    public List<People.WorkingDay>? WorkingHours { get; set; }
    /// <summary>Store that planned and issued parts on this site's work start from.</summary>
    public Guid? DefaultStoreId { get; set; }
    /// <summary>Users told about new requests at this site (instead of everyone who can manage requests).</summary>
    public List<Guid> RequestRecipientIds { get; set; } = [];
    /// <summary>Users told about new purchase requests at this site (0.16.0; instead of the organization's).</summary>
    public List<Guid> PurchaseRequestRecipientIds { get; set; } = [];
    /// <summary>Shown as "&lt;name&gt; via CMX" on e-mails about this site's records.</summary>
    public string? EmailSenderName { get; set; }
}

public enum LocationType { Area = 1, Building = 2, Floor = 3, Room = 4, Zone = 5, ProductionLine = 6, Space = 7, Other = 99 }

public enum Criticality { Low = 1, Medium = 2, High = 3, Critical = 4 }

/// <summary>Node in an unlimited-depth location tree under a site.</summary>
public class Location : TenantEntity
{
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? ParentId { get; set; }
    public Location? Parent { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>0.28.0: the name in Arabic; empty = the main name for everyone.</summary>
    public string? NameAr { get; set; }
    public LocationType Type { get; set; } = LocationType.Space;
    public Criticality Criticality { get; set; } = Criticality.Medium;
    public string? Description { get; set; }
    /// <summary>Shown to technicians on every work order raised at this location.</summary>
    public string? NotesToTechnician { get; set; }
    public string? NotesToTechnicianAr { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Materialized path of ancestor ids including self: "/{id1}/{id2}/". Enables fast subtree queries.</summary>
    public string Path { get; set; } = "/";
    public int Depth { get; set; }

    public static string Segment(Guid id) => id.ToString("N") + "/";
    /// <summary>The location's cost centre (0.20.0); none = its parent's.</summary>
    public Guid? CostCentreId { get; set; }
}
