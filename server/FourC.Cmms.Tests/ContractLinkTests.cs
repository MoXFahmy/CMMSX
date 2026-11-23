using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.ContractTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: contracts named on work orders and PMs, the "covered by" banner, an asset's contracts and the contract report.</summary>
public class ContractLinkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static object Wo(Guid site, Guid? asset, Guid? contract, string title = "Lift check") =>
        new { title, siteId = site, assetId = asset, type = "Corrective", priority = "Medium", contractId = contract, managesContract = true };

    [SkippableFact]
    public async Task Pm_passes_its_contract_to_the_work_orders_it_creates()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var c = w.O.Admin;
        var k = await ContractAsync(c, Body(w.C, Today.AddDays(-10), Today.AddDays(300), site: w.O.Site, assets: [w.A1]));
        var body = Pm12.Schedule(w.O.Site, await Pm12.TemplateAsync(c, "LIFTPM"), Today, asset: w.A1, extra: new() { ["contractId"] = k, ["managesContract"] = true });
        var pm = Id(await Pm12.SaveScheduleAsync(c, body));
        Assert.Equal(k, (await Ok(await c.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("contractId").GetGuid());
        await Pm12.GenerateAsync(c, Today, pm);
        var made = await Loop.DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(x => x.PmScheduleId == pm).Select(x => x.Id).FirstAsync());
        Assert.Equal(k, (await Ok(await c.GetAsync($"/api/work-orders/{made}"))).GetProperty("contract").GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task Contract_for_another_site_cannot_be_named_on_work()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var atB = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-10), Today.AddDays(300), site: w.SiteB));
        var ended = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-100), Today.AddDays(-1)));
        Assert.Contains("This contract is for another site.", await Problem(await w.O.Admin.PostAsJsonAsync("/api/work-orders", Wo(w.O.Site, null, atB))));
        Assert.Contains("This contract has ended.", await Problem(await w.O.Admin.PostAsJsonAsync("/api/work-orders", Wo(w.O.Site, null, ended))));
        // An order that already names a contract keeps it after the contract ends.
        var ok = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-10), Today.AddDays(300)));
        var wo = Id(await Ok(await w.O.Admin.PostAsJsonAsync("/api/work-orders", Wo(w.O.Site, null, ok))));
        await Loop.DbAsync(fx, db => db.Contracts.IgnoreQueryFilters().Where(x => x.Id == ok).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, Today.AddDays(-1))));
        var current = await Ok(await w.O.Admin.GetAsync($"/api/work-orders/{wo}"));
        var update = JsonSerializer.SerializeToNode(Wo(w.O.Site, null, ok, "Renamed"))!.AsObject();
        update["rowVersion"] = current.GetProperty("rowVersion").GetString();
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", update));
    }

    [SkippableFact]
    public async Task Options_list_active_and_upcoming_contracts_covering_the_asset_first()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var c = w.O.Admin;
        var k1 = await ContractAsync(c, Body(w.C, Today.AddDays(-10), Today.AddDays(300), title: "Org wide"));
        var k2 = await ContractAsync(c, Body(w.C, Today.AddDays(5), Today.AddDays(300), site: w.O.Site, assets: [w.A1], title: "Upcoming at A"));
        await ContractAsync(c, Body(w.C, Today.AddDays(-10), Today.AddDays(300), site: w.SiteB, title: "At B"));
        await ContractAsync(c, Body(w.C, Today.AddDays(-100), Today.AddDays(-1), title: "Expired"));
        var options = (await Ok(await c.GetAsync($"/api/contracts/options?siteId={w.O.Site}&assetId={w.A1}"))).EnumerateArray().ToList();
        Assert.Equal([k2, k1], options.Select(o => o.GetProperty("id").GetGuid()));
        Assert.True(options[0].GetProperty("covers").GetBoolean());
        Assert.False(options[1].GetProperty("covers").GetBoolean());
    }

    [SkippableFact]
    public async Task Covered_asset_shows_the_contract_on_work_orders_and_requests()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var c = w.O.Admin;
        var k = await ContractAsync(c, Body(w.C, Today.AddDays(-10), Today.AddDays(300), site: w.O.Site, assets: [w.A1]));
        var number = (await Ok(await c.GetAsync($"/api/contracts/{k}"))).GetProperty("number").GetString();
        var wo = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Lift noise", siteId = w.O.Site, assetId = w.A1, type = "Corrective", priority = "Medium" })));
        var covered = (await Ok(await c.GetAsync($"/api/work-orders/{wo}"))).GetProperty("coveredBy");
        Assert.Equal(number, covered.GetProperty("number").GetString());
        Assert.Equal("LIFT Co", covered.GetProperty("companyName").GetString());
        var request = Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", new { title = "Lift stuck", siteId = w.O.Site, assetId = w.A1, priority = "Medium", type = "Corrective" })));
        Assert.Equal(number, (await Ok(await c.GetAsync($"/api/work-requests/{request}"))).GetProperty("coveredBy").GetProperty("number").GetString());
        var other = Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = w.O.Site, status = "Active", criticality = "Medium", warrantyType = "None" })));
        var plain = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Pump", siteId = w.O.Site, assetId = other, type = "Corrective", priority = "Medium" })));
        Assert.Equal(JsonValueKind.Null, (await Ok(await c.GetAsync($"/api/work-orders/{plain}"))).GetProperty("coveredBy").ValueKind);
    }

    [SkippableFact]
    public async Task Asset_page_lists_contracts_covering_it()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var k = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-10), Today.AddDays(300), assets: [w.A1]));
        var list = (await Ok(await w.O.Admin.GetAsync($"/api/assets/{w.A1}/contracts"))).EnumerateArray().ToList();
        var row = Assert.Single(list);
        Assert.Equal(k, row.GetProperty("id").GetGuid());
        Assert.Equal(1000m, row.GetProperty("yearlyCost").GetDecimal());
        Assert.Equal("Active", row.GetProperty("status").GetString());
        Assert.Empty((await Ok(await w.O.Admin.GetAsync($"/api/assets/{w.B1}/contracts"))).EnumerateArray().Where(x => x.GetProperty("id").GetGuid() != k));
    }

    [SkippableFact]
    public async Task Contract_report_shows_value_spend_completed_and_expected()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var c = w.O.Admin;
        var elec = await CategoryAsync(c, "ELEC", 100);
        var year = Today.Year;
        var k = await ContractAsync(c, Body(w.C, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), value: 12000));
        var staff = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "S1", fullName = "Staff", isContractor = false, hourlyRate = 50m })));
        var wo = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", Wo(w.O.Site, null, k))));
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = staff, workDate = Today, hours = 2, overtimeHours = 0 }));
        await ContractorLineTests.AddLineAsync(c, wo, ContractorLineTests.Line(w.C, elec, done: 2, doneOn: Today));
        await Loop.DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(x => x.Id == wo)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WorkOrderStatus.Completed).SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow)));
        var report = await Ok(await c.GetAsync($"/api/reports/contracts?from={year}-01-01&to={year}-12-31"));
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(12000m, row.GetProperty("value").GetDecimal());
        Assert.Equal("EGP", row.GetProperty("currency").GetString());
        Assert.Equal(300m, row.GetProperty("spend").GetDecimal());
        Assert.Equal(1, row.GetProperty("completed").GetInt32());
        Assert.Equal(12.0m, row.GetProperty("expected").GetDecimal());
    }
}
