using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string OrganizationName, string FullName, string Email, string Password);
    public sealed record LoginRequest(string Email, string Password, bool RememberMe);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Auth");

        g.MapPost("/register", async (RegisterRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            DbContextOptions<CmmsDbContext> options, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            var v = new Validator()
                .Required("organizationName", req.OrganizationName, "Organization name", 200)
                .Required("fullName", req.FullName, "Your name", 200)
                .Required("email", req.Email, "Email", 256);
            if (!string.IsNullOrWhiteSpace(req.Email) && !req.Email.Contains('@')) v.Add("email", "Enter a valid email address.");
            if (!string.IsNullOrWhiteSpace(req.Email) && await users.FindByEmailAsync(req.Email.Trim()) is not null) v.Add("email", "An account with this email already exists.");
            foreach (var validator in users.PasswordValidators)
            {
                var r = await validator.ValidateAsync(users, null!, req.Password ?? "");
                foreach (var e in r.Errors) v.Add("password", e.Description);
            }
            if (!v.IsValid) return v.Problem();

            var tenantId = Guid.CreateVersion7();
            await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId));
            var (_, admin) = await TenantProvisioning.CreateAsync(db, hasher, tenantId, req.OrganizationName, req.FullName, req.Email, req.Password!, ct: ct);
            var user = await users.FindByIdAsync(admin.Id.ToString());
            await signIn.SignInAsync(user!, isPersistent: true);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapPost("/login", async (LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn, CmmsDbContext db) =>
        {
            var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
            var tenantActive = user is not null && await db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive);
            if (user is null || !user.IsActive || !tenantActive)
                return Results.Problem(title: "Invalid email or password.", statusCode: 401);
            var result = await signIn.PasswordSignInAsync(user, req.Password ?? "", req.RememberMe, lockoutOnFailure: true);
            if (result.IsLockedOut) return Results.Problem(title: "Too many failed attempts. Try again in a few minutes.", statusCode: 429);
            if (!result.Succeeded) return Results.Problem(title: "Invalid email or password.", statusCode: 401);
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapGet("/me", async (ITenantContext ctx, CmmsDbContext db, PermissionService perms) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.UserId);
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ctx.TenantId);
            if (user is null || tenant is null) return Results.Unauthorized();
            var permissions = await perms.GetAsync(user.Id);
            return Results.Ok(new
            {
                user = new { user.Id, user.FullName, user.Email, user.JobTitle },
                tenant = new { tenant.Id, tenant.Name, tenant.Slug, tenant.Settings },
                permissions = permissions.Order(),
            });
        }).RequireAuthorization();

        g.MapPost("/change-password", async (ChangePasswordRequest req, ITenantContext ctx, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            var user = await users.FindByIdAsync(ctx.UserId.ToString()!);
            if (user is null) return Results.Unauthorized();
            var r = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
            if (!r.Succeeded)
            {
                var v = new Validator();
                foreach (var e in r.Errors) v.Add(e.Code == "PasswordMismatch" ? "currentPassword" : "newPassword", e.Description);
                return v.Problem();
            }
            await signIn.RefreshSignInAsync(user);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();
    }
}
