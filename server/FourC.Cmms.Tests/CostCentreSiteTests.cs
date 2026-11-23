using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.BudgetTests;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.CostCentreWorkTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0 review: what a site-limited person sees of cost centres used at other sites.</summary>
public class CostCentreSiteTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string Range => $"from={D(new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1))}&to={D(Today.AddDays(40))}";
    private static List<JsonElement> Rows(JsonElement r) => r.GetProperty("rows").EnumerateArray().ToList();
    private static decimal N(JsonElement row, string key) => row.GetProperty(key).GetDecimal();
    private static JsonElement RowOf(JsonElement r, string group) => Rows(r).Single(x => x.GetProperty("group").GetString() == group);
    private static decimal Summary(JsonElement r, string label) => r.GetProperty("summary").EnumerateArray().Single(s => s.GetProperty("label").GetString() == label).GetProperty("value").GetDecimal();

    private sealed record Setup(PurchaseRequestTests.Org O, Guid SiteB, Guid Shared, Guid BOnly, HttpClient Limited);

    /// <summary>
    /// Site A (the org's): 100 of labour on SHARED and 50 with no cost centre. Site B: 200 of labour on B-ONLY (budget 999), 150 with
    /// no cost centre, and 30 of parts issued straight to a site-B place on SHARED. The limited person sees site A only.
    /// </summary>
    private async Task<Setup> SetupAsync()
    {
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        var shared = await CostCentreAsync(o.Admin, "SHARED", "Shared");
        var bOnly = await CostCentreAsync(o.Admin, "BONLY", "B only", site: siteB);
        async Task LabourAsync(Guid site, Guid? asset, decimal hours, string code)
        {
            var e = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code, fullName = code, isContractor = false, hourlyRate = 50m })));
            var wo = await WorkOrderAsync(o.Admin, site, asset, $"Work {code}");
            await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = e, workDate = D(Today), hours, overtimeHours = 0m }));
        }
        await LabourAsync(o.Site, await AssetAsync(o.Admin, o.Site, "A press", costCentre: shared), 2, "E1");
        await LabourAsync(o.Site, await AssetAsync(o.Admin, o.Site, "A spare"), 1, "E2");
        await LabourAsync(siteB, await AssetAsync(o.Admin, siteB, "B press", costCentre: bOnly), 4, "E3");
        await LabourAsync(siteB, await AssetAsync(o.Admin, siteB, "B spare"), 3, "E4");
        var month = new decimal?[12];
        month[Today.Month - 1] = 999m;
        await Ok(await PutBudgetAsync(o.Admin, Today.Year, bOnly, month));
        var storeB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stores", new { code = "BST", name = "B store", siteId = siteB })));
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = storeB, quantity = 10, unitCost = 10 }));
        var placeB = await LocationAsync(o.Admin, siteB, "BHALL");
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = storeB, quantity = 3, locationId = placeB, costCentreId = shared }));
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Lead A", ["workorders.view", "reports.view", "settings.manage"], siteIds: [o.Site]);
        return new Setup(o, siteB, shared, bOnly, limited);
    }

    [SkippableFact]
    public async Task Budget_report_shows_a_site_limited_viewer_only_their_sites_figures()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync();
        // The shared cost centre is whole: its site-A work and its site-B direct issues.
        var byCentre = await Ok(await s.Limited.GetAsync($"/api/reports/budget-vs-actual?{Range}"));
        Assert.Equal((100m, 30m, 130m), (N(RowOf(byCentre, "Shared"), "labor"), N(RowOf(byCentre, "Shared"), "directIssues"), N(RowOf(byCentre, "Shared"), "actual")));
        Assert.DoesNotContain(Rows(byCentre), r => r.GetProperty("group").GetString() == "B only");
        Assert.Equal(50m, N(RowOf(byCentre, "No cost centre"), "labor"));
        // By month: the same figures, never site B's own cost centre or its work without one.
        var byMonth = await Ok(await s.Limited.GetAsync($"/api/reports/budget-vs-actual?{Range}&groupBy=month"));
        Assert.Equal((0m, 180m), (Summary(byMonth, "Budget"), Summary(byMonth, "Actual")));
        Assert.Equal((Summary(byCentre, "Budget"), Summary(byCentre, "Actual")), (Summary(byMonth, "Budget"), Summary(byMonth, "Actual")));
        // A cost centre of another site can't be asked for.
        Assert.Equal(HttpStatusCode.NotFound, (await s.Limited.GetAsync($"/api/reports/budget-vs-actual?{Range}&groupBy=month&costCentreId={s.BOnly}")).StatusCode);
        // Everyone at every site sees it all.
        var all = await Ok(await s.O.Admin.GetAsync($"/api/reports/budget-vs-actual?{Range}&groupBy=month"));
        Assert.Equal((999m, 530m), (Summary(all, "Budget"), Summary(all, "Actual")));
    }

    [SkippableFact]
    public async Task A_shared_cost_centre_used_at_another_site_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync();
        // Its only use outside site A is the site-B direct issue (the site-A asset is moved off it first).
        var asset = (await Ok(await s.O.Admin.GetAsync("/api/assets?search=A press"))).GetProperty("items")[0];
        var detail = await Ok(await s.O.Admin.GetAsync($"/api/assets/{Id(asset)}"));
        await Ok(await s.O.Admin.PutAsJsonAsync($"/api/assets/{Id(asset)}", new
        {
            assetNumber = detail.GetProperty("assetNumber").GetString(), name = "A press", siteId = s.O.Site, status = "Active", criticality = "High", warrantyType = "None",
            costCentreId = (Guid?)null, managesCostCentre = true, rowVersion = detail.GetProperty("rowVersion").GetString(),
        }));
        var other = await CostCentreAsync(s.O.Admin, "OTHER", "Other");
        var wos = (await Ok(await s.O.Admin.GetAsync("/api/work-orders?pageSize=50"))).GetProperty("items").EnumerateArray().Select(Id).ToArray();
        await Ok(await s.O.Admin.PostAsJsonAsync("/api/work-orders/cost-centre", new { ids = wos, costCentreId = other }));
        var refused = await s.Limited.DeleteAsync($"/api/cost-centres/{s.Shared}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("in use", await refused.Content.ReadAsStringAsync());
    }
}
