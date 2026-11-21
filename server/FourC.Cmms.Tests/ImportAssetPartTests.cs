using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

/// <summary>The Asset parts sheet and the assets' running-time columns (0.8.0).</summary>
public class ImportAssetPartTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] Headings = ["Asset number", "Part number", "Quantity", "Note"];

    private static Sheet[] Basics() =>
    [
        new("Sites", SiteHeadings, ["CAI", "Cairo", null]),
        new("Assets", ["Asset number", "Name", "Site code"], ["PUMP-01", "Main pump", "CAI"], ["FAN-02", "Roof fan", "CAI"]),
        new("Parts", ["Part number", "Name", "Unit"], ["P1", "Ball bearing", "EA"], ["P2", "Seal kit", "EA"]),
    ];

    private static async Task<Guid> AssetIdAsync(HttpClient c, string number) =>
        (await OkAsync(await c.GetAsync($"/api/assets?search={number}"))).GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("assetNumber").GetString() == number).GetProperty("id").GetGuid();

    private static async Task<List<string>> PartsOfAsync(HttpClient c, string number) =>
        (await OkAsync(await c.GetAsync($"/api/assets/{await AssetIdAsync(c, number)}/parts"))).EnumerateArray()
            .Select(l => $"{l.GetProperty("partNumber").GetString()} × {l.GetProperty("quantity").GetDecimal():0.###}").ToList();

    [SkippableFact]
    public async Task Asset_parts_load_and_replace_per_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var first = await RunAsync(c, Book([.. Basics(),
            new Sheet("Asset parts", Headings, ["PUMP-01", "P1", 2m, "Drive end"], ["PUMP-01", "P2", 1m, null], ["FAN-02", "P1", 1m, null])]));
        Assert.Equal(3, Count(SheetOf(first, "AssetParts"), "new"));
        Assert.Equal(["P1 × 2", "P2 × 1"], await PartsOfAsync(c, "PUMP-01"));

        var second = await RunAsync(c, Book(new Sheet("Asset parts", Headings, ["PUMP-01", "P1", 3m, "Drive end"])));
        var sheet = SheetOf(second, "AssetParts");
        Assert.Equal(1, Count(sheet, "changed"));
        var changes = sheet.GetProperty("rows")[0].GetProperty("changes").EnumerateArray()
            .Select(ch => (ch.GetProperty("column").GetString(), ch.GetProperty("old").GetString(), ch.GetProperty("new").GetString())).ToList();
        Assert.Contains(("Parts", "P1 × 2; P2 × 1", "P1 × 3"), changes);
        Assert.Equal(["P1 × 3"], await PartsOfAsync(c, "PUMP-01"));
        Assert.Equal(["P1 × 1"], await PartsOfAsync(c, "FAN-02"));
    }

    [SkippableFact]
    public async Task A_problem_row_keeps_the_assets_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(), new Sheet("Asset parts", Headings, ["PUMP-01", "P1", 2m, null], ["PUMP-01", "P2", 1m, null])]));

        var result = await RunAsync(c, Book(new Sheet("Asset parts", Headings, ["PUMP-01", "P1", 5m, null], ["PUMP-01", "NOPE", 1m, null])));
        var sheet = SheetOf(result, "AssetParts");
        Assert.Equal(2, Count(sheet, "problems"));
        Assert.Contains("Not saved: fix row 3 of this asset first.", ProblemsOf(sheet));
        Assert.Contains("Part NOPE not found.", ProblemsOf(sheet));
        Assert.Equal(["P1 × 2", "P2 × 1"], await PartsOfAsync(c, "PUMP-01"));
    }

    [SkippableFact]
    public async Task Running_time_imports_and_exports()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        string[] assetHeadings = ["Asset number", "Name", "Site code", "Runs hours a day", "Runs on"];
        var result = await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]),
            new Sheet("Assets", assetHeadings, ["KILN-1", "Kiln", "CAI", 16m, "Sunday, Monday, الثلاثاء"], ["KILN-2", "Kiln 2", "CAI", 25m, null],
                ["KILN-3", "Kiln 3", "CAI", null, "Funday"]),
            new Sheet("Parts", ["Part number", "Name", "Unit"], ["P1", "Ball bearing", "EA"]),
            new Sheet("Asset parts", Headings, ["KILN-1", "P1", 4m, "Burner nozzle"])));
        var assets = SheetOf(result, "Assets");
        Assert.Equal(1, Count(assets, "new"));
        Assert.Contains("Running hours must be between 0.5 and 24.", ProblemsOf(assets));
        Assert.Contains(ProblemsOf(assets), p => p.StartsWith("Runs on must be one of:"));

        var kiln = await OkAsync(await c.GetAsync($"/api/assets/{await AssetIdAsync(c, "KILN-1")}"));
        Assert.Equal(16m, kiln.GetProperty("runsHoursPerDay").GetDecimal());
        Assert.Equal(["Sunday", "Monday", "Tuesday"], kiln.GetProperty("runsOn").EnumerateArray().Select(d => d.GetString()));

        var current = await (await c.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var check = await CheckAsync(c, current);
        foreach (var sheet in check.GetProperty("sheets").EnumerateArray())
            Assert.True(Count(sheet, "new") == 0 && Count(sheet, "changed") == 0 && Count(sheet, "problems") == 0,
                $"{sheet.GetProperty("sheet").GetString()}: {sheet.GetProperty("rows")}");
        Assert.Equal(1, Count(SheetOf(check, "AssetParts"), "unchanged"));
    }
}
