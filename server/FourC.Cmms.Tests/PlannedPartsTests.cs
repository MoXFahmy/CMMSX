using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [SkippableFact]
    public async Task Taking_reserved_stock_asks_first_and_goes_through_when_confirmed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 5);
        var woA = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 3);
        await PlanAsync(o.C, woA, part, 4);

        var other = await WorkOrderAsync(o.C, o.SiteA); // an unplanned job
        var issue = new { partId = part, storeId = o.Main, quantity = 3, workOrderId = other };
        var body = JsonDocument.Parse(await Fails(await o.C.PostAsJsonAsync("/api/inventory/issue", issue), HttpStatusCode.Conflict)).RootElement;
        var r = Assert.Single(body.GetProperty("reserved").EnumerateArray());
        Assert.Equal((woA, 2m), (r.GetProperty("workOrderId").GetGuid(), r.GetProperty("moreShort").GetDecimal()));
        Assert.Equal(5m, (await Ok(await o.C.GetAsync($"/api/parts/{part}"))).GetProperty("onHand").GetDecimal());

        await Ok(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 3, workOrderId = other, confirmReserved = true }));
        Assert.Equal(2m, Dec(await LineAsync(o.C, woA, part), "short"));
    }

    [SkippableFact]
    public async Task The_most_urgent_job_takes_its_own_parts_without_a_question()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 10);
        var woB = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 1);
        var woA = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 30);
        await PlanAsync(o.C, woB, part, 5);
        await PlanAsync(o.C, woA, part, 10); // already 5 short
        await Ok(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 5, workOrderId = woB }));
    }

    [SkippableFact]
    public async Task Transfers_out_warn_too_and_adjustments_never_do()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main, o.Sat);
        await ReceiveAsync(o.C, part, o.Main, 5);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 4, o.Main);
        await Fails(await o.C.PostAsJsonAsync("/api/inventory/transfer", new { partId = part, fromStoreId = o.Main, toStoreId = o.Sat, quantity = 3 }), HttpStatusCode.Conflict);
        await Ok(await o.C.PostAsJsonAsync("/api/inventory/transfer", new { partId = part, fromStoreId = o.Main, toStoreId = o.Sat, quantity = 3, confirmReserved = true }));
        await Ok(await o.C.PostAsJsonAsync("/api/inventory/adjust", new { partId = part, storeId = o.Main, quantity = -1, notes = "Damaged" }));
    }

    [SkippableFact]
    public async Task Without_planned_parts_issuing_asks_nothing_new()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 2);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await Ok(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 1, workOrderId = wo }));
        await Fails(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 5, workOrderId = wo }), HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> LevelledPartAsync(HttpClient c, string no, Guid store, decimal reorderLevel, decimal reorderQty, decimal max = 0) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = no, name = $"Part {no}", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel, maxLevel = max, reorderQuantity = reorderQty } },
        })));

    private static async Task<JsonElement> ListRowAsync(HttpClient c, Guid part, string query = "") =>
        (await Ok(await c.GetAsync($"/api/parts?pageSize=100{query}"))).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == part);

    private static async Task<JsonElement?> ProposalRowAsync(HttpClient c, Guid part) =>
        (await Ok(await c.GetAsync("/api/inventory/proposal"))).EnumerateArray().Where(r => r.GetProperty("partId").GetGuid() == part).Select(r => (JsonElement?)r).FirstOrDefault();

    [SkippableFact]
    public async Task Part_page_and_list_show_reserved_and_available_and_who_holds_the_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 10);
        var wo1 = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 1);
        var wo2 = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 5);
        await PlanAsync(o.C, wo1, part, 6);
        await PlanAsync(o.C, wo2, part, 6);

        var p = await Ok(await o.C.GetAsync($"/api/parts/{part}"));
        var level = p.GetProperty("levels")[0];
        Assert.Equal((12m, -2m), (Dec(level, "reserved"), Dec(level, "available")));
        Assert.Equal((12m, -2m), (Dec(p, "reserved"), Dec(p, "available")));
        var holders = p.GetProperty("reservedFor").EnumerateArray().ToList();
        Assert.Equal([wo1, wo2], holders.Select(h => h.GetProperty("workOrderId").GetGuid()));
        Assert.Equal([0m, 2m], holders.Select(h => Dec(h, "short")));

        var row = await ListRowAsync(o.C, part);
        Assert.Equal((10m, 12m, -2m), (Dec(row, "onHand"), Dec(row, "reserved"), Dec(row, "available")));
    }

    [SkippableFact]
    public async Task Reorder_checks_use_available_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await LevelledPartAsync(o.C, "P-1", o.Main, reorderLevel: 5, reorderQty: 10);
        await ReceiveAsync(o.C, part, o.Main, 8);
        Assert.False((await ListRowAsync(o.C, part)).GetProperty("low").GetBoolean());
        Assert.Null(await ProposalRowAsync(o.C, part));

        await PlanAsync(o.C, await WorkOrderAsync(o.C, o.SiteA), part, 6);
        Assert.True((await ListRowAsync(o.C, part, "&belowReorder=true")).GetProperty("low").GetBoolean());
        Assert.Equal(1, (await Ok(await o.C.GetAsync("/api/alerts"))).GetProperty("partsBelowReorder").GetInt32());
        var prop = await ProposalRowAsync(o.C, part);
        Assert.NotNull(prop);
        Assert.Equal((6m, 2m, 10m), (Dec(prop!.Value, "reserved"), Dec(prop.Value, "available"), Dec(prop.Value, "suggestedQuantity")));
        var report = (await Ok(await o.C.GetAsync("/api/reports/stock-reorder"))).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("partId").GetGuid() == part);
        Assert.Equal((6m, 2m, 10m), (Dec(report, "reserved"), Dec(report, "available"), Dec(report, "suggested")));
    }

    [SkippableFact]
    public async Task Proposal_covers_planned_need_where_no_max_level_is_set()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await LevelledPartAsync(o.C, "P-1", o.Main, reorderLevel: 0, reorderQty: 1);
        await ReceiveAsync(o.C, part, o.Main, 2);
        await PlanAsync(o.C, await WorkOrderAsync(o.C, o.SiteA), part, 7);
        Assert.Equal(5m, Dec((await ProposalRowAsync(o.C, part))!.Value, "suggestedQuantity"));
    }

    [SkippableFact]
    public async Task Nothing_changes_without_plans()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await LevelledPartAsync(o.C, "P-1", o.Main, reorderLevel: 5, reorderQty: 10, max: 20);
        await ReceiveAsync(o.C, part, o.Main, 4);
        var row = await ListRowAsync(o.C, part);
        Assert.Equal((4m, 0m, 4m, true), (Dec(row, "onHand"), Dec(row, "reserved"), Dec(row, "available"), row.GetProperty("low").GetBoolean()));
        Assert.Equal(16m, Dec((await ProposalRowAsync(o.C, part))!.Value, "suggestedQuantity")); // max 20 − on hand 4, as before
        var report = (await Ok(await o.C.GetAsync("/api/reports/stock-reorder"))).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("partId").GetGuid() == part);
        Assert.Equal(16m, Dec(report, "suggested"));
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name, siteId = site, status = "Active", criticality = "High", warrantyType = "None" })));

    private static async Task<Guid> TaskAsync(HttpClient c, string code, object[] parts, decimal? estimatedHours = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code, name = $"Task {code}", steps = new[] { "Check" }, estimatedHours, parts })));

    private static async Task<Guid> ScheduleAsync(HttpClient c, Guid site, Guid task, DateOnly start, Guid? asset = null, Guid[]? assets = null, object[]? extra = null, Guid[]? assignees = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "PM", siteId = site, assetId = asset, assetIds = assets, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled", frequency = 1,
            unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Iso(start), timeBased = false, extraTasks = extra ?? [], assigneeIds = assignees ?? [],
        })));

    private static async Task<List<Guid>> GenerateAsync(HttpClient c, Guid schedule) =>
        (await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(Today), scheduleIds = new[] { schedule } }))).GetProperty("results").EnumerateArray()
            .Where(r => r.GetProperty("workOrderId").ValueKind == JsonValueKind.String).Select(r => r.GetProperty("workOrderId").GetGuid()).ToList();

    private static async Task<List<(Guid Part, decimal Qty, Guid? Store)>> PlannedAsync(HttpClient c, Guid wo) =>
        (await DetailAsync(c, wo)).GetProperty("plannedParts").EnumerateArray()
            .Select(l => (l.GetProperty("partId").GetGuid(), Dec(l, "quantity"), l.GetProperty("storeId").ValueKind == JsonValueKind.Null ? (Guid?)null : l.GetProperty("storeId").GetGuid())).ToList();

    [SkippableFact]
    public async Task Generated_pm_work_orders_arrive_with_their_parts_planned()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var task = await TaskAsync(o.C, "T1", [new { partId = part, quantity = 2 }]);
        var sched = await ScheduleAsync(o.C, o.SiteA, task, Today, asset: await AssetAsync(o.C, o.SiteA, "Pump"));
        var wo = Assert.Single(await GenerateAsync(o.C, sched));
        Assert.Equal([(part, 2m, (Guid?)o.Main)], await PlannedAsync(o.C, wo));
        Assert.Equal(2m, Dec(await Ok(await o.C.GetAsync($"/api/parts/{part}")), "reserved"));
        var detail = await Ok(await o.C.GetAsync($"/api/pm-tasks/{task}"));
        Assert.Equal(part, Assert.Single(detail.GetProperty("parts").EnumerateArray()).GetProperty("partId").GetGuid());
    }

    [SkippableFact]
    public async Task A_round_plans_parts_for_every_covered_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var task = await TaskAsync(o.C, "T1", [new { partId = part, quantity = 2 }]);
        var assets = new[] { await AssetAsync(o.C, o.SiteA, "A1"), await AssetAsync(o.C, o.SiteA, "A2"), await AssetAsync(o.C, o.SiteA, "A3") };
        var wo = Assert.Single(await GenerateAsync(o.C, await ScheduleAsync(o.C, o.SiteA, task, Today, assets: assets)));
        Assert.Equal(6m, Assert.Single(await PlannedAsync(o.C, wo)).Qty);
    }

    [SkippableFact]
    public async Task Multiple_pm_plans_the_parts_of_the_task_used_that_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var p1 = await PartAsync(o.C, "P-1", o.Main);
        var p2 = await PartAsync(o.C, "P-2", o.Main);
        var routine = await TaskAsync(o.C, "W", [new { partId = p1, quantity = 1 }]);
        var bigger = await TaskAsync(o.C, "M", [new { partId = p2, quantity = 1 }]);
        var sched = await ScheduleAsync(o.C, o.SiteA, routine, Today.AddDays(-7), asset: await AssetAsync(o.C, o.SiteA, "Pump"), extra: [new { pmTaskId = bigger, everyNth = 2 }]);
        var wos = await GenerateAsync(o.C, sched);
        Assert.Equal(2, wos.Count);
        Assert.Equal([p1], (await PlannedAsync(o.C, wos[0])).Select(x => x.Part));
        Assert.Equal([p2], (await PlannedAsync(o.C, wos[1])).Select(x => x.Part));
    }

    [SkippableFact]
    public async Task Visit_cost_adds_parts_and_labour_for_schedule_and_forecast()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await ReceiveAsync(o.C, part, o.Main, 10, cost: 5);
        var task = await TaskAsync(o.C, "T1", [new { partId = part, quantity = 2 }], estimatedHours: 3);
        var techs = new[] { await EmployeeAsync(o.C, "T1", 10), await EmployeeAsync(o.C, "T2", 30) };
        var sched = await ScheduleAsync(o.C, o.SiteA, task, Today.AddDays(7), asset: await AssetAsync(o.C, o.SiteA, "Pump"), assignees: techs);

        var cost = (await Ok(await o.C.GetAsync($"/api/pm-schedules/{sched}"))).GetProperty("visitCost");
        Assert.Equal((10m, 60m, 70m), (Dec(cost, "parts"), Dec(cost, "labour"), Dec(cost, "total")));
        var rows = (await Ok(await o.C.GetAsync($"/api/pm/forecast?from={Iso(Today)}&to={Iso(Today.AddDays(30))}"))).EnumerateArray()
            .Where(r => r.GetProperty("id").GetGuid() == sched).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(70m, Dec(r, "cost")));
        Assert.Equal(10m, Dec(await Ok(await o.C.GetAsync($"/api/pm-tasks/{task}")), "partsCost"));
    }

    [SkippableFact]
    public async Task Task_parts_reject_duplicates_and_non_positive_quantities()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await Fails(await o.C.PostAsJsonAsync("/api/pm-tasks", new { code = "D", name = "Dup", parts = new[] { new { partId = part, quantity = 1 }, new { partId = part, quantity = 2 } } }), HttpStatusCode.BadRequest);
        await Fails(await o.C.PostAsJsonAsync("/api/pm-tasks", new { code = "Z", name = "Zero", parts = new[] { new { partId = part, quantity = 0 } } }), HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task A_part_on_a_pm_task_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        await TaskAsync(o.C, "T1", [new { partId = part, quantity = 1 }]);
        await Fails(await o.C.DeleteAsync($"/api/parts/{part}"), HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task A_part_used_only_on_deleted_work_orders_and_tasks_can_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 1);
        var task = await TaskAsync(o.C, "T1", [new { partId = part, quantity = 1 }]);
        (await o.C.DeleteAsync($"/api/work-orders/{wo}")).EnsureSuccessStatusCode();
        (await o.C.DeleteAsync($"/api/pm-tasks/{task}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await o.C.DeleteAsync($"/api/parts/{part}")).StatusCode);
    }

    [SkippableFact]
    public async Task Part_lookup_by_barcode_returns_the_barcode()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        await Ok(await o.C.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "BRG-1", name = "Bearing", unitOfMeasure = "EA", standardPrice = 10, barcode = "400123", isCritical = false, isShelfItem = false,
            allowNegativeStock = false, levels = Array.Empty<object>(),
        }));
        var found = (await Ok(await o.C.GetAsync("/api/parts/lookup?search=400123"))).EnumerateArray().ToList();
        Assert.Equal("400123", Assert.Single(found).GetProperty("barcode").GetString());
    }

    [SkippableFact]
    public async Task An_issue_that_cannot_go_through_says_why_before_asking_about_reserved_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = Id(await Ok(await o.C.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "GR-1", name = "Grease", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = true, allowNegativeStock = false,
            levels = new[] { new { storeId = o.Main, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await Ok(await o.C.PostAsJsonAsync("/api/inventory/receive", new
        {
            partId = part, storeId = o.Main, quantity = 5, unitCost = 5, batchNumber = "B1", expiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
        }));
        var reserving = await WorkOrderAsync(o.C, o.SiteA, dueInDays: 3);
        await PlanAsync(o.C, reserving, part, 4);
        var other = await WorkOrderAsync(o.C, o.SiteA);
        // The batch has expired: no point asking "Issue anyway?" about reserved stock first.
        var body = await Fails(await o.C.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Main, quantity = 3, workOrderId = other, batchNumber = "B1" }),
            HttpStatusCode.BadRequest);
        Assert.Contains("expired", body);
    }

    [SkippableFact]
    public async Task A_quantity_that_rounds_to_zero_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        // Quantities keep three decimals: 0.0004 would be saved as 0.
        await Fails(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, quantity = 0.0004m }), HttpStatusCode.BadRequest);
        await Fails(await o.C.PostAsJsonAsync("/api/pm-tasks", new { code = "T0", name = "Tiny", parts = new[] { new { partId = part, quantity = 0.0004m } } }), HttpStatusCode.BadRequest);
    }

    // ---- Final review fixes ----

    [SkippableFact]
    public async Task Reservation_view_reads_the_ledger_through_the_tenant_index()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await SetupAsync(); // the app (and its database) exists once a request has run
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var definition = await db.Database.SqlQueryRaw<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('StockReservations')) AS Value").SingleAsync();
        Assert.Contains("t.TenantId = wp.TenantId", definition); // StockTransactions' indexes start with TenantId
    }

    [SkippableFact]
    public async Task Planning_into_a_store_without_a_stock_level_shows_on_every_screen()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var part = await PartAsync(o.C, "P-1", o.Main);
        var wo = await WorkOrderAsync(o.C, o.SiteA);
        await PlanAsync(o.C, wo, part, 5, o.Sat);
        var p = await Ok(await o.C.GetAsync($"/api/parts/{part}"));
        var sat = p.GetProperty("levels").EnumerateArray().Single(l => l.GetProperty("storeId").GetGuid() == o.Sat);
        Assert.Equal((5m, -5m), (Dec(sat, "reserved"), Dec(sat, "available")));
        Assert.Equal(5m, Dec(p, "reserved"));
        Assert.Equal(wo, Assert.Single(p.GetProperty("reservedFor").EnumerateArray()).GetProperty("workOrderId").GetGuid());
        Assert.Equal(5m, Dec(await ListRowAsync(o.C, part), "reserved"));
    }

    [SkippableFact]
    public async Task Planned_hours_need_only_edit_work_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var role = Id(await Ok(await o.C.PostAsJsonAsync("/api/roles", new { name = "Supervisor", description = "No inventory", permissions = new[] { "workorders.view", "workorders.edit" } })));
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await o.C.PostAsJsonAsync("/api/users", new { fullName = "Supervisor", email, isActive = true, roleIds = new[] { role }, password = "Passw0rd!u" }));
        var sup = fx.Client();
        await Ok(await sup.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        var tech = await EmployeeAsync(o.C, "T1", 20);
        var wo = await WorkOrderAsync(o.C, o.SiteA, assignees: [tech]);

        await Ok(await sup.PutAsJsonAsync($"/api/work-orders/{wo}/planned-hours", new { lines = new[] { new { employeeId = tech, plannedHours = 2m } } }));
        await Fails(await sup.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = await PartAsync(o.C, "P-1", o.Main), quantity = 1 }), HttpStatusCode.Forbidden);
    }
}
