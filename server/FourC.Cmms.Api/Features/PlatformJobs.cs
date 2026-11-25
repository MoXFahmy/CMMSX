using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// The platform's own jobs (0.29.0), over every organization: once a day, unconfirmed sign-ups past their week are removed. Starts two
/// minutes after the server, then looks at the UTC date every hour; the last day it ran is kept in memory, so a restart runs that day's
/// work once more (each job is safe to repeat). One server runs it, like the PM scheduler. Off when <c>Platform:JobsAutoRun</c> is false.
/// </summary>
public sealed class PlatformJobs(IServiceScopeFactory scopes, IConfiguration config, ILogger<PlatformJobs> log) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    /// <summary>The UTC date the daily work last ran on.</summary>
    private DateOnly? _lastDaily;

    /// <summary>The UTC date the weekly export last finished on; a failed export leaves it, so the next hourly look tries again that Sunday.</summary>
    private DateOnly? _lastExport;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Platform:JobsAutoRun", true)) return;
        try
        {
            await Task.Delay(StartDelay, stop);
            while (!stop.IsCancellationRequested)
            {
                try { await TickAsync(DateTimeOffset.UtcNow, stop); }
                catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogError(ex, "Platform jobs run failed"); }
                await Task.Delay(Tick, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    /// <summary>One look at the clock: the daily work once per UTC date.</summary>
    private async Task TickAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (_lastDaily != today)
        {
            await RunDailyAsync(now, ct);
            _lastDaily = today;
        }
        await RunWeeklyAsync(now, ct);
    }

    /// <summary>The day's work, each job on its own: one that fails is logged and the others still run.</summary>
    public async Task RunDailyAsync(DateTimeOffset now, CancellationToken ct)
    {
        await RunJobAsync("Removing unconfirmed sign-ups", db => PendingSignUps.PurgeAsync(db, now, ct, log), ct);
        await RunJobAsync("Measuring the database", db => DatabaseSize.MeasureAsync(db, log, ct), ct);
    }

    /// <summary>The weekly work, when due: the native export. Remembered as done only once it succeeds.</summary>
    public async Task RunWeeklyAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!ExportDue(now, _lastExport)) return;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (await RunJobWithResultAsync("Weekly database export", db => NativeBackup.RunAsync(db, config, log, today, ct), ct)) _lastExport = today;
    }

    /// <summary>The weekly export is due on a Sunday from 02:00 UTC, once that Sunday (a server down all Sunday waits for the next one).</summary>
    public static bool ExportDue(DateTimeOffset nowUtc, DateOnly? lastExport)
    {
        var utc = nowUtc.UtcDateTime;
        return utc.DayOfWeek == DayOfWeek.Sunday && utc.TimeOfDay >= TimeSpan.FromHours(2) && lastExport != DateOnly.FromDateTime(utc);
    }

    /// <summary>A job on a database context of its own, bound to no organization: the job says which rows it works on.</summary>
    private Task RunJobAsync(string job, Func<CmmsDbContext, Task> work, CancellationToken ct)
        => RunJobWithResultAsync(job, async db => { await work(db); return true; }, ct);

    /// <summary>Like <see cref="RunJobAsync"/>, saying whether the job finished: false when it reported failure or threw (logged).</summary>
    private async Task<bool> RunJobWithResultAsync(string job, Func<CmmsDbContext, Task<bool>> work, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
            return await work(db);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Platform job failed: {Job}", job);
            return false;
        }
    }
}
