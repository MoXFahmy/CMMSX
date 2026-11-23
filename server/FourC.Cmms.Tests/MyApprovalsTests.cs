using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: what waits for my signature, and the bell's count.</summary>
public class MyApprovalsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> MineAsync(HttpClient c) => await Ok(await c.GetAsync("/api/approvals/mine"));
    private static List<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(Id).ToList();
    private static async Task<int?> BellAsync(HttpClient c)
    {
        var v = (await Ok(await c.GetAsync("/api/alerts"))).GetProperty("waitingForMyApproval");
        return v.ValueKind == JsonValueKind.Null ? null : v.GetInt32();
    }

    private static async Task<Guid> SubmittedRequestAsync(HttpClient c, Guid site, string what)
    {
        var id = Id(await Ok(await c.PostAsJsonAsync("/api/purchase-requests", new { siteId = site, lines = new[] { new { description = what, quantity = 1 } } })));
        await Ok(await c.PostAsync($"/api/purchase-requests/{id}/submit", null));
        return id;
    }

    private static async Task<Guid> SubmittedOrderAsync(HttpClient c, PurchaseRequestTests.Org o, decimal price)
    {
        var po = Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, taxPercent = 0,
            lines = new[] { new { description = "Overhaul", quantity = 1, unitPrice = price, discountPercent = 0 } },
        })));
        await Ok(await c.PostAsync($"/api/purchase-orders/{po}/submit", null));
        return po;
    }

    [SkippableFact]
    public async Task My_approvals_lists_requests_and_orders_waiting_for_me()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (a, aId, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        var (b, bId, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { aId } }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L2", minAmount = 10000, rule = "Any", approverIds = new[] { bId } }));
        var request = await SubmittedRequestAsync(o.Admin, o.Site, "Gloves");
        var po = await SubmittedOrderAsync(o.Admin, o, 20000);

        var mine = await MineAsync(a);
        Assert.Contains(request, Ids(mine.GetProperty("requests")));
        var order = mine.GetProperty("orders").EnumerateArray().Single(x => Id(x) == po);
        Assert.Equal("L1", order.GetProperty("levelName").GetString());
        Assert.Equal(["Maintenance Manager"], order.GetProperty("next").EnumerateArray().Select(n => n.GetString()).ToList());
        Assert.Equal(2, await BellAsync(a));
        Assert.DoesNotContain(po, Ids((await MineAsync(b)).GetProperty("orders")));

        await Ok(await a.PostAsync($"/api/purchase-orders/{po}/approve", null));
        Assert.DoesNotContain(po, Ids((await MineAsync(a)).GetProperty("orders")));
        var forB = (await MineAsync(b)).GetProperty("orders").EnumerateArray().Single(x => Id(x) == po);
        Assert.Equal(["Maintenance Manager"], forB.GetProperty("signedBefore").EnumerateArray().Select(n => n.GetString()).ToList());
    }

    [SkippableFact]
    public async Task Old_rule_orders_show_for_approvers_within_their_limit()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (high, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", limit: 1000);
        var (low, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", limit: 100);
        var po = await SubmittedOrderAsync(o.Admin, o, 500);
        Assert.Contains(po, Ids((await MineAsync(high)).GetProperty("orders")));
        Assert.DoesNotContain(po, Ids((await MineAsync(low)).GetProperty("orders")));
    }

    [SkippableFact]
    public async Task Sent_back_lists_my_rejected_requests_and_orders()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (a, aId, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "L1", minAmount = 0, rule = "Any", approverIds = new[] { aId } }));
        var request = await SubmittedRequestAsync(o.Admin, o.Site, "Wrong thing");
        await Ok(await a.PostAsJsonAsync($"/api/purchase-requests/{request}/reject", new { reason = "Wrong part" }));
        var po = await SubmittedOrderAsync(o.Admin, o, 500);
        await Ok(await a.PostAsJsonAsync($"/api/purchase-orders/{po}/reject", new { reason = "Too dear" }));
        var back = (await MineAsync(o.Admin)).GetProperty("sentBack").EnumerateArray().ToList();
        Assert.Equal("Wrong part", back.Single(x => Id(x) == request).GetProperty("reason").GetString());
        Assert.Equal("Too dear", back.Single(x => Id(x) == po).GetProperty("reason").GetString());
    }

    [SkippableFact]
    public async Task My_approvals_and_bell_only_count_sites_I_can_see()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var (approver, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", siteIds: [o.Site]);
        var here = await SubmittedRequestAsync(o.Admin, o.Site, "Gloves");
        var there = await SubmittedRequestAsync(o.Admin, other, "Helmets");
        var listed = Ids((await MineAsync(approver)).GetProperty("requests"));
        Assert.Contains(here, listed);
        Assert.DoesNotContain(there, listed);
        Assert.Equal(1, await BellAsync(approver));
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        Assert.Null(await BellAsync(tech));
    }
}
