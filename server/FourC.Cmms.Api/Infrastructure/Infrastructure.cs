using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Security.Claims;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Infrastructure;

public static class ClaimNames
{
    public const string TenantId = "tenant_id";
    public const string FullName = "full_name";
}

/// <summary>Resolves tenant and user from the authenticated principal.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    public Guid? TenantId => Guid.TryParse(User?.FindFirstValue(ClaimNames.TenantId), out var t) ? t : null;
    public Guid? UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null;
    public string? UserName => User?.FindFirstValue(ClaimNames.FullName) ?? User?.Identity?.Name;
}

public sealed class AppClaimsFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(ClaimNames.TenantId, user.TenantId.ToString()));
        id.AddClaim(new Claim(ClaimNames.FullName, user.FullName));
        return id;
    }
}

// ---------------- Permissions ----------------

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith(Prefix)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[Prefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}

public sealed class PermissionHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && (await permissions.GetAsync(userId)).Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}

/// <summary>Loads a user's effective permissions with a short cache; role edits bump a version to invalidate.</summary>
public sealed class PermissionService(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly ConcurrentDictionary<Guid, int> Versions = new();
    public static void Invalidate(Guid tenantId) => Versions.AddOrUpdate(tenantId, 1, (_, v) => v + 1);

    public async Task<HashSet<string>> GetAsync(Guid userId)
    {
        using var scope = scopes.CreateScope();
        var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
        var tenantId = await db.Users.Where(u => u.Id == userId && u.IsActive).Select(u => (Guid?)u.TenantId).FirstOrDefaultAsync();
        if (tenantId is null) return [];
        var key = $"perm:{userId}:{Versions.GetValueOrDefault(tenantId.Value)}";
        if (cache.TryGetValue(key, out HashSet<string>? cached)) return cached!;
        var perms = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId && db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .Select(r => r.Permissions).ToListAsync();
        var set = perms.SelectMany(p => p).ToHashSet();
        cache.Set(key, set, TimeSpan.FromMinutes(5));
        return set;
    }
}

public static class EndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder b, string permission) where TBuilder : IEndpointConventionBuilder =>
        b.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}

// ---------------- Files ----------------

public sealed class LocalFileStorage(IConfiguration config, IWebHostEnvironment env) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(string.IsNullOrWhiteSpace(config["Storage:Path"]) ? Path.Combine(env.ContentRootPath, "App_Data", "files") : config["Storage:Path"]!) + Path.DirectorySeparatorChar;

    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Invalid storage key.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

// ---------------- Errors & validation ----------------

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            DomainException => (400, ex.Message),
            DbUpdateConcurrencyException => (409, "This record was changed by someone else. Reload it and try again."),
            DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } } => (409, "A record with the same code or number already exists."),
            DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 547 } } => (409, "This record is referenced by other records and cannot be changed this way."),
            BadHttpRequestException b => (b.StatusCode, "Invalid request."),
            _ => (500, "An unexpected error occurred."),
        };
        if (status == 500) log.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { status, title }, ct);
        return true;
    }
}

/// <summary>Collects field errors and produces a standard validation problem response.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = [];
    public bool IsValid => _errors.Count == 0;

    public Validator Required(string field, string? value, string label, int max = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, $"{label} is required.");
        else if (max > 0 && value.Trim().Length > max) Add(field, $"{label} must be at most {max} characters.");
        return this;
    }

    public Validator MaxLength(string field, string? value, string label, int max)
    {
        if (value is not null && value.Length > max) Add(field, $"{label} must be at most {max} characters.");
        return this;
    }

    public Validator When(bool condition, string field, string message)
    {
        if (condition) Add(field, message);
        return this;
    }

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
    }

    public IResult Problem() => Results.ValidationProblem(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
}

public static class QueryExtensions
{
    public static IQueryable<T> SortBy<T>(this IQueryable<T> q, string? sort, string? dir, IReadOnlyDictionary<string, Expression<Func<T, object?>>> map, string defaultSort)
    {
        var key = sort is not null && map.ContainsKey(sort) ? sort : defaultSort;
        var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        return desc ? q.OrderByDescending(map[key]) : q.OrderBy(map[key]);
    }

    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    public static string? Clean(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static byte[] ToVersion(this string? base64) => string.IsNullOrEmpty(base64) ? [] : Convert.FromBase64String(base64);
}
