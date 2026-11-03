using System.Text.RegularExpressions;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

public static partial class TenantProvisioning
{
    public static readonly (string Name, string Description, string[] Permissions)[] DefaultRoles =
    [
        ("Administrator", "Full access to everything in the organization", Permissions.All.Select(p => p.Code).ToArray()),
        ("Maintenance Manager", "Manages assets, locations, people and all maintenance work",
            Permissions.All.Select(p => p.Code).Where(c => c is not Permissions.UsersManage and not Permissions.SettingsManage).ToArray()),
        ("Technician", "Executes work orders and raises requests",
            [Permissions.DashboardView, Permissions.LocationsView, Permissions.AssetsView, Permissions.PeopleView, Permissions.RequestsView, Permissions.RequestsCreate,
             Permissions.WorkOrdersView, Permissions.WorkOrdersCreate, Permissions.WorkOrdersExecute, Permissions.PmView, Permissions.MetersRecord, Permissions.InventoryView, Permissions.InventoryTransact]),
        ("Requester", "Submits and follows their own work requests", [Permissions.RequestsCreate, Permissions.LocationsView, Permissions.AssetsView]),
        ("Viewer", "Read-only access", [Permissions.DashboardView, Permissions.LocationsView, Permissions.AssetsView, Permissions.PeopleView, Permissions.RequestsView, Permissions.WorkOrdersView, Permissions.PmView,
             Permissions.InventoryView, Permissions.PurchasingView, Permissions.ReportsView]),
    ];

    public static readonly (string Code, string Name, string Color)[] DefaultTrades =
    [
        ("MECH", "Mechanical", "#868e96"), ("ELEC", "Electrical", "#fab005"), ("HVAC", "HVAC", "#228be6"), ("CIVIL", "Civil / Building", "#fd7e14"),
        ("INST", "Instrumentation", "#7950f2"), ("PLMB", "Plumbing", "#15aabf"), ("GEN", "General", "#40c057"),
    ];

    public static readonly (string Code, string Name)[] DefaultFailureCodes =
    [
        ("WEAR", "Wear and tear"), ("ELEC", "Electrical fault"), ("LUBE", "Lubrication failure"), ("MISU", "Misuse / operator error"),
        ("LEAK", "Leak"), ("BLOCK", "Blockage / jam"), ("CTRL", "Control / sensor fault"), ("UNK", "Unknown"),
    ];

    public static readonly (string Code, string Name)[] DefaultPartGroups =
    [
        ("BEAR", "Bearings & seals"), ("ELEC", "Electrical"), ("MECH", "Mechanical"), ("FILT", "Filters"), ("LUBE", "Lubricants"),
        ("HYD", "Hydraulics & pneumatics"), ("CONS", "Consumables"), ("SAFE", "Safety"),
    ];

    public static readonly (string Code, string Name, string Color)[] DefaultCategories =
    [
        ("HVAC", "HVAC & Refrigeration", "#228be6"), ("ELEC", "Electrical", "#fab005"), ("MECH", "Mechanical", "#868e96"),
        ("PLMB", "Plumbing", "#15aabf"), ("FIRE", "Fire & Safety", "#fa5252"), ("PROD", "Production Equipment", "#7950f2"),
        ("VEH", "Vehicles & Mobile Equipment", "#40c057"), ("BLDG", "Building & Facilities", "#fd7e14"), ("IT", "IT & Communications", "#4c6ef5"),
        ("MED", "Medical Equipment", "#e64980"),
    ];

    public static string Slugify(string name)
    {
        var s = NonAlnum().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "org" : s[..Math.Min(s.Length, 60)];
    }

    /// <summary>Creates a tenant with default roles and categories. The caller's DbContext must be bound to the new tenant id.</summary>
    public static async Task<(Tenant Tenant, AppUser Admin)> CreateAsync(
        CmmsDbContext db, IPasswordHasher<AppUser> hasher, Guid tenantId, string orgName, string adminName, string adminEmail, string adminPassword,
        bool seedCategories = true, CancellationToken ct = default)
    {
        var slug = Slugify(orgName);
        var baseSlug = slug; var i = 1;
        while (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) slug = $"{baseSlug}-{++i}";

        return await db.InTransactionAsync(async token =>
        {
        var tenant = new Tenant { Id = tenantId, Name = orgName.Trim(), Slug = slug };
        db.Tenants.Add(tenant);
        var roles = DefaultRoles.Select(r => new Role { TenantId = tenantId, Name = r.Name, Description = r.Description, Permissions = [.. r.Permissions], IsSystem = r.Name == "Administrator" }).ToList();
        db.Roles.AddRange(roles);
        if (seedCategories)
        {
            db.AssetCategories.AddRange(DefaultCategories.Select(c => new AssetCategory { TenantId = tenantId, Code = c.Code, Name = c.Name, Color = c.Color }));
            db.Trades.AddRange(DefaultTrades.Select(t => new FourC.Cmms.Core.People.Trade { TenantId = tenantId, Code = t.Code, Name = t.Name, Color = t.Color }));
            db.FailureCodes.AddRange(DefaultFailureCodes.Select(f => new FourC.Cmms.Core.Work.FailureCode { TenantId = tenantId, Code = f.Code, Name = f.Name }));
            db.PartGroups.AddRange(DefaultPartGroups.Select(g => new FourC.Cmms.Core.Inventory.PartGroup { TenantId = tenantId, Code = g.Code, Name = g.Name }));
        }
        var admin = NewUser(tenantId, adminName, adminEmail);
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        db.Users.Add(admin);
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = roles[0].Id });
        await db.SaveChangesAsync(token);
        return (tenant, admin);
        }, ct);
    }

    /// <summary>Builds a user record with normalized identity fields (same normalization as ASP.NET Identity).</summary>
    public static AppUser NewUser(Guid tenantId, string fullName, string email)
    {
        var e = email.Trim();
        return new AppUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, UserName = e, NormalizedUserName = e.ToUpperInvariant(), Email = e, NormalizedEmail = e.ToUpperInvariant(),
            FullName = fullName.Trim(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true,
        };
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}

public class IdentityValidationException(string[] errors) : Exception(string.Join(" ", errors))
{
    public string[] Errors { get; } = errors;
}
