using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: optional numbers that restart each year, with the year in them.</summary>
public class YearlyNumberingTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public void Yearly_numbers_restart_in_the_organizations_year()
    {
        var org = new TenantSettings { WorkOrderNumberPrefix = "WO-", TimeZone = "Africa/Cairo", RestartNumbersYearly = true };
        var lateEvening = new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero); // already 1 January 2027 in Cairo
        Assert.Equal(("WorkOrder:2027", "WO-2027-"), Numbering.For(org, null, NumberKind.WorkOrder, lateEvening));
        var site = new Site { Code = "CAI", Name = "Cairo" };
        site.Settings.WorkOrderNumberPrefix = "CAI-";
        Assert.Equal(($"WorkOrder:{site.Id:N}:2027", "CAI-2027-"), Numbering.For(org, site, NumberKind.WorkOrder, lateEvening));
        Assert.Equal(("Asset", "AST-"), Numbering.For(org, null, NumberKind.Asset, lateEvening));
        Assert.Equal(("WorkGroup:2027", "GRP-2027-"), Numbering.For(org, null, NumberKind.WorkGroup, lateEvening));
        org.RestartNumbersYearly = false;
        Assert.Equal(("WorkOrder", "WO-"), Numbering.For(org, null, NumberKind.WorkOrder, lateEvening));
    }

    [SkippableFact]
    public async Task New_work_orders_get_yearly_numbers()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await TestSettings.SetSettingsAsync(o.Admin, s => s["restartNumbersYearly"] = true);
        string NumberOf(System.Text.Json.JsonElement r) => r.GetProperty("number").GetString()!;
        var first = NumberOf(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "One", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        var second = NumberOf(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Two", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        Assert.Matches(@"^WO-\d{4}-00001$", first);
        Assert.Matches(@"^WO-\d{4}-00002$", second);
        var request = NumberOf(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = o.Site, priority = "Medium", type = "Corrective" })));
        Assert.Matches(@"^WR-\d{4}-00001$", request);
    }
}
