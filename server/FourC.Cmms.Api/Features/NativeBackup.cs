using System.ComponentModel.DataAnnotations.Schema;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// The weekly native export (0.29.0): RDS writes a full <c>.bak</c> of the database into the backup bucket, over the daily snapshots, so a copy
/// can be restored on any SQL Server. Only runs where <c>Backups:S3Arn</c> names the bucket (AWS); Azure and local runs leave it empty.
/// </summary>
public static class NativeBackup
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(3);

    /// <summary>The bucket's object for one day's export of one database.</summary>
    public static string ObjectArn(string bucketArn, string database, DateOnly date) => $"{bucketArn}/native/{database}-{date:yyyy-MM-dd}.bak";

    /// <summary>
    /// Starts the export and waits for RDS to finish it. True when there was nothing to do or it succeeded; false when it failed or ran past the
    /// cap (logged as <c>BackupExport status=ERROR …</c>), so the caller tries again. Nothing is sent, or logged, when no bucket is set.
    /// </summary>
    public static async Task<bool> RunAsync(CmmsDbContext db, IConfiguration config, ILogger log, DateOnly date, CancellationToken ct, TimeSpan? pollEvery = null, TimeSpan? giveUpAfter = null)
    {
        var bucket = config["Backups:S3Arn"]?.Trim();
        if (string.IsNullOrEmpty(bucket)) return true;
        var file = "";
        try
        {
            var name = db.Database.GetDbConnection().Database;
            var arn = ObjectArn(bucket.TrimEnd('/'), name, date);
            file = $"native/{name}-{date:yyyy-MM-dd}.bak";
            var started = (await db.Database.SqlQuery<RdsTask>(
                $"exec msdb.dbo.rds_backup_database @source_db_name={name}, @s3_arn_to_backup_to={arn}, @overwrite_s3_backup_file=1, @type='FULL'").ToListAsync(ct)).FirstOrDefault();
            if (started == null) { log.LogError("BackupExport status=ERROR no task was started"); return false; }

            var every = pollEvery ?? PollEvery;
            var deadline = DateTime.UtcNow + (giveUpAfter ?? GiveUpAfter);
            while (true)
            {
                await Task.Delay(every, ct);
                var task = (await db.Database.SqlQuery<RdsTask>($"exec msdb.dbo.rds_task_status @task_id={started.TaskId}").ToListAsync(ct)).FirstOrDefault();
                switch (task?.Lifecycle)
                {
                    case "SUCCESS":
                        log.LogInformation("BackupExport status=SUCCESS file={File}", file);
                        return true;
                    case "ERROR" or "CANCELLED" or "CANCEL_REQUESTED":
                        log.LogError("BackupExport status=ERROR {Info}", task.TaskInfo ?? task.Lifecycle);
                        return false;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    log.LogError("BackupExport status=ERROR still {Lifecycle} after {Hours} hours", task?.Lifecycle ?? "unknown", (giveUpAfter ?? GiveUpAfter).TotalHours);
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The type only: a connection failure's message can carry the server's address.
            log.LogError("BackupExport status=ERROR the export failed with {Error}", ex.GetType().Name);
            return false;
        }
    }

    /// <summary>A row of RDS's task procedures.</summary>
    private sealed class RdsTask
    {
        [Column("task_id")] public int TaskId { get; set; }
        [Column("lifecycle")] public string? Lifecycle { get; set; }
        [Column("task_info")] public string? TaskInfo { get; set; }
    }
}
