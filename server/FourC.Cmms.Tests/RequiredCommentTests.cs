using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Notifications;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class RequiredCommentTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Task RequireAsync(HttpClient admin, params string[] actions) =>
        TestSettings.SetSettingsAsync(admin, s => s["commentRequiredFor"] = new JsonArray(actions.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()));

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Loop.Org org, Guid[]? assignees = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Fix fan", type = "Corrective", priority = "Medium", siteId = org.Site, locationId = org.Location, assigneeIds = assignees })));

    private static Task<HttpResponseMessage> StatusAsync(HttpClient c, Guid wo, object body) => c.PostAsJsonAsync($"/api/work-orders/{wo}/status", body);

    private static async Task<Guid> ReasonAsync(HttpClient c, string code) =>
        (await Ok(await c.GetAsync("/api/cancel-reasons"))).EnumerateArray().First(r => r.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    [SkippableFact]
    public async Task The_hold_note_follows_the_setting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var first = await WorkOrderAsync(org.Admin, org);
        await Ok(await StatusAsync(org.Admin, first, new { status = "OnHold", holdReason = "WaitingForParts" }));
        await RequireAsync(org.Admin, "hold");
        var second = await WorkOrderAsync(org.Admin, org);
        Assert.Contains("Say why the work is on hold.", await Bad(await StatusAsync(org.Admin, second, new { status = "OnHold", holdReason = "WaitingForParts" })));
        await Ok(await StatusAsync(org.Admin, second, new { status = "OnHold", holdReason = "WaitingForParts", note = "Bearing on order" }));
    }

    [SkippableFact]
    public async Task The_reopen_note_follows_the_setting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = await WorkOrderAsync(org.Admin, org);
        await Ok(await StatusAsync(org.Admin, wo, new { status = "Completed", completionNotes = "Done" }));
        await RequireAsync(org.Admin, "reopen");
        Assert.Contains("Say why the work is reopened.", await Bad(await StatusAsync(org.Admin, wo, new { status = "InProgress" })));
        await Ok(await StatusAsync(org.Admin, wo, new { status = "InProgress", note = "Still noisy" }));
    }

    [SkippableFact]
    public async Task Cancelling_needs_a_reason_from_the_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = await WorkOrderAsync(org.Admin, org);
        Assert.Contains("Choose why it is cancelled.", await Bad(await StatusAsync(org.Admin, wo, new { status = "Cancelled" })));
        var dup = await ReasonAsync(org.Admin, "DUP");
        await Ok(await StatusAsync(org.Admin, wo, new { status = "Cancelled", cancelReasonId = dup }));
        var detail = await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal("DUP", detail.GetProperty("cancelReason").GetProperty("code").GetString());
        var list = await Ok(await org.Admin.GetAsync($"/api/work-orders?cancelReasonId={dup}&pageSize=50"));
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Duplicate", row.GetProperty("cancelReasonName").GetString());

        await Ok(await StatusAsync(org.Admin, wo, new { status = "Open" }));
        Assert.Equal(JsonValueKind.Null, (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("cancelReason").ValueKind);

        // With no active reason in the list, cancelling needs none.
        foreach (var r in (await Ok(await org.Admin.GetAsync("/api/cancel-reasons"))).EnumerateArray())
            await Ok(await org.Admin.PutAsJsonAsync($"/api/cancel-reasons/{Id(r)}", new { code = r.GetProperty("code").GetString(), name = r.GetProperty("name").GetString(), isActive = false }));
        await Ok(await StatusAsync(org.Admin, wo, new { status = "Cancelled" }));
    }

    [SkippableFact]
    public async Task The_cancel_note_follows_the_setting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = await WorkOrderAsync(org.Admin, org);
        await RequireAsync(org.Admin, "cancel");
        var other = await ReasonAsync(org.Admin, "OTHER");
        Assert.Contains("Say why the work is cancelled.", await Bad(await StatusAsync(org.Admin, wo, new { status = "Cancelled", cancelReasonId = other })));
        await Ok(await StatusAsync(org.Admin, wo, new { status = "Cancelled", cancelReasonId = other, note = "Customer moved out" }));
    }

    [SkippableFact]
    public async Task The_reject_reason_follows_the_setting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Guid NewRequest(JsonElement e) => Id(e);
        var first = NewRequest(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "A", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        await Bad(await org.Admin.PostAsJsonAsync($"/api/work-requests/{first}/reject", new { }));
        await RequireAsync(org.Admin);
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{first}/reject", new { }));
    }

    [SkippableFact]
    public async Task Bulk_cancel_needs_a_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await WorkOrderAsync(org.Admin, org);
        var b = await WorkOrderAsync(org.Admin, org);
        var skipped = await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { a, b }, status = new { status = "Cancelled" } }));
        Assert.Equal(0, skipped.GetProperty("updated").GetInt32());
        Assert.All(skipped.GetProperty("skipped").EnumerateArray(), s => Assert.Equal("Choose why it is cancelled.", s.GetProperty("reason").GetString()));
        var dup = await ReasonAsync(org.Admin, "DUP");
        var done = await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { a, b }, status = new { status = "Cancelled", cancelReasonId = dup } }));
        Assert.Equal(2, done.GetProperty("updated").GetInt32());
    }

    [SkippableFact]
    public async Task The_phone_hold_needs_a_note_when_required()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = await WorkOrderAsync(org.Admin, org, [me.Id]);
        await RequireAsync(org.Admin, "hold");
        var bundle = await Ok(await org.Admin.GetAsync("/api/tech/bundle"));
        Assert.True(bundle.GetProperty("completionRules").GetProperty("holdNote").GetBoolean());
        var answer = await Ok(await org.Admin.PostAsJsonAsync("/api/tech/actions", new
        {
            clientId = Guid.NewGuid(), at = DateTimeOffset.UtcNow, type = "status.change", workOrderId = wo, payload = new { status = "OnHold", holdReason = "WaitingForParts" },
        }));
        Assert.Equal("rejected", answer.GetProperty("status").GetString());
        Assert.Contains("Say why the work is on hold.", answer.GetProperty("message").GetString());
    }

    [SkippableFact]
    public async Task The_cancelled_email_goes_to_technicians_and_the_requester()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var tech = await EmployeeAsync(org.Admin, "T1");
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var request = Id(await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Blind broken", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        var wo = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { assigneeIds = new[] { tech.Id } }))).GetProperty("workOrderId").GetGuid();
        await Ok(await StatusAsync(org.Admin, wo, new { status = "Cancelled", cancelReasonId = await ReasonAsync(org.Admin, "NOTNEEDED"), note = "Replaced instead" }));
        Assert.Contains(await RowsAsync(fx, tech.Email), e => e.EventKey == EmailEvents.WorkOrderCancelled);
        var mail = Assert.Single(await RowsAsync(fx, requester.Email), e => e.EventKey == EmailEvents.WorkOrderCancelled);
        Assert.Contains("No longer needed", mail.TextBody);
    }
}
