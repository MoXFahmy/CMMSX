using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class InventoryTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(HttpClient admin, Guid site, Guid store, Guid supplier)> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Stores Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var store = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = site })));
        var supplier = Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        return (c, site, store, supplier);
    }

    private static async Task<Guid> PartAsync(HttpClient c, Guid store, string no, Guid? supplier = null, decimal reorderLevel = 0, decimal reorderQty = 0, decimal max = 0) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = no, name = $"Part {no}", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false, preferredSupplierId = supplier,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel, maxLevel = max, reorderQuantity = reorderQty } },
        })));

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = site, type = "Corrective", priority = "Medium" })));

    [SkippableFact]
    public async Task Stock_ledger_keeps_average_cost_balances_and_work_order_costs()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, store, _) = await SetupAsync();
        var store2 = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "SAT", name = "Satellite", siteId = site })));
        var part = await PartAsync(c, store, "P-1");
        var wo = await WorkOrderAsync(c, site);

        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 10, unitCost = 5 }));
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 10, unitCost = 7 }));
        Assert.Equal(6m, (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("averageCost").GetDecimal());

        await Ok(await c.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = store, quantity = 3, workOrderId = wo }));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = store, quantity = 100, workOrderId = wo })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/inventory/return", new { partId = part, storeId = store, quantity = 4, workOrderId = wo })).StatusCode);
        await Ok(await c.PostAsJsonAsync("/api/inventory/return", new { partId = part, storeId = store, quantity = 1, workOrderId = wo }));
        await Ok(await c.PostAsJsonAsync("/api/inventory/transfer", new { partId = part, fromStoreId = store, toStoreId = store2, quantity = 2 }));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/inventory/adjust", new { partId = part, storeId = store, quantity = -1, notes = "" })).StatusCode);
        await Ok(await c.PostAsJsonAsync("/api/inventory/adjust", new { partId = part, storeId = store, quantity = -1, notes = "Damaged" }));

        var p = await Ok(await c.GetAsync($"/api/parts/{part}"));
        Assert.Equal(17m, p.GetProperty("onHand").GetDecimal()); // 20 - 3 + 1 - 1 (the transfer only moves stock)
        var levels = p.GetProperty("levels").EnumerateArray().ToDictionary(l => l.GetProperty("storeId").GetGuid(), l => l.GetProperty("quantityOnHand").GetDecimal());
        Assert.Equal(15m, levels[store]);
        Assert.Equal(2m, levels[store2]);

        // Stock take posts only the difference.
        var take = await Ok(await c.PostAsJsonAsync("/api/inventory/stock-take", new { storeId = store, lines = new[] { new { partId = part, countedQuantity = 12 } } }));
        Assert.Equal(1, take.GetProperty("adjusted").GetInt32());

        var totals = (await Ok(await c.GetAsync($"/api/work-orders/{wo}"))).GetProperty("totals");
        Assert.Equal(12m, totals.GetProperty("partsCost").GetDecimal()); // (3 - 1) × 6
        Assert.Equal(12m, totals.GetProperty("totalCost").GetDecimal());
    }

    [SkippableFact]
    public async Task Concurrent_issues_never_take_stock_below_zero()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, store, _) = await SetupAsync();
        var part = await PartAsync(c, store, "P-RACE");
        var wo = await WorkOrderAsync(c, site);
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 5, unitCost = 1 }));
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => c.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = store, quantity = 1, workOrderId = wo })));
        Assert.Equal(5, results.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(0m, (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("onHand").GetDecimal());
    }

    [SkippableFact]
    public async Task Proposal_to_purchase_order_through_approval_and_receipt()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, store, supplier) = await SetupAsync();
        var part = await PartAsync(c, store, "P-RE", supplier, reorderLevel: 5, reorderQty: 10, max: 20);
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 2, unitCost = 10 }));

        var proposal = await Ok(await c.GetAsync("/api/inventory/proposal"));
        var line = proposal.EnumerateArray().Single(x => x.GetProperty("partId").GetGuid() == part);
        Assert.Equal(18m, line.GetProperty("suggestedQuantity").GetDecimal()); // up to max: 20 - 2

        var created = await Ok(await c.PostAsJsonAsync("/api/purchase-orders/from-proposal", new { lines = new[] { new { partId = part, storeId = store, quantity = 18, unitPrice = 10m } } }));
        var po = Id(created.EnumerateArray().Single());
        Assert.DoesNotContain((await Ok(await c.GetAsync("/api/inventory/proposal"))).EnumerateArray(), x => x.GetProperty("partId").GetGuid() == part); // now covered by the order

        // An approver with a 100 limit cannot approve a 180 order.
        var roles = await Ok(await c.GetAsync("/api/roles"));
        var manager = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Maintenance Manager").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await c.PostAsJsonAsync("/api/users", new { fullName = "Buyer", email, isActive = true, roleIds = new[] { manager }, password = "Passw0rd!u", purchaseApprovalLimit = 100 }));
        var buyer = fx.Client();
        await Ok(await buyer.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        await Ok(await buyer.PostAsync($"/api/purchase-orders/{po}/submit", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.PostAsync($"/api/purchase-orders/{po}/approve", null)).StatusCode);
        Assert.True((await Ok(await buyer.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("actions").GetProperty("approvalLimitExceeded").GetBoolean());
        await Ok(await c.PostAsync($"/api/purchase-orders/{po}/approve", null)); // admin: no limit

        var detail = await Ok(await c.GetAsync($"/api/purchase-orders/{po}"));
        var lineId = detail.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Ok(await c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId, quantity = 8 } } }));
        Assert.Equal("PartiallyReceived", (await Ok(await c.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId, quantity = 11 } } })).StatusCode);
        await Ok(await c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId, quantity = 10 } } }));
        Assert.Equal("Received", (await Ok(await c.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/purchase-orders/{po}/cancel", new { reason = "x" })).StatusCode);
        Assert.Equal(20m, (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("onHand").GetDecimal());
    }

    [SkippableFact]
    public async Task Service_lines_are_charged_to_the_work_order_in_base_currency()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, _, supplier) = await SetupAsync();
        var wo = await WorkOrderAsync(c, site);
        var po = Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = site, supplierId = supplier, currency = "EUR", exchangeRate = 2, taxPercent = 10, shippingCost = 5,
            lines = new[] { new { description = "Motor rewind", quantity = 1, unitPrice = 100, discountPercent = 10, workOrderId = wo } },
        })));
        var totals = (await Ok(await c.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("totals");
        Assert.Equal(104m, totals.GetProperty("total").GetDecimal());      // 90 + 9 tax + 5 shipping
        Assert.Equal(208m, totals.GetProperty("totalInBase").GetDecimal());
        await Ok(await c.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var lineId = (await Ok(await c.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Ok(await c.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId, quantity = 1 } } }));
        Assert.Equal(180m, (await Ok(await c.GetAsync($"/api/work-orders/{wo}"))).GetProperty("totals").GetProperty("otherCost").GetDecimal()); // 90 EUR × 2
    }

    [SkippableFact]
    public async Task Reports_run_and_respect_permissions_and_tenants()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site, store, _) = await SetupAsync();
        var part = await PartAsync(c, store, "P-REP");
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = 3, unitCost = 4 }));
        foreach (var r in (await Ok(await c.GetAsync("/api/reports"))).EnumerateArray())
        {
            var key = r.GetProperty("key").GetString();
            var report = await Ok(await c.GetAsync($"/api/reports/{key}"));
            Assert.True(report.GetProperty("columns").GetArrayLength() > 0, key);
        }
        var valuation = await Ok(await c.GetAsync("/api/reports/stock-valuation"));
        Assert.Equal(12m, valuation.GetProperty("summary")[0].GetProperty("value").GetDecimal());

        // A technician can move stock but cannot open reports.
        var roles = await Ok(await c.GetAsync("/api/roles"));
        var tech = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Technician").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await c.PostAsJsonAsync("/api/users", new { fullName = "Tech", email, isActive = true, roleIds = new[] { tech }, password = "Passw0rd!u" }));
        var t = fx.Client();
        await Ok(await t.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        Assert.Equal(HttpStatusCode.Forbidden, (await t.GetAsync("/api/reports/stock-valuation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await t.PostAsJsonAsync("/api/purchase-orders", new { siteId = site })).StatusCode);

        // Another organisation cannot see the part.
        var (other, _, _, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/parts/{part}")).StatusCode);
        Assert.Equal(0, (await Ok(await other.GetAsync("/api/parts"))).GetProperty("total").GetInt32());
    }
}
