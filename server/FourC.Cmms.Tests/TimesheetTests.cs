using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.AvailabilityTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: the daily timesheet and the week, over the same labour records the work order shows.</summary>
public class TimesheetTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly object[] EveryDay = Enum.GetValues<DayOfWeek>().Select(d => (object)new { day = d.ToString(), start = "08:00", end = "16:00" }).ToArray();

    private static async Task<Guid> JobAsync(HttpClient c, Guid site, string title, Guid[] who, DateOnly planned, Guid? team = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, siteId = site, type = "Corrective", priority = "Medium", assigneeIds = who, plannedStart = D(planned), teamId = team })));

    private static Task<HttpResponseMessage> LogAsync(HttpClient c, Guid wo, Guid who, DateOnly day, decimal hours, decimal overtime = 0) =>
        c.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = who, workDate = D(day), hours, overtimeHours = overtime });

    private static async Task<JsonElement> DayAsync(HttpClient c, Guid? who, DateOnly day) =>
        await Ok(await c.GetAsync($"/api/timesheet/day?date={D(day)}{(who is { } w ? $"&employeeId={w}" : "")}"));

    private static string Number(HttpClient c, Guid wo) => Ok(c.GetAsync($"/api/work-orders/{wo}").Result).Result.GetProperty("number").GetString()!;

    [SkippableFact]
    public async Task Day_lists_my_assigned_team_and_logged_work_with_hours()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var b = await PersonAsync(o.Admin, "B", "Bob Electrician", o.Site);
        var team = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/teams", new { code = "T1", name = "Team one", memberIds = new[] { a } })));
        var mine = await JobAsync(o.Admin, o.Site, "Mine", [a], Today);
        var teams = await JobAsync(o.Admin, o.Site, "Team's", [], Today, team);
        var logged = await JobAsync(o.Admin, o.Site, "Logged", [], Today.AddDays(-7));
        var others = await JobAsync(o.Admin, o.Site, "Bob's", [b], Today);
        await Ok(await LogAsync(o.Admin, logged, a, Today, 2));
        var day = await DayAsync(o.Admin, a, Today);
        var rows = day.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("workOrderId").GetGuid());
        Assert.Equal(new[] { mine, teams, logged }.Order(), rows.Keys.Order());
        Assert.DoesNotContain(others, rows.Keys);
        Assert.Equal(2m, rows[logged].GetProperty("entries")[0].GetProperty("hours").GetDecimal());
        Assert.Equal(2m, day.GetProperty("hours").GetDecimal());
        Assert.Equal("Ann Fitter", day.GetProperty("employee").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task Day_total_matches_work_order_labour()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new
        {
            code = "A", fullName = "Ann Fitter", isContractor = false, siteIds = new[] { o.Site }, hourlyRate = 50m, overtimeRate = 75m,
        })));
        var wo = await JobAsync(o.Admin, o.Site, "Pump", [a], Today);
        var entry = Id(await Ok(await LogAsync(o.Admin, wo, a, Today, 3, 1)));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/labor/{entry}", new { employeeId = a, workDate = D(Today), hours = 4m, overtimeHours = 1m }));
        var day = await DayAsync(o.Admin, a, Today);
        Assert.Equal((4m, 1m), (day.GetProperty("hours").GetDecimal(), day.GetProperty("overtimeHours").GetDecimal()));
        var labour = (await Ok(await o.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("labor");
        var only = Assert.Single(labour.EnumerateArray());
        Assert.Equal(275m, only.GetProperty("cost").GetDecimal());
        Assert.Equal(entry, day.GetProperty("rows")[0].GetProperty("entries")[0].GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task Others_timesheets_need_planner_rights()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, techUser, _) = await UserAsync(fx, o.Admin, "Doer", ["workorders.view", "workorders.execute"]);
        await PersonAsync(o.Admin, "T", "Tech", o.Site, techUser);
        var b = await PersonAsync(o.Admin, "B", "Bob Electrician", o.Site);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync($"/api/timesheet/day?date={D(Today)}&employeeId={b}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync($"/api/timesheet/week?from={D(Today)}")).StatusCode);
        Assert.Equal("Tech", (await DayAsync(tech, null, Today)).GetProperty("employee").GetProperty("name").GetString());
        var (unlinked, _, _) = await UserAsync(fx, o.Admin, "Viewer");
        Assert.Contains("Your account isn't linked to an employee.", await (await unlinked.GetAsync($"/api/timesheet/day?date={D(Today)}")).Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Week_shows_logged_against_available()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "A", fullName = "Ann Fitter", isContractor = false, siteIds = new[] { o.Site }, workingHours = EveryDay })));
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: Today.AddDays(1)));
        var wo = await JobAsync(o.Admin, o.Site, "Pump", [a], Today);
        await Ok(await LogAsync(o.Admin, wo, a, Today, 3));
        var week = await Ok(await o.Admin.GetAsync($"/api/timesheet/week?from={D(Today)}"));
        Assert.Equal(7, week.GetProperty("days").GetArrayLength());
        var days = week.GetProperty("people").EnumerateArray().Single(p => Id(p) == a).GetProperty("days").EnumerateArray().ToDictionary(d => d.GetProperty("day").GetString()!);
        Assert.Equal((3m, 8m), (days[D(Today)].GetProperty("hours").GetDecimal(), days[D(Today)].GetProperty("available").GetDecimal()));
        Assert.Equal(0m, days[D(Today.AddDays(1))].GetProperty("available").GetDecimal());
    }

    [SkippableFact]
    public async Task Board_and_timesheet_agree_on_available_hours()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var sunday = NextSunday;
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: sunday));
        await AddEntryAsync(o.Admin, Entry(types["MEET"], person: a, from: sunday.AddDays(1), hours: 2));
        await AddEntryAsync(o.Admin, Entry(types["EXTRA"], person: a, from: sunday.AddDays(5), hours: 6));
        var board = await Ok(await o.Admin.GetAsync($"/api/planning/board?from={D(sunday)}&to={D(sunday.AddDays(6))}"));
        var boardDays = board.GetProperty("people").EnumerateArray().Single(p => Id(p) == a).GetProperty("days").EnumerateArray()
            .ToDictionary(d => d.GetProperty("day").GetString()!, d => d.GetProperty("available").GetDecimal());
        foreach (var day in new[] { sunday, sunday.AddDays(1), sunday.AddDays(2), sunday.AddDays(5) })
            Assert.Equal(boardDays[D(day)], (await DayAsync(o.Admin, a, day)).GetProperty("available").GetProperty("available").GetDecimal());
    }
}
