using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class GroupActionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Group, List<Guid> Children, List<string> Numbers, Guid Me);

    /// <summary>A group PM on <paramref name="assets"/> assets assigned to the admin's own employee, generated today.</summary>
    private async Task<Setup> GroupAsync(int assets = 2, Loop.Org? existing = null)
    {
        var org = existing ?? await OrgAsync(fx, emailOn: false);
        var me = (await EmployeeAsync(org.Admin, $"ME{Guid.NewGuid():N}"[..8], userId: org.AdminId)).Id;
        var list = new List<Guid>();
        for (var n = 0; n < assets; n++) list.Add(await AssetAsync(org.Admin, org.Site, $"Fan {n + 1}"));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, $"T{Guid.NewGuid():N}"[..8]), Today, assets: [.. list],
            extra: new() { ["multiAssetMode"] = "Group", ["assigneeIds"] = new[] { me } })));
        var group = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        var children = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).OrderBy(w => w.Number).Select(w => new { w.Id, w.Number }).ToListAsync());
        return new Setup(org, group, children.Select(c => c.Id).ToList(), children.Select(c => c.Number).ToList(), me);
    }

    private static async Task<JsonElement> GroupDetailAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-groups/{id}"));
    private static async Task<JsonElement> WoAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-orders/{id}"));
    private static async Task LaborAsync(HttpClient c, Guid wo, Guid employee, decimal hours) =>
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = employee, workDate = Iso(Today), hours, overtimeHours = 0 }));

    [SkippableFact]
    public async Task Assign_skips_finished_or_worked_children()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync(3);
        var helper = (await EmployeeAsync(s.Org.Admin, "HLP")).Id;
        await LaborAsync(s.Org.Admin, s.Children[1], s.Me, 1);
        await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Children[2]}/status", new { status = "Completed", completionNotes = "Done" }));
        var answer = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{s.Group}/assign", new { employeeIds = new[] { helper } }));
        Assert.Equal(1, answer.GetProperty("assigned").GetInt32());
        Assert.Equal($"Left alone (finished or hours logged): {s.Numbers[1]}, {s.Numbers[2]}.", answer.GetProperty("message").GetString());
        Assert.Contains((await WoAsync(s.Org.Admin, s.Children[0])).GetProperty("assignees").EnumerateArray(), a => a.GetProperty("employeeId").GetGuid() == helper);
        Assert.DoesNotContain((await WoAsync(s.Org.Admin, s.Children[1])).GetProperty("assignees").EnumerateArray(), a => a.GetProperty("employeeId").GetGuid() == helper);
    }

    [SkippableFact]
    public async Task Report_for_all_keeps_existing_fields()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync();
        await DbAsync(fx, async db =>
        {
            var w = await db.WorkOrders.IgnoreQueryFilters().FirstAsync(x => x.Id == s.Children[0]);
            w.CompletionNotes = "Own notes";
            await db.SaveChangesAsync();
            return 0;
        });
        var answer = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{s.Group}/report", new { completionNotes = "Replaced filters", causeNotes = "Wear" }));
        Assert.Equal(2, answer.GetProperty("updated").GetInt32());
        var first = await WoAsync(s.Org.Admin, s.Children[0]);
        var second = await WoAsync(s.Org.Admin, s.Children[1]);
        Assert.Equal(("Own notes", "Wear"), (first.GetProperty("completionNotes").GetString(), first.GetProperty("causeNotes").GetString()));
        Assert.Equal(("Replaced filters", "Wear"), (second.GetProperty("completionNotes").GetString(), second.GetProperty("causeNotes").GetString()));
    }

    [SkippableFact]
    public async Task Status_of_all_reports_refusals()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync();
        var started = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{s.Group}/status", new { status = "InProgress" }));
        Assert.Equal(2, started.GetProperty("changed").GetInt32());
        Assert.Equal("InProgress", (await GroupDetailAsync(s.Org.Admin, s.Group)).GetProperty("status").GetString());
        await TestSettings.SetSettingsAsync(s.Org.Admin, x => x["commentRequiredFor"] = new JsonArray("hold"));
        var held = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{s.Group}/status", new { status = "OnHold", holdReason = "WaitingForParts" }));
        Assert.Equal(0, held.GetProperty("changed").GetInt32());
        var refused = held.GetProperty("refused").EnumerateArray().ToList();
        Assert.Equal(s.Numbers, refused.Select(r => r.GetProperty("number").GetString()));
        Assert.All(refused, r => Assert.False(string.IsNullOrEmpty(r.GetProperty("reason").GetString())));
    }

    [SkippableFact]
    public async Task Child_due_date_follows_the_group()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync(3);
        await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Children[2]}/status", new { status = "Completed", completionNotes = "Done" }));
        var before = (await WoAsync(s.Org.Admin, s.Children[2])).GetProperty("dueDate").GetString();
        await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/work-groups/{s.Group}/due", new { dueDate = Iso(Today.AddDays(10)) }));
        Assert.Equal(Iso(Today.AddDays(10)), (await WoAsync(s.Org.Admin, s.Children[0])).GetProperty("dueDate").GetString());
        Assert.Equal(Iso(Today.AddDays(10)), (await WoAsync(s.Org.Admin, s.Children[1])).GetProperty("dueDate").GetString());
        Assert.Equal(before, (await WoAsync(s.Org.Admin, s.Children[2])).GetProperty("dueDate").GetString());
        var w = await WoAsync(s.Org.Admin, s.Children[0]);
        var edit = new
        {
            title = w.GetProperty("title").GetString(), type = "Preventive", priority = "Medium", siteId = s.Org.Site, assetId = w.GetProperty("assetId").GetGuid(),
            locationId = w.GetProperty("locationId").ValueKind == JsonValueKind.Null ? (Guid?)null : w.GetProperty("locationId").GetGuid(), dueDate = Iso(Today.AddDays(30)),
        };
        Assert.Contains("The due date comes from group GRP-00001.", await Bad(await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Children[0]}", edit)));
        Assert.True((await s.Org.Admin.PutAsJsonAsync($"/api/work-orders/{s.Children[0]}", edit with { dueDate = Iso(Today.AddDays(10)) })).IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Group_comments_show_on_children_and_phone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync();
        await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-groups/{s.Group}/comments", new { body = "Bring the ladder" }));
        var w = await WoAsync(s.Org.Admin, s.Children[0]);
        Assert.Equal("GRP-00001", w.GetProperty("group").GetProperty("number").GetString());
        Assert.Equal("Bring the ladder", w.GetProperty("group").GetProperty("comments").EnumerateArray().Single().GetProperty("body").GetString());
        var job = (await Ok(await s.Org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == s.Children[0]);
        Assert.Equal("Bring the ladder", job.GetProperty("group").GetProperty("comments").EnumerateArray().Single().GetProperty("body").GetString());
        var list = await Ok(await s.Org.Admin.GetAsync($"/api/work-orders?groupId={s.Group}"));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.All(list.GetProperty("items").EnumerateArray(), i => Assert.Equal("GRP-00001", i.GetProperty("groupNumber").GetString()));
    }

    [SkippableFact]
    public async Task Totals_roll_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync();
        await LaborAsync(s.Org.Admin, s.Children[0], s.Me, 2);
        await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Children[1]}/costs", new { description = "Crane", quantity = 1, unitCost = 150 }));
        var g = await GroupDetailAsync(s.Org.Admin, s.Group);
        Assert.Equal((2m, 150m), (g.GetProperty("totals").GetProperty("laborHours").GetDecimal(), g.GetProperty("totals").GetProperty("otherCost").GetDecimal()));
        var rows = g.GetProperty("children").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2m, rows[0].GetProperty("laborHours").GetDecimal());
        Assert.Equal((0, 1), (rows[0].GetProperty("stepsDone").GetInt32(), rows[0].GetProperty("stepsTotal").GetInt32()));
    }

    [SkippableFact]
    public async Task Groups_follow_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await GroupAsync();
        var other = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var outsider = await UserAsync(fx, s.Org.Admin, "Maintenance Manager", siteIds: [other]);
        Assert.Empty((await Ok(await outsider.Client.GetAsync("/api/work-groups"))).EnumerateArray());
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/work-groups/{s.Group}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await outsider.Client.PostAsJsonAsync($"/api/work-groups/{s.Group}/assign", new { employeeIds = new[] { s.Me } })).StatusCode);
        Assert.Single((await Ok(await s.Org.Admin.GetAsync("/api/work-groups"))).EnumerateArray());
    }
}
