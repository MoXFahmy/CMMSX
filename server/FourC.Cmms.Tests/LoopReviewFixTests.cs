using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Fixes from the 0.10.0 final review.</summary>
public class LoopReviewFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> RequestAsync(HttpClient c, Loop.Org org, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["title"] = "Leaking tap", ["siteId"] = org.Site, ["locationId"] = org.Location, ["priority"] = "Low", ["type"] = "Corrective" };
        if (extra is not null) foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
        return Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", body)));
    }

    private static async Task<Guid> ApproveAndCompleteAsync(HttpClient admin, Guid request)
    {
        var wo = (await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Fixed" }));
        return wo;
    }

    [SkippableFact]
    public async Task Someone_who_logs_a_request_for_a_caller_is_not_the_requester()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var caller = Addr("caller");
        var id = await RequestAsync(org.Admin, org, new { requesterName = "Caller", requesterEmail = caller });
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "We will come on Monday." }));
        var thread = (await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}/comments"))).EnumerateArray().ToList();
        Assert.False(thread[0].GetProperty("byRequester").GetBoolean());
        Assert.Single(await RowsAsync(fx, caller), r => r.EventKey == EmailEvents.RequestComment);

        await ApproveAndCompleteAsync(org.Admin, id);
        var r = await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"));
        Assert.True(r.GetProperty("awaitingFeedback").GetBoolean());
        Assert.False(r.GetProperty("canAnswer").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 5 })).StatusCode);
    }

    [SkippableFact]
    public async Task Nobody_is_asked_when_nobody_can_answer()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await RequestAsync(org.Admin, org);
        // As an imported request: no contact address and nobody who raised it in CMX.
        await DbAsync(fx, async db =>
        {
            var req = await db.WorkRequests.IgnoreQueryFilters().FirstAsync(x => x.Id == id);
            req.RequesterEmail = null; req.CreatedById = null;
            return await db.SaveChangesAsync();
        });
        await ApproveAndCompleteAsync(org.Admin, id);
        Assert.False((await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"))).GetProperty("awaitingFeedback").GetBoolean());
    }

    [SkippableFact]
    public async Task Auto_approved_work_has_no_issuer()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "AIRC", name = "Air con", isActive = true })));
        var type = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "AC", name = "AC", isActive = true, autoApprove = true, defaultTradeId = trade })));
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org, new { requestTypeId = type });
        var wo = (await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"))).GetProperty("workOrder").GetProperty("id").GetGuid();
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" }));
        var mine = await RowsAsync(fx, requester.Email);
        Assert.Contains(mine, e => e.EventKey == EmailEvents.RequestCompleted);
        Assert.DoesNotContain(mine, e => e.EventKey == EmailEvents.WorkOrderCompleted);
    }

    [SkippableFact]
    public async Task Deleting_the_work_order_ends_the_wait_and_the_link()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var caller = Addr("caller");
        var id = await RequestAsync(org.Admin, org, new { requesterName = "Caller", requesterEmail = caller });
        var wo = await ApproveAndCompleteAsync(org.Admin, id);
        var mail = (await RowsAsync(fx, caller)).Single(r => r.EventKey == EmailEvents.RequestCompleted);
        var token = Regex.Match(mail.TextBody, "/feedback/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.NoContent, (await org.Admin.DeleteAsync($"/api/work-orders/{wo}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fx.Client().GetAsync($"/api/public/feedback/{token}")).StatusCode);
        var r = await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("New", r.GetProperty("status").GetString());
        Assert.False(r.GetProperty("awaitingFeedback").GetBoolean());
    }

    [SkippableFact]
    public async Task Old_backlog_does_not_hold_up_new_overdue_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        await DbAsync(fx, async db =>
        {
            for (var i = 0; i < OverdueNotifier.MaxPerRun + 50; i++)
                db.WorkOrders.Add(new WorkOrder
                {
                    TenantId = org.Tenant, Number = $"OLD-{i:00000}", Title = "Old", SiteId = org.Site, Status = WorkOrderStatus.Open,
                    DueAt = DateTimeOffset.UtcNow.AddDays(-30 - i),
                });
            return await db.SaveChangesAsync();
        });
        Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Just late", siteId = org.Site, locationId = org.Location, priority = "High", type = "Corrective", assigneeIds = new[] { tech.Id }, dueAt = DateTimeOffset.UtcNow.AddHours(-1),
        })));
        var notifier = fx.Factory!.Services.GetRequiredService<OverdueNotifier>();
        await notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, org.Tenant);
        Assert.Single(await RowsAsync(fx, tech.Email), r => r.EventKey == EmailEvents.WorkOrderOverdue);
    }

    [SkippableFact]
    public async Task The_people_picker_only_answers_for_sites_the_person_sees()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var elsewhere = await UserAsync(fx, org.Admin, "Requester", siteIds: [other]);
        Assert.Empty((await Ok(await elsewhere.Client.GetAsync($"/api/work-requests/people?siteId={org.Site}"))).EnumerateArray());
        Assert.NotEmpty((await Ok(await elsewhere.Client.GetAsync($"/api/work-requests/people?siteId={other}"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task An_accepted_request_keeps_its_work_orders_deadline_and_state()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        var wo = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { dueAt = DateTimeOffset.UtcNow.AddDays(3) }))).GetProperty("workOrderId").GetGuid();
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Fixed" }));
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 5 }));

        var w = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"));
        var r = await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Closed", r.GetProperty("status").GetString());
        Assert.Equal(w.GetProperty("dueAt").GetDateTimeOffset(), r.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal("Met", r.GetProperty("sla").GetProperty("completion").GetString());
        var row = (await Ok(await org.Admin.GetAsync("/api/work-requests?pageSize=50"))).GetProperty("items").EnumerateArray().Single(x => Id(x) == id);
        Assert.Equal("Met", row.GetProperty("completionState").GetString());
    }
}
