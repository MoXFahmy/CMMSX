using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmSpreadTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<List<Guid>> AssetsAsync(HttpClient c, Guid site, int n)
    {
        var list = new List<Guid>();
        for (var i = 0; i < n; i++) list.Add(await AssetAsync(c, site, $"Unit {i + 1:00}"));
        return list;
    }

    private static List<JsonElement> Made(JsonElement results) =>
        results.EnumerateArray().Where(r => r.GetProperty("workOrderId").ValueKind == JsonValueKind.String || r.GetProperty("groupId").ValueKind == JsonValueKind.String).ToList();

    [SkippableFact]
    public async Task Yearly_pm_on_12_assets_in_12_parts_does_one_a_month()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var assets = await AssetsAsync(org.Admin, org.Site, 12);
        var start = Today.AddDays(-61);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "Y"), start, assets: [.. assets],
            extra: new() { ["unit"] = "Year", ["spreadParts"] = 12 })));
        var made = Made(await GenerateAsync(org.Admin, Today, pm));
        var days = start.AddYears(1).DayNumber - start.DayNumber;
        Assert.Equal([Iso(start), Iso(start.AddDays(days / 12)), Iso(start.AddDays(2 * days / 12))], made.Select(r => r.GetProperty("dueDate").GetString()));
        for (var k = 0; k < 3; k++)
        {
            var wo = made[k].GetProperty("workOrderId").GetGuid();
            var covered = await DbAsync(fx, db => db.WorkOrderAssets.IgnoreQueryFilters().Where(a => a.WorkOrderId == wo).Select(a => a.AssetId).ToListAsync());
            Assert.Equal([assets[k]], covered);
        }
        Assert.Equal(Iso(start.AddDays(3 * days / 12)), (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Spread_with_group_mode_makes_a_group_per_part()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var assets = await AssetsAsync(org.Admin, org.Site, 4);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "G"), Today, assets: [.. assets],
            extra: new() { ["spreadParts"] = 2, ["multiAssetMode"] = "Group" })));
        var group = Made(await GenerateAsync(org.Admin, Today, pm)).Single().GetProperty("groupId").GetGuid();
        var children = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).Select(w => w.AssetId!.Value).ToListAsync());
        Assert.Equal(assets.Take(2).OrderBy(x => x), children.OrderBy(x => x));
    }

    [SkippableFact]
    public async Task Every_nth_counts_periods_with_spread()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var assets = await AssetsAsync(org.Admin, org.Site, 2);
        var routine = await TemplateAsync(org.Admin, "R", ["Routine"]);
        var big = await TemplateAsync(org.Admin, "N", ["Overhaul"]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today.AddMonths(-2), assets: [.. assets],
            extra: new() { ["spreadParts"] = 2, ["extraTasks"] = new[] { new { pmTaskId = big, everyNth = 2 } } })));
        var made = Made(await GenerateAsync(org.Admin, Today, pm));
        Assert.Equal(5, made.Count);
        Assert.Equal(["R", "R", "N", "N", "R"], made.Select(r => r.GetProperty("taskName").GetString()!.Replace("Template ", "")));
    }

    [SkippableFact]
    public async Task Spread_is_refused_with_calendars()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var assets = await AssetsAsync(org.Admin, org.Site, 3);
        var cal = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "C", name = "C", kind = "Dates", dates = new[] { Iso(Today.AddDays(5)) } })));
        var body = Schedule(org.Site, await TemplateAsync(org.Admin, "S"), Today, assets: [.. assets], extra: new() { ["spreadParts"] = 2, ["calendarIds"] = new[] { cal } });
        Assert.Contains("Spread needs a fixed interval, the Scheduled basis, and no calendars or extra meters.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules", body)));
        var tooMany = Schedule(org.Site, await TemplateAsync(org.Admin, "U"), Today, assets: [.. assets], extra: new() { ["spreadParts"] = 4 });
        Assert.Contains("spreadParts", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules", tooMany)));
    }
}
