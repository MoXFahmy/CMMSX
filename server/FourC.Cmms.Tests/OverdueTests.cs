using System.Net.Http.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class OverdueTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private OverdueNotifier Notifier => fx.Factory!.Services.GetRequiredService<OverdueNotifier>();

    private static async Task<Guid> LateWorkAsync(HttpClient c, Loop.Org org, Guid assignee, TimeSpan ago) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Late job", siteId = org.Site, locationId = org.Location, priority = "High", type = "Corrective", assigneeIds = new[] { assignee }, dueAt = DateTimeOffset.UtcNow - ago,
        })));

    private Task<DateTimeOffset?> NotifiedForAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == wo).Select(w => w.OverdueNotifiedFor).FirstAsync());

    [SkippableFact]
    public async Task Recently_overdue_work_is_mailed_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var manager = await UserAsync(fx, org.Admin, "Maintenance Manager");
        var tech = await EmployeeAsync(org.Admin, "T1");
        await LateWorkAsync(manager.Client, org, tech.Id, TimeSpan.FromHours(2));
        Assert.True(await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant) >= 2);
        Assert.Single(await RowsAsync(fx, tech.Email), r => r.EventKey == EmailEvents.WorkOrderOverdue);
        Assert.Single(await RowsAsync(fx, manager.Email), r => r.EventKey == EmailEvents.WorkOrderOverdue);
        Assert.Equal(0, await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant));
        Assert.Single(await RowsAsync(fx, tech.Email), r => r.EventKey == EmailEvents.WorkOrderOverdue);
    }

    [SkippableFact]
    public async Task Old_overdue_work_is_marked_not_sent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var wo = await LateWorkAsync(org.Admin, org, tech.Id, TimeSpan.FromDays(3));
        Assert.Equal(0, await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant));
        Assert.DoesNotContain(await RowsAsync(fx, tech.Email), r => r.EventKey == EmailEvents.WorkOrderOverdue);
        Assert.NotNull(await NotifiedForAsync(wo));
    }

    [SkippableFact]
    public async Task A_new_due_time_can_notify_again()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var wo = await LateWorkAsync(org.Admin, org, tech.Id, TimeSpan.FromHours(3));
        await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant);
        await DbAsync(fx, async db =>
        {
            var w = await db.WorkOrders.IgnoreQueryFilters().FirstAsync(x => x.Id == wo);
            w.DueAt = DateTimeOffset.UtcNow.AddHours(-1);
            return await db.SaveChangesAsync();
        });
        await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant);
        Assert.Equal(2, (await RowsAsync(fx, tech.Email)).Count(r => r.EventKey == EmailEvents.WorkOrderOverdue));
    }

    [SkippableFact]
    public async Task Nothing_is_mailed_or_marked_while_email_is_off()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var wo = await LateWorkAsync(org.Admin, org, tech.Id, TimeSpan.FromHours(2));
        Assert.Equal(0, await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant));
        Assert.Empty(await RowsAsync(fx, tech.Email));
        Assert.Null(await NotifiedForAsync(wo));
    }
}
