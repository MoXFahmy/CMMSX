using FourC.Cmms.Core.Assets;
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
    /// <summary>Completing a work order needs at least one labour entry.</summary>
    public bool RequireLaborToComplete { get; set; }
    /// <summary>Completing a work order needs every checklist step ticked (none to tick passes).</summary>
    public bool RequireAllTasksDone { get; set; }
    /// <summary>Completing breakdown work needs a failure code.</summary>
    public bool RequireFailureCodeForBreakdowns { get; set; }
    /// <summary>Logging labour or ticking a checklist step on an Open work order moves it to In progress.</summary>
    public bool AutoStartWork { get; set; }
    /// <summary>Generate PM work orders automatically when they come within each schedule's lead time.</summary>
    public bool AutoGeneratePm { get; set; } = true;
    public string PurchaseOrderNumberPrefix { get; set; } = "PO-";
    /// <summary>Default sales tax / VAT applied to new purchase orders.</summary>
    public decimal DefaultTaxPercent { get; set; }
    public string Currency { get; set; } = "USD";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Send e-mail notifications at all (off until an administrator switches it on).</summary>
    public bool EmailEnabled { get; set; }
    /// <summary>E-mail events (keys from <c>EmailEvents</c>) the organization switched off.</summary>
    public List<string> DisabledEmails { get; set; } = [];

    /// <summary>PM generation skips occurrences of single-asset schedules (and drops round assets) whose asset has one of these statuses.</summary>
    public List<AssetStatus> PmSkipAssetStatuses { get; set; } = [];
    /// <summary>PM work landing on these weekdays starts on the next working day; the schedule keeps its own dates.</summary>
    public List<DayOfWeek> PmNonWorkingDays { get; set; } = [];
    /// <summary>Scheduled-basis PM occurrences are skipped while an earlier work order of the same schedule is still open.</summary>
    public bool PmNoStacking { get; set; }
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
