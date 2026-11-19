using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FourC.Cmms.Tests;

public class PmTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private async Task<(HttpClient c, Guid site, Guid asset, Guid weekly, Guid monthly)> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "PM Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Site", isActive = true }))).GetProperty("id").GetGuid();
        var asset = (await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = site, status = "Active", criticality = "High", warrantyType = "None" }))).GetProperty("id").GetGuid();
        var weekly = (await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code = "W1", name = "Weekly check", steps = new[] { "Look", "Listen" } }))).GetProperty("id").GetGuid();
        var monthly = (await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code = "M1", name = "Monthly service", steps = new[] { "Grease" } }))).GetProperty("id").GetGuid();
        return (c, site, asset, weekly, monthly);
    }

    private static object Schedule(Guid site, Guid asset, Guid task, DateOnly start, string basis = "Scheduled", object[]? extra = null) => new
    {
        name = "Pump PM", siteId = site, assetId = asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis, frequency = 1, unit = "Week",
        workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Iso(start), timeBased = false, extraTasks = extra ?? [],
    };

    [SkippableFact]
    public async Task Generation_catches_up_rotates_tasks_and_is_idempotent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset, weekly, monthly) = await SetupAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sched = await Ok(await c.PostAsJsonAsync("/api/pm-schedules", Schedule(site, asset, weekly, today.AddDays(-21), extra: [new { pmTaskId = monthly, everyNth = 4 }])));

        var preview = await Ok(await c.GetAsync($"/api/pm/preview?until={Iso(today)}"));
        Assert.Equal(4, preview.GetArrayLength());
        var gen = await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today) }));
        Assert.Equal(4, gen.GetProperty("created").GetInt32());
        Assert.Equal("Monthly service", gen.GetProperty("results")[3].GetProperty("taskName").GetString());
        Assert.Equal(0, (await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today) }))).GetProperty("created").GetInt32());

        var wo = await Ok(await c.GetAsync($"/api/work-orders/{gen.GetProperty("results")[0].GetProperty("workOrderId").GetGuid()}"));
        Assert.Equal("Preventive", wo.GetProperty("type").GetString());
        Assert.Equal(2, wo.GetProperty("tasks").GetArrayLength());
        Assert.Equal(Iso(today.AddDays(-20)), wo.GetProperty("dueDate").GetString()); // 2-day work period
        var detail = await Ok(await c.GetAsync($"/api/pm-schedules/{sched.GetProperty("id").GetGuid()}"));
        Assert.Equal(Iso(today.AddDays(7)), detail.GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Actual_basis_waits_for_completion_and_meters_trigger_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset, weekly, _) = await SetupAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sched = (await Ok(await c.PostAsJsonAsync("/api/pm-schedules", Schedule(site, asset, weekly, today, "Actual")))).GetProperty("id").GetGuid();
        var first = await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today.AddDays(60)) }));
        Assert.Equal(1, first.GetProperty("created").GetInt32());
        Assert.Equal(0, (await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today.AddDays(60)) }))).GetProperty("created").GetInt32());
        var wo = first.GetProperty("results")[0].GetProperty("workOrderId").GetGuid();
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "InProgress" }));
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "ok" }));
        Assert.Equal(Iso(today.AddDays(7)), (await Ok(await c.GetAsync($"/api/pm-schedules/{sched}"))).GetProperty("nextDueDate").GetString());

        var meter = (await Ok(await c.PostAsJsonAsync("/api/meters", new { code = "RH", name = "Run hours", assetId = asset, unit = "h" }))).GetProperty("id").GetGuid();
        await Ok(await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 100 }));
        await Ok(await c.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Every 50 h", siteId = site, assetId = asset, priority = "Medium", pmTaskId = weekly, type = "Meter", basis = "Scheduled", frequency = 1, unit = "Month",
            workPeriodDays = 1, wholeMonth = false, leadDays = 0, startDate = Iso(today), meterId = meter, meterTrigger = "Incremental", meterInterval = 50, timeBased = false,
        }));
        Assert.Equal(0, (await Ok(await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 140, confirm = true }))).GetProperty("generated").GetArrayLength());
        var hit = await Ok(await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 155, confirm = true }));
        Assert.Single(hit.GetProperty("generated").EnumerateArray(), g => g.GetProperty("workOrderNumber").GetString() is not null);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 20 })).StatusCode); // lower than last needs confirmation
    }

    private static string? Skipped(JsonElement r) => r.GetProperty("skipped").ValueKind == JsonValueKind.String ? r.GetProperty("skipped").GetString() : null;

    [SkippableFact]
    public async Task Skip_rule_skips_occurrences_for_assets_in_chosen_statuses()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, _, weekly, _) = await SetupAsync();
        var down = (await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Broken pump", siteId = site, status = "UnderRepair", criticality = "High", warrantyType = "None" }))).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sched = await Ok(await c.PostAsJsonAsync("/api/pm-schedules", Schedule(site, down, weekly, today.AddDays(-14))));

        var preview = await Ok(await c.GetAsync($"/api/pm/preview?until={Iso(today)}"));
        Assert.Equal(3, preview.GetArrayLength());
        Assert.All(preview.EnumerateArray(), r => Assert.Null(Skipped(r))); // rule off: nothing skipped

        await TestSettings.SetSettingsAsync(c, s => s["pmSkipAssetStatuses"] = new JsonArray("UnderRepair"));
        var gen = await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today) }));
        Assert.Equal(0, gen.GetProperty("created").GetInt32());
        var results = gen.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("Skipped: the asset is Under repair", Skipped(r)));
        var detail = await Ok(await c.GetAsync($"/api/pm-schedules/{sched.GetProperty("id").GetGuid()}"));
        Assert.Equal(Iso(today.AddDays(7)), detail.GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Non_working_days_move_the_work_not_the_schedule()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset, weekly, _) = await SetupAsync();
        var sat = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (sat.DayOfWeek != DayOfWeek.Saturday) sat = sat.AddDays(1);
        var sched = await Ok(await c.PostAsJsonAsync("/api/pm-schedules", Schedule(site, asset, weekly, sat)));

        await TestSettings.SetSettingsAsync(c, s => s["pmNonWorkingDays"] = new JsonArray("Saturday", "Sunday"));
        var gen = await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(sat) }));
        Assert.Equal(1, gen.GetProperty("created").GetInt32());
        var wo = await Ok(await c.GetAsync($"/api/work-orders/{gen.GetProperty("results")[0].GetProperty("workOrderId").GetGuid()}"));
        Assert.Equal(Iso(sat.AddDays(2)), wo.GetProperty("plannedStart").GetString()); // Monday
        Assert.Equal(Iso(sat.AddDays(3)), wo.GetProperty("dueDate").GetString());      // 2-day work period
        var detail = await Ok(await c.GetAsync($"/api/pm-schedules/{sched.GetProperty("id").GetGuid()}"));
        Assert.Equal(Iso(sat.AddDays(7)), detail.GetProperty("nextDueDate").GetString()); // cadence keeps the Saturday
    }

    [SkippableFact]
    public async Task No_stacking_skips_while_previous_work_is_open()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset, weekly, _) = await SetupAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await TestSettings.SetSettingsAsync(c, s => s["pmNoStacking"] = true);
        var sched = await Ok(await c.PostAsJsonAsync("/api/pm-schedules", Schedule(site, asset, weekly, today.AddDays(-14))));

        var gen = await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(today) }));
        Assert.Equal(1, gen.GetProperty("created").GetInt32());
        var results = gen.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        Assert.Null(Skipped(results[0]));
        Assert.Equal("Skipped: the previous work order is still open", Skipped(results[1]));
        Assert.Equal("Skipped: the previous work order is still open", Skipped(results[2]));
        var detail = await Ok(await c.GetAsync($"/api/pm-schedules/{sched.GetProperty("id").GetGuid()}"));
        Assert.Equal(Iso(today.AddDays(7)), detail.GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Settings_reject_a_week_with_no_working_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, _, _, _, _) = await SetupAsync();
        var r = await TestSettings.TrySetSettingsAsync(c, s => s["pmNonWorkingDays"] = new JsonArray(Enum.GetNames<DayOfWeek>().Select(n => (JsonNode?)n).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("settings.pmNonWorkingDays", await r.Content.ReadAsStringAsync());
    }
}
