using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: orders signed by cumulative approval levels; today's rule when none apply.</summary>
public class PurchaseApprovalTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Signer(HttpClient Client, Guid Id, string Email);

    private sealed record Setup(PurchaseRequestTests.Org Org, Signer A, Signer B, Signer C, Guid L1, Guid L2);

    private async Task<Signer> SignerAsync(PurchaseRequestTests.Org o, decimal? limit = null, Guid[]? sites = null)
    {
        var (c, id, email) = await UserAsync(fx, o.Admin, "Maintenance Manager", siteIds: sites, limit: limit);
        return new Signer(c, id, email);
    }

    /// <summary>L1 from 0 (A, any one) and L2 from 10,000 (B and C, all must sign); the admin buys.</summary>
    private async Task<Setup> SetupAsync(bool email = false)
    {
        var o = await OrgAsync(fx, email);
        var a = await SignerAsync(o, limit: 100);
        var b = await SignerAsync(o);
        var c = await SignerAsync(o);
        var l1 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Maintenance manager", minAmount = 0, rule = "Any", approverIds = new[] { a.Id } })));
        var l2 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Plant manager", minAmount = 10000, rule = "All", approverIds = new[] { b.Id, c.Id } })));
        return new Setup(o, a, b, c, l1, l2);
    }

    private static async Task<Guid> OrderAsync(HttpClient c, PurchaseRequestTests.Org o, decimal price, string? currency = null, decimal? rate = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, taxPercent = 0, currency, exchangeRate = rate,
            lines = new[] { new { description = "Overhaul", quantity = 1, unitPrice = price, discountPercent = 0 } },
        })));

    private static async Task<JsonElement> PoAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/purchase-orders/{id}"));

    private static async Task<string> RefusedAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    private static List<string> StepNames(JsonElement po) =>
        po.GetProperty("approval").GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();

    private static string StepStatus(JsonElement po, int step) => po.GetProperty("approval").GetProperty("steps")[step].GetProperty("status").GetString()!;

    [SkippableFact]
    public async Task Without_levels_ordering_works_as_today()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var approver = await SignerAsync(o, limit: 1000);
        var po = await OrderAsync(o.Admin, o, 500);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await approver.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var d = await PoAsync(o.Admin, po);
        Assert.Equal("Approved", Status(d));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("approval").ValueKind);
    }

    [SkippableFact]
    public async Task Small_order_signed_by_level_one_is_approved()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync();
        var po = await OrderAsync(s.Org.Admin, s.Org, 5000);
        await Ok(await s.Org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        var d = await PoAsync(s.A.Client, po);
        Assert.Equal(["Maintenance manager"], StepNames(d));
        Assert.Equal("Waiting", StepStatus(d, 0));
        Assert.True(d.GetProperty("actions").GetProperty("approve").GetBoolean()); // A's limit of 100 doesn't matter here
        await Ok(await s.A.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        d = await PoAsync(s.A.Client, po);
        Assert.Equal("Approved", Status(d));
        Assert.Equal("Maintenance Manager", d.GetProperty("approvedByName").GetString());
    }

    [SkippableFact]
    public async Task Big_order_needs_every_level_in_order_and_all_on_an_all_level()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync(email: true);
        var po = await OrderAsync(s.Org.Admin, s.Org, 50000);
        await Ok(await s.Org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        Assert.Equal(["Maintenance manager", "Plant manager"], StepNames(await PoAsync(s.A.Client, po)));
        Assert.Contains("It isn't your turn to sign this order.", await RefusedAsync(await s.B.Client.PostAsync($"/api/purchase-orders/{po}/approve", null)));
        await Ok(await s.A.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Equal("Waiting", StepStatus(await PoAsync(s.A.Client, po), 1));
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            var to = await db.OutboundEmails.IgnoreQueryFilters().Where(e => e.EntityId == po && e.EventKey == "po.approvalWaiting").Select(e => e.ToAddress).ToListAsync();
            Assert.Contains(s.B.Email, to);
            Assert.Contains(s.C.Email, to);
        }
        await Ok(await s.B.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Equal("Submitted", Status(await PoAsync(s.A.Client, po)));
        Assert.Contains("You have already signed this step.", await RefusedAsync(await s.B.Client.PostAsync($"/api/purchase-orders/{po}/approve", null)));
        await Ok(await s.C.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Equal("Approved", Status(await PoAsync(s.A.Client, po)));
    }

    [SkippableFact]
    public async Task Submitter_cannot_sign_and_approve_from_draft_is_not_offered()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync();
        var own = await OrderAsync(s.A.Client, s.Org, 5000);
        Assert.Contains("Submit the order for approval first.", await RefusedAsync(await s.A.Client.PostAsync($"/api/purchase-orders/{own}/approve", null)));
        // With A the only approver on L1, A's own order couldn't be signed by anyone: submit is refused.
        Assert.Contains("Level 'Maintenance manager' has nobody who can sign orders for Cairo Plant.", await RefusedAsync(await s.A.Client.PostAsync($"/api/purchase-orders/{own}/submit", null)));
        await Ok(await s.Org.Admin.PutAsJsonAsync($"/api/approval-levels/{s.L1}", new { name = "Maintenance manager", minAmount = 0, rule = "Any", approverIds = new[] { s.A.Id, s.B.Id } }));
        await Ok(await s.A.Client.PostAsync($"/api/purchase-orders/{own}/submit", null));
        Assert.Contains("You submitted this order, so someone else must sign it.", await RefusedAsync(await s.A.Client.PostAsync($"/api/purchase-orders/{own}/approve", null)));
    }

    [SkippableFact]
    public async Task Reject_at_any_level_returns_the_order_to_draft_and_resubmitting_starts_again()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync(email: true);
        var po = await OrderAsync(s.Org.Admin, s.Org, 50000);
        await Ok(await s.Org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await s.A.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Contains("Give a reason.", await RefusedAsync(await s.B.Client.PostAsJsonAsync($"/api/purchase-orders/{po}/reject", new { reason = "" })));
        await Ok(await s.B.Client.PostAsJsonAsync($"/api/purchase-orders/{po}/reject", new { reason = "Too dear" }));
        var d = await PoAsync(s.A.Client, po);
        Assert.Equal("Draft", Status(d));
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            Assert.True(await db.OutboundEmails.IgnoreQueryFilters().AnyAsync(e => e.EntityId == po && e.EventKey == "po.rejected" && e.TextBody.Contains("Too dear")));
        }
        await Ok(await s.Org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        d = await PoAsync(s.A.Client, po);
        Assert.Equal(2, d.GetProperty("approval").GetProperty("round").GetInt32());
        Assert.Equal("Waiting", StepStatus(d, 0));
        var earlier = d.GetProperty("approval").GetProperty("earlierRounds")[0];
        Assert.Contains("Too dear", earlier.GetRawText());
    }

    [SkippableFact]
    public async Task Submit_is_refused_when_a_level_has_nobody_who_can_sign()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var a = await SignerAsync(o);
        var far1 = await SignerAsync(o, sites: [other]);
        var far2 = await SignerAsync(o, sites: [other]);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { a.Id } }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L2", minAmount = 10000, rule = "Any", approverIds = new[] { far1.Id, far2.Id } }));
        var po = await OrderAsync(o.Admin, o, 20000);
        var body = await RefusedAsync(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        Assert.Contains("Level 'L2' has nobody who can sign orders for Cairo Plant.", body);
        Assert.Equal("Draft", Status(await PoAsync(o.Admin, po)));
    }

    [SkippableFact]
    public async Task Approver_who_lost_access_cannot_sign_and_restart_picks_up_new_levels()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var d1 = await SignerAsync(o);
        var e1 = await SignerAsync(o);
        var level = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Only D", minAmount = 0, rule = "Any", approverIds = new[] { d1.Id } })));
        var po = await OrderAsync(o.Admin, o, 1000);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        // D loses "Approve purchases": a role without it.
        var role = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/roles", new { name = "Viewer only", description = "", permissions = new[] { "purchasing.view" } })));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/users/{d1.Id}", new
        {
            fullName = "Maintenance Manager", email = d1.Email, isActive = true, roleIds = new[] { role }, siteIds = Array.Empty<Guid>(),
        }));
        Assert.Equal(HttpStatusCode.Forbidden, (await d1.Client.PostAsync($"/api/purchase-orders/{po}/approve", null)).StatusCode); // no longer "Approve purchases"
        var detail = await PoAsync(o.Admin, po);
        Assert.True(detail.GetProperty("approval").GetProperty("nobodyCanSign").GetBoolean());
        Assert.True(detail.GetProperty("actions").GetProperty("restartApproval").GetBoolean());
        await Ok(await o.Admin.PutAsJsonAsync($"/api/approval-levels/{level}", new { name = "Only E", minAmount = 0, rule = "Any", approverIds = new[] { e1.Id } }));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/restart-approval", null));
        detail = await PoAsync(o.Admin, po);
        Assert.Equal(2, detail.GetProperty("approval").GetProperty("round").GetInt32());
        Assert.Equal(["Only E"], StepNames(detail));
        await Ok(await e1.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Equal("Approved", Status(await PoAsync(o.Admin, po)));
    }

    [SkippableFact]
    public async Task Signature_mail_skips_approvers_without_the_permission_or_the_site()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, email: true);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var good = await SignerAsync(o);
        var lost = await SignerAsync(o);
        var away = await SignerAsync(o, sites: [other]);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { good.Id, lost.Id, away.Id } }));
        // "lost" is moved to a role without "Approve purchases" after being named on the level.
        var role = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/roles", new { name = "Viewer only", description = "", permissions = new[] { "purchasing.view" } })));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/users/{lost.Id}", new { fullName = "Maintenance Manager", email = lost.Email, isActive = true, roleIds = new[] { role }, siteIds = Array.Empty<Guid>() }));
        var po = await OrderAsync(o.Admin, o, 1000);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        var to = await Loop.DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().Where(e => e.EntityId == po && e.EventKey == "po.approvalWaiting").Select(e => e.ToAddress).ToListAsync());
        Assert.Contains(good.Email, to);
        Assert.DoesNotContain(lost.Email, to);
        Assert.DoesNotContain(away.Email, to);
    }

    [SkippableFact]
    public async Task Two_signatures_at_once_on_an_any_step_count_once()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await SignerAsync(o);
        var a2 = await SignerAsync(o);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { a.Id, a2.Id } }));
        var po = await OrderAsync(o.Admin, o, 1000);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        var both = await Task.WhenAll(a.Client.PostAsync($"/api/purchase-orders/{po}/approve", null), a2.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Single(both, r => r.IsSuccessStatusCode);
        var refused = Assert.Single(both, r => !r.IsSuccessStatusCode);
        Assert.Contains("This step is already signed off.", await refused.Content.ReadAsStringAsync());
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        Assert.Equal(1, await db.PurchaseOrderSignatures.IgnoreQueryFilters().CountAsync(x => x.PurchaseOrderId == po));
    }

    [SkippableFact]
    public async Task Foreign_currency_order_at_the_minimum_waits_for_that_level()
    {
        Skip.IfNot(fx.Enabled);
        var s = await SetupAsync();
        var po = await OrderAsync(s.Org.Admin, s.Org, 200, currency: "EUR", rate: 50);
        await Ok(await s.Org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        Assert.Equal(["Maintenance manager", "Plant manager"], StepNames(await PoAsync(s.A.Client, po)));
    }

    [SkippableFact]
    public async Task Orders_submitted_before_levels_finish_under_the_old_rule()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var approver = await SignerAsync(o, limit: 1000);
        var po = await OrderAsync(o.Admin, o, 500);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { approver.Id } }));
        await Ok(await approver.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var d = await PoAsync(o.Admin, po);
        Assert.Equal("Approved", Status(d));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("approval").ValueKind);
    }

    [SkippableFact]
    public async Task Order_returned_under_levels_and_resubmitted_below_every_level_is_approved_by_the_old_rule()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await SignerAsync(o, limit: 10000);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "From 5k", minAmount = 5000, rule = "Any", approverIds = new[] { a.Id } }));
        var po = await OrderAsync(o.Admin, o, 6000);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await a.Client.PostAsJsonAsync($"/api/purchase-orders/{po}/reject", new { reason = "Too dear" }));
        // Cut below the lowest level and submit again: today's rule now applies.
        var d = await PoAsync(o.Admin, po);
        await Ok(await o.Admin.PutAsJsonAsync($"/api/purchase-orders/{po}", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, taxPercent = 0, rowVersion = d.GetProperty("rowVersion").GetString(),
            lines = new[] { new { description = "Overhaul", quantity = 1, unitPrice = 4000m, discountPercent = 0 } },
        }));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        Assert.True((await PoAsync(a.Client, po)).GetProperty("actions").GetProperty("approve").GetBoolean());
        await Ok(await a.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        d = await PoAsync(o.Admin, po);
        Assert.Equal("Approved", Status(d));
        Assert.Equal(1, d.GetProperty("approval").GetProperty("earlierRounds").GetArrayLength());
    }

    [SkippableFact]
    public async Task Submitter_listed_on_an_all_level_is_told_why_submit_is_refused()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await SignerAsync(o);
        var b = await SignerAsync(o);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Board", minAmount = 0, rule = "All", approverIds = new[] { a.Id, b.Id } }));
        var po = await OrderAsync(a.Client, o, 1000);
        Assert.Contains("Level 'Board' needs all its approvers, and you submitted this order, so you can't sign it.",
            await RefusedAsync(await a.Client.PostAsync($"/api/purchase-orders/{po}/submit", null)));
    }

    [SkippableFact]
    public async Task Adding_a_line_to_a_saved_draft_order_keeps_both()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var po = await OrderAsync(o.Admin, o, 100);
        var d = await PoAsync(o.Admin, po);
        var first = d.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Ok(await o.Admin.PutAsJsonAsync($"/api/purchase-orders/{po}", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, taxPercent = 0, rowVersion = d.GetProperty("rowVersion").GetString(),
            lines = new object[]
            {
                new { id = first, description = "Overhaul", quantity = 1, unitPrice = 100m, discountPercent = 0 },
                new { description = "Gaskets", quantity = 4, unitPrice = 5m, discountPercent = 0 },
            },
        }));
        Assert.Equal(2, (await PoAsync(o.Admin, po)).GetProperty("lines").GetArrayLength());
    }

    /// <summary>
    /// 0.16.1: the second approver on an "any one" level arriving just after the first one signed (as on the CI runner, where it read
    /// the order before the first signature was saved) is told the step is signed off — never sent down the old single-approver path.
    /// </summary>
    [SkippableFact]
    public async Task Late_second_signature_on_an_any_step_is_told_it_is_signed_off()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await SignerAsync(o);
        var a2 = await SignerAsync(o);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { a.Id, a2.Id } }));
        var po = await OrderAsync(o.Admin, o, 1000);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await a.Client.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.Contains("This step is already signed off.", await RefusedAsync(await a2.Client.PostAsync($"/api/purchase-orders/{po}/approve", null)));
        Assert.Contains("This step is already signed off.", await RefusedAsync(await a2.Client.PostAsJsonAsync($"/api/purchase-orders/{po}/reject", new { reason = "Too late" })));
        var d = await PoAsync(o.Admin, po);
        Assert.Equal("Approved", Status(d));
        Assert.Equal("Maintenance Manager", d.GetProperty("approvedByName").GetString());
    }
}
