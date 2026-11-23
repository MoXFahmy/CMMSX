using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: raising, approving, rejecting and cancelling purchase requests; comments, files and e-mails.</summary>
public class PurchaseRequestTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    internal static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    internal static string Status(JsonElement e) => e.GetProperty("status").GetString()!;

    internal sealed record Org(HttpClient Admin, Guid Tenant, Guid Site, Guid Store, Guid Part, Guid Supplier);

    internal static async Task<Org> OrgAsync(ApiFixture fx, bool email = false)
    {
        var c = fx.Client();
        var me = await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Buyers Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var tenant = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        if (email) await TestSettings.SetSettingsAsync(c, s => s["emailEnabled"] = true);
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Cairo Plant", isActive = true })));
        var store = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = site })));
        var supplier = Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var part = Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "BRG-1", name = "Bearing", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false, preferredSupplierId = supplier,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        return new Org(c, tenant, site, store, part, supplier);
    }

    /// <summary>A signed-in user with a built-in role, or with a new role holding exactly <paramref name="permissions"/>.</summary>
    internal static async Task<(HttpClient Client, Guid Id, string Email)> UserAsync(ApiFixture fx, HttpClient admin, string role, string[]? permissions = null,
        Guid[]? siteIds = null, decimal? limit = null)
    {
        Guid roleId;
        if (permissions is null)
            roleId = (await Ok(await admin.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        else
            roleId = Id(await Ok(await admin.PostAsJsonAsync("/api/roles", new { name = $"{role} {Guid.NewGuid():N}"[..30], description = "", permissions })));
        var email = $"{Guid.NewGuid():N}@test.local";
        var user = Id(await Ok(await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u", purchaseApprovalLimit = limit, siteIds = siteIds ?? [],
        })));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return (c, user, email);
    }

    internal static object PartLine(Org o, decimal qty = 4, Guid? workOrder = null, decimal? price = null) =>
        new { partId = o.Part, quantity = qty, storeId = o.Store, workOrderId = workOrder, estimatedUnitPrice = price };

    internal static async Task<Guid> RequestAsync(HttpClient c, Org o, params object[] lines) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/purchase-requests", new { siteId = o.Site, lines })));

    private async Task<List<OutboundEmail>> MailsAsync(Guid tenant, string key)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && e.EventKey == key).ToListAsync();
    }

    [SkippableFact]
    public async Task Technician_raises_a_request_numbered_PR_and_submits_it()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, techId, _) = await UserAsync(fx, o.Admin, "Technician");
        var id = await RequestAsync(tech, o, PartLine(o), new { description = "Crane hire", quantity = 1, unitOfMeasure = "day" });
        var d = await Ok(await tech.GetAsync($"/api/purchase-requests/{id}"));
        Assert.StartsWith("PR-0000", d.GetProperty("number").GetString());
        Assert.Equal("Draft", Status(d));
        Assert.Equal(2, d.GetProperty("lines").GetArrayLength());
        Assert.True(d.GetProperty("actions").GetProperty("submit").GetBoolean());
        await Ok(await tech.PostAsync($"/api/purchase-requests/{id}/submit", null));
        d = await Ok(await tech.GetAsync($"/api/purchase-requests/{id}"));
        Assert.Equal("Submitted", Status(d));
        Assert.Equal(techId, d.GetProperty("requestedById").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, d.GetProperty("submittedAt").ValueKind);
    }

    [SkippableFact]
    public async Task Submit_needs_a_line_a_store_for_parts_and_positive_quantities()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        async Task<string> Refused(object[] lines)
        {
            var id = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests", new { siteId = o.Site, lines })));
            var r = await o.Admin.PostAsync($"/api/purchase-requests/{id}/submit", null);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return await r.Content.ReadAsStringAsync();
        }
        Assert.Contains("Add at least one line.", await Refused([]));
        Assert.Contains("Line 1: choose the store this part should arrive in.", await Refused([new { partId = o.Part, quantity = 2 }]));
        Assert.Contains("Line 1: quantity must be more than 0.", await Refused([new { description = "Gloves", quantity = 0 }]));
    }

    [SkippableFact]
    public async Task Approver_approves_or_rejects_with_a_reason_and_nobody_approves_their_own()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var (manager, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        var first = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{first}/submit", null));
        Assert.False((await Ok(await tech.GetAsync($"/api/purchase-requests/{first}"))).GetProperty("actions").GetProperty("approve").GetBoolean());
        await Ok(await manager.PostAsync($"/api/purchase-requests/{first}/approve", null));
        var d = await Ok(await manager.GetAsync($"/api/purchase-requests/{first}"));
        Assert.Equal("Approved", Status(d));
        Assert.Equal("Maintenance Manager", d.GetProperty("decidedByName").GetString());

        var second = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{second}/submit", null));
        var blank = await manager.PostAsJsonAsync($"/api/purchase-requests/{second}/reject", new { reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Contains("Give a reason.", await blank.Content.ReadAsStringAsync());
        await Ok(await manager.PostAsJsonAsync($"/api/purchase-requests/{second}/reject", new { reason = "Too early" }));
        d = await Ok(await manager.GetAsync($"/api/purchase-requests/{second}"));
        Assert.Equal("Rejected", Status(d));
        Assert.Equal("Too early", d.GetProperty("rejectReason").GetString());

        // Someone who may raise and approve requests still can't approve their own.
        var (both, _, _) = await UserAsync(fx, o.Admin, "Lead", ["purchasing.request", "purchasing.approveRequests", "inventory.view"]);
        var own = await RequestAsync(both, o, PartLine(o));
        await Ok(await both.PostAsync($"/api/purchase-requests/{own}/submit", null));
        var self = await both.PostAsync($"/api/purchase-requests/{own}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Contains("You can't approve your own request.", await self.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Rejected_request_is_fixed_and_submitted_again()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var id = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{id}/submit", null));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-requests/{id}/reject", new { reason = "Wrong part" }));
        await Ok(await tech.PutAsJsonAsync($"/api/purchase-requests/{id}", new { siteId = o.Site, lines = new[] { PartLine(o, 6) } }));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{id}/submit", null));
        Assert.Equal("Submitted", Status(await Ok(await tech.GetAsync($"/api/purchase-requests/{id}"))));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{id}/approve", null));
        var late = await tech.PutAsJsonAsync($"/api/purchase-requests/{id}", new { siteId = o.Site, lines = new[] { PartLine(o, 9) } });
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Contains("This request can't be changed any more.", await late.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Requester_sees_only_their_own_others_see_their_sites()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var (other, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var (manager, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        var id = await RequestAsync(tech, o, PartLine(o));
        var mine = await RequestAsync(other, o, PartLine(o));
        var list = (await Ok(await other.GetAsync("/api/purchase-requests"))).GetProperty("items").EnumerateArray().Select(Id).ToList();
        Assert.Equal([mine], list);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/purchase-requests/{id}")).StatusCode);
        var all = (await Ok(await manager.GetAsync("/api/purchase-requests"))).GetProperty("items").EnumerateArray().Select(Id).ToList();
        Assert.Contains(id, all);
        Assert.Contains(mine, all);
    }

    [SkippableFact]
    public async Task Draft_is_deleted_later_requests_are_cancelled_with_a_reason()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var draft = await RequestAsync(tech, o, PartLine(o));
        Assert.Equal(HttpStatusCode.NoContent, (await tech.DeleteAsync($"/api/purchase-requests/{draft}")).StatusCode);
        var sent = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{sent}/submit", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await tech.DeleteAsync($"/api/purchase-requests/{sent}")).StatusCode);
        await Ok(await tech.PostAsJsonAsync($"/api/purchase-requests/{sent}/cancel", new { reason = "Not needed" }));
        var d = await Ok(await tech.GetAsync($"/api/purchase-requests/{sent}"));
        Assert.Equal("Cancelled", Status(d));
        Assert.Equal("Not needed", d.GetProperty("cancelReason").GetString());
    }

    [SkippableFact]
    public async Task Work_order_on_a_line_must_be_at_the_request_site_and_fills_its_asset()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var far = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Far job", siteId = other, type = "Corrective", priority = "Medium" })));
        var r = await o.Admin.PostAsJsonAsync("/api/purchase-requests", new { siteId = o.Site, lines = new[] { PartLine(o, 2, far) } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Line 1: the work order is at another site.", await r.Content.ReadAsStringAsync());

        var asset = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = o.Site, status = "Active", criticality = "Medium", warrantyType = "None" })));
        var near = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Pump job", siteId = o.Site, assetId = asset, type = "Corrective", priority = "Medium" })));
        var id = await RequestAsync(o.Admin, o, PartLine(o, 2, near));
        var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{id}"))).GetProperty("lines")[0];
        Assert.Equal(near, line.GetProperty("workOrder").GetProperty("id").GetGuid());
        Assert.Equal(asset, line.GetProperty("asset").GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task Submitted_request_emails_the_site_recipients_and_decisions_email_the_requester()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, email: true);
        var (tech, _, techEmail) = await UserAsync(fx, o.Admin, "Technician");
        var (buyer, buyerId, buyerEmail) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        var (_, _, managerEmail) = await UserAsync(fx, o.Admin, "Maintenance Manager");

        // No recipients named: everyone who may approve requests at the site hears.
        var first = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{first}/submit", null));
        var sent = (await MailsAsync(o.Tenant, "pr.submitted")).Select(m => m.ToAddress).ToList();
        Assert.Contains(buyerEmail, sent);
        Assert.Contains(managerEmail, sent);

        // The site names the buyer: only the buyer hears.
        var site = await Ok(await o.Admin.GetAsync($"/api/sites/{o.Site}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/sites/{o.Site}", new
        {
            code = "S1", name = "Cairo Plant", isActive = true, settings = new { purchaseRequestRecipientIds = new[] { buyerId } },
        }));
        var second = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{second}/submit", null));
        var toSecond = (await MailsAsync(o.Tenant, "pr.submitted")).Where(m => m.EntityId == second).Select(m => m.ToAddress).ToList();
        Assert.Equal([buyerEmail], toSecond);

        await Ok(await buyer.PostAsync($"/api/purchase-requests/{second}/approve", null));
        Assert.Equal([techEmail], (await MailsAsync(o.Tenant, "pr.approved")).Where(m => m.EntityId == second).Select(m => m.ToAddress).ToList());
        _ = site;
    }

    [SkippableFact]
    public async Task Comments_and_files_on_a_request()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var id = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsJsonAsync($"/api/purchase-requests/{id}/comments", new { body = "Needed for the line 3 stop" }));
        var comment = (await Ok(await tech.GetAsync($"/api/purchase-requests/{id}"))).GetProperty("comments")[0];
        Assert.Equal("Needed for the line 3 stop", comment.GetProperty("body").GetString());
        Assert.Equal("Technician", comment.GetProperty("userName").GetString());

        using var form = new MultipartFormDataContent { { new StringContent("PurchaseRequest"), "entityType" }, { new StringContent(id.ToString()), "entityId" } };
        var file = new ByteArrayContent("%PDF-1.4\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "quote.pdf");
        await Ok(await tech.PostAsync("/api/attachments", form));
        var files = await Ok(await tech.GetAsync($"/api/attachments?entityType=PurchaseRequest&entityId={id}"));
        Assert.Equal(1, files.GetArrayLength());
    }
}
