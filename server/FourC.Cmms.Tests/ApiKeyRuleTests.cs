using System.Net;
using System.Net.Http.Json;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using static FourC.Cmms.Tests.ApiKeyTests;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.ServiceAccountTests;

namespace FourC.Cmms.Tests;

/// <summary>What keys may do (0.26.0): read-only keys, areas no key reaches, and the per-key rate limit.</summary>
public class ApiKeyRuleTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static object Asset(Guid site) => new { name = "Pump", siteId = site, status = "Active", criticality = "High", warrantyType = "None" };

    [SkippableFact]
    public async Task Read_only_key_reads_but_cannot_write()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "BI", "Administrator");
        var c = KeyClient(fx, (await KeyAsync(org.Admin, account, readOnly: true)).Key);
        await Ok(await c.GetAsync("/api/assets"));
        var r = await c.PostAsJsonAsync("/api/assets", Asset(org.Site));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("This key is read-only.", await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Keys_cannot_manage_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "ERP", "Administrator");
        var c = KeyClient(fx, (await KeyAsync(org.Admin, account)).Key);
        var role = await RoleAsync(org.Admin, "Viewer");
        var attempts = new Func<Task<HttpResponseMessage>>[]
        {
            () => c.PostAsJsonAsync("/api/users", new { fullName = "Evil", email = Addr("evil"), isActive = true, roleIds = new[] { role }, password = "Passw0rd!x" }),
            () => c.PutAsJsonAsync($"/api/roles/{role}", new { name = "Viewer", permissions = Array.Empty<string>() }),
            () => c.PostAsJsonAsync($"/api/users/{org.AdminId}/password", new { password = "Another0ne!x" }),
            () => c.PostAsJsonAsync("/api/api-keys", new { serviceAccountId = account, name = "More", readOnly = false }),
            () => c.PostAsJsonAsync("/api/service-accounts", new { fullName = "More", roleIds = new[] { role }, isActive = true }),
            () => c.PutAsJsonAsync("/api/settings/security", new { passwordMinLength = 8, passwordExpiryDays = 0, passwordHistory = 0, oneSessionPerUser = false }),
            () => c.PostAsJsonAsync("/api/anonymise", new { userId = org.AdminId, confirm = "x" }),
            () => c.GetAsync("/api/auth/me"),
            () => c.GetAsync("/api/tech/bundle"),
        };
        foreach (var attempt in attempts)
        {
            var r = await attempt();
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{r.RequestMessage!.Method} {r.RequestMessage.RequestUri}: {(int)r.StatusCode}");
            Assert.Contains("API keys can't do this.", await r.Content.ReadAsStringAsync());
        }
        await Ok(await c.GetAsync("/api/users"));
        await Ok(await c.GetAsync("/api/roles"));
    }

    [Theory]
    [InlineData("GET", "/api/users", false)]
    [InlineData("POST", "/api/users", true)]
    [InlineData("PUT", "/api/users/5b0c2b1e-7a0e-4a52-9a49-0b2f4b4a0b1c", true)]
    [InlineData("GET", "/api/roles", false)]
    [InlineData("DELETE", "/api/roles/5b0c2b1e-7a0e-4a52-9a49-0b2f4b4a0b1c", true)]
    [InlineData("GET", "/api/auth/me", true)]
    [InlineData("GET", "/api/tech/bundle", true)]
    [InlineData("GET", "/api/api-keys", true)]
    [InlineData("GET", "/api/settings/security", true)]
    [InlineData("GET", "/api/settings", false)]
    [InlineData("GET", "/api/assets", false)]
    [InlineData("POST", "/api/work-requests", false)]
    public void Blocked_paths(string method, string path, bool blocked) => Assert.Equal(blocked, ApiKeyRules.Blocked(method, new PathString(path)));

    [SkippableFact]
    public async Task Rate_limit_answers_429()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "Busy", "Viewer");
        var busy = KeyClient(fx, (await KeyAsync(org.Admin, account, name: "Busy")).Key);
        var calm = KeyClient(fx, (await KeyAsync(org.Admin, account, name: "Calm")).Key);
        HttpResponseMessage last = null!;
        for (var i = 0; i < 121; i++) last = await busy.GetAsync("/api/sites");
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.RetryAfter is not null, "Retry-After");
        Assert.Contains("Too many requests with this key.", await last.Content.ReadAsStringAsync());
        await Ok(await calm.GetAsync("/api/sites"));
        await Ok(await org.Admin.GetAsync("/api/sites")); // people signed in aren't limited
    }
}
