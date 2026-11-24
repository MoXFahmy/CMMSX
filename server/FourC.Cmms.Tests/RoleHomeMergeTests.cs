using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.RoleHomeTests;

namespace FourC.Cmms.Tests;

/// <summary>What a person with several roles gets on their home page and menu (0.22.0).</summary>
public class RoleHomeMergeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> NewRoleAsync(HttpClient admin, string name, string[] permissions, object? home = null, string[]? hidden = null) =>
        (await Ok(await admin.PostAsJsonAsync("/api/roles", new { name, permissions, home, hiddenModules = hidden }))).GetProperty("id").GetGuid();

    private async Task<HttpClient> UserAsync(HttpClient admin, params Guid[] roleIds)
    {
        var email = Addr("user");
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Person", email, isActive = true, roleIds, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    private static async Task<JsonElement> HomeAsync(HttpClient c) => (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("home");
    private static object Tile(string label, string to) => new { label, color = "blue", to };

    [SkippableFact]
    public async Task Home_merges_in_case_insensitive_name_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        string[] perms = ["workorders.view", "assets.view"];
        var beta = await NewRoleAsync(admin, "beta", perms, new object[] { new { type = "late-work" }, new { type = "shortcuts", tiles = new[] { Tile("A", "/assets"), Tile("B", "/pm") } } });
        var alpha = await NewRoleAsync(admin, "Alpha", perms, new object[] { new { type = "my-work" }, new { type = "late-work" }, new { type = "shortcuts", tiles = new[] { Tile("C", "/assets") } } });
        var gamma = await NewRoleAsync(admin, "Gamma", perms);
        var user = await UserAsync(admin, beta, alpha, gamma);

        var cards = (await HomeAsync(user)).GetProperty("cards").EnumerateArray().ToArray();
        Assert.Equal(["my-work", "late-work", "shortcuts", "shortcuts"], cards.Select(c => c.GetProperty("type").GetString()));
        Assert.Equal(["C"], cards[2].GetProperty("tiles").EnumerateArray().Select(t => t.GetProperty("label").GetString()));
        Assert.Equal(["B"], cards[3].GetProperty("tiles").EnumerateArray().Select(t => t.GetProperty("label").GetString()));

        // Renamed, Alpha now comes after beta.
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{alpha}", new { name = "zeta", permissions = perms }));
        cards = (await HomeAsync(user)).GetProperty("cards").EnumerateArray().ToArray();
        Assert.Equal(["late-work", "shortcuts", "my-work"], cards.Select(c => c.GetProperty("type").GetString()));
        Assert.Equal(["A", "B"], cards[1].GetProperty("tiles").EnumerateArray().Select(t => t.GetProperty("label").GetString()));
    }

    [SkippableFact]
    public async Task Home_is_empty_without_role_homes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var plain = await NewRoleAsync(admin, "Plain", ["assets.view"]);
        var home = await HomeAsync(await UserAsync(admin, plain));
        Assert.Empty(home.GetProperty("cards").EnumerateArray());
        Assert.Empty(home.GetProperty("hiddenModules").EnumerateArray());
        // A user always has a role; with none (data from elsewhere), nothing is shown or hidden.
        Assert.Empty(FourC.Cmms.Api.Infrastructure.RoleHomes.Merge([]));
        Assert.Empty(FourC.Cmms.Api.Infrastructure.RoleHomes.Hidden([]));
    }

    [SkippableFact]
    public async Task Menu_items_hide_only_when_every_role_that_can_open_them_hides_them()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        async Task<string[]> HiddenFor(params Guid[] roles) => Strings((await HomeAsync(await UserAsync(admin, roles))).GetProperty("hiddenModules"));

        var r1 = await NewRoleAsync(admin, "R1", ["inventory.view"], hidden: ["inventory"]);
        var r2 = await NewRoleAsync(admin, "R2", ["inventory.view"]);
        var r2NoInventory = await NewRoleAsync(admin, "R2b", ["assets.view"]);
        var r2Hides = await NewRoleAsync(admin, "R2c", ["inventory.view"], hidden: ["inventory"]);
        Assert.DoesNotContain("inventory", await HiddenFor(r1, r2));
        Assert.Contains("inventory", await HiddenFor(r1, r2NoInventory));
        Assert.Contains("inventory", await HiddenFor(r1, r2Hides));
        Assert.Contains("inventory", await HiddenFor(r1));

        var p1 = await NewRoleAsync(admin, "P1", ["purchasing.request"], hidden: ["purchasing"]);
        var p2 = await NewRoleAsync(admin, "P2", ["purchasing.view"]);
        Assert.DoesNotContain("purchasing", await HiddenFor(p1, p2));
        Assert.Contains("purchasing", await HiddenFor(p1));
    }

    [SkippableFact]
    public async Task Administrator_counts_as_able_for_every_menu_item()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var adminRole = (await RoleAsync(admin, "Administrator")).GetProperty("id").GetGuid();
        // An organization from before contracts existed: its stored Administrator list lacks the newer codes.
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FourC.Cmms.Core.Data.CmmsDbContext>();
            var role = await db.Roles.IgnoreQueryFilters().SingleAsync(r => r.Id == adminRole);
            role.Permissions = role.Permissions.Where(p => p != "contracts.view" && p != "contracts.manage").ToList();
            await db.SaveChangesAsync();
        }
        var hider = await NewRoleAsync(admin, "Hider", ["contracts.view"], hidden: ["contracts"]);
        var home = await HomeAsync(await UserAsync(admin, adminRole, hider));
        Assert.DoesNotContain("contracts", Strings(home.GetProperty("hiddenModules")));
    }
}
