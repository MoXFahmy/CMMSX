using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.AvailabilityTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: the planning board counts each person's available and planned hours, warns, and lets people take work.</summary>
public class PlanningAvailabilityTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> JobAsync(HttpClient c, Guid site, string title, Guid[] who, DateOnly planned, decimal? estimate = null, Guid? template = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title, siteId = site, type = "Corrective", priority = "Medium", assigneeIds = who, plannedStart = D(planned), estimatedHours = estimate, templateId = template,
        })));

    private static async Task<JsonElement> BoardAsync(HttpClient c, DateOnly from, DateOnly to, Guid? site = null) =>
        await Ok(await c.GetAsync($"/api/planning/board?from={D(from)}&to={D(to)}{(site is { } s ? $"&siteId={s}" : "")}"));

    private static JsonElement DayOf(JsonElement board, Guid person, DateOnly day) =>
        board.GetProperty("people").EnumerateArray().Single(p => Id(p) == person).GetProperty("days").EnumerateArray().Single(d => d.GetProperty("day").GetString() == D(day));

    [SkippableFact]
    public async Task Board_shows_available_hours_after_time_away_and_extra_time()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var sunday = NextSunday;
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: sunday));
        await AddEntryAsync(o.Admin, Entry(types["MEET"], person: a, from: sunday.AddDays(1), hours: 2));
        await AddEntryAsync(o.Admin, Entry(types["EXTRA"], person: a, from: sunday.AddDays(5), hours: 6));
        var b = await BoardAsync(o.Admin, sunday, sunday.AddDays(6));
        Assert.Equal(0m, DayOf(b, a, sunday).GetProperty("available").GetDecimal());
        Assert.Equal("Holiday", DayOf(b, a, sunday).GetProperty("slots")[0].GetProperty("name").GetString());
        Assert.Equal(6m, DayOf(b, a, sunday.AddDays(1)).GetProperty("available").GetDecimal());
        Assert.Equal(6m, DayOf(b, a, sunday.AddDays(5)).GetProperty("available").GetDecimal());
    }

    [SkippableFact]
    public async Task Team_entry_follows_current_members()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var bob = await PersonAsync(o.Admin, "B", "Bob Electrician", o.Site);
        var team = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/teams", new { code = "T1", name = "Team one", memberIds = new[] { a } })));
        var sunday = NextSunday;
        await AddEntryAsync(o.Admin, Entry(types["TRAIN"], team: team, from: sunday));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/teams/{team}", new { code = "T1", name = "Team one", memberIds = new[] { bob }, isActive = true }));
        var b = await BoardAsync(o.Admin, sunday, sunday);
        Assert.Equal(0m, DayOf(b, bob, sunday).GetProperty("available").GetDecimal());
        Assert.Equal(8m, DayOf(b, a, sunday).GetProperty("available").GetDecimal());
    }

    [SkippableFact]
    public async Task Planned_hours_count_all_visible_work_whatever_the_filter()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var site2 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Second plant", isActive = true })));
        var a = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new
        {
            code = "A", fullName = "Ann Fitter", isContractor = false, siteIds = new[] { o.Site, site2 }, workingHours = SundayToThursday,
        })));
        var sunday = NextSunday;
        await JobAsync(o.Admin, o.Site, "Here", [a], sunday, 3);
        await JobAsync(o.Admin, site2, "There", [a], sunday, 4);
        var b = await BoardAsync(o.Admin, sunday, sunday, o.Site);
        Assert.Equal(7m, DayOf(b, a, sunday).GetProperty("planned").GetDecimal());
    }

    [SkippableFact]
    public async Task Moving_onto_a_day_away_or_over_hours_warns_but_moves()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var sunday = NextSunday;
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: sunday));
        var small = await JobAsync(o.Admin, o.Site, "Small", [a], sunday.AddDays(2), 1);
        var moved = await Ok(await o.Admin.PostAsJsonAsync("/api/planning/move", new { workOrderId = small, day = D(sunday) }));
        Assert.Contains("Ann Fitter is away on that day (Holiday).", moved.GetProperty("warning").GetString());
        Assert.Equal(D(sunday), (await Ok(await o.Admin.GetAsync($"/api/work-orders/{small}"))).GetProperty("plannedStart").GetString());
        var big = await JobAsync(o.Admin, o.Site, "Big", [a], sunday.AddDays(3), 10);
        var over = await Ok(await o.Admin.PostAsJsonAsync("/api/planning/move", new { workOrderId = big, day = D(sunday.AddDays(1)) }));
        Assert.Contains("This takes Ann Fitter to 10 of 8 hours", over.GetProperty("warning").GetString());
    }

    [SkippableFact]
    public async Task Assign_to_me_takes_unassigned_work_once()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (one, oneUser, _) = await UserAsync(fx, o.Admin, "Technician");
        var (two, twoUser, _) = await UserAsync(fx, o.Admin, "Technician");
        var (nobody, _, _) = await UserAsync(fx, o.Admin, "Technician");
        await PersonAsync(o.Admin, "T1", "Tech one", o.Site, oneUser);
        await PersonAsync(o.Admin, "T2", "Tech two", o.Site, twoUser);
        var job = await JobAsync(o.Admin, o.Site, "Leak", [], NextSunday);
        var answers = await Task.WhenAll(
            one.PostAsJsonAsync("/api/planning/take", new { workOrderId = job }),
            two.PostAsJsonAsync("/api/planning/take", new { workOrderId = job }));
        Assert.Single(answers, r => r.IsSuccessStatusCode);
        var refused = Assert.Single(answers, r => !r.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Someone was assigned meanwhile. Refresh the board.", await refused.Content.ReadAsStringAsync());
        Assert.Single((await Ok(await o.Admin.GetAsync($"/api/work-orders/{job}"))).GetProperty("assignees").EnumerateArray());
        var other = await JobAsync(o.Admin, o.Site, "Drip", [], NextSunday);
        var unlinked = await nobody.PostAsJsonAsync("/api/planning/take", new { workOrderId = other });
        Assert.Contains("Your account isn't linked to an employee.", await unlinked.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Asset_cards_show_the_expected_stop_of_their_pm_task()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/pm-tasks", new { code = "CHL", name = "Chiller service", steps = new[] { "Isolate" }, expectedStopHours = 4m })));
        var job = await JobAsync(o.Admin, o.Site, "Chiller service", [], NextSunday, template: task);
        var b = await BoardAsync(o.Admin, NextSunday, NextSunday);
        Assert.Equal(4m, b.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == job).GetProperty("expectedStopHours").GetDecimal());
    }

    [SkippableFact]
    public async Task Available_hours_are_the_same_for_a_site_limited_viewer()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        // A technician who works everywhere, in a team of site B with a whole-day training.
        var tech = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new
        {
            code = "T", fullName = "Roaming tech", isContractor = false, siteIds = Array.Empty<Guid>(), workingHours = SundayToThursday,
        })));
        var team = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/teams", new { code = "TB", name = "Team B", siteId = siteB, memberIds = new[] { tech } })));
        await AddEntryAsync(o.Admin, Entry(types["TRAIN"], team: team, from: NextSunday));
        var (planner, _, _) = await UserAsync(fx, o.Admin, "Planner A", ["workorders.view", "workorders.edit"], siteIds: [o.Site]);
        Assert.Equal(0m, DayOf(await BoardAsync(o.Admin, NextSunday, NextSunday), tech, NextSunday).GetProperty("available").GetDecimal());
        Assert.Equal(0m, DayOf(await BoardAsync(planner, NextSunday, NextSunday), tech, NextSunday).GetProperty("available").GetDecimal());
    }
}
