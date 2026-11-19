using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

/// <summary>PM rounds: one schedule covering an ordered list of assets (legacy 4CSQL multi-asset PM).</summary>
public class PmRoundTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts a 400 whose validation errors include <paramref name="key"/>.</summary>
    private static async Task Invalid(HttpResponseMessage r, string key)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("errors").TryGetProperty(key, out _), $"no '{key}' error in {body}");
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Ctx(HttpClient Http, Guid Site, Guid OtherSite, Guid A, Guid B, Guid C, Guid Elsewhere, Guid Task, Guid Monthly);

    private async Task<Guid> AssetAsync(HttpClient c, string name, Guid site) =>
        (await Ok(await c.PostAsJsonAsync("/api/assets", new { name, siteId = site, status = "Active", criticality = "Medium", warrantyType = "None" }))).GetProperty("id").GetGuid();

    private async Task<Ctx> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Round Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Site", isActive = true }))).GetProperty("id").GetGuid();
        var other = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "O", name = "Other site", isActive = true }))).GetProperty("id").GetGuid();
        var task = (await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code = "W1", name = "Weekly check", steps = new[] { "Look", "Listen" } }))).GetProperty("id").GetGuid();
        var monthly = (await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code = "M1", name = "Monthly service", steps = new[] { "Grease" } }))).GetProperty("id").GetGuid();
        return new Ctx(c, site, other, await AssetAsync(c, "Feeder A", site), await AssetAsync(c, "Feeder B", site), await AssetAsync(c, "Feeder C", site),
            await AssetAsync(c, "Pump elsewhere", other), task, monthly);
    }

    private static object Round(Ctx x, IEnumerable<Guid> assetIds, DateOnly start, string type = "FixedInterval", Guid? assetId = null, object[]? extra = null, string? rowVersion = null) => new
    {
        rowVersion, name = "Feeder round", siteId = x.Site, assetId, assetIds, priority = "Medium", pmTaskId = x.Task, type, basis = "Scheduled", frequency = 1, unit = "Week",
        workPeriodDays = 1, wholeMonth = false, leadDays = 0, startDate = Iso(start), timeBased = false, extraTasks = extra ?? [],
        meterTrigger = type == "Meter" ? "Incremental" : null, meterInterval = type == "Meter" ? 50 : (int?)null,
    };

    private static List<Guid> Ids(JsonElement array) => array.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();

    [SkippableFact]
    public async Task Round_schedule_saves_its_ordered_asset_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.C, x.A, x.B], Today)))).GetProperty("id").GetGuid();

        var detail = await Ok(await x.Http.GetAsync($"/api/pm-schedules/{id}"));
        Assert.Equal([x.C, x.A, x.B], Ids(detail.GetProperty("assets")));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("assetId").ValueKind);

        var list = await Ok(await x.Http.GetAsync($"/api/pm-schedules?assetId={x.A}"));
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(id, row.GetProperty("id").GetGuid());
        Assert.Equal(3, row.GetProperty("assetCount").GetInt32());
    }

    [SkippableFact]
    public async Task Round_schedule_rejects_bad_lists()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        await Invalid(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.Elsewhere], Today)), "assetIds");
        await Invalid(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.A], Today)), "assetIds");
        await Invalid(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()), Today)), "assetIds");
        await Invalid(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today, type: "Meter")), "assetIds");
        await Invalid(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today, assetId: x.C)), "assetId");
    }

    [SkippableFact]
    public async Task Round_can_be_turned_back_into_a_single_asset_schedule()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today)))).GetProperty("id").GetGuid();
        await Ok(await x.Http.PutAsJsonAsync($"/api/pm-schedules/{id}", Round(x, [], Today, assetId: x.A)));

        var detail = await Ok(await x.Http.GetAsync($"/api/pm-schedules/{id}"));
        Assert.Empty(detail.GetProperty("assets").EnumerateArray());
        Assert.Equal(x.A, detail.GetProperty("assetId").GetGuid());
    }
    private static async Task<JsonElement> Generate(Ctx x, DateOnly until) =>
        await Ok(await x.Http.PostAsJsonAsync("/api/pm/generate", new { until = Iso(until) }));

    private static JsonElement ResultFor(JsonElement gen, Guid scheduleId) =>
        gen.GetProperty("results").EnumerateArray().First(r => r.GetProperty("scheduleId").GetGuid() == scheduleId);

    [SkippableFact]
    public async Task Round_generates_one_work_order_with_steps_per_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today)));

        var gen = await Generate(x, Today);
        Assert.Equal(1, gen.GetProperty("created").GetInt32());
        var wo = await Ok(await x.Http.GetAsync($"/api/work-orders/{gen.GetProperty("results")[0].GetProperty("workOrderId").GetGuid()}"));
        Assert.Equal(JsonValueKind.Null, wo.GetProperty("assetId").ValueKind);
        Assert.Equal([x.A, x.B], Ids(wo.GetProperty("assets")));
        var tasks = wo.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal([x.A, x.A, x.B, x.B], tasks.Select(t => t.GetProperty("assetId").GetGuid()));
        Assert.Equal(["Look", "Listen", "Look", "Listen"], tasks.Select(t => t.GetProperty("description").GetString()));
    }

    [SkippableFact]
    public async Task Round_leaves_out_unavailable_and_moved_assets()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B, x.C], Today)));
        await Ok(await x.Http.PostAsJsonAsync("/api/assets/bulk", new { ids = new[] { x.B }, status = "Disposed" }));
        await Ok(await x.Http.PostAsJsonAsync($"/api/assets/{x.C}/transfer", new { siteId = x.OtherSite, reason = "moved" }));
        var d = await AssetAsync(x.Http, "Feeder D", x.Site);
        var lonely = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [d], Today)))).GetProperty("id").GetGuid();
        Assert.True((await x.Http.DeleteAsync($"/api/assets/{d}")).IsSuccessStatusCode);

        var gen = await Generate(x, Today);
        Assert.Equal(1, gen.GetProperty("created").GetInt32());
        var made = gen.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("workOrderId").ValueKind == JsonValueKind.String);
        var wo = await Ok(await x.Http.GetAsync($"/api/work-orders/{made.GetProperty("workOrderId").GetGuid()}"));
        Assert.Equal([x.A], Ids(wo.GetProperty("assets")));
        var skipped = ResultFor(gen, lonely);
        Assert.Equal("No active assets in the list", skipped.GetProperty("skipped").GetString());
        Assert.Equal(JsonValueKind.Null, skipped.GetProperty("workOrderId").ValueKind);
    }

    [SkippableFact]
    public async Task Round_rotates_the_heavier_task_like_single_asset_schedules()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today.AddDays(-7), extra: [new { pmTaskId = x.Monthly, everyNth = 2 }])));

        var gen = await Generate(x, Today);
        Assert.Equal(2, gen.GetProperty("created").GetInt32());
        var second = await Ok(await x.Http.GetAsync($"/api/work-orders/{gen.GetProperty("results")[1].GetProperty("workOrderId").GetGuid()}"));
        var tasks = second.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(["Grease", "Grease"], tasks.Select(t => t.GetProperty("description").GetString()));
        Assert.Equal([x.A, x.B], tasks.Select(t => t.GetProperty("assetId").GetGuid()));
    }
    private async Task<(JsonElement Wo, Guid Id)> RoundWorkOrderAsync(Ctx x, Guid[] assets)
    {
        await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, assets, Today)));
        var id = (await Generate(x, Today)).GetProperty("results")[0].GetProperty("workOrderId").GetGuid();
        return (await Ok(await x.Http.GetAsync($"/api/work-orders/{id}")), id);
    }

    [SkippableFact]
    public async Task Round_work_order_shows_in_each_assets_history_and_open_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var (_, round) = await RoundWorkOrderAsync(x, [x.A, x.B]);
        var single = (await Ok(await x.Http.PostAsJsonAsync("/api/work-orders", new { title = "Fix feeder A", type = "Corrective", priority = "High", siteId = x.Site, assetId = x.A }))).GetProperty("id").GetGuid();

        List<Guid> ListFor(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([round], ListFor(await Ok(await x.Http.GetAsync($"/api/work-orders?assetId={x.B}"))));
        var forA = ListFor(await Ok(await x.Http.GetAsync($"/api/work-orders?assetId={x.A}")));
        Assert.Equal(2, forA.Count);
        Assert.Contains(round, forA);
        Assert.Contains(single, forA);

        var open = await Ok(await x.Http.GetAsync($"/api/assets/{x.B}/open-work"));
        Assert.Contains(round, open.GetProperty("openWorkOrders").EnumerateArray().Select(w => w.GetProperty("id").GetGuid()));
    }

    [SkippableFact]
    public async Task Steps_record_readings_and_remarks()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var (wo, id) = await RoundWorkOrderAsync(x, [x.A, x.B]);
        var task = wo.GetProperty("tasks")[2].GetProperty("id").GetGuid();

        await Ok(await x.Http.PutAsJsonAsync($"/api/work-orders/{id}/tasks/{task}", new { reading = "72 °C", remarks = "تسريب بسيط" }));
        var step = (await Ok(await x.Http.GetAsync($"/api/work-orders/{id}"))).GetProperty("tasks")[2];
        Assert.Equal("72 °C", step.GetProperty("reading").GetString());
        Assert.Equal("تسريب بسيط", step.GetProperty("remarks").GetString());
        Assert.False(step.GetProperty("isDone").GetBoolean());

        await Invalid(await x.Http.PutAsJsonAsync($"/api/work-orders/{id}/tasks/{task}", new { reading = new string('9', 101) }), "reading");
        await Invalid(await x.Http.PutAsJsonAsync($"/api/work-orders/{id}/tasks/{task}", new { remarks = new string('ت', 2001) }), "remarks");
        await Ok(await x.Http.PutAsJsonAsync($"/api/work-orders/{id}/tasks/{task}", new { reading = "" }));
        Assert.Equal(JsonValueKind.Null, (await Ok(await x.Http.GetAsync($"/api/work-orders/{id}"))).GetProperty("tasks")[2].GetProperty("reading").ValueKind);
    }
    [SkippableFact]
    public async Task Asset_lookup_can_be_limited_to_the_rounds_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var found = (await Ok(await x.Http.GetAsync($"/api/assets/lookup?siteId={x.OtherSite}"))).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([x.Elsewhere], found);
    }
    [SkippableFact]
    public async Task Saving_without_assetIds_keeps_the_round()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today)))).GetProperty("id").GetGuid();
        // A client that predates rounds sends no assetIds at all: the list must be left as it is.
        var old = new
        {
            name = "Feeder round renamed", siteId = x.Site, priority = "Medium", pmTaskId = x.Task, type = "FixedInterval", basis = "Scheduled", frequency = 1, unit = "Week",
            workPeriodDays = 1, wholeMonth = false, leadDays = 0, startDate = Iso(Today), timeBased = false, extraTasks = Array.Empty<object>(),
        };
        await Ok(await x.Http.PutAsJsonAsync($"/api/pm-schedules/{id}", old));
        var detail = await Ok(await x.Http.GetAsync($"/api/pm-schedules/{id}"));
        Assert.Equal([x.A, x.B], Ids(detail.GetProperty("assets")));
        Assert.Equal("Feeder round renamed", detail.GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task Reordering_a_round_is_concurrency_checked_and_audited()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today)))).GetProperty("id").GetGuid();
        var rv = (await Ok(await x.Http.GetAsync($"/api/pm-schedules/{id}"))).GetProperty("rowVersion").GetString();

        await Ok(await x.Http.PutAsJsonAsync($"/api/pm-schedules/{id}", Round(x, [x.B, x.A], Today, rowVersion: rv)));
        Assert.Equal(HttpStatusCode.Conflict, (await x.Http.PutAsJsonAsync($"/api/pm-schedules/{id}", Round(x, [x.A, x.B], Today, rowVersion: rv))).StatusCode);

        var audit = await Ok(await x.Http.GetAsync($"/api/audit?entityType=PmSchedule&entityId={id}"));
        Assert.Contains(audit.EnumerateArray(), e => e.GetProperty("action").GetString() == "Updated" && (e.GetProperty("changes").GetString() ?? "").Contains("assets", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task Saving_a_round_with_a_moved_asset_names_that_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today)))).GetProperty("id").GetGuid();
        await Ok(await x.Http.PostAsJsonAsync($"/api/assets/{x.B}/transfer", new { siteId = x.OtherSite, reason = "moved" }));
        var number = (await Ok(await x.Http.GetAsync($"/api/assets/{x.B}"))).GetProperty("assetNumber").GetString()!;

        var r = await x.Http.PutAsJsonAsync($"/api/pm-schedules/{id}", Round(x, [x.A, x.B], Today));
        await Invalid(r, "assetIds");
        Assert.Contains(number, await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Round_with_every_asset_skipped_is_skipped_not_piled_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var x = await SetupAsync();
        await TestSettings.SetSettingsAsync(x.Http, s => s["pmSkipAssetStatuses"] = new System.Text.Json.Nodes.JsonArray("UnderRepair"));
        await Ok(await x.Http.PostAsJsonAsync("/api/assets/bulk", new { ids = new[] { x.A, x.B }, status = "UnderRepair" }));
        var id = (await Ok(await x.Http.PostAsJsonAsync("/api/pm-schedules", Round(x, [x.A, x.B], Today.AddDays(-7))))).GetProperty("id").GetGuid();

        var gen = await Ok(await x.Http.PostAsJsonAsync("/api/pm/generate", new { until = Iso(Today) }));
        Assert.Equal(0, gen.GetProperty("created").GetInt32());
        var results = gen.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Skipped: every asset on the round has a status that skips PM work", r.GetProperty("skipped").GetString()));
        Assert.Equal(Iso(Today.AddDays(7)), (await Ok(await x.Http.GetAsync($"/api/pm-schedules/{id}"))).GetProperty("nextDueDate").GetString());
    }
}
