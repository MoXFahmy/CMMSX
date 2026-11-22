using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Notifications;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class RequestCommentTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> RequestAsync(HttpClient c, Loop.Org org) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", new { title = "Tap drips", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));

    private static async Task<List<JsonElement>> ThreadAsync(HttpClient c, Guid request) =>
        (await Ok(await c.GetAsync($"/api/work-requests/{request}/comments"))).EnumerateArray().ToList();

    [SkippableFact]
    public async Task The_requester_and_maintenance_share_the_thread()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester", name: "Rana");
        var id = await RequestAsync(requester.Client, org);
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "It is getting worse." }));
        Assert.Single(await ThreadAsync(org.Admin, id));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "Coming tomorrow." }));
        var thread = await ThreadAsync(requester.Client, id);
        Assert.Equal(["It is getting worse.", "Coming tomorrow."], thread.Select(c => c.GetProperty("body").GetString()!).ToArray());
        Assert.True(thread[0].GetProperty("byRequester").GetBoolean());
        Assert.False(thread[1].GetProperty("byRequester").GetBoolean());
        Assert.Equal("Rana", thread[0].GetProperty("userName").GetString());
    }

    [SkippableFact]
    public async Task Other_requesters_cannot_see_the_thread()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var stranger = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/work-requests/{id}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "Hi" })).StatusCode);
    }

    [SkippableFact]
    public async Task A_comment_goes_to_the_other_side()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var recipient = await UserAsync(fx, org.Admin, "Maintenance Manager");
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { requestRecipientIds = new[] { recipient.Id } });
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);

        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "Any news?" }));
        Assert.Single(await RowsAsync(fx, recipient.Email), r => r.EventKey == EmailEvents.RequestComment);
        Assert.Empty((await RowsAsync(fx, requester.Email)).Where(r => r.EventKey == EmailEvents.RequestComment));

        await Ok(await recipient.Client.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "Parts ordered." }));
        Assert.Single(await RowsAsync(fx, requester.Email), r => r.EventKey == EmailEvents.RequestComment);
        Assert.Single(await RowsAsync(fx, recipient.Email), r => r.EventKey == EmailEvents.RequestComment);
    }

    [SkippableFact]
    public async Task The_work_order_shows_the_request_comments_read_only()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org);
        await Ok(await requester.Client.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = "Under the sink." }));
        var wo = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        var tech = await UserAsync(fx, org.Admin, "Technician");
        var detail = await Ok(await tech.Client.GetAsync($"/api/work-orders/{wo}"));
        var comments = detail.GetProperty("request").GetProperty("comments").EnumerateArray().ToList();
        Assert.Single(comments);
        Assert.Equal("Under the sink.", comments[0].GetProperty("body").GetString());
    }

    [SkippableFact]
    public async Task Empty_or_long_comments_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await RequestAsync(org.Admin, org);
        await Bad(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = " " }));
        await Bad(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/comments", new { body = new string('x', 4001) }));
    }
}
