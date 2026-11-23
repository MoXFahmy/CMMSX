using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CatalogueTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: receiving packs books pieces at cost per piece, and every "on order" or request amount counts pieces.</summary>
public class CatalogueReceiptTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>An approved order from SKF (catalogue Box of 100) for <paramref name="boxes"/> boxes of the test part at 45, 2% off.</summary>
    private async Task<(PurchaseRequestTests.Org O, Guid Skf, Guid Po, Guid Line)> OrderAsync(decimal boxes = 1, string currency = "USD", decimal rate = 1, decimal packSize = 100)
    {
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        await AddRowAsync(o.Admin, o.Part, Row(skf, packSize, 45, $"Box of {packSize:0}", 2));
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = skf, currency, exchangeRate = rate,
            lines = new[] { new { partId = o.Part, storeId = o.Store, description = "Bearing", unitOfMeasure = $"Box of {packSize:0}", quantity = boxes, unitPrice = 45m, discountPercent = 2m, packSize } },
        })));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = Id((await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0]);
        return (o, skf, po, line);
    }

    private static Task<JsonElement> ReceiveAsync(HttpClient c, Guid po, Guid line, decimal quantity) =>
        Ok(c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity } } }).Result);

    [SkippableFact]
    public async Task Receiving_a_box_books_its_pieces_at_cost_per_piece()
    {
        Skip.IfNot(fx.Enabled);
        var (o, skf, po, line) = await OrderAsync();
        await ReceiveAsync(o.Admin, po, line, 1);
        Assert.Equal(100m, await Phone.OnHandAsync(o.Admin, o.Part, o.Store));
        var part = await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"));
        Assert.Equal(0.441m, part.GetProperty("averageCost").GetDecimal());
        Assert.Equal(0.441m, part.GetProperty("lastPurchasePrice").GetDecimal());
        var row = (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}/suppliers"))).EnumerateArray().Single(r => r.GetProperty("supplierId").GetGuid() == skf);
        Assert.Equal(45m, row.GetProperty("lastPaidPrice").GetDecimal());
        Assert.Equal(2m, row.GetProperty("lastPaidDiscount").GetDecimal());
        Assert.Equal("USD", row.GetProperty("lastPaidCurrency").GetString());
        Assert.Equal(Today, DateOnly.Parse(row.GetProperty("lastPaidOn").GetString()!));
        Assert.Equal(45m, row.GetProperty("packPrice").GetDecimal()); // the catalogue price itself never moves
    }

    [SkippableFact]
    public async Task Half_a_box_received_books_half_its_pieces()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, po, line) = await OrderAsync();
        await ReceiveAsync(o.Admin, po, line, 0.5m);
        Assert.Equal(50m, await Phone.OnHandAsync(o.Admin, o.Part, o.Store));
        Assert.Equal("PartiallyReceived", (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("status").GetString());
        await ReceiveAsync(o.Admin, po, line, 0.5m);
        Assert.Equal(100m, await Phone.OnHandAsync(o.Admin, o.Part, o.Store));
        Assert.Equal("Received", (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Foreign_currency_pack_cost_per_piece_uses_the_rate()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, po, line) = await OrderAsync(currency: "EUR", rate: 48.5m, packSize: 10);
        await ReceiveAsync(o.Admin, po, line, 1);
        Assert.Equal(10m, await Phone.OnHandAsync(o.Admin, o.Part, o.Store));
        // 45 less 2% = 44.10 per box of 10, × 48.5 = 213.885 per piece.
        Assert.Equal(213.885m, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("lastPurchasePrice").GetDecimal());
    }

    [SkippableFact]
    public async Task On_order_counts_pieces_everywhere()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, _, _) = await OrderAsync(boxes: 2);
        await Loop.DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().Where(l => l.PartId == o.Part)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReorderLevel, 1000m).SetProperty(l => l.ReorderQuantity, 500m)));
        var listed = (await Ok(await o.Admin.GetAsync("/api/parts?search=BRG-1"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(200m, listed.GetProperty("onOrder").GetDecimal());
        Assert.Equal(200m, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("onOrder").GetDecimal());
        var proposed = (await Ok(await o.Admin.GetAsync("/api/inventory/proposal"))).EnumerateArray().Single(r => r.GetProperty("partId").GetGuid() == o.Part);
        Assert.Equal(200m, proposed.GetProperty("onOrder").GetDecimal());
    }

    [SkippableFact]
    public async Task Request_shows_ordered_and_received_in_pieces()
    {
        Skip.IfNot(fx.Enabled);
        var x = await CatalogueOrderTests.SetupAsync(fx);
        var (tech, _, _) = await UserAsync(fx, x.O.Admin, "Technician");
        var req = await RequestAsync(tech, x.O, PartLine(x.O, 20));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await Ok(await x.O.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var lineId = (await Ok(await x.O.Admin.GetAsync($"/api/purchase-requests/{req}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var po = Id((await Ok(await x.O.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = lineId, supplierId = x.Skf } } })))[0]);
        await Ok(await x.O.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await x.O.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var poLine = Id((await Ok(await x.O.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0]);
        await ReceiveAsync(x.O.Admin, po, poLine, 1);
        var line = (await Ok(await x.O.Admin.GetAsync($"/api/purchase-requests/{req}"))).GetProperty("lines")[0];
        Assert.Equal(100m, line.GetProperty("ordered").GetDecimal());
        Assert.Equal(100m, line.GetProperty("received").GetDecimal());
    }

    [SkippableFact]
    public async Task Phone_receipt_of_a_pack_books_pieces()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, po, line) = await OrderAsync();
        // The phone works for a person: the admin as an employee.
        var me = (await Ok(await o.Admin.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "ME", fullName = "Admin", isContractor = false, userId = me }));
        var stock = await Ok(await o.Admin.GetAsync("/api/tech/stock"));
        var phoneLine = stock.GetProperty("purchaseOrders").EnumerateArray().Single(x => Id(x) == po).GetProperty("lines")[0];
        Assert.Equal(100m, phoneLine.GetProperty("packSize").GetDecimal());
        Assert.Equal(1m, phoneLine.GetProperty("outstanding").GetDecimal());
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(o.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 1 })));
        Assert.Equal(100m, await Phone.OnHandAsync(o.Admin, o.Part, o.Store));
    }
}
