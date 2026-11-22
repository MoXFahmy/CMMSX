using System.Net.Http.Json;
using System.Text.Json;
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
