using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FourC.Cmms.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FourC.Cmms.Core.Notifications;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class PushTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private PushSenderService Sender => fx.Factory!.Services.GetRequiredService<PushSenderService>();

    /// <summary>A technician with a CMX login, linked to an employee, whose phone registered <paramref name="token"/>.</summary>
    private async Task<(Loop.User User, Guid Employee)> TechWithPhoneAsync(Loop.Org org, string token, Guid[]? siteIds = null)
    {
        var tech = await UserAsync(fx, org.Admin, "Technician", siteIds: siteIds);
        var emp = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = $"T{Guid.NewGuid():N}"[..8], fullName = "Tech", userId = tech.Id, siteIds, isContractor = false })));
        await Ok(await tech.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        return (tech, emp);
    }

    private static Task<Guid> JobAsync(HttpClient c, Loop.Org org, Guid assignee, Guid? site = null) =>
        Task.Run(async () => Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Check chiller", siteId = site ?? org.Site, priority = "High", type = "Corrective", assigneeIds = new[] { assignee } }))));

    [SkippableFact]
    public async Task An_assigned_technician_with_a_phone_gets_a_push()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"tok-{Guid.NewGuid():N}";
        var (_, emp) = await TechWithPhoneAsync(org, token);
        var wo = await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        var push = Assert.Single(fx.Pushes.To(token));
        Assert.Contains("Check chiller", push.Body);
        Assert.Equal($"/tech/jobs/{wo}", push.Link);
    }

    [SkippableFact]
    public async Task An_approved_request_pushes_to_the_requester()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var token = $"tok-{Guid.NewGuid():N}";
        await Ok(await requester.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        var id = Id(await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Lights out", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { }));
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Single(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Gone_tokens_are_removed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"gone-{Guid.NewGuid():N}";
        fx.Pushes.Gone[token] = true;
        var (tech, emp) = await TechWithPhoneAsync(org, token);
        await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
        var left = await DbAsync(fx, db => db.UserSessions.IgnoreQueryFilters().CountAsync(s => s.UserId == tech.Id && s.PushToken == token));
        Assert.Equal(0, left);
    }

    /// <summary>Answers Firebase's token and send calls with a chosen status and body.</summary>
    private sealed class FirebaseStub(HttpStatusCode status, string body) : HttpMessageHandler, IHttpClientFactory
    {
        public int Sends;
        public HttpClient CreateClient(string name) => new(this);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "token.test")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"a","expires_in":3600}""") });
            Sends++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static async Task<(PushResult Result, int Sends)> FirebaseAnswersAsync(HttpStatusCode status, string body)
    {
        using var rsa = RSA.Create(2048);
        var key = new { project_id = "p", client_email = "x@p.iam", private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = "https://token.test/token" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Push:ServiceAccountJson"] = System.Text.Json.JsonSerializer.Serialize(key) }).Build();
        var stub = new FirebaseStub(status, body);
        var result = await new FcmPushTransport(config, stub, NullLogger<FcmPushTransport>.Instance).SendAsync("tok", "t", "b", null, CancellationToken.None);
        return (result, stub.Sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"The registration token is not a valid FCM registration token"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED","details":[{"errorCode":"SENDER_ID_MISMATCH"}]}}""")]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}""")]
    public async Task Invalid_argument_and_sender_mismatch_mark_the_token_dead_without_retries(HttpStatusCode status, string body)
    {
        var (result, sends) = await FirebaseAnswersAsync(status, body);
        Assert.Equal(PushResult.Gone, result); // the sender clears the token and never retries a Gone answer
        Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":{"code":500,"status":"INTERNAL"}}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED","message":"The caller does not have permission"}}""")]
    public async Task Other_firebase_errors_are_still_retried(HttpStatusCode status, string body)
    {
        var (result, _) = await FirebaseAnswersAsync(status, body);
        Assert.Equal(PushResult.Failed, result);
    }

    [SkippableFact]
    public async Task Dead_tokens_cost_one_attempt_and_the_message_is_done()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"dead-{Guid.NewGuid():N}";
        fx.Pushes.Gone[token] = true;
        var (tech, emp) = await TechWithPhoneAsync(org, token);
        await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        var m = await DbAsync(fx, db => db.PushMessages.IgnoreQueryFilters().Where(p => p.UserId == tech.Id).SingleAsync());
        Assert.Equal((EmailStatus.Sent, 1), (m.Status, m.Attempts));
    }

    [SkippableFact]
    public async Task Push_messages_older_than_30_days_are_cleaned_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var now = DateTimeOffset.UtcNow;
        PushMessage Row(EmailStatus status, int daysAgo) => new() { TenantId = org.Tenant, UserId = org.AdminId, Title = "t", Body = "b", Status = status, CreatedAt = now.AddDays(-daysAgo) };
        var oldSent = Row(EmailStatus.Sent, 31); var oldFailed = Row(EmailStatus.Failed, 45);
        var oldPending = Row(EmailStatus.Pending, 40); var recentSent = Row(EmailStatus.Sent, 29); var recentFailed = Row(EmailStatus.Failed, 2);
        await DbAsync(fx, async db => { db.PushMessages.AddRange(oldSent, oldFailed, oldPending, recentSent, recentFailed); return await db.SaveChangesAsync(); });

        await fx.Factory!.Services.GetRequiredService<PlatformJobs>().RunDailyAsync(now, CancellationToken.None);

        var left = await DbAsync(fx, db => db.PushMessages.IgnoreQueryFilters().Where(p => p.TenantId == org.Tenant).Select(p => p.Id).ToListAsync());
        Assert.Equal(new[] { oldPending.Id, recentSent.Id, recentFailed.Id }.Order(), left.Order());
    }

    private static string Repo(string path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "VERSION"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir ?? throw new FileNotFoundException("VERSION"), path));
    }

    [SkippableFact]
    public async Task Request_approved_push_opens_an_existing_page()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var token = $"tok-{Guid.NewGuid():N}";
        await Ok(await requester.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        var id = Id(await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Lights out", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { }));
        await Sender.RunOnceAsync(CancellationToken.None);
        var link = Assert.Single(fx.Pushes.To(token)).Link!;

        var uri = new Uri(new Uri("https://x.test"), link);
        var web = Repo("web/src/App.tsx");
        var phone = Repo("web/src/tech/TechApp.tsx");
        if (uri.AbsolutePath.StartsWith("/tech/"))
        {
            // On the phone: the tech shell must have this route (not just its catch-all).
            var route = System.Text.RegularExpressions.Regex.Replace(uri.AbsolutePath, "[0-9a-f-]{36}", ":id");
            Assert.Contains($"<Route path=\"{route}\"", phone);
        }
        else
        {
            // The full web app: a tap in the phone shell leaves /tech (App.tsx picks the page by the address), so the page is a web route.
            Assert.Contains($"<Route path=\"{uri.AbsolutePath.TrimStart('/')}\"", web);
            Assert.Contains("startsWith('/tech/')", web);
            Assert.Equal($"?open={id}", uri.Query);
        }
        Assert.DoesNotContain("/tech/requests", link);
    }

    [SkippableFact]
    public async Task Pushes_respect_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var token = $"tok-{Guid.NewGuid():N}";
        var (_, emp) = await TechWithPhoneAsync(org, token, [other]);
        await JobAsync(org.Admin, org, emp, org.Site);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Signing_out_stops_pushes_to_that_phone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"tok-{Guid.NewGuid():N}";
        var (tech, emp) = await TechWithPhoneAsync(org, token);
        await Ok(await tech.Client.PostAsync("/api/auth/logout", null));
        await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Nothing_is_queued_without_a_key()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await using var bare = fx.Factory!.WithWebHostBuilder(b => b.UseSetting("Push:ServiceAccountJson", ""));
        var c = bare.CreateClient();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "No Push Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        var tenant = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var me = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var emp = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "ME", fullName = "Me", userId = me, isContractor = false })));
        await Ok(await c.PostAsJsonAsync("/api/tech/push-token", new { token = "tok-none" }));
        await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "x", siteId = site, priority = "Low", type = "Corrective", assigneeIds = new[] { emp } }));
        var queued = await DbAsync(fx, db => db.PushMessages.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenant));
        Assert.Equal(0, queued);
    }
}
