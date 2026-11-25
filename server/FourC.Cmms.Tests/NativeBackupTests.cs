using System.Collections.Concurrent;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FourC.Cmms.Tests;

/// <summary>The weekly native export (0.29.0): the object key, and doing nothing without a bucket.</summary>
public class NativeBackupTests
{
    [Fact]
    public void ObjectArn_builds_the_key()
    {
        Assert.Equal("arn:aws:s3:::b/native/Cmms-2026-11-29.bak", NativeBackup.ObjectArn("arn:aws:s3:::b", "Cmms", new DateOnly(2026, 11, 29)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Export_is_skipped_without_a_bucket(string? arn)
    {
        // A context whose server can't be reached: any SQL sent would throw (and be logged), so a clean true with no lines proves none was sent.
        var options = new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=x;Connect Timeout=1").Options;
        await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backups:S3Arn"] = arn }).Build();
        var log = new ListLogger();

        var done = await NativeBackup.RunAsync(db, config, log, new DateOnly(2026, 11, 29), CancellationToken.None);

        Assert.True(done);
        Assert.Empty(log.Lines);
    }

    private const string Unreachable = "Server=127.0.0.1,1;Database=x;Connect Timeout=1";

    private static IConfiguration Bucket() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backups:S3Arn"] = "arn:aws:s3:::b" }).Build();

    [Fact]
    public async Task A_failed_export_returns_false_and_logs_an_error_without_the_server()
    {
        var options = new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer(Unreachable).Options;
        await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        var log = new ListLogger();

        var done = await NativeBackup.RunAsync(db, Bucket(), log, new DateOnly(2026, 11, 29), CancellationToken.None);

        Assert.False(done);
        Assert.Contains("BackupExport status=ERROR", log.Text);
        Assert.DoesNotContain("127.0.0.1", log.Text);
    }

    [Fact]
    public async Task A_failed_weekly_run_is_not_remembered_so_it_is_tried_again()
    {
        var options = new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer(Unreachable).Options;
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        var log = new ListLogger();
        var jobs = new PlatformJobs(services.GetRequiredService<IServiceScopeFactory>(), Bucket(), new ListLoggerOf<PlatformJobs>(log));
        var sunday = new DateTimeOffset(2026, 11, 29, 2, 30, 0, TimeSpan.Zero);

        await jobs.RunWeeklyAsync(sunday, CancellationToken.None);
        await jobs.RunWeeklyAsync(sunday.AddHours(1), CancellationToken.None);

        Assert.Equal(2, log.Lines.Count(l => l.Contains("BackupExport status=ERROR")));
        Assert.DoesNotContain("127.0.0.1", log.Text);
    }
}

/// <summary>A logger that keeps what it is given, for the tests that look at log lines.</summary>
internal sealed class ListLogger : ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();
    public IReadOnlyCollection<string> Lines => _lines;
    public string Text => string.Join('\n', _lines);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");
}

/// <summary>The typed face of a <see cref="ListLogger"/>.</summary>
internal sealed class ListLoggerOf<T>(ListLogger inner) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => inner.Log(logLevel, eventId, state, exception, formatter);
}

/// <summary>Hands every logger it creates the same <see cref="ListLogger"/>, to catch what the running app logs.</summary>
internal sealed class ListLoggerProvider(ListLogger logger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => logger;
    public void Dispose() { }
}
