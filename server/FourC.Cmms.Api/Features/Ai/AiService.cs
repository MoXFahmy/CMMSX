using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>This month's allowance as a page shows it: <c>renewsOn</c> is the organization's local date the next month starts.</summary>
public sealed record AiStatus(bool Configured, bool Enabled, int Ceiling, int Limit, int Used, DateOnly RenewsOn);

/// <summary>The AI assistant's switch, allowance and call log (0.15.0).</summary>
public sealed class AiService(CmmsDbContext db, IAiClient client, IOptions<AiOptions> options)
{
    /// <summary>A place held for a call longer than this is treated as abandoned and stops counting.</summary>
    public static readonly TimeSpan PendingCounts = TimeSpan.FromMinutes(5);

    public AiOptions Options => options.Value;

    /// <summary>The organization's limit under the server's ceiling (an empty limit is the ceiling).</summary>
    public static int EffectiveLimit(int? limit, int ceiling) => Math.Max(0, Math.Min(limit ?? ceiling, ceiling));

    /// <summary>The organization's month around <paramref name="now"/>: start and end as instants, and the local date it renews.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End, DateOnly RenewsOn) Month(TimeZoneInfo tz, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var first = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var next = first.AddMonths(1);
        return (new DateTimeOffset(first, tz.GetUtcOffset(first)), new DateTimeOffset(next, tz.GetUtcOffset(next)), DateOnly.FromDateTime(next));
    }

    public static TimeZoneInfo Zone(string? id) => TimeZoneInfo.TryFindSystemTimeZoneById(id ?? "UTC", out var tz) ? tz : TimeZoneInfo.Utc;

    /// <summary>Calls counted this month: answered and refused, plus places held for calls still waiting on Claude.</summary>
    public async Task<int> UsedAsync(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken ct)
    {
        var stale = now - PendingCounts;
        return await db.AiCalls.CountAsync(c => c.CreatedAt >= start && c.CreatedAt < end
            && (c.Result == AiResult.Answered || c.Result == AiResult.Refused || (c.Result == AiResult.Pending && c.CreatedAt > stale)), ct);
    }

    public async Task<AiStatus> StatusAsync(CancellationToken ct)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var now = DateTimeOffset.UtcNow;
        var month = Month(Zone(settings.TimeZone), now);
        var ceiling = Math.Max(0, options.Value.MonthlyCeiling);
        return new AiStatus(client.Configured, settings.AiEnabled, ceiling, EffectiveLimit(settings.AiMonthlyLimit, ceiling),
            await UsedAsync(month.Start, month.End, now, ct), month.RenewsOn);
    }
}
