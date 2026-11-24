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
/// <summary>
/// The organization's security rules (0.25.0). Saved on their own (Settings › Security): the organization settings page keeps
/// them as they are, so a page from before 0.25.0 can't reset them. The defaults change nothing.
/// </summary>
public class SecuritySettings
{
    /// <summary>8–64.</summary>
    public int PasswordMinLength { get; set; } = 8;
    /// <summary>0 = never; otherwise 30–730 days after a password was set.</summary>
    public int PasswordExpiryDays { get; set; }
    /// <summary>A new password can't be one of the last N (0 = no check, up to 24).</summary>
    public int PasswordHistory { get; set; }
    /// <summary>Signing in on another device asks whether to end the other session.</summary>
    public bool OneSessionPerUser { get; set; }
    /// <summary>Activity older than this many months is deleted nightly; null = keep everything (12–120 otherwise).</summary>
    public int? ActivityRetentionMonths { get; set; }
}

public class TenantSettings
{
    /// <summary>0.25.0: password rules, one session per person and activity retention.</summary>
    public SecuritySettings Security { get; set; } = new();

    public Terminology Terminology { get; set; } = new();
    /// <summary>The same terms shown in Arabic.</summary>
    public Terminology TerminologyArabic { get; set; } = Terminology.Arabic();
    /// <summary>0.21.0: the organization's names, colours, order and hidden values, by list (workType, priority, assetStatus, criticality) then value.</summary>
    public Dictionary<string, Dictionary<string, ListLabel>> ListLabels { get; set; } = new();
    /// <summary>Language for people who haven't chosen one, and for e-mails to suppliers ("en" or "ar").</summary>
    public string DefaultLanguage { get; set; } = "en";
    public string AssetNumberPrefix { get; set; } = "AST-";
    public int AssetNumberPadding { get; set; } = 5;
    public bool AutoNumberAssets { get; set; } = true;
    public string WorkOrderNumberPrefix { get; set; } = "WO-";
    public string WorkRequestNumberPrefix { get; set; } = "WR-";
    /// <summary>0.21.0: numbers (not assets') restart each year with the year in them: WO-2027-00001.</summary>
    public bool RestartNumbersYearly { get; set; }
    /// <summary>Require "action taken" notes before a work order can be completed.</summary>
    public bool RequireCompletionNotes { get; set; } = true;
    /// <summary>Completing a work order needs at least one labour entry.</summary>
    public bool RequireLaborToComplete { get; set; }
    /// <summary>Completing a work order needs every checklist step ticked (none to tick passes).</summary>
    public bool RequireAllTasksDone { get; set; }
    /// <summary>Completing a work order needs every Number, Meter and Text step answered (0.13.0).</summary>
    public bool RequireReadingSteps { get; set; }
    /// <summary>What a failed checklist step raises when the step says "Default" (0.13.0).</summary>
    public FourC.Cmms.Core.Pm.FollowUpKind FollowUpDefault { get; set; } = FourC.Cmms.Core.Pm.FollowUpKind.Request;
    /// <summary>The work template for follow-up work orders when the default is a work order.</summary>
    public Guid? FollowUpTemplateId { get; set; }
    /// <summary>The organization's logo (0.14.0): when it was last uploaded (Unix ms), or null when there is none. Pages add it to the logo's address.</summary>
    public long? LogoVersion { get; set; }
    /// <summary>The logo's media type (image/png, image/jpeg or image/webp), from its first bytes.</summary>
    public string? LogoContentType { get; set; }
    /// <summary>The AI assistant (0.15.0) is on for this organization; it also needs the server's key.</summary>
    public bool AiEnabled { get; set; }
    /// <summary>AI actions a month; empty = the server's ceiling. Never above the ceiling.</summary>
    public int? AiMonthlyLimit { get; set; }
    /// <summary>Completing breakdown work needs a failure code (a cause).</summary>
    public bool RequireFailureCodeForBreakdowns { get; set; }
    /// <summary>Logging labour or ticking a checklist step on an Open work order moves it to In progress.</summary>
    public bool AutoStartWork { get; set; }
    /// <summary>Generate PM work orders automatically when they come within each schedule's lead time.</summary>
    public bool AutoGeneratePm { get; set; } = true;
    public string PurchaseOrderNumberPrefix { get; set; } = "PO-";
    public string PurchaseRequestNumberPrefix { get; set; } = "PR-";
    /// <summary>Users told about new purchase requests (0.16.0); empty = everyone who may approve requests at the site.</summary>
    public List<Guid> PurchaseRequestRecipientIds { get; set; } = [];
    /// <summary>Users told when a contract needs attention, besides its manager (0.17.0); with no manager either, everyone who may manage contracts.</summary>
    public List<Guid> ContractAlertRecipientIds { get; set; } = [];
    /// <summary>Default sales tax / VAT applied to new purchase orders.</summary>
    public decimal DefaultTaxPercent { get; set; }
    public string Currency { get; set; } = "USD";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Send e-mail notifications at all (off until an administrator switches it on).</summary>
    public bool EmailEnabled { get; set; }
    /// <summary>E-mail events (keys from <c>EmailEvents</c>) the organization switched off.</summary>
    public List<string> DisabledEmails { get; set; } = [];
    /// <summary>Who gets each e-mail: event key → recipient kinds (<c>EmailRecipientKinds</c>). An event not listed uses its defaults.</summary>
    public Dictionary<string, List<string>> EmailRecipients { get; set; } = [];
    /// <summary>Actions that need a comment (<c>CommentActions</c>); completing follows <see cref="RequireCompletionNotes"/>.</summary>
    public List<string> CommentRequiredFor { get; set; } = ["reject", "refuse"];

    /// <summary>PM generation skips occurrences of single-asset schedules (and drops round assets) whose asset has one of these statuses.</summary>
    public List<AssetStatus> PmSkipAssetStatuses { get; set; } = [];
    /// <summary>PM work landing on these weekdays starts on the next working day; the schedule keeps its own dates.</summary>
    public List<DayOfWeek> PmNonWorkingDays { get; set; } = [];
    /// <summary>Scheduled-basis PM occurrences are skipped while an earlier work order of the same schedule is still open.</summary>
    public bool PmNoStacking { get; set; }

    /// <summary>Response and completion targets per priority (none until someone sets them); see <c>Sla</c>.</summary>
    public List<Work.SlaTarget> SlaTargets { get; set; } = [];
    /// <summary>Hours a technician can work in a day, for the planning board's load per person.</summary>
    public decimal DailyCapacityHours { get; set; } = 8;
    /// <summary>The organization's weekly working hours; empty = every day from 08:00 for <see cref="DailyCapacityHours"/> (before 0.9.0).</summary>
    public List<People.WorkingDay> WorkingHours { get; set; } = [];
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

    /// <summary>The Arabic defaults (with the article, as they read in labels and sentences).</summary>
    public static Terminology Arabic() => new() { Site = "الموقع", Sites = "المواقع", Location = "المكان", Locations = "الأماكن", Asset = "الأصل", Assets = "الأصول" };
}
