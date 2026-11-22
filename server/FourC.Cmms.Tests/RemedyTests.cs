using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>Cause and remedy on completion (0.14.0): the web, the breakdown rule, inactive codes, and a work group's report.</summary>
public class RemedyTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> CodeAsync(HttpClient c, string path, string code) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name = $"{code} name", isActive = true })));

    private static async Task Deactivate(HttpClient c, string path, Guid id, string code) =>
        await Ok(await c.PutAsJsonAsync($"{path}/{id}", new { code, name = $"{code} name", isActive = false }));

    private static async Task<Guid> WorkAsync(Loop.Org org, string type = "Corrective") =>
        Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, priority = "High", type })));

    private static Task<HttpResponseMessage> CompleteAsync(Loop.Org org, Guid wo, Guid? failureCodeId, Guid? remedyId) =>
        org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done", failureCodeId, remedyId });

    private static Guid? IdOf(JsonElement wo, string field) => wo.GetProperty(field) is { ValueKind: JsonValueKind.Object } x ? x.GetProperty("id").GetGuid() : null;

    [SkippableFact]
    public async Task Completing_records_cause_and_remedy()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (cause, remedy) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXW1"), await CodeAsync(org.Admin, "/api/remedies", "SEAL"));
        var wo = await WorkAsync(org);
        await Ok(await CompleteAsync(org, wo, cause, remedy));
        var detail = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal((cause, remedy), (IdOf(detail, "failureCode"), IdOf(detail, "remedy")));
    }

    [SkippableFact]
    public async Task Breakdown_rule_asks_for_a_cause()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await TestSettings.SetSettingsAsync(org.Admin, x => x["requireFailureCodeForBreakdowns"] = true);
        var cause = await CodeAsync(org.Admin, "/api/failure-codes", "CXW1");
        var wo = await WorkAsync(org, "Breakdown");
        Assert.Contains("Choose a cause for breakdown work.", await Bad(await CompleteAsync(org, wo, null, null)));
        await Ok(await CompleteAsync(org, wo, cause, null)); // a remedy is never required
    }

    [SkippableFact]
    public async Task Inactive_cause_and_remedy_are_kept_on_completion()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (cause, remedy) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXW1"), await CodeAsync(org.Admin, "/api/remedies", "SEAL"));
        var kept = await WorkAsync(org);
        await DbAsync(fx, async db =>
        {
            var w = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == kept);
            w.FailureCodeId = cause; w.RemedyId = remedy;
            return await db.SaveChangesAsync();
        });
        await Deactivate(org.Admin, "/api/failure-codes", cause, "CXW1");
        await Deactivate(org.Admin, "/api/remedies", remedy, "SEAL");
        await Ok(await CompleteAsync(org, kept, cause, remedy));
        var other = await WorkAsync(org);
        Assert.Contains("Unknown cause.", await Bad(await CompleteAsync(org, other, cause, null)));
        Assert.Contains("Unknown remedy.", await Bad(await CompleteAsync(org, other, null, remedy)));
    }

    [SkippableFact]
    public async Task Group_report_sets_remedy_where_empty()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (first, second) = (await CodeAsync(org.Admin, "/api/remedies", "SEAL"), await CodeAsync(org.Admin, "/api/remedies", "BELT"));
        var list = new List<Guid> { await AssetAsync(org.Admin, org.Site, "Fan 1"), await AssetAsync(org.Admin, org.Site, "Fan 2") };
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "GRP"), Today, assets: [.. list], extra: new() { ["multiAssetMode"] = "Group" })));
        var group = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        var children = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).OrderBy(w => w.Number).Select(w => w.Id).ToListAsync());
        await DbAsync(fx, async db => { (await db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == children[0])).RemedyId = first; return await db.SaveChangesAsync(); });
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-groups/{group}/report", new { completionNotes = "Done", remedyId = second }));
        var remedies = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).OrderBy(w => w.Number).Select(w => w.RemedyId).ToListAsync());
        Assert.Equal(new Guid?[] { first, second }, remedies);
    }
}
