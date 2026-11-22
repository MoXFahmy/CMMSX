using System.Net.Http.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class SafetyStepTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Blocked = "Answer the safety steps first: #1 Lock out.";
    private static readonly object[] Steps = [new { description = "Lock out", type = "Check", beforeStart = true }, new { description = "Clean", type = "Check", followUp = "None" }];

    private sealed record Setup(Loop.Org Org, Guid Wo, List<WorkOrderTask> Steps);

    private async Task<Setup> SetupAsync(Loop.Org? existing = null)
    {
        var org = existing ?? await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var t = await TypedTemplateAsync(org.Admin, $"S{Guid.NewGuid():N}"[..8], Steps);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Medium", type = "Preventive", templateId = t,
        })));
        return new Setup(org, wo, await StepsAsync(wo));
    }

    private Task<List<WorkOrderTask>> StepsAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).OrderBy(t => t.Sequence).ToListAsync());

    private Task<WorkOrderStatus> StatusOfAsync(Guid wo) => DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == wo).Select(w => w.Status).SingleAsync());

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient c, Guid wo, Guid step, object body) => c.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{step}", body);

    private static Task<HttpResponseMessage> MoveAsync(HttpClient c, Guid wo, object body) => c.PostAsJsonAsync($"/api/work-orders/{wo}/status", body);

    [SkippableFact]
    public async Task Work_cant_start_before_safety_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains(Blocked, await Bad(await MoveAsync(s.Org.Admin, s.Wo, new { status = "InProgress" })));
        await Ok(await AnswerAsync(s.Org.Admin, s.Wo, s.Steps[0].Id, new { answer = "Ok" }));
        await Ok(await MoveAsync(s.Org.Admin, s.Wo, new { status = "InProgress" }));
        Assert.Equal(WorkOrderStatus.InProgress, await StatusOfAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Safety_not_ok_blocks_start()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await AnswerAsync(s.Org.Admin, s.Wo, s.Steps[0].Id, new { answer = "NotOk" }));
        Assert.Contains("A safety step isn't OK: #1 Lock out.", await Bad(await MoveAsync(s.Org.Admin, s.Wo, new { status = "InProgress" })));
    }

    [SkippableFact]
    public async Task Other_steps_wait_for_safety_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains(Blocked, await Bad(await AnswerAsync(s.Org.Admin, s.Wo, s.Steps[1].Id, new { answer = "Ok" })));
        await Ok(await AnswerAsync(s.Org.Admin, s.Wo, s.Steps[0].Id, new { answer = "NotApplicable" }));
        await Ok(await AnswerAsync(s.Org.Admin, s.Wo, s.Steps[1].Id, new { answer = "Ok" }));
    }

    [SkippableFact]
    public async Task Bulk_group_and_phone_status_changes_check_safety()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var bulk = await Ok(await s.Org.Admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { s.Wo }, status = new { status = "InProgress" } }));
        Assert.Contains(Blocked, Assert.Single(bulk.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());
        var phone = await ActAsync(s.Org.Admin, "status.change", new { status = "InProgress" }, s.Wo);
        Assert.Equal(("rejected", Blocked), (Status(phone), Message(phone)));
        // A group whose work orders have safety steps.
        var a = await AssetAsync(s.Org.Admin, s.Org.Site, "Fan A");
        var b = await AssetAsync(s.Org.Admin, s.Org.Site, "Fan B");
        var t = await TypedTemplateAsync(s.Org.Admin, "GRP", Steps);
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, t, Today, assets: [a, b], extra: new() { ["multiAssetMode"] = "Group", ["name"] = "Fans" })));
        var group = (await GenerateAsync(s.Org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        var answer = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{group}/status", new { status = "InProgress" }));
        Assert.Equal(0, answer.GetProperty("changed").GetInt32());
        Assert.All(answer.GetProperty("refused").EnumerateArray(), r => Assert.Contains("Answer the safety steps first", r.GetProperty("reason").GetString()));
        Assert.Equal(WorkOrderStatus.Open, await StatusOfAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Auto_start_waits_for_safety()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await TestSettings.SetSettingsAsync(org.Admin, x => x["autoStartWork"] = true);
        var s = await SetupAsync(org);
        var me = (await EmployeeAsync(org.Admin, $"ME{Guid.NewGuid():N}"[..8], userId: org.AdminId)).Id;
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Wo}/labor", new { employeeId = me, workDate = Iso(Today), hours = 1, overtimeHours = 0 }));
        Assert.Equal(WorkOrderStatus.Open, await StatusOfAsync(s.Wo));
        await Ok(await AnswerAsync(org.Admin, s.Wo, s.Steps[0].Id, new { answer = "Ok" }));
        Assert.Equal(WorkOrderStatus.InProgress, await StatusOfAsync(s.Wo)); // answering the safety step itself starts it
    }

    [SkippableFact]
    public async Task Round_safety_is_per_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var t = await TypedTemplateAsync(org.Admin, "R", Steps);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, assets: [a, b])));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var steps = await StepsAsync(wo);
        var lockA = steps.Single(x => x.AssetId == a && x.BeforeStart); var cleanA = steps.Single(x => x.AssetId == a && !x.BeforeStart);
        var lockB = steps.Single(x => x.AssetId == b && x.BeforeStart); var cleanB = steps.Single(x => x.AssetId == b && !x.BeforeStart);
        await Ok(await MoveAsync(org.Admin, wo, new { status = "InProgress" })); // a round starts without its assets' safety steps
        await Ok(await AnswerAsync(org.Admin, wo, lockB.Id, new { answer = "Ok" }));
        await Ok(await AnswerAsync(org.Admin, wo, cleanB.Id, new { answer = "Ok" }));
        Assert.Contains($"Answer the safety steps first: #{lockA.Sequence} Lock out.", await Bad(await AnswerAsync(org.Admin, wo, cleanA.Id, new { answer = "Ok" })));
    }

    [SkippableFact]
    public async Task Completing_a_round_needs_every_assets_safety_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TypedTemplateAsync(org.Admin, "R", Steps), Today, assets: [a, b])));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var steps = await StepsAsync(wo);
        var lockA = steps.Single(x => x.AssetId == a && x.BeforeStart);
        await Ok(await MoveAsync(org.Admin, wo, new { status = "InProgress" }));
        foreach (var x in steps.Where(x => x.AssetId == b)) await Ok(await AnswerAsync(org.Admin, wo, x.Id, new { answer = "Ok" }));
        var done = new { status = "Completed", completionNotes = "Done" };
        Assert.Contains($"Answer the safety steps first: #{lockA.Sequence} Lock out.", await Bad(await MoveAsync(org.Admin, wo, done)));
        await Ok(await AnswerAsync(org.Admin, wo, lockA.Id, new { answer = "NotOk" }));
        Assert.Contains($"A safety step isn't OK: #{lockA.Sequence} Lock out.", await Bad(await MoveAsync(org.Admin, wo, done)));
        await Ok(await AnswerAsync(org.Admin, wo, lockA.Id, new { answer = "NotApplicable" }));
        await Ok(await MoveAsync(org.Admin, wo, done));
    }

    [SkippableFact]
    public async Task Completing_needs_safety_steps_too()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains(Blocked, await Bad(await MoveAsync(s.Org.Admin, s.Wo, new { status = "Completed", completionNotes = "Done" })));
    }
}
