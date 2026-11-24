using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>
/// Runs queued checks and imports (0.26.0), one at a time on this server, through <see cref="ImportRun"/>: as the person who started
/// it, with their sites, in their language, with progress and a heartbeat, and the result kept on the job.
/// </summary>
public sealed class ImportWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<ImportWorker> log) : BackgroundService
{
    private readonly SemaphoreSlim _one = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Import:RunInBackground", true)) return;
        var poll = TimeSpan.FromMilliseconds(Math.Max(50, config.GetValue("Import:PollMilliseconds", 2000)));
        var trimmed = DateTimeOffset.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                // Once an hour, the files of imports finished more than 30 days ago go (their history lines stay).
                if (DateTimeOffset.UtcNow - trimmed > TimeSpan.FromHours(1))
                {
                    trimmed = DateTimeOffset.UtcNow;
                    using var scope = scopes.CreateScope();
                    await using var root = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(null));
                    await Housekeeping.TrimImportFilesAsync(root, scope.ServiceProvider.GetRequiredService<IFileStorage>(), trimmed, stop);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Removing old import files failed"); }
            try { while (await RunNextAsync(stop)) { } }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Import worker run failed"); }
            try { await Task.Delay(poll, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Claims the oldest queued job and runs it; false when there was none.</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
            ImportJob job;
            await using (var root = new CmmsDbContext(options, new FixedTenantContext(null)))
            {
                await ImportJobs.FailStaleAsync(root, DateTimeOffset.UtcNow, ct);
                if (await ImportJobs.ClaimNextAsync(root, DateTimeOffset.UtcNow, ct) is not { } id) return false;
                job = await root.ImportJobs.IgnoreQueryFilters().AsNoTracking().FirstAsync(j => j.Id == id, ct);
            }
            await RunAsync(scope.ServiceProvider, options, job, ct);
            return true;
        }
        finally { _one.Release(); }
    }

    private async Task RunAsync(IServiceProvider services, DbContextOptions<CmmsDbContext> options, ImportJob job, CancellationToken ct)
    {
        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var storage = services.GetRequiredService<IFileStorage>();
        var access = job.AccessJson is null ? null : JsonSerializer.Deserialize<ImportJobs.Access>(job.AccessJson);
        var ctx = new FixedTenantContext(job.TenantId, job.UserId, job.UserName, "Import", access?.SiteIds, access?.BranchPaths);
        using var beating = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = BeatAsync(options, job.Id, beating.Token);
        string? resultJson = null, error = null;
        ImportResult? finished = null;
        try
        {
            using (Translator.Use(job.Language))
            {
                await using var stream = await storage.OpenReadAsync(job.StorageKey ?? "", ct) ?? throw new DomainException(ImportJobs.FileGone);
                var book = SpreadsheetReader.Read(stream, job.FileName);
                await using var db = new CmmsDbContext(options, ctx);
                var last = DateTimeOffset.MinValue;
                var result = await ImportRun.RunAsync(db, ctx, book, job.Save, TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Import:MaxMinutes", 30))), null, ct,
                    async (sheet, done, total) =>
                    {
                        if (DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(2) && done != total) return;
                        last = DateTimeOffset.UtcNow;
                        await using var p = new CmmsDbContext(options, new FixedTenantContext(null));
                        await p.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.Sheet, sheet).SetProperty(j => j.RowsDone, done).SetProperty(j => j.RowsTotal, total)
                            .SetProperty(j => j.HeartbeatAt, DateTimeOffset.UtcNow), CancellationToken.None);
                    });
                if (result.Stopped is not null) error = result.Stopped;
                else { resultJson = JsonSerializer.Serialize(result, json); finished = result; }
            }
        }
        catch (DomainException e) { error = e.Message; }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(e, "Import job {Job} failed", job.Id);
            error = ImportJobs.Broke;
        }
        finally
        {
            await beating.CancelAsync();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
        var now = DateTimeOffset.UtcNow;
        await using (var done = new CmmsDbContext(options, new FixedTenantContext(null)))
            await done.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, error is null ? ImportJobStatus.Done : ImportJobStatus.Failed).SetProperty(j => j.ResultJson, resultJson)
                .SetProperty(j => j.Error, error).SetProperty(j => j.FinishedAt, now), CancellationToken.None);
        // A long one (more than a minute since the upload): the person who started it gets an e-mail; service accounts never do.
        if (now - job.CreatedAt > TimeSpan.FromMinutes(1))
        {
            try
            {
                using (Translator.Use(job.Language))
                {
                    await using var mailDb = new CmmsDbContext(options, ctx);
                    await services.GetRequiredService<EmailOutbox>().ImportFinishedAsync(mailDb, ctx, job, finished, error, CancellationToken.None);
                    await mailDb.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception e) { log.LogWarning(e, "Couldn't e-mail the result of import {Job}", job.Id); }
        }
    }

    /// <summary>Tells other servers this job is still alive, every 15 seconds, even while one long save runs.</summary>
    private static async Task BeatAsync(DbContextOptions<CmmsDbContext> options, Guid jobId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
            await db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.HeartbeatAt, DateTimeOffset.UtcNow), ct);
        }
    }
}
