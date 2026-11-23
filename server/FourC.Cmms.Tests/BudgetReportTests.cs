using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.BudgetTests;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.CostCentreWorkTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: budget against actual, committed spend, and the Maintenance costs report by cost centre.</summary>
public class BudgetReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly MonthStart => new(Today.Year, Today.Month, 1);

    private static async Task<JsonElement> ReportAsync(HttpClient c, string key, string query = "") =>
        await Ok(await c.GetAsync($"/api/reports/{key}?from={D(MonthStart.AddMonths(-1))}&to={D(Today.AddDays(40))}{query}"));

    private static List<JsonElement> Rows(JsonElement r) => r.GetProperty("rows").EnumerateArray().ToList();
    private static decimal N(JsonElement row, string key) => row.GetProperty(key).GetDecimal();
    private static JsonElement RowOf(JsonElement r, string group) => Rows(r).Single(x => x.GetProperty("group").GetString() == group);

    /// <summary>An employee at 50 an hour logging <paramref name="hours"/> today on a new work order of <paramref name="asset"/>.</summary>
    private static async Task<Guid> LabourAsync(PurchaseRequestTests.Org o, Guid? asset, decimal hours, string code)
    {
        var e = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code, fullName = code, isContractor = false, hourlyRate = 50m })));
        var wo = await WorkOrderAsync(o.Admin, o.Site, asset, $"Work {code}");
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = e, workDate = D(Today), hours, overtimeHours = 0m }));
        return wo;
    }

    [SkippableFact]
    public async Task Actual_on_work_orders_matches_maintenance_costs()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var line = await CostCentreAsync(o.Admin, "LINE1", "Line 1", prod);
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Press", costCentre: line), 2, "E1");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Spare press"), 3, "E2");
        var cost = await WorkOrderAsync(o.Admin, o.Site, await AssetAsync(o.Admin, o.Site, "Wrapper", costCentre: prod), "Other cost");
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{cost}/costs", new { description = "Crane hire", quantity = 1m, unitCost = 40m }));
        var budget = await ReportAsync(o.Admin, "budget-vs-actual");
        var top = Rows(budget).Where(r => r.GetProperty("level").GetInt32() == 0).ToList();
        var onWork = top.Sum(r => N(r, "labor") + N(r, "parts") + N(r, "contractor") + N(r, "other"));
        var costs = await ReportAsync(o.Admin, "maintenance-costs");
        var total = costs.GetProperty("summary").EnumerateArray().First().GetProperty("value").GetDecimal();
        Assert.Equal(total, onWork);
        Assert.Equal(290m, onWork); // 100 + 150 + 40
        Assert.Equal(150m, N(RowOf(budget, "No cost centre"), "labor"));
    }

    [SkippableFact]
    public async Task Committed_is_the_open_part_of_approved_work_order_lines()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing");
        var wo = await WorkOrderAsync(o.Admin, o.Site, await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod));
        async Task<Guid> OrderAsync(bool approve, Guid? forWork, string? cancel = null)
        {
            var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
            {
                siteId = o.Site, supplierId = o.Supplier, currency = "EUR", exchangeRate = 50m,
                lines = new[] { new { description = "Repair", quantity = 10m, unitPrice = 2m, discountPercent = 0m, workOrderId = forWork } },
            })));
            if (approve)
            {
                await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
                await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
            }
            if (cancel is not null) await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/cancel", new { reason = cancel }));
            return po;
        }
        var po = await OrderAsync(true, wo);
        var line = Id((await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0]);
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 4m } } }));
        await OrderAsync(false, wo);                 // a draft
        await OrderAsync(true, wo, "Not needed");    // cancelled
        await OrderAsync(true, null);                // not for a work order
        Assert.Equal(600m, N(RowOf(await ReportAsync(o.Admin, "budget-vs-actual"), "Production"), "committed"));
        await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders/cost-centre", new { ids = new[] { wo }, costCentreId = pack }));
        var after = await ReportAsync(o.Admin, "budget-vs-actual");
        Assert.Equal(600m, N(RowOf(after, "Packing"), "committed"));
        Assert.Equal(0m, N(RowOf(after, "Production"), "committed"));
    }

    [SkippableFact]
    public async Task Parents_add_up_their_children_and_direct_issues_have_a_column()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var line = await CostCentreAsync(o.Admin, "LINE1", "Line 1", prod);
        var month = new decimal?[12];
        month[Today.Month - 1] = 1000m;
        await Ok(await PutBudgetAsync(o.Admin, Today.Year, prod, month));
        month[Today.Month - 1] = 500m;
        await Ok(await PutBudgetAsync(o.Admin, Today.Year, line, month));
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Press", costCentre: line), 2, "E1");
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 10, unitCost = 10 }));
        var l = await LocationAsync(o.Admin, o.Site, "L");
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = o.Store, quantity = 3, locationId = l, costCentreId = prod }));
        var r = await ReportAsync(o.Admin, "budget-vs-actual");
        var p = RowOf(r, "Production");
        Assert.Equal((1500m, 130m, 30m), (N(p, "budget"), N(p, "actual"), N(p, "directIssues")));
        Assert.Equal(1370m, N(p, "variance"));
        Assert.Equal(1, RowOf(r, "Line 1").GetProperty("level").GetInt32());
        Assert.Equal(100m, N(RowOf(r, "Line 1"), "actual"));
    }

    [SkippableFact]
    public async Task Maintenance_costs_by_cost_centre_keeps_its_totals()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var line = await CostCentreAsync(o.Admin, "LINE1", "Line 1", prod);
        var other = await CostCentreAsync(o.Admin, "FAC", "Facilities");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Press", costCentre: line), 2, "E1");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Wrapper", costCentre: prod), 1, "E2");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Boiler", costCentre: other), 4, "E3");
        decimal Total(JsonElement x) => x.GetProperty("summary").EnumerateArray().First().GetProperty("value").GetDecimal();
        var all = await ReportAsync(o.Admin, "maintenance-costs");
        var grouped = await ReportAsync(o.Admin, "maintenance-costs", "&groupBy=costCentre");
        Assert.Equal(Total(all), Total(grouped));
        Assert.Equal(Total(all), Rows(grouped).Sum(x => N(x, "total")));
        Assert.Equal(150m, Total(await ReportAsync(o.Admin, "maintenance-costs", $"&costCentreId={prod}")));
    }

    [SkippableFact]
    public async Task Technician_grouping_follows_the_cost_centre_filter()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var other = await CostCentreAsync(o.Admin, "FAC", "Facilities");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Press", costCentre: prod), 2, "E1");
        await LabourAsync(o, await AssetAsync(o.Admin, o.Site, "Boiler", costCentre: other), 4, "E2");
        var r = await ReportAsync(o.Admin, "maintenance-costs", $"&groupBy=technician&costCentreId={prod}");
        Assert.Equal(100m, r.GetProperty("summary").EnumerateArray().First().GetProperty("value").GetDecimal());
        Assert.Equal(100m, Rows(r).Sum(x => N(x, "total")));
    }
}
