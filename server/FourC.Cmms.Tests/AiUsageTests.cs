using System.Net;
using System.Text.Json;
using FourC.Cmms.Core.Ai;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The AI usage page (0.15.0): this month against the limit, twelve months, by feature and person, and the call log.</summary>
public class AiUsageTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private Task AddAsync(Loop.Org org, DateTimeOffset at, AiResult result, AiFeature feature, Guid? user, string name, decimal cost) => DbAsync(fx, async db =>
    {
        db.AiCalls.Add(new AiCall { TenantId = org.Tenant, CreatedAt = at, Result = result, Feature = feature, UserId = user, UserName = name, Model = "claude-opus-5-5", EstimatedCost = cost, InputTokens = 100 });
        return await db.SaveChangesAsync();
    });

    /// <summary>UTC organization; calls this month by two people, and two and thirteen months back.</summary>
    private async Task<Loop.Org> SeedAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => { s["timeZone"] = "UTC"; s["aiEnabled"] = true; }));
        var now = DateTimeOffset.UtcNow;
        var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddHours(1);
        var (amal, badr) = (Guid.NewGuid(), Guid.NewGuid());
        await AddAsync(org, thisMonth, AiResult.Answered, AiFeature.Report, amal, "Amal", 0.01m);
        await AddAsync(org, thisMonth.AddMinutes(1), AiResult.Refused, AiFeature.Report, amal, "Amal", 0.002m);
        await AddAsync(org, thisMonth.AddMinutes(2), AiResult.Answered, AiFeature.Diagnosis, badr, "Badr", 0.05m);
        await AddAsync(org, thisMonth.AddMinutes(3), AiResult.Failed, AiFeature.Diagnosis, badr, "Badr", 0m);
        await AddAsync(org, thisMonth.AddMinutes(4), AiResult.OverLimit, AiFeature.Report, amal, "Amal", 0m);
        await AddAsync(org, thisMonth.AddMonths(-2), AiResult.Answered, AiFeature.Report, amal, "Amal", 0.02m);
        await AddAsync(org, thisMonth.AddMonths(-13), AiResult.Answered, AiFeature.Report, amal, "Amal", 0.5m);
        return org;
    }

    [SkippableFact]
    public async Task Usage_sums_this_month_and_twelve_months()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await SeedAsync();
        var u = await Ok(await org.Admin.GetAsync("/api/ai/usage"));
        Assert.Equal(3, u.GetProperty("used").GetInt32());
        Assert.Equal(300, u.GetProperty("limit").GetInt32());
        var months = u.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, months.Count);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(now.ToString("yyyy-MM"), months[^1].GetProperty("month").GetString());
        Assert.Equal((3, 0.062m), (months[^1].GetProperty("actions").GetInt32(), months[^1].GetProperty("cost").GetDecimal()));
        Assert.Equal((1, 0.02m), (months[^3].GetProperty("actions").GetInt32(), months[^3].GetProperty("cost").GetDecimal()));
        Assert.Equal(4, months.Sum(m => m.GetProperty("actions").GetInt32())); // the 13-month-old call is out
        var features = u.GetProperty("byFeature").EnumerateArray().ToDictionary(f => f.GetProperty("feature").GetString()!, f => f.GetProperty("actions").GetInt32());
        Assert.Equal(2, features["Report"]);
        Assert.Equal(1, features["Diagnosis"]);
        var people = u.GetProperty("byPerson").EnumerateArray().Select(p => (p.GetProperty("name").GetString(), p.GetProperty("actions").GetInt32())).ToList();
        Assert.Equal([("Amal", 2), ("Badr", 1)], people);
    }

    [SkippableFact]
    public async Task Call_log_filters_pages_and_exports()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await SeedAsync();
        var failed = await Ok(await org.Admin.GetAsync("/api/ai/calls?result=Failed"));
        Assert.Equal("Failed", Assert.Single(failed.GetProperty("items").EnumerateArray()).GetProperty("result").GetString());
        var diag = await Ok(await org.Admin.GetAsync("/api/ai/calls?feature=Diagnosis"));
        Assert.Equal(2, diag.GetProperty("total").GetInt32());
        var paged = await Ok(await org.Admin.GetAsync("/api/ai/calls?pageSize=2"));
        Assert.Equal((7, 2), (paged.GetProperty("total").GetInt32(), paged.GetProperty("items").GetArrayLength()));
        var first = paged.GetProperty("items")[0];
        Assert.Equal("OverLimit", first.GetProperty("result").GetString()); // newest first (13 months back is the oldest)
        var badr = (await Ok(await org.Admin.GetAsync("/api/ai/calls"))).GetProperty("items").EnumerateArray().First(i => i.GetProperty("userName").GetString() == "Badr").GetProperty("userId").GetGuid();
        Assert.Equal(2, (await Ok(await org.Admin.GetAsync($"/api/ai/calls?userId={badr}"))).GetProperty("total").GetInt32());
        var today = Iso(DateOnly.FromDateTime(DateTime.UtcNow));
        var monthStart = Iso(new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1));
        Assert.Equal(5, (await Ok(await org.Admin.GetAsync($"/api/ai/calls?from={monthStart}&to={today}"))).GetProperty("total").GetInt32());
        var file = await org.Admin.GetAsync("/api/ai/calls/export?feature=Report");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.Content.Headers.ContentType!.MediaType);
    }

    [SkippableFact]
    public async Task Usage_needs_settings_manage()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var tech = await UserAsync(fx, org.Admin, "Technician");
        foreach (var url in new[] { "/api/ai/usage", "/api/ai/calls", "/api/ai/calls/export" })
            Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.GetAsync(url)).StatusCode);
    }
}
