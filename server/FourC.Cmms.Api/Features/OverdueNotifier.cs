using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Sends the overdue e-mail (0.10.0): once for each due time an active work order passes. Work that went overdue long ago (more than
/// <see cref="TooOld"/>) is only marked, so switching e-mail on never floods people with old backlog.
/// </summary>
public sealed class OverdueNotifier(IServiceScopeFactory scopes, IConfiguration config, ILogger<OverdueNotifier> log) : BackgroundService
{
    public static readonly TimeSpan TooOld = TimeSpan.FromHours(48);
    public const int MaxPerRun = 200;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Email:OverdueAutoRun", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Email:OverdueStartDelaySeconds", 90)), stop); } catch (OperationCanceledException) { return; }
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Email:OverdueIntervalMinutes", 30)));
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnceAsync(DateTimeOffset.UtcNow, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Overdue e-mail run failed"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over the organizations with e-mail on (or only <paramref name="onlyTenant"/>). Returns the e-mails queued.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct, Guid? onlyTenant = null)
    {
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        var mail = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        if (!mail.Available) return 0;
        List<Guid> tenantIds;
        await using (var root = new CmmsDbContext(options, new FixedTenantContext(null)))
        {
            var tenants = await root.Tenants.AsNoTracking().Where(t => t.IsActive && (onlyTenant == null || t.Id == onlyTenant)).ToListAsync(ct);
            tenantIds = tenants.Where(t => t.Settings.EmailEnabled && mail.AllowedFor(t)).Select(t => t.Id).ToList();
        }
        var queued = 0;
        var tooOld = now - TooOld;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                var ctx = new FixedTenantContext(tenantId, null, "CMX");
                await using var db = new CmmsDbContext(options, ctx);
                var unnotified = db.WorkOrders.Where(w => (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
                    && w.DueAt != null && w.DueAt < now && (w.OverdueNotifiedFor == null || w.OverdueNotifiedFor != w.DueAt));
                // Long-overdue backlog (imported, or e-mail was off) is marked in one go, so it never holds up work that just went overdue.
                await unnotified.Where(w => w.DueAt < tooOld).ExecuteUpdateAsync(s => s.SetProperty(w => w.OverdueNotifiedFor, w => w.DueAt), ct);
                var late = await unnotified.Where(w => w.DueAt >= tooOld).Include(w => w.Assignees).OrderBy(w => w.DueAt).Take(MaxPerRun).ToListAsync(ct);
                if (late.Count == 0) continue;
                var before = db.ChangeTracker.Entries<FourC.Cmms.Core.Notifications.OutboundEmail>().Count();
                foreach (var w in late)
                {
                    await mail.WorkOrderOverdueAsync(db, ctx, w, ct);
                    w.OverdueNotifiedFor = w.DueAt;
                }
                queued += db.ChangeTracker.Entries<FourC.Cmms.Core.Notifications.OutboundEmail>().Count() - before;
                await db.SaveChangesAsync(ct);
            }
            // One organization's trouble (an edit saved at the same moment) doesn't stop the others; the next run tries again.
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Overdue e-mails failed for tenant {Tenant}", tenantId); }
        }
        return queued;
    }
}
