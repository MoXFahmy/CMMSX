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

    /// <summary>Saves a user's access over (the PUT needs the whole person back).</summary>
    private static async Task LimitAsync(HttpClient admin, Guid user, string email, string role, string name, Guid[]? siteIds = null, Guid[]? locationIds = null)
    {
        var roleId = (await Ok(await admin.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        await Ok(await admin.PutAsJsonAsync($"/api/users/{user}", new { fullName = name, email, isActive = true, roleIds = new[] { roleId }, siteIds, locationIds }));
    }

    private static async Task<Guid> LocationAsync(HttpClient admin, Guid site, string code) =>
        Id(await Ok(await admin.PostAsJsonAsync("/api/locations", new { siteId = site, code, name = code, type = "Building", criticality = "Medium", isActive = true })));

    [SkippableFact]
    public async Task Pm_recipient_outside_site_access_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var outsider = await UserAsync(fx, org.Admin, "Viewer", siteIds: [other], name: "Outsider");
        var insider = await UserAsync(fx, org.Admin, "Viewer", siteIds: [org.Site], name: "Insider");
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var template = await TemplateAsync(org.Admin, "D");

        var refused = await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules", Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { outsider.Id, insider.Id } })));
        Assert.Contains("Outsider", refused);
        Assert.DoesNotContain("Insider", refused);
        // Someone who sees the site is fine, and the same rule holds when a saved PM gains the outsider.
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { insider.Id } })));
        var edit = Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { insider.Id, outsider.Id } });
        Assert.Contains("Outsider", await Bad(await org.Admin.PutAsJsonAsync($"/api/pm-schedules/{pm}", edit)));
        Assert.Equal([insider.Id], (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("recipientIds").EnumerateArray().Select(x => x.GetGuid()).ToList());
    }

    [SkippableFact]
    public async Task Pm_recipient_limited_to_another_branch_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var elsewhere = await LocationAsync(org.Admin, org.Site, "B2");
        var neighbour = await UserAsync(fx, org.Admin, "Viewer", name: "Neighbour");
        var local = await UserAsync(fx, org.Admin, "Viewer", name: "Local");
        await LimitAsync(org.Admin, neighbour.Id, neighbour.Email, "Viewer", "Neighbour", locationIds: [elsewhere]);
        await LimitAsync(org.Admin, local.Id, local.Email, "Viewer", "Local", locationIds: [org.Location]);
        var asset = await AssetAsync(org.Admin, org.Site, "Pump", org.Location);
        var template = await TemplateAsync(org.Admin, "D");

        // The asset stands in the first building: someone limited to the second would never get its mail.
        Assert.Contains("Neighbour", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules",
            Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { neighbour.Id } }))));
        await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { local.Id } }));
    }

    [SkippableFact]
    public async Task Mail_still_drops_recipients_who_lost_access_after_saving()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var person = await UserAsync(fx, org.Admin, "Viewer", name: "Outsider");
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var template = await TemplateAsync(org.Admin, "D");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, template, Today, asset, extra: new() { ["recipientIds"] = new[] { person.Id } })));
        // Moved to another site afterwards: the second line of defence is the mail itself.
        await LimitAsync(org.Admin, person.Id, person.Email, "Viewer", "Outsider", siteIds: [other]);
        await GenerateAsync(org.Admin, Today, pm);
        Assert.Empty(await RowsAsync(fx, person.Email));
    }
}
