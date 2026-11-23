using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: availability types, entries of time away and extra time, and the hours they leave.</summary>
public class AvailabilityTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>The next Sunday after today: a working day in the Sunday–Thursday pattern used here.</summary>
    internal static DateOnly NextSunday
    {
        get { var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1); while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1); return d; }
    }

    /// <summary>08:00–16:00 Sunday to Thursday (8 hours), Friday and Saturday off.</summary>
    internal static readonly object[] SundayToThursday =
        new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday }
            .Select(d => (object)new { day = d.ToString(), start = "08:00", end = "16:00" }).ToArray();

    internal static async Task<Guid> PersonAsync(HttpClient c, string code, string name, Guid site, Guid? user = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code, fullName = name, isContractor = false, siteIds = new[] { site }, workingHours = SundayToThursday, userId = user })));

    internal static async Task<Dictionary<string, Guid>> TypesAsync(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/availability-types"))).EnumerateArray().ToDictionary(t => t.GetProperty("code").GetString()!, Id);

    internal static object Entry(Guid type, Guid? person = null, Guid? team = null, DateOnly? from = null, DateOnly? to = null, DayOfWeek[]? days = null, decimal? hours = null) =>
        new { typeId = type, employeeId = person, teamId = team, from = from is { } f ? D(f) : null, to = to is { } t ? D(t) : null, days = days?.Select(x => x.ToString()), hours, notes = (string?)null };

    internal static async Task<Guid> AddEntryAsync(HttpClient c, object entry) => Id(await Ok(await c.PostAsJsonAsync("/api/availability", entry)));

    private static async Task<string> Refused(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [SkippableFact]
    public async Task Every_organization_starts_with_five_types()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = (await Ok(await o.Admin.GetAsync("/api/availability-types"))).EnumerateArray().ToList();
        Assert.Equal(["EXTRA", "HOL", "MEET", "SICK", "TRAIN"], types.Select(t => t.GetProperty("code").GetString()).Order());
        Assert.Equal("Away", types.Single(t => t.GetProperty("code").GetString() == "HOL").GetProperty("kind").GetString());
        Assert.Equal("Extra", types.Single(t => t.GetProperty("code").GetString() == "EXTRA").GetProperty("kind").GetString());
    }

    [SkippableFact]
    public async Task Type_in_use_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: NextSunday));
        Assert.Contains("This type is in use. Make it inactive instead.", await Refused(await o.Admin.DeleteAsync($"/api/availability-types/{types["HOL"]}")));
        var fresh = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/availability-types", new { code = "TRAVEL", name = "Travel", color = "#868e96", kind = "Away" })));
        Assert.True((await o.Admin.DeleteAsync($"/api/availability-types/{fresh}")).IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Entries_are_checked()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var team = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/teams", new { code = "T1", name = "Team one", memberIds = new[] { a } })));
        var day = NextSunday;
        async Task<string> Post(object e) => await Refused(await o.Admin.PostAsJsonAsync("/api/availability", e));
        Assert.Contains("Choose a person or a team, not both.", await Post(Entry(types["HOL"], person: a, team: team, from: day)));
        Assert.Contains("Choose a person or a team, not both.", await Post(Entry(types["HOL"], from: day)));
        Assert.Contains("The end can't be before the start.", await Post(Entry(types["HOL"], person: a, from: day, to: day.AddDays(-1))));
        Assert.Contains("Use at most 366 days.", await Post(Entry(types["HOL"], person: a, from: day, to: day.AddDays(400))));
        Assert.Contains("Hours must be more than 0 and at most 24.", await Post(Entry(types["MEET"], person: a, from: day, hours: 0)));
        Assert.Contains("Hours must be more than 0 and at most 24.", await Post(Entry(types["MEET"], person: a, from: day, hours: 25)));
        Assert.Contains("Extra time needs hours.", await Post(Entry(types["EXTRA"], person: a, from: day)));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/availability-types/{types["TRAIN"]}", new { code = "TRAIN", name = "Training", color = "#7950f2", kind = "Away", isActive = false }));
        Assert.Contains("Choose an active type.", await Post(Entry(types["TRAIN"], person: a, from: day)));
    }

    [SkippableFact]
    public async Task Planner_or_people_manager_edits_entries_technician_cannot()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/availability", Entry(types["HOL"], person: a, from: NextSunday))).StatusCode);
        var (planner, _, _) = await UserAsync(fx, o.Admin, "Planner", ["workorders.view", "workorders.edit"]);
        var (people, _, _) = await UserAsync(fx, o.Admin, "People", ["workorders.view", "people.manage"]);
        var mine = await AddEntryAsync(planner, Entry(types["HOL"], person: a, from: NextSunday));
        await AddEntryAsync(people, Entry(types["SICK"], person: a, from: NextSunday.AddDays(1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.DeleteAsync($"/api/availability/{mine}")).StatusCode);
        Assert.True((await people.DeleteAsync($"/api/availability/{mine}")).IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Days_query_combines_pattern_time_away_and_extra()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var a = await PersonAsync(o.Admin, "A", "Ann Fitter", o.Site);
        var sunday = NextSunday;
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: a, from: sunday));
        await AddEntryAsync(o.Admin, Entry(types["MEET"], person: a, from: sunday.AddDays(1), hours: 2));
        await AddEntryAsync(o.Admin, Entry(types["EXTRA"], person: a, from: sunday.AddDays(5), hours: 6)); // Friday, a day off
        var days = (await Ok(await o.Admin.GetAsync($"/api/availability/hours?employeeId={a}&from={D(sunday)}&to={D(sunday.AddDays(5))}")))
            .EnumerateArray().ToDictionary(x => x.GetProperty("day").GetString()!);
        Assert.Equal(0m, days[D(sunday)].GetProperty("available").GetDecimal());
        Assert.Equal("Holiday", days[D(sunday)].GetProperty("slots")[0].GetProperty("name").GetString());
        Assert.Equal(6m, days[D(sunday.AddDays(1))].GetProperty("available").GetDecimal());
        Assert.Equal(8m, days[D(sunday.AddDays(2))].GetProperty("available").GetDecimal());
        Assert.Equal(0m, days[D(sunday.AddDays(5))].GetProperty("pattern").GetDecimal());
        Assert.Equal(6m, days[D(sunday.AddDays(5))].GetProperty("available").GetDecimal());
    }

    [SkippableFact]
    public async Task Availability_entries_are_written_only_for_people_the_user_can_see()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = await TypesAsync(o.Admin);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        var there = await PersonAsync(o.Admin, "B", "Site B tech", siteB);
        var here = await PersonAsync(o.Admin, "A", "Site A tech", o.Site);
        var (planner, _, _) = await UserAsync(fx, o.Admin, "Planner A", ["workorders.view", "workorders.edit"], siteIds: [o.Site]);
        Assert.Contains("Choose an active person.", await Refused(await planner.PostAsJsonAsync("/api/availability", Entry(types["HOL"], person: there, from: NextSunday))));
        var theirs = await AddEntryAsync(o.Admin, Entry(types["HOL"], person: there, from: NextSunday));
        Assert.Equal(HttpStatusCode.NotFound, (await planner.PutAsJsonAsync($"/api/availability/{theirs}", Entry(types["SICK"], person: there, from: NextSunday))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await planner.DeleteAsync($"/api/availability/{theirs}")).StatusCode);
        var people = (await Ok(await planner.GetAsync("/api/availability/people"))).EnumerateArray().Select(Id).ToList();
        Assert.Contains(here, people);
        Assert.DoesNotContain(there, people);
        await AddEntryAsync(planner, Entry(types["HOL"], person: here, from: NextSunday));
    }
}
