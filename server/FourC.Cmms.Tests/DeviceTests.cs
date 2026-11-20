using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class DeviceTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Password = "Passw0rd!x";
    private const string AndroidUa = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36 4CCMMS-Android/0.4.0";
    private const string DesktopUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Registers an organization (that client is signed in, the old way, without a device session) and returns its e-mail.</summary>
    private async Task<(HttpClient Admin, string Email)> RegisterAsync()
    {
        var c = fx.Client();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Devices Co", fullName = "Admin", email, password = Password }));
        return (c, email);
    }

    private async Task<HttpClient> SignInAsync(string email, string userAgent, bool rememberMe = true, string password = Password)
    {
        var c = fx.Client();
        c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe }));
        return c;
    }

    private static async Task<List<JsonElement>> DevicesAsync(HttpClient c) => (await Ok(await c.GetAsync("/api/auth/me/devices"))).EnumerateArray().ToList();

    [SkippableFact]
    public async Task Signing_in_records_a_device_named_after_the_browser_or_app()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await RegisterAsync();
        var phone = await SignInAsync(email, AndroidUa);
        var mine = Assert.Single(await DevicesAsync(phone), d => d.GetProperty("current").GetBoolean());
        Assert.Equal("Android app 0.4.0", mine.GetProperty("deviceName").GetString());
    }

    [SkippableFact]
    public async Task Signing_a_device_out_stops_it_within_a_minute_and_says_so()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await RegisterAsync();
        var a = await SignInAsync(email, DesktopUa);
        var b = await SignInAsync(email, AndroidUa);
        var bDevice = (await DevicesAsync(a)).Single(d => d.GetProperty("deviceName").GetString() == "Android app 0.4.0");
        await Ok(await a.PostAsync($"/api/auth/me/devices/{bDevice.GetProperty("id").GetGuid()}/sign-out", null));

        var refused = await b.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.True(refused.Headers.TryGetValues("X-Session-Revoked", out var v) && v.Single() == "1");
        await Ok(await a.GetAsync("/api/auth/me"));
    }

    [SkippableFact]
    public async Task Signing_out_other_devices_keeps_this_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, email) = await RegisterAsync();
        var a = await SignInAsync(email, DesktopUa);
        var b = await SignInAsync(email, AndroidUa);
        await Ok(await a.PostAsync("/api/auth/me/devices/sign-out-others", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/me")).StatusCode);
        Assert.True(Assert.Single(await DevicesAsync(a)).GetProperty("current").GetBoolean());
    }

    [SkippableFact]
    public async Task A_password_change_keeps_the_device_session()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await RegisterAsync();
        var a = await SignInAsync(email, DesktopUa);
        var before = (await DevicesAsync(a)).Single(d => d.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        await Ok(await a.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "N3wPassw0rd!x" }));
        var after = (await DevicesAsync(a)).Single(d => d.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        Assert.Equal(before, after);
    }

    [SkippableFact]
    public async Task Sign_ins_from_before_the_upgrade_are_adopted_as_devices()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await RegisterAsync(); // signed in without a device session, like a cookie from 0.3.x
        var d = Assert.Single(await DevicesAsync(admin));
        Assert.Equal("Browser", d.GetProperty("deviceName").GetString()); // the test client sends no user agent
        Assert.True(d.GetProperty("current").GetBoolean());
        Assert.Single(await DevicesAsync(admin)); // adopted once, not on every request
    }

    [SkippableFact]
    public async Task Technician_sign_ins_last_30_days_and_others_12_hours()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await RegisterAsync();
        DateTimeOffset Expires(HttpResponseMessage r)
        {
            var cookie = r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("cmms.auth="));
            var part = cookie.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
            return DateTimeOffset.Parse(part["expires=".Length..]);
        }
        async Task<HttpResponseMessage> Login(string ua, bool technician = false)
        {
            var c = fx.Client();
            c.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
            var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password = Password, rememberMe = true, technician });
            await Ok(r);
            return r;
        }
        Assert.True(Expires(await Login(AndroidUa)) >= DateTimeOffset.UtcNow.AddDays(29));
        Assert.True(Expires(await Login(DesktopUa, technician: true)) >= DateTimeOffset.UtcNow.AddDays(29));
        Assert.True(Expires(await Login(DesktopUa)) <= DateTimeOffset.UtcNow.AddHours(13));
    }

    [SkippableFact]
    public async Task Administrators_see_and_sign_out_any_users_devices()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await RegisterAsync();
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var tech = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Technician").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        var user = (await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Tech", email, isActive = true, roleIds = new[] { tech }, password = Password }))).GetProperty("id").GetGuid();
        var phone = await SignInAsync(email, AndroidUa);

        var devices = (await Ok(await admin.GetAsync($"/api/users/{user}/devices"))).EnumerateArray().ToList();
        var d = Assert.Single(devices);
        await Ok(await admin.PostAsync($"/api/users/{user}/devices/{d.GetProperty("id").GetGuid()}/sign-out", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync(email, DesktopUa)).GetAsync($"/api/users/{user}/devices")).StatusCode);
    }
}
