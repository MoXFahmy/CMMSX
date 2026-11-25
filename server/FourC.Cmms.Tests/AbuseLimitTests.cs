using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static FourC.Cmms.Tests.ApiKeyTests;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.ServiceAccountTests;

namespace FourC.Cmms.Tests;

/// <summary>Per-address limits (0.29.0): wrong passwords, sign-ups and answer links from one address; the address comes from a proxy header.</summary>
public sealed class AbuseFixture : ApiFixture
{
    protected override void Configure(IWebHostBuilder b)
    {
        b.UseSetting("Abuse:Enabled", "true");
        b.UseSetting("Abuse:SignInFailures", "3");
        b.UseSetting("Abuse:SignUpsPerHour", "2");
        b.UseSetting("Abuse:AnswersPer15Minutes", "3");
        b.UseSetting("Abuse:ViewerAddressHeader", "CloudFront-Viewer-Address");
    }
}

public class AbuseLimitTests(AbuseFixture fx) : IClassFixture<AbuseFixture>
{
    private const string Header = "CloudFront-Viewer-Address";
    private static int _next;

    private HttpClient From(string address)
    {
        var c = fx.Client();
        c.DefaultRequestHeaders.Add(Header, $"{address}:1234");
        return c;
    }

    /// <summary>An organization set up from an address of its own, so set-up never counts against the address a test limits.</summary>
    private async Task<(HttpClient Admin, string Email)> OrgAsync()
    {
        var c = From($"198.51.100.{Interlocked.Increment(ref _next) % 250 + 1}");
        var email = Addr("admin");
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Abuse Co", fullName = "Admin", email, password = "Passw0rd!x" }));
        return (c, email);
    }

    private static Task<HttpResponseMessage> SignIn(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password });

    [SkippableFact]
    public async Task Failed_sign_ins_from_one_address_are_limited()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await OrgAsync();
        var c = From("203.0.113.7");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(c, email, "wrong-password")).StatusCode);
        var blocked = await SignIn(c, email, "Passw0rd!x"); // even the right password
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many attempts from your network", await blocked.Content.ReadAsStringAsync());
        Assert.True(blocked.Headers.RetryAfter is not null);
    }

    [SkippableFact]
    public async Task Successful_sign_ins_are_never_limited()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await OrgAsync();
        var c = From("203.0.113.20");
        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.OK, (await SignIn(c, email, "Passw0rd!x")).StatusCode);
    }

    [SkippableFact]
    public async Task Limits_are_per_address()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await OrgAsync();
        var first = From("203.0.113.30");
        for (var i = 0; i < 4; i++) await SignIn(first, email, "wrong-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignIn(first, email, "Passw0rd!x")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignIn(From("203.0.113.31"), email, "Passw0rd!x")).StatusCode);
    }

    [SkippableFact]
    public async Task Sign_ups_are_limited_and_keys_are_not()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await OrgAsync();
        var account = await ServiceAccountAsync(admin, "Reader", "Viewer");
        var (_, key) = await KeyAsync(admin, account);
        var c = From("203.0.113.40");
        for (var i = 0; i < 2; i++)
            await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Co", fullName = "A", email = Addr("a"), password = "Passw0rd!x" }));
        var third = await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Co", fullName = "A", email = Addr("a"), password = "Passw0rd!x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("Too many attempts from your network", await third.Content.ReadAsStringAsync());
        // An API key from the same address is only ever limited by Api:RequestsPerMinute.
        var k = KeyClient(fx, key);
        k.DefaultRequestHeaders.Add(Header, "203.0.113.40:1234");
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await k.GetAsync("/api/assets")).StatusCode);
    }

    [SkippableFact]
    public async Task Answer_links_are_limited()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = From("203.0.113.50");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/public/feedback/not-a-real-token")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/public/feedback/not-a-real-token")).StatusCode);
    }

    /// <summary>The text the sign-in attack alarm counts: infra/aws/cmms.yml, SignInFailedMetric, <c>{ $.Message = "SignInFailed" }</c>.</summary>
    private const string SignInFailedLine = "SignInFailed";

    [SkippableFact]
    public async Task Wrong_password_logs_exactly_the_line_the_alarm_counts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await OrgAsync();
        var log = new ListLogger();
        fx.Factory!.Services.GetRequiredService<ILoggerFactory>().AddProvider(new ListLoggerProvider(log));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(From("203.0.113.60"), email, "wrong-password")).StatusCode);

        // The JSON log's Message is the whole formatted text, and the metric filter wants it equal, not containing.
        Assert.Contains(log.Lines, l => l == $"Warning {SignInFailedLine} ");
        // No address and no e-mail address anywhere in what was logged.
        Assert.DoesNotContain(log.Lines, l => l.Contains("203.0.113.60") || l.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The line the guard logs when it turns an attempt away (0.29.1): the alarm counts it with <see cref="SignInFailedLine"/>.</summary>
    private const string SignInRefusedLine = "SignInRefused";

    [SkippableFact]
    public async Task Refused_sign_in_logs_SignInRefused_without_address_or_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, email) = await OrgAsync();
        var c = From("203.0.113.61");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(c, email, "wrong-password")).StatusCode);
        var log = new ListLogger();
        fx.Factory!.Services.GetRequiredService<ILoggerFactory>().AddProvider(new ListLoggerProvider(log));

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignIn(c, email, "Passw0rd!x")).StatusCode);

        Assert.Contains(log.Lines, l => l == $"Warning {SignInRefusedLine} ");
        Assert.DoesNotContain(log.Lines, l => l.Contains("203.0.113.61") || l.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    private static string Template(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "infra", "aws", "cmms.yml"))) dir = dir.Parent;
        Assert.True(dir is not null, "infra/aws/cmms.yml not found above the test's folder");
        return File.ReadAllText(Path.Combine(dir!.FullName, "infra", "aws", name));
    }

    [Fact]
    public void Metric_filter_counts_both_sign_in_messages()
    {
        Assert.Contains($"FilterPattern: '{{ $.Message = \"{SignInFailedLine}\" || $.Message = \"{SignInRefusedLine}\" }}'", Template("cmms.yml"));
    }

    [Fact]
    public void The_edge_stack_alarms_on_cloudfront_errors_only_when_there_is_a_distribution()
    {
        var edge = Template("edge.yml");
        Assert.Contains("DistributionId:", edge);
        Assert.Contains("AlarmName: cmms-cloudfront-errors", edge);
        Assert.Contains("5xxErrorRate", edge);
        Assert.Contains("Condition: HasDistribution", edge);
        Assert.Matches(@"(?m)^  DistributionId:\r?\n(    .*\r?\n)*?    Value: !Ref Distribution\r?$", Template("cmms.yml"));
    }
}
