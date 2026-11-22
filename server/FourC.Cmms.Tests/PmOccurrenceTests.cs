using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmOccurrenceTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> OccurrencesAsync(HttpClient c, Guid pm) => await Ok(await c.GetAsync($"/api/pm-schedules/{pm}/occurrences"));
    private static async Task<HttpResponseMessage> GenerateOneAsync(HttpClient c, Guid pm, DateOnly date) =>
        await c.PostAsJsonAsync($"/api/pm-schedules/{pm}/occurrences/generate", new { date = Iso(date) });

    private async Task<(Loop.Org Org, Guid Pm)> MonthlyAsync(DateOnly start)
    {
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), start, asset)));
        return (org, pm);
    }

    [SkippableFact]
    public async Task Lists_next_24_with_generated_marked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, pm) = await MonthlyAsync(Today.AddMonths(1));
        var dates = (await OccurrencesAsync(org.Admin, pm)).GetProperty("dates").EnumerateArray().ToList();
        Assert.Equal(24, dates.Count);
        Assert.Equal(Iso(Today.AddMonths(1)), dates[0].GetProperty("date").GetString());
        Assert.Equal(Iso(Today.AddMonths(24)), dates[23].GetProperty("date").GetString());
        Assert.All(dates, d => Assert.Equal("Planned", d.GetProperty("status").GetString()));
        Assert.Equal("Template T", dates[0].GetProperty("templateName").GetString());
    }

    [SkippableFact]
    public async Task Generate_a_later_occurrence_now()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, pm) = await MonthlyAsync(Today.AddMonths(1));
        var made = await Ok(await GenerateOneAsync(org.Admin, pm, Today.AddMonths(3)));
        var number = made.GetProperty("workOrderNumber").GetString();
        var third = (await OccurrencesAsync(org.Admin, pm)).GetProperty("dates").EnumerateArray().Single(d => d.GetProperty("date").GetString() == Iso(Today.AddMonths(3)));
        Assert.Equal(("Generated", number), (third.GetProperty("status").GetString(), third.GetProperty("workOrderNumber").GetString()));
        Assert.Equal(Iso(Today.AddMonths(1)), (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Out_of_order_occurrence_is_not_generated_twice()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, pm) = await MonthlyAsync(Today.AddMonths(1));
        await Ok(await GenerateOneAsync(org.Admin, pm, Today.AddMonths(3)));
        var results = (await GenerateAsync(org.Admin, Today.AddMonths(3), pm)).EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        Assert.Equal("Already generated", results[2].GetProperty("skipped").GetString());
        var detail = await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.Equal(Iso(Today.AddMonths(4)), detail.GetProperty("nextDueDate").GetString());
        Assert.Equal(3, await DbAsync(fx, db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.WorkOrders), w => w.PmScheduleId == pm)));
    }

    [SkippableFact]
    public async Task Generating_an_existing_occurrence_says_so()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, pm) = await MonthlyAsync(Today.AddMonths(1));
        var number = (await Ok(await GenerateOneAsync(org.Admin, pm, Today.AddMonths(1)))).GetProperty("workOrderNumber").GetString();
        Assert.Equal(Iso(Today.AddMonths(2)), (await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("nextDueDate").GetString());
        Assert.Contains($"This occurrence was already generated as {number}.", await Bad(await GenerateOneAsync(org.Admin, pm, Today.AddMonths(3).AddMonths(-2))));
        Assert.Contains("date", await Bad(await GenerateOneAsync(org.Admin, pm, Today.AddDays(3))));
    }

    [SkippableFact]
    public async Task Meter_projection_estimates_dates()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Generator");
        var meter = await MeterAsync(org.Admin, asset, "GEN");
        await Ok(await org.Admin.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 0, readAt = DateTimeOffset.UtcNow.AddDays(-10), confirm = true }));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 50, confirm = true }));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today, asset, extra: new()
        {
            ["type"] = "Meter", ["meterId"] = meter, ["meterTrigger"] = "Incremental", ["meterInterval"] = 100,
        })));
        var readings = (await OccurrencesAsync(org.Admin, pm)).GetProperty("readings").EnumerateArray().ToList();
        Assert.Equal(24, readings.Count);
        Assert.Equal((150m, Iso(Today.AddDays(20))), (readings[0].GetProperty("reading").GetDecimal(), readings[0].GetProperty("estimatedDate").GetString()));
        Assert.Equal((250m, Iso(Today.AddDays(40))), (readings[1].GetProperty("reading").GetDecimal(), readings[1].GetProperty("estimatedDate").GetString()));
    }

    [SkippableFact]
    public async Task Spread_pm_lists_its_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var assets = new List<Guid>();
        for (var i = 0; i < 12; i++) assets.Add(await AssetAsync(org.Admin, org.Site, $"Unit {i + 1:00}"));
        var start = Today.AddDays(1);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "Y"), start, assets: [.. assets],
            extra: new() { ["unit"] = "Year", ["spreadParts"] = 12 })));
        var days = start.AddYears(1).DayNumber - start.DayNumber;
        var dates = (await OccurrencesAsync(org.Admin, pm)).GetProperty("dates").EnumerateArray().Select(d => d.GetProperty("date").GetString()).Take(3).ToList();
        Assert.Equal([Iso(start), Iso(start.AddDays(days / 12)), Iso(start.AddDays(2 * days / 12))], dates);
    }
}
