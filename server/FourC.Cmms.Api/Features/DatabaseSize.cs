using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>The size of the database's data files (0.29.0), logged once a day so an alarm can watch it against the Express edition's 10 GB cap.</summary>
public static class DatabaseSize
{
    /// <summary>Data files only (the log file isn't counted by the cap), in megabytes. Logs <c>DatabaseSize mb={mb}</c>; null, and no line, on any provider but SQL Server.</summary>
    public static async Task<long?> MeasureAsync(CmmsDbContext db, ILogger log, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return null;
        // Pages of 8 KB.
        var mb = (await db.Database.SqlQuery<long?>($"SELECT SUM(CAST(size AS bigint)) * 8 / 1024 AS Value FROM sys.database_files WHERE type = 0").ToListAsync(ct)).FirstOrDefault();
        if (mb == null) return null;
        log.LogInformation("DatabaseSize mb={Mb}", mb.Value);
        return mb;
    }
}
