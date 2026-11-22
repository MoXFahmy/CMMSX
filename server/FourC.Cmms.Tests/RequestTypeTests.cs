using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Notifications;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class RequestTypeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> TypeAsync(HttpClient c, string code, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = code, ["name"] = $"Type {code}", ["isActive"] = true, ["autoApprove"] = false };
        if (extra is not null)
            foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
        return Id(await Ok(await c.PostAsJsonAsync("/api/request-types", body)));
    }

    private static async Task<Guid> RequestAsync(HttpClient c, Loop.Org org, object extra)
    {
        var body = new Dictionary<string, object?> { ["title"] = "No cooling", ["siteId"] = org.Site, ["locationId"] = org.Location, ["priority"] = "Medium", ["type"] = "Corrective" };
        foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[p.Name] = p.Value;
        return Id(await Ok(await c.PostAsJsonAsync("/api/work-requests", body)));
    }

    [SkippableFact]
    public async Task Request_types_can_be_listed_created_and_protected()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "AIRC", name = "Air con", isActive = true })));
        var ac = await TypeAsync(org.Admin, "AC", new { defaultTradeId = trade, defaultPriority = "High", recipientIds = new[] { org.AdminId } });
        var list = await Ok(await org.Admin.GetAsync("/api/request-types"));
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ac);
        Assert.Equal("High", row.GetProperty("defaultPriority").GetString());
        Assert.Equal(trade, row.GetProperty("defaultTradeId").GetGuid());

        await Bad(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "AC", name = "Again", isActive = true, autoApprove = false }));
        Assert.Contains("Choose the trade or team the work goes to.",
            await Bad(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "IT", name = "IT", isActive = true, autoApprove = true })));
        await Bad(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "PL", name = "Plumbing", isActive = true, autoApprove = false, recipientIds = new[] { Guid.NewGuid() } }));

        var tech = await UserAsync(fx, org.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.PostAsJsonAsync("/api/request-types", new { code = "X", name = "X", isActive = true, autoApprove = false })).StatusCode);
        Assert.Contains(ac, (await Ok(await tech.Client.GetAsync("/api/request-types"))).EnumerateArray().Select(Id));

        await RequestAsync(org.Admin, org, new { requestTypeId = ac });
        Assert.Contains("in use", await Bad(await org.Admin.DeleteAsync($"/api/request-types/{ac}")));
        var unused = await TypeAsync(org.Admin, "UN");
        Assert.Equal(HttpStatusCode.NoContent, (await org.Admin.DeleteAsync($"/api/request-types/{unused}")).StatusCode);
    }

    [SkippableFact]
    public async Task Type_and_extra_recipients_are_added_to_the_site_recipients()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var a = await UserAsync(fx, org.Admin, "Maintenance Manager", name: "Site person");
        var b = await UserAsync(fx, org.Admin, "Maintenance Manager", name: "Type person");
        var c = await UserAsync(fx, org.Admin, "Maintenance Manager", name: "Extra person");
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { requestRecipientIds = new[] { a.Id } });
        var type = await TypeAsync(org.Admin, "AC", new { recipientIds = new[] { b.Id } });
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var id = await RequestAsync(requester.Client, org, new { requestTypeId = type, extraRecipientIds = new[] { c.Id } });

        var rows = await RowsAsync(fx, a.Email, b.Email, c.Email, requester.Email, org.AdminEmail);
        Assert.Equal(new[] { a.Email, b.Email, c.Email }.Order(), rows.Where(r => r.EventKey == EmailEvents.RequestSubmitted).Select(r => r.ToAddress).Order());
        var detail = await Ok(await requester.Client.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Type AC", detail.GetProperty("requestType").GetProperty("name").GetString());
        Assert.Equal(c.Id, detail.GetProperty("extraRecipients")[0].GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task Managers_still_hear_when_only_extra_recipients_are_set()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var c = await UserAsync(fx, org.Admin, "Technician", name: "Extra person");
        var requester = await UserAsync(fx, org.Admin, "Requester");
        await RequestAsync(requester.Client, org, new { extraRecipientIds = new[] { c.Id } });
        var rows = await RowsAsync(fx, org.AdminEmail, c.Email);
        Assert.Equal(new[] { org.AdminEmail, c.Email }.Order(), rows.Where(r => r.EventKey == EmailEvents.RequestSubmitted).Select(r => r.ToAddress).Order());
    }

    [SkippableFact]
    public async Task An_extra_recipient_must_see_the_place()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var elsewhere = await UserAsync(fx, org.Admin, "Maintenance Manager", siteIds: [other]);
        var here = await UserAsync(fx, org.Admin, "Maintenance Manager");
        var requester = await UserAsync(fx, org.Admin, "Requester");
        Assert.Contains("Choose people who can see this place.", await Bad(await requester.Client.PostAsJsonAsync("/api/work-requests",
            new { title = "x", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", extraRecipientIds = new[] { elsewhere.Id } })));
        var people = (await Ok(await requester.Client.GetAsync($"/api/work-requests/people?siteId={org.Site}"))).EnumerateArray().Select(Id).ToList();
        Assert.Contains(here.Id, people);
        Assert.DoesNotContain(elsewhere.Id, people);
    }

    [SkippableFact]
    public async Task Auto_approve_creates_the_work_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "AIRC", name = "Air con", isActive = true })));
        var team = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/teams", new { code = "T2", name = "Team elsewhere", siteId = other, memberIds = Array.Empty<Guid>(), isActive = true })));
        var type = await TypeAsync(org.Admin, "AC", new { autoApprove = true, defaultTradeId = trade, teamId = team });
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var id = await RequestAsync(requester.Client, org, new
        {
            requestTypeId = type, priority = "High",
            accessWindows = new[] { new { start, end = start.AddHours(3), note = "Room empty" } },
        });

        var r = await Ok(await org.Admin.GetAsync($"/api/work-requests/{id}"));
        Assert.Equal("Approved", r.GetProperty("status").GetString());
        Assert.StartsWith("Approved automatically", r.GetProperty("decisionNote").GetString());
        var wo = await Ok(await org.Admin.GetAsync($"/api/work-orders/{r.GetProperty("workOrder").GetProperty("id").GetGuid()}"));
        Assert.Equal(trade, wo.GetProperty("tradeId").GetGuid());
        Assert.Equal(JsonValueKind.Null, wo.GetProperty("teamId").ValueKind);
        Assert.Equal("High", wo.GetProperty("priority").GetString());
        Assert.Equal("Room empty", wo.GetProperty("accessWindows")[0].GetProperty("note").GetString());
        Assert.Contains(await RowsAsync(fx, requester.Email), e => e.EventKey == EmailEvents.RequestApproved);
    }

    [SkippableFact]
    public async Task A_phone_request_of_an_auto_type_is_approved()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "AIRC", name = "Air con", isActive = true })));
        var type = await TypeAsync(org.Admin, "AC", new { autoApprove = true, defaultTradeId = trade });
        var answer = await Ok(await org.Admin.PostAsJsonAsync("/api/tech/actions", new
        {
            clientId = Guid.NewGuid(), at = DateTimeOffset.UtcNow, type = "request.create", workOrderId = (Guid?)null,
            payload = new { title = "Hot office", siteId = org.Site, locationId = org.Location, priority = "Medium", requestTypeId = type },
        }));
        Assert.Equal("done", answer.GetProperty("status").GetString());
        var list = await Ok(await org.Admin.GetAsync("/api/work-requests?pageSize=50"));
        var row = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("title").GetString() == "Hot office");
        Assert.Equal("Approved", row.GetProperty("status").GetString());
        Assert.Equal("Type AC", row.GetProperty("requestTypeName").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("workOrderId").ValueKind);
    }
}
