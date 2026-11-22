using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>The small items the 0.9.0 final review left for 0.9.1.</summary>
public class TidyAccessTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static async Task<Guid> RoleAsync(HttpClient admin, string name) =>
        (await Ok(await admin.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static List<Guid> Ids(JsonElement e) =>
        (e.ValueKind == JsonValueKind.Array ? e : e.GetProperty("items")).EnumerateArray().Select(Id).ToList();

    // 1
    [SkippableFact]
    public async Task A_user_whose_places_were_deleted_can_still_be_edited()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Deleted Places Co");
        var spare = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, code = "SP", name = "Spare", type = "Building", criticality = "Medium", isActive = true })));
        var viewer = await RoleAsync(w.Admin, "Viewer");
        var email = $"{Guid.NewGuid():N}@test.local";
        var u = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "Gone", email, isActive = true, roleIds = new[] { viewer }, password = UserPassword, locationIds = new[] { spare } })));
        await Ok(await w.Admin.DeleteAsync($"/api/locations/{spare}"));
        await Ok(await w.Admin.PutAsJsonAsync($"/api/users/{u}", new { fullName = "Gone renamed", email, isActive = true, roleIds = new[] { viewer } }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = UserPassword, rememberMe = false }));
        Assert.Empty(Ids(await Ok(await c.GetAsync("/api/sites")))); // still nothing, never everything
        // A new id that doesn't exist is still refused.
        var bad = await w.Admin.PutAsJsonAsync($"/api/users/{u}", new { fullName = "Gone", email, isActive = true, roleIds = new[] { viewer }, locationIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // 2
    [SkippableFact]
    public async Task A_limited_admin_cannot_sign_out_people_who_see_more()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Devices Co");
        var (limitedAdmin, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var (_, everyone) = await UserAsync(fx, w.Admin, "Administrator");
        var r = await limitedAdmin.PostAsync($"/api/users/{everyone}/devices/{Guid.NewGuid()}/sign-out", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("You can only change people whose access is inside yours.", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await limitedAdmin.GetAsync($"/api/users/{everyone}/devices")).StatusCode);
    }

    // 3
    [SkippableFact]
    public async Task Audit_of_another_sites_labour_is_not_found()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Audit Child Co");
        var emp = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tech", hourlyRate = 10, isContractor = false })));
        var labour = Id(await Ok(await w.Admin.PostAsJsonAsync($"/api/work-orders/{w.B.WorkOrder}/labor", new { employeeId = emp, workDate = Today, hours = 1, overtimeHours = 0 })));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/audit?entityType=WorkOrderLabor&entityId={labour}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/audit?entityType=SomethingNew&entityId={labour}")).StatusCode);
        // Shared records keep their trail.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/audit?entityType=Employee&entityId={emp}")).StatusCode);
    }

    // 4
    [SkippableFact]
    public async Task An_edit_from_an_older_page_keeps_the_team()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Old Page Co");
        var team = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/teams", new { code = "T", name = "Crew", siteId = w.A.Site, memberIds = Array.Empty<Guid>(), isActive = true })));
        var wo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "Low", teamId = team })));
        async Task<JsonElement> Detail() => await Ok(await w.Admin.GetAsync($"/api/work-orders/{wo}"));
        var d = await Detail();
        await Ok(await w.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Job edited", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "Low", rowVersion = d.GetProperty("rowVersion").GetString() }));
        Assert.Equal(team, (await Detail()).GetProperty("teamId").GetGuid());
        d = await Detail();
        await Ok(await w.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Job edited", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "Low", managesTeam = true, rowVersion = d.GetProperty("rowVersion").GetString() }));
        Assert.Equal(JsonValueKind.Null, (await Detail()).GetProperty("teamId").ValueKind);
    }

    // 5
    [SkippableFact]
    public async Task A_teams_site_follows_its_work_and_limited_people_give_a_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Team Site Co");
        var team = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/teams", new { code = "ANY", name = "Anywhere", memberIds = Array.Empty<Guid>(), isActive = true })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "At A", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "Low", teamId = team }));
        var toB = await w.Admin.PutAsJsonAsync($"/api/teams/{team}", new { code = "ANY", name = "Anywhere", siteId = w.B.Site, memberIds = Array.Empty<Guid>(), isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, toB.StatusCode);
        Assert.Contains("This team has open work or PM schedules at another site.", await toB.Content.ReadAsStringAsync());
        await Ok(await w.Admin.PutAsJsonAsync($"/api/teams/{team}", new { code = "ANY", name = "Anywhere", siteId = w.A.Site, memberIds = Array.Empty<Guid>(), isActive = true }));
        var (c, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var everywhere = await c.PostAsJsonAsync("/api/teams", new { code = "MINE", name = "Mine", memberIds = Array.Empty<Guid>(), isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, everywhere.StatusCode);
        Assert.Contains("siteId", await everywhere.Content.ReadAsStringAsync());
        await Ok(await c.PostAsJsonAsync("/api/teams", new { code = "MINE", name = "Mine", siteId = w.A.Site, memberIds = Array.Empty<Guid>(), isActive = true }));
    }

    // 6
    [SkippableFact]
    public async Task The_board_shows_limited_people_only_their_sites_technicians()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Board Sites Co");
        var atA = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "EA", fullName = "Ann A", isContractor = false, siteIds = new[] { w.A.Site } })));
        var atB = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "EB", fullName = "Bob B", isContractor = false, siteIds = new[] { w.B.Site } })));
        var floater = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "EF", fullName = "Fay Floater", isContractor = false })));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var people = (await Ok(await c.GetAsync($"/api/planning/board?from={Today}&to={Today}"))).GetProperty("people").EnumerateArray().Select(Id).ToList();
        Assert.Contains(atA, people);
        Assert.Contains(floater, people);
        Assert.DoesNotContain(atB, people);
    }

    // 7
    [SkippableFact]
    public async Task Request_recipients_must_see_the_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Recipients See Co");
        var manager = await RoleAsync(w.Admin, "Maintenance Manager");
        var bOnly = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "B only", email = $"{Guid.NewGuid():N}@test.local", isActive = true, roleIds = new[] { manager }, password = UserPassword, siteIds = new[] { w.B.Site } })));
        var r = await SiteSettingsTests.TrySetSiteAsync(w.Admin, w.A.Site, new { requestRecipientIds = new[] { bOnly } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Choose people who can see all of this site.", await r.Content.ReadAsStringAsync());
    }

    // 8
    [SkippableFact]
    public async Task Import_numbering_skips_numbers_used_in_hidden_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Import Number Co");
        // The world numbered AST-00001 (A) and AST-00002 (B); B also holds AST-00003, typed by hand (the counter didn't move).
        await Ok(await w.Admin.PostAsJsonAsync("/api/assets", new { assetNumber = "AST-00003", name = "Hand numbered", siteId = w.B.Site, locationId = w.B.Location, status = "Active", criticality = "Low", warrantyType = "None" }));
        var (c, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var sheet = SheetOf(await RunAsync(c, Book(new Sheet("Assets", ["Asset number", "Name", "Site code", "Location code"], [null, "New fan", "A", "LA"]))), "Assets");
        Assert.Equal((1, 0), (Count(sheet, "new"), Count(sheet, "problems")));
        var numbers = (await Ok(await w.Admin.GetAsync("/api/assets?pageSize=100"))).GetProperty("items").EnumerateArray().Select(a => a.GetProperty("assetNumber").GetString()).ToList();
        Assert.Contains("AST-00004", numbers);
    }

    // 9
    [SkippableFact]
    public async Task Completed_from_filters_the_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Completed From Co");
        await Ok(await w.Admin.PostAsJsonAsync($"/api/work-orders/{w.A.WorkOrder}/status", new { status = "Completed", completionNotes = "Done" }));
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal([w.A.WorkOrder], Ids(await Ok(await w.Admin.GetAsync($"/api/work-orders?completedFrom={Today}"))));
        Assert.Empty(Ids(await Ok(await w.Admin.GetAsync($"/api/work-orders?completedFrom={tomorrow}"))));
    }

    // 10
    [SkippableFact]
    public async Task Saving_a_round_keeps_assets_the_person_cannot_see()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Round Keep Co");
        var other = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, code = "LA2", name = "Annex", type = "Building", criticality = "Medium", isActive = true })));
        var hidden = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/assets", new { name = "Annex pump", siteId = w.A.Site, locationId = other, status = "Active", criticality = "Low", warrantyType = "None" })));
        var round = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Round", siteId = w.A.Site, locationId = w.A.Location, priority = "Medium", pmTaskId = w.Task, type = "FixedInterval", basis = "Scheduled",
            frequency = 1, unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Today, timeBased = false, extraTasks = Array.Empty<object>(),
            assetIds = new[] { w.A.Asset, hidden },
        })));
        var (c, _) = await UserAsync(fx, w.Admin, "Administrator", locationIds: [w.A.Location]);
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Ok(await c.GetAsync($"/api/pm-schedules/{round}"))).GetRawText())!;
        body["name"] = "Round renamed";
        body["assetIds"] = new[] { w.A.Asset }; // all the editor can see
        await Ok(await c.PutAsJsonAsync($"/api/pm-schedules/{round}", body));
        var assets = (await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{round}"))).GetProperty("assets").EnumerateArray().Select(Id).ToList();
        Assert.Equal([w.A.Asset, hidden], assets);
    }

    // ---- From the 0.9.1 review ----

    private static object RoundBody(World w, Guid location, params Guid[] assets) => new
    {
        name = "Round", siteId = w.A.Site, locationId = location, priority = "Medium", pmTaskId = w.Task, type = "FixedInterval", basis = "Scheduled",
        frequency = 1, unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Today, timeBased = false, extraTasks = Array.Empty<object>(),
        assetIds = assets,
    };

    private async Task<(World W, Guid Hidden, Guid Round, HttpClient Branch)> RoundWorldAsync(string org)
    {
        var w = await WorldAsync(fx, org);
        var annex = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, code = "LA2", name = "Annex", type = "Building", criticality = "Medium", isActive = true })));
        var hidden = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/assets", new { name = "Annex pump", siteId = w.A.Site, locationId = annex, status = "Active", criticality = "Low", warrantyType = "None" })));
        var round = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/pm-schedules", RoundBody(w, w.A.Location, w.A.Asset, hidden))));
        var (branch, _) = await UserAsync(fx, w.Admin, "Administrator", locationIds: [w.A.Location]);
        return (w, hidden, round, branch);
    }

    [SkippableFact]
    public async Task A_round_doesnt_take_assets_the_person_cannot_see()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (w, hidden, round, branch) = await RoundWorldAsync("Round Take Co");
        var extra = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/assets", new { name = "Annex fan", siteId = w.A.Site, locationId = (await Ok(await w.Admin.GetAsync($"/api/assets/{hidden}"))).GetProperty("locationId").GetGuid(), status = "Active", criticality = "Low", warrantyType = "None" })));
        Assert.Equal(HttpStatusCode.BadRequest, (await branch.PostAsJsonAsync("/api/pm-schedules", RoundBody(w, w.A.Location, w.A.Asset, extra))).StatusCode);
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Ok(await branch.GetAsync($"/api/pm-schedules/{round}"))).GetRawText())!;
        body["assetIds"] = new[] { w.A.Asset, extra };
        Assert.Equal(HttpStatusCode.BadRequest, (await branch.PutAsJsonAsync($"/api/pm-schedules/{round}", body)).StatusCode);
        // Someone limited to site A naming site B's asset learns nothing about it.
        var (siteA, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var other = await siteA.PostAsJsonAsync("/api/pm-schedules", RoundBody(w, w.A.Location, w.A.Asset, w.B.Asset));
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.DoesNotContain(w.B.AssetNumber, await other.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task A_branch_user_cant_turn_a_round_with_hidden_assets_into_one_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (w, _, round, branch) = await RoundWorldAsync("Round Convert Co");
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Ok(await branch.GetAsync($"/api/pm-schedules/{round}"))).GetRawText())!;
        body["assetIds"] = Array.Empty<Guid>();
        body["assetId"] = w.A.Asset;
        var single = await branch.PutAsJsonAsync($"/api/pm-schedules/{round}", body);
        Assert.Equal(HttpStatusCode.BadRequest, single.StatusCode);
        Assert.Contains("This round also covers assets in parts of the site you can't see.", await single.Content.ReadAsStringAsync());
        body["assetId"] = null; // a location schedule
        Assert.Equal(HttpStatusCode.BadRequest, (await branch.PutAsJsonAsync($"/api/pm-schedules/{round}", body)).StatusCode);
    }

    [SkippableFact]
    public async Task Generating_a_round_as_a_branch_user_covers_every_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (w, hidden, round, branch) = await RoundWorldAsync("Round Generate Co");
        await Ok(await branch.PostAsJsonAsync("/api/pm/generate", new { until = Today, ids = new[] { round } }));
        var wo = (await Ok(await w.Admin.GetAsync($"/api/work-orders?search=Round"))).GetProperty("items").EnumerateArray().First(x => x.GetProperty("title").GetString()!.StartsWith("Round"));
        var assets = (await Ok(await w.Admin.GetAsync($"/api/work-orders/{Id(wo)}"))).GetProperty("assets").EnumerateArray().Select(Id).ToList();
        Assert.Equal([w.A.Asset, hidden], assets);
    }

    [SkippableFact]
    public async Task Unlimited_admins_switched_to_one_site_can_still_make_every_site_teams()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Switched Team Co");
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/teams") { Content = JsonContent.Create(new { code = "ALL", name = "Everywhere", memberIds = Array.Empty<Guid>(), isActive = true }) };
        m.Headers.Add("X-Cmx-Site", w.A.Site.ToString());
        await Ok(await w.Admin.SendAsync(m));
    }

    [SkippableFact]
    public async Task Older_pages_keep_a_team_only_where_it_fits()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Old Page Fit Co");
        var teamA = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/teams", new { code = "TA", name = "A crew", siteId = w.A.Site, memberIds = Array.Empty<Guid>(), isActive = true })));
        var teamB = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/teams", new { code = "TB", name = "B crew", siteId = w.B.Site, memberIds = Array.Empty<Guid>(), isActive = true })));
        var wo = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Moving job", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "Low", teamId = teamA })));
        async Task<JsonElement> Detail() => await Ok(await w.Admin.GetAsync($"/api/work-orders/{wo}"));
        // An older page moves it to site B: A's team doesn't fit there and goes.
        await Ok(await w.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Moving job", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "Low", rowVersion = (await Detail()).GetProperty("rowVersion").GetString() }));
        Assert.Equal(JsonValueKind.Null, (await Detail()).GetProperty("teamId").ValueKind);
        // A team sent without the flag is still a choice.
        await Ok(await w.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Moving job", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "Low", teamId = teamB, rowVersion = (await Detail()).GetProperty("rowVersion").GetString() }));
        Assert.Equal(teamB, (await Detail()).GetProperty("teamId").GetGuid());
        // PM schedules keep their team the same way.
        var s = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{w.A.Schedule}"))).GetRawText())!;
        s["teamId"] = teamA; s["managesTeam"] = true;
        await Ok(await w.Admin.PutAsJsonAsync($"/api/pm-schedules/{w.A.Schedule}", s));
        s = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{w.A.Schedule}"))).GetRawText())!;
        s.Remove("teamId"); s.Remove("managesTeam"); s["name"] = "Renamed by an older page";
        await Ok(await w.Admin.PutAsJsonAsync($"/api/pm-schedules/{w.A.Schedule}", s));
        Assert.Equal(teamA, (await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{w.A.Schedule}"))).GetProperty("teamId").GetGuid());
    }

    [SkippableFact]
    public async Task Request_recipients_must_see_all_of_the_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Recipients Whole Co");
        var manager = await RoleAsync(w.Admin, "Maintenance Manager");
        var partly = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "Hall only", email = $"{Guid.NewGuid():N}@test.local", isActive = true, roleIds = new[] { manager }, password = UserPassword, locationIds = new[] { w.A.Location } })));
        var r = await SiteSettingsTests.TrySetSiteAsync(w.Admin, w.A.Site, new { requestRecipientIds = new[] { partly } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Choose people who can see all of this site.", await r.Content.ReadAsStringAsync());
    }
}
