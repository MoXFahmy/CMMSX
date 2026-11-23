using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Service contracts' background pass (0.17.0), built like <see cref="OverdueNotifier"/>: renews contracts with automatic renewal whose end has
/// passed (several periods when the service was down for a while), then sends one alert e-mail per notice deadline or end date in its window.
/// Renewal runs whether or not e-mail is on; a new deadline after a renewal may alert again.
/// </summary>
public sealed class ContractNotifier(IServiceScopeFactory scopes, IConfiguration config, ILogger<ContractNotifier> log) : BackgroundService
{
    public const int MaxPerRun = 200;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Contracts:AutoRun", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Contracts:StartDelaySeconds", 120)), stop); } catch (OperationCanceledException) { return; }
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Contracts:IntervalMinutes", 60)));
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnceAsync(DateTimeOffset.UtcNow, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Contract renewal and alert run failed"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over the active organizations (or only <paramref name="onlyTenant"/>): periods added and e-mails queued.</summary>
    public async Task<(int Renewed, int Emails)> RunOnceAsync(DateTimeOffset now, CancellationToken ct, Guid? onlyTenant = null, int maxPerRun = MaxPerRun)
    {
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        var mail = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        List<(Guid Id, string TimeZone, bool Mail)> tenants;
        await using (var root = new CmmsDbContext(options, new FixedTenantContext(null)))
            tenants = (await root.Tenants.AsNoTracking().Where(t => t.IsActive && (onlyTenant == null || t.Id == onlyTenant)).ToListAsync(ct))
                .Select(t => (t.Id, t.Settings.TimeZone, mail.Available && t.Settings.EmailEnabled && mail.AllowedFor(t))).ToList();
        int renewed = 0, emails = 0;
        foreach (var (tenantId, timeZone, mailOn) in tenants)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Sla.Zone(timeZone)).DateTime);
            try { renewed += await RenewAsync(options, tenantId, today, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Contract renewal failed for tenant {Tenant}", tenantId); }
            if (!mailOn) continue;
            try { emails += await AlertAsync(options, mail, tenantId, today, ct, maxPerRun); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Contract alerts failed for tenant {Tenant}", tenantId); }
        }
        return (renewed, emails);
    }

    /// <summary>
    /// Renews each due contract in a database context of its own: one contract that can't be saved (someone edited it at the same moment,
    /// another server renewed it first) is skipped until the next pass and never spoils the others.
    /// </summary>
    private async Task<int> RenewAsync(DbContextOptions<CmmsDbContext> options, Guid tenantId, DateOnly today, CancellationToken ct)
    {
        List<Guid> due;
        await using (var list = new CmmsDbContext(options, new FixedTenantContext(tenantId, null, "CMX")))
            due = await list.Contracts.AsNoTracking().Where(c => c.AutoRenew && c.TerminatedOn == null && c.RenewalMonths >= 1 && c.EndDate < today)
                .OrderBy(c => c.EndDate).ThenBy(c => c.Number).Select(c => c.Id).Take(MaxPerRun).ToListAsync(ct);
        var added = 0;
        foreach (var id in due)
        {
            await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId, null, "CMX"));
            try
            {
                var c = await db.Contracts.Include(x => x.Periods).FirstOrDefaultAsync(x => x.Id == id, ct);
                // Looked at again here: another pass may have renewed or someone terminated it since the list was made.
                if (c is null || !c.AutoRenew || c.TerminatedOn is not null || c.RenewalMonths is not >= 1 || c.EndDate >= today) continue;
                var latest = c.Periods.OrderBy(p => p.Number).LastOrDefault();
                var number = latest?.Number ?? 0;
                var renewals = ContractTerms.Renewals(c.EndDate, c.RenewalMonths.Value, today);
                foreach (var (start, end) in renewals)
                {
                    db.ContractPeriods.Add(new ContractPeriod
                    {
                        TenantId = tenantId, ContractId = c.Id, Number = ++number, StartDate = start, EndDate = end, Value = latest?.Value ?? c.Value, IsRenewal = true,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                    c.EndDate = end;
                }
                await db.SaveChangesAsync(ct);
                added += renewals.Count;
            }
            // This contract waits for the next pass; the others go on.
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Renewing contract {Contract} failed", id); }
        }
        return added;
    }

    /// <summary>
    /// Queues one alert per contract whose deadline is in its window and wasn't alerted yet, each saved with its contract in a context of its
    /// own: the e-mail and "alerted for" are saved together or not at all, and a clash (another server alerting it) is simply skipped.
    /// </summary>
    private async Task<int> AlertAsync(DbContextOptions<CmmsDbContext> options, EmailOutbox mail, Guid tenantId, DateOnly today, CancellationToken ct, int maxPerRun)
    {
        var ctx = new FixedTenantContext(tenantId, null, "CMX");
        List<Guid> waiting;
        await using (var list = new CmmsDbContext(options, ctx))
            // Already alerted for today's deadline: left out here, so they never fill the batch and starve the others.
            waiting = await ContractEndpoints.NeedingAttention(list.Contracts.AsNoTracking(), today)
                .Where(c => c.AlertSentFor == null || c.AlertSentFor != (c.AutoRenew ? c.EndDate.AddDays(-c.NoticeDays) : c.EndDate))
                .OrderBy(c => c.EndDate).ThenBy(c => c.Number).Select(c => c.Id).Take(maxPerRun).ToListAsync(ct);
        var queued = 0;
        foreach (var id in waiting)
        {
            await using var db = new CmmsDbContext(options, ctx);
            try
            {
                var c = await db.Contracts.FirstOrDefaultAsync(x => x.Id == id, ct);
                var deadline = c is null ? null : ContractTerms.Deadline(c.AutoRenew, c.EndDate, c.NoticeDays, c.TerminatedOn);
                if (c is null || deadline is null || c.AlertSentFor == deadline || !ContractTerms.NeedsAttention(today, deadline, c.AlertDays)) continue;
                await mail.ContractAlertAsync(db, ctx, c, deadline.Value, ct);
                c.AlertSentFor = deadline;
                await db.SaveChangesAsync(ct);
                queued += db.ChangeTracker.Entries<FourC.Cmms.Core.Notifications.OutboundEmail>().Count();
            }
            // Tried again on the next pass.
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Alerting contract {Contract} failed", id); }
        }
        return queued;
    }
}
