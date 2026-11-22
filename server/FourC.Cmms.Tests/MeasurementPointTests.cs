using System.Net;
using System.Net.Http.Json;
using FourC.Cmms.Core.Pm;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class MeasurementPointTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Points_can_be_made_listed_changed_and_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var b = await AssetAsync(org.Admin, org.Site, "AHU-2");
        var id = await PointAsync(org.Admin, "SAT", new() { ["assetIds"] = new[] { a, b } });
        var row = (await Ok(await org.Admin.GetAsync("/api/measurement-points"))).EnumerateArray().Single(r => Id(r) == id);
        Assert.Equal(("SAT", 2), (row.GetProperty("code").GetString(), row.GetProperty("assetCount").GetInt32()));
        var detail = await Ok(await org.Admin.GetAsync($"/api/measurement-points/{id}"));
        Assert.Equal(32m, detail.GetProperty("alarmHigh").GetDecimal());
        var body = Point("SAT", new() { ["name"] = "Supply air", ["assetIds"] = new[] { a }, ["rowVersion"] = detail.GetProperty("rowVersion").GetString() });
        await Ok(await org.Admin.PutAsJsonAsync($"/api/measurement-points/{id}", body));
        detail = await Ok(await org.Admin.GetAsync($"/api/measurement-points/{id}"));
        Assert.Equal(("Supply air", 1), (detail.GetProperty("name").GetString(), detail.GetProperty("assets").GetArrayLength()));
        Assert.Single((await Ok(await org.Admin.GetAsync($"/api/measurement-points?assetId={a}"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await org.Admin.DeleteAsync($"/api/measurement-points/{id}")).StatusCode);
        Assert.DoesNotContain((await Ok(await org.Admin.GetAsync("/api/measurement-points"))).EnumerateArray(), r => Id(r) == id);
    }

    [SkippableFact]
    public async Task Level_order_and_range_are_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("Levels must go up: alarm low, alert low, target, alert high, alarm high.",
            await Bad(await org.Admin.PostAsJsonAsync("/api/measurement-points", Point("A", new() { ["alertHigh"] = 40 }))));
        Assert.Contains("The valid range's minimum must be below its maximum.",
            await Bad(await org.Admin.PostAsJsonAsync("/api/measurement-points", Point("B", new() { ["validMin"] = 60 }))));
        Assert.Contains("Levels must be inside the valid range.",
            await Bad(await org.Admin.PostAsJsonAsync("/api/measurement-points", Point("C", new() { ["alarmHigh"] = 70 }))));
        await PointAsync(org.Admin, "D");
        Assert.Contains("Another point uses this code.", await Bad(await org.Admin.PostAsJsonAsync("/api/measurement-points", Point("D"))));
        Assert.Contains("Unknown work template.",
            await Bad(await org.Admin.PostAsJsonAsync("/api/measurement-points", Point("E", new() { ["alarmHighTemplateId"] = Guid.NewGuid() }))));
    }

    [SkippableFact]
    public async Task A_point_used_by_a_template_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var point = await PointAsync(org.Admin, "SAT");
        var template = await TemplateAsync(org.Admin, "T1", ["Temperature"]);
        await DbAsync(fx, async db =>
        {
            var step = await db.PmTaskSteps.IgnoreQueryFilters().SingleAsync(s => s.PmTaskId == template);
            step.Type = StepType.Number; step.MeasurementPointId = point;
            return await db.SaveChangesAsync();
        });
        Assert.Contains("In use by 1 work template(s).", await Bad(await org.Admin.DeleteAsync($"/api/measurement-points/{point}")));
    }

    [SkippableFact]
    public async Task Direct_reading_stores_its_level()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        await Ok(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 31.04m, note = "Hot" }));
        var reading = (await ReadingsAsync(org.Admin, point, asset)).EnumerateArray().Single();
        Assert.Equal((31.0m, "AlertHigh"), (reading.GetProperty("value").GetDecimal(), reading.GetProperty("level").GetString()));
    }

    [SkippableFact]
    public async Task Each_point_keeps_its_readings_on_the_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var busy = await PointAsync(org.Admin, "SAT");
        var monthly = await PointAsync(org.Admin, "RAT");
        var now = DateTimeOffset.UtcNow;
        MeasurementReading Reading(Guid point, DateTimeOffset at) => new()
        {
            TenantId = org.Tenant, PointId = point, AssetId = asset, SiteId = org.Site, Value = 20, ReadAt = at, Level = MeasureLevel.Normal, ByName = "Admin",
        };
        await DbAsync(fx, db =>
        {
            db.MeasurementReadings.AddRange(Enumerable.Range(0, 401).Select(i => Reading(busy, now.AddMinutes(-i))));
            db.MeasurementReadings.AddRange(Reading(monthly, now.AddDays(-30)), Reading(monthly, now.AddDays(-60)));
            return db.SaveChangesAsync();
        });
        var shown = (await Ok(await org.Admin.GetAsync($"/api/assets/{asset}/measurements"))).EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("readings").GetArrayLength());
        Assert.Equal((200, 2), (shown["SAT"], shown["RAT"]));
    }

    [SkippableFact]
    public async Task Reading_outside_the_valid_range_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        Assert.Contains("Enter a value from 0 to 60 °C.", await Bad(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 61 })));
    }

    [SkippableFact]
    public async Task Readings_follow_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var here = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var there = await AssetAsync(org.Admin, other, "AHU-9");
        var point = await PointAsync(org.Admin, "SAT", new() { ["assetIds"] = new[] { here, there } });
        await Ok(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = here, value = 20 }));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = there, value = 21 }));
        var outsider = await UserAsync(fx, org.Admin, "Maintenance Manager", siteIds: [other]);
        var seen = (await ReadingsAsync(outsider.Client, point)).EnumerateArray().Select(r => r.GetProperty("value").GetDecimal()).ToList();
        Assert.Equal([21m], seen);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = here, value = 22 })).StatusCode);
        // The point's own page lists only the assets the person can see.
        Assert.Single((await Ok(await outsider.Client.GetAsync($"/api/measurement-points/{point}"))).GetProperty("assets").EnumerateArray());
    }

    [SkippableFact]
    public async Task Points_need_pm_manage_and_readings_meters_record()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        var tech = await UserAsync(fx, org.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.PostAsJsonAsync("/api/measurement-points", Point("T"))).StatusCode);
        await Ok(await tech.Client.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 20 }));
        var requester = await UserAsync(fx, org.Admin, "Requester");
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.Client.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 20 })).StatusCode);
    }
}
