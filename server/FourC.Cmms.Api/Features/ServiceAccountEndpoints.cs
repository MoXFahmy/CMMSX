using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Service accounts (0.26.0): non-person accounts with roles and sites, for API keys to act as. Managed under Settings › Users
/// (Manage users), with the same access rules as people.
/// </summary>
public static class ServiceAccountEndpoints
{
    public const string EditThere = "Edit service accounts under Service accounts.";
    public const string NoPassword = "Service accounts have no password.";
    public const string NotPeople = "Service accounts aren't people.";

    public sealed record ServiceAccountInput(string FullName, List<Guid> RoleIds, List<Guid>? SiteIds, List<Guid>? LocationIds, bool IsActive = true);

    /// <summary>The made-up address a service account carries (sign-in names must be unique); nothing is ever sent to it.</summary>
    public static string EmailOf(Guid id) => $"service-{id}@service.invalid";

    public static void MapServiceAccountEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/service-accounts").WithTags("Service accounts").RequirePermission(Permissions.UsersManage);

        g.MapPost("/", async (ServiceAccountInput input, CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, db, ct);
            var access = await AdminEndpoints.CheckAccessAsync(AsUser(input), null, db, Access.Of(http) ?? UserAccess.Everything, v, ct);
            if (!v.IsValid) return v.Problem();
            var id = Guid.CreateVersion7();
            var account = TenantProvisioning.NewUser(db.CurrentTenantId!.Value, input.FullName, EmailOf(id));
            account.Id = id;
            account.IsServiceAccount = true;
            account.IsActive = input.IsActive;
            (account.SiteIds, account.LocationIds) = access;
            db.Users.Add(account);
            db.UserRoles.AddRange(input.RoleIds.Distinct().Select(r => new UserRole { UserId = id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/service-accounts/{id}", new { id });
        });

        g.MapPut("/{id:guid}", async (Guid id, ServiceAccountInput input, CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId && u.IsServiceAccount, ct);
            if (account is null) return Results.NotFound();
            var editor = Access.Of(http) ?? UserAccess.Everything;
            if (await UserAccessRules.OutsideAsync(db, editor, account, ct) is { } outside) throw new DomainException(outside);
            var v = await ValidateAsync(input, db, ct);
            var access = await AdminEndpoints.CheckAccessAsync(AsUser(input), account, db, editor, v, ct);
            if (!v.IsValid) return v.Problem();
            account.FullName = input.FullName.Trim();
            account.IsActive = input.IsActive;
            (account.SiteIds, account.LocationIds) = access;
            var existing = await db.UserRoles.Where(r => r.UserId == id).ToListAsync(ct);
            db.UserRoles.RemoveRange(existing.Where(r => !input.RoleIds.Contains(r.RoleId)));
            db.UserRoles.AddRange(input.RoleIds.Distinct().Where(r => existing.All(e => e.RoleId != r)).Select(r => new UserRole { UserId = id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            PermissionService.Invalidate(account.TenantId);
            AccessService.Invalidate(account.Id);
            return Results.Ok(new { account.Id });
        });

        // Deleting keeps the account for its history: it's made inactive and its keys are revoked for good.
        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId && u.IsServiceAccount, ct);
            if (account is null) return Results.NotFound();
            if (await UserAccessRules.OutsideAsync(db, Access.Of(http) ?? UserAccess.Everything, account, ct) is { } outside) throw new DomainException(outside);
            account.IsActive = false;
            await db.SaveChangesAsync(ct);
            await RevokeKeysAsync(db, id, http, ct);
            PermissionService.Invalidate(account.TenantId);
            AccessService.Invalidate(account.Id);
            return Results.NoContent();
        });
    }

    /// <summary>Revokes every key of a deleted service account (API keys arrive with the next step of 0.26.0).</summary>
    private static Task RevokeKeysAsync(CmmsDbContext db, Guid accountId, HttpContext http, CancellationToken ct) => Task.CompletedTask;

    private static AdminEndpoints.UserInput AsUser(ServiceAccountInput i) =>
        new(i.FullName, "service@service.invalid", null, null, i.IsActive, i.RoleIds ?? [], null, null, i.SiteIds, i.LocationIds);

    private static async Task<Validator> ValidateAsync(ServiceAccountInput i, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("fullName", i.FullName, "Name", 200);
        var roleIds = i.RoleIds ?? [];
        if (roleIds.Count == 0) v.Add("roleIds", "Assign at least one role.");
        else if (await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct) != roleIds.Distinct().Count()) v.Add("roleIds", "Unknown role.");
        return v;
    }
}
