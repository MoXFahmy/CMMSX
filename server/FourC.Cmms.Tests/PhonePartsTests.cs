using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class PhonePartsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid Store, Guid Part, Guid Me, Guid Wo);

    /// <summary>An organization whose admin is a technician with one job; a store with <paramref name="stock"/> of one part.</summary>
    private async Task<Setup> SetupAsync(decimal stock, bool emailOn = false)
    {
        var org = await OrgAsync(fx, emailOn);
        var store = await StoreAsync(org.Admin, org.Site);
        var part = await PartAsync(org.Admin, store);
        if (stock > 0) await ReceiveAsync(org.Admin, part, store, stock);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = await JobAsync(org.Admin, org, me.Id);
        return new Setup(org, store, part, me.Id, wo);
    }

    private static async Task<Guid> JobAsync(HttpClient c, Loop.Org org, Guid? assignee, string title = "Swap bearing") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective",
            assigneeIds = assignee is { } a ? new[] { a } : Array.Empty<Guid>(), dueDate = Iso(Today.AddDays(3)) })));

    private static async Task<JsonElement> LineAsync(HttpClient c, Guid wo) =>
        (await Ok(await c.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo).GetProperty("plannedLines").EnumerateArray().Single();

    [SkippableFact]
    public async Task Using_a_planned_part_issues_against_the_reservation()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        var line = Id(await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{s.Wo}/planned-parts", new { partId = s.Part, storeId = s.Store, quantity = 2 })));
        var answer = await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 2, lineId = line }, s.Wo);
        Assert.Equal("done", Status(answer));
        Assert.Null(Message(answer));
        Assert.Equal(3m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
        Assert.Equal(2m, (await LineAsync(s.Org.Admin, s.Wo)).GetProperty("issued").GetDecimal());
    }

    [SkippableFact]
    public async Task A_shelf_life_part_used_on_the_phone_goes_on_the_store_keepers_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(0);
        var oil = await PartAsync(s.Org.Admin, s.Store, "OIL-1", shelf: true);
        var answer = await ActAsync(s.Org.Admin, "part.use", new { partId = oil, storeId = s.Store, quantity = 2 }, s.Wo);
        Assert.Equal("done", Status(answer));
        Assert.Equal("OIL-1 is a shelf-life part: added to the job's parts; the store keeper issues it with its batch.", Message(answer));
        var line = (await Ok(await s.Org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == s.Wo)
            .GetProperty("plannedLines").EnumerateArray().Single(l => l.GetProperty("partId").GetGuid() == oil);
        Assert.Equal(2m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(0m, line.GetProperty("issued").GetDecimal());
    }

    [SkippableFact]
    public async Task Parts_used_without_a_plan_show_on_the_job()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        Assert.Equal("done", Status(await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 2 }, s.Wo)));
        var bundle = await Ok(await s.Org.Admin.GetAsync("/api/tech/bundle"));
        Assert.True(bundle.GetProperty("can").GetProperty("issueStock").GetBoolean());
        var job = bundle.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == s.Wo);
        Assert.Empty(job.GetProperty("plannedLines").EnumerateArray());
        var used = Assert.Single(job.GetProperty("usedParts").EnumerateArray());
        Assert.Equal(s.Part, used.GetProperty("partId").GetGuid());
        Assert.Equal(s.Store, used.GetProperty("storeId").GetGuid());
        Assert.Equal(2m, used.GetProperty("issued").GetDecimal());
    }

    [SkippableFact]
    public async Task Part_use_is_applied_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        var id = Guid.NewGuid();
        await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 1 }, s.Wo, id);
        await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 1 }, s.Wo, id);
        Assert.Equal(4m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
    }

    [SkippableFact]
    public async Task Short_stock_becomes_a_planned_line()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(2);
        var answer = await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 5 }, s.Wo);
        Assert.Equal("done", Status(answer));
        Assert.Equal("Only 2 in the store; the rest is on the store keeper's list.", Message(answer));
        Assert.Equal(0m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
        var line = await LineAsync(s.Org.Admin, s.Wo);
        Assert.Equal(5m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(2m, line.GetProperty("issued").GetDecimal());
        var shortages = await Ok(await s.Org.Admin.GetAsync($"/api/inventory/shortages?storeId={s.Store}"));
        var row = Assert.Single(shortages.EnumerateArray());
        Assert.Equal(s.Wo, row.GetProperty("workOrderId").GetGuid());
        Assert.Equal(3m, row.GetProperty("short").GetDecimal());
    }

    [SkippableFact]
    public async Task Taking_stock_reserved_for_other_work_is_issued_with_a_note()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(2);
        var other = await JobAsync(s.Org.Admin, s.Org, null, "Other job");
        await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/work-orders/{other}/planned-parts", new { partId = s.Part, storeId = s.Store, quantity = 2 }));
        var answer = await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 2 }, s.Wo);
        Assert.Equal("done", Status(answer));
        Assert.StartsWith("This took parts reserved for", Message(answer));
        Assert.Equal(0m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
    }

    [SkippableFact]
    public async Task Returning_is_limited_to_what_was_issued()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        await ActAsync(s.Org.Admin, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 2 }, s.Wo);
        Assert.Equal("rejected", Status(await ActAsync(s.Org.Admin, "part.return", new { partId = s.Part, storeId = s.Store, quantity = 3 }, s.Wo)));
        Assert.Equal("done", Status(await ActAsync(s.Org.Admin, "part.return", new { partId = s.Part, storeId = s.Store, quantity = 1 }, s.Wo)));
        Assert.Equal(4m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
    }

    [SkippableFact]
    public async Task Stores_of_other_sites_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        var other = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var far = await StoreAsync(s.Org.Admin, other, "FAR");
        await ReceiveAsync(s.Org.Admin, s.Part, far, 5);
        var tech = await UserAsync(fx, s.Org.Admin, "Technician", siteIds: [s.Org.Site]);
        var emp = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/employees", new { code = "T2", fullName = "Tech", userId = tech.Id, siteIds = new[] { s.Org.Site }, isContractor = false })));
        var wo = await JobAsync(s.Org.Admin, s.Org, emp);
        Assert.Equal("rejected", Status(await ActAsync(tech.Client, "part.use", new { partId = s.Part, storeId = far, quantity = 1 }, wo)));
        Assert.Equal(5m, await OnHandAsync(s.Org.Admin, s.Part, far));
    }

    [SkippableFact]
    public async Task Without_stock_permission_the_part_is_asked_for_instead()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        var role = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/roles", new { name = "Field only", description = "No stock", permissions = new[] { "workorders.view", "workorders.execute" } })));
        var email = Addr("field");
        var user = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/users", new { fullName = "Field", email, isActive = true, roleIds = new[] { role }, password = Loop.Password })));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = Loop.Password, rememberMe = false }));
        var emp = Id(await Ok(await s.Org.Admin.PostAsJsonAsync("/api/employees", new { code = "F1", fullName = "Field", userId = user, isContractor = false })));
        var wo = await JobAsync(s.Org.Admin, s.Org, emp);
        var answer = await ActAsync(c, "part.use", new { partId = s.Part, storeId = s.Store, quantity = 2 }, wo);
        Assert.Equal("done", Status(answer));
        Assert.Equal("Added to the job's parts; the store keeper issues them.", Message(answer));
        Assert.Equal(5m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
        Assert.Equal("rejected", Status(await ActAsync(c, "stock.issue", new { partId = s.Part, storeId = s.Store, quantity = 1, workOrderId = wo }, wo)));
    }

    [SkippableFact]
    public async Task A_store_keeper_issues_to_any_open_job()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(5);
        var other = await JobAsync(s.Org.Admin, s.Org, null, "Not mine");
        var answer = await ActAsync(s.Org.Admin, "stock.issue", new { partId = s.Part, storeId = s.Store, quantity = 1, workOrderId = other }, other);
        Assert.Equal("done", Status(answer));
        Assert.Equal(4m, await OnHandAsync(s.Org.Admin, s.Part, s.Store));
    }
}

