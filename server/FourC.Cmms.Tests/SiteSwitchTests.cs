using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>The site switch in the header and the per-site counters on the dashboard (0.9.0).</summary>
public class SiteSwitchTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static HttpRequestMessage Get(string path, Guid? site)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, path);
        if (site is { } s) m.Headers.Add("X-Cmx-Site", s.ToString());
        return m;
    }

    private static List<Guid> Ids(JsonElement e) =>
        (e.ValueKind == JsonValueKind.Array ? e : e.GetProperty("items")).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();

    [SkippableFact]
    public async Task The_site_header_narrows_to_one_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Switch Co");
        Assert.Equal([w.A.Asset], Ids(await Ok(await w.Admin.SendAsync(Get("/api/assets", w.A.Site)))));
        var access = (await Ok(await w.Admin.SendAsync(Get("/api/auth/me", w.A.Site)))).GetProperty("access");
        Assert.Equal(w.A.Site, access.GetProperty("siteId").GetGuid());
        Assert.Equal(2, access.GetProperty("sites").GetArrayLength());
        // Without the header: everything again.
        Assert.Equal(2, Ids(await Ok(await w.Admin.GetAsync("/api/assets"))).Count);
    }

    [SkippableFact]
    public async Task A_site_header_outside_the_scope_is_ignored()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Outside Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal([w.A.Asset], Ids(await Ok(await c.SendAsync(Get("/api/assets", w.B.Site)))));
        var access = (await Ok(await c.SendAsync(Get("/api/auth/me", w.B.Site)))).GetProperty("access");
        Assert.Equal(JsonValueKind.Null, access.GetProperty("siteId").ValueKind);
    }

    [SkippableFact]
    public async Task A_list_filtered_to_a_hidden_site_is_empty()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Hidden Filter Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal(0, (await Ok(await c.GetAsync($"/api/work-orders?siteId={w.B.Site}"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Ok(await c.GetAsync($"/api/assets?siteId={w.B.Site}"))).GetProperty("total").GetInt32());
    }

    [SkippableFact]
    public async Task Dashboard_counts_work_per_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Group Co");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Late B", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "High", dueDate = yesterday }));

        var rows = (await Ok(await w.Admin.GetAsync("/api/dashboard"))).GetProperty("bySiteWork").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var a = rows.Single(r => r.GetProperty("siteId").GetGuid() == w.A.Site);
        var b = rows.Single(r => r.GetProperty("siteId").GetGuid() == w.B.Site);
        Assert.Equal("Site A", a.GetProperty("name").GetString());
        Assert.Equal(1, a.GetProperty("open").GetInt32());
        Assert.Equal(2, b.GetProperty("open").GetInt32());
        Assert.Equal(0, a.GetProperty("late").GetInt32());
        Assert.Equal(1, b.GetProperty("late").GetInt32());
        Assert.Equal(0, b.GetProperty("completedLast30").GetInt32());
        Assert.Equal(1, a.GetProperty("requestsWaiting").GetInt32());

        // One site in view: no per-site rows.
        Assert.Equal(0, (await Ok(await w.Admin.SendAsync(Get("/api/dashboard", w.A.Site)))).GetProperty("bySiteWork").GetArrayLength());
    }
}
