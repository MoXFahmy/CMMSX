using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: brands with their models, picked on assets; the asset keeps the names as text.</summary>
public class BrandTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<Guid> BrandAsync(HttpClient c, string name) => Id(await Ok(await c.PostAsJsonAsync("/api/brands", new { name, isActive = true })));
    internal static async Task<Guid> ModelAsync(HttpClient c, Guid brand, string name) =>
        Id(await Ok(await c.PostAsJsonAsync($"/api/brands/{brand}/models", new { name, isActive = true })));

    internal static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name, Guid? brand, Guid? model) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new
        {
            name, siteId = site, status = "Active", criticality = "Medium", warrantyType = "None", brandId = brand, modelId = model, managesBrand = true,
        })));

    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid asset) => await Ok(await c.GetAsync($"/api/assets/{asset}"));

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient c, Guid asset, object extra)
    {
        var d = await DetailAsync(c, asset);
        var body = new Dictionary<string, object?>
        {
            ["assetNumber"] = d.GetProperty("assetNumber").GetString(), ["name"] = d.GetProperty("name").GetString(), ["siteId"] = d.GetProperty("siteId").GetGuid(),
            ["status"] = "Active", ["criticality"] = "Medium", ["warrantyType"] = "None", ["rowVersion"] = d.GetProperty("rowVersion").GetString(),
        };
        foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
        return await c.PutAsJsonAsync($"/api/assets/{asset}", body);
    }

    [SkippableFact]
    public async Task Asset_picks_a_brand_and_model_and_keeps_the_text()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var grundfos = await BrandAsync(o.Admin, "Grundfos");
        var cr10 = await ModelAsync(o.Admin, grundfos, "CR 10");
        var daikin = await BrandAsync(o.Admin, "Daikin");
        var ftx = await ModelAsync(o.Admin, daikin, "FTX");
        var pump = await AssetAsync(o.Admin, o.Site, "Pump", grundfos, cr10);
        var d = await DetailAsync(o.Admin, pump);
        Assert.Equal(("Grundfos", "CR 10"), (d.GetProperty("manufacturer").GetString(), d.GetProperty("model").GetString()));
        Assert.Equal((grundfos, cr10), (d.GetProperty("brandId").GetGuid(), d.GetProperty("modelId").GetGuid()));
        var wrong = await SaveAsync(o.Admin, pump, new { brandId = grundfos, modelId = ftx, managesBrand = true });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Choose a model of the chosen brand.", await wrong.Content.ReadAsStringAsync());
        var found = (await Ok(await o.Admin.GetAsync($"/api/assets?brandId={grundfos}"))).GetProperty("items").EnumerateArray().Select(Id).ToList();
        Assert.Equal([pump], found);
        Assert.Single((await Ok(await o.Admin.GetAsync($"/api/assets?modelId={cr10}"))).GetProperty("items").EnumerateArray());
    }

    [SkippableFact]
    public async Task Renaming_and_merging_brands_updates_assets()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var grundfos = await BrandAsync(o.Admin, "Grundfos");
        var pumps = await BrandAsync(o.Admin, "Grundfos Pumps");
        var a = await AssetAsync(o.Admin, o.Site, "Pump A", grundfos, null);
        var b = await AssetAsync(o.Admin, o.Site, "Pump B", pumps, null);
        await Ok(await o.Admin.PutAsJsonAsync($"/api/brands/{grundfos}", new { name = "Grundfos A/S", isActive = true }));
        Assert.Equal("Grundfos A/S", (await DetailAsync(o.Admin, a)).GetProperty("manufacturer").GetString());
        var conflict = await o.Admin.PutAsJsonAsync($"/api/brands/{pumps}", new { name = "grundfos a/s", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(grundfos, (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mergeWith").GetGuid());
        await Ok(await o.Admin.PutAsJsonAsync($"/api/brands/{pumps}", new { name = "grundfos a/s", isActive = true, merge = true }));
        var moved = await DetailAsync(o.Admin, b);
        Assert.Equal((grundfos, "Grundfos A/S"), (moved.GetProperty("brandId").GetGuid(), moved.GetProperty("manufacturer").GetString()));
        var brands = (await Ok(await o.Admin.GetAsync("/api/brands"))).EnumerateArray().Select(Id).ToList();
        Assert.Equal([grundfos], brands);
    }

    [SkippableFact]
    public async Task Brands_in_use_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var grundfos = await BrandAsync(o.Admin, "Grundfos");
        await AssetAsync(o.Admin, o.Site, "Pump", grundfos, null);
        var refused = await o.Admin.DeleteAsync($"/api/brands/{grundfos}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This brand is in use. Make it inactive instead.", await refused.Content.ReadAsStringAsync());
        var (viewer, _, _) = await UserAsync(fx, o.Admin, "Viewer", ["assets.view"]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/brands")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/brands", new { name = "X", isActive = true })).StatusCode);
    }

    [SkippableFact]
    public async Task Old_clients_keep_the_brand()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var grundfos = await BrandAsync(o.Admin, "Grundfos");
        var cr10 = await ModelAsync(o.Admin, grundfos, "CR 10");
        var pump = await AssetAsync(o.Admin, o.Site, "Pump", grundfos, cr10);
        await Ok(await SaveAsync(o.Admin, pump, new { name = "Pump renamed", manufacturer = "Grundfos", model = "CR 10" }));
        var d = await DetailAsync(o.Admin, pump);
        Assert.Equal((grundfos, cr10), (d.GetProperty("brandId").GetGuid(), d.GetProperty("modelId").GetGuid()));
    }
}
