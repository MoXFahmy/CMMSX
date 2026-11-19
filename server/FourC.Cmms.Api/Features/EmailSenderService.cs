using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Background job: sends queued e-mails (the outbox) for every organization, retrying failures after 1, 5, 30 and 120
/// minutes and giving up after the fifth. Rows are claimed before sending, so two app instances never send one twice.
/// </summary>
public sealed class EmailSenderService(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<EmailSenderService> log) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120)];
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepSent = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var o = options.Value;
        if (!o.Available || !o.AutoSend) return;
        var interval = TimeSpan.FromSeconds(Math.Max(1, o.PollSeconds));
        while (!stop.IsCancellationRequested)
        {
            try { while (await RunOnceAsync(ct: stop) == BatchSize) { } } // keep going while there's a backlog
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "E-mail sender run failed"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Sends up to one batch of due e-mails; returns how many were sent. <paramref name="now"/> lets tests move the clock.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        using var scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetService<IEmailSender>();
        if (sender is null) return 0;
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
        var all = db.OutboundEmails.IgnoreQueryFilters();

        var ids = await all.Where(e => e.Status == EmailStatus.Pending && e.NextAttemptAt <= at && (e.LockedUntil == null || e.LockedUntil < at))
            .OrderBy(e => e.NextAttemptAt).Select(e => e.Id).Take(BatchSize).ToListAsync(ct);
        var sent = 0;
        if (ids.Count > 0)
        {
            // Claim: only rows nobody else claimed meanwhile get this run's marker (a lock time unique to this run).
            var claim = at + ClaimFor + TimeSpan.FromTicks(Random.Shared.Next(1, 10_000_000));
            await all.Where(e => ids.Contains(e.Id) && e.Status == EmailStatus.Pending && (e.LockedUntil == null || e.LockedUntil < at))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LockedUntil, claim), ct);
            var rows = await all.Where(e => ids.Contains(e.Id) && e.LockedUntil == claim).ToListAsync(ct);
            var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
            var orgNames = await db.Tenants.Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            var o = options.Value;
            foreach (var row in rows)
            {
                row.Attempts++;
                row.LockedUntil = null;
                try
                {
                    var fromName = EmailText.OneLine($"{orgNames.GetValueOrDefault(row.TenantId) ?? "4C CMMS"} via {o.FromName}");
                    await sender.SendAsync(new EmailMessage(row.ToAddress, row.ToName, o.From!, fromName, row.ReplyTo, row.Subject, row.HtmlBody, row.TextBody), ct);
                    row.Status = EmailStatus.Sent;
                    row.SentAt = DateTimeOffset.UtcNow;
                    row.LastError = null;
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    row.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    if (row.Attempts >= MaxAttempts) row.Status = EmailStatus.Failed;
                    else row.NextAttemptAt = at + RetryDelays[Math.Min(row.Attempts, RetryDelays.Length) - 1];
                    log.LogWarning("E-mail {Id} to {To} failed (attempt {Attempt}): {Error}", row.Id, row.ToAddress, row.Attempts, row.LastError);
                }
                await db.SaveChangesAsync(ct);
            }
        }
        var cutoff = at - KeepSent;
        await all.Where(e => e.Status == EmailStatus.Sent && e.SentAt < cutoff).ExecuteDeleteAsync(ct);
        return sent;
    }
}
