using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>One session per person, expiry on devices already signed in, and sign-ins in the activity log (0.25.0).</summary>
public class SessionRuleTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private Task<HttpResponseMessage> Login(HttpClient c, string email, bool endOthers = false) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", rememberMe = false, endOtherSessions = endOthers });

    [SkippableFact]
    public async Task Second_sign_in_asks_before_ending_the_first()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, oneSession: true));
        var a = fx.Client();
        await Ok(await Login(a, org.AdminEmail, endOthers: true)); // the registration's own session is ended
        var b = fx.Client();
        var asked = await Login(b, org.AdminEmail);
        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        var body = await asked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("other-session", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("device").GetString()));
        await Ok(await a.GetAsync("/api/auth/me")); // asking ended nothing
        await Ok(await Login(b, org.AdminEmail, endOthers: true));
        var ended = await a.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        Assert.Equal("1", ended.Headers.GetValues("X-Session-Revoked").Single());
        await Ok(await b.GetAsync("/api/auth/me"));
    }

    [SkippableFact]
    public async Task Off_by_default_nothing_asks()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = fx.Client();
        var b = fx.Client();
        await Ok(await Login(a, org.AdminEmail));
        await Ok(await Login(b, org.AdminEmail));
        await Ok(await a.GetAsync("/api/auth/me"));
        await Ok(await b.GetAsync("/api/auth/me"));
    }

    [SkippableFact]
    public async Task Expiry_reaches_signed_in_devices()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await org.Admin.GetAsync("/api/auth/me"));
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, expiry: 30));
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == org.AdminId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordChangedAt, DateTimeOffset.UtcNow.AddDays(-31)));
            ((MemoryCache)scope.ServiceProvider.GetRequiredService<IMemoryCache>()).Compact(1.0);
        }
        var r = await org.Admin.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("1", r.Headers.GetValues("X-Password-Expired").Single());
    }

    [SkippableFact]
    public async Task Sign_ins_and_failures_are_recorded()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var c = fx.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Wrong-one-9", rememberMe = false })).StatusCode);
        await Ok(await Login(c, org.AdminEmail));
        await Ok(await c.PostAsync("/api/auth/logout", null));
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var events = await db.AuditEntries.IgnoreQueryFilters().Where(a => a.EntityType == "AppUser" && a.EntityId == org.AdminId).ToListAsync();
        var failed = Assert.Single(events, e => e.Action == "SignInFailed");
        Assert.DoesNotContain("Wrong-one-9", failed.Changes ?? "");
        Assert.Contains("Device", failed.Changes ?? "");
        Assert.Contains(events, e => e.Action == "SignedIn" && e.Changes!.Contains("Device"));
        Assert.Contains(events, e => e.Action == "SignedOut");
    }
}
