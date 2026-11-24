using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Small clean-ups: old records nobody needs, and files left behind by a save that failed.</summary>
public static class Housekeeping
{
    /// <summary>
    /// How long a phone change's record is kept: it only stops a change sent twice from counting twice, and phones send
    /// within days (changes older than 30 days are refused anyway).
    /// </summary>
    public static readonly TimeSpan KeepTechActions = TimeSpan.FromDays(90);

    /// <summary>Deletes records of phone changes older than <see cref="KeepTechActions"/>, for every organization.</summary>
    public static Task<int> TrimAsync(CmmsDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var before = now - KeepTechActions;
        return db.TechActions.IgnoreQueryFilters().Where(a => a.ReceivedAt < before).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Deletes activity log entries older than each organization's chosen retention (0.25.0); organizations that keep
    /// everything (no retention set) are left alone.
    /// </summary>
    public static async Task<int> TrimActivityAsync(CmmsDbContext db, DateTimeOffset now, CancellationToken ct = default, int batchSize = 2000)
    {
        var keeps = (await db.Tenants.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct))
            .Where(t => t.Settings.Security?.ActivityRetentionMonths is > 0)
            .Select(t => (t.Id, Months: t.Settings.Security!.ActivityRetentionMonths!.Value)).ToList();
        var deleted = 0;
        foreach (var (tenantId, months) in keeps)
        {
            var before = now.AddMonths(-months);
            // In small batches, so a first run over years of changes never locks the table every save writes to.
            while (true)
            {
                var n = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE TOP ({batchSize}) FROM [AuditEntries] WHERE [TenantId] = {tenantId} AND [Timestamp] < {before}", ct);
                deleted += n;
                if (n < batchSize) break;
            }
        }
        return deleted;
    }

    /// <summary>Saves the record of a file already stored; if that fails, removes the file so it isn't left behind, and rethrows.</summary>
    public static Task SaveOrRemoveFileAsync(IFileStorage storage, string key, Func<Task<int>> save, CancellationToken ct = default) =>
        SaveOrRemoveFilesAsync(storage, [key], save, ct);

    /// <summary>As <see cref="SaveOrRemoveFileAsync"/> for several stored files.</summary>
    public static async Task SaveOrRemoveFilesAsync(IFileStorage storage, IReadOnlyCollection<string> keys, Func<Task<int>> save, CancellationToken ct = default)
    {
        try { await save(); }
        catch
        {
            foreach (var key in keys)
                try { await storage.DeleteAsync(key, ct); } catch { /* the save's error matters more */ }
            throw;
        }
    }
}
