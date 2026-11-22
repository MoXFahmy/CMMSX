using System.Net.Http.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Ck13;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>
/// Answers and direct readings save in one transaction (0.13.0): when the last save fails, nothing of the answer, its follow-up or an automatic start
/// stays behind. A temporary check constraint on one record makes that save fail (a trigger would clash with EF's OUTPUT clause).
/// </summary>
public class AnswerTransactionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<T> FailingAsync<T>(string table, string check, Func<Task<T>> work)
    {
        var name = $"CK_test_{Guid.NewGuid():N}";
        await DbAsync(fx, db => db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] WITH NOCHECK ADD CONSTRAINT [{name}] CHECK ({check})"));
        try { return await work(); }
        finally { await DbAsync(fx, db => db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] DROP CONSTRAINT [{name}]")); }
    }

    private async Task<(Loop.Org Org, Guid Asset, Guid Wo, List<WorkOrderTask> Steps)> WorkAsync(Loop.Org org, object[] steps)
    {
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var t = await TypedTemplateAsync(org.Admin, "T1", steps);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, assetId = asset, priority = "Low", type = "Preventive", templateId = t,
        })));
        return (org, asset, wo, await DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(x => x.WorkOrderId == wo).OrderBy(x => x.Sequence).ToListAsync()));
    }

    [SkippableFact]
    public async Task A_failed_save_leaves_no_follow_up_behind()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, wo, steps) = await WorkAsync(await OrgAsync(fx, emailOn: false), [new { description = "Belt", type = "Check" }]);
        var step = steps[0].Id;
        var put = () => org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{step}", new { answer = "NotOk" });
        var failed = await FailingAsync("WorkOrderTasks", $"FollowUpRequestId IS NULL OR WorkOrderId <> '{wo}'", put);
        Assert.False(failed.IsSuccessStatusCode);
        var (requests, answer) = await DbAsync(fx, async db => (
            await db.WorkRequests.IgnoreQueryFilters().CountAsync(r => r.SourceTaskId == step),
            (await db.WorkOrderTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == step)).Answer));
        Assert.Equal((0, null), (requests, answer));
        await Ok(await put());
        Assert.Equal(1, await DbAsync(fx, db => db.WorkRequests.IgnoreQueryFilters().CountAsync(r => r.SourceTaskId == step)));
    }

    [SkippableFact]
    public async Task A_failed_all_ok_doesnt_start_the_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await TestSettings.SetSettingsAsync(org.Admin, x => x["autoStartWork"] = true);
        var (_, _, wo, _) = await WorkAsync(org, [new { description = "Belt", type = "Check", followUp = "None" }, new { description = "Filter", type = "Check", followUp = "None" }]);
        var failed = await FailingAsync("WorkOrderTasks", $"Answer IS NULL OR WorkOrderId <> '{wo}'",
            () => org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/tasks/all-ok", new { }));
        Assert.False(failed.IsSuccessStatusCode);
        var (status, answered) = await DbAsync(fx, async db => (
            await db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == wo).Select(w => w.Status).SingleAsync(),
            await db.WorkOrderTasks.IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == wo && x.Answer != null)));
        Assert.Equal((WorkOrderStatus.Open, 0), (status, answered));
    }

    [SkippableFact]
    public async Task A_failed_direct_reading_leaves_nothing_behind()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var point = await PointAsync(org.Admin, "SAT");
        var failed = await FailingAsync("MeasurementReadings", $"FollowUpRequestId IS NULL OR PointId <> '{point}'",
            () => org.Admin.PostAsJsonAsync($"/api/measurement-points/{point}/readings", new { assetId = asset, value = 30 }));
        Assert.False(failed.IsSuccessStatusCode);
        var (readings, requests) = await DbAsync(fx, async db => (
            await db.MeasurementReadings.IgnoreQueryFilters().CountAsync(r => r.PointId == point),
            await db.WorkRequests.IgnoreQueryFilters().CountAsync(r => r.AssetId == asset)));
        Assert.Equal((0, 0), (readings, requests));
    }
}
