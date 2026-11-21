using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string OrganizationName, string FullName, string Email, string Password);
    /// <param name="Technician">Signing in from the technician view: with RememberMe it lasts 30 days, like the Android app.</param>
    public sealed record LoginRequest(string Email, string Password, bool RememberMe, bool Technician = false);

    /// <summary>How long "Keep me signed in" lasts on a technician device (Android app or technician view) without use.</summary>
    private static readonly TimeSpan TechnicianSignIn = TimeSpan.FromDays(30);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record LanguageInput(string? Language);

    /// <summary>Self-service sign-up of new organizations; switch off with Auth:AllowRegistration=false.</summary>
    private static bool AllowRegistration(IConfiguration config) => config.GetValue("Auth:AllowRegistration", true);

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Auth");

        g.MapGet("/options", (IConfiguration config) => Results.Ok(new { allowRegistration = AllowRegistration(config) })).AllowAnonymous();

        g.MapPost("/register", async (RegisterRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            DbContextOptions<CmmsDbContext> options, IPasswordHasher<AppUser> hasher, IConfiguration config, CancellationToken ct) =>
        {
            if (!AllowRegistration(config))
                return Results.Problem(title: Translator.T("New organizations can't be created here. Ask your administrator for an invitation."), statusCode: 403);
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
            var (_, admin) = await TenantProvisioning.CreateAsync(db, hasher, tenantId, req.OrganizationName, req.FullName, req.Email, req.Password!, language: Translator.CurrentLanguage, ct: ct);
            var user = await users.FindByIdAsync(admin.Id.ToString());
            await signIn.SignInAsync(user!, isPersistent: true);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapPost("/login", async (LoginRequest req, HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn, CmmsDbContext db, CancellationToken ct) =>
        {
            var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
            var tenantActive = user is not null && await db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive);
            if (user is null || !user.IsActive || !tenantActive)
                return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            var result = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
            if (result.IsLockedOut) return Results.Problem(title: Translator.T("Too many failed attempts. Try again in a few minutes."), statusCode: 429);
            if (!result.Succeeded) return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            // Every sign-in is a device the user (or an administrator) can sign out later.
            var userAgent = http.Request.Headers.UserAgent.ToString();
            var longSignIn = req.RememberMe && (req.Technician || Sessions.IsAndroidApp(userAgent));
            var session = await Sessions.StartAsync(db, user.Id, user.TenantId, userAgent, longSignIn ? TechnicianSignIn : Sessions.DefaultLifetime, ct);
            var props = new AuthenticationProperties { IsPersistent = req.RememberMe };
            props.Items[Sessions.Key] = session.Id.ToString();
            if (longSignIn) props.ExpiresUtc = DateTimeOffset.UtcNow.Add(TechnicianSignIn);
            await signIn.SignInWithClaimsAsync(user, props, []);
            user.LastLoginAt = DateTimeOffset.UtcNow;
            // Someone who signs in in another language than their organization's, and hasn't chosen one yet, keeps it.
            if (user.Language is null)
            {
                var orgLanguage = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId, ct)).Settings.DefaultLanguage;
                if (Translator.CurrentLanguage != Languages.Effective(null, orgLanguage)) user.Language = Translator.CurrentLanguage;
            }
            await users.UpdateAsync(user);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapPost("/logout", async (HttpContext http, SignInManager<AppUser> signIn, CmmsDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
        {
            if (await Sessions.CurrentAsync(http) is { } sid) await Sessions.RevokeAsync(db, cache, db.UserSessions.IgnoreQueryFilters().Where(s => s.Id == sid), ct);
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
                user = new
                {
                    user.Id, user.FullName, user.Email, user.JobTitle,
                    Language = Languages.Effective(user.Language, tenant.Settings.DefaultLanguage), LanguageChoice = user.Language,
                },
                // The technician view is for people linked to an employee record.
                employeeId = await db.Employees.AsNoTracking().Where(e => e.UserId == user.Id && e.IsActive).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(),
                tenant = new { tenant.Id, tenant.Name, tenant.Slug, tenant.Settings },
                permissions = permissions.Order(),
            });
        }).RequireAuthorization();

        // A person's language; null goes back to the organization's default.
        g.MapPut("/me/language", async (LanguageInput input, ITenantContext ctx, CmmsDbContext db, CancellationToken ct) =>
        {
            if (input.Language is not null && !Languages.IsSupported(input.Language))
                return new Validator().When(true, "language", "Choose a language CMX has.").Problem();
            await db.Users.Where(u => u.Id == ctx.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Language, input.Language), ct);
            return Results.Ok(new { ok = true });
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
