using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Security.Claims;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
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
        var roles = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId && db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .Select(r => new { r.IsSystem, r.Permissions }).ToListAsync();
        // The system Administrator role always has every permission, including those added by later releases.
        var set = roles.Any(r => r.IsSystem) ? Permissions.All.Select(p => p.Code).ToHashSet() : roles.SelectMany(r => r.Permissions).ToHashSet();
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

/// <summary>
/// Attachments in Azure Blob Storage. In Azure the app signs in with its managed identity (no keys);
/// a connection string is accepted for local development (e.g. Azurite).
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly bool _ensureContainer;
    private volatile bool _containerReady;

    public AzureBlobFileStorage(IConfiguration config)
    {
        var name = config["Storage:Container"] is { Length: > 0 } c ? c : "files";
        if (config["Storage:ConnectionString"] is { Length: > 0 } cs)
        {
            _container = new BlobContainerClient(cs, name);
            _ensureContainer = true; // local emulator: create on first use; in Azure the template creates it
        }
        else
        {
            var endpoint = config["Storage:BlobEndpoint"] is { Length: > 0 } e ? e
                : throw new InvalidOperationException("Storage:BlobEndpoint (or Storage:ConnectionString) is required when Storage:Provider is AzureBlob.");
            _container = new BlobContainerClient(new Uri(new Uri(endpoint), name), AzureCredentials.Create(config));
        }
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (!_ensureContainer || _containerReady) return;
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        _containerReady = true;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(key).UploadAsync(content, overwrite: true, ct);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try { return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct); }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try { await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }
}

/// <summary>
/// Attachments in Amazon S3. On AWS the app uses its ECS task role (no keys); Storage:S3:ServiceUrl plus access keys
/// point it at an S3-compatible server (MinIO, moto) for local testing.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly string? _region;
    private readonly bool _ensureBucket;
    private volatile bool _bucketReady;

    public S3FileStorage(IConfiguration config)
    {
        _bucket = config["Storage:Bucket"] is { Length: > 0 } b ? b
            : throw new InvalidOperationException("Storage:Bucket is required when Storage:Provider is S3.");
        _region = config["Storage:Region"] is { Length: > 0 } region ? region : null;
        var s3Config = new AmazonS3Config();
        if (_region is not null) s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(_region);
        if (config["Storage:S3:ServiceUrl"] is { Length: > 0 } url)
        {
            s3Config.ServiceURL = url;
            s3Config.ForcePathStyle = true;
            s3Config.AuthenticationRegion = _region ?? "us-east-1";
            _s3 = new AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials(config["Storage:S3:AccessKey"], config["Storage:S3:SecretKey"]), s3Config);
            _ensureBucket = true; // local emulator: create on first use; on AWS the template creates it
        }
        else
        {
            _s3 = new AmazonS3Client(s3Config); // default credential chain = the ECS task role
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!_ensureBucket || _bucketReady) return;
        var request = new PutBucketRequest { BucketName = _bucket };
        if (_region is not null and not "us-east-1") request.BucketRegion = S3Region.FindValue(_region); // S3 needs the region outside us-east-1
        try { await _s3.PutBucketAsync(request, ct); }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
        _bucketReady = true;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = content, AutoCloseStream = false }, ct);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try { return (await _s3.GetObjectAsync(_bucket, key, ct)).ResponseStream; }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _s3.DeleteObjectAsync(_bucket, key, ct);

    public void Dispose() => _s3.Dispose();
}

public static class AzureCredentials
{
    /// <summary>The app's user-assigned managed identity when Azure:ClientId is set; otherwise the developer's Azure sign-in.</summary>
    public static TokenCredential Create(IConfiguration config) =>
        config["Azure:ClientId"] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential();
}

// ---------------- Database ----------------

public static class DatabaseConnection
{
    /// <summary>
    /// ConnectionStrings:Cmms, plus Database:User / Database:Password when the login is supplied separately
    /// (e.g. injected from AWS Secrets Manager) so the password never has to be written into the connection string.
    /// </summary>
    public static string? Build(IConfiguration config)
    {
        var cs = config.GetConnectionString("Cmms");
        if (config["Database:User"] is not { Length: > 0 } user) return cs;
        return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs) { UserID = user, Password = config["Database:Password"] ?? "" }.ConnectionString;
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
            BadHttpRequestException b => (b.StatusCode, b.StatusCode == 404 ? "Not found." : $"Invalid request: {b.Message}"),
            _ => (500, "An unexpected error occurred."),
        };
        if (status == 500) log.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
        else if (ex is BadHttpRequestException) log.LogWarning("Bad request on {Path}: {Message}", ctx.Request.Path, ex.Message);
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
