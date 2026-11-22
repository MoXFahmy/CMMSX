using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The responsiveness report (0.14.0): each request and work order against its targets, and averages by priority and trade.</summary>
public class ResponsivenessTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Period = "from=2026-01-01&to=2026-12-31";
    private static DateTimeOffset At(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed record Setup(Loop.Org Org, Guid Met, Guid Late, Guid Pending, Guid Imported);

    private async Task<Setup> SetupAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var t1 = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "TRA", name = "Trade A", isActive = true })));
        var t2 = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "TRB", name = "Trade B", isActive = true })));
        var type = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "RT", name = "Type", isActive = true, defaultTradeId = t1 })));
        return await DbAsync(fx, async db =>
        {
            var met = new WorkRequest { TenantId = org.Tenant, Number = $"R-{Guid.NewGuid():N}"[..20], Title = "Met", SiteId = org.Site, Priority = WorkPriority.High,
                Status = WorkRequestStatus.Rejected, RespondBy = At(3, 1, 10), DecidedAt = At(3, 1, 9), RequestTypeId = type };
            var imported = new WorkRequest { TenantId = org.Tenant, Number = $"R-{Guid.NewGuid():N}"[..20], Title = "Imported", SiteId = org.Site, Priority = WorkPriority.Low,
                Status = WorkRequestStatus.Approved, RespondBy = At(3, 3, 10) };
            var late = new WorkOrder { TenantId = org.Tenant, Number = $"W-{Guid.NewGuid():N}"[..20], Title = "Late", SiteId = org.Site, Priority = WorkPriority.High, TradeId = t2,
                Status = WorkOrderStatus.Completed, StartBy = At(3, 2, 10), RespondedAt = At(3, 2, 12), DueAt = At(3, 5, 10), CompletedAt = At(3, 4, 10) };
            var pending = new WorkOrder { TenantId = org.Tenant, Number = $"W-{Guid.NewGuid():N}"[..20], Title = "Pending", SiteId = org.Site, Priority = WorkPriority.Medium,
                Status = WorkOrderStatus.Open, StartBy = At(12, 30, 10) };
            db.WorkRequests.AddRange(met, imported); db.WorkOrders.AddRange(late, pending);
            await db.SaveChangesAsync();
            return new Setup(org, met.Id, late.Id, pending.Id, imported.Id);
        });
    }

    private static string? Text(JsonElement row, string key) => row.GetProperty(key).ValueKind == JsonValueKind.String ? row.GetProperty(key).GetString() : null;

    [SkippableFact]
    public async Task Responsiveness_lists_each_order_with_its_outcomes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var rows = (await Ok(await s.Org.Admin.GetAsync($"/api/reports/responsiveness?{Period}"))).GetProperty("rows").EnumerateArray().ToList();
        JsonElement Of(Guid id) => rows.Single(r => (r.TryGetProperty("requestId", out var q) && q.ValueKind == JsonValueKind.String && q.GetGuid() == id)
            || (r.TryGetProperty("workOrderId", out var w) && w.ValueKind == JsonValueKind.String && w.GetGuid() == id));
        Assert.Equal(4, rows.Count);
        Assert.Equal("Met", Text(Of(s.Met), "response"));
        Assert.Equal(("Missed", "Met"), (Text(Of(s.Late), "response"), Text(Of(s.Late), "completion")));
        Assert.Equal("Pending", Text(Of(s.Pending), "response"));
        Assert.Null(Text(Of(s.Imported), "response")); // finished with no time recorded: not judged
        Assert.Equal((At(3, 2, 10), At(3, 2, 12)), (Of(s.Late).GetProperty("respondBy").GetDateTimeOffset(), Of(s.Late).GetProperty("respondedAt").GetDateTimeOffset()));
    }

    [SkippableFact]
    public async Task Responsiveness_groups_average_by_priority_and_trade()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var byPriority = (await Ok(await s.Org.Admin.GetAsync($"/api/reports/responsiveness?{Period}&groupBy=priority"))).GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("group").GetString()!, r => r);
        Assert.Equal(50m, byPriority["High"].GetProperty("responsePct").GetDecimal());
        var byTrade = (await Ok(await s.Org.Admin.GetAsync($"/api/reports/responsiveness?{Period}&groupBy=trade"))).GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("group").GetString()!, r => r.GetProperty("orders").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["Trade A"] = 1, ["Trade B"] = 1, ["No trade"] = 2 }, byTrade);
    }
}
