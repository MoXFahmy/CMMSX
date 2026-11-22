using System.Net.Http.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmRecipientsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Pm_recipients_reach_generated_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await UserAsync(fx, org.Admin, "Viewer", name: "Template person");
        var b = await UserAsync(fx, org.Admin, "Viewer", name: "PM person");
        var gone = await UserAsync(fx, org.Admin, "Viewer", name: "Gone");
        var asset = await AssetAsync(org.Admin, org.Site, "Lift");
        var template = await TemplateAsync(org.Admin, "L", null, new() { ["recipientIds"] = new[] { a.Id, b.Id } });
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { b.Id, gone.Id } })));
        await DbAsync(fx, async db =>
        {
            var u = await db.Users.FindAsync(gone.Id);
            u!.IsActive = false;
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Contains(gone.Id, (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("recipientIds").EnumerateArray().Select(x => x.GetGuid()));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var people = (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("recipients").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), people.OrderBy(x => x));
    }

    [SkippableFact]
    public async Task Pm_recipients_get_the_generated_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var watcher = await UserAsync(fx, org.Admin, "Viewer", name: "Watcher");
        var asset = await AssetAsync(org.Admin, org.Site, "Boiler");
        var template = await TemplateAsync(org.Admin, "B");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { watcher.Id } })));
        await GenerateAsync(org.Admin, Today, pm);
        Assert.Contains(await RowsAsync(fx, watcher.Email), e => e.EventKey == "pm.assigned");
    }

    [SkippableFact]
    public async Task Work_order_recipients_get_completed_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var watcher = await UserAsync(fx, org.Admin, "Viewer", name: "Watcher");
        var template = await TemplateAsync(org.Admin, "C", null, new() { ["recipientIds"] = new[] { watcher.Id } });
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix", siteId = org.Site, locationId = org.Location, priority = "Medium", type = "Corrective", templateId = template,
        })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" }));
        Assert.Contains(await RowsAsync(fx, watcher.Email), e => e.EventKey == "workorder.completed");
    }

    [SkippableFact]
    public async Task Recipients_without_access_get_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var outsider = await UserAsync(fx, org.Admin, "Viewer", siteIds: [other], name: "Outsider");
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var template = await TemplateAsync(org.Admin, "D");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { outsider.Id } })));
        await GenerateAsync(org.Admin, Today, pm);
        Assert.Empty(await RowsAsync(fx, outsider.Email));
    }
}
