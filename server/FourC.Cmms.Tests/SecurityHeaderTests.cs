using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Browser security headers (0.29.0): on every response, on every host; file downloads keep the headers they set themselves.</summary>
public class SecurityHeaderTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static void AssertHeaders(HttpResponseMessage r, string what, string expectedPolicy)
    {
        string One(string name) => r.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : $"(no {name})";
        Assert.True(expectedPolicy == One("Content-Security-Policy"), $"{what}: Content-Security-Policy was {One("Content-Security-Policy")}");
        Assert.True("nosniff" == One("X-Content-Type-Options"), $"{what}: X-Content-Type-Options");
        Assert.True("DENY" == One("X-Frame-Options"), $"{what}: X-Frame-Options");
        Assert.True("strict-origin-when-cross-origin" == One("Referrer-Policy"), $"{what}: Referrer-Policy");
        Assert.True("same-origin" == One("Cross-Origin-Opener-Policy"), $"{what}: Cross-Origin-Opener-Policy");
        Assert.True("camera=(self), microphone=(self), geolocation=(self)" == One("Permissions-Policy"), $"{what}: Permissions-Policy");
    }

    private static async Task<Guid> UploadAsync(HttpClient c, Guid org, string name, string type, byte[] bytes)
    {
        // Attached to the organization's first site (any entity the person can see will do).
        var sites = await Ok(await c.GetAsync("/api/sites"));
        var site = sites[0].GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Site"), "entityType" },
            { new StringContent(site.ToString()), "entityId" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "files", name);
        return (await Ok(await c.PostAsync("/api/attachments", form)))[0].GetProperty("id").GetGuid();
    }

    [SkippableFact]
    public async Task Api_page_and_file_responses_carry_the_headers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var health = await org.Admin.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        AssertHeaders(health, "/api/health", SecurityHeaders.ContentSecurityPolicy);
        // The page: the web build may be absent in the test host (404), the headers go on whatever the answer is.
        AssertHeaders(await org.Admin.GetAsync("/"), "/", SecurityHeaders.ContentSecurityPolicy);
        AssertHeaders(await org.Admin.GetAsync("/api/does-not-exist"), "unknown API path", SecurityHeaders.ContentSecurityPolicy);
        // An error answer carries them too.
        AssertHeaders(await fx.Client().GetAsync("/api/auth/me"), "401", SecurityHeaders.ContentSecurityPolicy);
    }

    [SkippableFact]
    public async Task Attachment_download_keeps_its_own_headers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var id = await UploadAsync(org.Admin, org.Tenant, "notes.txt", "text/plain", "hello"u8.ToArray());
        var r = await org.Admin.GetAsync($"/api/attachments/{id}/content");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("hello", await r.Content.ReadAsStringAsync());
        // The file's own, stricter policy (no script, sandboxed) stays; the other headers are the app's.
        Assert.Equal("default-src 'none'; style-src 'unsafe-inline'; sandbox", Assert.Single(r.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(r.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(r.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("same-origin", Assert.Single(r.Headers.GetValues("Cross-Origin-Opener-Policy")));

        var pdf = await UploadAsync(org.Admin, org.Tenant, "manual.pdf", "application/pdf", "%PDF-1.4"u8.ToArray());
        var shown = await org.Admin.GetAsync($"/api/attachments/{pdf}/content");
        Assert.Equal("default-src 'none'; style-src 'unsafe-inline'", Assert.Single(shown.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("application/pdf", shown.Content.Headers.ContentType?.MediaType);
    }

    [SkippableFact]
    public async Task Hsts_only_over_https()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        // The production path: behind CloudFront, which reports the original scheme in its own header.
        await using var behindCdn = fx.Factory!.WithWebHostBuilder(b =>
        {
            b.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
            b.UseSetting("ForwardedHeaders:ProtoHeaderName", "CloudFront-Forwarded-Proto");
        });
        var c = behindCdn.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var viaHttps = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        viaHttps.Headers.Add("CloudFront-Forwarded-Proto", "https");
        var secure = await c.SendAsync(viaHttps);
        Assert.Equal("max-age=31536000", Assert.Single(secure.Headers.GetValues("Strict-Transport-Security")));
        AssertHeaders(secure, "https", SecurityHeaders.ContentSecurityPolicy);

        var plain = await c.GetAsync("/api/health");
        Assert.False(plain.Headers.Contains("Strict-Transport-Security"), "plain HTTP must not get Strict-Transport-Security");
        AssertHeaders(plain, "http", SecurityHeaders.ContentSecurityPolicy);
    }

    [Fact]
    public void The_policy_is_the_specified_one()
    {
        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; media-src 'self' blob:; worker-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'",
            SecurityHeaders.ContentSecurityPolicy);
    }
}
