using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class AccessWindowTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>A window on <paramref name="day"/> from 06:00 to 10:00 UTC (the test organizations run on UTC).</summary>
    private static object Window(DateOnly day, string? note = null) =>
        new { start = new DateTimeOffset(day.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero), end = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), note };

    private static async Task<JsonElement> WorkOrderAsync(HttpClient c, Guid wo) => await Ok(await c.GetAsync($"/api/work-orders/{wo}"));

    private static async Task<Guid> AssetAsync(HttpClient c, Loop.Org org, bool stopped) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Chiller", siteId = org.Site, locationId = org.Location, status = "Active", criticality = "High", warrantyType = "None", onlyWhenStopped = stopped })));

    [SkippableFact]
    public async Task Windows_are_copied_on_approval()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new
        {
            title = "Fix the AC in room 12", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective",
            accessWindows = new[] { Window(Today.AddDays(3), "Guest out"), Window(Today.AddDays(5)) },
        })));
        var wo = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        var w = await WorkOrderAsync(org.Admin, wo);
        Assert.Equal(2, w.GetProperty("accessWindows").GetArrayLength());
        Assert.False(w.GetProperty("outsideWindows").GetBoolean());

        await Ok(await org.Admin.PostAsJsonAsync("/api/planning/move", new { workOrderId = wo, day = Iso(Today.AddDays(4)) }));
        Assert.True((await WorkOrderAsync(org.Admin, wo)).GetProperty("outsideWindows").GetBoolean());
        await Ok(await org.Admin.PostAsJsonAsync("/api/planning/move", new { workOrderId = wo, day = Iso(Today.AddDays(3)) }));
        Assert.False((await WorkOrderAsync(org.Admin, wo)).GetProperty("outsideWindows").GetBoolean());
    }

    [SkippableFact]
    public async Task Bad_windows_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var t = DateTimeOffset.UtcNow.AddDays(1);
        var backwards = new[] { new { start = t, end = t.AddHours(-2), note = (string?)null } };
        Assert.Contains("must end after it starts", await Bad(await org.Admin.PostAsJsonAsync("/api/work-requests",
            new { title = "x", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", accessWindows = backwards })));
        Assert.Contains("must end after it starts", await Bad(await org.Admin.PostAsJsonAsync("/api/work-orders",
            new { title = "x", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", accessWindows = backwards })));
    }

    [SkippableFact]
    public async Task A_work_order_update_keeps_windows_when_left_out()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders",
            new { title = "Paint hall", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", accessWindows = new[] { Window(Today.AddDays(2)) } })));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Paint hall again", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" }));
        Assert.Equal(1, (await WorkOrderAsync(org.Admin, wo)).GetProperty("accessWindows").GetArrayLength());
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}",
            new { title = "Paint hall again", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", accessWindows = Array.Empty<object>() }));
        Assert.Equal(0, (await WorkOrderAsync(org.Admin, wo)).GetProperty("accessWindows").GetArrayLength());
    }

    [SkippableFact]
    public async Task The_board_flags_work_planned_outside_its_windows()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var outside = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Outside", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", plannedStart = Iso(Today.AddDays(4)),
            accessWindows = new[] { Window(Today.AddDays(3)) },
        })));
        var inside = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Inside", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", plannedStart = Iso(Today.AddDays(3)),
            accessWindows = new[] { Window(Today.AddDays(3)) },
        })));
        var board = await Ok(await org.Admin.GetAsync($"/api/planning/board?from={Iso(Today)}&to={Iso(Today.AddDays(6))}"));
        var jobs = board.GetProperty("jobs").EnumerateArray().ToDictionary(j => Id(j));
        Assert.True(jobs[outside].GetProperty("outsideWindows").GetBoolean());
        Assert.False(jobs[inside].GetProperty("outsideWindows").GetBoolean());
        Assert.Equal(1, jobs[inside].GetProperty("accessWindows").GetArrayLength());
    }

    [SkippableFact]
    public async Task The_stopped_flag_flows_from_the_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org, stopped: true);
        Assert.True((await Ok(await org.Admin.GetAsync($"/api/assets/{asset}"))).GetProperty("onlyWhenStopped").GetBoolean());

        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = org.Site, assetId = asset, priority = "Low", type = "Corrective" })));
        Assert.True((await Ok(await org.Admin.GetAsync($"/api/work-requests/{request}"))).GetProperty("onlyWhenStopped").GetBoolean());
        var flagged = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Clean coils", siteId = org.Site, assetId = asset, priority = "Low", type = "Corrective", plannedStart = Iso(Today.AddDays(1)) })));
        Assert.True((await WorkOrderAsync(org.Admin, flagged)).GetProperty("onlyWhenStopped").GetBoolean());
        var notFlagged = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders",
            new { title = "Read gauge", siteId = org.Site, assetId = asset, priority = "Low", type = "Corrective", onlyWhenStopped = false, plannedStart = Iso(Today.AddDays(1)) })));
        Assert.False((await WorkOrderAsync(org.Admin, notFlagged)).GetProperty("onlyWhenStopped").GetBoolean());

        var list = await Ok(await org.Admin.GetAsync("/api/work-orders?onlyWhenStopped=true&pageSize=50"));
        Assert.Equal([flagged], list.GetProperty("items").EnumerateArray().Select(Id).ToArray());
        var board = await Ok(await org.Admin.GetAsync($"/api/planning/board?from={Iso(Today)}&to={Iso(Today.AddDays(6))}&onlyWhenStopped=true"));
        Assert.Equal([flagged], board.GetProperty("jobs").EnumerateArray().Select(Id).ToArray());
        Assert.True(board.GetProperty("jobs")[0].GetProperty("onlyWhenStopped").GetBoolean());

        var task = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-tasks", new { code = "C1", name = "Coil clean", steps = new[] { "Clean" } })));
        var schedule = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Monthly coils", siteId = org.Site, locationId = org.Location, assetId = asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled",
            frequency = 1, unit = "Month", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Iso(Today), timeBased = false, extraTasks = Array.Empty<object>(),
        })));
        var pmList = await Ok(await org.Admin.GetAsync("/api/pm-schedules?pageSize=50"));
        Assert.True(pmList.GetProperty("items").EnumerateArray().Single(x => Id(x) == schedule).GetProperty("onlyWhenStopped").GetBoolean());
        await Ok(await org.Admin.PostAsJsonAsync("/api/pm/generate", new { until = Iso(Today) }));
        var generated = await Ok(await org.Admin.GetAsync("/api/work-orders?source=pm&pageSize=50"));
        var pmWork = Assert.Single(generated.GetProperty("items").EnumerateArray());
        Assert.True(pmWork.GetProperty("onlyWhenStopped").GetBoolean());
    }

    [SkippableFact]
    public async Task The_phone_job_carries_windows_and_the_flag()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var asset = await AssetAsync(org.Admin, org, stopped: true);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Swap filter", siteId = org.Site, assetId = asset, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id },
            accessWindows = new[] { Window(Today.AddDays(1), "Night shift only") },
        })));
        var bundle = await Ok(await org.Admin.GetAsync("/api/tech/bundle"));
        var job = bundle.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.True(job.GetProperty("onlyWhenStopped").GetBoolean());
        Assert.Equal("Night shift only", job.GetProperty("accessWindows")[0].GetProperty("note").GetString());
    }
}
