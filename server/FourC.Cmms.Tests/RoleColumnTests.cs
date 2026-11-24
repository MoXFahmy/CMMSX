using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.RoleHomeTests;

namespace FourC.Cmms.Tests;

/// <summary>Default columns per role, and a person resetting their own (0.22.0).</summary>
public class RoleColumnTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> NewRoleAsync(HttpClient admin, string name, params string[] permissions) =>
        (await Ok(await admin.PostAsJsonAsync("/api/roles", new { name, permissions = permissions.Length > 0 ? permissions : ["workorders.view"] }))).GetProperty("id").GetGuid();

    private async Task<(HttpClient Client, Guid Id)> UserAsync(HttpClient admin, params Guid[] roleIds)
    {
        var email = Addr("user");
        var id = (await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Person", email, isActive = true, roleIds, password = "Passw0rd!u" }))).GetProperty("id").GetGuid();
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return (c, id);
    }

    private static Task<HttpResponseMessage> SetRole(HttpClient admin, Guid role, string list, string[] hidden, bool resetMembers = false) =>
        admin.PutAsJsonAsync($"/api/roles/{role}/columns/{list}", new { hiddenColumns = hidden, resetMembers });

    private static Task<HttpResponseMessage> SetOwn(HttpClient c, string list, params string[] hidden) =>
        c.PutAsJsonAsync($"/api/auth/me/list-settings/{list}", new { hiddenColumns = hidden });

    private static async Task<JsonElement> Settings(HttpClient c) => await Ok(await c.GetAsync("/api/auth/me/list-settings"));

    private static (string[] Hidden, bool FromRole) Entry(JsonElement settings, string list)
    {
        var e = settings.GetProperty(list);
        return (Strings(e.GetProperty("hiddenColumns")), e.GetProperty("fromRole").GetBoolean());
    }

    private static void Is((string[] Hidden, bool FromRole) entry, string[] hidden, bool fromRole)
    {
        Assert.Equal(hidden, entry.Hidden);
        Assert.Equal(fromRole, entry.FromRole);
    }

    [SkippableFact]
    public async Task Role_default_columns_apply_until_the_person_picks_their_own()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var bTech = await NewRoleAsync(admin, "B Tech");
        var aLead = await NewRoleAsync(admin, "A Lead");
        await Ok(await SetRole(admin, bTech, "work-orders", ["trade"]));
        await Ok(await SetRole(admin, aLead, "work-orders", ["priority"]));
        await Ok(await SetRole(admin, aLead, "assets", ["+serialNumber"]));
        var (user, _) = await UserAsync(admin, bTech, aLead);

        var s = await Settings(user);
        Is(Entry(s, "work-orders"), ["priority"], true);
        Is(Entry(s, "assets"), ["+serialNumber"], true);

        await Ok(await SetOwn(user, "work-orders", "createdAt"));
        Is(Entry(await Settings(user), "work-orders"), ["createdAt"], false);

        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync("/api/auth/me/list-settings/work-orders")).StatusCode);
        Is(Entry(await Settings(user), "work-orders"), ["priority"], true);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{aLead}/columns/work-orders")).StatusCode);
        Is(Entry(await Settings(user), "work-orders"), ["trade"], true);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{bTech}/columns/work-orders")).StatusCode);
        Assert.False((await Settings(user)).TryGetProperty("work-orders", out _));

        var get = await Ok(await admin.GetAsync($"/api/roles/{aLead}/columns/assets"));
        Assert.Equal(["+serialNumber"], Strings(get.GetProperty("hiddenColumns")));
        get = await Ok(await admin.GetAsync($"/api/roles/{aLead}/columns/work-orders"));
        Assert.Equal(JsonValueKind.Null, get.GetProperty("hiddenColumns").ValueKind);
        var role = await RoleAsync(admin, "A Lead");
        Assert.Equal(["assets"], Strings(role.GetProperty("defaultColumns")));
    }

    [SkippableFact]
    public async Task Setting_a_role_default_can_reset_members()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var tech = await NewRoleAsync(admin, "Techs");
        var other = await NewRoleAsync(admin, "Others");
        var (m1, _) = await UserAsync(admin, tech);
        var (m2, _) = await UserAsync(admin, tech);
        var (m3, _) = await UserAsync(admin, tech);
        var (outsider, _) = await UserAsync(admin, other);
        await Ok(await SetOwn(m1, "work-orders", "trade"));
        await Ok(await SetOwn(m2, "work-orders", "site"));
        await Ok(await SetOwn(m2, "assets", "site"));
        await Ok(await SetOwn(m3, "assets", "site"));
        await Ok(await SetOwn(outsider, "work-orders", "site"));

        var get = await Ok(await admin.GetAsync($"/api/roles/{tech}/columns/work-orders"));
        Assert.Equal(2, get.GetProperty("membersWithOwn").GetInt32());

        var put = await Ok(await SetRole(admin, tech, "work-orders", ["priority"]));
        Assert.Equal(0, put.GetProperty("resetCount").GetInt32());
        Is(Entry(await Settings(m1), "work-orders"), ["trade"], false);

        put = await Ok(await SetRole(admin, tech, "work-orders", ["priority"], resetMembers: true));
        Assert.Equal(2, put.GetProperty("resetCount").GetInt32());
        Is(Entry(await Settings(m1), "work-orders"), ["priority"], true);
        Is(Entry(await Settings(m2), "work-orders"), ["priority"], true);
        Is(Entry(await Settings(m2), "assets"), ["site"], false);
        Is(Entry(await Settings(outsider), "work-orders"), ["site"], false);
        get = await Ok(await admin.GetAsync($"/api/roles/{tech}/columns/work-orders"));
        Assert.Equal(0, get.GetProperty("membersWithOwn").GetInt32());
    }

    [SkippableFact]
    public async Task Deleting_a_role_removes_its_default_columns()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var keep = await NewRoleAsync(admin, "Keep");
        var gone = await NewRoleAsync(admin, "A Gone");
        await Ok(await SetRole(admin, gone, "work-orders", ["trade"]));
        var (user, userId) = await UserAsync(admin, gone, keep);
        Is(Entry(await Settings(user), "work-orders"), ["trade"], true);

        var roleIds = new[] { keep };
        await Ok(await admin.PutAsJsonAsync($"/api/users/{userId}", new { fullName = "Person", email = Addr("moved"), isActive = true, roleIds }));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{gone}")).StatusCode);
        Assert.False((await Settings(user)).TryGetProperty("work-orders", out _));
        await Ok(await user.GetAsync("/api/auth/me"));
        Assert.Empty(Strings((await RoleAsync(admin, "Keep")).GetProperty("defaultColumns")));
    }

    [SkippableFact]
    public async Task Role_columns_need_users_manage()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var manager = await RoleAsync(admin, "Maintenance Manager");
        var tech = await RoleAsync(admin, "Technician");
        var (m, _) = await UserAsync(admin, manager.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await SetRole(m, tech.GetProperty("id").GetGuid(), "work-orders", ["trade"])).StatusCode);
        var bad = await SetRole(admin, tech.GetProperty("id").GetGuid(), "nope", ["trade"]);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.DeleteAsync("/api/auth/me/list-settings/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetRole(admin, Guid.NewGuid(), "work-orders", ["trade"])).StatusCode);
    }
}
