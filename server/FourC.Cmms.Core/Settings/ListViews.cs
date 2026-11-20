using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Settings;

/// <summary>The lists that have the list toolkit (columns, saved views, export, bulk actions).</summary>
public static class ListKeys
{
    public const string WorkOrders = "work-orders";
    public const string WorkRequests = "work-requests";
    public const string PmSchedules = "pm-schedules";
    public const string Parts = "parts";
    public const string PurchaseOrders = "purchase-orders";
    public const string Assets = "assets";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { WorkOrders, WorkRequests, PmSchedules, Parts, PurchaseOrders, Assets };

    public static bool IsValid(string? list) => list is not null && All.Contains(list);
}

/// <summary>A person's named view of a list: its address-bar query (filters, tab, sort, page size) and hidden columns.</summary>
public class SavedView : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string List { get; set; }
    public required string Name { get; set; }
    public string Query { get; set; } = "";
    public List<string> HiddenColumns { get; set; } = [];
    /// <summary>Applied when the person opens the list with nothing in its address. At most one per person and list.</summary>
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A person's current column choice on a list.</summary>
public class UserListSetting : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string List { get; set; }
    public List<string> HiddenColumns { get; set; } = [];
}
