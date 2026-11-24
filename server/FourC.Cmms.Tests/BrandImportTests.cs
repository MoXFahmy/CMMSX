using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: the Brands and models sheet.</summary>
public class BrandImportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Brand_sheet_imports_and_round_trips()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await RunAsync(o.Admin, Book(new Sheet("Brands and models", ["Brand", "Model", "Active"],
            ["Grundfos", "CR 10", "Yes"], ["grundfos ", "CR 15", "Yes"], ["Daikin", null, "No"])));
        var brands = (await Ok(await o.Admin.GetAsync("/api/brands"))).EnumerateArray().ToList();
        Assert.Equal(["Daikin", "Grundfos"], brands.Select(b => b.GetProperty("name").GetString()).ToArray());
        Assert.False(brands[0].GetProperty("isActive").GetBoolean());
        Assert.Equal(["CR 10", "CR 15"], brands[1].GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToArray());
        var current = await (await o.Admin.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var sheet = SheetOf(await CheckAsync(o.Admin, current), "BrandsAndModels");
        Assert.Equal((0, 0, 0), (Count(sheet, "new"), Count(sheet, "changed"), Count(sheet, "problems")));
    }
}
