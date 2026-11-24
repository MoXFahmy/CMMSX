using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.RoleHomeTests;

namespace FourC.Cmms.Tests;

/// <summary>Saved views pinned to the home page as counters, and the unassigned work filter their cards use (0.22.0).</summary>
public class HomePinTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> ViewAsync(HttpClient c, string name, string list = "work-orders", string query = "priority=High") =>
        (await Ok(await c.PostAsJsonAsync("/api/views", new { list, name, query, hiddenColumns = Array.Empty<string>(), isDefault = false }))).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> Pin(HttpClient c, Guid id, bool pinned = true) => c.PutAsJsonAsync($"/api/views/{id}/pin", new { pinned });

    private static async Task<Guid[]> HomeAsync(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/views/home"))).EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToArray();

    [SkippableFact]
    public async Task Pins_round_trip_in_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await OrgAsync(fx);
        var a = await ViewAsync(c, "A");
        var b = await ViewAsync(c, "B", "assets", "status=Active");
        var x = await ViewAsync(c, "C");
        var pinned = await Ok(await Pin(c, a));
        Assert.True(pinned.GetProperty("pinnedToHome").GetBoolean());
        await Ok(await Pin(c, b));
        await Ok(await Pin(c, x));
        Assert.Equal([a, b, x], await HomeAsync(c));
        var first = (await Ok(await c.GetAsync("/api/views/home"))).EnumerateArray().First(v => v.GetProperty("id").GetGuid() == b);
        Assert.Equal("assets", first.GetProperty("list").GetString());
        Assert.Equal("B", first.GetProperty("name").GetString());
        Assert.Equal("status=Active", first.GetProperty("query").GetString());
        var listed = (await Ok(await c.GetAsync("/api/views?list=work-orders"))).EnumerateArray().First(v => v.GetProperty("id").GetGuid() == a);
        Assert.True(listed.GetProperty("pinnedToHome").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/views/home-order", new { ids = new[] { x, a, Guid.NewGuid() } })).StatusCode);
        Assert.Equal([x, a, b], await HomeAsync(c));
        await Ok(await Pin(c, a, false));
        Assert.Equal([x, b], await HomeAsync(c));
        await Ok(await c.DeleteAsync($"/api/views/{x}"));
        Assert.Equal([b], await HomeAsync(c));
        // Pinning again puts it last.
        await Ok(await Pin(c, a));
        Assert.Equal([b, a], await HomeAsync(c));
    }

    [SkippableFact]
    public async Task Pins_are_capped_at_24()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await OrgAsync(fx);
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++) ids.Add(await ViewAsync(c, $"V{i}", i % 2 == 0 ? "work-orders" : "parts", $"search=v{i}"));
        foreach (var id in ids.Take(24)) await Ok(await Pin(c, id));
        var r = await Pin(c, ids[24]);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("You can pin up to 24 views to your home page. Unpin one first.", await r.Content.ReadAsStringAsync());
        // Pinning one that is already pinned isn't a 25th.
        await Ok(await Pin(c, ids[0]));
        await Ok(await Pin(c, ids[3], false));
        await Ok(await Pin(c, ids[24]));
        Assert.Equal(24, (await HomeAsync(c)).Length);
    }

    [SkippableFact]
    public async Task Pins_are_personal()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var mine = await ViewAsync(admin, "Mine");
        await Ok(await Pin(admin, mine));
        var email = Addr("other");
        var roleIds = new[] { (await RoleAsync(admin, "Technician")).GetProperty("id").GetGuid() };
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Other", email, isActive = true, roleIds, password = "Passw0rd!u" }));
        var other = fx.Client();
        await Ok(await other.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        Assert.Equal(HttpStatusCode.NotFound, (await Pin(other, mine)).StatusCode);
        Assert.Empty(await HomeAsync(other));
        Assert.Equal(HttpStatusCode.NoContent, (await other.PutAsJsonAsync("/api/views/home-order", new { ids = new[] { mine } })).StatusCode);
        Assert.Equal([mine], await HomeAsync(admin));
    }

    [SkippableFact]
    public async Task Unassigned_filter_counts_work_with_no_person()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await OrgAsync(fx);
        var user = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var site = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Site", isActive = true }))).GetProperty("id").GetGuid();
        var me = (await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Admin", isContractor = false, userId = user, siteIds = new[] { site } }))).GetProperty("id").GetGuid();
        var team = (await Ok(await c.PostAsJsonAsync("/api/teams", new { code = "T1", name = "Team one", memberIds = Array.Empty<Guid>() }))).GetProperty("id").GetGuid();
        async Task Wo(object body) => await Ok(await c.PostAsJsonAsync("/api/work-orders", body));
        await Wo(new { title = "Assigned", siteId = site, type = "Corrective", priority = "Medium", assigneeIds = new[] { me }, tasks = Array.Empty<string>() });
        await Wo(new { title = "Team only", siteId = site, type = "Corrective", priority = "Medium", teamId = team, tasks = Array.Empty<string>() });
        await Wo(new { title = "Nobody", siteId = site, type = "Corrective", priority = "Medium", tasks = Array.Empty<string>() });

        var list = await Ok(await c.GetAsync("/api/work-orders?unassigned=true"));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.Equal(["Nobody", "Team only"], list.GetProperty("items").EnumerateArray().Select(w => w.GetProperty("title").GetString()).Order());
        Assert.Equal(0, (await Ok(await c.GetAsync("/api/work-orders?unassigned=true&mine=true"))).GetProperty("total").GetInt32());
        Assert.Equal(3, (await Ok(await c.GetAsync("/api/work-orders"))).GetProperty("total").GetInt32());
    }

    [SkippableFact]
    public async Task Due_soonest_sort_puts_undated_work_last()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await OrgAsync(fx);
        var site = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Site", isActive = true }))).GetProperty("id").GetGuid();
        async Task Wo(string title, string? dueDate) =>
            await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, siteId = site, type = "Corrective", priority = "Medium", dueDate, tasks = Array.Empty<string>() }));
        await Wo("Undated", null);
        await Wo("Later", DateTime.UtcNow.Date.AddDays(5).ToString("yyyy-MM-dd"));
        await Wo("Sooner", DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd"));
        var list = await Ok(await c.GetAsync("/api/work-orders?sort=dueSoonest&dir=asc"));
        Assert.Equal(["Sooner", "Later", "Undated"], list.GetProperty("items").EnumerateArray().Select(w => w.GetProperty("title").GetString()));
    }
}
