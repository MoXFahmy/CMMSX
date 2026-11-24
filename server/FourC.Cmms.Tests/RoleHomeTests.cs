using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>A role's home page and hidden menu items (0.22.0).</summary>
public class RoleHomeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    internal static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";

    internal static async Task<HttpClient> OrgAsync(ApiFixture fx)
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Homes Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        return c;
    }

    internal static async Task<JsonElement> RoleAsync(HttpClient admin, string name) =>
        (await Ok(await admin.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == name);

    internal static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static object Body(JsonElement role, object? home = null, object? hiddenModules = null, bool send = true) => send
        ? new { name = role.GetProperty("name").GetString(), description = "d", permissions = Strings(role.GetProperty("permissions")), home, hiddenModules }
        : new { name = role.GetProperty("name").GetString(), description = "d", permissions = Strings(role.GetProperty("permissions")) };

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [SkippableFact]
    public async Task Role_home_and_menu_round_trip()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var tech = await RoleAsync(admin, "Technician");
        var home = new object[]
        {
            new { type = "my-work" },
            new { type = "shortcuts", tiles = new[] { new { label = "New request", labelAr = "طلب جديد", color = "teal", to = "/work-requests?new=1" } } },
        };
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}", Body(tech, home, new[] { "purchasing", "budgets" })));

        tech = await RoleAsync(admin, "Technician");
        var cards = tech.GetProperty("home").EnumerateArray().ToArray();
        Assert.Equal(["my-work", "shortcuts"], cards.Select(c => c.GetProperty("type").GetString()));
        var tile = Assert.Single(cards[1].GetProperty("tiles").EnumerateArray());
        Assert.Equal("New request", tile.GetProperty("label").GetString());
        Assert.Equal("طلب جديد", tile.GetProperty("labelAr").GetString());
        Assert.Equal("teal", tile.GetProperty("color").GetString());
        Assert.Equal("/work-requests?new=1", tile.GetProperty("to").GetString());
        Assert.Equal(["purchasing", "budgets"], Strings(tech.GetProperty("hiddenModules")));

        // An older client sends neither: both stay.
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}", Body(tech, send: false)));
        tech = await RoleAsync(admin, "Technician");
        Assert.Equal(2, tech.GetProperty("home").GetArrayLength());
        Assert.Equal(["purchasing", "budgets"], Strings(tech.GetProperty("hiddenModules")));

        // An empty home is no home.
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}", Body(tech, Array.Empty<object>(), Array.Empty<string>())));
        tech = await RoleAsync(admin, "Technician");
        Assert.Equal(JsonValueKind.Null, tech.GetProperty("home").ValueKind);
        Assert.Empty(Strings(tech.GetProperty("hiddenModules")));

        // A new role can be created with them.
        var created = await Ok(await admin.PostAsJsonAsync("/api/roles", new { name = "Storekeeper", permissions = new[] { "inventory.view" }, home = new[] { new { type = "low-stock" } }, hiddenModules = new[] { "pm" } }));
        var store = await RoleAsync(admin, "Storekeeper");
        Assert.Equal(Id(created), Id(store));
        Assert.Equal("low-stock", store.GetProperty("home")[0].GetProperty("type").GetString());
        Assert.Equal(["pm"], Strings(store.GetProperty("hiddenModules")));
        Assert.Empty(Strings(store.GetProperty("defaultColumns")));
    }

    [SkippableFact]
    public async Task Role_home_is_validated()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var tech = await RoleAsync(admin, "Technician");

        async Task Refused(object? home, object? modules, string message)
        {
            var r = await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}", Body(tech, home, modules));
            var body = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
            Assert.Contains(message, body);
        }
        object Tile(string label = "Go", string? labelAr = null, string color = "blue", string to = "/assets") => new { label, labelAr, color, to };
        object Shortcuts(params object[] tiles) => new { type = "shortcuts", tiles };

        await Refused(new[] { new { type = "charts" } }, null, "Choose a card from the list.");
        var all = new[] { "work-summary", "work-by-site", "urgent-work", "asset-summary", "assets-by-status", "assets-by-category", "assets-by-site",
            "warranty-soon", "recent-assets", "my-work", "unassigned-work", "late-work", "assets-down", "low-stock", "my-approvals", "quick-request",
            "location-tree", "shortcuts" };
        // 21 cards (repeats included) is refused for the count first.
        await Refused(all.Concat(["my-work", "late-work", "low-stock"]).Select(t => new { type = t }).ToArray(), null, "A home page can have at most 20 cards.");
        await Refused(new[] { new { type = "my-work" }, new { type = "my-work" } }, null, "Each card can be on a home page once.");
        await Refused(new object[] { new { type = "my-work", tiles = new[] { Tile() } } }, null, "Only the shortcuts card has tiles.");
        await Refused(new[] { Shortcuts(Enumerable.Range(0, 13).Select(_ => Tile()).ToArray()) }, null, "A shortcuts card can have at most 12 tiles.");
        await Refused(new[] { Shortcuts(Tile(label: " ")) }, null, "Give every tile a label.");
        await Refused(new[] { Shortcuts(Tile(label: new string('x', 41))) }, null, "Keep tile labels to 40 characters.");
        await Refused(new[] { Shortcuts(Tile(labelAr: new string('س', 41))) }, null, "Keep tile labels to 40 characters.");
        await Refused(new[] { Shortcuts(Tile(color: "purple")) }, null, "Choose a colour from the list.");
        foreach (var bad in new[] { "https://x.test", "//x.test", "/\\x.test", "javascript:alert(1)", "work-orders", "/" + new string('a', 300) })
            await Refused(new[] { Shortcuts(Tile(to: bad)) }, null, "A tile link must be a CMX address starting with /.");
        await Refused(null, new[] { "settings" }, "Choose menu items from the list.");

        // Every card once, and a long but valid link, are fine.
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}",
            Body(tech, all.Select(t => t == "shortcuts" ? Shortcuts(Tile(to: "/" + new string('a', 299))) : new { type = t }).ToArray(), new[] { "people" })));
    }

    [SkippableFact]
    public async Task Duplicate_copies_everything_but_people()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var tech = await RoleAsync(admin, "Technician");
        await Ok(await admin.PutAsJsonAsync($"/api/roles/{Id(tech)}", Body(tech, new[] { new { type = "late-work" } }, new[] { "contracts" })));
        var tenantId = (await Ok(await admin.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            db.RoleListSettings.Add(new RoleListSetting { TenantId = tenantId, RoleId = Id(tech), List = ListKeys.WorkOrders, HiddenColumns = ["trade"] });
            await db.SaveChangesAsync();
        }
        // Someone has the role: the copy has nobody.
        var roleIds = new[] { Id(tech) };
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "T", email = Addr("t"), isActive = true, roleIds, password = "Passw0rd!u" }));

        var first = await Ok(await admin.PostAsync($"/api/roles/{Id(tech)}/duplicate", null));
        Assert.Equal("Technician (copy)", first.GetProperty("name").GetString());
        var second = await Ok(await admin.PostAsync($"/api/roles/{Id(tech)}/duplicate", null));
        Assert.Equal("Technician (copy 2)", second.GetProperty("name").GetString());

        tech = await RoleAsync(admin, "Technician");
        var copy = await RoleAsync(admin, "Technician (copy)");
        Assert.Equal(Id(first), Id(copy));
        Assert.Equal(Strings(tech.GetProperty("permissions")).Order(), Strings(copy.GetProperty("permissions")).Order());
        Assert.Equal("late-work", copy.GetProperty("home")[0].GetProperty("type").GetString());
        Assert.Equal(["contracts"], Strings(copy.GetProperty("hiddenModules")));
        Assert.Equal(["work-orders"], Strings(copy.GetProperty("defaultColumns")));
        Assert.Equal(0, copy.GetProperty("userCount").GetInt32());
        Assert.Equal(1, tech.GetProperty("userCount").GetInt32());
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            var cols = await db.RoleListSettings.IgnoreQueryFilters().SingleAsync(x => x.RoleId == Id(first));
            Assert.Equal(["trade"], cols.HiddenColumns);
        }

        var adminRole = await RoleAsync(admin, "Administrator");
        var adminCopy = await Ok(await admin.PostAsync($"/api/roles/{Id(adminRole)}/duplicate", null));
        var ac = await RoleAsync(admin, adminCopy.GetProperty("name").GetString()!);
        Assert.False(ac.GetProperty("isSystem").GetBoolean());
        Assert.Equal(Permissions.All.Select(p => p.Code).Order(), Strings(ac.GetProperty("permissions")).Order());

        // A long name still fits.
        var longName = new string('R', 100);
        var lr = await Ok(await admin.PostAsJsonAsync("/api/roles", new { name = longName, permissions = new[] { "assets.view" } }));
        var lc = await Ok(await admin.PostAsync($"/api/roles/{Id(lr)}/duplicate", null));
        var lcName = lc.GetProperty("name").GetString()!;
        Assert.True(lcName.Length <= 100);
        Assert.EndsWith(" (copy)", lcName);
    }

    [SkippableFact]
    public async Task Menu_modules_list_is_served()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await OrgAsync(fx);
        var list = (await Ok(await admin.GetAsync("/api/roles/menu-modules"))).EnumerateArray().ToArray();
        Assert.Equal(MenuModuleList.Select(m => m.Key), list.Select(m => m.GetProperty("key").GetString()));
        Assert.Equal(MenuModuleList.Select(m => string.Join(",", m.AnyOf)), list.Select(m => string.Join(",", Strings(m.GetProperty("anyOf")))));
    }

    /// <summary>The menu items a role can hide, with the permissions that show them. The web keeps the same list (web/src/components/menuModules.ts); both are pinned to this.</summary>
    internal static readonly (string Key, string[] AnyOf)[] MenuModuleList =
    [
        ("locations", ["locations.view"]),
        ("assets", ["assets.view"]),
        ("work-requests", ["requests.create"]),
        ("work-orders", ["workorders.view"]),
        ("work-groups", ["workorders.view"]),
        ("planning", ["workorders.view"]),
        ("availability", ["workorders.view"]),
        ("timesheet", ["workorders.view"]),
        ("pm", ["pm.view"]),
        ("contracts", ["contracts.view"]),
        ("inventory", ["inventory.view"]),
        ("purchasing", ["purchasing.view", "purchasing.request", "purchasing.approveRequests"]),
        ("approvals", ["purchasing.approve", "purchasing.approveRequests"]),
        ("reports", ["reports.view"]),
        ("budgets", ["reports.view"]),
        ("people", ["people.view"]),
    ];

    [Fact]
    public void Menu_modules_match_the_web_list()
    {
        Assert.Equal(MenuModuleList.Select(m => m.Key), MenuModules.All.Select(m => m.Key));
        Assert.Equal(MenuModuleList.Select(m => string.Join(",", m.AnyOf)), MenuModules.All.Select(m => string.Join(",", m.AnyOf)));
    }
}
