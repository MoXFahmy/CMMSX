using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>
/// Gives due dates saved before 0.5.0, or imported, their due time: the end of that day in the organization's time zone
/// (or, while the organization is still on "UTC", the one zone all its sites share). Idempotent: only empty due times.
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
            foreach (var day in orders)
            {
                var at = Sla.EndOfDay(day, zone);
                await db.WorkOrders.IgnoreQueryFilters().Where(w => w.TenantId == t.Id && w.DueAt == null && w.DueDate == day)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.DueAt, at), ct);
            }
            foreach (var day in requests)
            {
                var at = Sla.EndOfDay(day, zone);
                await db.WorkRequests.IgnoreQueryFilters().Where(r => r.TenantId == t.Id && r.DueAt == null && r.RequiredBy == day)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.DueAt, at), ct);
            }
        }
    }

    private static async Task<TimeZoneInfo> ZoneAsync(CmmsDbContext db, Guid tenantId, string? configured, CancellationToken ct)
    {
        if (configured is { Length: > 0 } && configured != "UTC" && TimeZoneInfo.TryFindSystemTimeZoneById(configured, out var zone)) return zone;
        var siteZones = (await db.Sites.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && s.TimeZone != null).Select(s => s.TimeZone!).Distinct().ToListAsync(ct))
            .Select(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null).OfType<TimeZoneInfo>().DistinctBy(z => z.Id).ToList();
        return siteZones.Count == 1 ? siteZones[0] : Sla.Zone(configured);
    }
}
