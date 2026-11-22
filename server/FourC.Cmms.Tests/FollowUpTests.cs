using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class FollowUpTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Asset, Guid Wo, List<WorkOrderTask> Steps);

    private async Task<Setup> SetupAsync(Loop.Org org, object[] steps)
    {
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var t = await TypedTemplateAsync(org.Admin, $"T{Guid.NewGuid():N}"[..8], steps);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Low", type = "Preventive", templateId = t,
        })));
        return new Setup(org, asset, wo, await StepsAsync(wo));
    }

    private Task<List<WorkOrderTask>> StepsAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).OrderBy(t => t.Sequence).ToListAsync());

    private static Task<HttpResponseMessage> AnswerAsync(Setup s, int index, object body) => s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[index].Id}", body);

    private Task<List<WorkRequest>> RequestsFromAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkRequests.IgnoreQueryFilters().Where(r => r.SourceWorkOrderId == wo).ToListAsync());

    private Task<List<WorkOrder>> WorkFromAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.SourceWorkOrderId == wo).ToListAsync());

    private static object Check(string text, string followUp = "Default", Guid? template = null) => new { description = text, type = "Check", followUp, followUpTemplateId = template };

    [SkippableFact]
    public async Task Not_ok_raises_a_request_by_default()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(await OrgAsync(fx, emailOn: false), [Check("Belt")]);
        var answer = await Ok(await AnswerAsync(s, 0, new { answer = "NotOk", remarks = "Cracked" }));
        var r = (await RequestsFromAsync(s.Wo)).Single();
        Assert.Equal(("Belt — from step 1 of WO-00001", s.Asset, s.Steps[0].Id), (r.Title, r.AssetId!.Value, r.SourceTaskId!.Value));
        Assert.Contains("Answer: Not OK", r.Description);
        Assert.Contains("Cracked", r.Description);
        Assert.Equal(r.Id, (await StepsAsync(s.Wo))[0].FollowUpRequestId);
        Assert.Equal(("request", r.Number), (answer.GetProperty("followUp").GetProperty("kind").GetString(), answer.GetProperty("followUp").GetProperty("number").GetString()));
        Assert.Empty(await WorkFromAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Step_set_to_work_order_raises_one_from_its_template()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var repair = await TypedTemplateAsync(org.Admin, "REP", [new { description = "Lock out", type = "Check", beforeStart = true }, Check("Replace belt")],
            new() { ["title"] = "Belt replacement", ["priority"] = "High", ["workType"] = "Corrective" });
        var s = await SetupAsync(org, [Check("Look"), Check("Belt", "WorkOrder", repair)]);
        await Ok(await AnswerAsync(s, 1, new { answer = "NotOk" }));
        var w = (await WorkFromAsync(s.Wo)).Single();
        Assert.Equal(("Belt replacement — from step 2 of WO-00001", WorkPriority.High, WorkType.Corrective, s.Asset), (w.Title, w.Priority, w.Type, w.AssetId!.Value));
        Assert.Equal(s.Steps[1].Id, w.SourceTaskId);
        var steps = await StepsAsync(w.Id);
        Assert.Equal([true, false], steps.Select(x => x.BeforeStart));
        Assert.Empty(await RequestsFromAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Point_level_template_wins()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var alert = await TemplateAsync(org.Admin, "ALERT");
        var alarm = await TemplateAsync(org.Admin, "ALARM");
        var point = await PointAsync(org.Admin, "SAT", new() { ["alertHighTemplateId"] = alert, ["alarmHighTemplateId"] = alarm });
        var hot = await SetupAsync(org, [new { description = "Supply air", type = "Number", measurementPointId = point }]);
        await Ok(await AnswerAsync(hot, 0, new { value = 33 }));
        Assert.Equal(alarm, (await WorkFromAsync(hot.Wo)).Single().PmTaskId);
        var warm = await SetupAsync(org, [new { description = "Supply air", type = "Number", measurementPointId = point }]);
        await Ok(await AnswerAsync(warm, 0, new { value = 29 }));
        Assert.Equal(alert, (await WorkFromAsync(warm.Wo)).Single().PmTaskId);
        Assert.Empty(await RequestsFromAsync(hot.Wo));
    }

    [SkippableFact]
    public async Task None_raises_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(await OrgAsync(fx, emailOn: false), [Check("Belt", "None")]);
        await Ok(await AnswerAsync(s, 0, new { answer = "NotOk" }));
        Assert.Empty(await RequestsFromAsync(s.Wo));
        Assert.Empty(await WorkFromAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Org_default_work_order_uses_its_template()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var fallback = await TemplateAsync(org.Admin, "DEF");
        Assert.Contains("Choose a work template for this follow-up.",
            await Bad(await TestSettings.TrySetSettingsAsync(org.Admin, x => x["followUpDefault"] = "WorkOrder")));
        await TestSettings.SetSettingsAsync(org.Admin, x => { x["followUpDefault"] = "WorkOrder"; x["followUpTemplateId"] = fallback.ToString(); });
        var s = await SetupAsync(org, [Check("Belt")]);
        await Ok(await AnswerAsync(s, 0, new { answer = "NotOk" }));
        Assert.Equal(fallback, (await WorkFromAsync(s.Wo)).Single().PmTaskId);
    }

    [SkippableFact]
    public async Task Changing_the_answer_raises_one_follow_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var s = await SetupAsync(org, [Check("Belt"), new { description = "Supply air", type = "Number", measurementPointId = point }]);
        foreach (var a in new[] { "NotOk", "Ok", "NotOk" }) await Ok(await AnswerAsync(s, 0, new { answer = a }));
        foreach (var v in new[] { 33, 20, 33 }) await Ok(await AnswerAsync(s, 1, new { value = v }));
        Assert.Equal(2, (await RequestsFromAsync(s.Wo)).Count); // one for each step
        Assert.Single(await DbAsync(fx, db => db.MeasurementReadings.IgnoreQueryFilters().Where(r => r.WorkOrderId == s.Wo && !r.IsDeleted).ToListAsync()));
    }

    [SkippableFact]
    public async Task Round_follow_up_is_for_the_steps_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var t = await TypedTemplateAsync(org.Admin, "R", [Check("Belt")]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, assets: [a, b])));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var stepB = (await StepsAsync(wo)).Single(x => x.AssetId == b);
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{stepB.Id}", new { answer = "NotOk" }));
        Assert.Equal(b, (await RequestsFromAsync(wo)).Single().AssetId);
    }

    [SkippableFact]
    public async Task Phone_answer_raises_the_follow_up_in_the_same_save()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(await OrgAsync(fx, emailOn: false), [Check("Belt")]);
        Assert.Equal("done", Status(await ActAsync(s.Org.Admin, "task.update", new { taskId = s.Steps[0].Id, answer = "NotOk" }, s.Wo)));
        Assert.Single(await RequestsFromAsync(s.Wo));
    }

    [SkippableFact]
    public async Task Rejected_phone_answer_raises_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var s = await SetupAsync(org, [Check("Belt"), new { description = "Supply air", type = "Number", measurementPointId = point }]);
        Assert.Equal("rejected", Status(await ActAsync(s.Org.Admin, "task.update", new { taskId = s.Steps[1].Id, value = 61 }, s.Wo)));
        // Someone else answered the step after the phone did: the phone's older Not OK is refused and raises nothing.
        var tech = await UserAsync(fx, org.Admin, "Technician");
        await Ok(await AnswerAsync(s, 0, new { answer = "Ok" }));
        Assert.Equal("rejected", Status(await ActAsync(tech.Client, "task.update", new { taskId = s.Steps[0].Id, answer = "NotOk" }, s.Wo, at: DateTimeOffset.UtcNow.AddHours(-1))));
        Assert.Empty(await RequestsFromAsync(s.Wo));
        Assert.Empty(await DbAsync(fx, db => db.MeasurementReadings.IgnoreQueryFilters().Where(r => r.WorkOrderId == s.Wo).ToListAsync()));
    }

    [SkippableFact]
    public async Task Direct_reading_at_alarm_raises_its_follow_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var alarm = await TemplateAsync(org.Admin, "ALARM", extra: new() { ["title"] = "Overheating" });
        var point = await PointAsync(org.Admin, "SAT", new() { ["alarmHighTemplateId"] = alarm });
        await Ok(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 33 }));
        var reading = await DbAsync(fx, db => db.MeasurementReadings.IgnoreQueryFilters().SingleAsync(r => r.PointId == point));
        var w = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == reading.FollowUpWorkOrderId));
        Assert.Equal(("Overheating — from a reading of 33 °C", asset, alarm), (w.Title, w.AssetId!.Value, w.PmTaskId!.Value));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 20 }));
        Assert.Equal(1, await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().CountAsync(x => x.PmTaskId == alarm)));
    }

    [SkippableFact]
    public async Task Follow_up_shows_its_source()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var repair = await TemplateAsync(org.Admin, "REP");
        var s = await SetupAsync(org, [Check("Look", "Request"), Check("Belt", "WorkOrder", repair)]);
        await Ok(await AnswerAsync(s, 1, new { answer = "NotOk", remarks = "Cracked" }));
        await Ok(await AnswerAsync(s, 0, new { answer = "NotOk" }));
        var w = (await WorkFromAsync(s.Wo)).Single();
        var source = (await Ok(await org.Admin.GetAsync($"/api/work-orders/{w.Id}"))).GetProperty("source");
        Assert.Equal(("WO-00001", 2, "Belt", "NotOk", "Cracked"), (source.GetProperty("number").GetString(), source.GetProperty("stepSequence").GetInt32(),
            source.GetProperty("stepDescription").GetString(), source.GetProperty("answer").GetString(), source.GetProperty("remarks").GetString()));
        Assert.Equal(JsonValueKind.Array, source.GetProperty("photos").ValueKind);
        var r = (await RequestsFromAsync(s.Wo)).Single();
        Assert.Equal(1, (await Ok(await org.Admin.GetAsync($"/api/work-requests/{r.Id}"))).GetProperty("source").GetProperty("stepSequence").GetInt32());
        var steps = (await Ok(await org.Admin.GetAsync($"/api/work-orders/{s.Wo}"))).GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal((w.Number, "workOrder"), (steps[1].GetProperty("followUp").GetProperty("number").GetString(), steps[1].GetProperty("followUp").GetProperty("kind").GetString()));
    }
}
