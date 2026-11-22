using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class OutOfLimitsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task ReadAsync(HttpClient c, Guid point, Guid asset, decimal value) =>
        await Ok(await c.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value }));

    private static decimal Kpi(JsonElement report, string label) =>
        report.GetProperty("summary").EnumerateArray().Single(k => k.GetProperty("label").GetString() == label).GetProperty("value").GetDecimal();

    [SkippableFact]
    public async Task Out_of_limits_lists_alert_and_alarm_readings()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        foreach (var v in new[] { 20m, 29m, 33m }) await ReadAsync(org.Admin, point, asset, v);
        var report = await Ok(await org.Admin.GetAsync("/api/reports/out-of-limits"));
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([33m, 29m], rows.Select(r => r.GetProperty("value").GetDecimal()));
        Assert.Equal(["Alarm high", "Alert high"], rows.Select(r => r.GetProperty("level").GetString()));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.GetProperty("followUp").GetString())));
        Assert.Equal((1m, 1m, 2m), (Kpi(report, "Alarms"), Kpi(report, "Alerts"), Kpi(report, "Follow-ups raised")));
    }

    [SkippableFact]
    public async Task Out_of_limits_follows_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var there = await AssetAsync(org.Admin, other, "AHU-9");
        var point = await PointAsync(org.Admin, "SAT");
        await ReadAsync(org.Admin, point, there, 33);
        var local = await UserAsync(fx, org.Admin, "Maintenance Manager", siteIds: [org.Site]);
        Assert.Empty((await Ok(await local.Client.GetAsync("/api/reports/out-of-limits"))).GetProperty("rows").EnumerateArray());
        Assert.Single((await Ok(await org.Admin.GetAsync("/api/reports/out-of-limits"))).GetProperty("rows").EnumerateArray());
    }

    [SkippableFact]
    public async Task Asset_measurements_return_series_with_levels()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT", new() { ["assetIds"] = new[] { asset } });
        await PointAsync(org.Admin, "UNUSED");
        await ReadAsync(org.Admin, point, asset, 20);
        await ReadAsync(org.Admin, point, asset, 33);
        var points = (await Ok(await org.Admin.GetAsync($"/api/assets/{asset}/measurements"))).EnumerateArray().ToList();
        var sat = Assert.Single(points);
        Assert.Equal(("SAT", 28m), (sat.GetProperty("code").GetString(), sat.GetProperty("alertHigh").GetDecimal()));
        var readings = sat.GetProperty("readings").EnumerateArray().ToList();
        Assert.Equal([20m, 33m], readings.Select(r => r.GetProperty("value").GetDecimal()));
        Assert.Equal(["Normal", "AlarmHigh"], readings.Select(r => r.GetProperty("level").GetString()));
    }
}
