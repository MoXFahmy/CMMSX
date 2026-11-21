using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportAssetTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] AssetHeadings = ["Asset number", "Name", "Site code", "Location code", "Parent asset number", "Category code"];

    private static Sheet Places() => new("Sites", SiteHeadings, ["CAI", "Cairo", null], ["ALX", "Alexandria", null]);
    private static Sheet Rooms() => new("Locations", LocationHeadings, ["CAI", "B1", "Building 1", null], ["ALX", "W1", "Warehouse", null]);

    private static async Task<List<JsonElement>> Assets(HttpClient c) =>
        (await OkAsync(await c.GetAsync("/api/assets?pageSize=100"))).GetProperty("items").EnumerateArray().ToList();

    [SkippableFact]
    public async Task Assets_are_added_with_their_place_parent_and_category()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book(Places(), Rooms(), new Sheet("Assets", AssetHeadings,
            ["MOTOR-01", "Pump motor", "CAI", "B1", "PUMP-01", null],
            ["PUMP-01", "Main pump", "CAI", "B1", null, "PUMPS"])));

        var sheet = SheetOf(result, "Assets");
        Assert.Equal((2, 0), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.Contains("Categories PUMPS", result.GetProperty("newLookups").EnumerateArray().Select(x => x.GetString()));
        var assets = await Assets(c);
        var pump = assets.Single(a => a.GetProperty("assetNumber").GetString() == "PUMP-01");
        var motor = await OkAsync(await c.GetAsync($"/api/assets/{assets.Single(a => a.GetProperty("assetNumber").GetString() == "MOTOR-01").GetProperty("id")}"));
        Assert.Equal(pump.GetProperty("id").GetGuid(), motor.GetProperty("parentAssetId").GetGuid());
    }

    [SkippableFact]
    public async Task A_blank_asset_number_is_numbered_and_always_new()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(Places()));
        var file = Book(new Sheet("Assets", ["Asset number", "Name", "Site code"], [null, "Fan", "CAI"]));
        await RunAsync(c, file);
        var second = SheetOf(await RunAsync(c, file), "Assets");
        Assert.Equal(1, Count(second, "new"));
        Assert.Equal(["AST-00001", "AST-00002"], (await Assets(c)).Select(a => a.GetProperty("assetNumber").GetString()).Order());
    }

    [SkippableFact]
    public async Task Numbers_set_aside_for_problem_rows_are_given_back()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(Places()));
        string[] headings = ["Asset number", "Name", "Site code", "Location code"];
        // The last row's location doesn't exist: it is a problem only once its row is checked.
        await RunAsync(c, Book(new Sheet("Assets", headings, [null, "Fan", "CAI", null], [null, "Pump", "CAI", null], [null, "Ghost", "CAI", "NOPE"])));
        await OkAsync(await c.PostAsJsonAsync("/api/assets", new { name = "Made on screen", siteId = (await OkAsync(await c.GetAsync("/api/sites")))[0].GetProperty("id"),
            status = "Active", criticality = "Medium", warrantyType = "None" }));
        Assert.Equal(["AST-00001", "AST-00002", "AST-00003"], (await Assets(c)).Select(a => a.GetProperty("assetNumber").GetString()).Order());
    }

    [SkippableFact]
    public async Task Moving_an_asset_records_a_transfer()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(Places(), Rooms(), new Sheet("Assets", AssetHeadings, ["PUMP-01", "Main pump", "CAI", "B1", null, null])));
        var moved = SheetOf(await RunAsync(c, Book(new Sheet("Assets", AssetHeadings, ["PUMP-01", null, "ALX", "W1", null, null]))), "Assets");
        Assert.Equal(1, Count(moved, "changed"));
        var id = (await Assets(c)).Single().GetProperty("id");
        var transfers = await OkAsync(await c.GetAsync($"/api/assets/{id}/transfers"));
        Assert.Equal(1, transfers.GetArrayLength());
        Assert.Equal("Alexandria", transfers[0].GetProperty("toSite").GetString());
    }

    [SkippableFact]
    public async Task Custom_fields_are_read_and_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await OkAsync(await c.PostAsJsonAsync("/api/custom-fields", new { entityType = "Asset", key = "power", label = "Power (kW)", dataType = "Number", isRequired = true, sortOrder = 1, showInList = false }));
        await RunAsync(c, Book(Places()));
        string[] headings = ["Asset number", "Name", "Site code", "Power (kW)"];

        var missing = SheetOf(await CheckAsync(c, Book(new Sheet("Assets", headings, ["PUMP-01", "Main pump", "CAI", null]))), "Assets");
        Assert.Contains("Power (kW) is required.", ProblemsOf(missing));

        await RunAsync(c, Book(new Sheet("Assets", headings, ["PUMP-01", "Main pump", "CAI", 7.5m])));
        var asset = await OkAsync(await c.GetAsync($"/api/assets/{(await Assets(c)).Single().GetProperty("id")}"));
        Assert.Equal("7.5", asset.GetProperty("customFields").GetProperty("power").GetString());
    }

    [SkippableFact]
    public async Task People_get_trades_and_sites_by_code()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        string[] people = ["Code", "Full name", "Trades", "Sites"];
        var result = await RunAsync(c, Book(Places(), new Sheet("Trades", ["Code", "Name"], ["ELEC", "Electrical"]),
            new Sheet("People", people, ["E1", "Ahmed Hassan", "ELEC, WELD", "CAI"], ["E2", "Mona Adel", null, "XX"])));
        var sheet = SheetOf(result, "People");
        Assert.Equal((1, 1), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.Contains("Site XX not found.", ProblemsOf(sheet));
        Assert.Contains("Trades WELD", result.GetProperty("newLookups").EnumerateArray().Select(x => x.GetString()));
        var e1 = (await OkAsync(await c.GetAsync("/api/employees"))).EnumerateArray().Single(e => e.GetProperty("code").GetString() == "E1");
        Assert.Equal(2, e1.GetProperty("tradeIds").GetArrayLength());
        Assert.Equal(1, e1.GetProperty("siteIds").GetArrayLength());
    }
}
