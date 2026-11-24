using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.ServiceAccountTests;

namespace FourC.Cmms.Tests;

/// <summary>API keys (0.26.0): a key acts as its service account, within its roles and sites; only its hash is kept.</summary>
public class ApiKeyTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<(Guid Id, string Key)> KeyAsync(HttpClient admin, Guid account, bool readOnly = false, string name = "Integration")
    {
        var made = await Ok(await admin.PostAsJsonAsync("/api/api-keys", new { serviceAccountId = account, name, readOnly }));
        return (Id(made), made.GetProperty("key").GetString()!);
    }

    /// <summary>A client with the key and no cookie.</summary>
    internal static HttpClient KeyClient(ApiFixture fx, string key)
    {
        var c = fx.Client();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        return c;
    }

    private static object Asset(Guid site, string name = "Pump") => new { name, siteId = site, status = "Active", criticality = "High", warrantyType = "None" };

    [SkippableFact]
    public async Task Key_acts_as_its_account()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "SAP integration", "Administrator");
        var (_, key) = await KeyAsync(org.Admin, account);
        var asset = Id(await Ok(await KeyClient(fx, key).PostAsJsonAsync("/api/assets", Asset(org.Site))));
        var created = await DbAsync(fx, db => db.AuditEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.EntityId == asset && a.Action == "Created"));
        Assert.Equal(("API", "SAP integration", account), (created.Source, created.UserName, created.UserId!.Value));
    }

    [SkippableFact]
    public async Task Key_stays_within_its_roles_and_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var siteB = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var here = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", Asset(org.Site, "Here"))));
        var there = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", Asset(siteB, "There"))));
        var account = await ServiceAccountAsync(org.Admin, "BI reader", "Viewer", [org.Site]);
        var c = KeyClient(fx, (await KeyAsync(org.Admin, account)).Key);
        var items = (await Ok(await c.GetAsync("/api/assets"))).GetProperty("items").EnumerateArray().Select(Id).ToList();
        Assert.Contains(here, items);
        Assert.DoesNotContain(there, items);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/assets", Asset(org.Site))).StatusCode);
    }

    [SkippableFact]
    public async Task Only_the_hash_is_kept()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "Gateway", "Viewer");
        var (id, key) = await KeyAsync(org.Admin, account);
        Assert.StartsWith("cmx_", key);
        Assert.Equal(44, key.Length);
        var row = await DbAsync(fx, db => db.ApiKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync(k => k.Id == id));
        Assert.Equal(ApiKeyAuth.HashOf(key), row.Hash);
        Assert.Equal(key[4..12], row.Prefix);
        var listed = (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().Single(k => Id(k) == id);
        Assert.Equal(key[4..12], listed.GetProperty("prefix").GetString());
        Assert.False(listed.TryGetProperty("key", out _));
        Assert.False(listed.TryGetProperty("hash", out _));
        Assert.Equal("Active", listed.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Bad_keys_all_answer_the_same()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "Gateway", "Viewer");
        var (revokedId, revoked) = await KeyAsync(org.Admin, account, name: "Old");
        await Ok(await org.Admin.PostAsync($"/api/api-keys/{revokedId}/revoke", null));
        var (expiredId, expired) = await KeyAsync(org.Admin, account, name: "Expired");
        await DbAsync(fx, db => db.ApiKeys.IgnoreQueryFilters().Where(k => k.Id == expiredId).ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))));
        var other = await ServiceAccountAsync(org.Admin, "Off", "Viewer");
        var (_, inactive) = await KeyAsync(org.Admin, other, name: "Off");
        await DbAsync(fx, db => db.Users.IgnoreQueryFilters().Where(u => u.Id == other).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        foreach (var key in new[] { "cmx_" + new string('a', 40), revoked, expired, inactive })
        {
            var r = await KeyClient(fx, key).GetAsync("/api/assets");
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Contains("The API key isn't valid.", await r.Content.ReadAsStringAsync());
        }
        var listed = (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().ToDictionary(k => k.GetProperty("name").GetString()!, k => k.GetProperty("status").GetString());
        Assert.Equal(("Revoked", "Expired", "AccountInactive"), (listed["Old"], listed["Expired"], listed["Off"]));
    }

    [SkippableFact]
    public async Task A_key_header_never_falls_back_to_the_cookie()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/assets");
        req.Headers.Add("X-Api-Key", "cmx_wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await org.Admin.SendAsync(req)).StatusCode);
        // The other header works too.
        var account = await ServiceAccountAsync(org.Admin, "Gateway", "Viewer");
        var (_, key) = await KeyAsync(org.Admin, account);
        using var ok = new HttpRequestMessage(HttpMethod.Get, "/api/assets");
        ok.Headers.Add("X-Api-Key", key);
        await Ok(await fx.Client().SendAsync(ok));
    }

    [SkippableFact]
    public async Task Deactivating_or_deleting_the_account_stops_its_keys()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var viewer = await RoleAsync(org.Admin, "Viewer");
        var account = await ServiceAccountAsync(org.Admin, "Gateway", "Viewer");
        var (id, key) = await KeyAsync(org.Admin, account);
        var c = KeyClient(fx, key);
        await Ok(await c.GetAsync("/api/assets"));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/service-accounts/{account}", new { fullName = "Gateway", roleIds = new[] { viewer }, isActive = false }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/assets")).StatusCode);
        await Ok(await org.Admin.PutAsJsonAsync($"/api/service-accounts/{account}", new { fullName = "Gateway", roleIds = new[] { viewer }, isActive = true }));
        await Ok(await c.GetAsync("/api/assets"));
        Assert.True((await org.Admin.DeleteAsync($"/api/service-accounts/{account}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/assets")).StatusCode);
        Assert.Equal("Revoked", (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().Single(k => Id(k) == id).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Last_used_is_kept()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var account = await ServiceAccountAsync(org.Admin, "Gateway", "Viewer");
        var (id, key) = await KeyAsync(org.Admin, account);
        Assert.Equal(JsonValueKind.Null, (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().Single(k => Id(k) == id).GetProperty("lastUsedAt").ValueKind);
        await Ok(await KeyClient(fx, key).GetAsync("/api/assets"));
        Assert.NotEqual(JsonValueKind.Null, (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().Single(k => Id(k) == id).GetProperty("lastUsedAt").ValueKind);
    }
}
