using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.CostCentreTests;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: the Cost centres and Budgets sheets, and cost-centre codes on the Locations, Assets and PM schedules sheets.</summary>
public class CostCentreImportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid? CostCentreOf(JsonElement e) => e.GetProperty("costCentreId").ValueKind == JsonValueKind.Null ? null : e.GetProperty("costCentreId").GetGuid();

    [SkippableFact]
    public async Task Cost_centre_and_budget_sheets_import_and_export()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await RunAsync(o.Admin, Book(
            new Sheet("Cost centres", ["Code", "Name", "Parent code", "Site code", "Active"], ["LINE1", "Line 1", "PROD", null, "Yes"], ["PROD", "Production", null, null, "Yes"]),
            new Sheet("Budgets", ["Cost centre code", "Year", "Month", "Amount"], ["LINE1", 2027m, 1m, 500m])));
        var list = (await Ok(await o.Admin.GetAsync("/api/cost-centres"))).EnumerateArray().ToList();
        var line = list.Single(x => x.GetProperty("code").GetString() == "LINE1");
        Assert.Equal("Production › Line 1", line.GetProperty("path").GetString());
        var budget = (await Ok(await o.Admin.GetAsync("/api/budgets?year=2027"))).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("costCentreId").GetGuid() == Id(line));
        Assert.Equal(500m, budget.GetProperty("months")[0].GetDecimal());

        var current = await (await o.Admin.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var check = await CheckAsync(o.Admin, current);
        foreach (var key in new[] { "CostCentres", "Budgets" })
        {
            var sheet = SheetOf(check, key);
            Assert.Equal((0, 0, 0), (Count(sheet, "new"), Count(sheet, "changed"), Count(sheet, "problems")));
        }
    }

    [SkippableFact]
    public async Task Location_asset_and_pm_sheets_set_cost_centres()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var siteCode = (await Ok(await o.Admin.GetAsync($"/api/sites/{o.Site}"))).GetProperty("code").GetString();
        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var pump = await Pm12.AssetAsync(o.Admin, o.Site, "Pump");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, DateOnly.FromDateTime(DateTime.UtcNow), pump, extra: new() { ["code"] = "PM1" })));
        await RunAsync(o.Admin, Book(
            new Sheet("Locations", ["Site code", "Code", "Name", "Cost centre code"], [siteCode, "HALL", "Hall", "PROD"]),
            new Sheet("Assets", ["Asset number", "Name", "Site code", "Cost centre code"], ["PRESS-1", "Press", siteCode, "PROD"]),
            new Sheet("PM schedules", ["Code", "Cost centre code"], ["PM1", "PROD"])));
        var location = (await Ok(await o.Admin.GetAsync("/api/locations?search=HALL"))).GetProperty("items")[0];
        Assert.Equal(prod, CostCentreOf(await Ok(await o.Admin.GetAsync($"/api/locations/{Id(location)}"))));
        var asset = (await Ok(await o.Admin.GetAsync("/api/assets?search=PRESS-1"))).GetProperty("items")[0];
        Assert.Equal(prod, CostCentreOf(await Ok(await o.Admin.GetAsync($"/api/assets/{Id(asset)}"))));
        Assert.Equal(prod, CostCentreOf(await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"))));

        var bad = SheetOf(await CheckAsync(o.Admin, Book(new Sheet("Assets", ["Asset number", "Name", "Site code", "Cost centre code"], ["PRESS-2", "Press", siteCode, "NOPE"]))), "Assets");
        Assert.Contains("Cost centre NOPE not found.", ProblemsOf(bad));
    }
}
