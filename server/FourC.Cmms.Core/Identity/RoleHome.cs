namespace FourC.Cmms.Core.Identity;

/// <summary>One card on a role's home page (0.22.0). Only the shortcuts card has tiles.</summary>
public sealed class HomeCard
{
    public required string Type { get; set; }
    public List<HomeTile>? Tiles { get; set; }
}

/// <summary>A coloured tile on the shortcuts card: a label (and its Arabic wording) and a CMX address.</summary>
public sealed class HomeTile
{
    public required string Label { get; set; }
    public string? LabelAr { get; set; }
    public required string Color { get; set; }
    public required string To { get; set; }
}

/// <summary>The cards a role's home page can hold, in catalogue order, and their limits.</summary>
public static class HomeCardTypes
{
    public const string Shortcuts = "shortcuts";
    public const int MaxCards = 20, MaxTiles = 12, MaxLabel = 40, MaxLink = 300;

    public static readonly IReadOnlyList<string> All =
    [
        "work-summary", "work-by-site", "urgent-work", "asset-summary", "assets-by-status", "assets-by-category", "assets-by-site",
        "warranty-soon", "recent-assets", "my-work", "unassigned-work", "late-work", "assets-down", "low-stock", "my-approvals",
        "quick-request", "location-tree", Shortcuts,
    ];

    public static readonly IReadOnlyList<string> Colors = ["blue", "teal", "green", "orange", "red", "grape", "indigo", "gray"];
}

/// <summary>
/// The menu items a role can hide, each with the permissions that show it (any one is enough). Home and Settings can't be
/// hidden. The web menu keeps the same list; tests on both sides pin them to one literal.
/// </summary>
public static class MenuModules
{
    public static readonly IReadOnlyList<(string Key, string[] AnyOf)> All =
    [
        ("locations", [Permissions.LocationsView]),
        ("assets", [Permissions.AssetsView]),
        ("work-requests", [Permissions.RequestsCreate]),
        ("work-orders", [Permissions.WorkOrdersView]),
        ("work-groups", [Permissions.WorkOrdersView]),
        ("planning", [Permissions.WorkOrdersView]),
        ("availability", [Permissions.WorkOrdersView]),
        ("timesheet", [Permissions.WorkOrdersView]),
        ("pm", [Permissions.PmView]),
        ("contracts", [Permissions.ContractsView]),
        ("inventory", [Permissions.InventoryView]),
        ("purchasing", [Permissions.PurchasingView, Permissions.PurchasingRequest, Permissions.PurchasingApproveRequests]),
        ("approvals", [Permissions.PurchasingApprove, Permissions.PurchasingApproveRequests]),
        ("reports", [Permissions.ReportsView]),
        ("budgets", [Permissions.ReportsView]),
        ("people", [Permissions.PeopleView]),
    ];

    public static bool IsValid(string? key) => key is not null && All.Any(m => m.Key == key);
}
