using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Services;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: brands, models and units built from the texts records already carry, and kept linked.</summary>
public class ReconcileTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> OldAssetAsync(HttpClient c, Guid site, string name, string? maker, string? model) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name, siteId = site, status = "Active", criticality = "Medium", warrantyType = "None", manufacturer = maker, model })));

    private static async Task PartAsync(HttpClient c, string no, string unit) => await Ok(await c.PostAsJsonAsync("/api/parts", new
    {
        partNumber = no, name = no, unitOfMeasure = unit, standardPrice = 1, isCritical = false, isShelfItem = false, allowNegativeStock = false, levels = Array.Empty<object>(),
    }));

    [SkippableFact]
    public async Task Reconcile_links_texts_and_is_idempotent()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await OldAssetAsync(o.Admin, o.Site, "A", " grundfos", "CR 10");
        var b = await OldAssetAsync(o.Admin, o.Site, "B", "Grundfos", "cr 10");
        var c = await OldAssetAsync(o.Admin, o.Site, "C", "GRUNDFOS  ", null);
        await OldAssetAsync(o.Admin, o.Site, "D", null, "Orphan model");
        await PartAsync(o.Admin, "P1", "pcs");
        await PartAsync(o.Admin, "P2", "PCS");
        var first = await Loop.DbAsync(fx, db => ReferenceLists.ReconcileAsync(db, o.Tenant, CancellationToken.None));
        Assert.Equal((1, 1, 2), first); // one brand, one model, "PCS" and the fixture's "EA" ("pcs" is a starter unit)
        var brands = (await Ok(await o.Admin.GetAsync("/api/brands"))).EnumerateArray().ToList();
        var brand = Assert.Single(brands);
        Assert.Equal("grundfos", brand.GetProperty("name").GetString()!.ToLowerInvariant());
        Assert.Single(brand.GetProperty("models").EnumerateArray());
        foreach (var id in new[] { a, b, c })
        {
            var d = await Ok(await o.Admin.GetAsync($"/api/assets/{id}"));
            Assert.Equal(Id(brand), d.GetProperty("brandId").GetGuid());
            Assert.Equal(brand.GetProperty("name").GetString(), d.GetProperty("manufacturer").GetString());
        }
        var symbols = (await Ok(await o.Admin.GetAsync("/api/units"))).EnumerateArray().Select(u => u.GetProperty("symbol").GetString()).ToList();
        Assert.Contains("pcs", symbols);
        Assert.Contains("PCS", symbols);
        Assert.Equal((0, 0, 0), await Loop.DbAsync(fx, db => ReferenceLists.ReconcileAsync(db, o.Tenant, CancellationToken.None)));
    }

    [SkippableFact]
    public async Task Import_adds_unknown_brands_and_units()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteCode = (await Ok(await o.Admin.GetAsync($"/api/sites/{o.Site}"))).GetProperty("code").GetString();
        var book = Book(
            new Sheet("Assets", ["Asset number", "Name", "Site code", "Manufacturer", "Model"], ["AC-1", "Split unit", siteCode, "Daikin", "FTX"]),
            new Sheet("Parts", ["Part number", "Name", "Unit"], ["BX-1", "Filters", "box"]));
        var check = await CheckAsync(o.Admin, book);
        Assert.Contains("New brands: 1", check.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        await RunAsync(o.Admin, book);
        var brand = Assert.Single((await Ok(await o.Admin.GetAsync("/api/brands"))).EnumerateArray());
        Assert.Equal("Daikin", brand.GetProperty("name").GetString());
        Assert.Equal("FTX", brand.GetProperty("models")[0].GetProperty("name").GetString());
        Assert.Contains("box", (await Ok(await o.Admin.GetAsync("/api/units"))).EnumerateArray().Select(u => u.GetProperty("symbol").GetString()));
        var asset = (await Ok(await o.Admin.GetAsync("/api/assets?search=AC-1"))).GetProperty("items")[0];
        Assert.Equal(Id(brand), (await Ok(await o.Admin.GetAsync($"/api/assets/{Id(asset)}"))).GetProperty("brandId").GetGuid());
    }
}
