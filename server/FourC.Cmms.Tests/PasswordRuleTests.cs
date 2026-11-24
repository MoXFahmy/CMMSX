using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>An organization's password rules (0.25.0).</summary>
public class PasswordRuleTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static Task<HttpResponseMessage> SetSecurity(HttpClient c, int min = 8, int expiry = 0, int history = 0, bool oneSession = false, int? retention = null) =>
        c.PutAsJsonAsync("/api/settings/security", new { passwordMinLength = min, passwordExpiryDays = expiry, passwordHistory = history, oneSessionPerUser = oneSession, activityRetentionMonths = retention });

    private static async Task Refused(HttpResponseMessage r, string message)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        Assert.Contains(message, body);
    }

    private static async Task<Guid> RoleAsync(HttpClient c) => Id((await Ok(await c.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == "Technician"));

    private static Task<HttpResponseMessage> ChangePassword(HttpClient c, string current, string next) =>
        c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = current, newPassword = next });

    private async Task BackdateAsync(Guid userId, int days)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordChangedAt, DateTimeOffset.UtcNow.AddDays(-days)));
    }

    [SkippableFact]
    public async Task Defaults_change_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s = await Ok(await org.Admin.GetAsync("/api/settings/security"));
        Assert.Equal((8, 0, 0, false), (s.GetProperty("passwordMinLength").GetInt32(), s.GetProperty("passwordExpiryDays").GetInt32(),
            s.GetProperty("passwordHistory").GetInt32(), s.GetProperty("oneSessionPerUser").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, s.GetProperty("activityRetentionMonths").ValueKind);
        await Ok(await org.Admin.PostAsJsonAsync("/api/users", new { fullName = "Eight", email = Addr("eight"), isActive = true, roleIds = new[] { await RoleAsync(org.Admin) }, password = "Abcdef1!" }));
    }

    [SkippableFact]
    public async Task Minimum_length_applies_everywhere()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await SetSecurity(org.Admin, min: 12));
        await Refused(await org.Admin.PostAsJsonAsync("/api/users", new { fullName = "Ten", email = Addr("ten"), isActive = true, roleIds = new[] { await RoleAsync(org.Admin) }, password = "Abcdefgh1!" }),
            "Use at least 12 characters.");
        await Refused(await ChangePassword(org.Admin, "Passw0rd!x", "Abcdefgh1!"), "Use at least 12 characters.");
        await Ok(await ChangePassword(org.Admin, "Passw0rd!x", "Abcdefgh1!xyz"));
        // A new organization starts with the default rules.
        await Ok(await fx.Client().PostAsJsonAsync("/api/auth/register", new { organizationName = "Eight Co", fullName = "Admin", email = Addr("new"), password = "Passw0rd" }));
    }

    [SkippableFact]
    public async Task Recent_passwords_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await SetSecurity(org.Admin, history: 3));
        await Ok(await ChangePassword(org.Admin, "Passw0rd!x", "Second0ne!"));
        await Ok(await ChangePassword(org.Admin, "Second0ne!", "Third0ne!!"));
        await Refused(await ChangePassword(org.Admin, "Third0ne!!", "Passw0rd!x"), "You used this password recently. Choose another one.");
        await Refused(await ChangePassword(org.Admin, "Third0ne!!", "Second0ne!"), "You used this password recently. Choose another one.");
    }

    [SkippableFact]
    public async Task Expired_password_must_be_renewed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await SetSecurity(org.Admin, expiry: 30));
        await BackdateAsync(org.AdminId, 31);
        var c = fx.Client();
        var login = await c.PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Passw0rd!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("password-expired", body.GetProperty("code").GetString());
        Assert.Contains("Your password has expired. Choose a new one.", body.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        await Refused(await c.PostAsJsonAsync("/api/auth/renew-password", new { email = org.AdminEmail, password = "Passw0rd!x", newPassword = "Passw0rd!x", rememberMe = false }),
            "You used this password recently. Choose another one.");
        await Ok(await c.PostAsJsonAsync("/api/auth/renew-password", new { email = org.AdminEmail, password = "Passw0rd!x", newPassword = "Renewed0ne!", rememberMe = false }));
        await Ok(await c.GetAsync("/api/auth/me"));
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        Assert.True(await db.AuditEntries.IgnoreQueryFilters().AnyAsync(a => a.EntityId == org.AdminId && a.Action == "PasswordRenewed"));
    }

    [SkippableFact]
    public async Task Raised_minimum_is_met_at_next_sign_in()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await SetSecurity(org.Admin, min: 12));
        var login = await fx.Client().PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Passw0rd!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("password-rules", body.GetProperty("code").GetString());
        Assert.Contains("Your organization's password rules changed. Choose a new password.", body.GetProperty("title").GetString());
    }

    [SkippableFact]
    public async Task Older_settings_pages_keep_security_settings()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await SetSecurity(org.Admin, min: 12, history: 2));
        var current = JsonNode.Parse(await (await org.Admin.GetAsync("/api/settings")).Content.ReadAsStringAsync())!.AsObject();
        var settings = current["settings"]!.AsObject();
        settings.Remove("security");
        await Ok(await org.Admin.PutAsJsonAsync("/api/settings", new JsonObject { ["name"] = current["name"]!.GetValue<string>(), ["settings"] = settings.DeepClone() }));
        var s = await Ok(await org.Admin.GetAsync("/api/settings/security"));
        Assert.Equal((12, 2), (s.GetProperty("passwordMinLength").GetInt32(), s.GetProperty("passwordHistory").GetInt32()));
    }

    [SkippableFact]
    public async Task Security_settings_are_validated()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Refused(await SetSecurity(org.Admin, min: 7), "Enter a minimum length from 8 to 64.");
        await Refused(await SetSecurity(org.Admin, expiry: 10), "Enter 0 (never) or 30 to 730 days.");
        await Refused(await SetSecurity(org.Admin, history: 25), "Enter 0 to 24.");
        await Refused(await SetSecurity(org.Admin, retention: 6), "Keep at least 12 months, at most 120, or leave it empty.");
    }
}
