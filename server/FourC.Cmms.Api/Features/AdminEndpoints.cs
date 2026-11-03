using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AdminEndpoints
{
    public sealed record UserInput(string FullName, string Email, string? JobTitle, string? Phone, bool IsActive, List<Guid> RoleIds, string? Password);
    public sealed record RoleInput(string Name, string? Description, List<string> Permissions);
    public sealed record SettingsInput(string Name, TenantSettings Settings);
    public sealed record PasswordInput(string Password);

    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequirePermission(Permissions.UsersManage);

        users.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.TenantId == db.CurrentTenantId).OrderBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id, u.FullName, u.Email, u.JobTitle, u.Phone, u.IsActive, u.LastLoginAt, u.CreatedAt,
                    Roles = db.UserRoles.Where(r => r.UserId == u.Id).Select(r => new { r.RoleId, r.Role!.Name }).ToList(),
                }).ToListAsync(ct));

        users.MapPost("/", async (UserInput input, CmmsDbContext db, UserManager<AppUser> um, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            var v = await ValidateUserAsync(input, null, db, um, ct);
            if (string.IsNullOrWhiteSpace(input.Password)) v.Add("password", "Set an initial password.");
            if (!v.IsValid) return v.Problem();
            var user = TenantProvisioning.NewUser(db.CurrentTenantId!.Value, input.FullName, input.Email);
            user.JobTitle = input.JobTitle.Clean(); user.Phone = input.Phone.Clean(); user.IsActive = input.IsActive;
            user.PasswordHash = hasher.HashPassword(user, input.Password!);
            db.Users.Add(user);
            db.UserRoles.AddRange(input.RoleIds.Distinct().Select(r => new UserRole { UserId = user.Id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", new { user.Id });
        });

        users.MapPut("/{id:guid}", async (Guid id, UserInput input, CmmsDbContext db, UserManager<AppUser> um, ITenantContext ctx, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId, ct);
            if (user is null) return Results.NotFound();
            var v = await ValidateUserAsync(input, id, db, um, ct);
            if (id == ctx.UserId && !input.IsActive) v.Add("isActive", "You cannot deactivate your own account.");
            var adminRole = await db.Roles.Where(r => r.IsSystem).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (id == ctx.UserId && !input.RoleIds.Contains(adminRole) && await db.UserRoles.AnyAsync(r => r.UserId == id && r.RoleId == adminRole, ct))
                v.Add("roleIds", "You cannot remove the Administrator role from yourself.");
            if (!v.IsValid) return v.Problem();
            var emailChanged = !string.Equals(user.Email, input.Email.Trim(), StringComparison.OrdinalIgnoreCase);
            user.FullName = input.FullName.Trim(); user.JobTitle = input.JobTitle.Clean(); user.Phone = input.Phone.Clean(); user.IsActive = input.IsActive;
            if (emailChanged)
            {
                user.Email = user.UserName = input.Email.Trim();
                user.NormalizedEmail = user.NormalizedUserName = input.Email.Trim().ToUpperInvariant();
            }
            if (emailChanged || !input.IsActive) user.SecurityStamp = Guid.NewGuid().ToString("N");
            var existing = await db.UserRoles.Where(r => r.UserId == id).ToListAsync(ct);
            db.UserRoles.RemoveRange(existing.Where(r => !input.RoleIds.Contains(r.RoleId)));
            db.UserRoles.AddRange(input.RoleIds.Distinct().Where(r => existing.All(e => e.RoleId != r)).Select(r => new UserRole { UserId = id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            PermissionService.Invalidate(user.TenantId);
            return Results.Ok(new { user.Id });
        });

        users.MapPost("/{id:guid}/password", async (Guid id, PasswordInput input, CmmsDbContext db, UserManager<AppUser> um, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId, ct);
            if (user is null) return Results.NotFound();
            var v = new Validator();
            foreach (var pv in um.PasswordValidators)
                foreach (var e in (await pv.ValidateAsync(um, user, input.Password ?? "")).Errors) v.Add("password", e.Description);
            if (!v.IsValid) return v.Problem();
            user.PasswordHash = um.PasswordHasher.HashPassword(user, input.Password!);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.LockoutEnd = null; user.AccessFailedCount = 0;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var roles = api.MapGroup("/roles").WithTags("Roles").RequirePermission(Permissions.UsersManage);
        roles.MapGet("/permissions", () => Permissions.All);
        roles.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var counts = await db.UserRoles.Where(r => db.Roles.Any(x => x.Id == r.RoleId)).GroupBy(r => r.RoleId).Select(x => new { x.Key, C = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            var list = await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct);
            return list.Select(r => new { r.Id, r.Name, r.Description, r.IsSystem, r.Permissions, UserCount = counts.GetValueOrDefault(r.Id) });
        });
        roles.MapPost("/", async (RoleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateRoleAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var role = new Role { Name = input.Name.Trim(), Description = input.Description.Clean(), Permissions = CleanPermissions(input.Permissions) };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/roles/{role.Id}", new { role.Id });
        });
        roles.MapPut("/{id:guid}", async (Guid id, RoleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.NotFound();
            var v = await ValidateRoleAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            role.Name = role.IsSystem ? role.Name : input.Name.Trim();
            role.Description = input.Description.Clean();
            role.Permissions = role.IsSystem ? Permissions.All.Select(p => p.Code).ToList() : CleanPermissions(input.Permissions);
            await db.SaveChangesAsync(ct);
            PermissionService.Invalidate(role.TenantId);
            return Results.Ok(new { role.Id });
        });
        roles.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.NotFound();
            if (role.IsSystem) throw new DomainException("The Administrator role cannot be deleted.");
            if (await db.UserRoles.AnyAsync(r => r.RoleId == id, ct)) throw new DomainException("This role is assigned to users. Remove it from them first.");
            db.Roles.Remove(role);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => new { t.Name, t.Slug, t.Settings }).FirstAsync(ct))
            .RequireAuthorization();
        settings.MapPut("/", async (SettingsInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = new Validator().Required("name", input.Name, "Organization name", 200)
                .MaxLength("settings.assetNumberPrefix", input.Settings.AssetNumberPrefix, "Asset number prefix", 20)
                .When(input.Settings.AssetNumberPadding is < 1 or > 12, "settings.assetNumberPadding", "Padding must be between 1 and 12.");
            foreach (var (k, val) in new[] { ("site", input.Settings.Terminology.Site), ("location", input.Settings.Terminology.Location), ("asset", input.Settings.Terminology.Asset) })
                v.Required($"settings.terminology.{k}", val, "Label", 40);
            if (!v.IsValid) return v.Problem();
            var t = await db.Tenants.FirstAsync(x => x.Id == db.CurrentTenantId, ct);
            t.Name = input.Name.Trim();
            t.Settings = input.Settings;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Name, t.Settings });
        }).RequirePermission(Permissions.SettingsManage);

        settings.MapPost("/sample-data", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var created = await SampleData.SeedAsync(db, ct: ct);
            return Results.Ok(new { created });
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static List<string> CleanPermissions(List<string>? p) => (p ?? []).Intersect(Permissions.All.Select(x => x.Code)).Distinct().ToList();

    private static async Task<Validator> ValidateUserAsync(UserInput i, Guid? id, CmmsDbContext db, UserManager<AppUser> um, CancellationToken ct)
    {
        var v = new Validator().Required("fullName", i.FullName, "Name", 200).Required("email", i.Email, "Email", 256)
            .MaxLength("jobTitle", i.JobTitle, "Job title", 200).MaxLength("phone", i.Phone, "Phone", 50);
        if (!string.IsNullOrWhiteSpace(i.Email))
        {
            if (!i.Email.Contains('@')) v.Add("email", "Enter a valid email address.");
            else if (await um.FindByEmailAsync(i.Email.Trim()) is { } other && other.Id != id) v.Add("email", "This email is already used by another account.");
        }
        var roleIds = i.RoleIds ?? [];
        if (roleIds.Count == 0) v.Add("roleIds", "Assign at least one role.");
        else if (await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct) != roleIds.Distinct().Count()) v.Add("roleIds", "Unknown role.");
        if (!string.IsNullOrWhiteSpace(i.Password))
            foreach (var pv in um.PasswordValidators)
                foreach (var e in (await pv.ValidateAsync(um, null!, i.Password)).Errors) v.Add("password", e.Description);
        return v;
    }

    private static async Task<Validator> ValidateRoleAsync(RoleInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("name", i.Name, "Name", 100).MaxLength("description", i.Description, "Description", 500);
        if (!string.IsNullOrWhiteSpace(i.Name) && await db.Roles.AnyAsync(r => r.Name == i.Name.Trim() && r.Id != id, ct)) v.Add("name", "A role with this name exists.");
        return v;
    }
}

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var in90 = today.AddDays(90);
            var byStatus = await db.Assets.GroupBy(a => a.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var byCriticality = await db.Assets.GroupBy(a => a.Criticality).Select(x => new { Criticality = x.Key, Count = x.Count() }).ToListAsync(ct);
            var byCategory = await db.Assets.GroupBy(a => new { a.CategoryId, Name = a.Category!.Name, Color = a.Category.Color })
                .Select(x => new { x.Key.CategoryId, Name = x.Key.Name ?? "Uncategorized", x.Key.Color, Count = x.Count() }).OrderByDescending(x => x.Count).Take(10).ToListAsync(ct);
            var bySite = await db.Assets.GroupBy(a => new { a.SiteId, a.Site!.Name }).Select(x => new { x.Key.SiteId, x.Key.Name, Count = x.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
            var expiring = await db.Assets.AsNoTracking().Where(a => a.WarrantyExpiresOn != null && a.WarrantyExpiresOn >= today && a.WarrantyExpiresOn <= in90)
                .OrderBy(a => a.WarrantyExpiresOn).Take(8).Select(a => new { a.Id, a.AssetNumber, a.Name, a.WarrantyExpiresOn, a.WarrantyType }).ToListAsync(ct);
            var recent = await db.Assets.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(8)
                .Select(a => new { a.Id, a.AssetNumber, a.Name, a.Status, a.CreatedAt, SiteName = a.Site!.Name }).ToListAsync(ct);
            var active = new[] { FourC.Cmms.Core.Work.WorkOrderStatus.Open, FourC.Cmms.Core.Work.WorkOrderStatus.InProgress, FourC.Cmms.Core.Work.WorkOrderStatus.OnHold };
            var since = DateTimeOffset.UtcNow.AddDays(-30);
            var woByStatus = await db.WorkOrders.GroupBy(w => w.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var woByPriority = await db.WorkOrders.Where(w => active.Contains(w.Status)).GroupBy(w => w.Priority).Select(x => new { Priority = x.Key, Count = x.Count() }).ToListAsync(ct);
            var work = new
            {
                Open = woByStatus.Where(x => active.Contains(x.Status)).Sum(x => x.Count),
                Overdue = await db.WorkOrders.CountAsync(w => w.DueDate < today && active.Contains(w.Status), ct),
                CompletedLast30 = await db.WorkOrders.CountAsync(w => w.CompletedAt >= since, ct),
                RequestsAwaiting = await db.WorkRequests.CountAsync(r => r.Status == FourC.Cmms.Core.Work.WorkRequestStatus.New, ct),
                ByStatus = woByStatus, ByPriority = woByPriority,
                Urgent = await db.WorkOrders.AsNoTracking().Where(w => active.Contains(w.Status)).OrderByDescending(w => w.Priority).ThenBy(w => w.DueDate).Take(6)
                    .Select(w => new { w.Id, w.Number, w.Title, w.Priority, w.Status, w.DueDate, AssetNumber = w.Asset!.AssetNumber }).ToListAsync(ct),
            };
            return new
            {
                Work = work,
                Totals = new
                {
                    Sites = await db.Sites.CountAsync(ct), Locations = await db.Locations.CountAsync(ct), Assets = byStatus.Sum(x => x.Count),
                    CriticalAssets = byCriticality.Where(x => x.Criticality == Criticality.Critical).Sum(x => x.Count),
                    UnderRepair = byStatus.Where(x => x.Status == AssetStatus.UnderRepair).Sum(x => x.Count),
                    WarrantyExpiring90 = await db.Assets.CountAsync(a => a.WarrantyExpiresOn != null && a.WarrantyExpiresOn >= today && a.WarrantyExpiresOn <= in90, ct),
                    AssetValue = await db.Assets.SumAsync(a => a.CurrentValue ?? 0, ct),
                },
                ByStatus = byStatus, ByCriticality = byCriticality, ByCategory = byCategory, BySite = bySite,
                WarrantyExpiring = expiring, RecentAssets = recent,
            };
        }).WithTags("Dashboard").RequirePermission(Permissions.DashboardView);
    }
}
