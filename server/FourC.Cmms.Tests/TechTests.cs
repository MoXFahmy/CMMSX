using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class TechTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>An organization whose admin is also an employee (the technician) working at site A.</summary>
    private sealed record Org(HttpClient C, Guid Me, Guid SiteA, Guid SiteB, Guid Other);

    private async Task<Org> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Field Co", fullName = "Tess Tech", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var userId = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var siteA = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "A", name = "Site A", isActive = true })));
        var siteB = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "B", name = "Site B", isActive = true })));
        var me = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Tess Tech", isContractor = false, userId, siteIds = new[] { siteA } })));
        var other = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "E2", fullName = "Other Tech", isContractor = false })));
        return new Org(c, me, siteA, siteB, other);
    }

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site, Guid[] assignees, string[]? tasks = null, Guid? asset = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Job", siteId = site, type = "Corrective", priority = "High", assigneeIds = assignees, tasks = tasks ?? [], assetId = asset, dueDate = Today.AddDays(1),
        })));

    private static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name, siteId = site, status = "Active", criticality = "High", warrantyType = "None" })));

    [SkippableFact]
    public async Task The_bundle_holds_my_open_jobs_with_everything_the_job_page_shows()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var asset = await AssetAsync(o.C, o.SiteA, "Pump");
        var mine = await WorkOrderAsync(o.C, o.SiteA, [o.Me], ["Check oil", "Check belt"], asset);
        await WorkOrderAsync(o.C, o.SiteA, [o.Other]);
        var done = await WorkOrderAsync(o.C, o.SiteA, [o.Me]);
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{done}/status", new { status = "Completed", completionNotes = "Done" }));
        var store = Id(await Ok(await o.C.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = o.SiteA })));
        var part = Id(await Ok(await o.C.PostAsJsonAsync("/api/parts", new { partNumber = "P-1", name = "Bearing", unitOfMeasure = "EA", standardPrice = 5, levels = new[] { new { storeId = store, minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } } })));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{mine}/planned-parts", new { partId = part, quantity = 2 }));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{mine}/comments", new { body = "Bring the long ladder" }));

        var b = await Ok(await o.C.GetAsync("/api/tech/bundle"));
        Assert.Equal(o.Me, b.GetProperty("employeeId").GetGuid());
        var job = Assert.Single(b.GetProperty("jobs").EnumerateArray());
        Assert.Equal(mine, Id(job));
        Assert.Equal("Pump", job.GetProperty("asset").GetProperty("name").GetString());
        Assert.Equal(2, job.GetProperty("tasks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("tasks")[0].GetProperty("updatedAt").ValueKind);
        Assert.Equal("P-1", job.GetProperty("plannedParts")[0].GetProperty("partNumber").GetString());
        Assert.Equal("Bring the long ladder", job.GetProperty("comments")[0].GetProperty("body").GetString());
        Assert.True(b.GetProperty("failureCodes").ValueKind == JsonValueKind.Array);
    }

    [SkippableFact]
    public async Task A_user_without_an_employee_record_gets_a_clear_answer()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Office Co", fullName = "Olive", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var r = await c.GetAsync("/api/tech/bundle");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("linked to an employee", await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Places_cover_my_sites_and_answer_304_when_unchanged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var a = await AssetAsync(o.C, o.SiteA, "On A");
        await AssetAsync(o.C, o.SiteB, "On B");
        var first = await o.C.GetAsync("/api/tech/places");
        var p = await Ok(first);
        Assert.Equal([a], p.GetProperty("assets").EnumerateArray().Select(Id));
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/tech/places");
        again.Headers.IfNoneMatch.Add(etag!);
        Assert.Equal(HttpStatusCode.NotModified, (await o.C.SendAsync(again)).StatusCode);

        await AssetAsync(o.C, o.SiteA, "New on A");
        var changed = new HttpRequestMessage(HttpMethod.Get, "/api/tech/places");
        changed.Headers.IfNoneMatch.Add(etag!);
        Assert.Equal(2, (await Ok(await o.C.SendAsync(changed))).GetProperty("assets").GetArrayLength());
    }

    [SkippableFact]
    public async Task Ticking_a_step_records_who_and_when()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me], ["Check oil"]);
        var task = (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks")[0];
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{Id(task)}", new { isDone = true }));
        var after = (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks")[0];
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("updatedAt").ValueKind);
        Assert.Equal("Tess Tech", after.GetProperty("updatedByName").GetString());
        Assert.Equal(o.Me, (await Ok(await o.C.GetAsync("/api/auth/me"))).GetProperty("employeeId").GetGuid());
    }
}
