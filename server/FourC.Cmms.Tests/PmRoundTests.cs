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

    private static object Round(Ctx x, IEnumerable<Guid> assetIds, DateOnly start, string type = "FixedInterval", Guid? assetId = null, object[]? extra = null) => new
    {
        name = "Feeder round", siteId = x.Site, assetId, assetIds, priority = "Medium", pmTaskId = x.Task, type, basis = "Scheduled", frequency = 1, unit = "Week",
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
}
