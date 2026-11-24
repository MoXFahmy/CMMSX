using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>API keys (0.26.0): how a key looks, is read from a request and is hashed.</summary>
public static class ApiKeyAuth
{
    public const string Scheme = "ApiKey";
    /// <summary>The default scheme: the key handler when a key header is present, else the sign-in cookie.</summary>
    public const string Smart = "Smart";
    public const string KeyIdClaim = "cmx_key";
    public const string ReadOnlyClaim = "cmx_key_ro";
    public const string Invalid = "The API key isn't valid.";
    public const string HeaderName = "X-Api-Key";
    private const string Start = "cmx_";
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>A new key: "cmx_" and 40 random letters and digits; its prefix (8 characters after "cmx_") and hash.</summary>
    public static (string Key, string Prefix, string Hash) Make()
    {
        var key = Start + RandomNumberGenerator.GetString(Letters, 40);
        return (key, key[4..12], HashOf(key));
    }

    public static string HashOf(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>The key a request carries ("Authorization: Bearer cmx_…" or "X-Api-Key: …"); null when it carries none.</summary>
    public static string? Read(HttpRequest request)
    {
        if (request.Headers[HeaderName].ToString() is { Length: > 0 } header) return header.Trim();
        var auth = request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && auth[7..].Trim() is var token && token.StartsWith(Start, StringComparison.Ordinal)
            ? token : null;
    }

    public static bool IsKey(ClaimsPrincipal? user) => user?.FindFirst(KeyIdClaim) is not null;
}

/// <summary>
/// Signs a request in by its API key: the key must exist, be neither revoked nor expired, and belong to an active service account
/// of an active organization. The principal is the service account's, so roles, sites and the activity log work as for a person.
/// </summary>
public sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ApiKeyAuth.Read(Request) is not { } key) return AuthenticateResult.NoResult();
        if (key.Length != 44 || !key.StartsWith("cmx_", StringComparison.Ordinal)) return AuthenticateResult.Fail(ApiKeyAuth.Invalid);
        var prefix = key[4..12];
        var hash = Encoding.ASCII.GetBytes(ApiKeyAuth.HashOf(key));
        // Its own database context: the request's takes its site filters from the principal this builds.
        await using var db = new CmmsDbContext(Context.RequestServices.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
        var now = DateTimeOffset.UtcNow;
        var row = (await db.ApiKeys.IgnoreQueryFilters().AsNoTracking().Where(k => k.Prefix == prefix).ToListAsync(Context.RequestAborted))
            .FirstOrDefault(k => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(k.Hash), hash));
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= now) return AuthenticateResult.Fail(ApiKeyAuth.Invalid);
        var account = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == row.ServiceAccountId && u.TenantId == row.TenantId && u.IsActive && u.IsServiceAccount)
            .Select(u => new { u.Id, u.TenantId, u.FullName }).FirstOrDefaultAsync(Context.RequestAborted);
        if (account is null || !await db.Tenants.AnyAsync(t => t.Id == account.TenantId && t.IsActive, Context.RequestAborted))
            return AuthenticateResult.Fail(ApiKeyAuth.Invalid);
        if (row.LastUsedAt is null || now - row.LastUsedAt > TimeSpan.FromMinutes(1))
            await db.ApiKeys.IgnoreQueryFilters().Where(k => k.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()), new(ClaimTypes.Name, account.FullName),
            new(ClaimNames.TenantId, account.TenantId.ToString()), new(ClaimNames.FullName, account.FullName),
            new(ApiKeyAuth.KeyIdClaim, row.Id.ToString()),
        };
        if (row.ReadOnly) claims.Add(new Claim(ApiKeyAuth.ReadOnlyClaim, "1"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        await Access.EstablishAsync(Context, principal);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>One answer for every bad key, so nothing about a key leaks.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new { title = Translator.T(ApiKeyAuth.Invalid), status = 401 }, Context.RequestAborted);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
