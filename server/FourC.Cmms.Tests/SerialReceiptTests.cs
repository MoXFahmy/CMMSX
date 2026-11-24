using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>Serial tracking on parts and serial numbers at receipt (0.24.0).</summary>
public class SerialReceiptTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task Refused(HttpResponseMessage r, string message)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        Assert.Contains(message, body);
    }

    internal static Task<HttpResponseMessage> Track(HttpClient c, Guid part, bool on, object? stores = null, int? life = null) =>
        c.PostAsJsonAsync($"/api/parts/{part}/serial-tracking", new { on, unitLifeMonths = life, stores });

    /// <summary>A serial-tracked part with its level in the org's store and the given units received.</summary>
    internal static async Task<Guid> SerialPartAsync(HttpClient c, PurchaseRequestTests.Org o, string number, params string[] serials)
    {
        var part = Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = number, name = $"Pump {number}", unitOfMeasure = "EA", standardPrice = 500, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            isSerialTracked = true, unitLifeMonths = 24, levels = new[] { new { storeId = o.Store, bin = "P1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        if (serials.Length > 0) await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = o.Store, quantity = serials.Length, unitCost = 500, serials }));
        return part;
    }

    internal static async Task<List<(Guid Id, string Serial, string? Status, Guid? Store)>> UnitsAsync(ApiFixture fx, Guid part)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return (await db.Assets.IgnoreQueryFilters().Where(a => a.PartId == part && !a.IsDeleted).OrderBy(a => a.SerialNumber).ToListAsync())
            .Select(a => (a.Id, a.SerialNumber!, (string?)a.Status.ToString(), a.StoreId)).ToList();
    }

    internal static async Task<decimal> OnHandAsync(HttpClient c, Guid part, Guid store) =>
        (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("levels").EnumerateArray().Single(l => l.GetProperty("storeId").GetGuid() == store).GetProperty("quantityOnHand").GetDecimal();

    [SkippableFact]
    public async Task Switching_on_needs_serials_matching_the_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 2, unitCost = 10 }));
        var entries = (await Ok(await o.Admin.GetAsync($"/api/inventory/transactions?partId={o.Part}"))).GetProperty("total").GetInt32();
        await Refused(await Track(o.Admin, o.Part, true, new[] { new { storeId = o.Store, serials = new[] { "P-1" } } }), "MAIN has 2");
        await Refused(await Track(o.Admin, o.Part, true), "MAIN has 2");
        await Ok(await Track(o.Admin, o.Part, true, new[] { new { storeId = o.Store, serials = new[] { "P-1", "P-2" } } }, life: 12));
        var units = await UnitsAsync(fx, o.Part);
        Assert.Equal(["P-1", "P-2"], units.Select(u => u.Serial));
        Assert.All(units, u => { Assert.Equal("InStorage", u.Status); Assert.Equal(o.Store, u.Store); });
        Assert.Equal(2, await OnHandAsync(o.Admin, o.Part, o.Store));
        Assert.Equal(entries, (await Ok(await o.Admin.GetAsync($"/api/inventory/transactions?partId={o.Part}"))).GetProperty("total").GetInt32());
        var detail = await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"));
        Assert.True(detail.GetProperty("isSerialTracked").GetBoolean());
        Assert.Equal(12, detail.GetProperty("unitLifeMonths").GetInt32());
        await Refused(await Track(o.Admin, o.Part, false), "This part still has units that aren't scrapped.");
        await Refused(await Track(o.Admin, o.Part, true, life: 601), "Enter the life in months, from 1 to 600.");
    }

    [SkippableFact]
    public async Task Manual_receipt_creates_one_unit_per_serial()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-1", "SN-1", "SN-2", "SN-3");
        var units = await UnitsAsync(fx, part);
        Assert.Equal(["SN-1", "SN-2", "SN-3"], units.Select(u => u.Serial));
        Assert.Equal(3, await OnHandAsync(o.Admin, part, o.Store));
        var tx = (await Ok(await o.Admin.GetAsync($"/api/inventory/transactions?partId={part}"))).GetProperty("items").EnumerateArray().ToList();
        var receipt = Assert.Single(tx);
        Assert.Equal(3, receipt.GetProperty("quantity").GetDecimal());
        var unit = await Ok(await o.Admin.GetAsync($"/api/assets/{units[0].Id}"));
        Assert.Equal("Pump PMP-1", unit.GetProperty("name").GetString());
        Assert.Equal(500, unit.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(24), DateOnly.Parse(unit.GetProperty("endOfLifeOn").GetString()!));
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        Assert.Equal(3, await db.UnitMovements.IgnoreQueryFilters().CountAsync(m => units.Select(u => u.Id).Contains(m.UnitId) && m.Kind == FourC.Cmms.Core.Inventory.UnitMovementKind.Received));
    }

    [SkippableFact]
    public async Task Serials_are_trimmed_and_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-2", "A");
        Task<HttpResponseMessage> Receive(decimal quantity, params string[] serials) =>
            o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = o.Store, quantity, unitCost = 500, serials });
        await Refused(await Receive(2, " B ", "b"), "Serial numbers must be different.");
        await Refused(await Receive(3, "B", "C"), "Enter 3 serial numbers, one for each piece received.");
        await Refused(await Receive(1, " a "), "Serial number a is already used by a unit of this part.");
        await Refused(await Receive(1.5m, "B", "C"), "Serial parts are received in whole pieces.");
        await Ok(await Receive(1, "  B  ", ""));
        Assert.Equal(["A", "B"], (await UnitsAsync(fx, part)).Select(u => u.Serial));
    }

    [SkippableFact]
    public async Task Po_receipt_asks_one_serial_per_piece_in_packs()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-3");
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Pump swap", siteId = o.Site, type = "Corrective", priority = "Medium", tasks = Array.Empty<string>() })));
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0,
            lines = new[] { new { partId = (Guid?)part, description = "Pump pack", quantity = 1m, unitPrice = 1000m, discountPercent = 0m, storeId = (Guid?)o.Store, workOrderId = (Guid?)wo, packSize = (decimal?)2 } },
        })));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Refused(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1m, storeId = o.Store, serials = new[] { "K1" } } } }),
            "Enter 2 serial numbers, one for each piece received.");
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1m, storeId = o.Store, serials = new[] { "K1", "K2" } } } }));
        var units = await UnitsAsync(fx, part);
        Assert.Equal(["K1", "K2"], units.Select(u => u.Serial));
        Assert.All(units, u => Assert.Equal("InStorage", u.Status));
        Assert.Equal(2, await OnHandAsync(o.Admin, part, o.Store));
        // Ordered for a work order, the units still wait in the store: nothing is issued to the job.
        Assert.Empty((await Ok(await o.Admin.GetAsync($"/api/inventory/transactions?workOrderId={wo}"))).GetProperty("items").EnumerateArray());
        Assert.Equal(500, (await Ok(await o.Admin.GetAsync($"/api/assets/{units[0].Id}"))).GetProperty("purchasePrice").GetDecimal());
    }

    [SkippableFact]
    public async Task New_parts_can_start_serial_tracked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-4");
        var d = await Ok(await o.Admin.GetAsync($"/api/parts/{part}"));
        Assert.True(d.GetProperty("isSerialTracked").GetBoolean());
        Assert.Equal(24, d.GetProperty("unitLifeMonths").GetInt32());
        var row = (await Ok(await o.Admin.GetAsync("/api/parts?search=PMP-4"))).GetProperty("items")[0];
        Assert.True(row.GetProperty("isSerialTracked").GetBoolean());
    }
}
