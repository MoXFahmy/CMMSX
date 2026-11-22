using System.Net.Http.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmCalendarApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Calendars_can_be_made_listed_changed_and_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var dates = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new
        {
            code = "LOW", name = "Low season", kind = "Dates", frequency = 1, unit = "Month", dates = new[] { Iso(Today.AddDays(40)), Iso(Today.AddDays(10)), Iso(Today.AddDays(10)) },
        })));
        var every = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "Q", name = "Quarterly", kind = "Interval", frequency = 3, unit = "Month", anchorDate = Iso(Today) })));
        var list = (await Ok(await org.Admin.GetAsync("/api/pm-calendars"))).EnumerateArray().ToList();
        Assert.Equal(["LOW", "Q"], list.Select(c => c.GetProperty("code").GetString()));
        var low = await Ok(await org.Admin.GetAsync($"/api/pm-calendars/{dates}"));
        Assert.Equal([Iso(Today.AddDays(10)), Iso(Today.AddDays(40))], low.GetProperty("dates").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(Iso(Today.AddDays(10)), low.GetProperty("next").EnumerateArray().First().GetString());
        var q = await Ok(await org.Admin.GetAsync($"/api/pm-calendars/{every}"));
        Assert.Equal(12, q.GetProperty("next").GetArrayLength());
        await Ok(await org.Admin.PutAsJsonAsync($"/api/pm-calendars/{every}", new { code = "Q", name = "Every quarter", kind = "Interval", frequency = 3, unit = "Month", anchorDate = Iso(Today) }));
        Assert.Equal("Every quarter", (await Ok(await org.Admin.GetAsync($"/api/pm-calendars/{every}"))).GetProperty("name").GetString());
        Assert.True((await org.Admin.DeleteAsync($"/api/pm-calendars/{every}")).IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Calendars_hold_at_most_500_dates_and_need_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var many = Enumerable.Range(1, 501).Select(n => Iso(Today.AddDays(n))).ToArray();
        Assert.Contains("At most 500 dates.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "M", name = "Many", kind = "Dates", dates = many })));
        Assert.Contains("Add at least one date.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "N", name = "None", kind = "Dates", dates = Array.Empty<string>() })));
        Assert.Contains("anchorDate", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "I", name = "No anchor", kind = "Interval", frequency = 1, unit = "Month" })));
    }

    [SkippableFact]
    public async Task A_calendar_in_use_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var cal = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "C", name = "Cal", kind = "Dates", dates = new[] { Iso(Today.AddDays(5)) } })));
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today.AddDays(30), asset)));
        await DbAsync(fx, async db =>
        {
            db.PmScheduleCalendars.Add(new Core.Pm.PmScheduleCalendar { PmScheduleId = pm, ScheduleCalendarId = cal });
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Contains("In use by 1 PM(s).", await Bad(await org.Admin.DeleteAsync($"/api/pm-calendars/{cal}")));
        Assert.Equal(1, (await Ok(await org.Admin.GetAsync("/api/pm-calendars"))).EnumerateArray().Single().GetProperty("scheduleCount").GetInt32());
    }
}
