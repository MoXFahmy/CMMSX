using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class TypedStepTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static object Safety(string text) => new { description = text, type = "Check", beforeStart = true };
    private static object Num(string text, Guid point) => new { description = text, type = "Number", measurementPointId = point };
    private static object Photo(string text) => new { description = text, type = "Check", photoRequired = true };

    private Task<List<WorkOrderTask>> StepsAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).OrderBy(t => t.Sequence).ToListAsync());

    private static async Task<Guid> WoAsync(HttpClient c, Loop.Org org, Guid? asset, Guid? template = null, string[]? tasks = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix", siteId = org.Site, locationId = asset is null ? org.Location : (Guid?)null, assetId = asset, priority = "Medium", type = "Corrective",
            templateId = template, tasks,
        })));

    [SkippableFact]
    public async Task Template_steps_keep_their_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var repair = await TemplateAsync(org.Admin, "REP");
        var t = await TypedTemplateAsync(org.Admin, "T1",
        [
            Safety("Lock out"), Num("Supply air", point), new { description = "Hours", type = "Meter", unit = "h" },
            new { description = "Belt", type = "Text", photoRequired = true, followUp = "WorkOrder", followUpTemplateId = repair },
        ]);
        var steps = (await Ok(await org.Admin.GetAsync($"/api/pm-tasks/{t}"))).GetProperty("stepDetails").EnumerateArray().ToList();
        Assert.Equal(["Check", "Number", "Meter", "Text"], steps.Select(s => s.GetProperty("type").GetString()));
        Assert.True(steps[0].GetProperty("beforeStart").GetBoolean());
        Assert.Equal(point, steps[1].GetProperty("measurementPointId").GetGuid());
        Assert.Equal("h", steps[2].GetProperty("unit").GetString());
        Assert.Equal(("WorkOrder", repair, true), (steps[3].GetProperty("followUp").GetString(), steps[3].GetProperty("followUpTemplateId").GetGuid(), steps[3].GetProperty("photoRequired").GetBoolean()));
        // The plain list still reads the texts.
        Assert.Equal(["Lock out", "Supply air", "Hours", "Belt"], (await Ok(await org.Admin.GetAsync($"/api/pm-tasks/{t}"))).GetProperty("steps").EnumerateArray().Select(s => s.GetString()));
    }

    [SkippableFact]
    public async Task A_point_sets_the_unit_and_decimals()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var t = await TypedTemplateAsync(org.Admin, "T1", [new { description = "Supply air", type = "Number", unit = "K", decimals = 0, measurementPointId = point }]);
        var step = await DbAsync(fx, db => db.PmTaskSteps.IgnoreQueryFilters().SingleAsync(s => s.PmTaskId == t));
        Assert.Equal((point, null, null), (step.MeasurementPointId!.Value, step.Unit, step.Decimals));
    }

    [SkippableFact]
    public async Task Pm_work_copies_step_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        var t = await TypedTemplateAsync(org.Admin, "T1", [Safety("Lock out"), Num("Supply air", point)]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, asset)));
        var wo = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var steps = await StepsAsync(wo);
        Assert.Equal([StepType.Check, StepType.Number], steps.Select(s => s.Type));
        Assert.True(steps[0].BeforeStart);
        Assert.Equal(point, steps[1].MeasurementPointId);
    }

    [SkippableFact]
    public async Task Round_and_group_children_copy_their_assets_template_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var point = await PointAsync(org.Admin, "SAT");
        var routine = await TypedTemplateAsync(org.Admin, "R", [Photo("Look")]);
        var own = await TypedTemplateAsync(org.Admin, "OWN", [Num("Current", point)]);
        var round = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today, assets: [a, b],
            extra: new() { ["assetTemplates"] = new[] { new { assetId = b, pmTaskId = own } } })));
        var wo = (await GenerateAsync(org.Admin, Today, round)).EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
        var steps = await StepsAsync(wo);
        Assert.True(steps.Single(s => s.AssetId == a).PhotoRequired);
        Assert.Equal((StepType.Number, point), (steps.Single(s => s.AssetId == b).Type, steps.Single(s => s.AssetId == b).MeasurementPointId!.Value));
        var group = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today, assets: [a, b],
            extra: new() { ["multiAssetMode"] = "Group", ["name"] = "Fans", ["assetTemplates"] = new[] { new { assetId = b, pmTaskId = own } } })));
        var groupId = (await GenerateAsync(org.Admin, Today, group)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        var children = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == groupId).Select(w => new { w.Id, w.AssetId }).ToListAsync());
        Assert.True((await StepsAsync(children.Single(c => c.AssetId == a).Id)).Single().PhotoRequired);
        Assert.Equal(StepType.Number, (await StepsAsync(children.Single(c => c.AssetId == b).Id)).Single().Type);
    }

    [SkippableFact]
    public async Task Work_order_from_template_matches_steps_by_text()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var t = await TypedTemplateAsync(org.Admin, "T1", [Safety("Lock out"), Num("Temp", point), Photo("Belt")]);
        var wo = await WoAsync(org.Admin, org, null, t, ["Lock out", "Temp", "Belt", "Extra"]);
        var steps = await StepsAsync(wo);
        Assert.Equal([true, false, false, false], steps.Select(s => s.BeforeStart));
        Assert.Equal([StepType.Check, StepType.Number, StepType.Check, StepType.Check], steps.Select(s => s.Type));
        Assert.Equal([false, false, true, false], steps.Select(s => s.PhotoRequired));
    }

    [SkippableFact]
    public async Task Asset_checklist_and_phone_add_copy_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Press");
        var safety = await TypedTemplateAsync(org.Admin, "SAFE", [Safety("Lock out")]);
        await Ok(await org.Admin.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, pmTaskId = safety }));
        var wo = await WoAsync(org.Admin, org, asset, tasks: ["Fix it"]);
        Assert.Equal([false, true], (await StepsAsync(wo)).Select(s => s.BeforeStart));
        var photo = await TypedTemplateAsync(org.Admin, "PH", [Photo("Picture")]);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "tasks.fromPm", new { pmTaskId = photo }, wo)));
        Assert.True((await StepsAsync(wo)).Last().PhotoRequired);
    }

    [SkippableFact]
    public async Task Own_template_copies_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var t = await TypedTemplateAsync(org.Admin, "SH", [Safety("Lock out")]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, asset)));
        var copy = (await Ok(await org.Admin.PostAsync($"/api/pm-schedules/{pm}/own-template", null))).GetProperty("pmTaskId").GetGuid();
        Assert.True((await Ok(await org.Admin.GetAsync($"/api/pm-tasks/{copy}"))).GetProperty("stepDetails").EnumerateArray().Single().GetProperty("beforeStart").GetBoolean());
    }

    [SkippableFact]
    public async Task Meter_step_finds_the_assets_meter()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var one = await AssetAsync(org.Admin, org.Site, "One meter");
        var meter = await MeterAsync(org.Admin, one, "H1");
        var two = await AssetAsync(org.Admin, org.Site, "Two meters");
        await MeterAsync(org.Admin, two, "H2");
        var km = await MeterAsync(org.Admin, two, "K2", "km");
        var any = await TypedTemplateAsync(org.Admin, "ANY", [new { description = "Reading", type = "Meter" }]);
        var kms = await TypedTemplateAsync(org.Admin, "KM", [new { description = "Odometer", type = "Meter", unit = "km" }]);
        Assert.Equal(meter, (await StepsAsync(await WoAsync(org.Admin, org, one, any))).Single().MeterId);
        Assert.Null((await StepsAsync(await WoAsync(org.Admin, org, two, any))).Single().MeterId);
        Assert.Equal(km, (await StepsAsync(await WoAsync(org.Admin, org, two, kms))).Single().MeterId);
    }

    [SkippableFact]
    public async Task Templates_used_for_follow_ups_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        const string used = "This template is used for follow-up work. Mark it inactive instead.";
        var level = await TemplateAsync(org.Admin, "LVL");
        await PointAsync(org.Admin, "SAT", new() { ["alarmHighTemplateId"] = level });
        Assert.Contains(used, await Bad(await org.Admin.DeleteAsync($"/api/pm-tasks/{level}")));
        var repair = await TemplateAsync(org.Admin, "REP");
        await TypedTemplateAsync(org.Admin, "CHK", [new { description = "Belt", type = "Check", followUp = "WorkOrder", followUpTemplateId = repair }]);
        Assert.Contains(used, await Bad(await org.Admin.DeleteAsync($"/api/pm-tasks/{repair}")));
        var fallback = await TemplateAsync(org.Admin, "DEF");
        await TestSettings.SetSettingsAsync(org.Admin, s => { s["followUpDefault"] = "WorkOrder"; s["followUpTemplateId"] = fallback.ToString(); });
        Assert.Contains(used, await Bad(await org.Admin.DeleteAsync($"/api/pm-tasks/{fallback}")));
        var free = await TemplateAsync(org.Admin, "FREE");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await org.Admin.DeleteAsync($"/api/pm-tasks/{free}")).StatusCode);
    }

    [SkippableFact]
    public async Task Follow_up_kind_needs_a_template()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var body = new Dictionary<string, object?> { ["code"] = "X", ["name"] = "X", ["isActive"] = true };
        body["stepDetails"] = new[] { new { description = "Belt", type = "Check", followUp = "WorkOrder" } };
        Assert.Contains("Choose a work template for this follow-up.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-tasks", body)));
        body["stepDetails"] = new[] { new { description = "Belt", type = "Check", measurementPointId = point } };
        Assert.Contains("Only number steps can use a measurement point.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-tasks", body)));
        body["stepDetails"] = new[] { new { description = "Belt", type = "Number", measurementPointId = Guid.NewGuid() } };
        Assert.Contains("Unknown measurement point.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-tasks", body)));
    }
}