public class PhoneStockPermissionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>A person with this role, linked to an employee and assigned to one job.</summary>
    private async Task<(HttpClient Client, Guid Wo)> PersonAsync(Loop.Org org, string[] permissions)
    {
        var role = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/roles", new { name = $"R{Guid.NewGuid():N}"[..10], description = "Test", permissions })));
        var email = Addr("phone");
        var user = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/users", new { fullName = "Phone", email, isActive = true, roleIds = new[] { role }, password = Loop.Password })));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = Loop.Password, rememberMe = false }));
        var emp = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = $"E{Guid.NewGuid():N}"[..8], fullName = "Phone", userId = user, isContractor = false })));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { emp } })));
        return (c, wo);
    }

    [SkippableFact]
    public async Task Role_without_stock_permission_gets_no_stock_actions_in_the_bundle()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        var part = await PartAsync(org.Admin, store);
        await ReceiveAsync(org.Admin, part, store, 5);
        var count = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/stock-counts", new { storeId = store, name = "November" })));
        await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);

        var viewOnly = await PersonAsync(org, ["workorders.view", "workorders.execute", "inventory.view"]);
        Assert.False((await Ok(await viewOnly.Client.GetAsync("/api/tech/bundle"))).GetProperty("can").GetProperty("issueStock").GetBoolean());
        Assert.Empty((await Ok(await viewOnly.Client.GetAsync("/api/tech/stock"))).GetProperty("counts").EnumerateArray());

        // Someone with the stock permission still gets the counts.
        var keeper = await PersonAsync(org, ["workorders.view", "workorders.execute", "inventory.view", "inventory.transact"]);
        Assert.True((await Ok(await keeper.Client.GetAsync("/api/tech/bundle"))).GetProperty("can").GetProperty("issueStock").GetBoolean());
        Assert.Contains((await Ok(await keeper.Client.GetAsync("/api/tech/stock"))).GetProperty("counts").EnumerateArray(), c => Id(c) == count);

        // The server refuses the actions too, whatever the phone shows.
        var line = await ActAsync(viewOnly.Client, "count.line", new { countId = count, partId = part, quantity = 3 });
        Assert.Equal(("rejected", "You don't have permission to do this."), (Status(line), Message(line)));
        var issue = await ActAsync(viewOnly.Client, "stock.issue", new { partId = part, storeId = store, quantity = 1, workOrderId = viewOnly.Wo }, viewOnly.Wo);
        Assert.Equal("rejected", Status(issue));
        Assert.Equal("done", Status(await ActAsync(keeper.Client, "count.line", new { countId = count, partId = part, quantity = 3 })));
        Assert.Equal(5m, await OnHandAsync(org.Admin, part, store));
    }
}

