using FourC.Cmms.Core.Common;
using Microsoft.AspNetCore.Identity;

namespace FourC.Cmms.Core.Identity;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public required string FullName { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    /// <summary>The language this person chose ("en", "ar"); null = their organization's default.</summary>
    public string? Language { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>When the password was last set (0.25.0), for expiry; null = before 0.25.0's migration ran (set to the migration time).</summary>
    public DateTimeOffset? PasswordChangedAt { get; set; }
    /// <summary>"Sign out other devices": sign-ins without a device session (from before 0.4.0) issued before this are refused.</summary>
    public DateTimeOffset? SignedOutBefore { get; set; }
    public string? LegacyRef { get; set; }
    /// <summary>Largest purchase order (base currency) this user may approve; null = no limit (legacy authorization limit).</summary>
    public decimal? PurchaseApprovalLimit { get; set; }
    /// <summary>E-mail events (keys from <c>EmailEvents</c>) this user doesn't want.</summary>
    public List<string> EmailOptOuts { get; set; } = [];
    /// <summary>Sites this person may see; empty = every site.</summary>
    public List<Guid> SiteIds { get; set; } = [];
    /// <summary>Branches of the location tree this person is limited to (they see these places and everything under them); empty = no branch limit.</summary>
    public List<Guid> LocationIds { get; set; } = [];
    public List<UserRole> Roles { get; set; } = [];
}

/// <summary>Tenant-defined role holding a set of permission codes.</summary>
public class Role : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    /// <summary>System roles (Administrator) cannot be deleted or stripped of permissions.</summary>
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = [];
    /// <summary>The role's home page cards in order (0.22.0); null = not set up, so the role adds no cards.</summary>
    public List<HomeCard>? HomeLayout { get; set; }
    /// <summary>Menu items (keys from <see cref="MenuModules"/>) this role hides. Tidying only: permissions decide access.</summary>
    public List<string> HiddenModules { get; set; } = [];
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public static class Permissions
{
    public const string DashboardView = "dashboard.view";
    public const string LocationsView = "locations.view";
    public const string LocationsManage = "locations.manage";
    public const string AssetsView = "assets.view";
    public const string AssetsCreate = "assets.create";
    public const string AssetsEdit = "assets.edit";
    public const string AssetsDelete = "assets.delete";
    public const string AssetsTransfer = "assets.transfer";
    public const string PeopleView = "people.view";
    public const string PeopleManage = "people.manage";
    public const string RequestsView = "requests.view";
    public const string RequestsCreate = "requests.create";
    public const string RequestsManage = "requests.manage";
    public const string WorkOrdersView = "workorders.view";
    public const string WorkOrdersCreate = "workorders.create";
    public const string WorkOrdersEdit = "workorders.edit";
    public const string WorkOrdersExecute = "workorders.execute";
    public const string WorkOrdersClose = "workorders.close";
    public const string WorkOrdersDelete = "workorders.delete";
    /// <summary>A crew leader logs hours for the other technicians on a job (0.11.0).</summary>
    public const string WorkOrdersLogForOthers = "workorders.logForOthers";
    public const string PmView = "pm.view";
    public const string PmManage = "pm.manage";
    public const string PmGenerate = "pm.generate";
    public const string MetersRecord = "meters.record";
    public const string InventoryView = "inventory.view";
    public const string InventoryManage = "inventory.manage";
    public const string InventoryTransact = "inventory.transact";
    public const string PurchasingView = "purchasing.view";
    public const string PurchasingCreate = "purchasing.create";
    public const string PurchasingApprove = "purchasing.approve";
    public const string PurchasingReceive = "purchasing.receive";
    /// <summary>Ask for parts or services to be bought (0.16.0).</summary>
    public const string PurchasingRequest = "purchasing.request";
    /// <summary>Approve or reject purchase requests (0.16.0).</summary>
    public const string PurchasingApproveRequests = "purchasing.approveRequests";
    public const string SuppliersManage = "suppliers.manage";
    /// <summary>See service contracts (0.17.0).</summary>
    public const string ContractsView = "contracts.view";
    /// <summary>Create, edit, amend and terminate service contracts (0.17.0).</summary>
    public const string ContractsManage = "contracts.manage";
    public const string ReportsView = "reports.view";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";

    public sealed record PermissionInfo(string Code, string Group, string Name);

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(DashboardView, "General", "View dashboard"),
        new(LocationsView, "Locations", "View sites and locations"),
        new(LocationsManage, "Locations", "Create, edit and delete sites and locations"),
        new(AssetsView, "Assets", "View assets"),
        new(AssetsCreate, "Assets", "Create assets"),
        new(AssetsEdit, "Assets", "Edit assets"),
        new(AssetsDelete, "Assets", "Delete assets"),
        new(AssetsTransfer, "Assets", "Transfer assets between locations"),
        new(PeopleView, "People", "View employees and trades"),
        new(PeopleManage, "People", "Manage employees, trades and rates"),
        new(RequestsView, "Work requests", "View all work requests (everyone sees their own)"),
        new(RequestsCreate, "Work requests", "Submit work requests"),
        new(RequestsManage, "Work requests", "Approve, reject and convert work requests"),
        new(WorkOrdersView, "Work orders", "View work orders"),
        new(WorkOrdersCreate, "Work orders", "Create work orders"),
        new(WorkOrdersEdit, "Work orders", "Edit and assign work orders"),
        new(WorkOrdersExecute, "Work orders", "Start work, log labour, tick tasks, complete work"),
        new(WorkOrdersClose, "Work orders", "Close, cancel and reopen work orders"),
        new(WorkOrdersDelete, "Work orders", "Delete work orders"),
        new(WorkOrdersLogForOthers, "Work orders", "Log hours for others"),
        new(PmView, "Preventive maintenance", "View PM tasks, schedules, meters and forecast"),
        new(PmManage, "Preventive maintenance", "Create and edit PM tasks, schedules and meters"),
        new(PmGenerate, "Preventive maintenance", "Generate PM work orders"),
        new(MetersRecord, "Preventive maintenance", "Log meter readings"),
        new(InventoryView, "Inventory", "View parts, stores, stock and transactions"),
        new(InventoryManage, "Inventory", "Create and edit parts, stores and stock levels"),
        new(InventoryTransact, "Inventory", "Receive, issue, return, adjust, transfer and count stock"),
        new(PurchasingView, "Purchasing", "View suppliers and purchase orders"),
        new(PurchasingCreate, "Purchasing", "Create, edit and submit purchase orders"),
        new(PurchasingApprove, "Purchasing", "Approve purchase orders (by approval levels, or up to the user's limit)"),
        new(PurchasingReceive, "Purchasing", "Receive goods against purchase orders"),
        new(PurchasingRequest, "Purchasing", "Raise purchase requests"),
        new(PurchasingApproveRequests, "Purchasing", "Approve purchase requests"),
        new(SuppliersManage, "Purchasing", "Manage suppliers"),
        new(ContractsView, "Contracts", "View contracts"),
        new(ContractsManage, "Contracts", "Create and edit contracts"),
        new(ReportsView, "Reports", "View reports and KPIs"),
        new(UsersManage, "Administration", "Manage users and roles"),
        new(SettingsManage, "Administration", "Manage organization settings, categories and custom fields"),
    ];
}
