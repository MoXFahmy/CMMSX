using FourC.Cmms.Core.Common;
using Microsoft.AspNetCore.Identity;

namespace FourC.Cmms.Core.Identity;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public required string FullName { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? LegacyRef { get; set; }
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
        new(UsersManage, "Administration", "Manage users and roles"),
        new(SettingsManage, "Administration", "Manage organization settings, categories and custom fields"),
    ];
}
