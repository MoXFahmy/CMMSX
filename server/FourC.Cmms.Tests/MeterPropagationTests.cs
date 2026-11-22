using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class MeterPropagationTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> ReadAsync(HttpClient c, Guid meter, decimal value, DateTimeOffset? at = null) =>
        await Ok(await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value, readAt = at, confirm = true }));

    private static async Task<HttpResponseMessage> FollowAsync(HttpClient c, Guid meter, Guid asset, string code, Guid? source, string unit = "h") =>
        await c.PutAsJsonAsync($"/api/meters/{meter}", new { code, name = $"Meter {code}", assetId = asset, unit, isActive = true, sourceMeterId = source, managesSource = true });

    private static async Task<decimal?> CurrentAsync(HttpClient c, Guid meter)
    {
        var m = (await Ok(await c.GetAsync("/api/meters"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == meter);
        return m.GetProperty("currentReading").ValueKind == JsonValueKind.Number ? m.GetProperty("currentReading").GetDecimal() : null;
    }

    [SkippableFact]
    public async Task Reading_adds_the_increase_to_linked_meters()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var truck = await AssetAsync(org.Admin, org.Site, "Truck");
        var engine = await AssetAsync(org.Admin, org.Site, "Engine");
        var parent = await MeterAsync(org.Admin, truck, "TRK");
        var child = await MeterAsync(org.Admin, engine, "ENG");
        await ReadAsync(org.Admin, parent, 100);
        await ReadAsync(org.Admin, child, 40);
        await Ok(await FollowAsync(org.Admin, child, engine, "ENG", parent));
        await ReadAsync(org.Admin, parent, 150);
        Assert.Equal(90m, await CurrentAsync(org.Admin, child));
        var latest = (await Ok(await org.Admin.GetAsync($"/api/meters/{child}/readings"))).EnumerateArray().First();
        Assert.Equal((90m, "From TRK", "Propagated"), (latest.GetProperty("value").GetDecimal(), latest.GetProperty("note").GetString(), latest.GetProperty("source").GetString()));
        var listed = (await Ok(await org.Admin.GetAsync("/api/meters"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == child);
        Assert.Equal(parent, listed.GetProperty("sourceMeterId").GetGuid());
    }

    [SkippableFact]
    public async Task Chain_propagates_and_runs_pms()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Truck");
        var p = await MeterAsync(org.Admin, asset, "P");
        var c = await MeterAsync(org.Admin, asset, "C");
        var g = await MeterAsync(org.Admin, asset, "G");
        await ReadAsync(org.Admin, p, 100);
        await ReadAsync(org.Admin, c, 40);
        await ReadAsync(org.Admin, g, 45);
        await Ok(await FollowAsync(org.Admin, c, asset, "C", p));
        await Ok(await FollowAsync(org.Admin, g, asset, "G", c));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today, asset, extra: new()
        {
            ["type"] = "Meter", ["meterId"] = g, ["meterTrigger"] = "Incremental", ["meterInterval"] = 50,
        })));
        var answer = await ReadAsync(org.Admin, p, 160);
        Assert.Equal(105m, await CurrentAsync(org.Admin, g));
        Assert.Contains(answer.GetProperty("generated").EnumerateArray(), r => r.GetProperty("scheduleId").GetGuid() == pm && r.GetProperty("workOrderId").ValueKind == JsonValueKind.String);
    }

    [SkippableFact]
    public async Task Resets_and_late_readings_are_not_propagated()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Truck");
        var p = await MeterAsync(org.Admin, asset, "P");
        var c = await MeterAsync(org.Admin, asset, "C");
        await ReadAsync(org.Admin, p, 100, DateTimeOffset.UtcNow.AddHours(-1));
        await ReadAsync(org.Admin, c, 40);
        await Ok(await FollowAsync(org.Admin, c, asset, "C", p));
        await ReadAsync(org.Admin, p, 300, DateTimeOffset.UtcNow.AddHours(-3)); // older than the latest
        await ReadAsync(org.Admin, p, 80); // meter reset
        Assert.Equal(40m, await CurrentAsync(org.Admin, c));
    }

    [SkippableFact]
    public async Task Different_unit_and_loops_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Truck");
        var hours = await MeterAsync(org.Admin, asset, "H");
        var km = await MeterAsync(org.Admin, asset, "K", "km");
        var other = await MeterAsync(org.Admin, asset, "O");
        Assert.Contains("A meter can only follow a meter with the same unit.", await Bad(await FollowAsync(org.Admin, km, asset, "K", hours, "km")));
        await Ok(await FollowAsync(org.Admin, other, asset, "O", hours));
        Assert.Contains("Meters can't follow each other in a loop.", await Bad(await FollowAsync(org.Admin, hours, asset, "H", other)));
        Assert.Contains("Meters can't follow each other in a loop.", await Bad(await FollowAsync(org.Admin, hours, asset, "H", hours)));
    }

    [SkippableFact]
    public async Task A_followed_meter_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Truck");
        var p = await MeterAsync(org.Admin, asset, "P");
        var c = await MeterAsync(org.Admin, asset, "C");
        await Ok(await FollowAsync(org.Admin, c, asset, "C", p));
        Assert.Contains("Other meters follow this one. Unlink them first.", await Bad(await org.Admin.DeleteAsync($"/api/meters/{p}")));
    }
}
