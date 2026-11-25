using System.Net.Http.Json;
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

/// <summary>
/// A transient failure (what the retry strategy retries) on the first command whose text contains a marker, once; for tests that a
/// transaction run again after a dropped connection still ends fully applied. Attach it to a second host with <see cref="HostAsync"/>.
/// </summary>
public sealed class FlakyOnce : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private string? _marker;
    public bool Fired { get; private set; }
    private int _skip;
    /// <summary>Fails the first command containing <paramref name="marker"/> after letting <paramref name="skip"/> such commands through.</summary>
    public void Arm(string marker, int skip = 0) { Fired = false; _marker = marker; _skip = skip; }

    private void Check(System.Data.Common.DbCommand command)
    {
        if (_marker is { } m && command.CommandText.Contains(m) && _skip-- <= 0) { _marker = null; Fired = true; throw new TimeoutException("Injected transient failure"); }
    }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
    { Check(command); return base.ReaderExecutingAsync(command, eventData, result, ct); }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
    { Check(command); return base.NonQueryExecutingAsync(command, eventData, result, ct); }

    /// <summary>A second host on the same database whose contexts carry <paramref name="flaky"/>, with a client signed in as the given user.</summary>
    public static async Task<HttpClient> HostAsync(ApiFixture fx, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor flaky, string email, string password)
    {
        var host = fx.Factory!.WithWebHostBuilder(b => b.ConfigureServices(s => s.ConfigureDbContext<CmmsDbContext>(o => o.AddInterceptors(flaky))));
        var c = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe = false });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return c;
    }
}

/// <summary>
/// Another person's save lands first (0.29.1): just before the first command containing a marker, runs <c>touch</c> (a change to the row the
/// command is about to update), so the command finds the row changed and the save fails the way a real race does. Once.
/// </summary>
public sealed class TouchOnce(Func<Task> touch) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private string? _marker;
    public bool Fired { get; private set; }
    public void Arm(string marker) { Fired = false; _marker = marker; }

    private async Task CheckAsync(System.Data.Common.DbCommand command)
    {
        if (_marker is { } m && command.CommandText.Contains(m)) { _marker = null; Fired = true; await touch(); }
    }

    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
    { await CheckAsync(command); return result; }

    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
    { await CheckAsync(command); return result; }
}

/// <summary>Keeps the text of every command that contains a marker (to check what the app really sends).</summary>
public sealed class CommandRecorder(string marker) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public System.Collections.Concurrent.ConcurrentBag<string> Seen { get; } = [];

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
    {
        if (command.CommandText.Contains(marker)) Seen.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}
