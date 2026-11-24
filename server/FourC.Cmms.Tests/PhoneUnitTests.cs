using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

/// <summary>Fitting and removing serial units from the phone, and the units in the phone's data (0.24.0).</summary>
public class PhoneUnitTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Store, Guid Part, Guid U1, Guid U2, Guid Chiller, Guid Wo);

    /// <summary>A default store with two units PH-1 and PH-2, and my job on Chiller 2.</summary>
    private async Task<Setup> SetupAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { defaultStoreId = store });
        var part = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "PMP-1", name = "Pump", unitOfMeasure = "EA", standardPrice = 500, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            isSerialTracked = true, levels = new[] { new { storeId = store, bin = "P1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await Ok(await org.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 2, unitCost = 500, serials = new[] { "PH-1", "PH-2" } }));
        var units = await SerialReceiptTests.UnitsAsync(fx, part);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var chiller = await CostCentreWorkTests.AssetAsync(org.Admin, org.Site, "Chiller 2", org.Location);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Pump swap", siteId = org.Site, assetId = chiller, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id },
        })));
        return new Setup(org, store, part, units[0].Id, units[1].Id, chiller, wo);
    }

    [SkippableFact]
    public async Task Phone_fits_and_removes_units()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var c = s.Org.Admin;
        Assert.Equal("done", Status(await ActAsync(c, "unit.fit", new { unitId = s.U1, assetId = s.Chiller }, s.Wo)));
        Assert.Equal(s.Chiller, (await UnitFitTests.UnitAsync(fx, s.U1)).ParentAssetId);
        Assert.Equal(1, await OnHandAsync(c, s.Part, s.Store));
        Assert.Equal("done", Status(await ActAsync(c, "unit.remove", new { unitId = s.U1, outcome = "Store", note = "Worn" }, s.Wo)));
        var back = await UnitFitTests.UnitAsync(fx, s.U1);
        Assert.Equal((AssetStatus.InStorage, (Guid?)s.Store), (back.Status, back.StoreId));
        Assert.Equal(2, await OnHandAsync(c, s.Part, s.Store));
        Assert.Equal("Worn", (await UnitFitTests.MovesAsync(fx, s.U1)).Last().Note);

        Assert.Equal("done", Status(await ActAsync(c, "unit.fit", new { unitId = s.U1, assetId = s.Chiller }, s.Wo)));
        await SiteSettingsTests.SetSiteAsync(c, s.Org.Site, new { defaultStoreId = (Guid?)null });
        var answer = await ActAsync(c, "unit.remove", new { unitId = s.U1, outcome = "Store" }, s.Wo);
        Assert.Equal(("rejected", "This job's site has no default store."), (Status(answer), Message(answer)));
        Assert.Equal("done", Status(await ActAsync(c, "unit.remove", new { unitId = s.U1, outcome = "Repair" }, s.Wo)));
        Assert.Equal(AssetStatus.UnderRepair, (await UnitFitTests.UnitAsync(fx, s.U1)).Status);
        var other = await ActAsync(c, "unit.fit", new { unitId = s.U2, assetId = s.U1 }, s.Wo);
        Assert.Equal(("rejected", "Choose an asset of this work order."), (Status(other), Message(other)));
    }

    [SkippableFact]
    public async Task Phone_fit_of_a_unit_fitted_meanwhile_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await UnitFitTests.Fit(s.Org.Admin, s.Wo, s.U1, s.Chiller));
        var answer = await ActAsync(s.Org.Admin, "unit.fit", new { unitId = s.U1, assetId = s.Chiller }, s.Wo, at: DateTimeOffset.UtcNow.AddMinutes(-20));
        Assert.Equal(("rejected", "This unit isn't in a store."), (Status(answer), Message(answer)));
        Assert.Equal(1, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
    }

    [SkippableFact]
    public async Task Bundle_carries_units()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        await Ok(await UnitFitTests.Fit(s.Org.Admin, s.Wo, s.U1, s.Chiller));
        var job = (await Ok(await s.Org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == s.Wo);
        var fitted = Assert.Single(job.GetProperty("units").EnumerateArray());
        Assert.Equal((s.U1, "PH-1", s.Chiller, "Pump"), (Id(fitted), fitted.GetProperty("serialNumber").GetString(), fitted.GetProperty("fittedToId").GetGuid(),
            fitted.GetProperty("partName").GetString()));
        var stock = await Ok(await s.Org.Admin.GetAsync("/api/tech/stock"));
        var spare = Assert.Single(stock.GetProperty("units").EnumerateArray());
        Assert.Equal((s.U2, "PH-2", s.Store, s.Part), (Id(spare), spare.GetProperty("serialNumber").GetString(), spare.GetProperty("storeId").GetGuid(), spare.GetProperty("partId").GetGuid()));
        Assert.True(stock.GetProperty("parts").EnumerateArray().Single(p => Id(p) == s.Part).GetProperty("isSerialTracked").GetBoolean());
    }
}
