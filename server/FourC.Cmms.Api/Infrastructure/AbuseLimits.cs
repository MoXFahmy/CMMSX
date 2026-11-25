using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Limits per visitor address (0.29.0) on what anyone can reach without signing in: wrong passwords, sign-ups, e-mail resends and
/// confirmations, requester answer links. Addresses are kept in memory only, never in the database, the activity log or the
/// application log. Switch all of it off with Abuse:Enabled=false. API keys keep their own limit (<see cref="ApiKeyRules"/>).
/// </summary>
public static class AbuseLimits
{
    public const string TooManyFromNetwork = "Too many attempts from your network. Try again in {0} minutes.";

    public const string SignUp = "abuse-signup";
    public const string Resend = "abuse-resend";
    public const string Confirm = "abuse-confirm";
    public const string Answer = "abuse-answer";

    public static bool Enabled(IConfiguration config) => config.GetValue("Abuse:Enabled", true);

    /// <summary>The refusal for an address that has used up its tries: 429 with Retry-After and the wait in whole minutes.</summary>
    public static IResult Refuse(HttpContext http, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        http.Response.Headers.RetryAfter = seconds.ToString();
        return Results.Problem(title: Translator.T(string.Format(TooManyFromNetwork, (int)Math.Ceiling(seconds / 60.0))), statusCode: StatusCodes.Status429TooManyRequests);
    }

    public static IServiceCollection AddAbuseLimits(this IServiceCollection services, IConfiguration config)
    {
        var enabled = Enabled(config);
        services.AddSingleton<SignInGuard>();
        services.Configure<RateLimiterOptions>(o =>
        {
            void Add(string policy, string setting, int fallback, TimeSpan window) =>
                o.AddPolicy(policy, http =>
                {
                    if (!enabled) return RateLimitPartition.GetNoLimiter("off");
                    var permits = Math.Max(1, config.GetValue(setting, fallback));
                    var address = VisitorAddress.Of(http, config);
                    return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{address}",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 });
                });
            Add(SignUp, "Abuse:SignUpsPerHour", 5, TimeSpan.FromHours(1));
            Add(Resend, "Abuse:ResendsPerHour", 10, TimeSpan.FromHours(1));
            Add(Confirm, "Abuse:ConfirmsPer15Minutes", 20, TimeSpan.FromMinutes(15));
            Add(Answer, "Abuse:AnswersPer15Minutes", 30, TimeSpan.FromMinutes(15));
        });
        return services;
    }
}

/// <summary>
/// Wrong passwords per address (0.29.0): so many in a window and that address is turned away, whoever it tries to be. A wrong
/// password, a locked account and an unknown address all count; a successful sign-in never does. The count is in memory only.
/// </summary>
public sealed class SignInGuard(IMemoryCache cache, IConfiguration config, ILogger<SignInGuard> log)
{
    private sealed class Tries { public readonly List<DateTimeOffset> At = []; }

    private TimeSpan Window => TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Abuse:SignInWindowMinutes", 15)));
    private int Limit => Math.Max(1, config.GetValue("Abuse:SignInFailures", 20));
    private static string Key(string address) => "signin-failures|" + address;

    public bool IsBlocked(string address, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!AbuseLimits.Enabled(config) || !cache.TryGetValue(Key(address), out Tries? tries) || tries is null) return false;
        lock (tries)
        {
            var now = DateTimeOffset.UtcNow;
            tries.At.RemoveAll(t => t <= now - Window);
            if (tries.At.Count < Limit) return false;
            retryAfter = tries.At[0] + Window - now;
        }
        log.LogWarning("SignInRefused"); // counted with SignInFailed by the alarm, so an attack that is being turned away keeps it up; no address, no e-mail
        return true;
    }

    public void Failed(string address)
    {
        log.LogWarning("SignInFailed"); // for the alarm on guessing; no address, no e-mail
        if (!AbuseLimits.Enabled(config)) return;
        var tries = cache.GetOrCreate(Key(address), e => { e.SlidingExpiration = Window; return new Tries(); })!;
        lock (tries)
        {
            var now = DateTimeOffset.UtcNow;
            tries.At.RemoveAll(t => t <= now - Window);
            tries.At.Add(now);
        }
    }
}
