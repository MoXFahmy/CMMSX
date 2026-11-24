using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Inventory;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;
using static FourC.Cmms.Tests.UnitFitTests;

namespace FourC.Cmms.Tests;

/// <summary>Unit actions in stores, registering units already on machines, the asset form's limits, lists and history (0.24.0).</summary>
public class UnitActionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static Task<HttpResponseMessage> Act(HttpClient c, Guid unit, string action, object body) =>
        c.PostAsJsonAsync($"/api/serial-units/{unit}/{action}", body);

    internal static async Task<List<JsonElement>> ListAsync(HttpClient c, string query) =>
        (await Ok(await c.GetAsync($"/api/serial-units?{query}"))).GetProperty("items").EnumerateArray().ToList();

    private static async Task<decimal> StockAsync(HttpClient c, Guid part, Guid store) =>
        (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("levels").EnumerateArray()
            .Where(l => l.GetProperty("storeId").GetGuid() == store).Select(l => l.GetProperty("quantityOnHand").GetDecimal()).FirstOrDefault();

    private static Guid? G(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetGuid();

    /// <summary>The asset form as the page sends it, from the asset's detail.</summary>
    private static Dictionary<string, object?> Form(JsonElement d) => new()
    {
        ["assetNumber"] = d.GetProperty("assetNumber").GetString(), ["name"] = d.GetProperty("name").GetString(), ["siteId"] = d.GetProperty("siteId").GetGuid(),
        ["locationId"] = G(d, "locationId"), ["parentAssetId"] = G(d, "parentAssetId"), ["status"] = d.GetProperty("status").GetString(),
        ["criticality"] = d.GetProperty("criticality").GetString(), ["serialNumber"] = d.GetProperty("serialNumber").GetString(),
        ["warrantyType"] = d.GetProperty("warrantyType").GetString(), ["manufacturer"] = d.GetProperty("manufacturer").GetString(), ["model"] = d.GetProperty("model").GetString(),
    };

    [SkippableFact]
    public async Task Stock_follows_every_unit_action()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var store2 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stores", new { code = "STORE2", name = "Second", siteId = o.Site })));
        var part = await SerialPartAsync(o.Admin, o, "PMP-7", "A1", "A2", "A3");
        var u = await UnitsAsync(fx, part);
        async Task Balanced(decimal main, decimal second)
        {
            Assert.Equal(main, await StockAsync(o.Admin, part, o.Store));
            Assert.Equal(second, await StockAsync(o.Admin, part, store2));
            Assert.Equal((int)main, (await ListAsync(o.Admin, $"partId={part}&storeId={o.Store}&state=InStore")).Count);
            Assert.Equal((int)second, (await ListAsync(o.Admin, $"partId={part}&storeId={store2}&state=InStore")).Count);
        }
        await Ok(await Act(o.Admin, u[0].Id, "transfer", new { storeId = store2 }));
        await Balanced(2, 1);
        Assert.Equal(store2, (await UnitAsync(fx, u[0].Id)).StoreId);
        await Ok(await Act(o.Admin, u[1].Id, "send-for-repair", new { note = "Leaking" }));
        await Balanced(1, 1);
        await Refused(await Act(o.Admin, u[2].Id, "back-from-repair", new { storeId = o.Store }), "This unit isn't under repair.");
        await Ok(await Act(o.Admin, u[1].Id, "back-from-repair", new { storeId = o.Store, cost = 120, note = "Rewound" }));
        await Balanced(2, 1);
        await Ok(await o.Admin.PostAsJsonAsync($"/api/parts/{part}/units/found", new { storeId = o.Store, serial = "A4" }));
        await Balanced(3, 1);
        await Refused(await o.Admin.PostAsJsonAsync($"/api/parts/{part}/units/found", new { storeId = o.Store, serial = "a1" }), "Serial number a1 is already used by a unit of this part.");
        await Refused(await Act(o.Admin, u[2].Id, "scrap", new { note = " " }), "Enter why the unit is scrapped.");
        await Ok(await Act(o.Admin, u[2].Id, "scrap", new { note = "Dropped" }));
        await Balanced(2, 1);
        Assert.Equal([UnitMovementKind.Received, UnitMovementKind.SentForRepair, UnitMovementKind.BackFromRepair], (await MovesAsync(fx, u[1].Id)).Select(m => m.Kind));
        Assert.Equal(120, (await MovesAsync(fx, u[1].Id)).Last().Cost);
        Assert.Equal([UnitMovementKind.Received, UnitMovementKind.Transferred], (await MovesAsync(fx, u[0].Id)).Select(m => m.Kind));
        Assert.Equal([UnitMovementKind.Received, UnitMovementKind.Scrapped], (await MovesAsync(fx, u[2].Id)).Select(m => m.Kind));
    }

    [SkippableFact]
    public async Task Units_under_repair_and_fitted_units_have_a_way_out()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        await Refused(await Act(s.O.Admin, s.U1, "scrap", new { note = "Worn" }), "Remove the unit first.");
        await Ok(await Act(s.O.Admin, s.U2, "send-for-repair", new { }));
        Assert.Equal(0, await StockAsync(s.O.Admin, s.Part, s.O.Store));
        await Ok(await Act(s.O.Admin, s.U2, "scrap", new { note = "Not worth repairing" }));
        Assert.Equal(0, await StockAsync(s.O.Admin, s.Part, s.O.Store));
        Assert.Equal(AssetStatus.Disposed, (await UnitAsync(fx, s.U2)).Status);
        await Refused(await Act(s.O.Admin, s.U2, "scrap", new { note = "Again" }), "This unit is already scrapped.");
        await Ok(await Remove(s.O.Admin, s.U1, null, "Store", s.O.Store));
        Assert.Equal(1, await StockAsync(s.O.Admin, s.Part, s.O.Store));
        Assert.Equal(JsonValueKind.Null, Assert.Single(await TxAsync(s.O.Admin, $"partId={s.Part}&type=Return")).GetProperty("workOrderId").ValueKind);
    }

    [SkippableFact]
    public async Task Units_already_on_machines_can_be_registered()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        var fitted = Id(await Ok(await s.O.Admin.PostAsJsonAsync($"/api/parts/{s.Part}/units/fitted", new { assetId = s.Chiller, serial = "F-1" })));
        var f = await UnitAsync(fx, fitted);
        Assert.Equal((s.Chiller, AssetStatus.Active, (Guid?)null, s.Part), (f.ParentAssetId, f.Status, f.StoreId, f.PartId));
        Assert.Equal(2, await StockAsync(s.O.Admin, s.Part, s.O.Store));
        var registered = Assert.Single(await MovesAsync(fx, fitted));
        Assert.Equal((UnitMovementKind.Registered, (Guid?)s.Chiller), (registered.Kind, registered.AssetId));

        Task<Guid> AssetAsync(string name, Guid? parent, string? serial) => Task.Run(async () => Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/assets", new
        {
            name, siteId = s.O.Site, parentAssetId = parent, serialNumber = serial, status = "Active", criticality = "High", warrantyType = "None",
        }))));
        Task<HttpResponseMessage> MakeUnit(Guid asset, Guid part) => s.O.Admin.PostAsJsonAsync($"/api/assets/{asset}/make-unit", new { partId = part });
        await Refused(await MakeUnit(await AssetAsync("Loose pump", null, "X8"), s.Part), "Only an asset fitted to another asset can be made a unit.");
        await Refused(await MakeUnit(await AssetAsync("Old pump", s.Chiller, null), s.Part), "Give the asset a serial number first.");
        var oldPump = await AssetAsync("Old pump 2", s.Chiller, "X9");
        await Refused(await MakeUnit(oldPump, s.O.Part), "Choose a serial-tracked part.");
        await Refused(await MakeUnit(await AssetAsync("Twin pump", s.Chiller, "s-1"), s.Part), "Serial number s-1 is already used by a unit of this part.");
        await Ok(await MakeUnit(oldPump, s.Part));
        Assert.Equal(s.Part, (await UnitAsync(fx, oldPump)).PartId);
        Assert.Equal(UnitMovementKind.Registered, Assert.Single(await MovesAsync(fx, oldPump)).Kind);
        await Refused(await MakeUnit(oldPump, s.Part), "This asset is already a unit.");
        Assert.Equal(2, await StockAsync(s.O.Admin, s.Part, s.O.Store));
    }

    [SkippableFact]
    public async Task Asset_with_fitted_units_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        await Refused(await s.O.Admin.DeleteAsync($"/api/assets/{s.Chiller}"), "Remove its fitted units first.");
    }

    [SkippableFact]
    public async Task A_unit_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Refused(await s.O.Admin.DeleteAsync($"/api/assets/{s.U2}"), "A unit can't be deleted; scrap it instead.");
    }

    [SkippableFact]
    public async Task A_units_place_is_locked_on_the_asset_form()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        const string locked = "A unit's status and place change through fit, remove and the unit actions.";
        var d = await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.U1}"));
        Assert.Equal(("PMP-9", "Pump PMP-9", "Main", "InStore"),
            (d.GetProperty("partNumber").GetString(), d.GetProperty("partName").GetString(), d.GetProperty("storeName").GetString(), d.GetProperty("unitState").GetString()));
        var moved = Form(d); moved["status"] = "Active";
        await Refused(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", moved), locked);
        var parented = Form(d); parented["parentAssetId"] = s.Chiller;
        await Refused(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", parented), locked);
        await Refused(await s.O.Admin.PostAsJsonAsync($"/api/assets/{s.U1}/transfer", new { siteId = s.O.Site, locationId = (Guid?)null, reason = "x", includeChildren = true }), locked);
        await Refused(await s.O.Admin.PostAsJsonAsync("/api/assets/bulk", new { ids = new[] { s.U1 }, status = "Inactive" }), locked);
        var clash = Form(d); clash["serialNumber"] = "s-2";
        await Refused(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", clash), "Serial number s-2 is already used by a unit of this part.");
        var blank = Form(d); blank["serialNumber"] = " ";
        await Refused(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", blank), "A unit needs a serial number.");
        var edited = Form(d); edited["serialNumber"] = "S-1B"; edited["endOfLifeOn"] = "2030-06-30"; edited["managesEndOfLife"] = true;
        await Ok(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", edited));
        var after = await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.U1}"));
        Assert.Equal(("S-1B", "2030-06-30"), (after.GetProperty("serialNumber").GetString(), after.GetProperty("endOfLifeOn").GetString()));
        // A page from before 0.24.0 leaves the end of life alone.
        await Ok(await s.O.Admin.PutAsJsonAsync($"/api/assets/{s.U1}", Form(after)));
        Assert.Equal("2030-06-30", (await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.U1}"))).GetProperty("endOfLifeOn").GetString());
    }

    [SkippableFact]
    public async Task Lists_and_history()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Act(s.O.Admin, s.U2, "send-for-repair", new { note = "Noisy" }));
        await Ok(await Act(s.O.Admin, s.U2, "back-from-repair", new { storeId = s.O.Store, cost = 80 }));
        await Ok(await Act(s.O.Admin, s.U2, "send-for-repair", new { }));
        await Ok(await Act(s.O.Admin, s.U2, "back-from-repair", new { storeId = s.O.Store, cost = 40.5 }));
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        var rows = await ListAsync(s.O.Admin, $"partId={s.Part}");
        var r1 = rows.Single(r => r.GetProperty("id").GetGuid() == s.U1);
        Assert.Equal(("Fitted", (Guid?)s.Chiller, "Chiller 2"), (r1.GetProperty("state").GetString(), G(r1, "fittedToId"), r1.GetProperty("fittedToName").GetString()));
        var r2 = rows.Single(r => r.GetProperty("id").GetGuid() == s.U2);
        Assert.Equal(("InStore", "Main", 2, 120.5m),
            (r2.GetProperty("state").GetString(), r2.GetProperty("storeName").GetString(), r2.GetProperty("repairCount").GetInt32(), r2.GetProperty("repairCost").GetDecimal()));
        Assert.Single(await ListAsync(s.O.Admin, $"partId={s.Part}&state=Fitted"));
        Assert.Single(await ListAsync(s.O.Admin, "search=S-2"));

        var detail = await Ok(await s.O.Admin.GetAsync($"/api/serial-units/{s.U1}"));
        var kinds = detail.GetProperty("movements").EnumerateArray().Select(m => m.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["Fitted", "Received"], kinds);
        var fit = detail.GetProperty("movements")[0];
        Assert.Equal((s.Wo, "Chiller 2"), (fit.GetProperty("workOrderId").GetGuid(), fit.GetProperty("assetName").GetString()));

        var on = await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.Chiller}/fitted-units"));
        Assert.Equal(s.U1, Assert.Single(on.GetProperty("now").EnumerateArray()).GetProperty("unitId").GetGuid());
        Assert.Empty(on.GetProperty("before").EnumerateArray());
        await Ok(await Remove(s.O.Admin, s.U1, s.Wo, "Repair"));
        var off = await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.Chiller}/fitted-units"));
        Assert.Empty(off.GetProperty("now").EnumerateArray());
        var before = Assert.Single(off.GetProperty("before").EnumerateArray());
        Assert.Equal((s.U1, "S-1", "Repair"), (before.GetProperty("unitId").GetGuid(), before.GetProperty("serialNumber").GetString(), before.GetProperty("outcome").GetString()));
        Assert.True(before.GetProperty("to").GetDateTimeOffset() >= before.GetProperty("from").GetDateTimeOffset());
    }
}
