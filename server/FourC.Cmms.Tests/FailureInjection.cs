using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>
/// Makes one database write fail on purpose (0.29.1): a CHECK constraint that refuses only rows matching a condition no other
/// test uses, dropped again on dispose. For tests that a save failing part-way changes nothing.
/// </summary>
public sealed class FailureInjection : IAsyncDisposable
{
    private readonly ApiFixture _fx;
    private readonly string _table;
    private readonly string _name = "CK_zz_" + Guid.NewGuid().ToString("N")[..12];

    private FailureInjection(ApiFixture fx, string table) { _fx = fx; _table = table; }

    /// <summary>
    /// From now on a row of <paramref name="table"/> that is inserted or changed so that <paramref name="allowed"/> is false is refused.
    /// Existing rows are not checked.
    /// </summary>
    public static async Task<FailureInjection> RefuseAsync(ApiFixture fx, string table, string allowed)
    {
        var f = new FailureInjection(fx, table);
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] WITH NOCHECK ADD CONSTRAINT [{f._name}] CHECK ({allowed})");
        return f;
    }

    public async ValueTask DisposeAsync()
    {
        using var scope = _fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{_table}] DROP CONSTRAINT [{_name}]");
    }
}
