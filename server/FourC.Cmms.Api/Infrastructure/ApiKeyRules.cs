using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// What a request made with an API key may do (0.26.0): read only when its key says so, never manage who has access, never act
/// as a person signing in or as the technician app; and at most so many requests a minute per key.
/// </summary>
public static class ApiKeyRules
{
    public const string ReadOnly = "This key is read-only.";
    public const string NotWithKeys = "API keys can't do this.";
    public const string TooMany = "Too many requests with this key. Try again in {0} seconds.";

    /// <summary>Areas no key reaches, whatever its account's roles.</summary>
    private static readonly string[] Closed = ["/api/auth", "/api/tech", "/api/api-keys", "/api/service-accounts", "/api/settings/security", "/api/anonymise"];

    /// <summary>Areas a key may read but not change: people and roles.</summary>
    private static readonly string[] ReadOnlyAreas = ["/api/users", "/api/roles"];

    public static bool Blocked(string method, PathString path) =>
        Closed.Any(p => path.StartsWithSegments(p))
        || IsDevices(path) // a person's phones and their sign-ins, whatever the method (0.26.1)
        || (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && ReadOnlyAreas.Any(p => path.StartsWithSegments(p)));

    /// <summary>/api/users/{id}/devices and below.</summary>
    private static bool IsDevices(PathString path)
    {
        var parts = (path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 && parts[0].Equals("api", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("users", StringComparison.OrdinalIgnoreCase)
               && parts[3].Equals("devices", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Refuses what a key may not do (after it has signed the request in, before permissions are checked).</summary>
    public static IApplicationBuilder UseApiKeyRules(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (ApiKeyAuth.IsKey(http.User))
        {
            var refusal = Blocked(http.Request.Method, http.Request.Path) ? NotWithKeys
                : http.User.FindFirst(ApiKeyAuth.ReadOnlyClaim) is not null && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) ? ReadOnly
                : null;
            if (refusal is not null)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { title = Translator.T(refusal), status = 403 });
                return;
            }
        }
        await next();
    });

    /// <summary>A fixed one-minute window per key (Api:RequestsPerMinute, 120 by default); people signed in aren't limited.</summary>
    public static IServiceCollection AddApiKeyLimits(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = Math.Max(1, config.GetValue("Api:RequestsPerMinute", 120));
        return services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.User.FindFirst(ApiKeyAuth.KeyIdClaim)?.Value is { } key
                    ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("people"));
            o.OnRejected = async (context, ct) =>
            {
                var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : 60;
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = wait.ToString();
                // A key's own limit says so in seconds; a visitor address's (0.29.0) in minutes.
                var title = ApiKeyAuth.IsKey(context.HttpContext.User) ? string.Format(TooMany, wait) : string.Format(AbuseLimits.TooManyFromNetwork, (int)Math.Ceiling(wait / 60.0));
                await context.HttpContext.Response.WriteAsJsonAsync(new { title = Translator.T(title), status = 429 }, ct);
            };
        });
    }
}
