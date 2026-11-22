using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>The PM annual plan (0.14.0): past months from the work generated, this and later months from the forecast.</summary>
public class AnnualPlanTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly DateOnly Now = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>A generated PM work order written straight to the database.</summary>
    private Task AddAsync(Loop.Org org, Guid pm, DateOnly due, DateTimeOffset? completed, WorkOrderStatus? status = null) => DbAsync(fx, db =>
    {
        db.WorkOrders.Add(new WorkOrder
        {
            TenantId = org.Tenant, Number = $"P-{Guid.NewGuid():N}"[..20], Title = "PM work", SiteId = org.Site, Type = WorkType.Preventive, PmScheduleId = pm, PmDueDate = due,
            DueDate = due, Status = status ?? (completed is null ? WorkOrderStatus.Open : WorkOrderStatus.Completed), CompletedAt = completed,
        });
        return db.SaveChangesAsync();
    });

    private static async Task<List<JsonElement>> PlanAsync(HttpClient c, int year) =>
        (await Ok(await c.GetAsync($"/api/reports/pm-annual-plan?year={year}"))).GetProperty("rows").EnumerateArray().ToList();

    private static (int Done, int Late, int Missed, int Cancelled, int Planned) Cell(JsonElement row, int month) => row.GetProperty($"m{month}") is { ValueKind: JsonValueKind.Object } c
        ? (c.GetProperty("done").GetInt32(), c.GetProperty("late").GetInt32(), c.GetProperty("missed").GetInt32(), c.GetProperty("cancelled").GetInt32(), c.GetProperty("planned").GetInt32())
        : (0, 0, 0, 0, 0);

    [SkippableFact]
    public async Task Annual_plan_shows_what_happened_and_what_is_planned()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (a, b) = (await AssetAsync(org.Admin, org.Site, "Pump A"), await AssetAsync(org.Admin, org.Site, "Pump B"));
        var task = await TemplateAsync(org.Admin, "MPM");
        var later = Now.AddYears(2); // nothing generated automatically during the test
        var single = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, task, later, a)));
        var round = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, task, later, assets: [a, b])));
        Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, task, later, b))); // nothing in last year: not listed
        var y = Now.Year - 1;
        DateTimeOffset On(int month, int day) => new(y, month, day, 12, 0, 0, TimeSpan.Zero);
        await AddAsync(org, single, new DateOnly(y, 1, 28), On(1, 28));
        await AddAsync(org, single, new DateOnly(y, 2, 26), On(3, 2));
        await AddAsync(org, single, new DateOnly(y, 3, 28), null);
        await AddAsync(org, single, new DateOnly(y, 4, 28), null, WorkOrderStatus.Cancelled);
        await AddAsync(org, round, new DateOnly(y, 5, 10), On(5, 10));
        var rows = await PlanAsync(org.Admin, y);
        Assert.Equal(2, rows.Count);
        var s = rows.Single(r => r.GetProperty("pmId").GetGuid() == single);
        Assert.Equal([(1, 0, 0, 0, 0), (0, 1, 0, 0, 0), (0, 0, 1, 0, 0), (0, 0, 0, 1, 0), (0, 0, 0, 0, 0)], new[] { 1, 2, 3, 4, 5 }.Select(m => Cell(s, m)));
        var r = rows.Single(x => x.GetProperty("pmId").GetGuid() == round);
        Assert.Equal("Round of 2 assets", r.GetProperty("asset").GetString());
        Assert.Equal((1, 0, 0, 0, 0), Cell(r, 5));
    }

    [SkippableFact]
    public async Task Annual_plan_this_month_shows_done_and_still_planned()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "WPM"), Now.AddDays(1), asset,
            extra: new() { ["unit"] = "Year" })));
        var first = new DateOnly(Now.Year, Now.Month, 1);
        await AddAsync(org, pm, first, new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(10));
        var row = (await PlanAsync(org.Admin, Now.AddDays(1).Year)).Single(r => r.GetProperty("pmId").GetGuid() == pm);
        if (Now.AddDays(1).Month == Now.Month) Assert.Equal((1, 0, 0, 0, 1), Cell(row, Now.Month));
        else Assert.Equal((0, 0, 0, 0, 1), Cell(row, Now.AddDays(1).Month)); // the last day of a month: tomorrow is next month's
    }

    [SkippableFact]
    public async Task Annual_plan_keeps_year_edges_in_the_organizations_time_zone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Lift");
        var year = Now.Month == 12 && Now.Day == 31 ? Now.Year + 1 : Now.Year;
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "YPM"), new DateOnly(year, 12, 31), asset, extra: new() { ["unit"] = "Year" })));
        var row = (await PlanAsync(org.Admin, year)).Single(r => r.GetProperty("pmId").GetGuid() == pm);
        Assert.Equal((0, 0, 0, 0, 1), Cell(row, 12));
        Assert.Equal((0, 0, 0, 0, 0), Cell(row, 11));
    }

    [SkippableFact]
    public async Task Year_is_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("Choose a year from 2000 to 2100.", await Bad(await org.Admin.GetAsync("/api/reports/pm-annual-plan?year=1999")));
    }
}
