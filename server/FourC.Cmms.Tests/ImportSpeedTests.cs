using System.Diagnostics;
using Xunit.Abstractions;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

/// <summary>
/// A full sheet (5,000 rows) must go through well inside the 45-second limit. The bound here is the spec's target for a
/// check of 5,000 assets (20 s); the test database is local, so production has less room than these numbers show.
/// </summary>
public class ImportSpeedTests(ApiFixture fx, ITestOutputHelper output) : IClassFixture<ApiFixture>
{
    private static readonly TimeSpan Target = TimeSpan.FromSeconds(20);

    private async Task<(System.Text.Json.JsonElement Result, TimeSpan Took)> TimedRunAsync(HttpClient c, byte[] file, string what)
    {
        var clock = Stopwatch.StartNew();
        var result = await RunAsync(c, file);
        output.WriteLine($"{what}: {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.True(result.GetProperty("stopped").ValueKind == System.Text.Json.JsonValueKind.Null, $"{what} ran out of time");
        return (result, clock.Elapsed);
    }

    [SkippableFact]
    public async Task Five_thousand_numbered_assets_import_in_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null])));
        var rows = Enumerable.Range(1, 5000).Select(i => new object?[] { null, $"Pump {i}", "CAI" }).ToArray();
        var (result, took) = await TimedRunAsync(c, Book(new Sheet("Assets", ["Asset number", "Name", "Site code"], rows)), "5,000 assets");
        Assert.Equal(5000, Count(SheetOf(result, "Assets"), "new"));
        Assert.True(took < Target, $"took {took.TotalSeconds:0.0} s");
    }

    [SkippableFact]
    public async Task Five_thousand_locations_import_in_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null])));
        // 50 buildings with 99 rooms each.
        var rows = Enumerable.Range(1, 5000).Select(i => i <= 50
            ? new object?[] { "CAI", $"B{i}", $"Building {i}", null }
            : new object?[] { "CAI", $"R{i}", $"Room {i}", $"B{(i % 50) + 1}" }).ToArray();
        var (result, took) = await TimedRunAsync(c, Book(new Sheet("Locations", LocationHeadings, rows)), "5,000 locations");
        Assert.Equal(5000, Count(SheetOf(result, "Locations"), "new"));
        Assert.True(took < Target, $"took {took.TotalSeconds:0.0} s");
    }

    [SkippableFact]
    public async Task Five_thousand_opening_stock_lines_import_in_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]), new Sheet("Stores", ["Code", "Name", "Site code"], ["ST1", "Main", "CAI"]),
            new Sheet("Parts", ["Part number", "Name", "Unit", "Standard price"], Enumerable.Range(1, 5000).Select(i => new object?[] { $"P{i}", $"Part {i}", "EA", 2m }).ToArray())));
        var rows = Enumerable.Range(1, 5000).Select(i => new object?[] { $"P{i}", "ST1", 10m }).ToArray();
        var (result, took) = await TimedRunAsync(c, Book(new Sheet("Stock", ["Part number", "Store code", "Opening quantity"], rows)), "5,000 opening stock lines");
        Assert.Equal(5000, Count(SheetOf(result, "Stock"), "new"));
        Assert.True(took < Target, $"took {took.TotalSeconds:0.0} s");
    }
}