public class PhoneLastPartTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Two_phones_issuing_the_last_part_make_the_second_short()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        // Several rounds, because the two reads only collide some of the time.
        for (var round = 0; round < 4; round++)
        {
            var part = await PartAsync(org.Admin, store, $"LAST-{round}");
            await ReceiveAsync(org.Admin, part, store, 1);
            var jobs = new List<Guid>();
            for (var i = 0; i < 6; i++)
                jobs.Add(Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = $"Job {round}-{i}", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id } }))));
            // Six phones, one part left: all of them are told "done".
            var answers = await Task.WhenAll(jobs.Select(wo => ActAsync(org.Admin, "part.use", new { partId = part, storeId = store, quantity = 1 }, wo)));
            Assert.All(answers, a => Assert.Equal("done", Status(a)));
            Assert.Equal(5, answers.Count(a => Message(a) == "Only 0 in the store; the rest is on the store keeper's list."));
            // Stock ends at 0 (never negative); one phone got the part, the others are short and on the store keeper's list.
            Assert.Equal(0m, await OnHandAsync(org.Admin, part, store));
            Assert.Equal(1, await DbAsync(fx, db => db.StockTransactions.IgnoreQueryFilters().CountAsync(t => t.PartId == part && t.Type == StockTransactionType.Issue)));
            var shortages = (await Ok(await org.Admin.GetAsync($"/api/inventory/shortages?storeId={store}"))).EnumerateArray()
                .Where(r => r.GetProperty("partId").GetGuid() == part).ToList();
            Assert.Equal(5, shortages.Count);
            Assert.All(shortages, r => Assert.Equal(1m, r.GetProperty("short").GetDecimal()));
            Assert.Equal(5, await DbAsync(fx, db => db.WorkOrderParts.IgnoreQueryFilters().CountAsync(l => l.PartId == part)));
        }
    }
}

public class PhoneReceiveTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<(Loop.Org Org, Guid Store, Guid Part, Guid Po, Guid Line)> ApprovedOrderAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        var part = await PartAsync(org.Admin, store);
        await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var supplier = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var po = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = org.Site, supplierId = supplier, currency = "USD", exchangeRate = 1, taxPercent = 0, shippingCost = 0,
            lines = new[] { new { partId = part, storeId = store, description = "Bearing", quantity = 10, unitPrice = 4, discountPercent = 0 } },
        })));
        await Ok(await org.Admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Ok(await org.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = Id((await Ok(await org.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0]);
        return (org, store, part, po, line);
    }

    [SkippableFact]
    public async Task Receiving_on_the_phone_updates_stock_and_the_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, part, po, line) = await ApprovedOrderAsync();
        var stock = await Ok(await org.Admin.GetAsync("/api/tech/stock"));
        Assert.Contains(stock.GetProperty("purchaseOrders").EnumerateArray(), o => Id(o) == po);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 4 })));
        Assert.Equal(4m, await OnHandAsync(org.Admin, part, store));
        var order = await Ok(await org.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal("PartiallyReceived", order.GetProperty("status").GetString());
        Assert.Equal("rejected", Status(await ActAsync(org.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 7 })));
    }

    [SkippableFact]
    public async Task Receiving_on_the_phone_is_applied_once_and_needs_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, part, po, line) = await ApprovedOrderAsync();
        var id = Guid.NewGuid();
        await ActAsync(org.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 2 }, clientId: id);
        await ActAsync(org.Admin, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 2 }, clientId: id);
        Assert.Equal(2m, await OnHandAsync(org.Admin, part, store));
        var tech = await UserAsync(fx, org.Admin, "Technician");
        await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "T9", fullName = "Tech", userId = tech.Id, isContractor = false }));
        Assert.Equal("rejected", Status(await ActAsync(tech.Client, "po.receive", new { purchaseOrderId = po, lineId = line, quantity = 1 })));
    }
}
