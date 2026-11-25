using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class RequestFeedbackTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> RequestAsync(HttpClient c, Loop.Org org, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["title"] = "Light flickers", ["siteId"] = org.Site, ["locationId"] = org.Location, ["priority"] = "Low", ["type"] = "Corrective" };
        if (extra is not null) foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
        return Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", body)));
    }

    /// <summary>Approves the request (assigning <paramref name="assignee"/>) and completes its work order; returns the work order.</summary>
    private static async Task<Guid> ApproveAndCompleteAsync(HttpClient admin, Guid request, Guid? assignee = null)
    {
        var wo = (await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { assigneeIds = assignee is { } a ? new[] { a } : null }))).GetProperty("workOrderId").GetGuid();
        await CompleteAsync(admin, wo);
        return wo;
    }

    private static async Task CompleteAsync(HttpClient admin, Guid wo) =>
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Replaced the starter" }));

    private static async Task<string> TokenAsync(ApiFixture fx, string to)
    {
        var mail = (await RowsAsync(fx, to)).Where(r => r.EventKey == EmailEvents.RequestCompleted).OrderBy(r => r.CreatedAt).Last();
        return Regex.Match(mail.TextBody, "/feedback/([A-Za-z0-9_-]+)").Groups[1].Value;
    }

    [SkippableFact]
    public async Task The_creator_accepts_on_the_request_page()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var requester = await UserAsync(fx, org.Admin, "Requester", name: "Rana");
        var id = await RequestAsync(requester.Client, org);
        var wo = await ApproveAndCompleteAsync(org.Admin, id);

        var mine = await Ok(await requester.Client.GetAsync($"/api/work-requests/{id}"));
        Assert.True(mine.GetProperty("awaitingFeedback").GetBoolean());
        Assert.True(mine.GetProperty("canAnswer").GetBoolean());
        Assert.False((await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"))).GetProperty("canAnswer").GetBoolean());
        // A requester with a login answers in CMX: the e-mail links to the request page, not to a one-time link.
        var mail = (await RowsAsync(fx, requester.Email)).Single(r => r.EventKey == EmailEvents.RequestCompleted);
        Assert.DoesNotContain("/feedback/", mail.TextBody);
        Assert.Equal(HttpStatusCode.Forbidden, (await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 5 })).StatusCode);

        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 4, comment = "Thanks" }));
        var after = await Ok(await requester.Client.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Closed", after.GetProperty("status").GetString());
        Assert.False(after.GetProperty("awaitingFeedback").GetBoolean());
        var answer = after.GetProperty("feedback").EnumerateArray().Single();
        Assert.True(answer.GetProperty("accepted").GetBoolean());
        Assert.Equal("Rana", answer.GetProperty("byName").GetString());
        var detail = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal(4, detail.GetProperty("request").GetProperty("feedback").GetProperty("rating").GetInt32());
        await Bad(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 5 }));
    }

    [SkippableFact]
    public async Task Accepting_needs_a_rating_from_one_to_five()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        await ApproveAndCompleteAsync(org.Admin, id);
        await Bad(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true }));
        await Bad(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 6 }));
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = true, rating = 1 }));
    }

    [SkippableFact]
    public async Task Refusing_reopens_the_work_with_the_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var requester = await UserAsync(fx, org.Admin, "Requester", name: "Rana");
        var id = await RequestAsync(requester.Client, org);
        var wo = await ApproveAndCompleteAsync(org.Admin, id, tech.Id);

        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = false, rating = 2, comment = "Still flickers" }));
        var w = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal("InProgress", w.GetProperty("status").GetString());
        var activity = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}/activity"));
        Assert.Contains(activity.EnumerateArray(), a => a.GetProperty("body").GetString() == "Refused by Rana: Still flickers");
        var r = await Ok(await requester.Client.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Approved", r.GetProperty("status").GetString());
        Assert.False(r.GetProperty("awaitingFeedback").GetBoolean());
        Assert.Contains(await RowsAsync(fx, tech.Email), e => e.EventKey == EmailEvents.RequestRefused);
        Assert.Contains(await RowsAsync(fx, org.AdminEmail), e => e.EventKey == EmailEvents.RequestRefused);

        await CompleteAsync(org.Admin, wo);
        Assert.True((await Ok(await requester.Client.GetAsync($"/api/work-requests/{id}"))).GetProperty("awaitingFeedback").GetBoolean());
    }

    [SkippableFact]
    public async Task The_refusal_comment_follows_the_setting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        await ApproveAndCompleteAsync(org.Admin, id);
        await Bad(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = false }));
        await TestSettings.SetSettingsAsync(org.Admin, s => s["commentRequiredFor"] = new JsonArray("reject"));
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = false }));
    }

    [SkippableFact]
    public async Task A_refusal_reopens_closed_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        var wo = await ApproveAndCompleteAsync(org.Admin, id);
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Closed" }));
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/feedback", new { accepted = false, comment = "Not fixed" }));
        Assert.Equal("InProgress", (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task A_requester_without_a_login_gets_a_link()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var caller = Addr("caller");
        var id = await RequestAsync(org.Admin, org, new { requesterName = "Guest", requesterEmail = caller });
        await ApproveAndCompleteAsync(org.Admin, id, tech.Id);
        var token = await TokenAsync(fx, caller);
        Assert.NotEmpty(token);

        var anon = fx.Client();
        var page = await Ok(await anon.GetAsync($"/api/public/feedback/{token}"));
        Assert.Equal("Loop Co", page.GetProperty("organization").GetString());
        Assert.Equal("Light flickers", page.GetProperty("title").GetString());
        Assert.Equal("Replaced the starter", page.GetProperty("completionNotes").GetString());
        Assert.DoesNotContain("Tech T1", page.GetRawText());

        await Ok(await anon.PostAsJsonAsync($"/api/public/feedback/{token}", new { accepted = true, rating = 5, comment = "Great" }));
        var r = await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Closed", r.GetProperty("status").GetString());
        var answer = r.GetProperty("feedback").EnumerateArray().Single();
        Assert.True(answer.GetProperty("viaLink").GetBoolean());
        Assert.Equal("Guest", answer.GetProperty("byName").GetString());
    }

    [SkippableFact]
    public async Task Answer_page_does_not_return_the_requester_name()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var caller = Addr("caller");
        var id = await RequestAsync(org.Admin, org, new { requesterName = "Guest Person", requesterEmail = caller });
        await ApproveAndCompleteAsync(org.Admin, id);
        var token = await TokenAsync(fx, caller);

        var page = await Ok(await fx.Client().GetAsync($"/api/public/feedback/{token}"));
        // The page shows the title, place and outcome only: nothing that names the person who asked.
        Assert.False(page.TryGetProperty("requesterName", out _));
        Assert.DoesNotContain("Guest Person", page.GetRawText());
        Assert.DoesNotContain(caller, page.GetRawText());
        Assert.Equal("Light flickers", page.GetProperty("title").GetString());
    }

    [SkippableFact]
    public async Task The_link_is_single_use_and_voided_by_a_reopen()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var caller = Addr("caller");
        var anon = fx.Client();

        var first = await RequestAsync(org.Admin, org, new { requesterName = "Guest", requesterEmail = caller });
        await ApproveAndCompleteAsync(org.Admin, first);
        var token = await TokenAsync(fx, caller);
        await Ok(await anon.PostAsJsonAsync($"/api/public/feedback/{token}", new { accepted = false, comment = "No" }));
        var again = await anon.PostAsJsonAsync($"/api/public/feedback/{token}", new { accepted = true, rating = 5 });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Contains("This link was already used or has expired.", await again.Content.ReadAsStringAsync());

        var second = await RequestAsync(org.Admin, org, new { requesterName = "Guest", requesterEmail = caller });
        var wo = await ApproveAndCompleteAsync(org.Admin, second);
        var reopened = await TokenAsync(fx, caller);
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "InProgress", note = "Forgot the cover" }));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/feedback/{reopened}")).StatusCode);

        await CompleteAsync(org.Admin, wo);
        var fresh = await TokenAsync(fx, caller);
        Assert.NotEqual(reopened, fresh);
        await DbAsync(fx, async db =>
        {
            var r = await db.WorkRequests.IgnoreQueryFilters().FirstAsync(x => x.Id == second);
            r.FeedbackTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/feedback/{fresh}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/feedback/not-a-real-token")).StatusCode);
    }

    [SkippableFact]
    public async Task Work_without_a_request_asks_nobody()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Paint", type = "Corrective", priority = "Low", siteId = org.Site, locationId = org.Location })));
        await CompleteAsync(org.Admin, wo);
        var count = await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().CountAsync(e => e.TenantId == org.Tenant && e.EventKey == EmailEvents.RequestCompleted));
        Assert.Equal(0, count);
    }
}
