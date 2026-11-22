using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class PhoneStopTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<(Loop.Org Org, Guid Asset, Guid Wo)> JobOnAssetAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = org.Site, locationId = org.Location, status = "Active", criticality = "High", warrantyType = "None" })));
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix pump", siteId = org.Site, assetId = asset, priority = "High", type = "Breakdown", assigneeIds = new[] { me.Id } })));
        return (org, asset, wo);
    }

    private static async Task<JsonElement> WoAsync(HttpClient c, Guid wo) => await Ok(await c.GetAsync($"/api/work-orders/{wo}"));

    [SkippableFact]
    public async Task Start_and_end_a_stop_with_chosen_times()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, wo) = await JobOnAssetAsync();
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "downtime.start", new { assetId = asset, at = started, reason = "Seal leaking" }, wo)));
        var w = await WoAsync(org.Admin, wo);
        Assert.Equal(started.ToUnixTimeSeconds(), w.GetProperty("downtime").GetProperty("startedAt").GetDateTimeOffset().ToUnixTimeSeconds());
        var ended = DateTimeOffset.UtcNow.AddMinutes(-30);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "downtime.end", new { assetId = asset, at = ended }, wo)));
        w = await WoAsync(org.Admin, wo);
        Assert.Equal(ended.ToUnixTimeSeconds(), w.GetProperty("downtime").GetProperty("endedAt").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.Equal(JsonValueKind.Null, w.GetProperty("assetDownSince").ValueKind);
    }

    [SkippableFact]
    public async Task Future_times_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, wo) = await JobOnAssetAsync();
        var answer = await ActAsync(org.Admin, "downtime.start", new { assetId = asset, at = DateTimeOffset.UtcNow.AddHours(2) }, wo);
        Assert.Equal("rejected", Status(answer));
        Assert.Equal("The time can't be in the future.", Message(answer));
    }

    [SkippableFact]
    public async Task Ending_when_not_down_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, wo) = await JobOnAssetAsync();
        var answer = await ActAsync(org.Admin, "downtime.end", new { assetId = asset }, wo);
        Assert.Equal("rejected", Status(answer));
        Assert.Equal("The asset isn't down.", Message(answer));
    }

    [SkippableFact]
    public async Task Starting_when_already_down_joins_the_stop()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, wo) = await JobOnAssetAsync();
        await ActAsync(org.Admin, "downtime.start", new { assetId = asset }, wo);
        var again = await ActAsync(org.Admin, "downtime.start", new { assetId = asset }, wo);
        Assert.Equal("done", Status(again));
        Assert.Equal("The asset is already down.", Message(again));
        var stops = await Ok(await org.Admin.GetAsync($"/api/assets/{asset}/downtime"));
        Assert.Single(stops.GetProperty("stops").EnumerateArray());
    }
}
