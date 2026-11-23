using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.ContractorLineTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: contractor visits on the planning board — their day, moving them, and who may change what.</summary>
public class ContractorVisitTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<JsonElement> BoardAsync(HttpClient c, DateOnly from, DateOnly to) =>
        await Ok(await c.GetAsync($"/api/planning/contractors?from={D(from)}&to={D(to)}"));

    private static JsonElement LineOf(JsonElement wo, Guid id) => wo.GetProperty("contractors").EnumerateArray().Single(l => Id(l) == id);

    private static async Task Plan(HttpClient c, Guid wo, DateOnly day)
    {
        var w = await Ok(await c.GetAsync($"/api/work-orders/{wo}"));
        await Ok(await c.PutAsJsonAsync($"/api/work-orders/{wo}", new
        {
            title = w.GetProperty("title").GetString(), siteId = w.GetProperty("siteId").GetGuid(), type = "Corrective", priority = "Medium",
            plannedStart = D(day), rowVersion = w.GetProperty("rowVersion").GetString(),
        }));
    }

    [SkippableFact]
    public async Task Visit_day_is_its_date_else_the_work_orders()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        await Plan(x.O.Admin, x.Wo, Today.AddDays(2));
        var line = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 4));
        var b = await BoardAsync(x.O.Admin, Today, Today.AddDays(6));
        var visit = b.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("lineId").GetGuid() == line);
        Assert.Equal(D(Today.AddDays(2)), visit.GetProperty("day").GetString());
        Assert.Equal("LIFT Co", b.GetProperty("companies").EnumerateArray().Single().GetProperty("name").GetString());

        var dated = await AddLineAsync(x.O.Admin, x.Wo, new { supplierId = x.C, categoryId = x.Elec, plannedHours = 2m, hoursDone = 0m, plannedOn = D(Today.AddDays(4)) });
        b = await BoardAsync(x.O.Admin, Today, Today.AddDays(6));
        Assert.Equal(D(Today.AddDays(4)), b.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("lineId").GetGuid() == dated).GetProperty("day").GetString());

        var undatedWo = Id(await Ok(await x.O.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Some day", siteId = x.O.Site, type = "Corrective", priority = "Medium" })));
        var undated = await AddLineAsync(x.O.Admin, undatedWo, Line(x.C, x.Elec, planned: 1));
        b = await BoardAsync(x.O.Admin, Today, Today.AddDays(6));
        Assert.Contains(b.GetProperty("undated").EnumerateArray(), v => v.GetProperty("lineId").GetGuid() == undated);
    }

    [SkippableFact]
    public async Task Dragging_a_visit_changes_only_its_date()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var line = await AddLineAsync(x.O.Admin, x.Wo, new { supplierId = x.C, categoryId = x.Elec, plannedHours = 4m, hoursDone = 0m, notes = "Bring the hoist" });
        await Ok(await x.O.Admin.PostAsJsonAsync("/api/planning/visit", new { lineId = line, day = D(Today.AddDays(5)) }));
        var l = LineOf(await Ok(await x.O.Admin.GetAsync($"/api/work-orders/{x.Wo}")), line);
        Assert.Equal(D(Today.AddDays(5)), l.GetProperty("plannedOn").GetString());
        Assert.Equal((150m, 4m, 0m, "Bring the hoist"),
            (l.GetProperty("rate").GetDecimal(), l.GetProperty("plannedHours").GetDecimal(), l.GetProperty("hoursDone").GetDecimal(), l.GetProperty("notes").GetString()));
    }

    [SkippableFact]
    public async Task Planned_on_is_a_planner_field()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var line = await AddLineAsync(x.O.Admin, x.Wo, new { supplierId = x.C, categoryId = x.Elec, plannedHours = 4m, hoursDone = 0m, plannedOn = D(Today.AddDays(3)) });
        var (tech, _, _) = await UserAsync(fx, x.O.Admin, "Doer", [Core.Identity.Permissions.WorkOrdersView, Core.Identity.Permissions.WorkOrdersExecute]);
        var r = await tech.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{line}",
            new { supplierId = x.C, categoryId = x.Elec, plannedHours = 4m, hoursDone = 0m, plannedOn = D(Today.AddDays(6)) });
        Assert.Contains("You can record the hours done, the date and the person; ask a planner to change the rest.", await CompanyTests.Problem(r));
        await Ok(await tech.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{line}", new { supplierId = x.C, categoryId = x.Elec, plannedHours = 4m, hoursDone = 3m, doneOn = D(Today) }));
        var l = LineOf(await Ok(await x.O.Admin.GetAsync($"/api/work-orders/{x.Wo}")), line);
        Assert.Equal(3m, l.GetProperty("hoursDone").GetDecimal());
        Assert.Equal(D(Today.AddDays(3)), l.GetProperty("plannedOn").GetString()); // recording a visit keeps its date
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/planning/visit", new { lineId = line, day = D(Today) })).StatusCode);
    }
}
