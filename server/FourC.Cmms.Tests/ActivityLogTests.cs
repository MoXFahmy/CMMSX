using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Settings › Activity log (0.25.0): filters, one change's entries, masking, site limits, export and "last changed".</summary>
public class ActivityLogTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<List<JsonElement>> RowsAsync(HttpClient c, string query = "") =>
        (await Ok(await c.GetAsync($"/api/activity?pageSize=200&{query}"))).GetProperty("items").EnumerateArray().ToList();

    [SkippableFact]
    public async Task Filters_and_same_change()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Chiller", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" }));
        var number = (await Ok(await org.Admin.GetAsync($"/api/assets/{Id(asset)}"))).GetProperty("assetNumber").GetString()!;
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, type = "Preventive", priority = "Low", tasks = new[] { "Check oil", "Check belts" },
        })));
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, min: 10));

        var web = await RowsAsync(org.Admin, "source=Web");
        Assert.True(web.Count >= 4, $"{web.Count} rows");
        Assert.All(web, r => Assert.Equal("Web", r.GetProperty("source").GetString()));
        var woRow = (await RowsAsync(org.Admin, "entityType=WorkOrder")).Single(r => r.GetProperty("entityId").GetGuid() == wo && r.GetProperty("action").GetString() == "Created");
        var same = await RowsAsync(org.Admin, $"changeId={woRow.GetProperty("changeId").GetGuid()}");
        Assert.True(same.Count >= 3, $"{same.Count} rows in one change");
        Assert.Contains(same, r => r.GetProperty("entityType").GetString() == "WorkOrderTask" && r.GetProperty("parentId").GetGuid() == wo);
        var found = Assert.Single(await RowsAsync(org.Admin, $"entityType=Asset&search={number}"));
        Assert.Contains(number, found.GetProperty("recordLabel").GetString());
        Assert.Contains("Chiller", found.GetProperty("recordLabel").GetString());
    }

    [SkippableFact]
    public async Task Masked_values_never_show()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var tech = await UserAsync(fx, org.Admin, "Technician");
        await Ok(await org.Admin.PostAsJsonAsync($"/api/users/{tech.Id}/password", new { password = "Changed0ne!x" }));
        var rows = await RowsAsync(org.Admin, "entityType=AppUser");
        var changed = rows.First(r => r.GetProperty("entityId").GetGuid() == tech.Id && (r.GetProperty("changes").GetString() ?? "").Contains("PasswordHash"));
        using var changes = JsonDocument.Parse(changed.GetProperty("changes").GetString()!);
        var hash = changes.RootElement.GetProperty("PasswordHash");
        Assert.Equal(("••••", "••••"), (hash.GetProperty("from").GetString(), hash.GetProperty("to").GetString()));
        Assert.All(rows, r => Assert.DoesNotContain("AQAAAA", r.GetProperty("changes").GetString() ?? ""));
    }

    [SkippableFact]
    public async Task Site_limited_admins_see_their_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var siteB = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var mine = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Here", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var theirs = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "There", siteId = siteB, status = "Active", criticality = "High", warrantyType = "None" })));
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, min: 10));
        var admin = await UserAsync(fx, org.Admin, "Administrator", [org.Site]);
        var rows = await RowsAsync(admin.Client);
        Assert.Contains(rows, r => r.GetProperty("entityId").GetGuid() == mine);
        Assert.DoesNotContain(rows, r => r.GetProperty("entityId").GetGuid() == theirs);
        Assert.Contains(rows, r => r.GetProperty("entityType").GetString() == "Tenant");
    }

    [SkippableFact]
    public async Task Export_works()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var r = await org.Admin.GetAsync("/api/activity/export?format=csv");
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}");
        var text = await r.Content.ReadAsStringAsync();
        Assert.Contains("timestamp", text.Split('\n')[0], StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Last_changed_names_who_and_when()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, min: 12));
        var last = Assert.Single((await Ok(await org.Admin.GetAsync("/api/activity/last-changed?entityType=Tenant&keys=security.passwordMinLength&keys=security.passwordHistory"))).EnumerateArray());
        Assert.Equal(("security.passwordMinLength", "Admin"), (last.GetProperty("key").GetString(), last.GetProperty("userName").GetString()));
    }
}
