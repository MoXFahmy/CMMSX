using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>
/// Gives due dates saved before 0.5.0, or imported, their due time: the end of that day in the organization's time zone
/// (or, while the organization is still on "UTC", the one zone all its sites share). Idempotent: only empty due times.
/// Runs at startup, after sample data and imports, and with the hourly PM scheduler (rows written by an older server during
/// a rolling deploy). One UPDATE per run of days with the same UTC offset, so years of imported work take a few statements.
/// </summary>
public static class DueBackfill
{
    public static async Task RunAsync(CmmsDbContext db, CancellationToken ct = default)
    {
        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Select(t => new { t.Id, t.Settings }).ToListAsync(ct);
        foreach (var t in tenants)
        {
            var orders = await db.WorkOrders.IgnoreQueryFilters().Where(w => w.TenantId == t.Id && w.DueAt == null && w.DueDate != null)
                .Select(w => w.DueDate!.Value).Distinct().ToListAsync(ct);
            var requests = await db.WorkRequests.IgnoreQueryFilters().Where(r => r.TenantId == t.Id && r.DueAt == null && r.RequiredBy != null)
                .Select(r => r.RequiredBy!.Value).Distinct().ToListAsync(ct);
            if (orders.Count == 0 && requests.Count == 0) continue;
            var zone = await ZoneAsync(db, t.Id, t.Settings.TimeZone, ct);
            foreach (var (from, to, seconds) in Runs(orders, zone))
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE WorkOrders SET DueAt = TODATETIMEOFFSET(DATEADD(SECOND, {seconds}, CAST(DueDate AS datetime2)), 0)
                    WHERE TenantId = {t.Id} AND DueAt IS NULL AND DueDate BETWEEN {from} AND {to}
                    """, ct);
            foreach (var (from, to, seconds) in Runs(requests, zone))
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE WorkRequests SET DueAt = TODATETIMEOFFSET(DATEADD(SECOND, {seconds}, CAST(RequiredBy AS datetime2)), 0)
                    WHERE TenantId = {t.Id} AND DueAt IS NULL AND RequiredBy BETWEEN {from} AND {to}
                    """, ct);
        }
    }

    /// <summary>
    /// Consecutive days (of those present) whose end of day has the same UTC offset, with the seconds from midnight UTC to
    /// that end of day. Only listed days have rows, so a range may safely span days that aren't listed.
    /// </summary>
    internal static IEnumerable<(DateOnly From, DateOnly To, int Seconds)> Runs(IEnumerable<DateOnly> days, TimeZoneInfo zone)
    {
        (DateOnly From, DateOnly To, int Seconds)? run = null;
        foreach (var day in days.Order())
        {
            var seconds = (int)(Sla.EndOfDay(day, zone) - new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)).TotalSeconds;
            if (run is { } r && r.Seconds == seconds) { run = (r.From, day, seconds); continue; }
            if (run is { } done) yield return done;
            run = (day, day, seconds);
        }
        if (run is { } last) yield return last;
    }

    private static async Task<TimeZoneInfo> ZoneAsync(CmmsDbContext db, Guid tenantId, string? configured, CancellationToken ct)
    {
        if (configured is { Length: > 0 } && configured != "UTC" && TimeZoneInfo.TryFindSystemTimeZoneById(configured, out var zone)) return zone;
        var siteZones = (await db.Sites.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && s.TimeZone != null).Select(s => s.TimeZone!).Distinct().ToListAsync(ct))
            .Select(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null).OfType<TimeZoneInfo>().DistinctBy(z => z.Id).ToList();
        return siteZones.Count == 1 ? siteZones[0] : Sla.Zone(configured);
    }
}
