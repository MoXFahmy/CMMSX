using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CatalogueTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: order lines filled from the catalogue in whole packs — the order form's fill, the re-order proposal and orders from requests.</summary>
public class CatalogueOrderTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The test part (EA, standard price 10) with SKF selling a Box of 100 at 45, 2% off, 10 days, minimum 1.</summary>
    internal sealed record Setup(PurchaseRequestTests.Org O, Guid Skf, Guid Row);

    internal static async Task<Setup> SetupAsync(ApiFixture fx, int minPacks = 1)
    {
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var row = await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100", 2, 10, minPacks, number: "6205-2RS"));
        return new Setup(o, skf, row);
    }

    private static async Task<JsonElement> FillAsync(HttpClient c, Guid supplier, Guid part, decimal pieces, string? currency = null) =>
        (await Ok(await c.PostAsJsonAsync("/api/purchase-orders/fill", new { supplierId = supplier, currency, lines = new[] { new { partId = part, pieces } } })))[0];

    [SkippableFact]
    public async Task Fill_turns_pieces_into_whole_packs_at_the_catalogue_price()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var f = await FillAsync(x.O.Admin, x.Skf, x.O.Part, 20);
        Assert.Equal("Box of 100", f.GetProperty("unitOfMeasure").GetString());
        Assert.Equal(100m, f.GetProperty("packSize").GetDecimal());
        Assert.Equal(1m, f.GetProperty("quantity").GetDecimal());
        Assert.Equal(45m, f.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2m, f.GetProperty("discountPercent").GetDecimal());
        Assert.Equal("6205-2RS", f.GetProperty("supplierPartNumber").GetString());
        Assert.True(f.GetProperty("cataloguePrice").GetBoolean());
    }

    [SkippableFact]
    public async Task Fill_keeps_the_pieces_when_the_pack_changes()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        Assert.Equal(3m, (await FillAsync(x.O.Admin, x.Skf, x.O.Part, 250)).GetProperty("quantity").GetDecimal());
        var y = await SetupAsync(fx, minPacks: 5);
        Assert.Equal(5m, (await FillAsync(y.O.Admin, y.Skf, y.O.Part, 250)).GetProperty("quantity").GetDecimal());
        // No catalogue row for this supplier: the part's own unit, the pieces as they are.
        var plain = await FillAsync(x.O.Admin, x.O.Supplier, x.O.Part, 7);
        Assert.Equal(1m, plain.GetProperty("packSize").GetDecimal());
        Assert.Equal(7m, plain.GetProperty("quantity").GetDecimal());
    }

    [SkippableFact]
    public async Task Other_currency_takes_the_pack_at_todays_price()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        await Loop.DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(p => p.Id == x.O.Part).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastPurchasePrice, 0.5m)));
        var f = await FillAsync(x.O.Admin, x.Skf, x.O.Part, 20, currency: "EUR");
        Assert.Equal(100m, f.GetProperty("packSize").GetDecimal());
        Assert.Equal(50m, f.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0m, f.GetProperty("discountPercent").GetDecimal());
        Assert.False(f.GetProperty("cataloguePrice").GetBoolean());
    }

    [SkippableFact]
    public async Task Proposal_orders_come_in_whole_packs_with_the_expected_date_from_lead_time()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var made = (await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-orders/from-proposal", new
        {
            lines = new[] { new { partId = x.O.Part, storeId = x.O.Store, quantity = 20m, unitPrice = (decimal?)null, supplierId = x.Skf } },
        })))[0];
        var po = await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{Id(made)}"));
        var line = po.GetProperty("lines")[0];
        Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(100m, line.GetProperty("packSize").GetDecimal());
        Assert.Equal("Box of 100", line.GetProperty("unitOfMeasure").GetString());
        Assert.Equal(45m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(Today.AddDays(10), DateOnly.Parse(po.GetProperty("expectedDate").GetString()!));
    }

    [SkippableFact]
    public async Task Requested_pieces_become_whole_packs_on_the_order()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var (tech, _, _) = await UserAsync(fx, x.O.Admin, "Technician");
        var req = await RequestAsync(tech, x.O, PartLine(x.O, 20));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await Ok(await x.O.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var detail = await Ok(await x.O.Admin.GetAsync($"/api/purchase-requests/{req}"));
        var lineId = detail.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var po = Id((await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = lineId, supplierId = x.Skf } } })))[0]);
        var line = (await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0];
        Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(100m, line.GetProperty("packSize").GetDecimal());
        detail = await Ok(await x.O.Admin.GetAsync($"/api/purchase-requests/{req}"));
        Assert.Equal(100m, detail.GetProperty("lines")[0].GetProperty("ordered").GetDecimal());
        Assert.Equal("Ordered", detail.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Catalogue_change_leaves_existing_order_lines_alone()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var made = (await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-orders/from-proposal", new
        {
            lines = new[] { new { partId = x.O.Part, storeId = x.O.Store, quantity = 20m, unitPrice = (decimal?)null, supplierId = x.Skf } },
        })))[0];
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/parts/{x.O.Part}/suppliers/{x.Row}", Row(x.Skf, 50, 30, "Box of 50")));
        var po = await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{Id(made)}"));
        var line = po.GetProperty("lines")[0];
        Assert.Equal(100m, line.GetProperty("packSize").GetDecimal());
        Assert.Equal(45m, line.GetProperty("unitPrice").GetDecimal());
        // Saving the draft from a page that doesn't send the pack keeps it.
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/purchase-orders/{Id(made)}", new
        {
            siteId = x.O.Site, supplierId = x.Skf, rowVersion = po.GetProperty("rowVersion").GetString(),
            lines = new[] { new { id = line.GetProperty("id").GetGuid(), partId = x.O.Part, description = "Bearing", unitOfMeasure = "Box of 100", quantity = 1m, unitPrice = 45m, discountPercent = 2m, storeId = x.O.Store } },
        }));
        Assert.Equal(100m, (await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{Id(made)}"))).GetProperty("lines")[0].GetProperty("packSize").GetDecimal());
    }

    [SkippableFact]
    public async Task Proposal_orders_take_the_catalogue_price_over_the_pages_price()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        // The proposal page has no price box: it sends today's price per piece, which only stands in when the catalogue has no price.
        var made = (await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-orders/from-proposal", new
        {
            lines = new[] { new { partId = x.O.Part, storeId = x.O.Store, quantity = 20m, unitPrice = (decimal?)10m, supplierId = x.Skf } },
        })))[0];
        var line = (await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{Id(made)}"))).GetProperty("lines")[0];
        Assert.Equal(45m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2m, line.GetProperty("discountPercent").GetDecimal());
        // A supplier without a catalogue row: the page's price per piece is used.
        var none = await SupplierAsync(x.O.Admin, "NONE");
        var plain = (await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-orders/from-proposal", new
        {
            lines = new[] { new { partId = x.O.Part, storeId = x.O.Store, quantity = 3m, unitPrice = (decimal?)7m, supplierId = none } },
        })))[0];
        var plainLine = (await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{Id(plain)}"))).GetProperty("lines")[0];
        Assert.Equal(7m, plainLine.GetProperty("unitPrice").GetDecimal());
    }

    [SkippableFact]
    public async Task Fill_keeps_the_lines_own_price_when_the_catalogue_doesnt_apply()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        async Task<JsonElement> Fill(Guid supplier, string? currency, decimal perPiece) =>
            (await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-orders/fill", new { supplierId = supplier, currency, lines = new[] { new { partId = x.O.Part, pieces = 20m, perPiece } } })))[0];
        // Catalogue price in the order's currency: it wins over the line's price.
        Assert.Equal(45m, (await Fill(x.Skf, null, 0.7m)).GetProperty("unitPrice").GetDecimal());
        // Another currency: the pack, at the line's own price per piece.
        var other = await Fill(x.Skf, "EUR", 0.7m);
        Assert.Equal(70m, other.GetProperty("unitPrice").GetDecimal());
        Assert.False(other.GetProperty("cataloguePrice").GetBoolean());
        // No catalogue row: the line's own price per piece.
        Assert.Equal(3m, (await Fill(await SupplierAsync(x.O.Admin, "NONE"), null, 3m)).GetProperty("unitPrice").GetDecimal());
    }
}
