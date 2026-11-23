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
    public async Task<(int Renewed, int Emails)> RunOnceAsync(DateTimeOffset now, CancellationToken ct, Guid? onlyTenant = null)
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
            try { emails += await AlertAsync(options, mail, tenantId, today, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Contract alerts failed for tenant {Tenant}", tenantId); }
        }
        return (renewed, emails);
    }

    private static async Task<int> RenewAsync(DbContextOptions<CmmsDbContext> options, Guid tenantId, DateOnly today, CancellationToken ct)
    {
        await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId, null, "CMX"));
        var due = await db.Contracts.Include(c => c.Periods)
            .Where(c => c.AutoRenew && c.TerminatedOn == null && c.RenewalMonths >= 1 && c.EndDate < today).Take(MaxPerRun).ToListAsync(ct);
        var added = 0;
        foreach (var c in due)
        {
            var latest = c.Periods.OrderBy(p => p.Number).LastOrDefault();
            var number = latest?.Number ?? 0;
            foreach (var (start, end) in ContractTerms.Renewals(c.EndDate, c.RenewalMonths!.Value, today))
            {
                var period = new ContractPeriod
                {
                    TenantId = tenantId, ContractId = c.Id, Number = ++number, StartDate = start, EndDate = end, Value = latest?.Value ?? c.Value, IsRenewal = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ContractPeriods.Add(period);
                c.EndDate = end;
                added++;
            }
            try { await db.SaveChangesAsync(ct); }
            // Someone saved the contract at the same moment, or another server renewed it: the next pass looks again.
            catch (DbUpdateException) { db.ChangeTracker.Clear(); added -= number - (latest?.Number ?? 0); }
        }
        return added;
    }

    private static async Task<int> AlertAsync(DbContextOptions<CmmsDbContext> options, EmailOutbox mail, Guid tenantId, DateOnly today, CancellationToken ct)
    {
        var ctx = new FixedTenantContext(tenantId, null, "CMX");
        await using var db = new CmmsDbContext(options, ctx);
        var window = await ContractEndpoints.NeedingAttention(db.Contracts, today).Take(MaxPerRun).ToListAsync(ct);
        var queued = 0;
        foreach (var c in window)
        {
            var deadline = ContractTerms.Deadline(c.AutoRenew, c.EndDate, c.NoticeDays, c.TerminatedOn);
            if (deadline is null || c.AlertSentFor == deadline) continue;
            var before = db.ChangeTracker.Entries<FourC.Cmms.Core.Notifications.OutboundEmail>().Count();
            await mail.ContractAlertAsync(db, ctx, c, deadline.Value, ct);
            c.AlertSentFor = deadline;
            try
            {
                await db.SaveChangesAsync(ct);
                queued += db.ChangeTracker.Entries<FourC.Cmms.Core.Notifications.OutboundEmail>().Count() - before;
            }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        }
        return queued;
    }
}
