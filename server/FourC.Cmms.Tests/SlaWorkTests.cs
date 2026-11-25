using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>Deadlines from targets on requests and work orders (0.5.0).</summary>
public class SlaWorkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    /// <summary>A Cairo wall-clock time on a day, as the instant it is (daylight saving included).</summary>
    private static DateTimeOffset LocalToUtc(DateOnly day, int hour, int minute) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")), TimeSpan.Zero);

    private static DateTimeOffset? At(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDateTimeOffset() : null;
    private static void Near(DateTimeOffset expected, DateTimeOffset? actual, int toleranceSeconds = 120)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, expected.AddSeconds(-toleranceSeconds), expected.AddSeconds(toleranceSeconds));
    }

    private sealed record Org(HttpClient C, Guid Site, Guid Me);

    /// <summary>An organization in Cairo time whose admin is also a technician. Targets: Urgent 1 h / 4 h, Medium completes in 3 days.</summary>
    private async Task<Org> SetupAsync(bool targets = true)
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Targets Co", fullName = "Tess Tech", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        await Ok(await SlaSettingsTests.SaveAsync(c, s =>
        {
            s["timeZone"] = "Africa/Cairo";
            if (targets)
                s["slaTargets"] = new JsonArray(
                    new JsonObject { ["priority"] = "Urgent", ["respondMinutes"] = 60, ["completeMinutes"] = 240 },
                    new JsonObject { ["priority"] = "Medium", ["completeMinutes"] = 3 * 24 * 60 });
        }));
        var user = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "C", name = "Cairo plant", isActive = true })));
        var me = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tess Tech", isContractor = false, userId = user, siteIds = new[] { site } })));
        return new Org(c, site, me);
    }

    private static object Wo(Guid site, string priority = "Urgent", string? dueDate = null, DateTimeOffset? dueAt = null, string title = "Pump leak", string[]? tasks = null) =>
        new { title, siteId = site, type = "Corrective", priority, dueDate, dueAt, tasks = tasks ?? [] };

    private static async Task<Guid> CreateAsync(HttpClient c, object body) => Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", body)));
    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-orders/{id}"));
    private static async Task<JsonElement> RequestAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-requests/{id}"));

    private async Task WithDbAsync(Func<CmmsDbContext, Task> act)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        await act(scope.ServiceProvider.GetRequiredService<CmmsDbContext>());
    }

    [SkippableFact]
    public async Task Targets_set_deadlines_on_new_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        var d = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site)));
        Near(now.AddHours(1), At(d, "startBy"));
        Near(now.AddHours(4), At(d, "dueAt"));
        Assert.True(d.GetProperty("dueFromTarget").GetBoolean());
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(At(d, "dueAt")!.Value, cairo).DateTime), DateOnly.Parse(d.GetProperty("dueDate").GetString()!));
    }

    [SkippableFact]
    public async Task A_date_typed_by_hand_wins_and_means_end_of_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var day = Loop.Today.AddDays(40);
        var d = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site, dueDate: Loop.Iso(day))));
        Assert.Equal(Sla.EndOfDay(day, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")), At(d, "dueAt"));
        Assert.False(d.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task A_deadline_near_midnight_lands_on_the_local_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var day = Loop.Today.AddDays(40);
        var late = LocalToUtc(day, 23, 30);
        var d = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site, dueAt: late)));
        Assert.Equal(Loop.Iso(day), d.GetProperty("dueDate").GetString()); // 23:30 in Cairo, not the next day
        Assert.Equal(late, At(d, "dueAt"));
        Assert.False(d.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task Saving_without_touching_due_fields_keeps_the_target_deadline()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var id = await CreateAsync(o.C, Wo(o.Site));
        var before = await DetailAsync(o.C, id);
        // The form sends back what it loaded (the date and the time, to the minute).
        var minute = At(before, "dueAt")!.Value;
        minute = new DateTimeOffset(minute.Year, minute.Month, minute.Day, minute.Hour, minute.Minute, 0, TimeSpan.Zero);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{id}", new
        {
            title = "Pump leak (renamed)", siteId = o.Site, type = "Corrective", priority = "Urgent", dueDate = before.GetProperty("dueDate").GetString(), dueAt = minute,
            rowVersion = before.GetProperty("rowVersion").GetString(),
        }));
        var after = await DetailAsync(o.C, id);
        Assert.Equal(At(before, "dueAt"), At(after, "dueAt"));
        Assert.True(after.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task Changing_priority_recomputes_target_deadlines_but_not_hand_set_ones()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var target = await CreateAsync(o.C, Wo(o.Site, priority: "Medium"));
        var hand = await CreateAsync(o.C, Wo(o.Site, priority: "Medium", dueDate: "2027-01-15"));
        foreach (var id in new[] { target, hand })
        {
            var d = await DetailAsync(o.C, id);
            await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{id}", new
            {
                title = "Pump leak", siteId = o.Site, type = "Corrective", priority = "Urgent", dueDate = d.GetProperty("dueDate").GetString(),
                rowVersion = d.GetProperty("rowVersion").GetString(),
            }));
        }
        var t = await DetailAsync(o.C, target);
        Near(DateTimeOffset.Parse(t.GetProperty("createdAt").GetString()!).AddHours(4), At(t, "dueAt"));
        Assert.True(t.GetProperty("dueFromTarget").GetBoolean());
        Assert.Equal(DateTimeOffset.Parse("2027-01-15T21:59:59Z"), At(await DetailAsync(o.C, hand), "dueAt"));
    }

    [SkippableFact]
    public async Task Requests_get_deadlines_and_approval_with_a_new_priority_counts_from_when_raised()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Noisy fan", siteId = o.Site, priority = "Medium", type = "Corrective" })));
        var r = await RequestAsync(o.C, rid);
        Near(now.AddDays(3), At(r, "dueAt"));
        Assert.Null(At(r, "respondBy")); // Medium has no response target

        var raised = now.AddHours(-10);
        await WithDbAsync(db => db.WorkRequests.IgnoreQueryFilters().Where(x => x.Id == rid).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, raised)));
        var approved = await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { priority = "Urgent" }));
        var w = await DetailAsync(o.C, approved.GetProperty("workOrderId").GetGuid());
        Near(raised.AddHours(4), At(w, "dueAt"));
        Assert.True(w.GetProperty("dueFromTarget").GetBoolean());
        Assert.Null(At(w, "startBy")); // the request's decision was the response
        Assert.NotNull(At(await RequestAsync(o.C, rid), "decidedAt"));
    }

    [SkippableFact]
    public async Task Needed_by_is_the_deadline_only_without_a_target()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        var low = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Paint door", siteId = o.Site, priority = "Low", type = "Corrective", requiredBy = "2027-02-10" })));
        Assert.Equal(DateTimeOffset.Parse("2027-02-10T21:59:59Z"), At(await RequestAsync(o.C, low), "dueAt"));
        var medium = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Fan", siteId = o.Site, priority = "Medium", type = "Corrective", requiredBy = "2027-02-10" })));
        Near(now.AddDays(3), At(await RequestAsync(o.C, medium), "dueAt"));
    }

    [SkippableFact]
    public async Task PM_work_keeps_its_schedule_date()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var asset = Id(await Ok(await o.C.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var task = Id(await Ok(await o.C.PostAsJsonAsync("/api/pm-tasks", new { code = "W1", name = "Weekly check", steps = new[] { "Look" } })));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Ok(await o.C.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Pump PM", siteId = o.Site, assetId = asset, priority = "Urgent", pmTaskId = task, type = "FixedInterval", basis = "Scheduled", frequency = 1, unit = "Week",
            workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = today.ToString("yyyy-MM-dd"), timeBased = false, extraTasks = Array.Empty<object>(),
        }));
        var gen = await Ok(await o.C.PostAsJsonAsync("/api/pm/generate", new { until = today.ToString("yyyy-MM-dd") }));
        var w = await DetailAsync(o.C, gen.GetProperty("results")[0].GetProperty("workOrderId").GetGuid());
        var due = DateOnly.Parse(w.GetProperty("dueDate").GetString()!);
        Assert.Equal(Core.Work.Sla.EndOfDay(due, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")), At(w, "dueAt"));
        Assert.Null(At(w, "startBy"));
        Assert.False(w.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task The_first_action_is_the_response()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var now = DateTimeOffset.UtcNow;

        var held = await CreateAsync(o.C, Wo(o.Site));
        Assert.Null(At(await DetailAsync(o.C, held), "respondedAt"));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{held}/status", new { status = "OnHold", holdReason = "WaitingForParts" }));
        Near(now, At(await DetailAsync(o.C, held), "respondedAt"));

        var ticked = await CreateAsync(o.C, Wo(o.Site, tasks: ["Isolate"]));
        var step = Id((await DetailAsync(o.C, ticked)).GetProperty("tasks")[0]);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{ticked}/tasks/{step}", new { isDone = true }));
        var first = At(await DetailAsync(o.C, ticked), "respondedAt");
        Near(now, first);

        // Offline on the phone two hours ago, sent now: the response is when the technician did it.
        var phone = await CreateAsync(o.C, Wo(o.Site));
        await Ok(await o.C.PostAsJsonAsync("/api/tech/actions", new { clientId = Guid.NewGuid(), at = now.AddHours(-2), type = "status.change", workOrderId = phone, payload = new { status = "InProgress" } }));
        Near(now.AddHours(-2), At(await DetailAsync(o.C, phone), "respondedAt"));

        // A later action never moves it.
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{ticked}/status", new { status = "InProgress" }));
        Assert.Equal(first, At(await DetailAsync(o.C, ticked), "respondedAt"));
    }

    [SkippableFact]
    public async Task Without_targets_nothing_changes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync(targets: false);
        var none = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site)));
        Assert.Null(At(none, "dueAt"));
        Assert.Null(At(none, "startBy"));
        var dated = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site, dueDate: "2026-12-24")));
        Assert.Equal(DateTimeOffset.Parse("2026-12-24T21:59:59Z"), At(dated, "dueAt"));
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Fan", siteId = o.Site, priority = "Urgent", type = "Corrective", requiredBy = "2026-12-20" })));
        var approved = await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { dueDate = "2026-12-22" }));
        var w = await DetailAsync(o.C, approved.GetProperty("workOrderId").GetGuid());
        Assert.Equal("2026-12-22", w.GetProperty("dueDate").GetString());
        Assert.Null(At(w, "startBy"));
    }

    [SkippableFact]
    public async Task Existing_due_dates_are_back_filled_in_the_organizations_time_zone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync(targets: false);
        var wid = await CreateAsync(o.C, Wo(o.Site, dueDate: "2026-11-30"));
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Fan", siteId = o.Site, priority = "Low", type = "Corrective", requiredBy = "2026-12-05" })));
        // As if saved before 0.5.0: a due date without a due time.
        await WithDbAsync(async db =>
        {
            await db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == wid).ExecuteUpdateAsync(s => s.SetProperty(w => w.DueAt, (DateTimeOffset?)null));
            await db.WorkRequests.IgnoreQueryFilters().Where(r => r.Id == rid).ExecuteUpdateAsync(s => s.SetProperty(r => r.DueAt, (DateTimeOffset?)null));
            await DueBackfill.RunAsync(db);
            await DueBackfill.RunAsync(db); // nothing left to do the second time
        });
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T21:59:59Z"), At(await DetailAsync(o.C, wid), "dueAt"));
        Assert.Equal(DateTimeOffset.Parse("2026-12-05T21:59:59Z"), At(await RequestAsync(o.C, rid), "dueAt"));
    }

    // ---- Task 3: late and due soon everywhere, and the "Targets met" report ----

    /// <summary>Moves a work order's creation and deadlines back (or on) in time, as if raised earlier.</summary>
    private async Task ShiftAsync(Guid workOrderId, TimeSpan by)
    {
        var h = by.TotalHours;
        await WithDbAsync(db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == workOrderId).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.CreatedAt, w => w.CreatedAt.AddHours(h))
            .SetProperty(w => w.DueAt, w => w.DueAt == null ? null : w.DueAt.Value.AddHours(h))
            .SetProperty(w => w.StartBy, w => w.StartBy == null ? null : w.StartBy.Value.AddHours(h))));
    }

    private static async Task<List<Guid>> LateIdsAsync(HttpClient c, string path) =>
        (await Ok(await c.GetAsync(path))).GetProperty("items").EnumerateArray().Select(Id).ToList();

    [SkippableFact]
    public async Task Late_means_past_the_due_time_not_past_the_due_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var late = await CreateAsync(o.C, Wo(o.Site));
        await ShiftAsync(late, TimeSpan.FromHours(-5)); // raised 5 h ago: due 1 h ago, the same day or not
        var later = await CreateAsync(o.C, Wo(o.Site, dueAt: DateTimeOffset.UtcNow.AddMinutes(30)));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{later}/status", new { status = "InProgress" })); // responded: only its deadline counts

        var ids = await LateIdsAsync(o.C, "/api/work-orders?late=true");
        Assert.Contains(late, ids);
        Assert.DoesNotContain(later, ids);
        Assert.Equal(ids, await LateIdsAsync(o.C, "/api/work-orders?overdue=true")); // old clients' filter means the same
        var row = (await Ok(await o.C.GetAsync("/api/work-orders"))).GetProperty("items").EnumerateArray().Single(r => Id(r) == late);
        Assert.Equal("Late", row.GetProperty("completionState").GetString());
        Assert.Equal("DueSoon", (await Ok(await o.C.GetAsync("/api/work-orders"))).GetProperty("items").EnumerateArray().Single(r => Id(r) == later).GetProperty("completionState").GetString());
        var work = (await Ok(await o.C.GetAsync("/api/dashboard"))).GetProperty("work");
        Assert.Equal(1, work.GetProperty("late").GetInt32());
        Assert.Equal(1, work.GetProperty("dueSoon").GetInt32());
        Assert.Equal(1, (await Ok(await o.C.GetAsync("/api/alerts"))).GetProperty("overdueWorkOrders").GetInt32());
    }

    [SkippableFact]
    public async Task Response_late_counts_until_someone_acts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var id = await CreateAsync(o.C, Wo(o.Site));
        await ShiftAsync(id, TimeSpan.FromHours(-2)); // start-by passed an hour ago, due in 2 h
        Assert.Contains(id, await LateIdsAsync(o.C, "/api/work-orders?late=true"));
        var d = await DetailAsync(o.C, id);
        Assert.Equal("Late", d.GetProperty("sla").GetProperty("response").GetString());
        Assert.Equal(1, (await Ok(await o.C.GetAsync("/api/dashboard"))).GetProperty("work").GetProperty("responseLate").GetInt32());

        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{id}/status", new { status = "OnHold", holdReason = "WaitingForParts" }));
        Assert.DoesNotContain(id, await LateIdsAsync(o.C, "/api/work-orders?late=true"));
        Assert.Equal("Missed", (await DetailAsync(o.C, id)).GetProperty("sla").GetProperty("response").GetString());
    }

    [SkippableFact]
    public async Task Requests_are_late_while_waiting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = o.Site, priority = "Urgent", type = "Corrective" })));
        await WithDbAsync(db => db.WorkRequests.IgnoreQueryFilters().Where(r => r.Id == rid)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, r => r.CreatedAt.AddHours(-2)).SetProperty(r => r.RespondBy, r => r.RespondBy!.Value.AddHours(-2))));
        Assert.Contains(rid, await LateIdsAsync(o.C, "/api/work-requests?late=true"));
        var row = (await Ok(await o.C.GetAsync("/api/work-requests"))).GetProperty("items").EnumerateArray().Single(r => Id(r) == rid);
        Assert.Equal("Late", row.GetProperty("responseState").GetString());

        await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { }));
        Assert.DoesNotContain(rid, await LateIdsAsync(o.C, "/api/work-requests?late=true"));
    }

    [SkippableFact]
    public async Task Targets_met_report_counts_each_thing_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        // Requests: one answered in time, one still waiting an hour after its response deadline.
        var inTime = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "A", siteId = o.Site, priority = "Urgent", type = "Corrective" })));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{inTime}/approve", new { }));
        var waiting = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "B", siteId = o.Site, priority = "Urgent", type = "Corrective" })));
        await WithDbAsync(db => db.WorkRequests.IgnoreQueryFilters().Where(r => r.Id == waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, r => r.CreatedAt.AddHours(-2)).SetProperty(r => r.RespondBy, r => r.RespondBy!.Value.AddHours(-2))));
        // Work orders: one completed in time (also responded in time), one an hour past its deadline.
        var done = await CreateAsync(o.C, Wo(o.Site));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{done}/status", new { status = "Completed", completionNotes = "Fixed" }));
        var overdue = await CreateAsync(o.C, Wo(o.Site));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{overdue}/status", new { status = "InProgress" }));
        await ShiftAsync(overdue, TimeSpan.FromHours(-5));
        // PM work an hour past its date: PM compliance has its own report, so it isn't counted here.
        var pm = await CreateAsync(o.C, Wo(o.Site));
        await WithDbAsync(db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == pm).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.PmDueDate, DateOnly.FromDateTime(DateTime.UtcNow)).SetProperty(w => w.PmScheduleId, Guid.NewGuid())
            .SetProperty(w => w.DueAt, DateTimeOffset.UtcNow.AddHours(-1)).SetProperty(w => w.StartBy, (DateTimeOffset?)null)));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await Ok(await o.C.GetAsync($"/api/reports/targets-met?from={today.AddDays(-2):yyyy-MM-dd}&to={today.AddDays(2):yyyy-MM-dd}&groupBy=priority"));
        var urgent = report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("priority").GetString() == "Urgent");
        // Response: request in time (met), request still waiting (missed), the completed job (met), the overdue job started 4 h after its
        // start-by (missed). The approved request's work order isn't counted again: its response was the request's decision.
        Assert.Equal((2, 4), (urgent.GetProperty("responseMet").GetInt32(), urgent.GetProperty("responseTotal").GetInt32()));
        // Completion: the completed job (met), the overdue one (missed); the PM job and pending deadlines aren't counted.
        Assert.Equal((1, 2), (urgent.GetProperty("completionMet").GetInt32(), urgent.GetProperty("completionTotal").GetInt32()));
    }

    [SkippableFact]
    public async Task Targets_met_report_leaves_out_finished_work_with_no_recorded_times()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        // Imported history: finished, but the old system kept no completion or decision time, so in time or late can't be told.
        var job = await CreateAsync(o.C, Wo(o.Site));
        await WithDbAsync(db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == job).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkOrderStatus.Closed).SetProperty(w => w.CompletedAt, (DateTimeOffset?)null)
            .SetProperty(w => w.RespondedAt, (DateTimeOffset?)null)
            .SetProperty(w => w.StartBy, DateTimeOffset.UtcNow.AddHours(-3)).SetProperty(w => w.DueAt, DateTimeOffset.UtcNow.AddHours(-1))));
        var req = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Old", siteId = o.Site, priority = "Urgent", type = "Corrective" })));
        await WithDbAsync(db => db.WorkRequests.IgnoreQueryFilters().Where(r => r.Id == req).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, WorkRequestStatus.Rejected).SetProperty(r => r.DecidedAt, (DateTimeOffset?)null)
            .SetProperty(r => r.RespondBy, DateTimeOffset.UtcNow.AddHours(-1))));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await Ok(await o.C.GetAsync($"/api/reports/targets-met?from={today.AddDays(-2):yyyy-MM-dd}&to={today.AddDays(2):yyyy-MM-dd}&groupBy=priority"));
        var urgent = report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("priority").GetString() == "Urgent");
        Assert.Equal(0, urgent.GetProperty("responseTotal").GetInt32());
        Assert.Equal(0, urgent.GetProperty("completionTotal").GetInt32());
    }

    // ---- Final review fixes ----

    private static async Task PutAsync(HttpClient c, Guid id, Org o, string priority, string? dueDate = null, DateTimeOffset? dueAt = null)
    {
        var d = await DetailAsync(c, id);
        await Ok(await c.PutAsJsonAsync($"/api/work-orders/{id}", new
        {
            title = "Pump leak", siteId = o.Site, type = "Corrective", priority, dueDate, dueAt, rowVersion = d.GetProperty("rowVersion").GetString(),
        }));
    }
    private static DateTimeOffset Created(JsonElement d) => DateTimeOffset.Parse(d.GetProperty("createdAt").GetString()!);

    [SkippableFact]
    public async Task Bulk_priority_change_follows_the_target_rules()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var id = await CreateAsync(o.C, Wo(o.Site, priority: "Medium"));
        await Ok(await o.C.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { id }, priority = "Urgent" }));
        var d = await DetailAsync(o.C, id);
        Near(Created(d).AddHours(4), At(d, "dueAt"));
        Near(Created(d).AddHours(1), At(d, "startBy"));
    }

    [SkippableFact]
    public async Task Changing_priority_and_due_together_still_sets_the_start_by()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var id = await CreateAsync(o.C, Wo(o.Site, priority: "Medium")); // Medium: no response target
        var typed = DateTimeOffset.Parse("2027-01-10T13:00:00Z");
        await PutAsync(o.C, id, o, "Urgent", dueAt: typed);
        var d = await DetailAsync(o.C, id);
        Assert.Equal(typed, At(d, "dueAt"));
        Assert.False(d.GetProperty("dueFromTarget").GetBoolean());
        Near(Created(d).AddHours(1), At(d, "startBy"));
    }

    [SkippableFact]
    public async Task Clearing_the_due_time_goes_back_to_the_target()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var id = await CreateAsync(o.C, Wo(o.Site, dueDate: "2027-03-01"));
        await PutAsync(o.C, id, o, "Urgent"); // the form sends no due date and no due time
        var d = await DetailAsync(o.C, id);
        Near(Created(d).AddHours(4), At(d, "dueAt"));
        Assert.True(d.GetProperty("dueFromTarget").GetBoolean());
        var low = await CreateAsync(o.C, Wo(o.Site, priority: "Low", dueDate: "2027-03-01"));
        await PutAsync(o.C, low, o, "Low");
        Assert.Null(At(await DetailAsync(o.C, low), "dueAt")); // no target to go back to
    }

    [SkippableFact]
    public async Task Approved_requests_show_their_work_orders_deadline()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Fan", siteId = o.Site, priority = "Medium", type = "Corrective" })));
        var wo = (await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { priority = "Urgent" }))).GetProperty("workOrderId").GetGuid();
        var row = (await Ok(await o.C.GetAsync("/api/work-requests"))).GetProperty("items").EnumerateArray().Single(r => Id(r) == rid);
        Assert.Equal(At(await DetailAsync(o.C, wo), "dueAt"), At(row, "dueAt"));
    }

    [SkippableFact]
    public async Task Approving_into_a_priority_without_a_target_uses_needed_by()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", new { title = "Fan", siteId = o.Site, priority = "Urgent", type = "Corrective", requiredBy = "2027-02-10" })));
        var wo = (await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { priority = "Low" }))).GetProperty("workOrderId").GetGuid();
        var d = await DetailAsync(o.C, wo);
        Assert.Equal(DateTimeOffset.Parse("2027-02-10T21:59:59Z"), At(d, "dueAt"));
        Assert.False(d.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task A_due_time_early_in_the_local_day_is_not_before_the_planned_start()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        // 01:00 on a day in Cairo is the evening before in UTC: not before a planned start of that day.
        var day = Loop.Today.AddDays(40);
        var r = await o.C.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Night job", siteId = o.Site, type = "Corrective", priority = "Medium", plannedStart = Loop.Iso(day), dueAt = LocalToUtc(day, 1, 0),
        });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Dashboard_cards_count_what_their_lists_show()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var both = await CreateAsync(o.C, Wo(o.Site));
        await ShiftAsync(both, TimeSpan.FromHours(-5)); // completion and response late
        var notStarted = await CreateAsync(o.C, Wo(o.Site));
        await ShiftAsync(notStarted, TimeSpan.FromHours(-2)); // only the response is late
        var soon = await CreateAsync(o.C, Wo(o.Site, dueAt: DateTimeOffset.UtcNow.AddMinutes(30)));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{soon}/status", new { status = "InProgress" }));

        var work = (await Ok(await o.C.GetAsync("/api/dashboard"))).GetProperty("work");
        Assert.Equal((await LateIdsAsync(o.C, "/api/work-orders?late=true")).Count, work.GetProperty("late").GetInt32());
        Assert.Equal((await LateIdsAsync(o.C, "/api/work-orders?responseLate=true")).Count, work.GetProperty("responseLate").GetInt32());
        Assert.Equal([soon], await LateIdsAsync(o.C, "/api/work-orders?dueSoon=true"));
        Assert.Equal(1, work.GetProperty("dueSoon").GetInt32());
        Assert.Equal(2, work.GetProperty("late").GetInt32());
        Assert.Equal(2, work.GetProperty("responseLate").GetInt32());
    }

    [SkippableFact]
    public async Task Requests_raised_offline_count_from_when_they_were_raised()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var raised = DateTimeOffset.UtcNow.AddHours(-3);
        await Ok(await o.C.PostAsJsonAsync("/api/tech/actions", new
        {
            clientId = Guid.NewGuid(), at = raised, type = "request.create", payload = new { title = "Leak by the pump", priority = "Urgent", siteId = o.Site },
        }));
        var row = (await Ok(await o.C.GetAsync("/api/work-requests"))).GetProperty("items").EnumerateArray().Single(r => r.GetProperty("title").GetString() == "Leak by the pump");
        Near(raised.AddHours(1), At(row, "respondBy"));
        Assert.Equal("Late", row.GetProperty("responseState").GetString());
    }

    [SkippableFact]
    public async Task Back_fill_uses_each_days_own_offset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync(targets: false);
        var summer = await CreateAsync(o.C, Wo(o.Site, dueDate: "2026-07-15")); // Cairo summer time, UTC+3
        var winter = await CreateAsync(o.C, Wo(o.Site, dueDate: "2026-12-15")); // UTC+2
        await WithDbAsync(async db =>
        {
            await db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == summer || w.Id == winter).ExecuteUpdateAsync(s => s.SetProperty(w => w.DueAt, (DateTimeOffset?)null));
            await DueBackfill.RunAsync(db);
        });
        Assert.Equal(DateTimeOffset.Parse("2026-07-15T20:59:59Z"), At(await DetailAsync(o.C, summer), "dueAt"));
        Assert.Equal(DateTimeOffset.Parse("2026-12-15T21:59:59Z"), At(await DetailAsync(o.C, winter), "dueAt"));
    }
}

