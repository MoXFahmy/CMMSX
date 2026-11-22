using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class BulkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(HttpClient admin, Guid site)> OrgAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Bulk Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        return (c, site);
    }

    private async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var email = Addr("user");
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site, string title = "Job", Guid[]? assignees = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, type = "Corrective", priority = "Medium", siteId = site, assigneeIds = assignees ?? [] })));

    private static async Task<Guid> EmployeeAsync(HttpClient c, string code, string? email = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code, fullName = $"Tech {code}", email, isContractor = false })));

    private static Task<HttpResponseMessage> SetStatus(HttpClient c, Guid id, object body) => c.PostAsJsonAsync($"/api/work-orders/{id}/status", body);

    private static async Task<JsonElement> Get(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-orders/{id}"));

    private static string Status(JsonElement wo) => wo.GetProperty("status").GetString()!;

    [SkippableFact]
    public async Task Bulk_status_applies_the_completion_rules_per_row()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await TestSettings.SetSettingsAsync(admin, s => s["requireLaborToComplete"] = true);
        var emp = await EmployeeAsync(admin, "T1");
        var withLabour = await WorkOrderAsync(admin, site, "With labour");
        var without = await WorkOrderAsync(admin, site, "Without labour");
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{withLabour}/labor", new { employeeId = emp, workDate = Iso(Today), hours = 2, overtimeHours = 0 }));
        var r = await Ok(await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { withLabour, without }, status = new { status = "Completed", completionNotes = "Replaced the seal" } }));
        Assert.Equal(1, r.GetProperty("updated").GetInt32());
        var skipped = Assert.Single(r.GetProperty("skipped").EnumerateArray());
        Assert.Equal(without, Id(skipped));
        Assert.False(string.IsNullOrEmpty(skipped.GetProperty("number").GetString()));
        Assert.Contains("labour", skipped.GetProperty("reason").GetString());
        var done = await Get(admin, withLabour);
        Assert.Equal("Completed", Status(done));
        Assert.Equal("Replaced the seal", done.GetProperty("completionNotes").GetString());
        Assert.Equal("Open", Status(await Get(admin, without)));
    }

    [SkippableFact]
    public async Task Bulk_status_skips_moves_the_workflow_does_not_allow()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var open = await WorkOrderAsync(admin, site);
        var done = await WorkOrderAsync(admin, site);
        await Ok(await SetStatus(admin, done, new { status = "Completed", completionNotes = "Done" }));
        var r = await Ok(await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { open, done }, status = new { status = "Closed" } }));
        Assert.Equal(1, r.GetProperty("updated").GetInt32());
        Assert.Equal(open, Id(Assert.Single(r.GetProperty("skipped").EnumerateArray())));
        Assert.Equal("Closed", Status(await Get(admin, done)));
    }

    [SkippableFact]
    public async Task Bulk_hold_needs_a_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var wo = await WorkOrderAsync(admin, site);
        var r = await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, status = new { status = "OnHold" } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Open", Status(await Get(admin, wo)));
        var ok = await Ok(await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, status = new { status = "OnHold", holdReason = "WaitingForParts" } }));
        Assert.Equal(1, ok.GetProperty("updated").GetInt32());
    }

    [SkippableFact]
    public async Task Bulk_close_needs_the_close_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var tech = await UserAsync(admin, "Technician");
        var wo = await WorkOrderAsync(admin, site);
        await Ok(await SetStatus(admin, wo, new { status = "Completed", completionNotes = "Done" }));
        var r = await Ok(await tech.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, status = new { status = "Closed" } }));
        Assert.Equal(0, r.GetProperty("updated").GetInt32());
        Assert.Contains("close", Assert.Single(r.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal("Completed", Status(await Get(admin, wo)));
    }

    [SkippableFact]
    public async Task Bulk_priority_skips_closed_work_and_needs_edit()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var open = await WorkOrderAsync(admin, site);
        var closed = await WorkOrderAsync(admin, site);
        await Ok(await SetStatus(admin, closed, new { status = "Cancelled", cancelReasonId = await Loop.AnyCancelReasonAsync(admin) }));
        var r = await Ok(await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { open, closed }, priority = "Urgent" }));
        Assert.Equal(1, r.GetProperty("updated").GetInt32());
        Assert.Contains("Reopen", Assert.Single(r.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal("Urgent", (await Get(admin, open)).GetProperty("priority").GetString());
        Assert.Equal("Medium", (await Get(admin, closed)).GetProperty("priority").GetString());
        var tech = await UserAsync(admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { open }, priority = "Low" })).StatusCode);
    }

    [SkippableFact]
    public async Task Bulk_add_technicians_adds_and_emails()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await TestSettings.SetSettingsAsync(admin, s => s["emailEnabled"] = true);
        var first = await EmployeeAsync(admin, "A1", Addr("first"));
        var newEmail = Addr("second");
        var second = await EmployeeAsync(admin, "A2", newEmail);
        var wo = await WorkOrderAsync(admin, site, "Pump", [first]);
        var r = await Ok(await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, addAssigneeIds = new[] { second, first } }));
        Assert.Equal(1, r.GetProperty("updated").GetInt32());
        var assignees = (await Get(admin, wo)).GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("employeeId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { first, second }, assignees);
        var log = await Ok(await admin.GetAsync("/api/email/log"));
        Assert.Contains(log.EnumerateArray(), e => e.GetProperty("toAddress").GetString() == newEmail);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, addAssigneeIds = new[] { Guid.NewGuid() } })).StatusCode);
    }

    [SkippableFact]
    public async Task Bulk_input_is_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var wo = await WorkOrderAsync(admin, site);
        async Task Bad(object body) => Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/work-orders/bulk", body)).StatusCode);
        await Bad(new { ids = Array.Empty<Guid>(), priority = "Low" });
        await Bad(new { ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(), priority = "Low" });
        await Bad(new { ids = new[] { wo } });
        await Bad(new { ids = new[] { wo }, priority = "Low", status = new { status = "InProgress" } });
    }

    [SkippableFact]
    public async Task Bulk_priority_refuses_values_the_edit_form_refuses()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var wo = await WorkOrderAsync(admin, site);
        var r = await admin.PostAsJsonAsync("/api/work-orders/bulk", new { ids = new[] { wo }, priority = 42 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Medium", (await Get(admin, wo)).GetProperty("priority").GetString());
    }

    private static async Task<Guid> PmScheduleAsync(HttpClient c, Guid site, DateOnly start)
    {
        var asset = Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Boiler", siteId = site, status = "Active", criticality = "High", warrantyType = "None" })));
        var task = Id(await Ok(await c.PostAsJsonAsync("/api/pm-tasks", new { code = $"T{Guid.NewGuid():N}"[..8], name = "Boiler check", steps = new[] { "Look" } })));
        return Id(await Ok(await c.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Boiler PM", siteId = site, assetId = asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled", frequency = 1, unit = "Month",
            workPeriodDays = 1, wholeMonth = false, leadDays = 0, startDate = Iso(start), timeBased = false, extraTasks = Array.Empty<object>(), assigneeIds = Array.Empty<Guid>(),
        })));
    }

    [SkippableFact]
    public async Task Bulk_pause_and_resume_PM_schedules()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var pm = await PmScheduleAsync(admin, site, Today.AddDays(-75));
        async Task<JsonElement> Schedule() => await Ok(await admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.True(DateOnly.Parse((await Schedule()).GetProperty("nextDueDate").GetString()!) < Today);

        var paused = await Ok(await admin.PostAsJsonAsync("/api/pm-schedules/bulk", new { ids = new[] { pm }, active = false }));
        Assert.Equal(1, paused.GetProperty("updated").GetInt32());
        Assert.False((await Schedule()).GetProperty("isActive").GetBoolean());
        var due = await Ok(await admin.GetAsync("/api/pm-schedules?dueWithinDays=60"));
        Assert.DoesNotContain(due.GetProperty("items").EnumerateArray(), x => Id(x) == pm);

        await Ok(await admin.PostAsJsonAsync("/api/pm-schedules/bulk", new { ids = new[] { pm }, active = true }));
        var resumed = await Schedule();
        Assert.True(resumed.GetProperty("isActive").GetBoolean());
        Assert.True(DateOnly.Parse(resumed.GetProperty("nextDueDate").GetString()!) >= Today, "resuming starts from today, like the edit form");
    }

    [SkippableFact]
    public async Task Bulk_parts_active_flag()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await OrgAsync();
        var part = Id(await Ok(await admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P1", name = "Filter", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = Array.Empty<object>(),
        })));
        var r = await Ok(await admin.PostAsJsonAsync("/api/parts/bulk", new { ids = new[] { part }, active = false }));
        Assert.Equal(1, r.GetProperty("updated").GetInt32());
        Assert.False((await Ok(await admin.GetAsync($"/api/parts/{part}"))).GetProperty("isActive").GetBoolean());
        var active = await Ok(await admin.GetAsync("/api/parts?activeOnly=true"));
        Assert.DoesNotContain(active.GetProperty("items").EnumerateArray(), x => Id(x) == part);
        await Ok(await admin.PostAsJsonAsync("/api/parts/bulk", new { ids = new[] { part }, active = true }));
        Assert.True((await Ok(await admin.GetAsync($"/api/parts/{part}"))).GetProperty("isActive").GetBoolean());
    }

    [SkippableFact]
    public async Task Pm_and_part_bulk_need_manage_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await OrgAsync();
        var tech = await UserAsync(admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/pm-schedules/bulk", new { ids = new[] { Guid.NewGuid() }, active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/parts/bulk", new { ids = new[] { Guid.NewGuid() }, active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/parts/bulk", new { ids = Array.Empty<Guid>(), active = false })).StatusCode);
    }
}
