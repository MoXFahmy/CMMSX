using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Notifications;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class EmailGridTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Task SetKindsAsync(HttpClient admin, string eventKey, params string[] kinds) =>
        TestSettings.SetSettingsAsync(admin, s =>
        {
            var map = s["emailRecipients"] as JsonObject ?? new JsonObject();
            map[eventKey] = new JsonArray(kinds.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());
            s["emailRecipients"] = map;
        });

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Loop.Org org, Guid[] assignees, Guid? requestId = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Fix it", type = "Corrective", priority = "High", siteId = org.Site, locationId = org.Location, assigneeIds = assignees })));

    [SkippableFact]
    public async Task Status_lists_the_grid()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var status = await Ok(await org.Admin.GetAsync("/api/email/status"));
        var row = status.GetProperty("grid").EnumerateArray().Single(r => r.GetProperty("key").GetString() == EmailEvents.WorkOrderCompleted);
        Assert.Equal(["requestRecipients", "workRecipients", "issuer"], row.GetProperty("applicable").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(["workRecipients", "issuer"], row.GetProperty("defaults").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.DoesNotContain(status.GetProperty("grid").EnumerateArray(), r => r.GetProperty("key").GetString()!.StartsWith("po."));
    }

    [SkippableFact]
    public async Task The_requester_gets_a_receipt_when_switched_on()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        await SetKindsAsync(org.Admin, EmailEvents.RequestSubmitted, EmailRecipientKinds.RequestRecipients, EmailRecipientKinds.Requester);
        var someone = Addr("caller");
        await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new
        {
            title = "Door stuck", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", requesterName = "Caller", requesterEmail = someone,
        }));
        Assert.Contains(await RowsAsync(fx, someone), r => r.EventKey == EmailEvents.RequestSubmitted);
    }

    [SkippableFact]
    public async Task Switching_the_issuer_off_stops_the_completed_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var manager = await UserAsync(fx, org.Admin, "Maintenance Manager");
        var first = await WorkOrderAsync(manager.Client, org, []);
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{first}/status", new { status = "Completed", completionNotes = "Done" }));
        Assert.Single(await RowsAsync(fx, manager.Email), r => r.EventKey == EmailEvents.WorkOrderCompleted);

        await SetKindsAsync(org.Admin, EmailEvents.WorkOrderCompleted);
        var second = await WorkOrderAsync(manager.Client, org, []);
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{second}/status", new { status = "Completed", completionNotes = "Done" }));
        Assert.Single(await RowsAsync(fx, manager.Email), r => r.EventKey == EmailEvents.WorkOrderCompleted);
    }

    [SkippableFact]
    public async Task The_contractors_column_separates_contractors()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var inHouse = await EmployeeAsync(org.Admin, "IN1");
        var outside = await EmployeeAsync(org.Admin, "OUT1", contractor: true);
        await SetKindsAsync(org.Admin, EmailEvents.WorkOrderAssigned, EmailRecipientKinds.Technicians);
        await WorkOrderAsync(org.Admin, org, [inHouse.Id, outside.Id]);
        Assert.Single(await RowsAsync(fx, inHouse.Email), r => r.EventKey == EmailEvents.WorkOrderAssigned);
        Assert.Empty(await RowsAsync(fx, outside.Email));

        await SetKindsAsync(org.Admin, EmailEvents.WorkOrderAssigned, EmailRecipientKinds.Contractors);
        await WorkOrderAsync(org.Admin, org, [inHouse.Id, outside.Id]);
        Assert.Single(await RowsAsync(fx, inHouse.Email), r => r.EventKey == EmailEvents.WorkOrderAssigned);
        Assert.Single(await RowsAsync(fx, outside.Email), r => r.EventKey == EmailEvents.WorkOrderAssigned);
    }

    [SkippableFact]
    public async Task The_grid_still_filters_by_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var here = await UserAsync(fx, org.Admin, "Maintenance Manager");
        var elsewhere = await UserAsync(fx, org.Admin, "Maintenance Manager", siteIds: [other]);
        var type = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/request-types",
            new { code = "AC", name = "AC", isActive = true, autoApprove = false, recipientIds = new[] { here.Id, elsewhere.Id } })));
        await SetKindsAsync(org.Admin, EmailEvents.WorkOrderCompleted, EmailRecipientKinds.Issuer, EmailRecipientKinds.RequestRecipients);
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests",
            new { title = "Warm room", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", requestTypeId = type })));
        var approved = await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{approved.GetProperty("workOrderId").GetGuid()}/status", new { status = "Completed", completionNotes = "Done" }));
        Assert.Single(await RowsAsync(fx, here.Email), r => r.EventKey == EmailEvents.WorkOrderCompleted);
        Assert.DoesNotContain(await RowsAsync(fx, elsewhere.Email), r => r.EventKey == EmailEvents.WorkOrderCompleted);
    }
}
