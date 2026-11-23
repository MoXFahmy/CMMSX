using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features.Ai;
using FourC.Cmms.Core.Ai;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The monthly AI allowance (0.15.0): counted per organization in its own time zone, never passed, failures free.</summary>
public class AiAllowanceTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Answer = """{"workDone":"Done","causeDetails":null,"symptomCode":null,"causeCode":null,"remedyCode":null,"hours":null,"parts":[]}""";

    private async Task<(Loop.Org Org, Guid Wo)> SetupAsync(int? limit = null)
    {
        fx.Ai.Reset();
        fx.Ai.Responder = _ => Task.FromResult(FakeAiClient.Answer(Answer));
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => { s["aiEnabled"] = true; s["aiMonthlyLimit"] = limit; }));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Leak", type = "Corrective", priority = "High", siteId = org.Site })));
        return (org, wo);
    }

    private static Task<HttpResponseMessage> Report(Loop.Org org, Guid wo) => AiReportTests.ReportAsync(org.Admin, wo, "replaced the seal");

    private static async Task<int> UsedAsync(Loop.Org org) => (await AiSettingsTests.StatusAsync(org.Admin)).GetProperty("used").GetInt32();

    private Task<List<AiCall>> CallsAsync(Loop.Org org) =>
        DbAsync(fx, db => db.AiCalls.IgnoreQueryFilters().Where(c => c.TenantId == org.Tenant).OrderBy(c => c.CreatedAt).ToListAsync());

    private Task AddCallAsync(Loop.Org org, DateTimeOffset at, AiResult result) => DbAsync(fx, async db =>
    {
        db.AiCalls.Add(new AiCall { TenantId = org.Tenant, UserName = "Seed", Model = "m", Result = result, CreatedAt = at });
        return await db.SaveChangesAsync();
    });

    [SkippableFact]
    public async Task Off_or_without_key_never_calls()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await SetupAsync();
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiEnabled"] = false));
        var off = await Report(org, wo);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Contains("The AI assistant is off for this organization.", await off.Content.ReadAsStringAsync());
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true));
        try
        {
            fx.Ai.Configured = false;
            var noKey = await Report(org, wo);
            Assert.Equal(HttpStatusCode.Conflict, noKey.StatusCode);
            Assert.Contains("AI isn't set up on this server.", await noKey.Content.ReadAsStringAsync());
        }
        finally { fx.Ai.Configured = true; }
        Assert.Empty(fx.Ai.Asks);
    }

    [SkippableFact]
    public async Task Allowance_is_enforced_with_the_message()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await SetupAsync(limit: 2);
        await Ok(await Report(org, wo));
        await Ok(await Report(org, wo));
        var over = await Report(org, wo);
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
        var renews = DateOnly.ParseExact((await AiSettingsTests.StatusAsync(org.Admin)).GetProperty("renewsOn").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains($"This month's AI allowance (2) is used up. It renews on {renews.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.",
            await over.Content.ReadAsStringAsync());
        Assert.Equal(2, fx.Ai.Asks.Count);
        Assert.Equal([AiResult.Answered, AiResult.Answered, AiResult.OverLimit], (await CallsAsync(org)).Select(c => c.Result));
        Assert.Equal(2, await UsedAsync(org));
    }

    [SkippableFact]
    public async Task Concurrent_calls_cannot_pass_the_limit()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await SetupAsync(limit: 1);
        fx.Ai.Responder = async _ => { await Task.Delay(500); return FakeAiClient.Answer(Answer); };
        var results = await Task.WhenAll(Report(org, wo), Report(org, wo));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], results.Select(r => r.StatusCode).OrderBy(s => (int)s));
        Assert.Single(fx.Ai.Asks);
    }

    [SkippableFact]
    public async Task Failed_call_is_logged_but_not_counted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await SetupAsync();
        fx.Ai.Responder = _ => throw new AiUnavailableException("network");
        var r = await Report(org, wo);
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Contains("The AI assistant didn't answer. Try again in a moment.", await r.Content.ReadAsStringAsync());
        Assert.Equal(AiResult.Failed, Assert.Single(await CallsAsync(org)).Result);
        Assert.Equal(0, await UsedAsync(org));
    }

    [SkippableFact]
    public async Task Lowered_limit_and_switch_off_refuse_at_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await SetupAsync(limit: 3);
        for (var i = 0; i < 3; i++) await Ok(await Report(org, wo));
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiMonthlyLimit"] = 2));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Report(org, wo)).StatusCode);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiMonthlyLimit"] = 0));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Report(org, wo)).StatusCode);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => { s["aiMonthlyLimit"] = null; s["aiEnabled"] = false; }));
        Assert.Equal(HttpStatusCode.Conflict, (await Report(org, wo)).StatusCode);
        Assert.Equal(3, fx.Ai.Asks.Count);
    }

    [SkippableFact]
    public async Task Month_follows_the_organization_time_zone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _) = await SetupAsync();
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["timeZone"] = "Africa/Cairo"));
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var first = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTimeOffset At(DateTime l) => new(l, tz.GetUtcOffset(l));
        await AddCallAsync(org, At(first.AddMinutes(-30)), AiResult.Answered); // 23:30 on the last day of last month, local
        await AddCallAsync(org, At(first.AddMinutes(30)), AiResult.Answered);  // 00:30 on the 1st, local
        var s = await AiSettingsTests.StatusAsync(org.Admin);
        Assert.Equal(1, s.GetProperty("used").GetInt32());
        Assert.Equal(Iso(DateOnly.FromDateTime(first.AddMonths(1))), s.GetProperty("renewsOn").GetString());
    }

    [SkippableFact]
    public async Task Stale_pending_rows_stop_counting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _) = await SetupAsync();
        await AddCallAsync(org, DateTimeOffset.UtcNow.AddMinutes(-6), AiResult.Pending);
        await AddCallAsync(org, DateTimeOffset.UtcNow.AddMinutes(-1), AiResult.Pending);
        Assert.Equal(1, await UsedAsync(org));
    }
}
