using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class LoopReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static JsonElement Kpi(JsonElement report, string label) =>
        report.GetProperty("summary").EnumerateArray().Single(k => k.GetProperty("label").GetString() == label).GetProperty("value");

    /// <summary>A request by <paramref name="requester"/> approved with <paramref name="trade"/>, completed and answered.</summary>
    private static async Task AnsweredAsync(HttpClient admin, Loop.User requester, Loop.Org org, Guid trade, bool accepted, int rating)
    {
        var id = Id(await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Fix", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        var wo = (await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { tradeId = trade }))).GetProperty("workOrderId").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" }));
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted, rating, comment = "ok" }));
    }

    [SkippableFact]
    public async Task Satisfaction_counts_the_answers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var elec = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "SPARK", name = "Sparks", isActive = true })));
        var plumb = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "PIPE", name = "Pipes", isActive = true })));
        await AnsweredAsync(org.Admin, requester, org, elec, true, 4);
        await AnsweredAsync(org.Admin, requester, org, plumb, false, 2);

        var report = await Ok(await org.Admin.GetAsync("/api/reports/requester-satisfaction"));
        Assert.Equal(2, Kpi(report, "Answers").GetInt32());
        Assert.Equal(3.0m, Kpi(report, "Average rating").GetDecimal());
        Assert.Equal(50m, Kpi(report, "Accepted").GetDecimal());

        var byTrade = await Ok(await org.Admin.GetAsync("/api/reports/requester-satisfaction?groupBy=trade"));
        var rows = byTrade.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!);
        Assert.Equal(1, rows["Sparks"].GetProperty("accepted").GetInt32());
        Assert.Equal(1, rows["Pipes"].GetProperty("refused").GetInt32());
    }

    [SkippableFact]
    public async Task Satisfaction_respects_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "SPARK", name = "Sparks", isActive = true })));
        await AnsweredAsync(org.Admin, requester, org, trade, true, 5);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var elsewhere = await UserAsync(fx, org.Admin, "Maintenance Manager", siteIds: [other]);
        var report = await Ok(await elsewhere.Client.GetAsync("/api/reports/requester-satisfaction"));
        Assert.Equal(0, Kpi(report, "Answers").GetInt32());
        Assert.Empty(report.GetProperty("rows").EnumerateArray());
    }

    [SkippableFact]
    public async Task Cancelled_work_by_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var reasons = (await Ok(await org.Admin.GetAsync("/api/cancel-reasons"))).EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!, Id);
        async Task<Guid> CancelAsync(string code)
        {
            var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "x", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
            await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Cancelled", cancelReasonId = reasons[code] }));
            return wo;
        }
        await CancelAsync("DUP");
        await CancelAsync("DUP");
        await CancelAsync("OTHER");
        var old = await CancelAsync("ERROR");
        await DbAsync(fx, async db =>
        {
            foreach (var c in await db.WorkOrderStatusChanges.IgnoreQueryFilters().Where(c => c.WorkOrderId == old).ToListAsync()) c.ChangedAt = DateTimeOffset.UtcNow.AddYears(-2);
            return await db.SaveChangesAsync();
        });

        var report = await Ok(await org.Admin.GetAsync("/api/reports/cancelled-work"));
        var rows = report.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!, r => r.GetProperty("cancelled").GetInt32());
        Assert.Equal(2, rows["Duplicate"]);
        Assert.Equal(1, rows["Other"]);
        Assert.False(rows.ContainsKey("Raised in error"));
        Assert.Equal(3, Kpi(report, "Cancelled").GetInt32());
    }
}
