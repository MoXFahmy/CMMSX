using System.Collections.Concurrent;
using System.Security.Claims;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>A branch of the location tree a person is limited to: its site and its materialized path.</summary>
public sealed record AccessBranch(Guid SiteId, string Path);

/// <summary>What a person may see: sites (null = every site) and, when limited to branches, those branches (never set without sites).</summary>
public sealed record UserAccess(IReadOnlyList<Guid>? SiteIds, IReadOnlyList<AccessBranch>? Branches)
{
    public static readonly UserAccess Everything = new(null, null);
    public bool Limited => SiteIds is not null;
}

/// <summary>What one request may see: the person's access, narrowed to the site in the header switch when that site is theirs.</summary>
public sealed record AccessScope(IReadOnlyList<Guid>? SiteIds, IReadOnlyList<string>? BranchPaths, Guid? NarrowedTo);

public static class Access
{
    /// <summary>The site switch in the header: work in one site at a time.</summary>
    public const string SiteHeader = "X-Cmx-Site";
    internal const string ScopeKey = "cmx.scope";
    internal const string AccessKey = "cmx.access";

    public static AccessScope Narrow(UserAccess access, Guid? requestedSite)
    {
        if (requestedSite is not { } site || (access.SiteIds is not null && !access.SiteIds.Contains(site)))
            return new AccessScope(access.SiteIds, access.Branches?.Select(b => b.Path).ToList(), null);
        return new AccessScope([site], access.Branches?.Where(b => b.SiteId == site).Select(b => b.Path).ToList(), site);
    }

    /// <summary>Whether a record at <paramref name="siteId"/> (and <paramref name="locationPath"/>, its location's path) is visible with <paramref name="access"/>.</summary>
    public static bool Covers(UserAccess access, Guid siteId, string? locationPath)
    {
        if (access.SiteIds is not null && !access.SiteIds.Contains(siteId)) return false;
        if (access.Branches is null) return true;
        return locationPath is not null && access.Branches.Any(b => b.SiteId == siteId && locationPath.StartsWith(b.Path, StringComparison.Ordinal));
    }

    /// <summary>
    /// People limited to branches must say where: a record without a location belongs to no branch, so they couldn't see what they
    /// made. Work orders and requests take the asset's location when only the asset is given.
    /// </summary>
    public static void RequireLocation(CmmsDbContext db, Validator v, Guid? locationId, Guid? assetId = null)
    {
        if (db.CurrentBranchPaths is not null && locationId is null && assetId is null) v.Add("locationId", "Choose a location in your part of the site.");
    }

    /// <summary>The signed-in person's whole access (before the header switch), or null outside a signed-in request.</summary>
    public static UserAccess? Of(HttpContext http) => http.Items[AccessKey] as UserAccess;

    /// <summary>
    /// Works out the request's scope for <paramref name="principal"/>, once. Called first thing when the sign-in cookie is checked
    /// (before anything opens the request's database context) and again by <see cref="UseSiteAccess"/> as a backstop.
    /// </summary>
    public static async Task EstablishAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        if (http.Items.ContainsKey(ScopeKey) || !Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        var access = await http.RequestServices.GetRequiredService<AccessService>().GetAsync(userId, http.RequestAborted);
        Guid? requested = Guid.TryParse(http.Request.Headers[SiteHeader].ToString(), out var s) ? s : null;
        http.Items[AccessKey] = access;
        http.Items[ScopeKey] = Narrow(access, requested);
    }

    /// <summary>The database filters read the scope through <see cref="HttpTenantContext"/>.</summary>
    public static IApplicationBuilder UseSiteAccess(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (http.User.Identity?.IsAuthenticated == true) await EstablishAsync(http, http.User);
        await next(http);
    });
}

/// <summary>Each person's access, cached briefly; saving a user, or moving or deleting a location, drops what is cached.</summary>
public sealed class AccessService(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly ConcurrentDictionary<Guid, int> Versions = new();
    private static int _locations;

    public static void Invalidate(Guid userId) => Versions.AddOrUpdate(userId, 1, (_, v) => v + 1);
    /// <summary>Branch paths may have changed (a location moved or was deleted).</summary>
    public static void LocationsChanged() => Interlocked.Increment(ref _locations);

    public async Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var key = $"access:{userId}:{Versions.GetValueOrDefault(userId)}:{Volatile.Read(ref _locations)}";
        if (cache.TryGetValue(key, out UserAccess? cached)) return cached!;
        using var scope = scopes.CreateScope();
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.TenantId, u.SiteIds, u.LocationIds }).FirstOrDefaultAsync(ct);
        UserAccess access;
        if (user is null) access = new UserAccess([], null);
        else if (user.LocationIds.Count > 0)
        {
            // Branches that no longer exist give nothing: a person limited to branches never falls back to everything.
            var ids = user.LocationIds;
            var branches = await db.Locations.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == user.TenantId && !l.IsDeleted && ids.Contains(l.Id))
                .Select(l => new AccessBranch(l.SiteId, l.Path)).ToListAsync(ct);
            access = new UserAccess(branches.Select(b => b.SiteId).Distinct().ToList(), branches);
        }
        else if (user.SiteIds.Count > 0) access = new UserAccess(user.SiteIds.Distinct().ToList(), null);
        else access = UserAccess.Everything;
        cache.Set(key, access, TimeSpan.FromSeconds(60));
        return access;
    }
}

/// <summary>Codes and numbers stay unique across the organization, including in sites the person can't see.</summary>
public static class Unique
{
    /// <summary><paramref name="visible"/> when a record the person can see matches, <paramref name="hidden"/> when only one in a site they can't
    /// see does, else null.</summary>
    public static async Task<string?> ClashAsync<T>(IQueryable<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, string visible, string hidden,
        CancellationToken ct) where T : class =>
        await set.AnyAsync(match, ct) ? visible : await set.AllSites().AnyAsync(match, ct) ? hidden : null;
}
