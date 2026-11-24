using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Pm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;

namespace FourC.Cmms.Tests;

/// <summary>Fitting and removing serial units on work orders (0.24.0).</summary>
public class UnitFitTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>Two units of a serial part in store MAIN, and Chiller 2 (with an hours meter at 100) and a work order on it.</summary>
    internal sealed record Setup(PurchaseRequestTests.Org O, Guid Part, Guid U1, Guid U2, Guid Chiller, Guid Meter, Guid Wo);

    internal static async Task<Setup> SetupAsync(ApiFixture fx)
    {
        var o = await OrgAsync(fx);
        var part = await SerialPartAsync(o.Admin, o, "PMP-9", "S-1", "S-2");
        var units = await UnitsAsync(fx, part);
        var chiller = await CostCentreWorkTests.AssetAsync(o.Admin, o.Site, "Chiller 2");
        var meter = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/meters", new { code = "CH2", name = "Chiller hours", assetId = chiller, unit = "h", isActive = true })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 100, confirm = true }));
        var wo = await CostCentreWorkTests.WorkOrderAsync(o.Admin, o.Site, chiller, "Pump swap");
        return new Setup(o, part, units[0].Id, units[1].Id, chiller, meter, wo);
    }

    internal static Task<HttpResponseMessage> Fit(HttpClient c, Guid wo, Guid unit, Guid asset, bool countHours = true) =>
        c.PostAsJsonAsync($"/api/work-orders/{wo}/units/fit", new { unitId = unit, assetId = asset, countHours });

    internal static Task<HttpResponseMessage> Remove(HttpClient c, Guid unit, Guid? wo, string outcome, Guid? store = null, string? note = null) =>
        c.PostAsJsonAsync($"/api/serial-units/{unit}/remove", new { workOrderId = wo, outcome, storeId = store, note });

    internal static async Task<Asset> UnitAsync(ApiFixture fx, Guid id)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CmmsDbContext>().Assets.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == id);
    }

    internal static async Task<List<Meter>> MetersOfAsync(ApiFixture fx, Guid asset)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CmmsDbContext>().Meters.IgnoreQueryFilters().AsNoTracking().Where(m => m.AssetId == asset && !m.IsDeleted).ToListAsync();
    }

    internal static async Task<List<UnitMovement>> MovesAsync(ApiFixture fx, Guid unit)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CmmsDbContext>().UnitMovements.IgnoreQueryFilters().AsNoTracking().Where(m => m.UnitId == unit).OrderBy(m => m.At).ToListAsync();
    }

    internal static async Task<List<JsonElement>> TxAsync(HttpClient c, string query) =>
        (await Ok(await c.GetAsync($"/api/inventory/transactions?{query}"))).GetProperty("items").EnumerateArray().ToList();

    [SkippableFact]
    public async Task Fitting_moves_the_unit_onto_the_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        var u = await UnitAsync(fx, s.U1);
        Assert.Equal(s.Chiller, u.ParentAssetId);
        Assert.Equal(AssetStatus.Active, u.Status);
        Assert.Null(u.StoreId);
        Assert.Equal(1, await OnHandAsync(s.O.Admin, s.Part, s.O.Store));
        var issued = Assert.Single(await TxAsync(s.O.Admin, $"workOrderId={s.Wo}"));
        Assert.Equal("Issue", issued.GetProperty("type").GetString());
        Assert.Equal(s.Part, issued.GetProperty("partId").GetGuid());
        Assert.Contains(await MovesAsync(fx, s.U1), m => m.Kind == UnitMovementKind.Fitted && m.WorkOrderId == s.Wo && m.AssetId == s.Chiller);
        var listed = Assert.Single((await Ok(await s.O.Admin.GetAsync($"/api/work-orders/{s.Wo}/units"))).EnumerateArray());
        Assert.Equal("S-1", listed.GetProperty("serialNumber").GetString());
        Assert.Equal(s.Chiller, listed.GetProperty("fittedToId").GetGuid());
        Assert.Equal("PMP-9", listed.GetProperty("partNumber").GetString());
        // The unit counts the chiller's running hours from now on.
        var rh = Assert.Single(await MetersOfAsync(fx, s.U1));
        Assert.Equal(s.Meter, rh.SourceMeterId);
        Assert.Equal("h", rh.Unit);
        Assert.StartsWith("RH", rh.Code);
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/meters/{s.Meter}/readings", new { value = 110, confirm = true }));
        Assert.Equal(10, (await MetersOfAsync(fx, s.U1)).Single().CurrentReading);
    }

    [SkippableFact]
    public async Task A_unit_cannot_be_fitted_twice()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller, countHours: false));
        Assert.Empty(await MetersOfAsync(fx, s.U1));
        await Refused(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller), "This unit isn't in a store.");
        var other = await CostCentreWorkTests.WorkOrderAsync(s.O.Admin, s.O.Site, s.Chiller, "Again");
        await Refused(await Fit(s.O.Admin, other, s.U1, s.Chiller), "This unit isn't in a store.");
        await Refused(await Fit(s.O.Admin, s.Wo, s.U2, s.U1), "Choose an asset of this work order.");
    }

    [SkippableFact]
    public async Task Fit_and_remove_follow_work_order_rules()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        var site2 = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var far = await CostCentreWorkTests.AssetAsync(s.O.Admin, site2, "Far pump");
        var farWo = await CostCentreWorkTests.WorkOrderAsync(s.O.Admin, site2, far, "Far job");
        await Refused(await Fit(s.O.Admin, farWo, s.U1, far), "Move the unit to a store at that site first.");
        var boiler = await CostCentreWorkTests.AssetAsync(s.O.Admin, s.O.Site, "Boiler");
        await Refused(await Fit(s.O.Admin, s.Wo, s.U1, boiler), "Choose an asset of this work order.");
        var closed = await CostCentreWorkTests.WorkOrderAsync(s.O.Admin, s.O.Site, s.Chiller, "Done job");
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/work-orders/{closed}/status", new { status = "InProgress" }));
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/work-orders/{closed}/status", new { status = "Completed", completionNotes = "Done" }));
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/work-orders/{closed}/status", new { status = "Closed" }));
        await Refused(await Fit(s.O.Admin, closed, s.U1, s.Chiller), "This work order is closed. Reopen it to make changes.");
        var (noStock, _, _) = await UserAsync(fx, s.O.Admin, "No stock",
            [Permissions.WorkOrdersView, Permissions.WorkOrdersExecute, Permissions.AssetsView, Permissions.InventoryView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fit(noStock, s.Wo, s.U1, s.Chiller)).StatusCode);
        // Removal on a work order: the unit must be on one of its assets.
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        var boilerWo = await CostCentreWorkTests.WorkOrderAsync(s.O.Admin, s.O.Site, boiler, "Boiler job");
        await Refused(await Remove(s.O.Admin, s.U1, boilerWo, "Store", s.O.Store), "This unit isn't fitted to this work order's asset.");
        await Refused(await Remove(s.O.Admin, s.U1, closed, "Store", s.O.Store), "This work order is closed. Reopen it to make changes.");
    }

    [SkippableFact]
    public async Task Removing_back_to_store_credits_no_work_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        var wo2 = await CostCentreWorkTests.WorkOrderAsync(s.O.Admin, s.O.Site, s.Chiller, "Pump back");
        await Ok(await Remove(s.O.Admin, s.U1, wo2, "Store", s.O.Store, "Worn seal"));
        Assert.Equal(2, await OnHandAsync(s.O.Admin, s.Part, s.O.Store));
        var back = Assert.Single(await TxAsync(s.O.Admin, $"partId={s.Part}&type=Return"));
        Assert.Equal(JsonValueKind.Null, back.GetProperty("workOrderId").ValueKind);
        Assert.Empty(await TxAsync(s.O.Admin, $"workOrderId={wo2}"));
        Assert.Single(await TxAsync(s.O.Admin, $"workOrderId={s.Wo}"));
        var u = await UnitAsync(fx, s.U1);
        Assert.Equal((AssetStatus.InStorage, s.O.Store, (Guid?)null), (u.Status, u.StoreId, u.ParentAssetId));
        Assert.Null((await MetersOfAsync(fx, s.U1)).Single().SourceMeterId);
        var removed = (await MovesAsync(fx, s.U1)).Last();
        Assert.Equal((UnitMovementKind.Removed, (Guid?)wo2, (Guid?)s.Chiller, (UnitOutcome?)UnitOutcome.Store, "Worn seal"),
            (removed.Kind, removed.WorkOrderId, removed.AssetId, removed.Outcome, removed.Note));
        Assert.Empty((await Ok(await s.O.Admin.GetAsync($"/api/work-orders/{s.Wo}/units"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task Remove_to_repair_and_scrap()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        await Ok(await Fit(s.O.Admin, s.Wo, s.U2, s.Chiller));
        await Refused(await Remove(s.O.Admin, s.U1, s.Wo, "Store"), "Choose the store it goes back to.");
        await Refused(await Remove(s.O.Admin, s.U1, s.Wo, "Lost"), "Choose what happens to the unit.");
        await Ok(await Remove(s.O.Admin, s.U1, s.Wo, "Repair"));
        var u1 = await UnitAsync(fx, s.U1);
        Assert.Equal((AssetStatus.UnderRepair, (Guid?)null, (Guid?)null), (u1.Status, u1.StoreId, u1.ParentAssetId));
        Assert.Equal(0, await OnHandAsync(s.O.Admin, s.Part, s.O.Store));
        await Ok(await Remove(s.O.Admin, s.U2, null, "Scrap", note: "Cracked casing"));
        var u2 = await UnitAsync(fx, s.U2);
        Assert.Equal(AssetStatus.Disposed, u2.Status);
        Assert.NotNull(u2.DisposedOn);
        Assert.Null(u2.ParentAssetId);
        Assert.Equal(0, await OnHandAsync(s.O.Admin, s.Part, s.O.Store));
        await Refused(await Remove(s.O.Admin, s.U2, null, "Store", s.O.Store), "This unit isn't fitted.");
    }
}
