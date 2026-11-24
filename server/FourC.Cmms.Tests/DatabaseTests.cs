using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class DatabaseTests(ManualImportFixture fx) : IClassFixture<ManualImportFixture>
{
    private string ConnectionString()
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        return db.Database.GetConnectionString()!;
    }

    [SkippableFact]
    public async Task Startup_turns_row_versioned_reads_on_when_they_are_off()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await NewOrganizationAsync(fx);
        var cs = ConnectionString();
        await using (var off = new SqlConnection(cs))
        {
            await off.OpenAsync();
            await using var cmd = new SqlCommand("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE", off);
            await cmd.ExecuteNonQueryAsync();
        }
        SqlConnection.ClearAllPools();

        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        Assert.True(await DatabaseSettings.EnsureReadCommittedSnapshotAsync(db));
        Assert.False(await DatabaseSettings.EnsureReadCommittedSnapshotAsync(db)); // already on: nothing to do
        SqlConnection.ClearAllPools();

        await using var check = new SqlConnection(cs);
        await check.OpenAsync();
        await using var setting = new SqlCommand("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()", check);
        Assert.Equal(true, await setting.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Reads_never_wait_for_unsaved_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await NewOrganizationAsync(fx); // the app has started and brought the database up to date
        var cs = ConnectionString();

        // Unsaved work holding rows, as an import check does while it runs.
        await using var writer = new SqlConnection(cs);
        await writer.OpenAsync();
        await using var tx = (SqlTransaction)await writer.BeginTransactionAsync();
        await using (var change = new SqlCommand("UPDATE Tenants SET Name = Name + N'!'", writer, tx))
            await change.ExecuteNonQueryAsync();

        // Someone else reading the same rows gets the last saved version at once, instead of waiting.
        await using var reader = new SqlConnection(cs);
        await reader.OpenAsync();
        await using var read = new SqlCommand("SELECT COUNT(*) FROM Tenants WHERE Name NOT LIKE N'%!'", reader) { CommandTimeout = 3 };
        Assert.True((int)(await read.ExecuteScalarAsync())! > 0);
        await tx.RollbackAsync();

        await using var setting = new SqlCommand("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()", reader);
        Assert.Equal(true, await setting.ExecuteScalarAsync());
    }
}
