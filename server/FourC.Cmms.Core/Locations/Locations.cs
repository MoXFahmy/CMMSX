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
    public LocationType Type { get; set; } = LocationType.Space;
    public Criticality Criticality { get; set; } = Criticality.Medium;
    public string? Description { get; set; }
    /// <summary>Shown to technicians on every work order raised at this location.</summary>
    public string? NotesToTechnician { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Materialized path of ancestor ids including self: "/{id1}/{id2}/". Enables fast subtree queries.</summary>
    public string Path { get; set; } = "/";
    public int Depth { get; set; }

    public static string Segment(Guid id) => id.ToString("N") + "/";
}
