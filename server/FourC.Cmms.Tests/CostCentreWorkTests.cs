using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: the cost centre in force for an asset or location, and given to every new work order however it is made.</summary>
public class CostCentreWorkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    internal static async Task<Guid> LocationAsync(HttpClient c, Guid site, string code, Guid? parent = null, Guid? costCentre = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/locations", new
        {
            siteId = site, parentId = parent, code, name = code, type = "Area", criticality = "Medium", isActive = true, costCentreId = costCentre, managesCostCentre = true,
        })));

    internal static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name, Guid? location = null, Guid? parent = null, Guid? costCentre = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new
        {
            name, siteId = site, locationId = location, parentAssetId = parent, status = "Active", criticality = "High", warrantyType = "None",
            costCentreId = costCentre, managesCostCentre = true,
        })));

    internal static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site, Guid? asset, string title = "Fix", Guid? location = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, siteId = site, assetId = asset, locationId = location, type = "Corrective", priority = "Medium" })));

    internal static async Task<Guid?> CostCentreOf(HttpClient c, Guid workOrder)
    {
        var w = await Ok(await c.GetAsync($"/api/work-orders/{workOrder}"));
        return w.GetProperty("costCentreId").ValueKind == JsonValueKind.Null ? null : w.GetProperty("costCentreId").GetGuid();
    }

    private static async Task SetAssetCostCentre(HttpClient c, Guid asset, Guid? costCentre)
    {
        var a = await Ok(await c.GetAsync($"/api/assets/{asset}"));
        await Ok(await c.PutAsJsonAsync($"/api/assets/{asset}", new
        {
            assetNumber = a.GetProperty("assetNumber").GetString(), name = a.GetProperty("name").GetString(), siteId = a.GetProperty("siteId").GetGuid(),
            locationId = a.GetProperty("locationId").ValueKind == JsonValueKind.Null ? (Guid?)null : a.GetProperty("locationId").GetGuid(),
            parentAssetId = a.GetProperty("parentAssetId").ValueKind == JsonValueKind.Null ? (Guid?)null : a.GetProperty("parentAssetId").GetGuid(),
            status = "Active", criticality = "High", warrantyType = "None", costCentreId = costCentre, managesCostCentre = true,
            rowVersion = a.GetProperty("rowVersion").GetString(),
        }));
    }

    [SkippableFact]
    public async Task In_force_follows_the_asset_then_its_parents_then_its_location()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var fac = await CostCentreAsync(o.Admin, "FAC", "Facilities");
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var l = await LocationAsync(o.Admin, o.Site, "L", costCentre: fac);
        var l2 = await LocationAsync(o.Admin, o.Site, "L2", l);
        var parent = await AssetAsync(o.Admin, o.Site, "Press", l2, costCentre: prod);
        var child = await AssetAsync(o.Admin, o.Site, "Press motor", l2, parent);
        Assert.Equal(prod, await CostCentreOf(o.Admin, await WorkOrderAsync(o.Admin, o.Site, child)));
        await SetAssetCostCentre(o.Admin, parent, null);
        Assert.Equal(fac, await CostCentreOf(o.Admin, await WorkOrderAsync(o.Admin, o.Site, child)));
        Assert.Equal(fac, await CostCentreOf(o.Admin, await WorkOrderAsync(o.Admin, o.Site, null, location: l2)));
    }

    [SkippableFact]
    public async Task Every_way_of_creating_work_gets_the_cost_centre()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing");
        var a = await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod);
        var b = await AssetAsync(o.Admin, o.Site, "Wrapper", costCentre: pack);

        Assert.Equal(prod, await CostCentreOf(o.Admin, await WorkOrderAsync(o.Admin, o.Site, a)));

        var request = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = o.Site, assetId = a, priority = "Medium", type = "Corrective" })));
        var fromRequest = (await Ok(await o.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        Assert.Equal(prod, await CostCentreOf(o.Admin, fromRequest));

        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var single = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, a, extra: new() { ["code"] = "PM1" })));
        var group = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, assets: [a, b],
            extra: new() { ["code"] = "PM2", ["multiAssetMode"] = "Group", ["groupPlan"] = "Copy" })));
        await Pm12.GenerateAsync(o.Admin, Today, single, group);
        var generated = (await Ok(await o.Admin.GetAsync("/api/work-orders?source=pm&pageSize=100"))).GetProperty("items").EnumerateArray().ToList();
        foreach (var w in generated)
        {
            var asset = w.GetProperty("assetId").GetGuid();
            Assert.Equal(asset == b ? pack : prod, await CostCentreOf(o.Admin, Id(w)));
        }
        Assert.Contains(generated, w => w.GetProperty("assetId").GetGuid() == b);

        var me = (await Ok(await o.Admin.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "ME", fullName = "Admin", isContractor = false, userId = me }));
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(o.Admin, "workorder.create", new { title = "From the phone", siteId = o.Site, assetId = b })));
        var phone = (await Ok(await o.Admin.GetAsync("/api/work-orders?search=From%20the%20phone"))).GetProperty("items")[0];
        Assert.Equal(pack, await CostCentreOf(o.Admin, Id(phone)));
    }

    [SkippableFact]
    public async Task PM_and_request_overrides_win()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var maint = await CostCentreAsync(o.Admin, "MAINT", "Maintenance");
        var a = await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod);
        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, a, extra: new() { ["code"] = "PM1", ["costCentreId"] = maint })));
        await Pm12.GenerateAsync(o.Admin, Today, pm);
        var generated = (await Ok(await o.Admin.GetAsync("/api/work-orders?source=pm&pageSize=100"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(maint, await CostCentreOf(o.Admin, Id(generated)));
        var request = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new
        {
            title = "Noise", siteId = o.Site, assetId = a, priority = "Medium", type = "Corrective", costCentreId = maint,
        })));
        var wo = (await Ok(await o.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        Assert.Equal(maint, await CostCentreOf(o.Admin, wo));
    }

    [SkippableFact]
    public async Task Changing_an_assets_cost_centre_leaves_existing_work()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing");
        var a = await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod);
        var before = await WorkOrderAsync(o.Admin, o.Site, a);
        await SetAssetCostCentre(o.Admin, a, pack);
        Assert.Equal(prod, await CostCentreOf(o.Admin, before));
        Assert.Equal(pack, await CostCentreOf(o.Admin, await WorkOrderAsync(o.Admin, o.Site, a)));
    }

    [SkippableFact]
    public async Task A_round_takes_the_cost_centre_its_assets_share_else_its_locations()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing");
        var fac = await CostCentreAsync(o.Admin, "FAC", "Facilities");
        var hall = await LocationAsync(o.Admin, o.Site, "HALL", costCentre: fac);
        var a = await AssetAsync(o.Admin, o.Site, "Press 1", hall, costCentre: prod);
        var b = await AssetAsync(o.Admin, o.Site, "Press 2", hall, costCentre: prod);
        var c = await AssetAsync(o.Admin, o.Site, "Wrapper", hall, costCentre: pack);
        var task = await Pm12.TemplateAsync(o.Admin, "LUBE");
        var same = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, assets: [a, b], extra: new() { ["code"] = "R1", ["multiAssetMode"] = "Round" })));
        var mixed = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, assets: [a, c], extra: new() { ["code"] = "R2", ["multiAssetMode"] = "Round" })));
        await Pm12.GenerateAsync(o.Admin, Today, same, mixed);
        var generated = (await Ok(await o.Admin.GetAsync("/api/work-orders?source=pm&pageSize=100"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, generated.Count);
        var bySchedule = new Dictionary<Guid, Guid?>();
        foreach (var w in generated)
        {
            var detail = await Ok(await o.Admin.GetAsync($"/api/work-orders/{Id(w)}"));
            bySchedule[detail.GetProperty("pm").GetProperty("id").GetGuid()] = await CostCentreOf(o.Admin, Id(w));
        }
        Assert.Equal(prod, bySchedule[same]);
        Assert.Equal(fac, bySchedule[mixed]);
    }
}
