using System.Diagnostics;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>This month's allowance as a page shows it: <c>renewsOn</c> is the organization's local date the next month starts.</summary>
public sealed record AiStatus(bool Configured, bool Enabled, int Ceiling, int Limit, int Used, DateOnly RenewsOn);

/// <summary>An AI press that can't go ahead or didn't work; the endpoint answers with the status and the (translated) message.</summary>
public sealed class AiProblem(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Claude's answer to one press, and the call-log row it was written to.</summary>
public sealed record AiRun(AiReply Reply, Guid CallId);

/// <summary>The AI assistant's switch, allowance and call log (0.15.0).</summary>
public sealed class AiService(CmmsDbContext db, ITenantContext ctx, IAiClient client, IOptions<AiOptions> options, ILogger<AiService> log)
{
    public const string NotSetUp = "AI isn't set up on this server.";
    public const string SwitchedOff = "The AI assistant is off for this organization.";
    public const string UsedUp = "This month's AI allowance ({0}) is used up. It renews on {1}.";
    public const string NoAnswer = "The AI assistant didn't answer. Try again in a moment.";

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

    /// <summary>US dollars for a reply's tokens at the server's prices.</summary>
    public static decimal Cost(AiReply r, AiOptions o) => Math.Round(
        (r.InputTokens * o.InputPrice + r.OutputTokens * o.OutputPrice + r.CacheReadTokens * o.CacheReadPrice + r.CacheWriteTokens * o.CacheWritePrice) / 1_000_000m, 6);

    /// <summary>
    /// Asks Claude once, if the switch is on, the server has a key and the month's allowance isn't used up. The check and a place in
    /// the call log are taken under a lock per organization (so two presses at once can't pass the limit); Claude is called outside
    /// it, and the row is finished with the answer, the tokens and the cost. A call that fails before an answer doesn't count.
    /// </summary>
    public async Task<AiRun> RunAsync(AiFeature feature, Guid? workOrderId, Guid? workRequestId, AiAsk ask, CancellationToken ct)
    {
        if (!client.Configured) throw new AiProblem(StatusCodes.Status409Conflict, NotSetUp);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
        var settings = tenant.Settings;
        if (!settings.AiEnabled) throw new AiProblem(StatusCodes.Status409Conflict, SwitchedOff);
        var o = options.Value;
        var limit = EffectiveLimit(settings.AiMonthlyLimit, Math.Max(0, o.MonthlyCeiling));
        var call = new AiCall
        {
            TenantId = tenant.Id, UserId = ctx.UserId, UserName = (ctx.UserName ?? "").Length > 200 ? ctx.UserName![..200] : ctx.UserName ?? "",
            Feature = feature, WorkOrderId = workOrderId, WorkRequestId = workRequestId, Model = o.Model, Result = AiResult.Pending,
        };
        var renews = await db.InTransactionAsync(async token =>
        {
            var resource = $"ai:{tenant.Id}";
            await db.Database.ExecuteSqlAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @r < 0 THROW 51000, 'The AI allowance lock timed out.', 1;", token);
            var now = DateTimeOffset.UtcNow;
            var month = Month(Zone(settings.TimeZone), now);
            call.CreatedAt = now;
            if (await UsedAsync(month.Start, month.End, now, token) >= limit) call.Result = AiResult.OverLimit;
            db.AiCalls.Add(call);
            await db.SaveChangesAsync(token);
            return month.RenewsOn;
        }, ct);
        if (call.Result == AiResult.OverLimit)
            throw new AiProblem(StatusCodes.Status429TooManyRequests, string.Format(UsedUp, limit, Translator.Date(renews)));

        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds)));
        try
        {
            var reply = await client.AskAsync(ask, timeout.Token);
            call.Result = reply.Refused ? AiResult.Refused : AiResult.Answered;
            call.Model = Clip(reply.Model, 100) is { Length: > 0 } m ? m : o.Model;
            (call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.CacheWriteTokens) = (reply.InputTokens, reply.OutputTokens, reply.CacheReadTokens, reply.CacheWriteTokens);
            call.EstimatedCost = Cost(reply, o);
            return new AiRun(reply, call.Id);
        }
        catch (Exception e) when (e is AiUnavailableException or OperationCanceledException or TimeoutException)
        {
            call.Result = AiResult.Failed;
            log.LogWarning(e, "AI call failed ({Feature})", feature);
            if (ct.IsCancellationRequested) throw;
            throw new AiProblem(StatusCodes.Status502BadGateway, NoAnswer);
        }
        finally
        {
            call.DurationMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
            await db.SaveChangesAsync(CancellationToken.None); // the row is finished even when the person has gone
        }
    }

    private static string Clip(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;
}
