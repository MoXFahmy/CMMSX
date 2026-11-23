using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>One row of the AI call log.</summary>
public sealed record AiCallRow(Guid Id, DateTimeOffset CreatedAt, Guid? UserId, string UserName, AiFeature Feature, Guid? WorkOrderId, string? WorkOrderNumber,
    Guid? WorkRequestId, string? RequestNumber, string Model, int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens, decimal EstimatedCost,
    AiResult Result, int DurationMs);

/// <summary>Call log filters: <c>from</c>/<c>to</c> are the organization's local dates.</summary>
public sealed record AiCallFilter(DateOnly? From, DateOnly? To, string? Feature, string? Result, Guid? UserId, int? Page, int? PageSize);

/// <summary>Settings › AI usage (0.15.0): this month against the limit, twelve months, by feature and person, and every call.</summary>
public static class AiUsage
{
    private static readonly AiResult[] Counted = [AiResult.Answered, AiResult.Refused];

    public static void Map(RouteGroupBuilder g)
    {
        g.MapGet("/usage", SummaryAsync).RequirePermission(Permissions.SettingsManage);
        g.MapGet("/calls", ([AsParameters] AiCallFilter f, CmmsDbContext db, CancellationToken ct) =>
            ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 25, ct)).RequirePermission(Permissions.SettingsManage);
        g.MapGet("/calls/export", ([AsParameters] AiCallFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "ai-calls", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct)).RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<IResult> SummaryAsync(CmmsDbContext db, AiService ai, CancellationToken ct)
    {
        var status = await ai.StatusAsync(ct);
        var tz = AiService.Zone((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone);
        var now = DateTimeOffset.UtcNow;
        var current = AiService.Month(tz, now);
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var firstLocal = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(-11);
        var from = new DateTimeOffset(firstLocal, tz.GetUtcOffset(firstLocal));
        var rows = await db.AiCalls.AsNoTracking().Where(c => c.CreatedAt >= from && c.CreatedAt < current.End)
            .Select(c => new { c.CreatedAt, c.Result, c.Feature, c.UserId, c.UserName, c.EstimatedCost }).ToListAsync(ct);
        string Key(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, tz).ToString("yyyy-MM");
        var months = Enumerable.Range(0, 12).Select(i => firstLocal.AddMonths(i).ToString("yyyy-MM")).Select(m => new
        {
            month = m,
            actions = rows.Count(r => Key(r.CreatedAt) == m && Counted.Contains(r.Result)),
            cost = rows.Where(r => Key(r.CreatedAt) == m).Sum(r => r.EstimatedCost),
        }).ToList();
        var thisMonth = rows.Where(r => r.CreatedAt >= current.Start).ToList();
        var byFeature = thisMonth.GroupBy(r => r.Feature).Select(g => new { feature = g.Key, actions = g.Count(r => Counted.Contains(r.Result)), cost = g.Sum(r => r.EstimatedCost) })
            .OrderByDescending(x => x.actions).ToList();
        var byPerson = thisMonth.GroupBy(r => new { r.UserId, r.UserName })
            .Select(g => new { userId = g.Key.UserId, name = g.Key.UserName, actions = g.Count(r => Counted.Contains(r.Result)), cost = g.Sum(r => r.EstimatedCost) })
            .OrderByDescending(x => x.actions).ThenBy(x => x.name).ToList();
        return Results.Ok(new { status.Ceiling, status.Limit, status.Used, status.RenewsOn, months, byFeature, byPerson });
    }

    public static async Task<PagedResult<AiCallRow>> ListAsync(CmmsDbContext db, AiCallFilter f, int page, int size, CancellationToken ct, int maxPageSize = 500)
    {
        var q = db.AiCalls.AsNoTracking();
        if (f.From is { } || f.To is { })
        {
            var tz = AiService.Zone((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone);
            DateTimeOffset At(DateOnly d) { var l = d.ToDateTime(TimeOnly.MinValue); return new DateTimeOffset(l, tz.GetUtcOffset(l)); }
            if (f.From is { } fr) { var s = At(fr); q = q.Where(c => c.CreatedAt >= s); }
            if (f.To is { } to) { var e = At(to.AddDays(1)); q = q.Where(c => c.CreatedAt < e); }
        }
        if (Enum.TryParse<AiFeature>(f.Feature, true, out var feature)) q = q.Where(c => c.Feature == feature);
        if (Enum.TryParse<AiResult>(f.Result, true, out var result)) q = q.Where(c => c.Result == result);
        if (f.UserId is { } user) q = q.Where(c => c.UserId == user);
        return await q.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new AiCallRow(c.Id, c.CreatedAt, c.UserId, c.UserName, c.Feature, c.WorkOrderId,
                db.WorkOrders.Where(w => w.Id == c.WorkOrderId).Select(w => w.Number).FirstOrDefault(), c.WorkRequestId,
                db.WorkRequests.Where(r => r.Id == c.WorkRequestId).Select(r => r.Number).FirstOrDefault(), c.Model, c.InputTokens, c.OutputTokens,
                c.CacheReadTokens, c.CacheWriteTokens, c.EstimatedCost, c.Result, c.DurationMs))
            .ToPagedAsync(page, size, ct, maxPageSize);
    }
}
