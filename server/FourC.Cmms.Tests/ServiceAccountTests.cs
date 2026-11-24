using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Service accounts (0.26.0): non-person accounts with roles and sites that API keys act as.</summary>
public class ServiceAccountTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<Guid> RoleAsync(HttpClient c, string name) =>
        Id((await Ok(await c.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == name));

    internal static async Task<Guid> ServiceAccountAsync(HttpClient admin, string name, string role, Guid[]? siteIds = null) =>
        Id(await Ok(await admin.PostAsJsonAsync("/api/service-accounts", new { fullName = name, roleIds = new[] { await RoleAsync(admin, role) }, siteIds, isActive = true })));

    internal static string EmailOf(Guid id) => $"service-{id}@service.invalid";

    private static async Task<JsonElement> RowAsync(HttpClient admin, Guid id) =>
        (await Ok(await admin.GetAsync("/api/users"))).EnumerateArray().Single(u => Id(u) == id);

    [SkippableFact]
    public async Task Create_list_and_edit()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await ServiceAccountAsync(org.Admin, "SAP integration", "Viewer");
        var row = await RowAsync(org.Admin, id);
        Assert.True(row.GetProperty("isServiceAccount").GetBoolean());
        Assert.Equal("SAP integration", row.GetProperty("fullName").GetString());
        await Ok(await org.Admin.PutAsJsonAsync($"/api/service-accounts/{id}", new { fullName = "SAP", roleIds = new[] { await RoleAsync(org.Admin, "Viewer") }, isActive = true }));
        Assert.Equal("SAP", (await RowAsync(org.Admin, id)).GetProperty("fullName").GetString());
        Assert.Contains("Name is required.", await Bad(await org.Admin.PostAsJsonAsync("/api/service-accounts", new { fullName = "", roleIds = new[] { await RoleAsync(org.Admin, "Viewer") }, isActive = true })));
    }

    [SkippableFact]
    public async Task Cannot_sign_in_or_get_a_password()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await ServiceAccountAsync(org.Admin, "BI reader", "Viewer");
        var login = await fx.Client().PostAsJsonAsync("/api/auth/login", new { email = EmailOf(id), password = "Passw0rd!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("Service accounts have no password.", await Bad(await org.Admin.PostAsJsonAsync($"/api/users/{id}/password", new { password = "Another0ne!x" })));
        Assert.Contains("Edit service accounts under Service accounts.", await Bad(await org.Admin.PutAsJsonAsync($"/api/users/{id}", new
        {
            fullName = "BI", email = Addr("bi"), isActive = true, roleIds = new[] { await RoleAsync(org.Admin, "Viewer") },
        })));
    }

    [SkippableFact]
    public async Task Not_a_person()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await ServiceAccountAsync(org.Admin, "Gateway", "Administrator");
        Assert.DoesNotContain((await Ok(await org.Admin.GetAsync("/api/users/lookup"))).EnumerateArray(), u => Id(u) == id);
        Assert.False((await org.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Manager", minAmount = 0, rule = "Any", approverIds = new[] { id } })).IsSuccessStatusCode);
        Assert.False((await org.Admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Gate", userId = id, isActive = true })).IsSuccessStatusCode);
        await DbAsync(fx, db => db.Users.IgnoreQueryFilters().Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        Assert.Contains("Service accounts aren't people.", await Bad(await org.Admin.GetAsync($"/api/anonymise/preview?userId={id}")));
    }

    [SkippableFact]
    public async Task Never_emailed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var id = await ServiceAccountAsync(org.Admin, "Integration", "Administrator");
        var requester = await UserAsync(fx, org.Admin, "Requester");
        await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Tap drips", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" }));
        Assert.NotEmpty(await RowsAsync(fx, org.AdminEmail)); // the request's e-mail went out to the people who triage requests
        Assert.Empty(await RowsAsync(fx, EmailOf(id)));
    }
}
