using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Services;
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
        var d = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site, dueDate: "2026-12-01")));
        Assert.Equal(DateTimeOffset.Parse("2026-12-01T21:59:59Z"), At(d, "dueAt"));
        Assert.False(d.GetProperty("dueFromTarget").GetBoolean());
    }

    [SkippableFact]
    public async Task A_deadline_near_midnight_lands_on_the_local_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var d = await DetailAsync(o.C, await CreateAsync(o.C, Wo(o.Site, dueAt: DateTimeOffset.Parse("2026-12-01T21:30:00Z"))));
        Assert.Equal("2026-12-01", d.GetProperty("dueDate").GetString()); // 23:30 in Cairo, not the next day
        Assert.Equal(DateTimeOffset.Parse("2026-12-01T21:30:00Z"), At(d, "dueAt"));
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
}
