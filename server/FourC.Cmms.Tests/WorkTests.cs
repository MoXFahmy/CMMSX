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
}
