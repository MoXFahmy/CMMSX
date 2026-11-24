using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Imports;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>Background imports (0.26.0): queuing an upload, claiming the next job, progress and jobs left behind by a restart.</summary>
public static class ImportJobs
{
    public const long MaxBytes = 25 * 1024 * 1024;
    public const string ChooseFile = "Choose a file of up to 25 MB.";
    public const string Restarted = "The server restarted during this import. Nothing was saved. Try again.";
    public const string Broke = "Something went wrong during this import. Nothing was saved.";
    public const string FileGone = "This file is no longer kept.";
    /// <summary>A running job whose heartbeat is older than this was left behind by a server that stopped.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    public sealed record Access(IReadOnlyList<Guid>? SiteIds, IReadOnlyList<string>? BranchPaths);

    /// <summary>Checks the upload (size, readable, row limits), keeps the file and queues the job; the job's id.</summary>
    public static async Task<Guid> QueueAsync(HttpRequest req, CmmsDbContext db, ITenantContext ctx, IFileStorage storage, bool save, CancellationToken ct)
    {
        if (!req.HasFormContentType) throw new DomainException(ChooseFile);
        var form = await req.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0 || file.Length > MaxBytes) throw new DomainException(ChooseFile);
        byte[] bytes;
        await using (var s = file.OpenReadStream()) { using var m = new MemoryStream(); await s.CopyToAsync(m, ct); bytes = m.ToArray(); }
        var book = SpreadsheetReader.Read(new MemoryStream(bytes), file.FileName);
        var rows = ImportRun.CheckLimits(book);
        var name = Path.GetFileName(file.FileName) is { Length: > 0 } n ? n[..Math.Min(n.Length, 200)] : "import.xlsx";
        var job = new ImportJob
        {
            Id = Guid.CreateVersion7(), FileName = name, Save = save, Status = ImportJobStatus.Queued, UserId = ctx.UserId, UserName = ctx.UserName,
            Language = Translator.CurrentLanguage, AccessJson = JsonSerializer.Serialize(new Access(ctx.SiteIds, ctx.BranchPaths)), CreatedAt = DateTimeOffset.UtcNow,
            RowsTotal = rows,
        };
        job.StorageKey = $"{db.CurrentTenantId}/imports/{job.Id}/{SafeName(name)}";
        await using (var m = new MemoryStream(bytes)) await storage.SaveAsync(job.StorageKey, m, ct);
        db.ImportJobs.Add(job);
        await Housekeeping.SaveOrRemoveFileAsync(storage, job.StorageKey, () => db.SaveChangesAsync(ct), ct);
        return job.Id;
    }

    private static string SafeName(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));

    /// <summary>Takes the oldest queued job (any organization) for this server: exactly one server gets each job.</summary>
    public static async Task<Guid?> ClaimNextAsync(CmmsDbContext root, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await root.Database.SqlQuery<Guid>($"""
            WITH next AS (SELECT TOP (1) * FROM [ImportJobs] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [Status] = N'Queued' ORDER BY [CreatedAt])
            UPDATE next SET [Status] = N'Running', [StartedAt] = {now}, [HeartbeatAt] = {now} OUTPUT inserted.[Id] AS [Value]
            """).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>Running jobs nobody has heard from for <see cref="Stale"/>: their server stopped, so nothing was saved.</summary>
    public static Task<int> FailStaleAsync(CmmsDbContext root, DateTimeOffset now, CancellationToken ct)
    {
        var before = now - Stale;
        return root.ImportJobs.IgnoreQueryFilters()
            .Where(j => j.Status == ImportJobStatus.Running && (j.HeartbeatAt == null || j.HeartbeatAt < before))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportJobStatus.Failed).SetProperty(j => j.Error, Restarted).SetProperty(j => j.FinishedAt, now), ct);
    }

    /// <summary>How many jobs (any organization) run before this queued one.</summary>
    public static Task<int> WaitingBehindAsync(CmmsDbContext db, ImportJob job, CancellationToken ct) =>
        job.Status != ImportJobStatus.Queued ? Task.FromResult(0)
        : db.ImportJobs.IgnoreQueryFilters().CountAsync(j => j.Id != job.Id && (j.Status == ImportJobStatus.Running || (j.Status == ImportJobStatus.Queued && j.CreatedAt < job.CreatedAt)), ct);

    /// <summary>A job as the import page and the API show it; the result only when asked for one job.</summary>
    public static object Shape(ImportJob j, int waitingBehind, bool withResult) => new
    {
        j.Id, j.Save, Status = j.Status.ToString(), j.FileName, j.UserName, j.CreatedAt, j.StartedAt, j.FinishedAt, j.Sheet, j.RowsDone, j.RowsTotal,
        WaitingBehind = waitingBehind, Error = j.Error is null ? null : Translator.T(j.Error),
        Result = withResult && j.ResultJson is not null ? JsonDocument.Parse(j.ResultJson).RootElement.Clone() : (JsonElement?)null,
    };
}
