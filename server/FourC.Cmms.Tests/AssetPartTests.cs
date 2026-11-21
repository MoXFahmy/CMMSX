using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

/// <summary>Spare parts per asset: the list, copy-from, where-used and the picker (0.8.0).</summary>
public class AssetPartTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static async Task<string> Refused(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        return body;
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record Org(HttpClient C, Guid A, Guid B, Guid P1, Guid P2, Guid P0);

    private async Task<Org> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Parts Co", fullName = "Pia Parts", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "P", name = "Plant", isActive = true })));
        async Task<Guid> Asset(string no) => Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { assetNumber = no, name = $"Pump {no}", siteId = site, status = "Active", criticality = "High", warrantyType = "None" })));
        async Task<Guid> Part(string no) => Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = no, name = $"Part {no}", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false, levels = Array.Empty<object>(),
        })));
        return new Org(c, await Asset("A-1"), await Asset("B-1"), await Part("P1"), await Part("P2"), await Part("AA0"));
    }

    private static object Line(Guid part, decimal qty, string? note = null) => new { partId = part, quantity = qty, note };
    private static async Task<List<JsonElement>> PartsAsync(Org o, Guid asset) => (await Ok(await o.C.GetAsync($"/api/assets/{asset}/parts"))).EnumerateArray().ToList();
    private static async Task PutAsync(Org o, Guid asset, params object[] lines) => await Ok(await o.C.PutAsJsonAsync($"/api/assets/{asset}/parts", lines));

    [SkippableFact]
    public async Task An_asset_gets_its_parts_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await PutAsync(o, o.A, Line(o.P2, 1), Line(o.P1, 2, "Drive-end bearing"));
        var list = await PartsAsync(o, o.A);
        Assert.Equal(["P1", "P2"], list.Select(l => l.GetProperty("partNumber").GetString()));
        Assert.Equal("Drive-end bearing", list[0].GetProperty("note").GetString());
        Assert.Equal("EA", list[0].GetProperty("unitOfMeasure").GetString());

        await PutAsync(o, o.A, Line(o.P1, 3));
        var only = Assert.Single(await PartsAsync(o, o.A));
        Assert.Equal(3m, only.GetProperty("quantity").GetDecimal());

        var url = $"/api/assets/{o.A}/parts";
        Assert.Contains("List each part once.", await Refused(await o.C.PutAsJsonAsync(url, new[] { Line(o.P1, 1), Line(o.P1, 2) })));
        Assert.Contains("Each part needs a quantity above zero.", await Refused(await o.C.PutAsJsonAsync(url, new[] { Line(o.P1, 0) })));
        Assert.Contains("Part not found.", await Refused(await o.C.PutAsJsonAsync(url, new[] { Line(Guid.NewGuid(), 1) })));
        Assert.Single(await PartsAsync(o, o.A));
    }

    [SkippableFact]
    public async Task Copy_adds_the_other_assets_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await PutAsync(o, o.A, Line(o.P1, 2));
        await PutAsync(o, o.B, Line(o.P1, 5), Line(o.P2, 1, "Seal kit"));
        await Ok(await o.C.PostAsync($"/api/assets/{o.A}/parts/copy-from/{o.B}", null));
        var list = await PartsAsync(o, o.A);
        Assert.Equal(2, list.Count);
        Assert.Equal(2m, list.Single(l => l.GetProperty("partId").GetGuid() == o.P1).GetProperty("quantity").GetDecimal());
        Assert.Equal("Seal kit", list.Single(l => l.GetProperty("partId").GetGuid() == o.P2).GetProperty("note").GetString());
    }

    [SkippableFact]
    public async Task Parts_show_where_they_are_used()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await PutAsync(o, o.A, Line(o.P1, 2));
        await PutAsync(o, o.B, Line(o.P1, 5));
        var used = (await Ok(await o.C.GetAsync($"/api/parts/{o.P1}"))).GetProperty("usedOn").EnumerateArray().ToList();
        Assert.Equal(["A-1", "B-1"], used.Select(u => u.GetProperty("assetNumber").GetString()));
        Assert.Equal(5m, used[1].GetProperty("quantity").GetDecimal());
    }

    [SkippableFact]
    public async Task The_picker_offers_the_assets_parts_first()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await PutAsync(o, o.A, Line(o.P2, 2));
        var items = (await Ok(await o.C.GetAsync($"/api/parts/lookup?assetId={o.A}"))).EnumerateArray().ToList();
        Assert.Equal(o.P2, Id(items[0]));
        Assert.Equal(2m, items[0].GetProperty("onAsset").GetDecimal());
        Assert.Equal(o.P0, Id(items[1]));
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("onAsset").ValueKind);
        Assert.Equal(3, items.Count);

        // Searching filters both groups.
        var found = (await Ok(await o.C.GetAsync($"/api/parts/lookup?assetId={o.A}&search=P1"))).EnumerateArray().ToList();
        Assert.Equal(o.P1, Id(Assert.Single(found)));
        // Without an asset the picker is as before.
        Assert.Equal(o.P0, Id((await Ok(await o.C.GetAsync("/api/parts/lookup"))).EnumerateArray().First()));
    }

    [SkippableFact]
    public async Task A_listed_part_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await PutAsync(o, o.A, Line(o.P1, 2));
        await PutAsync(o, o.B, Line(o.P1, 1), Line(o.P2, 1));
        Assert.Contains("This part is on assets' spare-parts lists.", await Refused(await o.C.DeleteAsync($"/api/parts/{o.P1}")));

        // Deleting an asset takes its list with it.
        Assert.Equal(HttpStatusCode.NoContent, (await o.C.DeleteAsync($"/api/assets/{o.B}")).StatusCode);
        Assert.Single((await Ok(await o.C.GetAsync($"/api/parts/{o.P1}"))).GetProperty("usedOn").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await o.C.DeleteAsync($"/api/parts/{o.P2}")).StatusCode);
    }
}
