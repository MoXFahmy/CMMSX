using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>Database options CMX relies on, checked when the app starts.</summary>
public static class DatabaseSettings
{
    /// <summary>
    /// Row-versioned reads (READ_COMMITTED_SNAPSHOT): someone reading never waits for someone else's unsaved work (an
    /// import check holds the rows it touches until it is undone). Databases EF creates have it on already; one that
    /// doesn't (created another way, or switched off) gets it here. Switching takes a moment alone with the database, so
    /// other open connections are ended; it happens only when the option is off. Returns true when it switched it on.
    /// </summary>
    public static async Task<bool> EnsureReadCommittedSnapshotAsync(DbContext db, CancellationToken ct = default)
    {
        var on = await db.Database.SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS Value FROM sys.databases WHERE name = DB_NAME()").FirstAsync(ct);
        if (on) return false;
        await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", ct);
        return true;
    }
}
