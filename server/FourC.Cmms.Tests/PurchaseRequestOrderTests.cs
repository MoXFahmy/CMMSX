using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: approved request lines become draft orders grouped by supplier and site; the request follows its orders.</summary>
public class PurchaseRequestOrderTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> ApprovedAsync(HttpClient requester, HttpClient approver, PurchaseRequestTests.Org o, params object[] lines)
    {
        var id = await RequestAsync(requester, o, lines);
        await Ok(await requester.PostAsync($"/api/purchase-requests/{id}/submit", null));
        await Ok(await approver.PostAsync($"/api/purchase-requests/{id}/approve", null));
        return id;
    }

    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/purchase-requests/{id}"));
    private static Guid LineId(JsonElement detail, int index) => detail.GetProperty("lines")[index].GetProperty("id").GetGuid();

    private async Task<int> EmailsAsync(Guid tenant, string key, Guid entity)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant && e.EventKey == key && e.EntityId == entity);
    }

    [SkippableFact]
    public async Task Approved_lines_become_draft_orders_grouped_by_supplier_and_site()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, email: true);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", new { code = "CRANE", name = "Crane Co", isContractor = false })));
        var first = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 4), new { description = "Crane hire", quantity = 1, unitOfMeasure = "day", estimatedUnitPrice = 500m });
        var second = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 2));
        var d1 = await DetailAsync(o.Admin, first);
        var d2 = await DetailAsync(o.Admin, second);
        var created = await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new
        {
            lines = new object[]
            {
                new { requestLineId = LineId(d1, 0) }, // the part's preferred supplier
                new { requestLineId = LineId(d1, 1), supplierId = other },
                new { requestLineId = LineId(d2, 0), supplierId = o.Supplier },
            },
        }));
        Assert.Equal(2, created.GetArrayLength());
        var main = created.EnumerateArray().Single(x => x.GetProperty("supplier").GetString() == "Supplier");
        var po = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{Id(main)}"));
        Assert.Equal("Draft", Status(po));
        Assert.Equal(2, po.GetProperty("lines").GetArrayLength());
        Assert.All(po.GetProperty("lines").EnumerateArray(), l =>
        {
            Assert.Equal(o.Store, l.GetProperty("storeId").GetGuid());
            Assert.NotEqual(JsonValueKind.Null, l.GetProperty("request").ValueKind);
        });
        var notes = po.GetProperty("notes").GetString()!;
        Assert.Contains(d1.GetProperty("number").GetString()!, notes);
        Assert.Contains(d2.GetProperty("number").GetString()!, notes);

        Assert.Equal("Ordered", Status(await DetailAsync(o.Admin, first)));
        Assert.Equal("Ordered", Status(await DetailAsync(o.Admin, second)));
        var line = (await DetailAsync(o.Admin, first)).GetProperty("lines")[1];
        Assert.Equal(1m, line.GetProperty("ordered").GetDecimal());
        Assert.StartsWith("PO-", line.GetProperty("order").GetProperty("number").GetString());
        Assert.Equal(1, await EmailsAsync(o.Tenant, "pr.ordered", first));
    }

    [SkippableFact]
    public async Task Only_approved_unordered_lines_can_be_ordered()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var waiting = await RequestAsync(tech, o, PartLine(o));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{waiting}/submit", null));
        var dw = await DetailAsync(o.Admin, waiting);
        var r = await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(dw, 0) } } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains($"{dw.GetProperty("number").GetString()} isn't approved.", await r.Content.ReadAsStringAsync());

        var ok = await ApprovedAsync(tech, o.Admin, o, PartLine(o), new { description = "Gloves", quantity = 10 });
        var d = await DetailAsync(o.Admin, ok);
        var number = d.GetProperty("number").GetString();
        var free = await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 1) } } });
        Assert.Equal(HttpStatusCode.BadRequest, free.StatusCode);
        Assert.Contains($"Choose a supplier for {number} line 2.", await free.Content.ReadAsStringAsync());
        await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 0) } } }));
        var again = await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 0) } } });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains($"{number} line 1 is already on PO-", await again.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Cancelled_or_deleted_order_puts_request_lines_back_to_order()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", new { code = "S2", name = "Second", isContractor = false })));
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var req = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 3), new { description = "Crane hire", quantity = 1 });
        var d = await DetailAsync(o.Admin, req);
        var orders = await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new
        {
            lines = new object[] { new { requestLineId = LineId(d, 0) }, new { requestLineId = LineId(d, 1), supplierId = other } },
        }));
        Assert.Equal("Ordered", Status(await DetailAsync(o.Admin, req)));
        var partsOrder = orders.EnumerateArray().Single(x => x.GetProperty("supplier").GetString() == "Supplier");
        var craneOrder = orders.EnumerateArray().Single(x => x.GetProperty("supplier").GetString() == "Second");

        Assert.Equal(HttpStatusCode.NoContent, (await o.Admin.DeleteAsync($"/api/purchase-orders/{Id(partsOrder)}")).StatusCode);
        d = await DetailAsync(o.Admin, req);
        Assert.Equal("PartlyOrdered", Status(d));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("lines")[0].GetProperty("order").ValueKind);

        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{Id(craneOrder)}/cancel", new { reason = "Supplier closed" }));
        Assert.Equal("Approved", Status(await DetailAsync(o.Admin, req)));
    }

    [SkippableFact]
    public async Task Receiving_tells_the_requester_and_shows_received_on_the_request()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, email: true);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var req = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 5));
        var d = await DetailAsync(o.Admin, req);
        var po = Id((await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 0) } } })))[0]);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var poLine = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = poLine, quantity = 2 } } }));
        Assert.Equal(2m, (await DetailAsync(o.Admin, req)).GetProperty("lines")[0].GetProperty("received").GetDecimal());
        Assert.Equal(1, await EmailsAsync(o.Tenant, "pr.received", req));
    }

    [SkippableFact]
    public async Task Parts_ordered_for_a_job_are_issued_to_it_on_receipt()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Pump job", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        var req = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 2, wo));
        var d = await DetailAsync(o.Admin, req);
        var po = Id((await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 0) } } })))[0]);
        var detail = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal(wo, detail.GetProperty("lines")[0].GetProperty("workOrderId").GetGuid());
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = detail.GetProperty("lines")[0].GetProperty("id").GetGuid(), quantity = 2 } } }));
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var issued = await db.StockTransactions.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo && t.Type == StockTransactionType.Issue).SumAsync(t => t.Quantity);
        Assert.Equal(2m, Math.Abs(issued));
    }

    [SkippableFact]
    public async Task Buyer_closes_a_partly_ordered_request()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var req = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 2), PartLine(o, 3));
        var d = await DetailAsync(o.Admin, req);
        await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 0) } } }));
        Assert.Equal("PartlyOrdered", Status(await DetailAsync(o.Admin, req)));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{req}/close", null));
        Assert.Equal("Closed", Status(await DetailAsync(o.Admin, req)));
        var late = await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = LineId(d, 1) } } });
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [SkippableFact]
    public async Task Buyer_sees_approved_lines_to_order_across_requests()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var first = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 2));
        var second = await ApprovedAsync(tech, o.Admin, o, PartLine(o, 3), new { description = "Gloves", quantity = 5 });
        var waiting = await RequestAsync(tech, o, PartLine(o, 1));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{waiting}/submit", null));
        var lines = (await Ok(await o.Admin.GetAsync("/api/purchase-requests/lines-to-order"))).EnumerateArray().ToList();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(o.Site, l.GetProperty("siteId").GetGuid()));
        var parts = lines.Where(l => l.GetProperty("part").ValueKind != JsonValueKind.Null).Select(l => l.GetProperty("id").GetGuid()).ToList();
        var made = await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = parts.Select(id => new { requestLineId = id }) }));
        Assert.Equal(1, made.GetArrayLength()); // both requests' parts on one order
        Assert.Single((await Ok(await o.Admin.GetAsync("/api/purchase-requests/lines-to-order"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/purchase-requests/lines-to-order")).StatusCode);
        _ = (first, second);
    }
}
