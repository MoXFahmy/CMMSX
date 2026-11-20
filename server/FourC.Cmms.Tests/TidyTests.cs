using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

/// <summary>0.5.1 tidy-up: small fixes from earlier reviews.</summary>
public class TidyTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task<(HttpClient C, Guid Site)> OrgAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Tidy Co", fullName = "Ann Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S", name = "Plant", isActive = true })));
        return (c, site);
    }
    private static async Task<Guid> JobAsync(HttpClient c, Guid site) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = site, type = "Corrective", priority = "Medium" })));
    private static Task<HttpResponseMessage> PhoneAsync(HttpClient c, Guid wo, object payload) =>
        c.PostAsJsonAsync("/api/tech/actions", new { clientId = Guid.NewGuid(), at = DateTimeOffset.UtcNow, type = "status.change", workOrderId = wo, payload });

    [SkippableFact]
    public async Task A_hold_note_sent_to_a_job_already_on_hold_is_kept()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site) = await OrgAsync();
        var wo = await JobAsync(c, site);
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "OnHold", holdReason = "WaitingForParts" }));
        var a = await Ok(await PhoneAsync(c, wo, new { status = "OnHold", holdReason = "WaitingForAccess", note = "Waiting for the crane" }));
        Assert.Equal("done", a.GetProperty("status").GetString());
        var activity = await Ok(await c.GetAsync($"/api/work-orders/{wo}/activity"));
        Assert.Contains(activity.EnumerateArray(), x => x.TryGetProperty("body", out var b) && b.GetString()?.Contains("Waiting for the crane") == true);
    }

    [SkippableFact]
    public async Task Completing_an_already_completed_job_without_notes_says_only_that()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site) = await OrgAsync();
        var wo = await JobAsync(c, site);
        await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Fixed" }));
        var a = await Ok(await PhoneAsync(c, wo, new { status = "Completed" }));
        Assert.Equal("done", a.GetProperty("status").GetString());
        Assert.DoesNotContain("notes", a.GetProperty("message").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task The_bell_does_not_count_inactive_parts_below_reorder()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site) = await OrgAsync();
        var store = Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = site })));
        var level = new[] { new { storeId = store, minLevel = 0, reorderLevel = 5, maxLevel = 0, reorderQuantity = 10 } };
        var active = Id(await Ok(await c.PostAsJsonAsync("/api/parts", new { partNumber = "P-1", name = "Bearing", unitOfMeasure = "EA", standardPrice = 5, levels = level })));
        var retired = Id(await Ok(await c.PostAsJsonAsync("/api/parts", new { partNumber = "P-2", name = "Old seal", unitOfMeasure = "EA", standardPrice = 5, levels = level })));
        await Ok(await c.PostAsJsonAsync("/api/parts/bulk", new { ids = new[] { retired }, active = false }));
        Assert.Equal(1, (await Ok(await c.GetAsync("/api/alerts"))).GetProperty("partsBelowReorder").GetInt32());
        _ = active;
    }

    [SkippableFact]
    public async Task A_far_future_created_date_filter_is_not_a_server_error()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, _) = await OrgAsync();
        await Ok(await c.GetAsync("/api/work-orders?createdTo=9999-12-31"));
        await Ok(await c.GetAsync("/api/work-orders?createdFrom=9999-12-31"));
    }

    [SkippableFact]
    public async Task Asset_bulk_changes_take_at_most_500_like_the_other_lists()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, _) = await OrgAsync();
        var r = await c.PostAsJsonAsync("/api/assets/bulk", new { ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(), status = "Inactive" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("500", await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Putting_a_person_back_on_a_job_can_restore_their_planned_hours()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (c, site) = await OrgAsync();
        var lee = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "L", fullName = "Lee", isContractor = false, siteIds = new[] { site } })));
        var wo = await JobAsync(c, site);
        await Ok(await c.PostAsJsonAsync("/api/planning/move", new { workOrderId = wo, toEmployeeId = lee, plannedHours = 3.5m }));
        var d = await Ok(await c.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal(3.5m, d.GetProperty("assignees")[0].GetProperty("plannedHours").GetDecimal());
        // The board tells the page each person's own planned hours, so Undo can put them back.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var board = await Ok(await c.GetAsync($"/api/planning/board?from={today:yyyy-MM-dd}&to={today.AddDays(6):yyyy-MM-dd}&siteId={site}"));
        var job = board.GetProperty("unscheduled").EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == wo);
        Assert.Equal(3.5m, job.GetProperty("assignees")[0].GetProperty("plannedHours").GetDecimal());
    }
}
