using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.ContractorLineTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.29.1: purchasing and contractor left-overs (contacts on open lines, kept rates, request-line links, receipts and suppliers at the same moment).</summary>
public class PurchasingLeftoverTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Lost = "This request line was just ordered by someone else.";

    private static object CompanyBody(string code, bool contractor, object[] contacts, object[] rates, bool? supplier = false) => new
    {
        code, name = code + " Co", isSupplier = supplier, isContractor = contractor, isManufacturer = false, isActive = true, contacts, addresses = Array.Empty<object>(), rates,
    };

    private static async Task<(Guid Id, Guid Contact)> CompanyWithContactAsync(HttpClient c, string code)
    {
        var id = await ContractorAsync(c, code);
        return (id, (await Ok(await c.GetAsync($"/api/suppliers/{id}"))).GetProperty("contacts")[0].GetProperty("id").GetGuid());
    }

    private static async Task<(Guid Id, string Number)> WorkOrderAtAsync(HttpClient c, Guid site)
    {
        var id = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Lift service", siteId = site, type = "Corrective", priority = "Medium" })));
        return (id, (await Ok(await c.GetAsync($"/api/work-orders/{id}"))).GetProperty("number").GetString()!);
    }

    // ---------------- 0.17.0-1 (decision 5, P7) ----------------

    [SkippableFact]
    public async Task Removing_a_contact_named_on_open_lines_is_refused_listing_visible_lines_only()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var (company, contact) = await CompanyWithContactAsync(o.Admin, "LIFT");
        var (woA, numberA) = await WorkOrderAtAsync(o.Admin, o.Site);
        var (woB, numberB) = await WorkOrderAtAsync(o.Admin, siteB);
        var lineA = await AddLineAsync(o.Admin, woA, Line(company, planned: 2, contact: contact));
        var lineB = await AddLineAsync(o.Admin, woB, Line(company, planned: 2, contact: contact));
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", siteIds: [o.Site]);
        var body = CompanyBody("LIFT", true, [], []);

        // A site-limited editor is told about the line they can see, and only counts the one they can't.
        var refused = await Problem(await limited.PutAsJsonAsync($"/api/suppliers/{company}", body));
        Assert.Contains(numberA, refused);
        Assert.Contains("1 more you can't see", refused);
        Assert.DoesNotContain(numberB, refused);
        // An editor who sees everything gets both.
        var all = await Problem(await o.Admin.PutAsJsonAsync($"/api/suppliers/{company}", body));
        Assert.Contains(numberA, all);
        Assert.Contains(numberB, all);
        Assert.DoesNotContain("can't see", all);
        Assert.Single((await Ok(await o.Admin.GetAsync($"/api/suppliers/{company}"))).GetProperty("contacts").EnumerateArray());

        // Only lines the editor can't see: a count and no number.
        await Ok(await o.Admin.DeleteAsync($"/api/work-orders/{woA}/contractors/{lineA}"));
        var onlyHidden = await Problem(await limited.PutAsJsonAsync($"/api/suppliers/{company}", body));
        Assert.DoesNotContain(numberA, onlyHidden);
        Assert.DoesNotContain(numberB, onlyHidden);
        Assert.Contains("1 of them you can't see", onlyHidden);

        // Once nobody is named on an open line the contact can go; keeping it (same id) was never a problem.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{company}", CompanyBody("LIFT", true, [new { id = contact, name = "Omar Lift", role = "Engineer", phone = "0100", email = "omar@lift.test" }], [])));
        await Ok(await o.Admin.DeleteAsync($"/api/work-orders/{woB}/contractors/{lineB}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{company}", body));
        Assert.Empty((await Ok(await o.Admin.GetAsync($"/api/suppliers/{company}"))).GetProperty("contacts").EnumerateArray());
    }

    // ---------------- 0.17.0-2 (decision 6) ----------------

    [SkippableFact]
    public async Task Unticking_contractor_keeps_rates_and_reticking_restores_them()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 100);
        var id = await ContractorAsync(o.Admin, "LIFT", (elec, 150m));
        var omar = (await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("contacts")[0].GetProperty("id").GetGuid();
        var contacts = new object[] { new { id = omar, name = "Omar Lift", role = "Engineer", phone = "0100", email = "omar@lift.test" } };

        // The form sends no rates once the box is unticked (it hides them): the company stays a supplier and keeps its rate.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", false, contacts, [], supplier: true)));
        Assert.Equal(1, await Loop.DbAsync(fx, db => db.SupplierCategoryRates.IgnoreQueryFilters().CountAsync(r => r.SupplierId == id)));
        Assert.Empty((await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("rates").EnumerateArray());
        Assert.Empty((await Ok(await o.Admin.GetAsync("/api/suppliers/lookup?role=supplier"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("rates").EnumerateArray());

        // Ticked again, the form (which could not see the rates) sends none: the kept rate comes back.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", true, contacts, [], supplier: true)));
        var back = (await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("rates").EnumerateArray().Single();
        Assert.Equal(elec, back.GetProperty("categoryId").GetGuid());
        Assert.Equal(150m, back.GetProperty("rate").GetDecimal());

        // A contractor who saves with no rates on purpose still clears them.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", true, contacts, [], supplier: true)));
        Assert.Empty((await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("rates").EnumerateArray());
    }

    // ---------------- 0.16.0-4 / 0.16.0-5 ----------------

    private static async Task<(Guid Request, Guid Line, string Number)> ApprovedLineAsync(HttpClient requester, HttpClient approver, PurchaseRequestTests.Org o, bool approve = true)
    {
        var request = await RequestAsync(requester, o, PartLine(o, 3));
        await Ok(await requester.PostAsync($"/api/purchase-requests/{request}/submit", null));
        if (approve) await Ok(await approver.PostAsync($"/api/purchase-requests/{request}/approve", null));
        var d = await Ok(await approver.GetAsync($"/api/purchase-requests/{request}"));
        return (request, d.GetProperty("lines")[0].GetProperty("id").GetGuid(), d.GetProperty("number").GetString()!);
    }

    private static object Order(PurchaseRequestTests.Org o, Guid? requestLine, Guid? lineId = null, decimal qty = 3) => new
    {
        siteId = o.Site, supplierId = o.Supplier, lines = new[] { new { id = lineId, partId = o.Part, quantity = qty, unitPrice = 10m, discountPercent = 0m, storeId = o.Store, purchaseRequestLineId = requestLine } },
    };

    private static Task<int> LiveLinesAsync(ApiFixture fx, Guid requestLine) => Loop.DbAsync(fx, db =>
        db.PurchaseOrderLines.IgnoreQueryFilters().CountAsync(l => l.PurchaseRequestLineId == requestLine
            && db.PurchaseOrders.IgnoreQueryFilters().Any(p => p.Id == l.PurchaseOrderId && p.Status != Core.Purchasing.PurchaseOrderStatus.Cancelled)));

    [SkippableFact]
    public async Task Order_line_link_to_an_unapproved_or_foreign_request_line_is_refused()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var waiting = await ApprovedLineAsync(tech, o.Admin, o, approve: false);
        Assert.Contains($"{waiting.Number} isn't approved.", await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, waiting.Line))));

        // Another organization's line, and a line nobody has ever heard of, are the same thing: unknown.
        var other = await OrgAsync(fx);
        var (otherTech, _, _) = await UserAsync(fx, other.Admin, "Technician");
        var foreign = await ApprovedLineAsync(otherTech, other.Admin, other);
        Assert.Contains("Unknown request line.", await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, foreign.Line))));
        Assert.Contains("Unknown request line.", await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, Guid.NewGuid()))));
        Assert.Equal(0, await LiveLinesAsync(fx, foreign.Line));

        // A buyer limited to one site cannot link a request line at another.
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var storeB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stores", new { code = "ALEX", name = "Alex store", siteId = siteB })));
        var atB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests", new { siteId = siteB, lines = new[] { new { partId = o.Part, quantity = 2, storeId = storeB } } })));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{atB}/submit", null));
        var (approver, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        await Ok(await approver.PostAsync($"/api/purchase-requests/{atB}/approve", null));
        var lineB = (await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{atB}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", siteIds: [o.Site]);
        Assert.Contains("Unknown request line.", await Problem(await limited.PostAsJsonAsync("/api/purchase-orders", Order(o, lineB))));
        Assert.Equal(0, await LiveLinesAsync(fx, lineB));
    }

    [SkippableFact]
    public async Task Order_line_link_to_a_line_already_on_a_live_order_is_refused()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var x = await ApprovedLineAsync(tech, o.Admin, o);
        var first = await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, x.Line)));
        var number = first.GetProperty("number").GetString()!;
        Assert.Equal("Ordered", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{x.Request}"))));

        Assert.Contains($"{x.Number} line 1 is already on {number}.", await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, x.Line))));
        Assert.Equal(1, await LiveLinesAsync(fx, x.Line));

        // Saving the order that holds the link again, and linking one line twice in one order, are different things.
        var po = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{Id(first)}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/purchase-orders/{Id(first)}", Order(o, x.Line, po.GetProperty("lines")[0].GetProperty("id").GetGuid(), qty: 5)));
        Assert.Equal(1, await LiveLinesAsync(fx, x.Line));
        var y = await ApprovedLineAsync(tech, o.Admin, o);
        var twice = await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, lines = new[]
            {
                new { partId = o.Part, quantity = 1, unitPrice = 10m, discountPercent = 0m, storeId = o.Store, purchaseRequestLineId = (Guid?)y.Line },
                new { partId = o.Part, quantity = 1, unitPrice = 10m, discountPercent = 0m, storeId = o.Store, purchaseRequestLineId = (Guid?)y.Line },
            },
        }));
        Assert.Contains("List each request line once.", twice);
        Assert.Equal(0, await LiveLinesAsync(fx, y.Line));
    }

    [SkippableFact]
    public async Task Two_buyers_ordering_one_request_line_make_one_live_line()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var (buyer, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        for (var round = 0; round < 3; round++)
        {
            var x = await ApprovedLineAsync(tech, o.Admin, o);
            var body = new { lines = new[] { new { requestLineId = x.Line } } };
            var both = await Task.WhenAll(o.Admin.PostAsJsonAsync("/api/purchase-requests/order", body), buyer.PostAsJsonAsync("/api/purchase-requests/order", body));
            Assert.Equal(1, both.Count(r => r.IsSuccessStatusCode));
            var loser = both.Single(r => !r.IsSuccessStatusCode);
            var text = await loser.Content.ReadAsStringAsync();
            // Whoever arrives after the other has finished is told the line is on an order; whoever overlaps is told it was just ordered.
            Assert.True(loser.StatusCode == HttpStatusCode.Conflict ? text.Contains(Lost) : loser.StatusCode == HttpStatusCode.BadRequest && text.Contains("line 1 is already on PO-"),
                $"{(int)loser.StatusCode}: {text}");
            Assert.Equal(1, await LiveLinesAsync(fx, x.Line));
            Assert.Equal("Ordered", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{x.Request}"))));
        }
    }

    [SkippableFact]
    public async Task Two_buyers_linking_one_request_line_through_the_order_form_make_one_live_line()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var (buyer, _, _) = await UserAsync(fx, o.Admin, "Maintenance Manager");
        for (var round = 0; round < 3; round++)
        {
            var x = await ApprovedLineAsync(tech, o.Admin, o);
            var both = await Task.WhenAll(o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, x.Line)), buyer.PostAsJsonAsync("/api/purchase-orders", Order(o, x.Line)));
            Assert.Equal(1, both.Count(r => r.IsSuccessStatusCode));
            var loser = both.Single(r => !r.IsSuccessStatusCode);
            var text = await loser.Content.ReadAsStringAsync();
            Assert.True(loser.StatusCode == HttpStatusCode.Conflict ? text.Contains(Lost) : loser.StatusCode == HttpStatusCode.BadRequest && text.Contains("line 1 is already on PO-"),
                $"{(int)loser.StatusCode}: {text}");
            Assert.Equal(1, await LiveLinesAsync(fx, x.Line));
        }
    }

    [SkippableFact]
    public async Task Reordering_after_a_cancel_still_works()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        var x = await ApprovedLineAsync(tech, o.Admin, o);
        var body = new { lines = new[] { new { requestLineId = x.Line } } };
        var first = Id((await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", body)))[0]);
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{first}/cancel", new { reason = "Wrong supplier" }));
        Assert.Equal("Approved", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{x.Request}"))));

        var second = Id((await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", body)))[0]);
        Assert.Equal("Ordered", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{x.Request}"))));
        Assert.Equal(1, await LiveLinesAsync(fx, x.Line));
        // The same through the order form, after the second one is cancelled too.
        await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{second}/cancel", new { reason = "Again" }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, x.Line)));
        Assert.Equal(1, await LiveLinesAsync(fx, x.Line));
        Assert.Equal("Ordered", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{x.Request}"))));
    }

    // ---------------- 0.11.0-3 ----------------

    [SkippableFact]
    public async Task Two_partial_receipts_at_once_cannot_over_receive()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        for (var round = 0; round < 3; round++)
        {
            var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", Order(o, null, qty: 12))));
            await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
            await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
            var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
            // A first receipt makes the order "partially received", so the next ones leave its status alone (the case the race hid in).
            await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 2 } } }));
            var before = (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("onHand").GetDecimal();

            var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 6 } } })));
            Assert.Equal(1, both.Count(r => r.IsSuccessStatusCode));
            var loser = both.Single(r => !r.IsSuccessStatusCode);
            var text = await loser.Content.ReadAsStringAsync();
            Assert.True(loser.StatusCode == HttpStatusCode.Conflict ? text.Contains("changed by someone else") : loser.StatusCode == HttpStatusCode.BadRequest && text.Contains("only 4 outstanding"),
                $"{(int)loser.StatusCode}: {text}");
            Assert.Equal(8m, (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("quantityReceived").GetDecimal());
            Assert.Equal(before + 6m, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("onHand").GetDecimal());
            // What is left can still be received afterwards.
            await Ok(await o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 4 } } }));
            Assert.Equal("Received", Status(await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))));
        }
    }

    // ---------------- 0.18.0-13 ----------------

    [SkippableFact]
    public async Task Adding_the_same_supplier_twice_at_once_says_it_exists()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        const string already = "This supplier is already in the part's catalogue.";
        for (var round = 0; round < 4; round++)
        {
            var supplier = await CatalogueTests.SupplierAsync(o.Admin, $"SKF{round}");
            var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => o.Admin.PostAsJsonAsync($"/api/parts/{o.Part}/suppliers", CatalogueTests.Row(supplier, 1, 5))));
            Assert.Equal(1, both.Count(r => r.IsSuccessStatusCode));
            var text = await both.Single(r => !r.IsSuccessStatusCode).Content.ReadAsStringAsync();
            Assert.Contains(already, text);
            Assert.DoesNotContain("same code or number", text);
            Assert.Equal(1, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}/suppliers"))).EnumerateArray().Count(r => r.GetProperty("supplierId").GetGuid() == supplier));
        }
        // Two companies with one code at once: the same, for the code.
        for (var round = 0; round < 4; round++)
        {
            var body = new { code = $"TWIN{round}", name = "Twin", isContractor = false };
            var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => o.Admin.PostAsJsonAsync("/api/suppliers", body)));
            Assert.Equal(1, both.Count(r => r.IsSuccessStatusCode));
            var text = await both.Single(r => !r.IsSuccessStatusCode).Content.ReadAsStringAsync();
            Assert.Contains("Another supplier uses this code.", text);
            Assert.DoesNotContain("same code or number", text);
        }
    }

    // ---------------- Arabic ----------------

    [Theory]
    [InlineData("This request line was just ordered by someone else.")]
    [InlineData("List each request line once.")]
    [InlineData("Can't remove Omar Lift: named on open work orders WO-00012 and 2 more you can't see. Pick someone else on those lines first.")]
    [InlineData("Can't remove Omar Lift: named on open work orders WO-00012, WO-00014. Pick someone else on those lines first.")]
    [InlineData("Can't remove Omar Lift: named on open work orders, 3 of them you can't see. Pick someone else on those lines first.")]
    [InlineData("PR-00004 line 2 is already on an order you can't see.")]
    public void New_messages_have_Arabic(string english)
    {
        var arabic = Translator.T(english, "ar");
        Assert.NotEqual(english, arabic);
        // Values inside a message stay (a work order number, a request number).
        foreach (var token in new[] { "WO-00012", "WO-00014", "PR-00004" }.Where(english.Contains)) Assert.Contains(token, arabic);
        Assert.DoesNotContain("Can't remove", arabic);
    }
}
