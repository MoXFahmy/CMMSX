using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

/// <summary>The planning board (0.5.0): where jobs sit, and moving them by day and person.</summary>
public class PlanningBoardTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private sealed record Org(HttpClient C, Guid Site, Guid A, Guid B);

    private async Task<Org> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Board Co", fullName = "Pat Planner", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Plant", isActive = true })));
        var a = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "A", fullName = "Ann Fitter", isContractor = false, siteIds = new[] { site } })));
        var b = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "B", fullName = "Bob Electrician", isContractor = false, siteIds = new[] { site } })));
        return new Org(c, site, a, b);
    }

    private static async Task<Guid> JobAsync(Org o, string title, Guid[]? who = null, DateOnly? planned = null, DateOnly? due = null, decimal? estimate = null) =>
        Id(await Ok(await o.C.PostAsJsonAsync("/api/work-orders", new
        {
            title, siteId = o.Site, type = "Corrective", priority = "Medium", assigneeIds = who ?? [], plannedStart = planned is { } p ? D(p) : null,
            dueDate = due is { } d ? D(d) : null, estimatedHours = estimate,
        })));

    private static async Task<JsonElement> BoardAsync(HttpClient c, DateOnly from, DateOnly to) => await Ok(await c.GetAsync($"/api/planning/board?from={D(from)}&to={D(to)}"));
    private static JsonElement Job(JsonElement board, string list, Guid id) => board.GetProperty(list).EnumerateArray().Single(j => Id(j) == id);
    private static bool Has(JsonElement board, string list, Guid id) => board.GetProperty(list).EnumerateArray().Any(j => Id(j) == id);

    [SkippableFact]
    public async Task Jobs_sit_on_their_planned_start_else_their_due_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var planned = await JobAsync(o, "Planned", [o.A], planned: Today.AddDays(2), due: Today.AddDays(5));
        var dueOnly = await JobAsync(o, "Due only", [o.B], due: Today.AddDays(3));
        var b = await BoardAsync(o.C, Today, Today.AddDays(6));
        Assert.Equal(D(Today.AddDays(2)), Job(b, "jobs", planned).GetProperty("day").GetString());
        Assert.Equal(D(Today.AddDays(3)), Job(b, "jobs", dueOnly).GetProperty("day").GetString());
        Assert.Equal(new[] { o.A, o.B }.OrderBy(x => x), b.GetProperty("people").EnumerateArray().Select(Id).OrderBy(x => x));
        Assert.Equal(8m, b.GetProperty("capacityHours").GetDecimal());
    }

    [SkippableFact]
    public async Task Earlier_and_unscheduled_trays()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var earlier = await JobAsync(o, "Left over", [o.A], planned: Today.AddDays(-5));
        var unscheduled = await JobAsync(o, "No dates", [o.A]);
        var done = await JobAsync(o, "Done", [o.A], planned: Today.AddDays(-5));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{done}/status", new { status = "Completed", completionNotes = "Done" }));
        var b = await BoardAsync(o.C, Today, Today.AddDays(6));
        Assert.True(Has(b, "earlier", earlier));
        Assert.True(Has(b, "unscheduled", unscheduled));
        Assert.False(Has(b, "jobs", earlier) || Has(b, "earlier", done) || Has(b, "jobs", done));
    }

    [SkippableFact]
    public async Task Hours_are_planned_hours_else_the_estimate_split()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var split = await JobAsync(o, "Split", [o.A, o.B], planned: Today, estimate: 6);
        var mixed = await JobAsync(o, "Mixed", [o.A, o.B], planned: Today, estimate: 6);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{mixed}/planned-hours", new { lines = new[] { new { employeeId = o.A, plannedHours = 4m } } }));
        var b = await BoardAsync(o.C, Today, Today.AddDays(6));
        decimal? Hours(Guid job, Guid who) => Job(b, "jobs", job).GetProperty("assignees").EnumerateArray().Single(a => a.GetProperty("employeeId").GetGuid() == who)
            .GetProperty("hours") is { ValueKind: JsonValueKind.Number } h ? h.GetDecimal() : null;
        Assert.Equal(3m, Hours(split, o.A));
        Assert.Equal(3m, Hours(split, o.B));
        Assert.Equal(4m, Hours(mixed, o.A));
        Assert.Equal(3m, Hours(mixed, o.B)); // no planned hours of his own: his share of the estimate
    }

    [SkippableFact]
    public async Task Upcoming_PMs_are_on_the_board()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var asset = Id(await Ok(await o.C.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var task = Id(await Ok(await o.C.PostAsJsonAsync("/api/pm-tasks", new { code = "W1", name = "Weekly check", steps = new[] { "Look" } })));
        var schedule = Id(await Ok(await o.C.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Pump PM", siteId = o.Site, assetId = asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled", frequency = 1, unit = "Week",
            workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = D(Today.AddDays(2)), timeBased = false, extraTasks = Array.Empty<object>(),
        })));
        var b = await BoardAsync(o.C, Today, Today.AddDays(6));
        var pm = Assert.Single(b.GetProperty("pms").EnumerateArray());
        Assert.Equal((schedule, D(Today.AddDays(2)), asset), (pm.GetProperty("id").GetGuid(), pm.GetProperty("date").GetString(), pm.GetProperty("assetId").GetGuid()));
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient c, object body) => c.PostAsJsonAsync("/api/planning/move", body);
    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/work-orders/{id}"));
    private static List<Guid> Assignees(JsonElement d) => d.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("employeeId").GetGuid()).OrderBy(x => x).ToList();

    [SkippableFact]
    public async Task Moving_changes_the_day_and_the_person()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var job = await JobAsync(o, "Move me", [o.A], planned: Today, due: Today.AddDays(2), estimate: 2);
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{job}/planned-hours", new { lines = new[] { new { employeeId = o.A, plannedHours = 5m } } }));

        var r = await Ok(await MoveAsync(o.C, new { workOrderId = job, day = D(Today.AddDays(1)) }));
        Assert.Equal(D(Today.AddDays(1)), (await DetailAsync(o.C, job)).GetProperty("plannedStart").GetString());
        Assert.True(r.ValueKind == JsonValueKind.Undefined || !r.TryGetProperty("warning", out var w0) || w0.ValueKind == JsonValueKind.Null);

        await Ok(await MoveAsync(o.C, new { workOrderId = job, fromEmployeeId = o.A, toEmployeeId = o.B }));
        var d = await DetailAsync(o.C, job);
        Assert.Equal([o.B], Assignees(d));
        Assert.Equal(5m, d.GetProperty("assignees")[0].GetProperty("plannedHours").GetDecimal()); // the planned hours move with the job

        await Ok(await MoveAsync(o.C, new { workOrderId = job, toEmployeeId = o.A }));
        Assert.Equal(new[] { o.A, o.B }.OrderBy(x => x).ToList(), Assignees(await DetailAsync(o.C, job)));
        await Ok(await MoveAsync(o.C, new { workOrderId = job, fromEmployeeId = o.B }));
        Assert.Equal([o.A], Assignees(await DetailAsync(o.C, job)));

        var late = await Ok(await MoveAsync(o.C, new { workOrderId = job, day = D(Today.AddDays(4)) })); // after its due day: allowed, with a warning
        Assert.False(string.IsNullOrEmpty(late.GetProperty("warning").GetString()));
        Assert.Equal(D(Today.AddDays(4)), (await DetailAsync(o.C, job)).GetProperty("plannedStart").GetString());
    }

    [SkippableFact]
    public async Task Moves_that_must_be_refused_change_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var job = await JobAsync(o, "Keep", [o.A], planned: Today);

        // A technician can see the board but not change it.
        var roles = await Ok(await o.C.GetAsync("/api/roles"));
        var tech = roles.EnumerateArray().First(x => x.GetProperty("name").GetString() == "Technician").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await o.C.PostAsJsonAsync("/api/users", new { fullName = "Tia Tech", email, isActive = true, roleIds = new[] { tech }, password = "Passw0rd!u" }));
        var t = fx.Client();
        await Ok(await t.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        await Ok(await t.GetAsync($"/api/planning/board?from={D(Today)}&to={D(Today.AddDays(6))}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(t, new { workOrderId = job, day = D(Today.AddDays(1)) })).StatusCode);

        // Another organization's work order is not there.
        var other = await SetupAsync();
        var theirs = await JobAsync(other, "Theirs", [other.A], planned: Today);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(o.C, new { workOrderId = theirs, day = D(Today.AddDays(1)) })).StatusCode);

        // Completed work is not planned any more.
        var done = await JobAsync(o, "Done", [o.A], planned: Today);
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{done}/status", new { status = "Completed", completionNotes = "Done" }));
        var refused = await MoveAsync(o.C, new { workOrderId = done, day = D(Today.AddDays(1)) });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await refused.Content.ReadAsStringAsync()));

        Assert.Equal(D(Today), (await DetailAsync(o.C, job)).GetProperty("plannedStart").GetString());
        Assert.Equal(D(Today), (await DetailAsync(o.C, done)).GetProperty("plannedStart").GetString());
        Assert.Equal(D(Today), (await DetailAsync(other.C, theirs)).GetProperty("plannedStart").GetString());
    }
}
