using System.Net.Http.Json;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class StepAnswerTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Asset, Guid Point, Guid Wo, List<WorkOrderTask> Steps);

    /// <summary>A work order on an asset made from a template with these typed steps (default: check, number with the SAT point, text).</summary>
    private async Task<Setup> SetupAsync(object[]? steps = null, Func<Loop.Org, Guid, Task>? beforeWork = null)
    {
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        if (beforeWork is not null) await beforeWork(org, asset);
        var t = await TypedTemplateAsync(org.Admin, "T1", steps ?? [
            new { description = "Belt", type = "Check" }, new { description = "Supply air", type = "Number", measurementPointId = point }, new { description = "Notes", type = "Text" },
        ]);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Medium", type = "Preventive", templateId = t,
        })));
        return new Setup(org, asset, point, wo, await StepsAsync(wo));
    }

    private Task<List<WorkOrderTask>> StepsAsync(Guid wo) =>
        DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).OrderBy(t => t.Sequence).ToListAsync());

    private static Task<HttpResponseMessage> AnswerAsync(Setup s, int index, object body) => s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Wo}/tasks/{s.Steps[index].Id}", body);

    private Task<List<MeasurementReading>> ReadingsOfAsync(Guid wo) =>
        DbAsync(fx, db => db.MeasurementReadings.IgnoreQueryFilters().Where(r => r.WorkOrderId == wo && !r.IsDeleted).ToListAsync());

    [SkippableFact]
    public async Task Check_step_takes_ok_not_ok_and_na()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        foreach (var answer in new[] { StepAnswer.Ok, StepAnswer.NotApplicable, StepAnswer.NotOk })
        {
            await Ok(await AnswerAsync(s, 0, new { answer = answer.ToString() }));
            var step = (await StepsAsync(s.Wo))[0];
            Assert.Equal((answer, true), (step.Answer!.Value, step.IsDone));
        }
    }

    [SkippableFact]
    public async Task Number_step_with_point_gets_its_level()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var answer = await Ok(await AnswerAsync(s, 1, new { value = 31.04m }));
        Assert.Equal("AlertHigh", answer.GetProperty("level").GetString());
        var step = (await StepsAsync(s.Wo))[1];
        Assert.Equal((31.0m, MeasureLevel.AlertHigh, true), (step.Value!.Value, step.Level!.Value, step.IsDone));
        var reading = (await ReadingsOfAsync(s.Wo)).Single();
        Assert.Equal((s.Point, s.Asset, step.Id, 31.0m), (reading.PointId, reading.AssetId!.Value, reading.WorkOrderTaskId!.Value, reading.Value));
    }

    [SkippableFact]
    public async Task Changing_a_value_replaces_its_reading()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await AnswerAsync(s, 1, new { value = 31 }));
        await Ok(await AnswerAsync(s, 1, new { value = 20 }));
        var reading = (await ReadingsOfAsync(s.Wo)).Single();
        Assert.Equal((20m, MeasureLevel.Normal), (reading.Value, reading.Level));
        await Ok(await AnswerAsync(s, 1, new { isDone = false }));
        Assert.Empty(await ReadingsOfAsync(s.Wo));
        var step = (await StepsAsync(s.Wo))[1];
        Assert.Equal((null, null, false), (step.Value, step.Level, step.IsDone));
    }

    [SkippableFact]
    public async Task Value_outside_the_valid_range_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains("Enter a value from 0 to 60 °C.", await Bad(await AnswerAsync(s, 1, new { value = 61 })));
        Assert.Null((await StepsAsync(s.Wo))[1].Value);
    }

    [SkippableFact]
    public async Task Meter_step_records_a_meter_reading()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Guid meter = Guid.Empty, pm = Guid.Empty;
        var s = await SetupAsync([new { description = "Hours", type = "Meter" }], async (org, asset) =>
        {
            meter = await MeterAsync(org.Admin, asset, "H1");
            pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "MPM"), Today.AddDays(10), asset, extra: new()
            {
                ["name"] = "Every 100 h", ["type"] = "Meter", ["meterId"] = meter, ["meterTrigger"] = "Incremental", ["meterInterval"] = 100,
            })));
        });
        await Ok(await AnswerAsync(s, 0, new { value = 150 }));
        var (current, readingWo, generated) = await DbAsync(fx, async db => (
            (await db.Meters.IgnoreQueryFilters().SingleAsync(m => m.Id == meter)).CurrentReading,
            (await db.MeterReadings.IgnoreQueryFilters().SingleAsync(r => r.MeterId == meter)).WorkOrderId,
            await db.WorkOrders.IgnoreQueryFilters().CountAsync(w => w.PmScheduleId == pm)));
        Assert.Equal((150m, s.Wo, 1), (current!.Value, readingWo!.Value, generated));
        Assert.Equal((150m, true), ((await StepsAsync(s.Wo))[0].Value!.Value, (await StepsAsync(s.Wo))[0].IsDone));
    }

    [SkippableFact]
    public async Task Meter_step_without_meter_needs_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Guid km = Guid.Empty;
        var s = await SetupAsync([new { description = "Reading", type = "Meter" }], async (org, asset) =>
        {
            await MeterAsync(org.Admin, asset, "H1");
            km = await MeterAsync(org.Admin, asset, "K1", "km");
        });
        Assert.Contains("Choose the meter this reading is for.", await Bad(await AnswerAsync(s, 0, new { value = 10 })));
        await Ok(await AnswerAsync(s, 0, new { value = 10, meterId = km }));
        Assert.Equal(km, (await StepsAsync(s.Wo))[0].MeterId);
    }

    [SkippableFact]
    public async Task Text_step_keeps_its_text()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await AnswerAsync(s, 2, new { text = "Belt worn on the left" }));
        var step = (await StepsAsync(s.Wo))[2];
        Assert.Equal(("Belt worn on the left", true), (step.TextValue, step.IsDone));
    }

    [SkippableFact]
    public async Task Wrong_answer_kind_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains("This step needs a different kind of answer.", await Bad(await AnswerAsync(s, 0, new { value = 3 })));
        Assert.Contains("This step needs a different kind of answer.", await Bad(await AnswerAsync(s, 1, new { answer = "Ok" })));
        Assert.Contains("This step needs a different kind of answer.", await Bad(await AnswerAsync(s, 2, new { value = 3 })));
    }

    [SkippableFact]
    public async Task Old_tick_still_works()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await AnswerAsync(s, 0, new { isDone = true, reading = "ok", remarks = "Fine" }));
        var step = (await StepsAsync(s.Wo))[0];
        Assert.Equal((StepAnswer.Ok, true, "ok", "Fine"), (step.Answer!.Value, step.IsDone, step.Reading, step.Remarks));
        await Ok(await AnswerAsync(s, 0, new { isDone = false }));
        step = (await StepsAsync(s.Wo))[0];
        Assert.Equal((null, false), (step.Answer, step.IsDone));
    }

    [SkippableFact]
    public async Task A_step_ticked_before_the_upgrade_stays_ticked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        // Ticked by 0.12 or earlier: done, no answer stored.
        await DbAsync(fx, async db =>
        {
            var t = await db.WorkOrderTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == s.Steps[0].Id);
            t.IsDone = true; t.Answer = null;
            return await db.SaveChangesAsync();
        });
        var shown = (await Ok(await s.Org.Admin.GetAsync($"/api/work-orders/{s.Wo}"))).GetProperty("tasks").EnumerateArray().First();
        Assert.Equal("Ok", shown.GetProperty("answer").GetString());
        await Ok(await AnswerAsync(s, 0, new { remarks = "Checked again" }));
        var step = (await StepsAsync(s.Wo))[0];
        Assert.Equal((true, StepAnswer.Ok, "Checked again"), (step.IsDone, step.Answer!.Value, step.Remarks));
    }

    [SkippableFact]
    public async Task Phone_answers_typed_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Equal("done", Status(await ActAsync(s.Org.Admin, "task.update", new { taskId = s.Steps[1].Id, value = 25 }, s.Wo)));
        Assert.Equal("done", Status(await ActAsync(s.Org.Admin, "task.update", new { taskId = s.Steps[0].Id, answer = "NotApplicable" }, s.Wo)));
        var steps = await StepsAsync(s.Wo);
        Assert.Equal((25m, StepAnswer.NotApplicable), (steps[1].Value!.Value, steps[0].Answer!.Value));
    }

    [SkippableFact]
    public async Task Phone_value_outside_valid_range_is_rejected()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var answer = await ActAsync(s.Org.Admin, "task.update", new { taskId = s.Steps[1].Id, value = 61 }, s.Wo);
        Assert.Equal(("rejected", "Enter a value from 0 to 60 °C."), (Status(answer), Message(answer)));
        Assert.Empty(await ReadingsOfAsync(s.Wo));
    }
}
