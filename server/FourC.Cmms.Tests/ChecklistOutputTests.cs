using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class ChecklistOutputTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Wo, Guid Point, Guid Meter, List<WorkOrderTask> Steps);

    private async Task<Setup> SetupAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var meter = await MeterAsync(org.Admin, asset, "H1");
        var point = await PointAsync(org.Admin, "SAT");
        await PointAsync(org.Admin, "OTHER");
        var me = (await EmployeeAsync(org.Admin, $"ME{Guid.NewGuid():N}"[..8], userId: org.AdminId)).Id;
        var t = await TypedTemplateAsync(org.Admin, "T1",
        [
            new { description = "Lock out", type = "Check", beforeStart = true }, new { description = "Supply air", type = "Number", measurementPointId = point },
            new { description = "Hours", type = "Meter" }, new { description = "Belt", type = "Text", photoRequired = true },
        ]);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Medium", type = "Preventive", templateId = t, assigneeIds = new[] { me },
        })));
        var steps = await DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(x => x.WorkOrderId == wo).OrderBy(x => x.Sequence).ToListAsync());
        return new Setup(org, wo, point, meter, steps);
    }

    private static async Task PhotoAsync(HttpClient c, Guid wo, Guid step)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("WorkOrder"), "entityType" }, { new StringContent(wo.ToString()), "entityId" }, { new StringContent(step.ToString()), "stepId" },
        };
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 0xFF, 0xD9]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "files", "belt.jpg");
        await Ok(await c.PostAsync("/api/attachments", form));
    }

    [SkippableFact]
    public async Task Detail_lists_typed_steps_with_photos_and_follow_ups()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[0].Id}", new { answer = "Ok" }));
        await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[1].Id}", new { value = 33 }));
        await PhotoAsync(s.Org.Admin, s.Wo, s.Steps[3].Id);
        var detail = await Ok(await s.Org.Admin.GetAsync($"/api/work-orders/{s.Wo}"));
        var tasks = detail.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(["Check", "Number", "Meter", "Text"], tasks.Select(t => t.GetProperty("type").GetString()));
        Assert.Equal(("Ok", true), (tasks[0].GetProperty("answer").GetString(), tasks[0].GetProperty("beforeStart").GetBoolean()));
        Assert.Equal((s.Point, "°C", 33m, "AlarmHigh"), (tasks[1].GetProperty("measurementPointId").GetGuid(), tasks[1].GetProperty("unit").GetString(),
            tasks[1].GetProperty("value").GetDecimal(), tasks[1].GetProperty("level").GetString()));
        Assert.Equal(JsonValueKind.String, tasks[1].GetProperty("followUp").GetProperty("number").ValueKind);
        Assert.Equal(s.Meter, tasks[2].GetProperty("meterId").GetGuid());
        Assert.StartsWith("H1", tasks[2].GetProperty("meterName").GetString());
        Assert.Equal((true, 1), (tasks[3].GetProperty("photoRequired").GetBoolean(), tasks[3].GetProperty("photos").GetArrayLength()));
        var point = detail.GetProperty("points").EnumerateArray().Single();
        Assert.Equal(("SAT", 32m, 60m), (point.GetProperty("code").GetString(), point.GetProperty("alarmHigh").GetDecimal(), point.GetProperty("validMax").GetDecimal()));
    }

    [SkippableFact]
    public async Task Bundle_carries_typed_steps_and_their_points()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await PhotoAsync(s.Org.Admin, s.Wo, s.Steps[3].Id);
        var bundle = await Ok(await s.Org.Admin.GetAsync("/api/tech/bundle"));
        var job = bundle.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == s.Wo);
        var tasks = job.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(["Check", "Number", "Meter", "Text"], tasks.Select(t => t.GetProperty("type").GetString()));
        Assert.True(tasks[0].GetProperty("beforeStart").GetBoolean());
        Assert.Equal(s.Point, tasks[1].GetProperty("measurementPointId").GetGuid());
        Assert.Equal(s.Meter, tasks[2].GetProperty("meterId").GetGuid());
        Assert.Equal(s.Steps[3].Id, job.GetProperty("files").EnumerateArray().Single().GetProperty("workOrderTaskId").GetGuid());
        var points = bundle.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(["SAT"], points.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal((28m, 0m), (points[0].GetProperty("alertHigh").GetDecimal(), points[0].GetProperty("validMin").GetDecimal()));
        Assert.False(bundle.GetProperty("completionRules").GetProperty("readings").GetBoolean());
    }

    [SkippableFact]
    public async Task Settings_save_follow_up_default()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var template = await TemplateAsync(org.Admin, "DEF");
        await TestSettings.SetSettingsAsync(org.Admin, x => { x["followUpDefault"] = "WorkOrder"; x["followUpTemplateId"] = template.ToString(); x["requireReadingSteps"] = true; });
        var settings = (await Ok(await org.Admin.GetAsync("/api/settings"))).GetProperty("settings");
        Assert.Equal(("WorkOrder", template, true), (settings.GetProperty("followUpDefault").GetString(), settings.GetProperty("followUpTemplateId").GetGuid(),
            settings.GetProperty("requireReadingSteps").GetBoolean()));
    }
}
