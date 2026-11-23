using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.ContractorLineTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: contractor lines and company-linked labour counted once, as Contractor, in every cost report.</summary>
public class ContractorCostReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Period => $"from={Today.Year}-01-01&to={Today.Year}-12-31";
    private static List<JsonElement> Rows(JsonElement report) => report.GetProperty("rows").EnumerateArray().ToList();

    /// <summary>
    /// A completed breakdown on one asset: a contractor line of 2 h done at 150, E (linked to the company) 1 h at 80, S (staff) 2 h at 50,
    /// and U (a contractor employee with no company) 1 h at 60.
    /// </summary>
    private async Task<(Setup X, Guid Asset)> WorkAsync(bool withContractors = true)
    {
        var o = await OrgAsync(fx);
        var asset = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Lift 1", siteId = o.Site, status = "Active", criticality = "Medium", warrantyType = "None" })));
        var x = await SetupOnAsync(fx, o, asset);
        var c = x.O.Admin;
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/labor", new { employeeId = x.S, workDate = Today, hours = 2, overtimeHours = 0 }));
        if (withContractors)
        {
            await AddLineAsync(c, x.Wo, Line(x.C, x.Elec, done: 2, doneOn: Today));
            await Ok(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/labor", new { employeeId = x.E, workDate = Today, hours = 1, overtimeHours = 0 }));
            var u = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "U1", fullName = "Free contractor", isContractor = true, company = "Someone", hourlyRate = 60m })));
            await Ok(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/labor", new { employeeId = u, workDate = Today, hours = 1, overtimeHours = 0 }));
        }
        await Loop.DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == x.Wo).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkOrderStatus.Completed).SetProperty(w => w.CompletedAt, DateTimeOffset.UtcNow).SetProperty(w => w.Type, WorkType.Breakdown)));
        return (x, asset);
    }

    [SkippableFact]
    public async Task Contractor_line_and_company_linked_labour_count_once_in_contractor()
    {
        Skip.IfNot(fx.Enabled);
        var (x, _) = await WorkAsync();
        var report = await Ok(await x.O.Admin.GetAsync($"/api/reports/maintenance-costs?{Period}"));
        var row = Assert.Single(Rows(report));
        Assert.Equal(160m, row.GetProperty("labor").GetDecimal());
        Assert.Equal(380m, row.GetProperty("contractor").GetDecimal());
        Assert.Equal(540m, row.GetProperty("total").GetDecimal());
        Assert.Equal(4m, row.GetProperty("hours").GetDecimal());
        var keys = report.GetProperty("columns").EnumerateArray().Select(col => col.GetProperty("key").GetString()).ToList();
        Assert.Equal(keys.IndexOf("other") + 1, keys.IndexOf("contractor"));
        Assert.Contains("contractor", report.GetProperty("chart").GetProperty("series").EnumerateArray().Select(s => s.GetString()));
    }

    [SkippableFact]
    public async Task Asset_costs_tab_shows_contractor_per_year()
    {
        Skip.IfNot(fx.Enabled);
        var (x, asset) = await WorkAsync();
        var year = (await Ok(await x.O.Admin.GetAsync($"/api/assets/{asset}/costs"))).GetProperty("years").EnumerateArray().Single(y => y.GetProperty("year").GetInt32() == Today.Year);
        Assert.Equal(160m, year.GetProperty("labor").GetDecimal());
        Assert.Equal(380m, year.GetProperty("contractor").GetDecimal());
        Assert.Equal(540m, year.GetProperty("total").GetDecimal());
    }

    [SkippableFact]
    public async Task Failure_tree_and_asset_history_totals_include_contractor()
    {
        Skip.IfNot(fx.Enabled);
        var (x, asset) = await WorkAsync();
        var tree = Rows(await Ok(await x.O.Admin.GetAsync($"/api/reports/failure-tree?{Period}")));
        Assert.Equal(540m, tree.First().GetProperty("cost").GetDecimal());
        var history = Assert.Single(Rows(await Ok(await x.O.Admin.GetAsync($"/api/reports/asset-work-history?assetId={asset}&{Period}"))));
        Assert.Equal(540m, history.GetProperty("cost").GetDecimal());
    }

    [SkippableFact]
    public async Task Organization_without_contractors_sees_zero_contractor()
    {
        Skip.IfNot(fx.Enabled);
        var (x, _) = await WorkAsync(withContractors: false);
        var row = Assert.Single(Rows(await Ok(await x.O.Admin.GetAsync($"/api/reports/maintenance-costs?{Period}"))));
        Assert.Equal(100m, row.GetProperty("labor").GetDecimal());
        Assert.Equal(0m, row.GetProperty("contractor").GetDecimal());
        Assert.Equal(100m, row.GetProperty("total").GetDecimal());
    }
}
