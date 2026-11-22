using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>An asset's costs by year with downtime cost (0.14.0), and the category's downtime cost per hour.</summary>
public class AssetCostTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly int LastYear = DateTime.UtcNow.Year - 1;

    private Task AddCostAsync(Loop.Org org, Guid asset, decimal amount, DateTimeOffset when) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder { TenantId = org.Tenant, Number = $"C-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = org.Site, AssetId = asset };
        var c = new WorkOrderCost { TenantId = org.Tenant, WorkOrderId = w.Id, Description = "Contractor", Quantity = 1, UnitCost = amount };
        db.WorkOrders.Add(w); db.WorkOrderCosts.Add(c);
        await db.SaveChangesAsync();
        return await db.WorkOrderCosts.IgnoreQueryFilters().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, when)); // as of that day
    });

    private static async Task<Dictionary<int, JsonElement>> YearsAsync(HttpClient c, Guid asset, bool components = false) =>
        (await Ok(await c.GetAsync($"/api/assets/{asset}/costs{(components ? "?components=true" : "")}"))).GetProperty("years").EnumerateArray()
            .ToDictionary(y => y.GetProperty("year").GetInt32());

    private static decimal? Dec(JsonElement e, string key) => e.GetProperty(key).ValueKind == JsonValueKind.Null ? null : e.GetProperty(key).GetDecimal();

    private async Task<(Loop.Org Org, Guid Asset, Guid Category)> PumpAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var category = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "CPMP", name = "Pump kit", downtimeCostPerHour = 20m })));
        var asset = await AssetAsync(org.Admin, org.Site, "Pump", category: category);
        var me = await EmployeeAsync(org.Admin, "ANN");
        await AddCostAsync(org, asset, 100, new DateTimeOffset(LastYear, 4, 1, 9, 0, 0, TimeSpan.Zero));
        await DbAsync(fx, async db =>
        {
            var w = new WorkOrder { TenantId = org.Tenant, Number = $"C-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = org.Site, AssetId = asset };
            db.WorkOrders.Add(w);
            db.WorkOrderLabor.Add(new WorkOrderLabor { TenantId = org.Tenant, WorkOrderId = w.Id, EmployeeId = me.Id, WorkDate = new DateOnly(DateTime.UtcNow.Year, 1, 2), Hours = 2, Rate = 50 });
            db.DowntimeEvents.Add(new DowntimeEvent { TenantId = org.Tenant, AssetId = asset, StartedAt = new DateTimeOffset(LastYear, 5, 10, 8, 0, 0, TimeSpan.Zero),
                EndedAt = new DateTimeOffset(LastYear, 5, 10, 18, 0, 0, TimeSpan.Zero), Reason = "Seal", Source = DowntimeSource.Manual });
            return await db.SaveChangesAsync();
        });
        return (org, asset, category);
    }

    [SkippableFact]
    public async Task Costs_per_year_add_labour_parts_other_and_downtime()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, _) = await PumpAsync();
        var years = await YearsAsync(org.Admin, asset);
        Assert.Equal(6, years.Count); // the last five years and this one
        var last = years[LastYear];
        Assert.Equal((100m, 10m, 200m, 300m), (Dec(last, "other")!.Value, Dec(last, "downtimeHours")!.Value, Dec(last, "downtimeCost")!.Value, Dec(last, "total")!.Value));
        Assert.Equal(100m, Dec(years[DateTime.UtcNow.Year], "labor"));
        await DbAsync(fx, async db => { (await db.Assets.IgnoreQueryFilters().SingleAsync(a => a.Id == asset)).DowntimeCostPerHour = 50; return await db.SaveChangesAsync(); });
        Assert.Equal(500m, Dec((await YearsAsync(org.Admin, asset))[LastYear], "downtimeCost"));
    }

    [SkippableFact]
    public async Task Components_are_included_when_asked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var pump = await AssetAsync(org.Admin, org.Site, "Pump");
        async Task<Guid> Child(Guid parent, string name) => Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets",
            new { name, siteId = org.Site, parentAssetId = parent, status = "Active", criticality = "High", warrantyType = "None" })));
        var motor = await Child(pump, "Motor");
        var bearing = await Child(motor, "Bearing");
        var when = new DateTimeOffset(LastYear, 3, 1, 9, 0, 0, TimeSpan.Zero);
        await AddCostAsync(org, pump, 10, when);
        await AddCostAsync(org, motor, 20, when);
        await AddCostAsync(org, bearing, 30, when);
        Assert.Equal(10m, Dec((await YearsAsync(org.Admin, pump))[LastYear], "other"));
        Assert.Equal(60m, Dec((await YearsAsync(org.Admin, pump, components: true))[LastYear], "other"));
    }

    [SkippableFact]
    public async Task Costs_of_an_asset_on_another_site_are_not_found()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var hidden = await AssetAsync(org.Admin, other, "Hidden pump");
        var user = await UserAsync(fx, org.Admin, "Maintenance Manager", [org.Site]);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/assets/{hidden}/costs")).StatusCode);
    }

    [SkippableFact]
    public async Task Reliability_uses_the_category_cost()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, asset, _) = await PumpAsync();
        var rows = (await Ok(await org.Admin.GetAsync($"/api/reports/reliability?from={LastYear}-01-01&to={LastYear}-12-31"))).GetProperty("rows").EnumerateArray();
        Assert.Equal(200m, rows.Single(r => r.GetProperty("assetId").GetGuid() == asset).GetProperty("downtimeCost").GetDecimal());
    }
}
