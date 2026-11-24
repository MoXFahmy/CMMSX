using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;

namespace FourC.Cmms.Tests;

/// <summary>Spare units out of the asset list, the end-of-life alert, counts, import and phone receiving (0.24.0).</summary>
public class UnitVisibilityTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> AssetsAsync(HttpClient c, string query = "") => await Ok(await c.GetAsync($"/api/assets?{query}"));
    private static int Total(JsonElement page) => page.GetProperty("total").GetInt32();
    private static bool Lists(JsonElement page, Guid id) => page.GetProperty("items").EnumerateArray().Any(a => a.GetProperty("id").GetGuid() == id);

    [SkippableFact]
    public async Task Spare_units_are_left_out_by_default()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var chiller = await CostCentreWorkTests.AssetAsync(o.Admin, o.Site, "Chiller 2");
        var before = Total(await AssetsAsync(o.Admin));
        var dashBefore = (await Ok(await o.Admin.GetAsync("/api/dashboard"))).GetProperty("totals").GetProperty("assets").GetInt32();
        var part = await SerialPartAsync(o.Admin, o, "PMP-5", "V-1", "V-2");
        var units = await UnitsAsync(fx, part);
        Assert.Equal(before, Total(await AssetsAsync(o.Admin)));
        Assert.Equal(dashBefore, (await Ok(await o.Admin.GetAsync("/api/dashboard"))).GetProperty("totals").GetProperty("assets").GetInt32());
        Assert.Equal(before + 2, Total(await AssetsAsync(o.Admin, "spareUnits=true")));
        Assert.True(Lists(await AssetsAsync(o.Admin, "search=V-1"), units[0].Id));
        var picker = (await Ok(await o.Admin.GetAsync("/api/assets/lookup"))).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(units[1].Id, picker);
        Assert.Contains(units[1].Id, (await Ok(await o.Admin.GetAsync("/api/assets/lookup?search=V-2"))).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));
        // A fitted unit is part of its machine: it shows, as a child.
        var wo = await CostCentreWorkTests.WorkOrderAsync(o.Admin, o.Site, chiller, "Swap");
        await Ok(await UnitFitTests.Fit(o.Admin, wo, units[0].Id, chiller, countHours: false));
        Assert.Equal(before + 1, Total(await AssetsAsync(o.Admin)));
        Assert.True(Lists(await AssetsAsync(o.Admin, $"parentAssetId={chiller}"), units[0].Id));
    }

    [SkippableFact]
    public async Task End_of_life_alert_counts_units_within_30_days()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-6", "E-1", "E-2", "E-3");
        var units = await UnitsAsync(fx, part);
        Assert.Equal(0, (await Ok(await o.Admin.GetAsync("/api/alerts"))).GetProperty("unitsEndingLife").GetInt32());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            var rows = await db.Assets.IgnoreQueryFilters().Where(a => a.PartId == part).ToListAsync();
            rows.Single(a => a.SerialNumber == "E-1").EndOfLifeOn = today.AddDays(10);
            rows.Single(a => a.SerialNumber == "E-2").EndOfLifeOn = today.AddDays(40);
            var scrapped = rows.Single(a => a.SerialNumber == "E-3");
            scrapped.EndOfLifeOn = today.AddDays(5);
            scrapped.Status = AssetStatus.Disposed;
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await Ok(await o.Admin.GetAsync("/api/alerts"))).GetProperty("unitsEndingLife").GetInt32());
        var ending = (await Ok(await o.Admin.GetAsync("/api/serial-units?endingWithinDays=30"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("E-1", Assert.Single(ending).GetProperty("serialNumber").GetString());
    }

    [SkippableFact]
    public async Task Counts_leave_serial_parts_out()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-8", "C-1", "C-2");
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 3, unitCost = 10 }));
        var count = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stock-counts", new { storeId = o.Store, name = "Year end" })));
        Assert.Equal(1, (await Ok(await o.Admin.GetAsync($"/api/stock-counts/{count}"))).GetProperty("serialPartsLeftOut").GetInt32());
        var answer = await Phone.ActAsync(o.Admin, "count.line", new { countId = count, partId = part, quantity = 2 });
        Assert.Equal(("rejected", "Count serial parts by their units on the part's units list."), (Phone.Status(answer), Phone.Message(answer)));
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(o.Admin, "count.line", new { countId = count, partId = o.Part, quantity = 3 })));
        var sheet = (await Ok(await o.Admin.GetAsync($"/api/inventory/stock-take?storeId={o.Store}"))).EnumerateArray().Select(l => l.GetProperty("partId").GetGuid()).ToList();
        Assert.DoesNotContain(part, sheet);
        Assert.Contains(o.Part, sheet);
        await Refused(await o.Admin.PostAsJsonAsync("/api/inventory/stock-take", new { storeId = o.Store, lines = new[] { new { partId = part, countedQuantity = 2m } } }),
            "This part is serial-tracked: scrap a unit or register a found one on the part's units.");
    }

    private static readonly string[] PartHeadings = ["Part number", "Name", "Unit", "Serial tracked", "Unit life (months)"];
    private static readonly string[] StockHeadings = ["Part number", "Store code", "Opening quantity"];

    private static Sheet[] Basics() =>
    [
        new("Sites", SiteHeadings, ["CAI", "Cairo", null]),
        new("Stores", ["Code", "Name", "Site code"], ["MAIN", "Main", "CAI"]),
    ];

    private static async Task<JsonElement> PartAsync(HttpClient c, string number)
    {
        var id = (await OkAsync(await c.GetAsync($"/api/parts?search={number}"))).GetProperty("items").EnumerateArray()
            .Single(p => p.GetProperty("partNumber").GetString() == number).GetProperty("id").GetGuid();
        return await OkAsync(await c.GetAsync($"/api/parts/{id}"));
    }

    [SkippableFact]
    public async Task Import_turns_serial_tracking_on_only_without_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(), new Sheet("Parts", PartHeadings, ["P1", "Pump", "EA", "yes", 12], ["P2", "Seal kit", "EA", "no", null]),
            new Sheet("Stock", StockHeadings, ["P2", "MAIN", 5m])]));
        var p1 = await PartAsync(c, "P1");
        Assert.True(p1.GetProperty("isSerialTracked").GetBoolean());
        Assert.Equal(12, p1.GetProperty("unitLifeMonths").GetInt32());
        Assert.False((await PartAsync(c, "P2")).GetProperty("isSerialTracked").GetBoolean());

        var result = await RunAsync(c, Book(new Sheet("Parts", PartHeadings, ["P2", "Seal kit", "EA", "yes", null])));
        Assert.Contains("Register its units on the part page first.", ProblemsOf(SheetOf(result, "Parts")));
        Assert.False((await PartAsync(c, "P2")).GetProperty("isSerialTracked").GetBoolean());
    }

    [SkippableFact]
    public async Task Import_refuses_opening_stock_for_serial_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(), new Sheet("Parts", PartHeadings, ["P1", "Pump", "EA", "yes", null]),
            new Sheet("Stock", StockHeadings, ["P1", "MAIN", 3m])]));
        Assert.Contains("Serial parts' stock comes from their units: register them on the part page.", ProblemsOf(SheetOf(result, "Stock")));
        var levels = (await PartAsync(c, "P1")).GetProperty("levels").EnumerateArray().ToList();
        Assert.All(levels, l => Assert.Equal(0, l.GetProperty("quantityOnHand").GetDecimal()));
        // A level without an opening quantity is fine.
        var plain = await RunAsync(c, Book(new Sheet("Stock", ["Part number", "Store code", "Bin"], ["P1", "MAIN", "B2"])));
        Assert.Equal(0, Count(SheetOf(plain, "Stock"), "problems"));
    }

    [SkippableFact]
    public async Task Phone_cannot_receive_serial_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-3");
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0,
            lines = new[] { new { partId = (Guid?)part, description = "Pump", quantity = 1m, unitPrice = 1000m, discountPercent = 0m, storeId = (Guid?)o.Store } },
        })));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var answer = await Phone.ActAsync(o.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 1, storeId = o.Store });
        Assert.Equal(("rejected", "Receive serial parts on the web, where their serial numbers are entered."), (Phone.Status(answer), Phone.Message(answer)));
    }
}
