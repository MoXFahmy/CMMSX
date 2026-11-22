using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class TemplateTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task UploadAsync(HttpClient c, string type, Guid id, string name)
    {
        using var form = new MultipartFormDataContent { { new StringContent(type), "entityType" }, { new StringContent(id.ToString()), "entityId" } };
        var file = new ByteArrayContent("manual"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", name);
        await Ok(await c.PostAsync("/api/attachments", form));
    }

    private static async Task<JsonElement> WoAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-orders/{id}"));

    [SkippableFact]
    public async Task Template_fields_save_and_show()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await TemplateAsync(org.Admin, "SVC", ["Isolate", "Inspect"], new()
        {
            ["title"] = "Chiller service", ["workType"] = "Inspection", ["priority"] = "High", ["recipientIds"] = new[] { org.AdminId }, ["expectedStopHours"] = 3.5m,
        });
        var t = await Ok(await org.Admin.GetAsync($"/api/pm-tasks/{id}"));
        Assert.Equal("Chiller service", t.GetProperty("title").GetString());
        Assert.Equal("Inspection", t.GetProperty("workType").GetString());
        Assert.Equal("High", t.GetProperty("priority").GetString());
        Assert.Equal(org.AdminId, t.GetProperty("recipientIds").EnumerateArray().Single().GetGuid());
        Assert.Equal(3.5m, t.GetProperty("expectedStopHours").GetDecimal());
    }

    [SkippableFact]
    public async Task Work_order_from_template_brings_parts_recipients_and_documents()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { defaultStoreId = store });
        var part = await PartAsync(org.Admin, store, "FLT-1");
        var template = await TemplateAsync(org.Admin, "FLT", ["Change filter"], new()
        {
            ["recipientIds"] = new[] { org.AdminId }, ["expectedStopHours"] = 1m, ["parts"] = new[] { new { partId = part, quantity = 2 } },
        });
        await UploadAsync(org.Admin, "PmTask", template, "manual.pdf");
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Filter", siteId = org.Site, locationId = org.Location, priority = "Medium", type = "Corrective", templateId = template,
        })));
        var w = await WoAsync(org.Admin, wo);
        Assert.Equal("Change filter", w.GetProperty("tasks").EnumerateArray().Single().GetProperty("description").GetString());
        var planned = w.GetProperty("plannedParts").EnumerateArray().Single();
        Assert.Equal((part, 2m), (planned.GetProperty("partId").GetGuid(), planned.GetProperty("quantity").GetDecimal()));
        Assert.Equal(org.AdminId, w.GetProperty("recipients").EnumerateArray().Single().GetProperty("id").GetGuid());
        var tpl = w.GetProperty("template");
        Assert.Equal(template, tpl.GetProperty("id").GetGuid());
        Assert.Equal(1m, tpl.GetProperty("expectedStopHours").GetDecimal());
        Assert.Equal("manual.pdf", tpl.GetProperty("documents").EnumerateArray().Single().GetProperty("fileName").GetString());
    }

    [SkippableFact]
    public async Task Pm_work_takes_the_template_title_type_and_priority()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Chiller");
        var full = await TemplateAsync(org.Admin, "A", null, new() { ["title"] = "Chiller inspection", ["workType"] = "Inspection", ["priority"] = "High" });
        var plain = await TemplateAsync(org.Admin, "B");
        var s1 = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, full, Today, asset, extra: new() { ["name"] = "PM one", ["priority"] = "Low" })));
        var s2 = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, plain, Today, asset, extra: new() { ["name"] = "PM two", ["priority"] = "Low" })));
        var results = (await GenerateAsync(org.Admin, Today, s1, s2)).EnumerateArray().ToList();
        var w1 = await WoAsync(org.Admin, results.Single(r => r.GetProperty("scheduleId").GetGuid() == s1).GetProperty("workOrderId").GetGuid());
        Assert.Equal(("Chiller inspection", "Inspection", "High"), (w1.GetProperty("title").GetString(), w1.GetProperty("type").GetString(), w1.GetProperty("priority").GetString()));
        var w2 = await WoAsync(org.Admin, results.Single(r => r.GetProperty("scheduleId").GetGuid() == s2).GetProperty("workOrderId").GetGuid());
        Assert.Equal(("PM two", "Preventive", "Low"), (w2.GetProperty("title").GetString(), w2.GetProperty("type").GetString(), w2.GetProperty("priority").GetString()));
    }

    [SkippableFact]
    public async Task Where_used_lists_pms_and_checklists()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Boiler");
        var t = await TemplateAsync(org.Admin, "W");
        var other = await TemplateAsync(org.Admin, "O");
        var routine = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today.AddDays(30), asset, extra: new() { ["code"] = "PM-R" })));
        await SaveScheduleAsync(org.Admin, Schedule(org.Site, other, Today.AddDays(30), asset, extra: new() { ["code"] = "PM-N", ["extraTasks"] = new[] { new { pmTaskId = t, everyNth = 4 } } }));
        await DbAsync(fx, async db =>
        {
            db.AssetChecklists.Add(new Core.Assets.AssetChecklist { TenantId = org.Tenant, AssetId = asset, PmTaskId = t });
            await db.SaveChangesAsync();
            return 0;
        });
        var used = (await Ok(await org.Admin.GetAsync($"/api/pm-tasks/{t}"))).GetProperty("usedBy");
        var schedules = used.GetProperty("schedules").EnumerateArray().ToList();
        Assert.Contains(schedules, s => s.GetProperty("id").GetGuid() == routine && s.GetProperty("role").GetString() == "routine");
        Assert.Contains(schedules, s => s.GetProperty("code").GetString() == "PM-N" && s.GetProperty("role").GetString() == "everyNth");
        Assert.Equal(asset, used.GetProperty("checklists").EnumerateArray().Single().GetProperty("assetId").GetGuid());
        Assert.Equal(0, used.GetProperty("openWorkOrders").GetInt32());
    }

    [SkippableFact]
    public async Task Own_template_copies_and_relinks()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var shared = await TemplateAsync(org.Admin, "SH", ["Old step"]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, shared, Today, asset, extra: new() { ["code"] = "PM-F" })));
        var copy = await Ok(await org.Admin.PostAsync($"/api/pm-schedules/{pm}/own-template", null));
        Assert.Equal("SH-PM-F", copy.GetProperty("code").GetString());
        var copyId = copy.GetProperty("pmTaskId").GetGuid();
        Assert.Equal(copyId, (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("pmTaskId").GetGuid());
        // Editing the shared template no longer reaches the PM.
        await Ok(await org.Admin.PutAsJsonAsync($"/api/pm-tasks/{shared}", new { code = "SH", name = "Shared", steps = new[] { "New step" }, isActive = true }));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        Assert.Equal("Old step", (await WoAsync(org.Admin, wo)).GetProperty("tasks").EnumerateArray().Single().GetProperty("description").GetString());
    }
}
