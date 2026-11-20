using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class PlannedPartsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static async Task<string> Fails(HttpResponseMessage r, HttpStatusCode code)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == code, $"expected {(int)code}, got {(int)r.StatusCode}: {body}");
        return body;
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Org(HttpClient C, Guid SiteA, Guid Main, Guid Sat, Guid SiteB, Guid Central);

    private async Task<Org> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Planners Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var siteA = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "A", name = "Site A", isActive = true })));
        var siteB = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "B", name = "Site B", isActive = true })));
        var main = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = siteA })));
        var sat = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "SAT", name = "Satellite", siteId = siteA })));
        var central = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "CEN", name = "Central", siteId = siteB })));
        return new Org(c, siteA, main, sat, siteB, central);
    }

    private static async Task<Guid> PartAsync(HttpClient c, string no, params Guid[] stores) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = no, name = $"Part {no}", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = stores.Select(s => new { storeId = s, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 }).ToArray(),
        })));

    private static async Task ReceiveAsync(HttpClient c, Guid part, Guid store, decimal qty, decimal cost = 5) =>
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity = qty, unitCost = cost }));

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site, int? dueInDays = null, Guid[]? assignees = null, decimal? estimatedHours = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Job", siteId = site, type = "Corrective", priority = "Medium", dueDate = dueInDays is { } d ? Today.AddDays(d) : (DateOnly?)null,
            assigneeIds = assignees ?? [], estimatedHours,
        })));

    private static async Task<Guid> EmployeeAsync(HttpClient c, string code, decimal? rate) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code, fullName = $"Tech {code}", isContractor = false, hourlyRate = rate })));

    private static async Task<Guid> PlanAsync(HttpClient c, Guid wo, Guid part, decimal qty, Guid? store = null) =>
        Id(await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, storeId = store, quantity = qty })));

    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid wo) => await Ok(await c.GetAsync($"/api/work-orders/{wo}"));

    private static async Task<JsonElement> LineAsync(HttpClient c, Guid wo, Guid part) =>
        (await DetailAsync(c, wo)).GetProperty("plannedParts").EnumerateArray().Single(l => l.GetProperty("partId").GetGuid() == part);

    private static async Task StatusAsync(HttpClient c, Guid wo, string status) =>
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status, completionNotes = "Done" }));

    private static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    [SkippableFact]
    public async Task Planned_parts_reserve_stock_until_issued_and_show_shortages_by_due_date()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 10);
        var wo1 = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 1);
        var wo2 = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 5);
        await PlanAsync(o.C, wo2, part, 6);
        await PlanAsync(o.C, wo1, part, 6);

        var l1 = await LineAsync(o.C, wo1, part);
        var l2 = await LineAsync(o.C, wo2, part);
        Assert.Equal(o.Main, l1.GetProperty("storeId").GetGuid());
        Assert.Equal((6m, 0m), (Dec(l1, "reserved"), Dec(l1, "short")));
        Assert.Equal((6m, 2m), (Dec(l2, "reserved"), Dec(l2, "short")));

        await Ok(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 4, workOrderId = wo1, confirmReserved = true }));
        l1 = await LineAsync(o.C, wo1, part);
        Assert.Equal((4m, 2m, 0m), (Dec(l1, "issued"), Dec(l1, "reserved"), Dec(l1, "short")));
    }

    [SkippableFact]
    public async Task Completing_releases_and_reopening_restores_the_reservation()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 3);

        await StatusAsync(o.C, wo, "Completed");
        Assert.Equal(0m, Dec(await LineAsync(o.C, wo, part), "reserved"));
        await StatusAsync(o.C, wo, "InProgress");
        Assert.Equal(3m, Dec(await LineAsync(o.C, wo, part), "reserved"));
        await StatusAsync(o.C, wo, "Cancelled");
        Assert.Equal(0m, Dec(await LineAsync(o.C, wo, part), "reserved"));
    }

    [SkippableFact]
    public async Task Issuing_from_another_store_counts_against_the_plan()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main, o.Sat);
        await ReceiveAsync(o.C, part, o.Main, 5);
        await ReceiveAsync(o.C, part, o.Sat, 5);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 2, o.Main);

        await Ok(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Sat, quantity = 2, workOrderId = wo }));
        var l = await LineAsync(o.C, wo, part);
        Assert.Equal((2m, 0m), (Dec(l, "issued"), Dec(l, "reserved")));
        Assert.Equal(o.Main, l.GetProperty("storeId").GetGuid());
    }

    [SkippableFact]
    public async Task Default_store_prefers_the_sites_store_then_a_central_store()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var local = await PartAsync(o.C, "P-LOCAL", o.Main, o.Sat, o.Central);
        await ReceiveAsync(o.C, local, o.Sat, 3);
        await ReceiveAsync(o.C, local, o.Central, 50);
        var centralOnly = await PartAsync(o.C, "P-CEN", o.Central);
        var nowhere = await PartAsync(o.C, "P-NONE");
        var wo = await WorkOrderAsync(o.C, o.SiteA);

        await PlanAsync(o.C, wo, local, 1);
        await PlanAsync(o.C, wo, centralOnly, 1);
        await PlanAsync(o.C, wo, nowhere, 1);
        Assert.Equal(o.Sat, (await LineAsync(o.C, wo, local)).GetProperty("storeId").GetGuid()); // own site first, most available
        Assert.Equal(o.Central, (await LineAsync(o.C, wo, centralOnly)).GetProperty("storeId").GetGuid());
        var none = await LineAsync(o.C, wo, nowhere);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("storeId").ValueKind);
        Assert.Equal((0m, 0m), (Dec(none, "reserved"), Dec(none, "short")));
    }

    [SkippableFact]
    public async Task Planning_the_same_part_twice_is_refused_with_a_clear_message()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 1);
        var body = await Fails(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, quantity = 2 }), HttpStatusCode.BadRequest);
        Assert.Contains("already planned", body);
        await Fails(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, quantity = 0 }), HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Planned_hours_and_parts_give_the_planned_cost()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 10, cost: 5);
        var tech = await EmployeeAsync(o.C, "T1", 20);
        var wo = await WorkOrderAsync(o.C, o.SiteA, assignees: [tech]);
        await PlanAsync(o.C, wo, part, 2);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo}/planned-hours", new { lines = new[] { new { employeeId = tech, plannedHours = 3m } } }));

        var d = await DetailAsync(o.C, wo);
        var planned = d.GetProperty("planned");
        Assert.Equal((3m, 60m, 10m, 70m), (Dec(planned, "laborHours"), Dec(planned, "laborCost"), Dec(planned, "partsCost"), Dec(planned, "totalCost")));
        Assert.Equal(3m, d.GetProperty("assignees")[0].GetProperty("plannedHours").GetDecimal());

        // No planned hours: the estimate, at the assignees' average rate.
        var a = await EmployeeAsync(o.C, "T2", 10);
        var b = await EmployeeAsync(o.C, "T3", 30);
        var wo2 = await WorkOrderAsync(o.C, o.SiteA, assignees: [a, b], estimatedHours: 4);
        var p2 = (await DetailAsync(o.C, wo2)).GetProperty("planned");
        Assert.Equal((4m, 80m), (Dec(p2, "laborHours"), Dec(p2, "laborCost")));
    }

    [SkippableFact]
    public async Task A_part_planned_on_a_work_order_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 1);
        var body = await Fails(await o.C.DeleteAsync($"/api/parts/{part}"), HttpStatusCode.BadRequest);
        Assert.Contains("planned", body);
    }

    [SkippableFact]
    public async Task Plans_are_locked_once_the_work_order_is_completed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        var line = await PlanAsync(o.C, wo, part, 1);
        await StatusAsync(o.C, wo, "Completed");
        await Fails(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = await PartAsync(o.C, "P-2", o.Main), quantity = 1 }), HttpStatusCode.BadRequest);
        await Fails(await o.C.PutAsJsonAsync($"/api/work-orders/{wo}/planned-parts/{line}", new { quantity = 5 }), HttpStatusCode.BadRequest);
        await Fails(await o.C.DeleteAsync($"/api/work-orders/{wo}/planned-parts/{line}"), HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Duplicating_copies_planned_parts_and_hours()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var t1 = await EmployeeAsync(o.C, "T1", 10);
        var t2 = await EmployeeAsync(o.C, "T2", 10);
        var src = await WorkOrderAsync(o.C, o.SiteA, assignees: [t1, t2]);
        await PlanAsync(o.C, src, part, 4);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{src}/planned-hours", new { lines = new[] { new { employeeId = t1, plannedHours = 2m }, new { employeeId = t2, plannedHours = 5m } } }));

        var copy = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-orders", new { title = "Copy", siteId = o.SiteA, type = "Corrective", priority = "Medium", assigneeIds = new[] { t1 }, copyPlanFromId = src })));
        var d = await DetailAsync(o.C, copy);
        var l = Assert.Single(d.GetProperty("plannedParts").EnumerateArray());
        Assert.Equal((part, 4m), (l.GetProperty("partId").GetGuid(), Dec(l, "quantity")));
        var a = Assert.Single(d.GetProperty("assignees").EnumerateArray());
        Assert.Equal(2m, a.GetProperty("plannedHours").GetDecimal());
    }
}
