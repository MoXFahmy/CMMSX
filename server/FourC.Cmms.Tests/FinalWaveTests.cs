using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>
/// 0.29.1 final-review wave: the busiest stock paths start from what is stored when they run again after a dropped connection (each test makes one write
/// fail once and checks the database afterwards), a phone receive that loses the order's row-version race is answered, a company ticked as contractor
/// again keeps its rates, "order from requests" is one attempt, and the switch-on lock query is limited to the organization's own rows.
/// </summary>
public class FinalWaveTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Task<JsonElement> Ok(HttpResponseMessage r) => Loop.Ok(r);
    private static Guid Id(JsonElement e) => Loop.Id(e);

    private async Task<(HttpClient Client, FlakyOnce Flaky)> FlakyAdminAsync(HttpClient admin)
    {
        var u = await Loop.UserAsync(fx, admin, "Administrator");
        var flaky = new FlakyOnce();
        return (await FlakyOnce.HostAsync(fx, flaky, u.Email, Loop.Password), flaky);
    }

    private Task<List<StockTransaction>> TxAsync(Guid part) =>
        Loop.DbAsync(fx, db => db.StockTransactions.IgnoreQueryFilters().AsNoTracking().Where(t => t.PartId == part).OrderBy(t => t.CreatedAt).ToListAsync());

    private Task<decimal> OnHandSumAsync(Guid part) =>
        Loop.DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().Where(l => l.PartId == part).SumAsync(l => l.QuantityOnHand));

    // ---------------- Important 1: stock paths start from what is stored ----------------

    [SkippableFact]
    public async Task Order_receive_run_again_after_a_dropped_connection_posts_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx, email: true);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 10, unitCost = 10 }));
        var (tech, _, _) = await PurchaseRequestTests.UserAsync(fx, o.Admin, "Technician");
        var req = await PurchaseRequestTests.RequestAsync(tech, o, PurchaseRequestTests.PartLine(o, 10, null, 20m));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var d = await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{req}"));
        var po = Id((await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = d.GetProperty("lines")[0].GetProperty("id").GetGuid() } } })))[0]);
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var detail = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        var line = detail.GetProperty("lines")[0];
        var price = line.GetProperty("unitPrice").GetDecimal() * (1 - line.GetProperty("discountPercent").GetDecimal() / 100);

        var (c, flaky) = await FlakyAdminAsync(o.Admin);
        flaky.Arm("INSERT INTO [StockTransactions]");
        await Ok(await c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line.GetProperty("id").GetGuid(), quantity = 10 } } }));
        Assert.True(flaky.Fired);

        var tx = await TxAsync(o.Part);
        Assert.Equal(1, tx.Count(t => t.PurchaseOrderId == po && t.Type == StockTransactionType.Receipt));
        Assert.Equal(2, tx.Count);                                           // the first receipt and this one: nothing written twice
        Assert.Equal(20m, await OnHandSumAsync(o.Part));
        Assert.Equal(tx.Sum(t => t.Quantity), await OnHandSumAsync(o.Part)); // the ledger adds up to the balance
        var avg = await Loop.DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(p => p.Id == o.Part).Select(p => p.AverageCost).SingleAsync());
        Assert.Equal(Math.Round((10 * 10m + 10 * price) / 20, 4), avg);       // worked out once, from what was stored
        var mails = await Loop.DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().CountAsync(e => e.TenantId == o.Tenant && e.EventKey == "pr.received" && e.EntityId == req));
        Assert.Equal(1, mails);
    }

    [SkippableFact]
    public async Task Transfer_run_again_after_a_dropped_connection_posts_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await Loop.OrgAsync(fx, emailOn: false);
        var from = await Phone.StoreAsync(org.Admin, org.Site);
        var to = await Phone.StoreAsync(org.Admin, org.Site, "ST2");
        var part = await Phone.PartAsync(org.Admin, from);
        await Phone.ReceiveAsync(org.Admin, part, from, 10);
        var (c, flaky) = await FlakyAdminAsync(org.Admin);
        flaky.Arm("INSERT INTO [StockTransactions]");
        await Ok(await c.PostAsJsonAsync("/api/inventory/transfer", new { partId = part, fromStoreId = from, toStoreId = to, quantity = 4 }));
        Assert.True(flaky.Fired);
        var tx = await TxAsync(part);
        Assert.Equal(1, tx.Count(t => t.Type == StockTransactionType.TransferOut));
        Assert.Equal(1, tx.Count(t => t.Type == StockTransactionType.TransferIn));
        Assert.Equal(6m, await Phone.OnHandAsync(org.Admin, part, from));
        Assert.Equal(4m, await Phone.OnHandAsync(org.Admin, part, to));
        Assert.Equal(tx.Sum(t => t.Quantity), await OnHandSumAsync(part));
    }

    [SkippableFact]
    public async Task Stock_take_run_again_after_a_dropped_connection_posts_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await Loop.OrgAsync(fx, emailOn: false);
        var store = await Phone.StoreAsync(org.Admin, org.Site);
        var part = await Phone.PartAsync(org.Admin, store);
        await Phone.ReceiveAsync(org.Admin, part, store, 10);
        var (c, flaky) = await FlakyAdminAsync(org.Admin);
        flaky.Arm("INSERT INTO [StockTransactions]");
        await Ok(await c.PostAsJsonAsync("/api/inventory/stock-take", new { storeId = store, lines = new[] { new { partId = part, countedQuantity = 7 } } }));
        Assert.True(flaky.Fired);
        var adjustments = (await TxAsync(part)).Where(t => t.Type == StockTransactionType.Adjustment).ToList();
        Assert.Equal(-3m, Assert.Single(adjustments).Quantity);
        Assert.Equal(7m, await Phone.OnHandAsync(org.Admin, part, store));
    }

    [SkippableFact]
    public async Task Stock_count_post_run_again_after_a_dropped_connection_posts_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await Loop.OrgAsync(fx, emailOn: false);
        var store = await Phone.StoreAsync(org.Admin, org.Site);
        var part = await Phone.PartAsync(org.Admin, store);
        await Phone.ReceiveAsync(org.Admin, part, store, 3);
        await Loop.EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var count = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/stock-counts", new { storeId = store, name = "November" })));
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(org.Admin, "count.line", new { countId = count, partId = part, quantity = 1 })));
        var (c, flaky) = await FlakyAdminAsync(org.Admin);
        flaky.Arm("INSERT INTO [StockTransactions]");
        var posted = await Ok(await c.PostAsync($"/api/stock-counts/{count}/post", null));
        Assert.True(flaky.Fired);
        Assert.Equal(1, posted.GetProperty("adjusted").GetInt32());
        Assert.Equal(-2m, Assert.Single(await TxAsync(part), t => t.Type == StockTransactionType.Adjustment).Quantity);
        Assert.Equal(1m, await Phone.OnHandAsync(org.Admin, part, store));
        Assert.Equal("Posted", (await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}"))).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Serial_receive_run_again_after_a_dropped_connection_receives_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var part = await SerialReceiptTests.SerialPartAsync(o.Admin, o, "SR-FW", "S-0");
        var (c, flaky) = await FlakyAdminAsync(o.Admin);
        flaky.Arm("INSERT INTO [StockTransactions]");
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = o.Store, quantity = 2, unitCost = 800, serials = new[] { "F-1", "F-2" } }));
        Assert.True(flaky.Fired);
        Assert.Equal(["F-1", "F-2", "S-0"], (await SerialReceiptTests.UnitsAsync(fx, part)).Select(u => u.Serial));
        var tx = await TxAsync(part);
        Assert.Equal(2, tx.Count(t => t.Type == StockTransactionType.Receipt));
        Assert.Equal(3m, await SerialReceiptTests.OnHandAsync(o.Admin, part, o.Store));
        var avg = await Loop.DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(p => p.Id == part).Select(p => p.AverageCost).SingleAsync());
        Assert.Equal(Math.Round((500m + 2 * 800m) / 3, 4), avg);
    }

    // ---------------- Issue 2: a phone receive that loses the order's row-version race ----------------

    private async Task<(Loop.Org Org, Guid Store, Guid Part, Guid Po, Guid Line)> ApprovedOrderAsync(decimal qty = 10)
    {
        var org = await Loop.OrgAsync(fx, emailOn: false);
        var store = await Phone.StoreAsync(org.Admin, org.Site);
        var part = await Phone.PartAsync(org.Admin, store);
        await Loop.EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var supplier = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var po = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = org.Site, supplierId = supplier, currency = "USD", exchangeRate = 1, taxPercent = 0, shippingCost = 0,
            lines = new[] { new { partId = part, storeId = store, description = "Bearing", quantity = qty, unitPrice = 4, discountPercent = 0 } },
        })));
        await Ok(await org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await org.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = Id((await Ok(await org.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0]);
        return (org, store, part, po, line);
    }

    [SkippableFact]
    public async Task Phone_receive_that_loses_the_order_race_is_answered_not_a_server_error()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, part, po, line) = await ApprovedOrderAsync();
        // Someone else saves the order between this receipt reading it and writing it.
        var touch = new TouchOnce(() => Loop.DbAsync(fx, db => db.Database.ExecuteSqlRawAsync("UPDATE PurchaseOrders SET UpdatedAt = UpdatedAt WHERE Id = {0}", po)));
        var host = fx.Factory!.WithWebHostBuilder(b => b.ConfigureServices(s => s.ConfigureDbContext<CmmsDbContext>(x => x.AddInterceptors(touch))));
        var c = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.True((await c.PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Passw0rd!x", rememberMe = false })).IsSuccessStatusCode);
        touch.Arm("UPDATE [PurchaseOrders]");
        var clientId = Guid.NewGuid();
        var answer = await Phone.ActAsync(c, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 4 }, clientId: clientId);
        Assert.True(touch.Fired);
        Assert.Equal("rejected", Phone.Status(answer));
        Assert.Equal("This record was changed by someone else. Reload it and try again.", Phone.Message(answer));
        Assert.Equal(0m, await Phone.OnHandAsync(org.Admin, part, store));
        Assert.Equal(0m, await Loop.DbAsync(fx, db => db.PurchaseOrderLines.IgnoreQueryFilters().Where(l => l.Id == line).Select(l => l.QuantityReceived).SingleAsync()));
        // Sent again (the phone retries what it could not read), it gets the same answer rather than a second try.
        Assert.Equal("rejected", Phone.Status(await Phone.ActAsync(c, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 4 }, clientId: clientId)));
        Assert.Equal(0m, await Phone.OnHandAsync(org.Admin, part, store));
    }

    [SkippableFact]
    public async Task Two_phone_receives_of_one_order_line_at_once_are_both_answered()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, part, po, line) = await ApprovedOrderAsync(qty: 20);
        var done = 0;
        for (var round = 0; round < 4; round++)
        {
            var calls = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => org.Admin.PostAsJsonAsync("/api/tech/actions",
                new { clientId = Guid.NewGuid(), at = DateTimeOffset.UtcNow, type = "po.receive", payload = new { purchaseOrderId = po, lineId = line, quantity = 2 } })));
            foreach (var r in calls)
            {
                var body = await Loop.Ok(r);
                var status = Phone.Status(body);
                Assert.Contains(status, new[] { "done", "rejected" });
                if (status == "done") done++;
            }
        }
        Assert.True(done >= 4);
        Assert.Equal(done * 2m, await Phone.OnHandAsync(org.Admin, part, store));
        Assert.Equal(done * 2m, await Loop.DbAsync(fx, db => db.PurchaseOrderLines.IgnoreQueryFilters().Where(l => l.Id == line).Select(l => l.QuantityReceived).SingleAsync()));
        Assert.Equal(done, (await TxAsync(part)).Count(t => t.Type == StockTransactionType.Receipt));
    }

    // ---------------- Issue 4: ticking "Contractor" again with a rate ----------------

    [SkippableFact]
    public async Task Reticking_contractor_with_a_new_rate_keeps_the_kept_rates()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var elec = await CompanyTests.CategoryAsync(o.Admin, "ELEC", 100);
        var hvac = await CompanyTests.CategoryAsync(o.Admin, "HVAC", 90);
        var plumb = await CompanyTests.CategoryAsync(o.Admin, "PLMB", 80);
        var id = await CompanyTests.ContractorAsync(o.Admin, "LIFT", (elec, 150m), (hvac, 120m));
        var omar = (await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("contacts")[0].GetProperty("id").GetGuid();
        object Body(bool contractor, params object[] rates) => new
        {
            code = "LIFT", name = "LIFT Co", isSupplier = true, isContractor = contractor, isManufacturer = false, isActive = true, rates,
            contacts = new object[] { new { id = omar, name = "Omar Lift", role = "Engineer", phone = "0100", email = "omar@lift.test" } }, addresses = Array.Empty<object>(),
        };
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", Body(false)));
        // Ticked again: the form shows an empty rate table (the kept rates are hidden) and the person enters one rate, for a new category, and re-enters one of the kept.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", Body(true, new { categoryId = plumb, rate = 70m }, new { categoryId = elec, rate = 175m })));
        var rates = (await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("rates").EnumerateArray()
            .ToDictionary(r => r.GetProperty("categoryId").GetGuid(), r => r.GetProperty("rate").GetDecimal());
        Assert.Equal(3, rates.Count);
        Assert.Equal(175m, rates[elec]);   // a sent rate for an existing category updates it
        Assert.Equal(120m, rates[hvac]);   // the one not re-entered is kept
        Assert.Equal(70m, rates[plumb]);   // the new one is added
        // A contractor who saves again without the new one, on purpose, still clears it.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", Body(true, new { categoryId = plumb, rate = 70m })));
        Assert.Single((await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"))).GetProperty("rates").EnumerateArray());
    }

    // ---------------- Issue 5: "order from requests" is one attempt ----------------

    [SkippableFact]
    public async Task Order_from_requests_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var (tech, _, _) = await PurchaseRequestTests.UserAsync(fx, o.Admin, "Technician");
        var req = await PurchaseRequestTests.RequestAsync(tech, o, PurchaseRequestTests.PartLine(o, 3));
        await Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await Ok(await o.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{req}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var (c, flaky) = await FlakyAdminAsync(o.Admin);
        // The claim on the request is the first write to it; the request's new status is the second, and that one drops.
        flaky.Arm("UPDATE [PurchaseRequests]", skip: 1);
        var made = await Ok(await c.PostAsJsonAsync("/api/purchase-requests/order", new { lines = new[] { new { requestLineId = line } } }));
        Assert.True(flaky.Fired);
        var po = Id(made[0]);
        Assert.Equal(1, await Loop.DbAsync(fx, db => db.PurchaseOrders.IgnoreQueryFilters().CountAsync(x => x.Lines.Any(l => l.PurchaseRequestLineId == line))));
        Assert.Equal("Ordered", PurchaseRequestStatusOf(await Ok(await o.Admin.GetAsync($"/api/purchase-requests/{req}"))));
        Assert.Equal(1, (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines").GetArrayLength());
    }

    private static string PurchaseRequestStatusOf(JsonElement d) => d.GetProperty("status").GetString()!;

    // ---------------- Issue 7: the switch-on lock query ----------------

    [SkippableFact]
    public async Task Switch_on_lock_query_is_limited_to_the_organizations_own_rows()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 1, unitCost = 10 }));
        var u = await Loop.UserAsync(fx, o.Admin, "Administrator");
        var recorder = new CommandRecorder("FROM StockLevels WITH");
        var c = await FlakyOnce.HostAsync(fx, recorder, u.Email, Loop.Password);
        await Ok(await c.PostAsJsonAsync($"/api/parts/{o.Part}/serial-tracking", new { on = true, stores = new[] { new { storeId = o.Store, serials = new[] { "L-1" } } } }));
        var sql = Assert.Single(recorder.Seen);
        Assert.Contains("TenantId = @", sql);                                  // only this organization's lines, whatever plan the optimizer picks
        Assert.Contains("INDEX(IX_StockLevels_TenantId_PartId_StoreId)", sql); // a seek on (tenant, part): never a scan of everyone's stock under HOLDLOCK
        Assert.Contains("QuantityOnHand", sql);                                // the row itself is read, so the row is locked
        Assert.Equal(["L-1"], (await SerialReceiptTests.UnitsAsync(fx, o.Part)).Select(x => x.Serial));
    }
}
