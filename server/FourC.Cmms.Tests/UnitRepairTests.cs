using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Inventory;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;
using static FourC.Cmms.Tests.UnitActionTests;
using static FourC.Cmms.Tests.UnitFitTests;

namespace FourC.Cmms.Tests;

/// <summary>Outside repair: a unit under repair on a purchase order line (0.24.0).</summary>
public class UnitRepairTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>An order in USD at 2.0 with one repair line: 300 less 10% = 270 a line, 540 in the organization's currency.</summary>
    private static Task<HttpResponseMessage> RepairPo(Setup s, Guid unit, Guid? wo = null) => s.O.Admin.PostAsJsonAsync("/api/purchase-orders", new
    {
        siteId = s.O.Site, supplierId = s.O.Supplier, shippingCost = 0, currency = "USD", exchangeRate = 2m,
        lines = new[] { new { partId = (Guid?)null, description = (string?)null, quantity = 1m, unitPrice = 300m, discountPercent = 10m, storeId = (Guid?)null, workOrderId = wo, unitId = (Guid?)unit } },
    });

    [SkippableFact]
    public async Task Repair_line_needs_a_unit_under_repair()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Refused(await RepairPo(s, s.U2), "Only units under repair can go on a repair line.");
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var first = await Ok(await RepairPo(s, s.U1));
        var line = (await Ok(await s.O.Admin.GetAsync($"/api/purchase-orders/{Id(first)}"))).GetProperty("lines")[0];
        Assert.Equal(("Repair of Pump PMP-9 S-1", s.U1, "S-1", 1m, JsonValueKind.Null),
            (line.GetProperty("description").GetString(), line.GetProperty("unitId").GetGuid(), line.GetProperty("unitSerial").GetString(),
             line.GetProperty("quantity").GetDecimal(), line.GetProperty("partId").ValueKind));
        Assert.False(string.IsNullOrEmpty(line.GetProperty("unitAssetNumber").GetString()));
        await Refused(await RepairPo(s, s.U1), $"This unit is already on an open repair line ({first.GetProperty("number").GetString()}).");
    }

    [SkippableFact]
    public async Task Receiving_a_repair_line_brings_the_unit_back_with_its_cost()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var po = Id(await Ok(await RepairPo(s, s.U1, s.Wo)));
        await Ok(await s.O.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = (await Ok(await s.O.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Refused(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1m, storeId = (Guid?)null } } }),
            "Choose the store the unit comes back to.");
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1m, storeId = (Guid?)s.O.Store } } }));
        var u = await UnitAsync(fx, s.U1);
        Assert.Equal((AssetStatus.InStorage, (Guid?)s.O.Store), (u.Status, u.StoreId));
        Assert.Equal(2, await OnHandAsync(s.O.Admin, s.Part, s.O.Store));
        var row = (await ListAsync(s.O.Admin, $"partId={s.Part}")).Single(r => r.GetProperty("id").GetGuid() == s.U1);
        Assert.Equal((1, 540m), (row.GetProperty("repairCount").GetInt32(), row.GetProperty("repairCost").GetDecimal()));
        var back = (await MovesAsync(fx, s.U1)).Last();
        Assert.Equal((UnitMovementKind.BackFromRepair, (Guid?)po, (Guid?)line, (decimal?)540m), (back.Kind, back.PurchaseOrderId, back.PurchaseOrderLineId, back.Cost));
        // Ordered for a work order: the work order is charged as for any service line.
        var costs = (await Ok(await s.O.Admin.GetAsync($"/api/work-orders/{s.Wo}"))).GetProperty("costs").EnumerateArray().ToList();
        var cost = Assert.Single(costs);
        Assert.Equal(("Repair of Pump PMP-9 S-1", 540m), (cost.GetProperty("description").GetString(), cost.GetProperty("total").GetDecimal()));
    }

    [SkippableFact]
    public async Task Cancelling_leaves_the_unit_under_repair()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var po = Id(await Ok(await RepairPo(s, s.U1)));
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/cancel", new { reason = "Supplier closed" }));
        Assert.Equal(AssetStatus.UnderRepair, (await UnitAsync(fx, s.U1)).Status);
        await Ok(await RepairPo(s, s.U1));
    }
}
