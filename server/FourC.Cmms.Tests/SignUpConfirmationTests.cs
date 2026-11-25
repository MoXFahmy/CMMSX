using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>New organizations confirm their e-mail before use (0.29.0). The operator's allow-list is narrowed to "CMX", so every test also proves it doesn't stop this e-mail.</summary>
public sealed class ConfirmFixture : ApiFixture
{
    protected override void Configure(IWebHostBuilder b)
    {
        b.UseSetting("Auth:RequireEmailConfirmation", "true");
        b.UseSetting("Email:AllowedOrganizations", "CMX");
    }
}

public class SignUpConfirmationTests(ConfirmFixture fx) : IClassFixture<ConfirmFixture>
{
    private const string BadLink = "This link has expired or was already used.";
    private const string Pending = "Confirm your e-mail address to start using CMX.";
    private static readonly Regex TokenIn = new(@"/confirm-email\?token=([A-Za-z0-9_\-]+)");

    private static Task<HttpResponseMessage> Register(HttpClient c, string email) =>
        c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Confirm Co", fullName = "Owner", email, password = "Passw0rd!x" });

    private static async Task<string> TokenAsync(ApiFixture fx, string email, int which = -1)
    {
        var rows = (await RowsAsync(fx, email)).OrderBy(r => r.CreatedAt).ToList();
        var row = which < 0 ? rows[^1] : rows[which];
        var m = TokenIn.Match(row.TextBody);
        Assert.True(m.Success, row.TextBody);
        return m.Groups[1].Value;
    }

    private static async Task<string> ProblemTitle(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    [SkippableFact]
    public async Task Sign_up_waits_for_the_email_link()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        var email = Addr("owner");
        var body = await Ok(await Register(c, email));
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("confirm").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        var rows = await RowsAsync(fx, email);
        var row = Assert.Single(rows);
        Assert.Contains("/confirm-email?token=", row.TextBody);
        Assert.Contains("/confirm-email?token=", row.HtmlBody);
    }

