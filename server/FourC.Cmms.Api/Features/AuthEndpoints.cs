using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
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
    /// <param name="EndOtherSessions">With one session per person on (0.25.0): sign in here and end the person's other sessions.</param>
    public sealed record LoginRequest(string Email, string Password, bool RememberMe, bool Technician = false, bool EndOtherSessions = false);

    /// <summary>How long "Keep me signed in" lasts on a technician device (Android app or technician view) without use.</summary>
    private static readonly TimeSpan TechnicianSignIn = TimeSpan.FromDays(30);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    /// <summary>Signing in with a new password after the old one expired or no longer meets the rules (0.25.0).</summary>
    public sealed record RenewPasswordRequest(string Email, string Password, string NewPassword, bool RememberMe, bool Technician = false, bool EndOtherSessions = false);
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
            await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId, source: "Web"));
            var (_, admin) = await TenantProvisioning.CreateAsync(db, hasher, tenantId, req.OrganizationName, req.FullName, req.Email, req.Password!, language: Translator.CurrentLanguage, ct: ct);
            var user = await users.FindByIdAsync(admin.Id.ToString());
            await signIn.SignInAsync(user!, isPersistent: true);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapPost("/login", async (LoginRequest req, HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn, CmmsDbContext db,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
        {
            var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
            var tenantActive = user is not null && await db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive);
            if (user is null || !user.IsActive || user.IsServiceAccount || !tenantActive)
                return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            var wasLocked = await users.IsLockedOutAsync(user);
            var result = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
            // Failed sign-ins of a known person reach the activity log (0.25.0), with the device and never the password. A locked
            // account's further tries aren't written: only the try that locked it, so no one can flood the log with an address.
            if (!result.Succeeded && !wasLocked)
            {
                AuditLog.Event(db, "AppUser", user.Id, result.IsLockedOut ? "LockedOut" : "SignInFailed", Device(http), user.TenantId, user.Id, user.FullName);
                await db.SaveChangesAsync(ct);
            }
            if (result.IsLockedOut) return Results.Problem(title: Translator.T("Too many failed attempts. Try again in a few minutes."), statusCode: 429);
            if (!result.Succeeded) return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            // The organization's password rules (0.25.0): an expired password, or one shorter than today's minimum, is renewed first.
            var rules = await PasswordRules.SettingsAsync(db, user.TenantId, ct);
            if (PasswordRules.IsExpired(user, rules, DateTimeOffset.UtcNow)) return RenewFirst("password-expired", PasswordRules.Expired);
            if ((req.Password ?? "").Length < rules.PasswordMinLength) return RenewFirst("password-rules", PasswordRules.RulesChanged);
            return await CompleteSignInAsync(http, db, users, signIn, cache, user, req.RememberMe, req.Technician, req.EndOtherSessions, ct);
        }).AllowAnonymous();

        // A new password in place of an expired one, or one that no longer meets the rules; then signed in as usual (0.25.0).
        g.MapPost("/renew-password", async (RenewPasswordRequest req, HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn, CmmsDbContext db,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) =>
        {
            var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
            var tenantActive = user is not null && await db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
            if (user is null || !user.IsActive || user.IsServiceAccount || !tenantActive)
                return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            var result = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
            if (result.IsLockedOut) return Results.Problem(title: Translator.T("Too many failed attempts. Try again in a few minutes."), statusCode: 429);
            if (!result.Succeeded) return Results.Problem(title: Translator.T("Invalid email or password."), statusCode: 401);
            var v = new Validator();
            foreach (var pv in users.PasswordValidators)
                foreach (var e in (await pv.ValidateAsync(users, user, req.NewPassword ?? "")).Errors) v.Add("newPassword", e.Description);
            if (v.IsValid) foreach (var p in await PasswordRules.ProblemsAsync(db, users.PasswordHasher, user, req.NewPassword ?? "", ct)) v.Add("newPassword", p);
            if (!v.IsValid) return v.Problem();
            user.PasswordHash = users.PasswordHasher.HashPassword(user, req.NewPassword!);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await PasswordRules.RecordAsync(db, user, ct);
            AuditLog.Event(db, "AppUser", user.Id, "PasswordRenewed", tenantId: user.TenantId, userId: user.Id, userName: user.FullName);
            await db.SaveChangesAsync(ct);
            // A new password signs every other device out (their sign-ins ended with the old one); one session per person then
            // doesn't ask about this very browser's expired session.
            await Sessions.RevokeAsync(db, cache, db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == user.Id), ct);
            return await CompleteSignInAsync(http, db, users, signIn, cache, user, req.RememberMe, req.Technician, req.EndOtherSessions, ct);
        }).AllowAnonymous();

        g.MapPost("/logout", async (HttpContext http, SignInManager<AppUser> signIn, CmmsDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, ITenantContext ctx,
            CancellationToken ct) =>
        {
            if (ctx.UserId is { } who && ctx.TenantId is { } tid)
            {
                AuditLog.Event(db, "AppUser", who, "SignedOut", Device(http), tid);
                await db.SaveChangesAsync(ct);
            }
            if (await Sessions.CurrentAsync(http) is { } sid) await Sessions.RevokeAsync(db, cache, db.UserSessions.IgnoreQueryFilters().Where(s => s.Id == sid), ct);
            await signIn.SignOutAsync();
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();

        g.MapGet("/me", async (ITenantContext ctx, CmmsDbContext db, PermissionService perms, HttpContext http) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.UserId);
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ctx.TenantId);
            if (user is null || tenant is null) return Results.Unauthorized();
            var permissions = await perms.GetAsync(user.Id);
            // The home page and menu of all the person's roles (0.22.0).
            var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == user.Id).Select(r => r.Role!).ToListAsync();
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
                access = await AccessViewAsync(db, http),
                home = new { cards = RoleHomes.Merge(roles), hiddenModules = RoleHomes.Hidden(roles) },
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

        g.MapPost("/change-password", async (ChangePasswordRequest req, ITenantContext ctx, UserManager<AppUser> users, SignInManager<AppUser> signIn, CmmsDbContext db, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(ctx.UserId.ToString()!);
            if (user is null) return Results.Unauthorized();
            // The organization's password rules (0.25.0), once the current password is right.
            if (await users.CheckPasswordAsync(user, req.CurrentPassword ?? "")
                && await PasswordRules.ProblemsAsync(db, users.PasswordHasher, user, req.NewPassword ?? "", ct) is { Count: > 0 } problems)
            {
                var rv = new Validator();
                foreach (var p in problems) rv.Add("newPassword", p);
                return rv.Problem();
            }
            var r = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
            if (!r.Succeeded)
            {
                var v = new Validator();
                foreach (var e in r.Errors) v.Add(e.Code == "PasswordMismatch" ? "currentPassword" : "newPassword", e.Description);
                return v.Problem();
            }
            await PasswordRules.RecordAsync(db, user, ct);
            AuditLog.Event(db, "AppUser", user.Id, "PasswordChanged");
            await db.SaveChangesAsync(ct);
            await signIn.RefreshSignInAsync(user);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();
    }

    /// <summary>The device a sign-in event came from, as the device list names it (no address is kept).</summary>
    private static Dictionary<string, object?> Device(HttpContext http) =>
        new() { ["Device"] = new { to = Sessions.DeviceName(http.Request.Headers.UserAgent.ToString()) } };

    /// <summary>"Choose a new password first" (0.25.0): no sign-in; the sign-in page shows new-password fields.</summary>
    private static IResult RenewFirst(string code, string message) =>
        Results.Problem(title: Translator.T(message), statusCode: 403, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// Signs a person in once their password checks out: every sign-in is a device they (or an administrator) can sign out later.
    /// </summary>
    private static async Task<IResult> CompleteSignInAsync(HttpContext http, CmmsDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache, AppUser user, bool rememberMe, bool technician, bool endOtherSessions, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // One session per person (0.25.0): another device still signed in is named, and ended only when the person says so.
        if ((await PasswordRules.SettingsAsync(db, user.TenantId, ct)).OneSessionPerUser)
        {
            var others = db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == user.Id && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now));
            if (await others.OrderByDescending(s => s.LastSeenAt).Select(s => new { s.DeviceName, s.LastSeenAt }).FirstOrDefaultAsync(ct) is { } other)
            {
                if (!endOtherSessions)
                    return Results.Problem(title: Translator.T("You're signed in on another device."), statusCode: 409,
                        extensions: new Dictionary<string, object?> { ["code"] = "other-session", ["device"] = other.DeviceName, ["lastSeenAt"] = other.LastSeenAt });
                await Sessions.RevokeAsync(db, cache, others, ct);
                AuditLog.Event(db, "AppUser", user.Id, "OtherSessionsEnded", Device(http), user.TenantId, user.Id, user.FullName);
            }
        }
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var longSignIn = rememberMe && (technician || Sessions.IsAndroidApp(userAgent));
        var session = await Sessions.StartAsync(db, user.Id, user.TenantId, userAgent, longSignIn ? TechnicianSignIn : Sessions.DefaultLifetime, ct);
        var props = new AuthenticationProperties { IsPersistent = rememberMe };
        props.Items[Sessions.Key] = session.Id.ToString();
        if (longSignIn) props.ExpiresUtc = DateTimeOffset.UtcNow.Add(TechnicianSignIn);
        await signIn.SignInWithClaimsAsync(user, props, []);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        AuditLog.Event(db, "AppUser", user.Id, "SignedIn", Device(http), user.TenantId, user.Id, user.FullName);
        // Someone who signs in in another language than their organization's, and hasn't chosen one yet, keeps it.
        if (user.Language is null)
        {
            var orgLanguage = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId, ct)).Settings.DefaultLanguage;
            if (Translator.CurrentLanguage != Languages.Effective(null, orgLanguage)) user.Language = Translator.CurrentLanguage;
        }
        await users.UpdateAsync(user);
        return Results.Ok(new { ok = true });
    }

    /// <summary>For the site switch: every site the person may see (ignoring the switch), and the site this request was narrowed to.</summary>
    private static async Task<object> AccessViewAsync(CmmsDbContext db, HttpContext http)
    {
        var access = Access.Of(http) ?? UserAccess.Everything;
        var ids = access.SiteIds;
        var sites = await db.Sites.AllSites().AsNoTracking().Where(s => ids == null || ids.Contains(s.Id)).OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Code, s.Name, s.IsActive }).ToListAsync();
        return new { limited = access.Limited, branches = access.Branches is not null, sites, siteId = (http.Items[Access.ScopeKey] as AccessScope)?.NarrowedTo };
    }
}
