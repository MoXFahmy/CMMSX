using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.20.0: the cost-centre tree — parents without cycles, in use, and site-limited people.</summary>
public class CostCentreTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<Guid> CostCentreAsync(HttpClient c, string code, string name, Guid? parent = null, Guid? site = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/cost-centres", new { code, name, parentId = parent, siteId = site, isActive = true })));

    private static async Task<List<JsonElement>> ListAsync(HttpClient c) => (await Ok(await c.GetAsync("/api/cost-centres"))).EnumerateArray().ToList();

    private static async Task<string> Refused(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [SkippableFact]
    public async Task Tree_has_parents_and_no_cycles()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        var line = await CostCentreAsync(o.Admin, "LINE1", "Line 1", prod);
        var pack = await CostCentreAsync(o.Admin, "PACK", "Packing", line);
        Assert.Equal("Production › Line 1 › Packing", (await ListAsync(o.Admin)).Single(x => Id(x) == pack).GetProperty("path").GetString());
        Assert.Contains("A cost centre can't be its own parent or under one of its own.",
            await Refused(await o.Admin.PutAsJsonAsync($"/api/cost-centres/{prod}", new { code = "PROD", name = "Production", parentId = pack, isActive = true })));
    }

    [SkippableFact]
    public async Task Cost_centre_in_use_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var prod = await CostCentreAsync(o.Admin, "PROD", "Production");
        await CostCentreAsync(o.Admin, "LINE1", "Line 1", prod);
        Assert.Contains("This cost centre is in use. Make it inactive instead.", await Refused(await o.Admin.DeleteAsync($"/api/cost-centres/{prod}")));
        var spare = await CostCentreAsync(o.Admin, "SPARE", "Spare");
        Assert.True((await o.Admin.DeleteAsync($"/api/cost-centres/{spare}")).IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Site_limited_people_see_their_sites_and_shared_cost_centres()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        var shared = await CostCentreAsync(o.Admin, "SHARED", "Shared");
        var a = await CostCentreAsync(o.Admin, "A", "Site A works", site: o.Site);
        var b = await CostCentreAsync(o.Admin, "B", "Site B works", site: siteB);
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Lead A", ["workorders.view", "settings.manage"], siteIds: [o.Site]);
        Assert.Equal(new[] { shared, a }.Order(), (await ListAsync(limited)).Select(Id).Order());
        Assert.Equal(HttpStatusCode.NotFound, (await limited.PutAsJsonAsync($"/api/cost-centres/{b}", new { code = "B", name = "Changed", siteId = siteB, isActive = true })).StatusCode);
    }
}
