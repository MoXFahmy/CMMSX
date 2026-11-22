using System.Net.Http.Headers;
using System.Net.Http.Json;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class StepCompletionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 0xFF, 0xD9];

    private sealed record Setup(Loop.Org Org, Guid Wo, List<WorkOrderTask> Steps);

    private async Task<Setup> SetupAsync(object[] steps, Loop.Org? existing = null)
    {
        var org = existing ?? await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var t = await TypedTemplateAsync(org.Admin, $"C{Guid.NewGuid():N}"[..8], steps);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Medium", type = "Preventive", templateId = t,
        })));
        return new Setup(org, wo, await StepsAsync(wo));
    }

    private Task<List<WorkOrderTask>> StepsAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).OrderBy(t => t.Sequence).ToListAsync());

    private static object Check(string text, bool photo = false) => new { description = text, type = "Check", photoRequired = photo, followUp = "None" };

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient c, Guid wo) => c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" });

    private static async Task UploadAsync(HttpClient c, Guid wo, Guid step)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("WorkOrder"), "entityType" }, { new StringContent(wo.ToString()), "entityId" }, { new StringContent(step.ToString()), "stepId" },
        };
        var file = new ByteArrayContent(Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "files", "belt.jpg");
        await Ok(await c.PostAsync("/api/attachments", form));
    }

    [SkippableFact]
    public async Task All_ok_answers_plain_check_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync([Check("A"), Check("B", photo: true), new { description = "N", type = "Number" }, Check("C")]);
        await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[3].Id}", new { answer = "NotApplicable" }));
        var answer = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/all-ok", new { }));
        Assert.Equal(1, answer.GetProperty("answered").GetInt32());
        var steps = await StepsAsync(s.Wo);
        Assert.Equal<StepAnswer?>([StepAnswer.Ok, null, null, StepAnswer.NotApplicable], steps.Select(x => x.Answer).ToList());
    }

    [SkippableFact]
    public async Task All_ok_on_a_round_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var t = await TypedTemplateAsync(org.Admin, "R", [Check("Look"), Check("Listen")]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, assets: [a, b])));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        Assert.Equal(2, (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/tasks/all-ok", new { assetId = a }))).GetProperty("answered").GetInt32());
        var steps = await StepsAsync(wo);
        Assert.All(steps.Where(x => x.AssetId == a), x => Assert.Equal(StepAnswer.Ok, x.Answer));
        Assert.All(steps.Where(x => x.AssetId == b), x => Assert.Null(x.Answer));
    }

    [SkippableFact]
    public async Task All_ok_respects_safety_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync([new { description = "Lock out", type = "Check", beforeStart = true }, Check("Look")]);
        Assert.Contains("Answer the safety steps first: #1 Lock out.", await Bad(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/all-ok", new { })));
        Assert.All(await StepsAsync(s.Wo), x => Assert.Null(x.Answer));
    }

    [SkippableFact]
    public async Task Photo_required_step_needs_a_photo_to_complete()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync([Check("Look"), Check("Belt", photo: true)]);
        foreach (var step in s.Steps) await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{step.Id}", new { answer = "Ok" }));
        Assert.Contains("Take a photo for: #2 Belt.", await Bad(await CompleteAsync(s.Org.Admin, s.Wo)));
        await UploadAsync(s.Org.Admin, s.Wo, s.Steps[1].Id);
        var files = (await Ok(await s.Org.Admin.GetAsync($"/api/attachments?entityType=WorkOrder&entityId={s.Wo}"))).EnumerateArray().ToList();
        Assert.Equal(s.Steps[1].Id, files.Single().GetProperty("workOrderTaskId").GetGuid());
        await Ok(await CompleteAsync(s.Org.Admin, s.Wo));
    }

    [SkippableFact]
    public async Task Phone_photo_on_a_step()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync([Check("Belt", photo: true)]);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "clientId" }, { new StringContent(DateTimeOffset.UtcNow.ToString("O")), "at" },
            { new StringContent(s.Wo.ToString()), "workOrderId" }, { new StringContent("photo"), "kind" }, { new StringContent(s.Steps[0].Id.ToString()), "taskId" },
        };
        var file = new ByteArrayContent(Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "belt.jpg");
        Assert.Equal("done", Status(await Ok(await s.Org.Admin.PostAsync("/api/tech/files", form))));
        Assert.Equal(s.Steps[0].Id, await DbAsync(fx, db => db.Attachments.IgnoreQueryFilters().Where(a => a.EntityId == s.Wo).Select(a => a.WorkOrderTaskId).SingleAsync()));
    }

    [SkippableFact]
    public async Task Readings_needed_to_complete_when_set()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s = await SetupAsync([new { description = "Temp", type = "Number" }], org);
        await Ok(await CompleteAsync(org.Admin, s.Wo)); // off by default
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Wo}/status", new { status = "InProgress" }));
        await TestSettings.SetSettingsAsync(org.Admin, x => x["requireReadingSteps"] = true);
        Assert.Contains("Answer these steps first: #1 Temp.", await Bad(await CompleteAsync(org.Admin, s.Wo)));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[0].Id}", new { value = 21.5m }));
        await Ok(await CompleteAsync(org.Admin, s.Wo));
    }
}
