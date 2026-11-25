using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The organization's logo (0.14.0) on printed reports and job cards.</summary>
public class LogoTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Png(int size, byte fill = 1) => [.. PngHead, .. Enumerable.Repeat(fill, size - PngHead.Length)];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient c, byte[] bytes, string name = "logo.png", string type = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return c.PostAsync("/api/settings/logo", new MultipartFormDataContent { { file, "file", name } });
    }

    private static async Task<long?> VersionAsync(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/settings"))).GetProperty("settings").GetProperty("logoVersion") is { ValueKind: JsonValueKind.Number } v ? v.GetInt64() : null;

    [SkippableFact]
    public async Task Logo_upload_replace_and_remove()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await UploadAsync(org.Admin, Png(100, 1)));
        var first = await VersionAsync(org.Admin);
        Assert.NotNull(first);
        var got = await org.Admin.GetAsync("/api/settings/logo");
        Assert.Equal(("image/png", 100), (got.Content.Headers.ContentType!.MediaType, (await got.Content.ReadAsByteArrayAsync()).Length));
        await Task.Delay(5);
        await Ok(await UploadAsync(org.Admin, Png(200, 2)));
        Assert.NotEqual(first, await VersionAsync(org.Admin));
        Assert.Equal(200, (await (await org.Admin.GetAsync("/api/settings/logo")).Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NoContent, (await org.Admin.DeleteAsync("/api/settings/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await org.Admin.GetAsync("/api/settings/logo")).StatusCode);
        Assert.Null(await VersionAsync(org.Admin));
    }

    [SkippableFact]
    public async Task Logo_download_carries_nosniff_and_the_content_policy()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await UploadAsync(org.Admin, Png(100)));
        var got = await org.Admin.GetAsync("/api/settings/logo");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
        Assert.Equal("nosniff", string.Join(",", got.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(FourC.Cmms.Api.Infrastructure.SecurityHeaders.ContentSecurityPolicy, string.Join(",", got.Headers.GetValues("Content-Security-Policy")));
    }

    [SkippableFact]
    public async Task Logo_type_and_size_are_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("Choose a PNG, JPEG or WebP image.", await Bad(await UploadAsync(org.Admin, "GIF89a-----"u8.ToArray(), "logo.gif", "image/gif")));
        Assert.Contains("Choose a PNG, JPEG or WebP image.", await Bad(await UploadAsync(org.Admin, "<svg></svg>"u8.ToArray(), "logo.png", "image/png"))); // not what it says
        Assert.Contains("The logo can be at most 1 MB.", await Bad(await UploadAsync(org.Admin, Png(1024 * 1024 + 1))));
    }

    [SkippableFact]
    public async Task Logo_needs_settings_manage_to_change()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await UploadAsync(org.Admin, Png(100)));
        var tech = await UserAsync(fx, org.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(tech.Client, Png(100))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.DeleteAsync("/api/settings/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tech.Client.GetAsync("/api/settings/logo")).StatusCode);
    }

    [SkippableFact]
    public async Task Another_organizations_logo_is_not_served()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var a = await OrgAsync(fx, emailOn: false);
        var b = await OrgAsync(fx, emailOn: false);
        await Ok(await UploadAsync(a.Admin, Png(100)));
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync("/api/settings/logo")).StatusCode);
    }

    [SkippableFact]
    public async Task Saving_settings_keeps_the_logo()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await UploadAsync(org.Admin, Png(100)));
        var version = await VersionAsync(org.Admin);
        var current = JsonNode.Parse((await Ok(await org.Admin.GetAsync("/api/settings"))).GetRawText())!;
        var settings = current["settings"]!.AsObject();
        settings.Remove("logoVersion"); // a page from before 0.14.0 doesn't know it
        await Ok(await org.Admin.PutAsJsonAsync("/api/settings", new { name = current["name"]!.GetValue<string>(), settings }));
        Assert.Equal(version, await VersionAsync(org.Admin));
        Assert.Equal(HttpStatusCode.OK, (await org.Admin.GetAsync("/api/settings/logo")).StatusCode);
    }
}
