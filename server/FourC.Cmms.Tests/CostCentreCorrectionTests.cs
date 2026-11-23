using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.CostCentreWorkTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: correcting cost centres — on any work order, on direct issues, and filling in past work.</summary>
public class CostCentreCorrectionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    [SkippableFact]
    public async Task Change_works_on_closed_work_and_is_logged()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var maint = await CostCentreAsync(o.Admin, "MAINT", "Maintenance");
        var a = await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod);
        var wo = await WorkOrderAsync(o.Admin, o.Site, a);
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "InProgress" }));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" }));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Closed" }));
        var changed = await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders/cost-centre", new { ids = new[] { wo }, costCentreId = maint }));
        Assert.Equal(1, changed.GetProperty("changed").GetInt32());
        Assert.Equal(maint, await CostCentreOf(o.Admin, wo));
        var history = await Ok(await o.Admin.GetAsync($"/api/audit?entityType=WorkOrder&entityId={wo}"));
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("changes").GetString()?.Contains("CostCentreId") == true);
        var (doer, _, _) = await UserAsync(fx, o.Admin, "Doer", ["workorders.view", "workorders.execute"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await doer.PostAsJsonAsync("/api/work-orders/cost-centre", new { ids = new[] { wo }, costCentreId = prod })).StatusCode);
    }

    [SkippableFact]
    public async Task Direct_issue_takes_the_locations_cost_centre_and_can_change()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var fac = await CostCentreAsync(o.Admin, "FAC", "Facilities");
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var maint = await CostCentreAsync(o.Admin, "MAINT", "Maintenance");
        var l = await LocationAsync(o.Admin, o.Site, "L", costCentre: fac);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 10, unitCost = 5 }));
        var first = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = o.Store, quantity = 2, locationId = l })));
        var second = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = o.Store, quantity = 1, locationId = l, costCentreId = prod })));
        async Task<Guid?> Of(Guid tx)
        {
            var row = (await Ok(await o.Admin.GetAsync($"/api/inventory/transactions?partId={o.Part}&pageSize=50"))).GetProperty("items").EnumerateArray().Single(x => Id(x) == tx);
            return row.GetProperty("costCentreId").ValueKind == JsonValueKind.Null ? null : row.GetProperty("costCentreId").GetGuid();
        }
        Assert.Equal(fac, await Of(first));
        Assert.Equal(prod, await Of(second));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/inventory/transactions/{first}/cost-centre", new { costCentreId = maint }));
        Assert.Equal(maint, await Of(first));
        var wo = await WorkOrderAsync(o.Admin, o.Site, null, location: l);
        var toWork = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = o.Store, quantity = 1, workOrderId = wo })));
        var refused = await o.Admin.PutAsJsonAsync($"/api/inventory/transactions/{toWork}/cost-centre", new { costCentreId = maint });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Only parts issued to a location have their own cost centre.", await refused.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Fill_in_never_overwrites_counts_first_and_repeats()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing");
        var a = await AssetAsync(o.Admin, o.Site, "Press");
        var blanks = new List<Guid>();
        for (var i = 0; i < 3; i++) blanks.Add(await WorkOrderAsync(o.Admin, o.Site, a, $"Blank {i}"));
        var kept = await WorkOrderAsync(o.Admin, o.Site, a, "Kept");
        var old = await WorkOrderAsync(o.Admin, o.Site, a, "Old");
        await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders/cost-centre", new { ids = new[] { kept }, costCentreId = pack }));
        // The old one was raised long ago.
        await Loop.DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreatedAt, DateTimeOffset.UtcNow.AddYears(-2))));
        // The asset gets a cost centre later; past work has none.
        var asset = await Ok(await o.Admin.GetAsync($"/api/assets/{a}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/assets/{a}", new
        {
            assetNumber = asset.GetProperty("assetNumber").GetString(), name = "Press", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None",
            costCentreId = prod, managesCostCentre = true, rowVersion = asset.GetProperty("rowVersion").GetString(),
        }));
        var from = Today.AddDays(-30);
        Assert.Equal(3, (await Ok(await o.Admin.GetAsync($"/api/cost-centres/fill-in?from={D(from)}"))).GetProperty("workOrders").GetInt32());
        Assert.Equal(3, (await Ok(await o.Admin.PostAsJsonAsync("/api/cost-centres/fill-in", new { from = D(from) }))).GetProperty("workOrders").GetInt32());
        foreach (var b in blanks) Assert.Equal(prod, await CostCentreOf(o.Admin, b));
        Assert.Equal(pack, await CostCentreOf(o.Admin, kept));
        Assert.Null(await CostCentreOf(o.Admin, old));
        Assert.Equal(0, (await Ok(await o.Admin.PostAsJsonAsync("/api/cost-centres/fill-in", new { from = D(from) }))).GetProperty("workOrders").GetInt32());
    }
}
