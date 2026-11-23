using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: budgets — one amount per cost centre per month.</summary>
public class BudgetTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static Task<HttpResponseMessage> PutBudgetAsync(HttpClient c, int year, Guid costCentre, decimal?[] months) =>
        c.PutAsJsonAsync("/api/budgets", new { year, rows = new[] { new { costCentreId = costCentre, months } } });

    private static async Task<JsonElement> RowAsync(HttpClient c, int year, Guid costCentre) =>
        (await Ok(await c.GetAsync($"/api/budgets?year={year}"))).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("costCentreId").GetGuid() == costCentre);

    [SkippableFact]
    public async Task Budget_grid_saves_months_per_cost_centre()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        await Ok(await PutBudgetAsync(o.Admin, 2027, prod, Enumerable.Repeat((decimal?)100m, 12).ToArray()));
        var row = await RowAsync(o.Admin, 2027, prod);
        Assert.Equal(1200m, row.GetProperty("total").GetDecimal());
        Assert.Equal(12, row.GetProperty("months").GetArrayLength());
        var months = Enumerable.Repeat((decimal?)100m, 12).ToArray();
        months[2] = null;
        await Ok(await PutBudgetAsync(o.Admin, 2027, prod, months));
        row = await RowAsync(o.Admin, 2027, prod);
        Assert.Equal(0m, row.GetProperty("months")[2].GetDecimal());
        Assert.Equal(1100m, row.GetProperty("total").GetDecimal());
    }

    [SkippableFact]
    public async Task Budgets_need_settings_rights_and_no_negatives()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var (reader, _, _) = await UserAsync(fx, o.Admin, "Reader", ["workorders.view", "reports.view"]);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/budgets?year=2027")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutBudgetAsync(reader, 2027, prod, new decimal?[12])).StatusCode);
        var months = new decimal?[12];
        months[0] = -5;
        var refused = await PutBudgetAsync(o.Admin, 2027, prod, months);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("A budget can't be below 0.", await refused.Content.ReadAsStringAsync());
    }
}
