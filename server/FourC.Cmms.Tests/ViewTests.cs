using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class ViewTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";

    private async Task<HttpClient> OrgAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Views Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        return c;
    }

    private async Task<HttpClient> UserAsync(HttpClient admin, string role = "Maintenance Manager")
    {
        var email = Addr("user");
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    private static Task<HttpResponseMessage> NewView(HttpClient c, string name, string list = "work-orders", string query = "priority=High", bool isDefault = false,
        string[]? hidden = null) =>
        c.PostAsJsonAsync("/api/views", new { list, name, query, hiddenColumns = hidden ?? ["trade"], isDefault });

    private static async Task<JsonElement[]> Views(HttpClient c, string list = "work-orders") =>
        (await Ok(await c.GetAsync($"/api/views?list={list}"))).EnumerateArray().ToArray();

    [SkippableFact]
    public async Task A_view_round_trips()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        var id = Id(await Ok(await NewView(admin, "Urgent at Cairo", query: "priority=Urgent&siteId=x&sort=dueDate&dir=asc", hidden: ["trade", "createdAt"])));
        var v = Assert.Single(await Views(admin));
        Assert.Equal(id, Id(v));
        Assert.Equal("Urgent at Cairo", v.GetProperty("name").GetString());
        Assert.Equal("priority=Urgent&siteId=x&sort=dueDate&dir=asc", v.GetProperty("query").GetString());
        Assert.Equal(["trade", "createdAt"], v.GetProperty("hiddenColumns").EnumerateArray().Select(x => x.GetString()));
        await Ok(await admin.PutAsJsonAsync($"/api/views/{id}", new { name = "Urgent", query = "priority=Urgent", hiddenColumns = Array.Empty<string>(), isDefault = false }));
        v = Assert.Single(await Views(admin));
        Assert.Equal("Urgent", v.GetProperty("name").GetString());
        Assert.Empty(v.GetProperty("hiddenColumns").EnumerateArray());
        await Ok(await admin.DeleteAsync($"/api/views/{id}"));
        Assert.Empty(await Views(admin));
    }

    [SkippableFact]
    public async Task Views_are_personal()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        var other = await UserAsync(admin);
        var id = Id(await Ok(await NewView(admin, "Mine")));
        Assert.Empty(await Views(other));
        var put = await other.PutAsJsonAsync($"/api/views/{id}", new { name = "Hijacked", query = "", hiddenColumns = Array.Empty<string>(), isDefault = true });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/views/{id}")).StatusCode);
        Assert.Equal("Mine", Assert.Single(await Views(admin)).GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task View_names_are_unique_per_list_ignoring_case()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        await Ok(await NewView(admin, "Mine"));
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, "  mine ")).StatusCode);
        await Ok(await NewView(admin, "mine", list: "parts"));
        var second = Id(await Ok(await NewView(admin, "Other")));
        var rename = await admin.PutAsJsonAsync($"/api/views/{second}", new { name = "MINE", query = "", hiddenColumns = Array.Empty<string>(), isDefault = false });
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
    }

    [SkippableFact]
    public async Task One_default_per_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        await Ok(await NewView(admin, "A", isDefault: true));
        await Ok(await NewView(admin, "On parts", list: "parts", isDefault: true));
        var b = Id(await Ok(await NewView(admin, "B", isDefault: true)));
        var views = await Views(admin);
        Assert.Equal(["B"], views.Where(v => v.GetProperty("isDefault").GetBoolean()).Select(v => v.GetProperty("name").GetString()));
        await Ok(await admin.PutAsJsonAsync($"/api/views/{b}", new { name = "B", query = "", hiddenColumns = Array.Empty<string>(), isDefault = false }));
        Assert.DoesNotContain(await Views(admin), v => v.GetProperty("isDefault").GetBoolean());
        Assert.True(Assert.Single(await Views(admin, "parts")).GetProperty("isDefault").GetBoolean());
    }

    [SkippableFact]
    public async Task View_limits()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, new string('x', 61))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, "Long", query: new string('q', 2001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, "Nope", list: "nope")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, "Cols", hidden: Enumerable.Range(0, 101).Select(i => $"c{i}").ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/views?list=nope")).StatusCode);
        for (var i = 0; i < 50; i++) await Ok(await NewView(admin, $"View {i}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await NewView(admin, "View 51")).StatusCode);
    }

    [SkippableFact]
    public async Task List_settings_round_trip_and_are_personal()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync();
        var other = await UserAsync(admin);
        await Ok(await admin.PutAsJsonAsync("/api/auth/me/list-settings/work-orders", new { hiddenColumns = new[] { "trade", "completedAt" } }));
        await Ok(await admin.PutAsJsonAsync("/api/auth/me/list-settings/work-orders", new { hiddenColumns = new[] { "trade" } }));
        var mine = await Ok(await admin.GetAsync("/api/auth/me/list-settings"));
        Assert.Equal(["trade"], mine.GetProperty("work-orders").GetProperty("hiddenColumns").EnumerateArray().Select(x => x.GetString()));
        var theirs = await Ok(await other.GetAsync("/api/auth/me/list-settings"));
        Assert.Empty(theirs.EnumerateObject());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/auth/me/list-settings/nope", new { hiddenColumns = Array.Empty<string>() })).StatusCode);
    }

    [SkippableFact]
    public async Task Quick_column_changes_on_a_new_list_all_save()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        for (var round = 0; round < 3; round++)
        {
            var admin = await OrgAsync();
            // Ticking columns quickly sends saves side by side; the first ones all find no saved row yet.
            var saves = Enumerable.Range(0, 6).Select(i => admin.PutAsJsonAsync("/api/auth/me/list-settings/parts", new { hiddenColumns = new[] { $"c{i}" } })).ToList();
            foreach (var r in await Task.WhenAll(saves)) await Ok(r);
            var mine = await Ok(await admin.GetAsync("/api/auth/me/list-settings"));
            Assert.Single(mine.GetProperty("parts").GetProperty("hiddenColumns").EnumerateArray());
        }
    }
}
