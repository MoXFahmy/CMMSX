using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: names of places, parts and lists are shown in the reader's language wherever they are only shown.</summary>
public class BilingualDisplayTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IEnumerable<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray();

    private static async Task<string> TextAsync(HttpClient c, string url, string language) =>
        await (await SendAsync(c, HttpMethod.Get, url, language)).Content.ReadAsStringAsync();

    private static async Task<Guid> LocationAsync(HttpClient c, Guid site, string code, string name, string? nameAr, Guid? parent = null, string type = "Room") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/locations", new { siteId = site, parentId = parent, code, name, nameAr, type, criticality = "Medium", isActive = true })));

    [SkippableFact]
    public async Task Asset_list_and_export_show_the_readers_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await AssetAsync(o.Admin, o.Site);
        var b = await AssetAsync(o.Admin, o.Site, name: "Boiler 1", nameAr: null);
        var ar = Items(await GetAsync(o.Admin, "/api/assets", "ar")).ToList();
        Assert.Equal(PumpAr, S(ar.Single(r => r.GetProperty("id").GetGuid() == a), "name"));
        Assert.Equal("Boiler 1", S(ar.Single(r => r.GetProperty("id").GetGuid() == b), "name"));
        var en = Items(await GetAsync(o.Admin, "/api/assets", "en")).ToList();
        Assert.Equal(Pump, S(en.Single(r => r.GetProperty("id").GetGuid() == a), "name"));
        Assert.Contains(PumpAr, await TextAsync(o.Admin, "/api/assets/export?format=csv&c=name&t=Name", "ar"));
        Assert.Contains(Pump, await TextAsync(o.Admin, "/api/assets/export?format=csv&c=name&t=Name", "en"));
    }

    [SkippableFact]
    public async Task Work_order_references_follow_the_reader()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var block = await LocationAsync(o.Admin, o.Site, "BA", "Block A", "المبنى أ", type: "Building");
        var room = await LocationAsync(o.Admin, o.Site, "PR", "Plant room", "غرفة المعدات", block);
        var cat = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "PMP", name = "Pumps", nameAr = "مضخات", isActive = true })));
        var symptom = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/symptoms", new { code = "LK", name = "Leak", nameAr = "تسريب", isActive = true })));
        var asset = await AssetAsync(o.Admin, o.Site, location: room, category: cat);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix pump", siteId = o.Site, locationId = room, assetId = asset, type = "Corrective", priority = "Medium", symptomId = symptom,
        })));
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar");
        Assert.Equal(PumpAr, S(w.GetProperty("asset"), "name"));
        var trail = w.GetProperty("locationTrail").EnumerateArray().Select(l => S(l, "name")).ToList();
        Assert.Equal(["المبنى أ", "غرفة المعدات"], trail);
        Assert.Equal("تسريب", S(w.GetProperty("symptom"), "name"));
        var en = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en");
        Assert.Equal(Pump, S(en.GetProperty("asset"), "name"));
        Assert.Equal("Leak", S(en.GetProperty("symptom"), "name"));

        var a = await GetAsync(o.Admin, $"/api/assets/{asset}", "ar");
        Assert.Equal("مضخات", S(a.GetProperty("category"), "name"));
        Assert.Equal(Pump, S(a, "name"));
        var row = Items(await GetAsync(o.Admin, "/api/work-orders", "ar")).Single(r => r.GetProperty("id").GetGuid() == wo);
        Assert.Contains("غرفة المعدات", row.GetRawText());
    }

    [SkippableFact]
    public async Task Search_finds_records_by_either_text()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var asset = await AssetAsync(o.Admin, o.Site);
        Assert.Contains(Items(await GetAsync(o.Admin, "/api/assets?search=" + Uri.EscapeDataString("التبريد"), "en")), r => r.GetProperty("id").GetGuid() == asset);
        Assert.Contains(await GetAsync(o.Admin, "/api/assets/lookup?search=" + Uri.EscapeDataString("التبريد"), "en").ContinueWith(t => t.Result.EnumerateArray().ToList()),
            r => r.GetProperty("id").GetGuid() == asset);
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "B6204", name = "Bearing 6204", nameAr = "رولمان 6204", unitOfMeasure = "pcs", standardPrice = 10, isCritical = false, isShelfItem = false,
            allowNegativeStock = false, isActive = true,
        })));
        Assert.Contains(Items(await GetAsync(o.Admin, "/api/parts?search=" + Uri.EscapeDataString("رولمان"), "en")), r => r.GetProperty("id").GetGuid() == part);
        var loc = await LocationAsync(o.Admin, o.Site, "PR", "Plant room", "غرفة المعدات");
        Assert.Contains(Items(await GetAsync(o.Admin, "/api/locations?search=" + Uri.EscapeDataString("المعدات"), "en")), r => r.GetProperty("id").GetGuid() == loc);
    }

    [SkippableFact]
    public async Task Sorting_follows_the_text_the_reader_sees()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var alpha = await AssetAsync(o.Admin, o.Site, name: "Alpha", nameAr: "ياء");
        var beta = await AssetAsync(o.Admin, o.Site, name: "Beta", nameAr: "ألف");
        var ar = Items(await GetAsync(o.Admin, "/api/assets?sort=name", "ar")).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(ar.IndexOf(beta) < ar.IndexOf(alpha));
        var en = Items(await GetAsync(o.Admin, "/api/assets?sort=name", "en")).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(en.IndexOf(alpha) < en.IndexOf(beta));
    }

    [SkippableFact]
    public async Task Location_paths_and_names_keep_site_filters()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var site2 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        await LocationAsync(o.Admin, site2, "SEC", "Secret room", "الغرفة السرية");
        await AssetAsync(o.Admin, site2, name: "Secret pump", nameAr: "المضخة السرية");
        var tech = await UserAsync(fx, o.Admin, "Technician", [o.Site]);
        var mine = await AssetAsync(o.Admin, o.Site);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, assetId = mine, type = "Corrective", priority = "Medium" })));
        foreach (var url in new[] { "/api/locations?search=" + Uri.EscapeDataString("السرية"), "/api/assets?search=" + Uri.EscapeDataString("السرية"),
                     "/api/assets/lookup?search=" + Uri.EscapeDataString("السرية"), "/api/locations", "/api/assets", $"/api/work-orders/{wo}" })
        {
            var text = await TextAsync(tech.Client, url, "ar");
            Assert.DoesNotContain("السرية", text);
        }
    }

    [SkippableFact]
    public async Task Screens_fall_back_when_arabic_is_missing()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var asset = await AssetAsync(o.Admin, o.Site, name: "Boiler 1", nameAr: null);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, assetId = asset, type = "Corrective", priority = "Medium" })));
        Assert.Equal("Boiler 1", S(Items(await GetAsync(o.Admin, "/api/assets", "ar")).Single(r => r.GetProperty("id").GetGuid() == asset), "name"));
        Assert.Equal("Boiler 1", S((await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar")).GetProperty("asset"), "name"));
    }

    /// <summary>A guard (passes before and after): purchasing keeps the part's main name.</summary>
    [SkippableFact]
    public async Task Purchasing_keeps_the_parts_main_name()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "B6204", name = "Bearing 6204", nameAr = "رولمان 6204", unitOfMeasure = "pcs", standardPrice = 10, isCritical = false, isShelfItem = false,
            allowNegativeStock = false, isActive = true,
        })));
        var supplier = await CatalogueTests.SupplierAsync(o.Admin, "SUP");
        var store = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stores", new { code = "ST1", name = "Main store", siteId = o.Site, isActive = true })));
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = supplier, shippingCost = 0, taxPercent = 0,
            lines = new[] { new { partId = part, quantity = 1, unitPrice = 10, discountPercent = 0, storeId = store } },
        })));
        var text = await TextAsync(o.Admin, $"/api/purchase-orders/{po}", "ar");
        Assert.Contains("Bearing 6204", text);
        Assert.DoesNotContain("رولمان", text);
    }
}
