using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

/// <summary>The reliability report on real stops and running time, and the Downtime log report (0.8.0).</summary>
public class DowntimeReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static readonly string[] AllDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private async Task<(HttpClient C, Guid Site, Guid Asset)> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Reports Co", fullName = "Rami Report", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "P", name = "Plant", isActive = true })));
        var asset = Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { assetNumber = "PR-1", name = "Press", siteId = site, status = "Active", criticality = "High",
            warrantyType = "None", runsHoursPerDay = 12, runsOn = AllDays })));
        return (c, site, asset);
    }

    private static string Range => $"from={DateTime.UtcNow.Date.AddDays(-7):yyyy-MM-dd}&to={DateTime.UtcNow.Date:yyyy-MM-dd}";

    [SkippableFact]
    public async Task Reliability_counts_stops_and_running_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset) = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        await Ok(await c.PostAsJsonAsync($"/api/assets/{asset}/downtime", new { startedAt = now.AddHours(-7), endedAt = now.AddHours(-1), reason = "Hydraulics" }));
        // Done the old way: "Asset was down" ticked with hours typed, no stop.
        var wo = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Jammed die", siteId = site, assetId = asset, type = "Breakdown", priority = "High", tasks = Array.Empty<string>() })));
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Cleared", assetWasDown = true, downtimeHours = 3 }));

        var report = await Ok(await c.GetAsync($"/api/reports/reliability?{Range}"));
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(asset, row.GetProperty("assetId").GetGuid());
        Assert.Equal(2, row.GetProperty("failures").GetInt32());
        Assert.Equal(9m, row.GetProperty("downtime").GetDecimal());
        Assert.Equal(4.5m, row.GetProperty("mttr").GetDecimal());
        // Planned: 8 days × 12 h = 96 h.
        Assert.Equal(43.5m, row.GetProperty("mtbf").GetDecimal());
        Assert.Equal(90.6m, row.GetProperty("availability").GetDecimal());
    }

    [SkippableFact]
    public async Task The_downtime_log_lists_each_stop()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset) = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        await Ok(await c.PostAsJsonAsync($"/api/assets/{asset}/downtime", new { startedAt = now.AddHours(-30), endedAt = now.AddHours(-28), reason = "Power cut" }));
        var request = await Ok(await c.PostAsJsonAsync("/api/work-requests", new { title = "Press stopped", siteId = site, assetId = asset, priority = "High", type = "Breakdown", assetIsDown = true }));

        var report = await Ok(await c.GetAsync($"/api/reports/downtime?{Range}"));
        Assert.Equal("Downtime log", report.GetProperty("title").GetString());
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Power cut", rows[0].GetProperty("reason").GetString());
        Assert.Equal(2m, rows[0].GetProperty("hours").GetDecimal());
        Assert.Equal("Press stopped", rows[1].GetProperty("reason").GetString());
        Assert.Equal(request.GetProperty("number").GetString(), rows[1].GetProperty("from").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("end").ValueKind);

        var catalog = await Ok(await c.GetAsync("/api/reports"));
        Assert.Contains(catalog.EnumerateArray(), r => r.GetProperty("key").GetString() == "downtime");
    }

    [SkippableFact]
    public async Task Work_done_during_a_stop_does_not_count_again()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset) = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        await Ok(await c.PostAsJsonAsync($"/api/assets/{asset}/downtime", new { startedAt = now.AddHours(-7), endedAt = now.AddHours(-1), reason = "Hydraulics" }));
        // The same incident, completed the old way ("Asset was down", hours typed) without being linked to the stop.
        var wo = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Hydraulic pump", siteId = site, assetId = asset, type = "Breakdown", priority = "High", tasks = Array.Empty<string>() })));
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Replaced the pump", assetWasDown = true, downtimeHours = 3,
            actualStart = now.AddHours(-5) }));

        var row = Assert.Single((await Ok(await c.GetAsync($"/api/reports/reliability?{Range}"))).GetProperty("rows").EnumerateArray());
        Assert.Equal(1, row.GetProperty("failures").GetInt32());
        Assert.Equal(6m, row.GetProperty("downtime").GetDecimal());
        Assert.Equal(6m, (await Ok(await c.GetAsync($"/api/assets/{asset}/downtime"))).GetProperty("downHours30").GetDecimal());
    }

    [SkippableFact]
    public async Task A_stop_shared_by_two_work_orders_counts_once_in_failure_analysis()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, asset) = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        var wos = new List<Guid>();
        foreach (var title in new[] { "Press stopped", "Press won't start" })
        {
            var r = Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", new { title, siteId = site, assetId = asset, priority = "High", type = "Breakdown", assetIsDown = true })));
            wos.Add((await Ok(await c.PostAsJsonAsync($"/api/work-requests/{r}/approve", new { }))).GetProperty("workOrderId").GetGuid());
        }
        var stop = Id((await Ok(await c.GetAsync($"/api/assets/{asset}/downtime"))).GetProperty("stops")[0]);
        await Ok(await c.PutAsJsonAsync($"/api/downtime/{stop}", new { startedAt = now.AddHours(-3), endedAt = (DateTimeOffset?)null, reason = "Press stopped" }));
        foreach (var wo in wos)
            await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Fixed" }));

        var pareto = await Ok(await c.GetAsync($"/api/reports/failure-pareto?{Range}"));
        var downtime = pareto.GetProperty("rows").EnumerateArray().Sum(r => r.GetProperty("downtime").GetDecimal());
        Assert.InRange(downtime, 2.9m, 3.1m);
    }
}
