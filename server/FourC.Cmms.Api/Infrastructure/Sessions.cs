using System.Security.Claims;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Signed-in devices. The session id lives in the auth cookie's properties (not its claims), so it survives
/// <c>RefreshSignInAsync</c> (password change) and the security-stamp refresh, which rebuild the claims.
/// </summary>
public static class Sessions
{
    public const string Key = "sid";
    public const string RevokedHeader = "X-Session-Revoked";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(10);
    /// <summary>How long an ordinary sign-in lasts without use (sliding); the Android app and technician view get 30 days.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    /// <summary>The latest the sign-in can run out: last use (known to within <see cref="TouchEvery"/>) plus its lifetime.</summary>
    private static DateTimeOffset ExpiresAt(DateTimeOffset seen, TimeSpan lifetime) => seen + lifetime + TouchEvery;

    /// <summary>
    /// Whether to reissue a long sign-in's cookie now, so its 30 days count from the last use. The cookie handler alone only
    /// renews at half its life, so a phone used on day 14 and then on day 31 was signed out.
    /// </summary>
    public static bool RenewCookie(DateTimeOffset? issued, DateTimeOffset? expires, DateTimeOffset now) =>
        issued is { } i && expires is { } e && e - i > TimeSpan.FromDays(1) && now - i > TimeSpan.FromDays(1);

    private static TimeSpan LifetimeOf(AuthenticationProperties p) =>
        p.ExpiresUtc is { } e && p.IssuedUtc is { } i && e > i ? e - i : DefaultLifetime;

    private sealed record State(bool Active, DateTimeOffset LastSeenAt);
    private static string CacheKey(Guid id) => $"session:{id}";

    public static bool IsAndroidApp(string? userAgent) => userAgent?.Contains("4CCMMS-Android/", StringComparison.Ordinal) == true;

    /// <summary>"Android app 0.4.0", "Chrome on Windows", "Safari on iPhone"…; "Browser" when nothing is recognized.</summary>
    public static string DeviceName(string? userAgent)
    {
        var ua = userAgent ?? "";
        var app = ua.IndexOf("4CCMMS-Android/", StringComparison.Ordinal);
        if (app >= 0) return $"Android app {ua[(app + "4CCMMS-Android/".Length)..].Split(' ')[0]}".Trim();
        string? browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : null;
        string? os = ua.Contains("Windows") ? "Windows" : ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android"
            : ua.Contains("Mac OS X") || ua.Contains("Macintosh") ? "macOS" : ua.Contains("Linux") ? "Linux" : null;
        var name = browser is null ? "Browser" : os is null ? browser : $"{browser} on {os}";
        return name.Length > 120 ? name[..120] : name;
    }

    public static async Task<UserSession> StartAsync(CmmsDbContext db, Guid userId, Guid tenantId, string? userAgent, TimeSpan lifetime, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var s = new UserSession
        {
            TenantId = tenantId, UserId = userId, DeviceName = DeviceName(userAgent), CreatedAt = now, LastSeenAt = now, ExpiresAt = ExpiresAt(now, lifetime),
        };
        db.UserSessions.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>The session id in the current request's auth cookie, if any.</summary>
    public static async Task<Guid?> CurrentAsync(HttpContext http)
    {
        var auth = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return auth.Properties?.Items.TryGetValue(Key, out var raw) == true && Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Marks sessions signed out and forgets their cached state, so this server refuses them at once.</summary>
    public static async Task RevokeAsync(CmmsDbContext db, IMemoryCache cache, IQueryable<UserSession> which, CancellationToken ct = default)
    {
        var ids = await which.Where(s => s.RevokedAt == null).Select(s => s.Id).ToListAsync(ct);
        if (ids.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        await db.UserSessions.IgnoreQueryFilters().Where(s => ids.Contains(s.Id)).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
        foreach (var id in ids) cache.Remove(CacheKey(id));
    }

    /// <summary>
    /// Refuses the request and says the device was signed out. The cookie is left in place so every later request (not
    /// just the first, which is usually the page load) carries the signal and the phone wipes its copy; signing in again
    /// replaces it.
    /// </summary>
    private static Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        ctx.HttpContext.Response.Headers[RevokedHeader] = "1";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs on every request with an auth cookie (after the security-stamp check). A cookie without a session (signed in
    /// before 0.4.0, or by sign-up) becomes a device; a signed-out device is refused with <see cref="RevokedHeader"/>.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var http = ctx.HttpContext;
        if (!Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimNames.TenantId), out var tenantId)) return;
        var db = http.RequestServices.GetRequiredService<CmmsDbContext>();
        var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
        var ct = http.RequestAborted;
        if (!ctx.Properties.Items.TryGetValue(Key, out var raw) || !Guid.TryParse(raw, out var sid))
        {
            // A sign-in from before "sign out other devices" was used is one of those other devices.
            var cutoff = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.SignedOutBefore).FirstOrDefaultAsync(ct);
            if (cutoff is { } before && (ctx.Properties.IssuedUtc ?? DateTimeOffset.MinValue) < before)
            {
                await RejectAsync(ctx);
                return;
            }
            var adopted = await StartAsync(db, userId, tenantId, http.Request.Headers.UserAgent.ToString(), LifetimeOf(ctx.Properties), ct);
            ctx.Properties.Items[Key] = adopted.Id.ToString();
            ctx.ShouldRenew = true;
            return;
        }
        var state = await cache.GetOrCreateAsync(CacheKey(sid), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return await db.UserSessions.IgnoreQueryFilters().Where(s => s.Id == sid && s.UserId == userId)
                .Select(s => new State(s.RevokedAt == null, s.LastSeenAt)).FirstOrDefaultAsync(ct);
        });
        if (state is not { Active: true })
        {
            await RejectAsync(ctx);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (RenewCookie(ctx.Properties.IssuedUtc, ctx.Properties.ExpiresUtc, now)) ctx.ShouldRenew = true;
        if (now - state.LastSeenAt > TouchEvery)
        {
            var expires = ExpiresAt(now, LifetimeOf(ctx.Properties));
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.Id == sid)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.ExpiresAt, expires), ct);
            cache.Remove(CacheKey(sid));
        }
    }
}
