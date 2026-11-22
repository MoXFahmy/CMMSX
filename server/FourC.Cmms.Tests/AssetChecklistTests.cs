using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class AssetChecklistTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<List<(Guid? Asset, string Step)>> StepsAsync(HttpClient c, Guid wo) =>
        (await Ok(await c.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks").EnumerateArray()
            .Select(t => (t.GetProperty("assetId").ValueKind == System.Text.Json.JsonValueKind.Null ? (Guid?)null : t.GetProperty("assetId").GetGuid(), t.GetProperty("description").GetString()!)).ToList();

    private static async Task<Guid> WoAsync(HttpClient c, Loop.Org org, Guid asset, Guid? template = null, string[]? steps = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix", siteId = org.Site, assetId = asset, priority = "Medium", type = "Corrective", templateId = template, tasks = steps,
        })));

    private static async Task AttachAsync(HttpClient c, Guid template, Guid? asset = null, Guid? category = null) =>
        await Ok(await c.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, assetCategoryId = category, pmTaskId = template }));

    [SkippableFact]
    public async Task Asset_checklist_is_added_to_new_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var safety = await TemplateAsync(org.Admin, "SAFE", ["Lock out"]);
        await AttachAsync(org.Admin, safety, asset: asset);
        Assert.Equal("Template SAFE", (await Ok(await org.Admin.GetAsync($"/api/asset-checklists?assetId={asset}"))).EnumerateArray().Single().GetProperty("name").GetString());
        var wo = await WoAsync(org.Admin, org, asset, steps: ["Fix the guard"]);
        Assert.Equal([(null, "Fix the guard"), (null, "Lock out")], await StepsAsync(org.Admin, wo));
    }

    [SkippableFact]
    public async Task Category_checklist_is_added()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var lifts = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "LIFT", name = "Lifts", isActive = true })));
        var asset = await AssetAsync(org.Admin, org.Site, "Lift 1", category: lifts);
        await AttachAsync(org.Admin, await TemplateAsync(org.Admin, "LS", ["Test the brake"]), category: lifts);
        Assert.Contains((null, "Test the brake"), await StepsAsync(org.Admin, await WoAsync(org.Admin, org, asset)));
    }

    [SkippableFact]
    public async Task Own_template_is_not_added_twice()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var t = await TemplateAsync(org.Admin, "P", ["Inspect"]);
        await AttachAsync(org.Admin, t, asset: asset);
        Assert.Equal([(null, "Inspect")], await StepsAsync(org.Admin, await WoAsync(org.Admin, org, asset, template: t)));
    }

    [SkippableFact]
    public async Task Round_gets_checklists_per_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        await AttachAsync(org.Admin, await TemplateAsync(org.Admin, "BX", ["Check the belt"]), asset: b);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "R", ["Routine"]), Today, assets: [a, b])));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        Assert.Equal([(a, "Routine"), (b, "Routine"), (b, "Check the belt")], await StepsAsync(org.Admin, wo));
    }

    [SkippableFact]
    public async Task Phone_and_request_work_get_them_too()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var asset = await AssetAsync(org.Admin, org.Site, "Boiler");
        await AttachAsync(org.Admin, await TemplateAsync(org.Admin, "B", ["Check the flue"]), asset: asset);
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = org.Site, assetId = asset, priority = "Low", type = "Corrective" })));
        var fromRequest = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        Assert.Contains((null, "Check the flue"), await StepsAsync(org.Admin, fromRequest));
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(org.Admin, "workorder.create", new { title = "Leak on boiler", assetId = asset, priority = "High" })));
        var fromPhone = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Title == "Leak on boiler").Select(w => w.Id).SingleAsync());
        Assert.Contains((null, "Check the flue"), await StepsAsync(org.Admin, fromPhone));
    }

    [SkippableFact]
    public async Task Asset_checklists_need_asset_edit()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var t = await TemplateAsync(org.Admin, "S", ["Lock out"]);
        var viewer = await UserAsync(fx, org.Admin, "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, pmTaskId = t })).StatusCode);
        await AttachAsync(org.Admin, t, asset: asset);
        Assert.Contains("This template is already on it.", await Bad(await org.Admin.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, pmTaskId = t })));
        Assert.Contains("assetId", await Bad(await org.Admin.PostAsJsonAsync("/api/asset-checklists", new { pmTaskId = t })));
    }

    [SkippableFact]
    public async Task Templates_on_assets_or_categories_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var checklist = await TemplateAsync(org.Admin, "CL1", ["Lock out"]);
        await AttachAsync(org.Admin, checklist, asset: asset);
        Assert.Contains("This template is a checklist on assets or categories. Remove it there or mark it inactive instead.",
            await Bad(await org.Admin.DeleteAsync($"/api/pm-tasks/{checklist}")));
        var a = await AssetAsync(org.Admin, org.Site, "A");
        var b = await AssetAsync(org.Admin, org.Site, "B");
        var own = await TemplateAsync(org.Admin, "OWN", ["Own step"]);
        await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "PMT"), Today, assets: [a, b],
            extra: new() { ["assetTemplates"] = new[] { new { assetId = a, pmTaskId = own } } }));
        Assert.Contains("This task is used by PM schedules. Mark it inactive instead.", await Bad(await org.Admin.DeleteAsync($"/api/pm-tasks/{own}")));
    }
}