    [SkippableFact]
    public async Task Sign_in_before_confirming_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        var email = Addr("owner");
        await Ok(await Register(c, email));
        var r = await fx.Client().PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("email-unconfirmed", json.GetProperty("code").GetString());
        Assert.Equal(Pending, json.GetProperty("title").GetString());
    }

    [SkippableFact]
    public async Task Link_confirms_once_and_signs_in()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        var email = Addr("owner");
        await Ok(await Register(c, email));
        var token = await TokenAsync(fx, email);
        var ok = await Ok(await c.PostAsJsonAsync("/api/auth/confirm-email", new { token }));
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        // Signing in with the password works now, too.
        Assert.Equal(HttpStatusCode.OK, (await fx.Client().PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", rememberMe = false })).StatusCode);
        var again = await fx.Client().PostAsJsonAsync("/api/auth/confirm-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(BadLink, await ProblemTitle(again));
    }

    [SkippableFact]
    public async Task Expired_link_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        var email = Addr("owner");
        await Ok(await Register(c, email));
        var token = await TokenAsync(fx, email);
        await DbAsync(fx, async db =>
        {
            var tenantId = await db.Users.IgnoreQueryFilters().Where(u => u.Email == email).Select(u => u.TenantId).FirstAsync();
            var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
            tenant.SignUpTokenExpires = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            return 0;
        });
        var r = await fx.Client().PostAsJsonAsync("/api/auth/confirm-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(BadLink, await ProblemTitle(r));
        var still = await DbAsync(fx, db => db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.SignUpPendingSince != null && db.Users.IgnoreQueryFilters().Any(u => u.TenantId == t.Id && u.Email == email)));
        Assert.True(still);
    }

    [SkippableFact]
    public async Task Signing_up_again_sends_a_new_link()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var email = Addr("owner");
        await Ok(await Register(fx.Client(), email));
        var old = await TokenAsync(fx, email);
        var second = await Ok(await Register(fx.Client(), email));
        Assert.True(second.GetProperty("ok").GetBoolean());
        Assert.True(second.GetProperty("confirm").GetBoolean());
        Assert.Equal(2, (await RowsAsync(fx, email)).Count);
        var fresh = await TokenAsync(fx, email);
        Assert.NotEqual(old, fresh);
        Assert.Equal(HttpStatusCode.BadRequest, (await fx.Client().PostAsJsonAsync("/api/auth/confirm-email", new { token = old })).StatusCode);
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/confirm-email", new { token = fresh }));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        // Once confirmed, the address is taken as before.
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(fx.Client(), email)).StatusCode);
    }

    [SkippableFact]
    public async Task Resend_never_reveals_an_account()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var unknown = Addr("nobody");
        var r1 = await Ok(await fx.Client().PostAsJsonAsync("/api/auth/resend-confirmation", new { email = unknown }));
        Assert.True(r1.GetProperty("ok").GetBoolean());
        Assert.Empty(await RowsAsync(fx, unknown));

        var email = Addr("owner");
        var c = fx.Client();
        await Ok(await Register(c, email));
        await Ok(await c.PostAsJsonAsync("/api/auth/confirm-email", new { token = await TokenAsync(fx, email) }));
        var before = (await RowsAsync(fx, email)).Count;
        var r2 = await Ok(await fx.Client().PostAsJsonAsync("/api/auth/resend-confirmation", new { email }));
        Assert.True(r2.GetProperty("ok").GetBoolean());
        Assert.Equal(before, (await RowsAsync(fx, email)).Count);

        // A pending address does get a fresh link.
        var pending = Addr("owner");
        await Ok(await Register(fx.Client(), pending));
        var old = await TokenAsync(fx, pending);
        Assert.True((await Ok(await fx.Client().PostAsJsonAsync("/api/auth/resend-confirmation", new { email = pending }))).GetProperty("ok").GetBoolean());
        Assert.Equal(2, (await RowsAsync(fx, pending)).Count);
        Assert.NotEqual(old, await TokenAsync(fx, pending));
    }

    [SkippableFact]
    public async Task Existing_and_admin_added_people_need_no_link()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        // An organization that already existed (no sign-up pending) with a person its administrator adds.
        var adminEmail = Addr("admin");
        var tenantId = Guid.CreateVersion7();
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var opts = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            await using var db = new CmmsDbContext(opts, new FixedTenantContext(tenantId, source: "Web"));
            var (tenant, _) = await TenantProvisioning.CreateAsync(db, hasher, tenantId, "Old Co", "Admin", adminEmail, "Passw0rd!x");
            Assert.Null(tenant.SignUpPendingSince);
        }
        var admin = fx.Client();
        await Ok(await admin.PostAsJsonAsync("/api/auth/login", new { email = adminEmail, password = "Passw0rd!x", rememberMe = false }));
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var viewer = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Viewer").GetProperty("id").GetGuid();
        var added = Addr("added");
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Added", email = added, isActive = true, roleIds = new[] { viewer }, password = "Passw0rd!v" }));
        var v = fx.Client();
        await Ok(await v.PostAsJsonAsync("/api/auth/login", new { email = added, password = "Passw0rd!v", rememberMe = false }));
        Assert.Equal(HttpStatusCode.OK, (await v.GetAsync("/api/auth/me")).StatusCode);
        Assert.Empty(await RowsAsync(fx, adminEmail, added));
    }

    [SkippableFact]
    public async Task Confirmation_mail_ignores_the_org_allow_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var email = Addr("owner");
        await Ok(await Register(fx.Client(), email));
        // The organization isn't "CMX" and has not switched e-mail on, yet the confirmation went out.
        var row = Assert.Single(await RowsAsync(fx, email));
        Assert.Equal("signup.confirm", row.EventKey);
        // And no address is kept anywhere: the row holds the person's own address only.
        Assert.Equal(email, row.ToAddress);
    }
}
