using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

/// <summary>People limited to some sites import only into those sites (0.9.0).</summary>
public class ImportAccessTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] AssetHeadings = ["Asset number", "Name", "Site code", "Location code"];

    [SkippableFact]
    public async Task A_limited_user_cannot_import_into_another_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await SiteAccessTests.WorldAsync(fx, "Import Limit Co");
        var (c, _) = await SiteAccessTests.UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var sheet = SheetOf(await RunAsync(c, Book(new Sheet("Assets", AssetHeadings, ["X-1", "Thing", "B", null], ["X-2", "Mine", "A", null]))), "Assets");
        Assert.Equal((1, 1), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.Contains(ProblemsOf(sheet), p => p.Contains("Site B not found."));
        var numbers = (await OkAsync(await w.Admin.GetAsync("/api/assets?pageSize=100"))).GetProperty("items").EnumerateArray().Select(a => a.GetProperty("assetNumber").GetString()).ToList();
        Assert.DoesNotContain("X-1", numbers);
        Assert.Contains("X-2", numbers);
    }

    [SkippableFact]
    public async Task Importing_a_code_used_in_a_hidden_site_is_a_problem_row()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await SiteAccessTests.WorldAsync(fx, "Import Hidden Co");
        var (c, _) = await SiteAccessTests.UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var result = await RunAsync(c, Book(
            new Sheet("Stores", ["Code", "Name", "Site code"], ["STB", "Copy of B's store", "A"]),
            new Sheet("Assets", AssetHeadings, [w.B.AssetNumber, "Copy", "A", null])));
        var assets = SheetOf(result, "Assets");
        Assert.Equal(1, Count(assets, "problems"));
        Assert.Contains($"{w.B.AssetNumber} is used in a site you can't see.", ProblemsOf(assets));
        Assert.Equal(1, Count(SheetOf(result, "Stores"), "problems"));
        // B's asset is untouched.
        Assert.Equal("Pump B", (await OkAsync(await w.Admin.GetAsync($"/api/assets/{w.B.Asset}"))).GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task Current_data_holds_only_visible_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await SiteAccessTests.WorldAsync(fx, "Import Current Co");
        var (c, _) = await SiteAccessTests.UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var current = await (await c.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var check = await CheckAsync(c, current);
        Assert.Equal(1, Count(SheetOf(check, "Assets"), "unchanged"));
        Assert.Equal(1, Count(SheetOf(check, "Sites"), "unchanged"));
        Assert.Equal(0, Count(SheetOf(check, "Assets"), "problems"));
    }

    [SkippableFact]
    public async Task Limited_people_cannot_add_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await SiteAccessTests.WorldAsync(fx, "No New Sites Co");
        var (c, _) = await SiteAccessTests.UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var r = await c.PostAsJsonAsync("/api/sites", new { code = "NEW", name = "New site", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Only people who see every site can add sites.", await r.Content.ReadAsStringAsync());
        var sheet = SheetOf(await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["NEW", "New site", null], ["B", "Renamed B", null]))), "Sites");
        Assert.Equal(2, Count(sheet, "problems"));
        Assert.Contains("Only people who see every site can add sites.", ProblemsOf(sheet));
        Assert.Contains("B is used in a site you can't see.", ProblemsOf(sheet));
    }
}
