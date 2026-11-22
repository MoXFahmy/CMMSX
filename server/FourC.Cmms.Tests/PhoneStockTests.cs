using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class PhoneStockTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task The_stock_download_covers_my_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var mine = await StoreAsync(org.Admin, org.Site, "MAIN");
        var theirs = await StoreAsync(org.Admin, other, "FAR");
        var part = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "BRG-1", name = "Bearing", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = mine, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 }, new { storeId = theirs, bin = "Z9", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await ReceiveAsync(org.Admin, part, mine, 7);
        var tech = await UserAsync(fx, org.Admin, "Technician", siteIds: [org.Site]);
        await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech", userId = tech.Id, siteIds = new[] { org.Site }, isContractor = false }));

        var stock = await Ok(await tech.Client.GetAsync("/api/tech/stock"));
        Assert.Equal([mine], stock.GetProperty("stores").EnumerateArray().Select(Id).ToArray());
        var p = stock.GetProperty("parts").EnumerateArray().Single(x => Id(x) == part);
        var level = Assert.Single(p.GetProperty("levels").EnumerateArray());
        Assert.Equal(7m, level.GetProperty("onHand").GetDecimal());
        Assert.Equal("A1", level.GetProperty("bin").GetString());
        Assert.NotEmpty(stock.GetProperty("cancelReasons").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, stock.GetProperty("purchaseOrders").ValueKind);
    }

    [SkippableFact]
    public async Task Purchase_orders_come_with_the_receive_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var stock = await Ok(await org.Admin.GetAsync("/api/tech/stock"));
        Assert.Equal(JsonValueKind.Array, stock.GetProperty("purchaseOrders").ValueKind);
    }

    [SkippableFact]
    public async Task The_bundle_has_planned_lines_and_the_default_store()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { defaultStoreId = store });
        var part = await PartAsync(org.Admin, store);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Swap bearing", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id } })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, storeId = store, quantity = 2 }));

        var job = (await Ok(await org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.Equal(store, job.GetProperty("defaultStoreId").GetGuid());
        var line = Assert.Single(job.GetProperty("plannedLines").EnumerateArray());
        Assert.Contains(job.GetProperty("crew").EnumerateArray(), c => c.GetProperty("employeeId").GetGuid() == me.Id);
        var can = (await Ok(await org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("can");
        Assert.True(can.GetProperty("logForOthers").GetBoolean());
        Assert.True(can.GetProperty("cancel").GetBoolean());
        Assert.True(can.GetProperty("createWorkOrders").GetBoolean());
        Assert.Equal(part, line.GetProperty("partId").GetGuid());
        Assert.Equal(2m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("issued").GetDecimal());
    }
}
