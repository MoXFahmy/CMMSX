using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Smart ordering (0.14.0): causes most often recorded with a symptom, remedies most often recorded with a cause.</summary>
public class CodePairTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> CodeAsync(HttpClient c, string path, string code) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name = code, isActive = true })));

    /// <summary>Work orders written straight to the database: completed at <paramref name="completed"/> (null = still open) with these codes.</summary>
    private Task AddWorkAsync(Loop.Org org, Guid site, DateTimeOffset? completed, Guid? symptom, Guid? cause, Guid? remedy, int count = 1) => DbAsync(fx, db =>
    {
        for (var n = 0; n < count; n++)
            db.WorkOrders.Add(new WorkOrder
            {
                TenantId = org.Tenant, Number = $"T-{Guid.NewGuid():N}"[..20], Title = "Coded work", SiteId = site,
                Status = completed is null ? WorkOrderStatus.Open : WorkOrderStatus.Completed, CompletedAt = completed, SymptomId = symptom, FailureCodeId = cause, RemedyId = remedy,
            });
        return db.SaveChangesAsync();
    });

    private static List<(Guid, Guid, int)> Pairs(JsonElement list) =>
        list.EnumerateArray().Select(p => (p.GetProperty("first").GetGuid(), p.GetProperty("second").GetGuid(), p.GetProperty("count").GetInt32())).ToList();

    [SkippableFact]
    public async Task Pairs_count_completed_work_of_two_years_per_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        var (c1, c2) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXA"), await CodeAsync(org.Admin, "/api/failure-codes", "CXB"));
        var r1 = await CodeAsync(org.Admin, "/api/remedies", "R1");
        var recent = DateTimeOffset.UtcNow.AddDays(-10);
        await AddWorkAsync(org, org.Site, recent, s1, c1, r1, count: 2);
        await AddWorkAsync(org, org.Site, recent, s1, c2, null);
        await AddWorkAsync(org, org.Site, DateTimeOffset.UtcNow.AddYears(-2).AddDays(-5), s1, c2, null, count: 3); // older than two years
        await AddWorkAsync(org, other, recent, s1, c2, null, count: 3); // a site the user can't see
        await AddWorkAsync(org, org.Site, null, s1, c2, null, count: 3); // not completed
        var user = await UserAsync(fx, org.Admin, "Maintenance Manager", [org.Site]);
        var pairs = await Ok(await user.Client.GetAsync("/api/code-pairs"));
        Assert.Equal([(s1, c1, 2), (s1, c2, 1)], Pairs(pairs.GetProperty("symptomCause")));
        Assert.Equal([(c1, r1, 2)], Pairs(pairs.GetProperty("causeRemedy")));
    }

    [SkippableFact]
    public async Task Pairs_keep_twenty_per_code()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        var recent = DateTimeOffset.UtcNow.AddDays(-3);
        var causes = new List<Guid>();
        for (var n = 0; n < 22; n++) causes.Add(await CodeAsync(org.Admin, "/api/failure-codes", $"CX{n:00}"));
        foreach (var c in causes) await AddWorkAsync(org, org.Site, recent, s1, c, null);
        await AddWorkAsync(org, org.Site, recent, s1, causes[21], null, count: 2);
        var list = Pairs((await Ok(await org.Admin.GetAsync("/api/code-pairs"))).GetProperty("symptomCause"));
        Assert.Equal(20, list.Count);
        Assert.Equal((s1, causes[21], 3), list[0]);
    }
}
