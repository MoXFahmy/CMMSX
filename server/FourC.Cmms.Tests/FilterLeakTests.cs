using System.Net.Http.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.26.1 final review: lists whose own query lost its organization or site limits through IgnoreQueryFilters/AllSites in a subquery.</summary>
public class FilterLeakTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Meters_stay_inside_the_organization()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var a = await OrgAsync(fx);
        var b = await OrgAsync(fx);
        var press = Id(await Ok(await b.Admin.PostAsJsonAsync("/api/assets", new { name = "Press", siteId = b.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var theirs = Id(await Ok(await b.Admin.PostAsJsonAsync("/api/meters", new { code = "RH", name = "Run hours", assetId = press, unit = "h", isActive = true })));
        var listed = (await Ok(await a.Admin.GetAsync("/api/meters"))).EnumerateArray().Select(Id).ToList();
        Assert.DoesNotContain(theirs, listed);
    }

    [SkippableFact]
    public async Task Lines_to_order_follow_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var request = await RequestAsync(tech, o, PartLine(o, 2));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{request}/submit", null));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{request}/approve", null));
        Assert.NotEmpty((await Ok(await o.Admin.GetAsync("/api/purchase-requests/lines-to-order"))).EnumerateArray());
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var (buyer, _, _) = await UserAsync(fx, o.Admin, "Buyer", ["purchasing.view", "purchasing.create"], [other]);
        Assert.Empty((await Ok(await buyer.GetAsync("/api/purchase-requests/lines-to-order"))).EnumerateArray());
    }
}
