using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;
using static FourC.Cmms.Tests.UnitActionTests;
using static FourC.Cmms.Tests.UnitFitTests;

namespace FourC.Cmms.Tests;

/// <summary>Serial units for people limited to sites or branches, the asset import, repair orders, counts and part deletion (0.24.0 review).</summary>
public class UnitReviewFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<(Guid Site, Guid Store)> SecondSiteAsync(HttpClient c)
    {
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var store = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "FAR", name = "Far", siteId = site })));
        return (site, store);
    }

    [SkippableFact]
    public async Task Switching_on_sees_stock_at_every_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var (_, far) = await SecondSiteAsync(o.Admin);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 2, unitCost = 10 }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = far, quantity = 1, unitCost = 10 }));
        var (mine, _) = await SiteAccessTests.UserAsync(fx, o.Admin, "Maintenance Manager", [o.Site]);
        await Refused(await Track(mine, o.Part, true, new[] { new { storeId = o.Store, serials = new[] { "P-1", "P-2" } } }), "This part has stock at sites you can't see.");
        await Refused(await Track(o.Admin, o.Part, true, new[] { new { storeId = o.Store, serials = new[] { "P-1", "P-2" } } }), "FAR has 1");
        await Ok(await Track(o.Admin, o.Part, true, new object[] { new { storeId = o.Store, serials = new[] { "P-1", "P-2" } }, new { storeId = far, serials = new[] { "P-3" } } }));
        Assert.Equal(3, (await UnitsAsync(fx, o.Part)).Count);
    }

    [SkippableFact]
    public async Task Removing_without_a_work_order_stays_in_my_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var (site2, far) = await SecondSiteAsync(o.Admin);
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "PMP-F", name = "Pump", unitOfMeasure = "EA", standardPrice = 500, isCritical = false, isShelfItem = false, allowNegativeStock = false, isSerialTracked = true,
            levels = new[] { new { storeId = far, bin = "F1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = far, quantity = 1, unitCost = 500, serials = new[] { "F-1" } }));
        var unit = (await UnitsAsync(fx, part)).Single().Id;
        var farPump = await CostCentreWorkTests.AssetAsync(o.Admin, site2, "Far pump");
        var wo = await CostCentreWorkTests.WorkOrderAsync(o.Admin, site2, farPump, "Far job");
        await Ok(await Fit(o.Admin, wo, unit, farPump, countHours: false));
        var (mine, _) = await SiteAccessTests.UserAsync(fx, o.Admin, "Maintenance Manager", [o.Site]);
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(mine, unit, null, "Scrap", note: "Not mine")).StatusCode);
        Assert.Equal(AssetStatus.Active, (await UnitAsync(fx, unit)).Status);
        await Ok(await Remove(o.Admin, unit, null, "Repair"));
    }

    [SkippableFact]
    public async Task Branch_limited_people_see_spare_units()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var b1 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new { siteId = o.Site, parentId = (Guid?)null, code = "B1", name = "B1", type = "Building", criticality = "Medium", isActive = true })));
        var part = await SerialPartAsync(o.Admin, o, "PMP-B", "B-1", "B-2");
        var (branch, _) = await SiteAccessTests.UserAsync(fx, o.Admin, "Maintenance Manager", locationIds: [b1]);
        Assert.Equal(2, (await ListAsync(branch, $"partId={part}")).Count);
        var unit = (await UnitsAsync(fx, part))[0].Id;
        await Ok(await Act(branch, unit, "send-for-repair", new { }));
        await Ok(await branch.GetAsync($"/api/serial-units/{unit}"));
    }

    [SkippableFact]
    public async Task Asset_import_reports_a_unit_row_without_aborting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await SecondSiteAsync(s.O.Admin);
        string NumberOf(JsonElement a) => a.GetProperty("assetNumber").GetString()!;
        var u1 = NumberOf(await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.U1}")));
        var u2 = NumberOf(await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.U2}")));
        var result = await RunAsync(s.O.Admin, Book(new Sheet("Assets", ["Asset number", "Name", "Site code", "Serial number"],
            [u1, "Pump PMP-9", "S2", "S-1"], [u2, "Pump PMP-9", "S1", "s-1"], ["NEW-1", "Roof fan", "S1", null])));
        var problems = ProblemsOf(SheetOf(result, "Assets")).ToList();
        Assert.Contains("A unit's status and place change through fit, remove and the unit actions.", problems);
        Assert.Contains("Serial number s-1 is already used by a unit of this part.", problems);
        Assert.True((await Ok(await s.O.Admin.GetAsync("/api/assets?search=NEW-1"))).GetProperty("total").GetInt32() == 1);
        Assert.Equal((s.O.Store, "S-2"), ((await UnitAsync(fx, s.U2)).StoreId!.Value, (await UnitAsync(fx, s.U2)).SerialNumber));
    }

    [SkippableFact]
    public async Task A_unit_on_a_repair_order_comes_back_through_it()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var po = await Ok(await s.O.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = s.O.Site, supplierId = s.O.Supplier, shippingCost = 0,
            lines = new[] { new { partId = (Guid?)null, description = (string?)null, quantity = 1m, unitPrice = 300m, discountPercent = 0m, storeId = (Guid?)null, unitId = (Guid?)s.U1 } },
        }));
        var message = $"This unit is on repair order {po.GetProperty("number").GetString()}: receive it or cancel the order there first.";
        await Refused(await Act(s.O.Admin, s.U1, "back-from-repair", new { storeId = s.O.Store }), message);
        await Refused(await Act(s.O.Admin, s.U1, "scrap", new { note = "Beyond repair" }), message);
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{Id(po)}/cancel", new { reason = "Repaired in house" }));
        await Ok(await Act(s.O.Admin, s.U1, "back-from-repair", new { storeId = s.O.Store }));
    }

    [SkippableFact]
    public async Task Location_counts_leave_spare_units_out()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        await CostCentreWorkTests.AssetAsync(o.Admin, o.Site, "Chiller");
        async Task<(int Tree, int Site, int List)> CountsAsync() =>
        (
            (await Ok(await o.Admin.GetAsync("/api/locations/hierarchy"))).GetProperty("sites").EnumerateArray().Single(x => Id(x) == o.Site).GetProperty("assetCount").GetInt32(),
            (await Ok(await o.Admin.GetAsync($"/api/sites/{o.Site}"))).GetProperty("assetCount").GetInt32(),
            (await Ok(await o.Admin.GetAsync("/api/sites"))).EnumerateArray().Single(x => Id(x) == o.Site).GetProperty("assetCount").GetInt32()
        );
        var before = await CountsAsync();
        Assert.Equal((1, 1, 1), before);
        await SerialPartAsync(o.Admin, o, "PMP-C", "C-1", "C-2");
        Assert.Equal(before, await CountsAsync());
    }

    [SkippableFact]
    public async Task A_part_with_units_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-D");
        var chiller = await CostCentreWorkTests.AssetAsync(o.Admin, o.Site, "Chiller");
        await Ok(await o.Admin.PostAsJsonAsync($"/api/parts/{part}/units/fitted", new { assetId = chiller, serial = "D-1" }));
        await Refused(await o.Admin.DeleteAsync($"/api/parts/{part}"), "This part has units. Mark it inactive instead.");
    }
}
