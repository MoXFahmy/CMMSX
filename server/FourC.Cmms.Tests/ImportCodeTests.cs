using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

/// <summary>The import's Symptoms, Causes and Remedies sheets (0.14.0) and the categories' downtime cost.</summary>
public class ImportCodeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] CodeHeadings = ["Code", "Name", "Active"];

    private static async Task<Dictionary<string, string>> NamesAsync(HttpClient c, string path) =>
        (await OkAsync(await c.GetAsync(path))).EnumerateArray().ToDictionary(x => x.GetProperty("code").GetString()!, x => x.GetProperty("name").GetString()!);

    [SkippableFact]
    public async Task Import_adds_and_updates_codes_by_code()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(
            new Sheet("Symptoms", CodeHeadings, ["LEAK", "Leaking", "Yes"], ["NOISE", "Noisy", "Yes"]),
            new Sheet("Causes", CodeHeadings, ["WEAR", "Worn part", "Yes"]),
            new Sheet("Remedies", CodeHeadings, ["SEAL", "Seal replaced", "Yes"])));
        Assert.Equal(new Dictionary<string, string> { ["LEAK"] = "Leaking", ["NOISE"] = "Noisy" }, await NamesAsync(c, "/api/symptoms"));
        Assert.Equal("Worn part", (await NamesAsync(c, "/api/failure-codes"))["WEAR"]);
        Assert.Equal("Seal replaced", (await NamesAsync(c, "/api/remedies"))["SEAL"]);
        await RunAsync(c, Book(new Sheet("Symptoms", CodeHeadings, ["LEAK", "Leaking water", "Yes"])));
        Assert.Equal("Leaking water", (await NamesAsync(c, "/api/symptoms"))["LEAK"]);
    }

    [SkippableFact]
    public async Task Categories_import_downtime_cost()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(new Sheet("Categories", ["Code", "Name", "Downtime cost per hour"], ["PUMP", "Pumps", 35m])));
        var cat = (await OkAsync(await c.GetAsync("/api/asset-categories"))).EnumerateArray().Single(x => x.GetProperty("code").GetString() == "PUMP");
        Assert.Equal(35m, cat.GetProperty("downtimeCostPerHour").GetDecimal());
    }
}
