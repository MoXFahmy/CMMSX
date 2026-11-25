using FourC.Cmms.Api.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The database size check (0.29.0). The weekly-due rule is covered by PendingSignUpTests.Weekly_export_is_due_on_Sunday_from_two_UTC_once.</summary>
public class PlatformJobTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Size_is_measured_and_logged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var log = new ListLogger();

        var mb = await DbAsync(fx, db => DatabaseSize.MeasureAsync(db, log, CancellationToken.None));

        Assert.True(mb > 0);
        Assert.Contains($"DatabaseSize mb={mb}", log.Text);
    }

    [SkippableFact]
    public async Task Daily_job_logs_the_size()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var log = new ListLogger();
        fx.Factory!.Services.GetRequiredService<ILoggerFactory>().AddProvider(new ListLoggerProvider(log));

        await fx.Factory.Services.GetRequiredService<PlatformJobs>().RunDailyAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Contains("DatabaseSize mb=", log.Text);
    }
}
