using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>Named technician teams that work orders and PM schedules can be given to (0.9.0).</summary>
public class TeamTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> EmployeeAsync(HttpClient c, string code, string? email = null, Guid? userId = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code, fullName = $"Tech {code}", email, userId, isContractor = false })));

    private static async Task<Guid> TeamAsync(HttpClient c, string code, Guid? site, params Guid[] members) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/teams", new { code, name = $"Team {code}", siteId = site, memberIds = members, isActive = true })));

    [SkippableFact]
    public async Task Teams_can_be_created_with_members()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Co");
        var e1 = await EmployeeAsync(w.Admin, "E1"); var e2 = await EmployeeAsync(w.Admin, "E2");
        var team = await TeamAsync(w.Admin, "HVAC", w.A.Site, e1, e2);
        var listed = (await Ok(await w.Admin.GetAsync("/api/teams"))).EnumerateArray().Single(t => Id(t) == team);
        Assert.Equal("Team HVAC", listed.GetProperty("name").GetString());
        Assert.Equal("Site A", listed.GetProperty("siteName").GetString());
        Assert.Equal(new[] { e1, e2 }.Order(), listed.GetProperty("memberIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        var again = await w.Admin.PostAsJsonAsync("/api/teams", new { code = "HVAC", name = "Copy", memberIds = Array.Empty<Guid>(), isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        // Teams usable at a site: its own and those of every site.
        var everywhere = await TeamAsync(w.Admin, "ALL", null);
        var atB = (await Ok(await w.Admin.GetAsync($"/api/teams?siteId={w.B.Site}"))).EnumerateArray().Select(Id).ToList();
        Assert.Equal([everywhere], atB);
    }

    [SkippableFact]
    public async Task A_work_order_can_be_given_to_a_team_and_filtered()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Work Co");
        var team = await TeamAsync(w.Admin, "ELEC", w.A.Site);
        var wo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Panel", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = team })));
        var list = await Ok(await w.Admin.GetAsync($"/api/work-orders?teamId={team}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Team ELEC", list.GetProperty("items")[0].GetProperty("teamName").GetString());
        var detail = await Ok(await w.Admin.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal("Team ELEC", detail.GetProperty("team").GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task A_team_of_another_site_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Site Co");
        var teamB = await TeamAsync(w.Admin, "BCREW", w.B.Site);
        var r = await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Wrong team", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = teamB });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("teamId", await r.Content.ReadAsStringAsync());
        var anywhere = await TeamAsync(w.Admin, "ANY", null);
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fine", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = anywhere }));
    }

    [SkippableFact]
    public async Task PM_work_inherits_the_schedules_team()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Pm Co");
        var team = await TeamAsync(w.Admin, "PMCREW", w.A.Site);
        await Ok(await w.Admin.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Team PM", siteId = w.A.Site, locationId = w.A.Location, assetId = w.A.Asset, priority = "Medium", pmTaskId = w.Task, type = "FixedInterval", basis = "Scheduled",
            frequency = 1, unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Iso(Today), timeBased = false, extraTasks = Array.Empty<object>(), teamId = team,
        }));
        await Ok(await w.Admin.PostAsJsonAsync("/api/pm/generate", new { until = Iso(Today) }));
        var items = (await Ok(await w.Admin.GetAsync($"/api/work-orders?teamId={team}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("title").GetString() == "Team PM");
    }

    [SkippableFact]
    public async Task Team_members_get_the_assigned_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Mail Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        var tenant = (await Ok(await w.Admin.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var a1 = $"m1-{Guid.NewGuid():N}@cmms.test"; var a2 = $"m2-{Guid.NewGuid():N}@cmms.test";
        var team = await TeamAsync(w.Admin, "MAIL", w.A.Site, await EmployeeAsync(w.Admin, "M1", a1), await EmployeeAsync(w.Admin, "M2", a2));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Team job", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = team }));
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var to = await db.OutboundEmails.IgnoreQueryFilters().Where(e => e.TenantId == tenant && e.EventKey == EmailEvents.WorkOrderAssigned).Select(e => e.ToAddress).ToListAsync();
        Assert.Equal(new[] { a1, a2 }.Order(), to.Order());
    }

    [SkippableFact]
    public async Task A_technicians_bundle_includes_their_teams_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Phone Co");
        var (tech, techUser) = await UserAsync(fx, w.Admin, "Technician");
        var me = await EmployeeAsync(w.Admin, "T1", userId: techUser);
        var team = await TeamAsync(w.Admin, "PHONE", w.A.Site, me);
        var wo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "For the team", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = team })));
        var jobs = (await Ok(await tech.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Select(Id).ToList();
        Assert.Contains(wo, jobs);
    }

    [SkippableFact]
    public async Task The_planning_board_filters_by_team()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Board Co");
        var member = await EmployeeAsync(w.Admin, "B1"); var other = await EmployeeAsync(w.Admin, "B2");
        var team = await TeamAsync(w.Admin, "BOARD", null, member);
        var day = Iso(Today);
        var teamWo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Team's", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = team, plannedStart = day })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Someone else's", siteId = w.A.Site, type = "Corrective", priority = "Low", plannedStart = day, assigneeIds = new[] { other } }));
        var board = await Ok(await w.Admin.GetAsync($"/api/planning/board?from={day}&to={day}&teamId={team}"));
        Assert.Equal([member], board.GetProperty("people").EnumerateArray().Select(Id).ToArray());
        Assert.Equal([teamWo], board.GetProperty("jobs").EnumerateArray().Select(Id).ToArray());
    }

    [SkippableFact]
    public async Task Reports_can_group_by_team()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Report Co");
        var emp = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "R1", fullName = "Rita", hourlyRate = 10, overtimeRate = 15, isContractor = false })));
        var team = await TeamAsync(w.Admin, "REP", w.A.Site, emp);
        var wo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Reported", siteId = w.A.Site, type = "Corrective", priority = "Low", teamId = team })));
        await Ok(await w.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp, workDate = Today, hours = 2, overtimeHours = 0 }));
        var range = $"from={Iso(Today.AddDays(-1))}&to={Iso(Today)}";
        var labour = await Ok(await w.Admin.GetAsync($"/api/reports/labor-hours?{range}&groupBy=team"));
        Assert.Contains(labour.GetProperty("rows").EnumerateArray(), r => r.GetProperty("group").GetString() == "Team REP");
        var costs = await Ok(await w.Admin.GetAsync($"/api/reports/maintenance-costs?{range}&groupBy=team"));
        Assert.Contains(costs.GetProperty("rows").EnumerateArray(), r => r.GetProperty("group").GetString() == "Team REP");
    }
}
