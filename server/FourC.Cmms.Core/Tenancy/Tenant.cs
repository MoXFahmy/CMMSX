using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Tenancy;

public class Tenant : Entity
{
    public required string Name { get; set; }
    /// <summary>URL-safe unique identifier (e.g. "acme-foods").</summary>
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TenantSettings Settings { get; set; } = new();
}

/// <summary>Per-tenant configuration stored as JSON. Replaces the legacy keyvalue / relable / tblFeatures tables.</summary>
public class TenantSettings
{
    public Terminology Terminology { get; set; } = new();
    public string AssetNumberPrefix { get; set; } = "AST-";
    public int AssetNumberPadding { get; set; } = 5;
    public bool AutoNumberAssets { get; set; } = true;
    public string WorkOrderNumberPrefix { get; set; } = "WO-";
    public string WorkRequestNumberPrefix { get; set; } = "WR-";
    /// <summary>Require "action taken" notes before a work order can be completed.</summary>
    public bool RequireCompletionNotes { get; set; } = true;
    /// <summary>Generate PM work orders automatically when they come within each schedule's lead time.</summary>
    public bool AutoGeneratePm { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string TimeZone { get; set; } = "UTC";
}

/// <summary>Customers rename hierarchy levels (e.g. legacy "Department" shown as "Site").</summary>
public class Terminology
{
    public string Site { get; set; } = "Site";
    public string Sites { get; set; } = "Sites";
    public string Location { get; set; } = "Location";
    public string Locations { get; set; } = "Locations";
    public string Asset { get; set; } = "Asset";
    public string Assets { get; set; } = "Assets";
}
