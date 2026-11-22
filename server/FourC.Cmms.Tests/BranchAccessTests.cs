using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>People limited to branches of the location tree see those places and everything under them (0.9.0).</summary>
public class BranchAccessTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record Tree(HttpClient Admin, Guid Site, Guid B1, Guid F1, Guid R1, Guid B2, Guid AssetR1, Guid AssetB2, Guid WoR1, Guid WoB2, Guid WoSite);

    private async Task<Tree> TreeAsync(string org)
    {
        var admin = fx.Client();
        await Ok(await admin.PostAsJsonAsync("/api/auth/register", new { organizationName = org, fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await admin.PostAsJsonAsync("/api/sites", new { code = "S", name = "Hotel", isActive = true })));
        async Task<Guid> Loc(Guid? parent, string code, string type) =>
            Id(await Ok(await admin.PostAsJsonAsync("/api/locations", new { siteId = site, parentId = parent, code, name = code, type, criticality = "Medium", isActive = true })));
        var b1 = await Loc(null, "B1", "Building");
        var f1 = await Loc(b1, "F1", "Floor");
        var r1 = await Loc(f1, "R1", "Room");
        var b2 = await Loc(null, "B2", "Building");
        async Task<Guid> Asset(Guid loc, string name) =>
            Id(await Ok(await admin.PostAsJsonAsync("/api/assets", new { name, siteId = site, locationId = loc, status = "Active", criticality = "Low", warrantyType = "None" })));
        var aR1 = await Asset(r1, "AC R1");
        var aB2 = await Asset(b2, "AC B2");
        async Task<Guid> Wo(Guid? loc, string title) =>
            Id(await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title, siteId = site, locationId = loc, type = "Corrective", priority = "Low" })));
        return new Tree(admin, site, b1, f1, r1, b2, aR1, aB2, await Wo(r1, "Room job"), await Wo(b2, "Other building job"), await Wo(null, "Site job"));
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient c, string path)
    {
        var e = await Ok(await c.GetAsync(path));
        var items = e.ValueKind == JsonValueKind.Array ? e : e.GetProperty("items");
        return items.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
    }

    [SkippableFact]
    public async Task A_branch_limited_user_sees_only_their_branches()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var t = await TreeAsync("Branch Co");
        var (c, _) = await UserAsync(fx, t.Admin, "Maintenance Manager", locationIds: [t.B1]);
        Assert.Equal([t.Site], await IdsAsync(c, "/api/sites"));
        Assert.Equal(new[] { t.B1, t.F1, t.R1 }.Order(), (await IdsAsync(c, "/api/locations")).Order());
        Assert.Equal([t.AssetR1], await IdsAsync(c, "/api/assets"));
        Assert.Equal([t.WoR1], await IdsAsync(c, "/api/work-orders"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/assets/{t.AssetB2}")).StatusCode);
    }

    [SkippableFact]
    public async Task New_locations_under_a_branch_are_included()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var t = await TreeAsync("Growing Co");
        var (c, _) = await UserAsync(fx, t.Admin, "Maintenance Manager", locationIds: [t.B1]);
        Assert.Equal(3, (await IdsAsync(c, "/api/locations")).Count);
        var r2 = Id(await Ok(await t.Admin.PostAsJsonAsync("/api/locations", new { siteId = t.Site, parentId = t.F1, code = "R2", name = "R2", type = "Room", criticality = "Medium", isActive = true })));
        Assert.Contains(r2, await IdsAsync(c, "/api/locations"));
    }

    [SkippableFact]
    public async Task Site_level_work_is_hidden_from_branch_limited_users()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var t = await TreeAsync("Site Work Co");
        var (c, _) = await UserAsync(fx, t.Admin, "Maintenance Manager", locationIds: [t.B1]);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/work-orders/{t.WoSite}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await t.Admin.GetAsync($"/api/work-orders/{t.WoSite}")).StatusCode);
    }

    [SkippableFact]
    public async Task Branch_limited_users_must_choose_a_location()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var t = await TreeAsync("Choose Co");
        var (c, _) = await UserAsync(fx, t.Admin, "Maintenance Manager", locationIds: [t.B1]);
        var none = await c.PostAsJsonAsync("/api/work-orders", new { title = "Where?", siteId = t.Site, type = "Corrective", priority = "Low" });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Contains("Choose a location in your part of the site.", await none.Content.ReadAsStringAsync());
        await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Room", siteId = t.Site, locationId = t.R1, type = "Corrective", priority = "Low" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/work-orders", new { title = "Not mine", siteId = t.Site, locationId = t.B2, type = "Corrective", priority = "Low" })).StatusCode);
        // The same for requests and assets.
        var request = await c.PostAsJsonAsync("/api/work-requests", new { title = "Where?", siteId = t.Site, priority = "Low", type = "Corrective" });
        Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
        var asset = await c.PostAsJsonAsync("/api/assets", new { name = "Loose", siteId = t.Site, status = "Active", criticality = "Low", warrantyType = "None" });
        Assert.Equal(HttpStatusCode.BadRequest, asset.StatusCode);
    }

    [SkippableFact]
    public async Task A_user_whose_branches_are_gone_sees_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var t = await TreeAsync("Gone Co");
        var (c, _) = await UserAsync(fx, t.Admin, "Maintenance Manager", locationIds: [t.B1]);
        Assert.NotEmpty(await IdsAsync(c, "/api/locations"));
        Assert.Equal(HttpStatusCode.NoContent, (await t.Admin.DeleteAsync($"/api/assets/{t.AssetR1}")).StatusCode);
        foreach (var loc in new[] { t.R1, t.F1, t.B1 })
            Assert.Equal(HttpStatusCode.NoContent, (await t.Admin.DeleteAsync($"/api/locations/{loc}")).StatusCode);
        Assert.Empty(await IdsAsync(c, "/api/sites"));
        Assert.Empty(await IdsAsync(c, "/api/locations"));
        Assert.Empty(await IdsAsync(c, "/api/work-orders"));
    }
}
