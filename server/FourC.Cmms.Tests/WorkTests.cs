using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class WorkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<(HttpClient admin, Guid site)> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Work Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = (await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true }))).GetProperty("id").GetGuid();
        return (c, site);
    }

    private async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    [SkippableFact]
    public async Task Request_approval_creates_work_order_and_costs_roll_up()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await SetupAsync();
        var emp = (await Ok(await admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tech", hourlyRate = 20, overtimeRate = 30, isContractor = false }))).GetProperty("id").GetGuid();

        var requester = await UserAsync(admin, "Requester");
        var req = await Ok(await requester.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = site, priority = "High", type = "Corrective" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.PostAsJsonAsync($"/api/work-requests/{req.GetProperty("id").GetGuid()}/approve", new { })).StatusCode);
        Assert.Equal(1, (await Ok(await requester.GetAsync("/api/work-requests"))).GetProperty("total").GetInt32());

        var approved = await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{req.GetProperty("id").GetGuid()}/approve", new { assigneeIds = new[] { emp } }));
        var wo = approved.GetProperty("workOrderId").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "InProgress" }));
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp, workDate = DateOnly.FromDateTime(DateTime.UtcNow), hours = 2.5, overtimeHours = 1 }));
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/costs", new { description = "Seal kit", quantity = 2, unitCost = 40 }));

        var detail = await Ok(await admin.GetAsync($"/api/work-orders/{wo}"));
        var totals = detail.GetProperty("totals");
        Assert.Equal(80m, totals.GetProperty("laborCost").GetDecimal()); // 2.5 × 20 + 1 × 30
        Assert.Equal(160m, totals.GetProperty("totalCost").GetDecimal());
        Assert.Equal("Approved", (await Ok(await admin.GetAsync($"/api/work-requests/{req.GetProperty("id").GetGuid()}"))).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Workflow_enforces_rules_and_permissions()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await SetupAsync();
        var wo = (await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix door", type = "Corrective", priority = "Low", siteId = site, tasks = new[] { "Inspect hinge" } }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed" })).StatusCode); // needs notes
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "OnHold" })).StatusCode);    // needs reason
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Closed" })).StatusCode);    // Open → Closed not allowed

        var tech = await UserAsync(admin, "Technician");
        await Ok(await tech.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "InProgress" }));
        await Ok(await tech.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Hinge replaced" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Closed" })).StatusCode);
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Closed" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/work-orders/{wo}/tasks", new { description = "Late task" })).StatusCode); // closed

        var activity = await Ok(await admin.GetAsync($"/api/work-orders/{wo}/activity"));
        Assert.Equal(4, activity.EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "status")); // created, started, completed, closed
    }

    [SkippableFact]
    public async Task Completion_rules_block_completion_until_met()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await SetupAsync();
        await TestSettings.SetSettingsAsync(admin, s =>
        {
            s["requireLaborToComplete"] = true; s["requireAllTasksDone"] = true; s["requireFailureCodeForBreakdowns"] = true;
        });
        var emp = (await Ok(await admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tech", hourlyRate = 20, isContractor = false }))).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wo = (await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = "Motor tripped", type = "Breakdown", priority = "High", siteId = site, tasks = new[] { "Isolate", "Repair" } }))).GetProperty("id").GetGuid();

        var refused = await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Reset the overload" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var errors = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("labor", out _));
        Assert.True(errors.TryGetProperty("tasks", out _));
        Assert.True(errors.TryGetProperty("failureCodeId", out _));

        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp, workDate = today, hours = 1, overtimeHours = 0 }));
        foreach (var task in (await Ok(await admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks").EnumerateArray())
            await Ok(await admin.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{task.GetProperty("id").GetGuid()}", new { isDone = true }));
        var fc = (await Ok(await admin.PostAsJsonAsync("/api/failure-codes", new { code = "OVL", name = "Overload", isActive = true }))).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Reset the overload", failureCodeId = fc }));

        // No checklist and not a breakdown: labour is the only rule that applies.
        var simple = (await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = "Adjust door", type = "Corrective", priority = "Low", siteId = site }))).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{simple}/labor", new { employeeId = emp, workDate = today, hours = 0.5, overtimeHours = 0 }));
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{simple}/status", new { status = "Completed", completionNotes = "Adjusted" }));
    }

    [SkippableFact]
    public async Task Auto_start_moves_open_work_to_in_progress_on_labour_or_step()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await SetupAsync();
        var emp = (await Ok(await admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tech", hourlyRate = 20, isContractor = false }))).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task<Guid> NewWo(params string[] tasks) =>
            (await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", type = "Corrective", priority = "Medium", siteId = site, tasks }))).GetProperty("id").GetGuid();
        async Task<string> Status(Guid wo) => (await Ok(await admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("status").GetString()!;
        Task Labour(Guid wo) => Ok(admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp, workDate = today, hours = 1, overtimeHours = 0 }).Result);

        var off = await NewWo();
        await Labour(off);
        Assert.Equal("Open", await Status(off)); // setting off: nothing changes

        await TestSettings.SetSettingsAsync(admin, s => s["autoStartWork"] = true);
        var a = await NewWo();
        await Labour(a);
        var detail = await Ok(await admin.GetAsync($"/api/work-orders/{a}"));
        Assert.Equal("InProgress", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, detail.GetProperty("startedAt").ValueKind);
        var activity = await Ok(await admin.GetAsync($"/api/work-orders/{a}/activity"));
        Assert.Contains(activity.EnumerateArray(), x => x.GetProperty("kind").GetString() == "status" && x.GetProperty("body").GetString() == "Started automatically: labour logged");

        var b = await NewWo("Check belts");
        var step = (await Ok(await admin.GetAsync($"/api/work-orders/{b}"))).GetProperty("tasks")[0].GetProperty("id").GetGuid();
        await Ok(await admin.PutAsJsonAsync($"/api/work-orders/{b}/tasks/{step}", new { reading = "12" }));
        Assert.Equal("Open", await Status(b)); // a reading alone doesn't start the work
        await Ok(await admin.PutAsJsonAsync($"/api/work-orders/{b}/tasks/{step}", new { isDone = true }));
        Assert.Equal("InProgress", await Status(b));
        var bActivity = await Ok(await admin.GetAsync($"/api/work-orders/{b}/activity"));
        Assert.Contains(bActivity.EnumerateArray(), x => x.GetProperty("body").GetString() == "Started automatically: checklist step done");

        var c = await NewWo();
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{c}/status", new { status = "OnHold", holdReason = "WaitingForParts" }));
        await Labour(c);
        Assert.Equal("OnHold", await Status(c)); // on hold is left alone
    }
}
