using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Symptoms and remedies (0.14.0) are lists like causes (failure codes); categories carry a downtime cost per hour.</summary>
public class CodeListTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> AddAsync(HttpClient c, string path, string code, string name) => Id(await Ok(await c.PostAsJsonAsync(path, new { code, name, isActive = true })));

    [SkippableTheory]
    [InlineData("/api/symptoms")]
    [InlineData("/api/remedies")]
    public async Task Symptoms_and_remedies_are_lists_like_causes(string path)
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var b = await AddAsync(org.Admin, path, "B", "Beta");
        await AddAsync(org.Admin, path, "A", "Alpha");
        Assert.Equal(["Alpha", "Beta"], (await Ok(await org.Admin.GetAsync(path))).EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        await Ok(await org.Admin.PutAsJsonAsync($"{path}/{b}", new { code = "B", name = "Bravo", isActive = false }));
        var bravo = (await Ok(await org.Admin.GetAsync(path))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == b);
        Assert.Equal(("Bravo", false), (bravo.GetProperty("name").GetString(), bravo.GetProperty("isActive").GetBoolean()));
        Assert.Contains("This code is already used.", await Bad(await org.Admin.PostAsJsonAsync(path, new { code = "A", name = "Again", isActive = true })));
    }

    [SkippableFact]
    public async Task A_code_in_use_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var symptom = await AddAsync(org.Admin, "/api/symptoms", "LEAK", "Leaking");
        var remedy = await AddAsync(org.Admin, "/api/remedies", "SEAL", "Seal replaced");
        var unused = await AddAsync(org.Admin, "/api/remedies", "NONE", "Nothing");
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = org.Site, priority = "High", type = "Corrective" })));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, priority = "High", type = "Corrective" })));
        await DbAsync(fx, async db =>
        {
            (await db.WorkRequests.IgnoreQueryFilters().SingleAsync(x => x.Id == request)).SymptomId = symptom;
            (await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == wo)).RemedyId = remedy;
            return await db.SaveChangesAsync();
        });
        Assert.Contains("This item is in use. Mark it inactive instead.", await Bad(await org.Admin.DeleteAsync($"/api/symptoms/{symptom}")));
        Assert.Contains("This item is in use. Mark it inactive instead.", await Bad(await org.Admin.DeleteAsync($"/api/remedies/{remedy}")));
        Assert.Equal(HttpStatusCode.NoContent, (await org.Admin.DeleteAsync($"/api/remedies/{unused}")).StatusCode);
    }

    [SkippableFact]
    public async Task Lists_need_settings_manage()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var tech = await UserAsync(fx, org.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.PostAsJsonAsync("/api/symptoms", new { code = "X", name = "X", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tech.Client.GetAsync("/api/remedies")).StatusCode);
    }

    [SkippableFact]
    public async Task Category_downtime_cost_is_saved_and_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "PUMP", name = "Pumps", downtimeCostPerHour = 20m })));
        var cat = (await Ok(await org.Admin.GetAsync("/api/asset-categories"))).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(20m, cat.GetProperty("downtimeCostPerHour").GetDecimal());
        Assert.Contains("Downtime cost can't be negative.", await Bad(await org.Admin.PutAsJsonAsync($"/api/asset-categories/{id}", new { code = "PUMP", name = "Pumps", downtimeCostPerHour = -1m })));
    }
}
