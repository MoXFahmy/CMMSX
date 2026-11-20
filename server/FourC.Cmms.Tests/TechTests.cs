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

    // ---- Queued changes (Task 3) ----

    private static async Task<JsonElement> SendAsync(HttpClient c, Guid clientId, DateTimeOffset at, string type, Guid? workOrderId, object payload) =>
        await Ok(await c.PostAsJsonAsync("/api/tech/actions", new { clientId, at, type, workOrderId, payload }));

    private static string Status(JsonElement answer) => answer.GetProperty("status").GetString()!;

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<JsonElement> UploadAsync(HttpClient c, Guid clientId, Guid workOrderId, string kind, byte[] bytes, string contentType, string fileName, string? signedBy = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(clientId.ToString()), "clientId" }, { new StringContent(DateTimeOffset.UtcNow.ToString("O")), "at" },
            { new StringContent(workOrderId.ToString()), "workOrderId" }, { new StringContent(kind), "kind" },
        };
        if (signedBy is not null) form.Add(new StringContent(signedBy), "signedBy");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await Ok(await c.PostAsync("/api/tech/files", form));
    }

    private async Task<HttpClient> SecondUserAsync(HttpClient admin, string roleName)
    {
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var role = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == roleName).GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Office Olga", email, isActive = true, roleIds = new[] { role }, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    [SkippableFact]
    public async Task The_same_change_sent_twice_is_applied_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me]);
        var id = Guid.NewGuid();
        var payload = new { workDate = Today, hours = 2, overtimeHours = 0, notes = "Pump" };
        Assert.Equal("done", Status(await SendAsync(o.C, id, DateTimeOffset.UtcNow, "labor.add", wo, payload)));
        Assert.Equal("done", Status(await SendAsync(o.C, id, DateTimeOffset.UtcNow, "labor.add", wo, payload)));
        Assert.Equal(1, (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("labor").GetArrayLength());
    }

    [SkippableFact]
    public async Task A_step_changed_in_the_office_after_the_technician_is_a_conflict()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var office = await SecondUserAsync(o.C, "Administrator");
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me], ["Check oil"]);
        var task = Id((await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks")[0]);
        await Ok(await office.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{task}", new { reading = "Office says 5" }));

        var a = await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), "task.update", wo, new { taskId = task, reading = "Phone says 7" });
        Assert.Equal(("rejected", "conflict"), (Status(a), a.GetProperty("code").GetString()));
        Assert.Contains("Office Olga", a.GetProperty("message").GetString());
        Assert.Equal("done", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), "task.update", wo, new { taskId = task, reading = "Phone says 7", force = true })));
        Assert.Equal("Phone says 7", (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks")[0].GetProperty("reading").GetString());
    }

    [SkippableFact]
    public async Task Changes_keep_the_technicians_time_within_limits()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me], ["One", "Two", "Three"]);
        var tasks = (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks").EnumerateArray().Select(Id).ToList();
        var now = DateTimeOffset.UtcNow;
        await SendAsync(o.C, Guid.NewGuid(), now.AddHours(-2), "task.update", wo, new { taskId = tasks[0], isDone = true });
        await SendAsync(o.C, Guid.NewGuid(), now.AddDays(1), "task.update", wo, new { taskId = tasks[1], isDone = true });
        await SendAsync(o.C, Guid.NewGuid(), now.AddDays(-40), "task.update", wo, new { taskId = tasks[2], isDone = true });
        var done = (await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks").EnumerateArray().ToDictionary(Id, t => t.GetProperty("doneAt").GetDateTimeOffset());
        Assert.InRange(done[tasks[0]], now.AddHours(-2).AddMinutes(-1), now.AddHours(-2).AddMinutes(1));
        Assert.InRange(done[tasks[1]], now.AddMinutes(-1), now.AddMinutes(1));
        Assert.InRange(done[tasks[2]], now.AddDays(-30).AddMinutes(-1), now.AddDays(-30).AddMinutes(1));
    }

    [SkippableFact]
    public async Task Completing_twice_offline_keeps_the_second_technicians_notes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me]);
        Assert.Equal("done", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "status.change", wo, new { status = "Completed", completionNotes = "Replaced belt" })));
        var second = await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "status.change", wo, new { status = "Completed", completionNotes = "Also greased bearings" });
        Assert.Equal("done", Status(second));
        Assert.False(string.IsNullOrEmpty(second.GetProperty("message").GetString()));
        var d = await Ok(await o.C.GetAsync($"/api/work-orders/{wo}"));
        Assert.Equal("Completed", d.GetProperty("status").GetString());
        Assert.Equal("Replaced belt", d.GetProperty("completionNotes").GetString());
        var activity = await Ok(await o.C.GetAsync($"/api/work-orders/{wo}/activity"));
        Assert.Contains(activity.EnumerateArray(), a => a.TryGetProperty("body", out var b) && b.GetString()?.Contains("Also greased bearings") == true);
    }

    [SkippableFact]
    public async Task Moves_that_are_no_longer_allowed_come_back_with_the_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me]);
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Cancelled", note = "Not needed" }));
        var a = await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "status.change", wo, new { status = "InProgress" });
        Assert.Equal("rejected", Status(a));
        Assert.False(string.IsNullOrWhiteSpace(a.GetProperty("message").GetString()));
    }

    [SkippableFact]
    public async Task Changes_to_work_I_cannot_see_are_rejected_not_retried()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var stranger = await SetupAsync();
        var theirs = await WorkOrderAsync(stranger.C, stranger.SiteA, [stranger.Me]);
        var a = await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "comment.add", theirs, new { body = "Hello" });
        Assert.Equal("rejected", Status(a));
        Assert.Equal("rejected", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "no.such.thing", null, new { })));
    }

    [SkippableFact]
    public async Task Meter_readings_and_requests_from_the_phone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var asset = await AssetAsync(o.C, o.SiteA, "Compressor");
        var meter = Id(await Ok(await o.C.PostAsJsonAsync("/api/meters", new { code = "M1", name = "Hours", assetId = asset, unit = "h", maxDailyIncrement = 24, isActive = true })));
        Assert.Equal("done", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "reading.add", null, new { meterId = meter, value = 100 })));
        Assert.Equal("rejected", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "reading.add", null, new { meterId = meter, value = 5000 })));
        Assert.Equal("done", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "reading.add", null, new { meterId = meter, value = 5000, confirm = true })));

        Assert.Equal("done", Status(await SendAsync(o.C, Guid.NewGuid(), DateTimeOffset.UtcNow, "request.create", null,
            new { title = "Oil leak under compressor", description = "Small puddle", priority = "High", assetId = asset, siteId = o.SiteA })));
        var requests = await Ok(await o.C.GetAsync("/api/work-requests?pageSize=50"));
        var r = requests.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("title").GetString() == "Oil leak under compressor");
        Assert.Equal(asset, r.GetProperty("assetId").GetGuid());
    }

    [SkippableFact]
    public async Task Photos_and_signatures_attach_once_and_bad_files_are_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await WorkOrderAsync(o.C, o.SiteA, [o.Me]);
        var photo = Guid.NewGuid();
        Assert.Equal("done", Status(await UploadAsync(o.C, photo, wo, "photo", Png, "image/png", "leak.png")));
        Assert.Equal("done", Status(await UploadAsync(o.C, photo, wo, "photo", Png, "image/png", "leak.png")));
        Assert.Equal("done", Status(await UploadAsync(o.C, Guid.NewGuid(), wo, "signature", Png, "image/png", "signature.png", "Mr. Customer")));
        Assert.Equal("rejected", Status(await UploadAsync(o.C, Guid.NewGuid(), wo, "photo", "hello"u8.ToArray(), "text/plain", "note.txt")));
        Assert.Equal("rejected", Status(await UploadAsync(o.C, Guid.NewGuid(), wo, "photo", new byte[11 * 1024 * 1024], "image/jpeg", "huge.jpg")));

        var files = (await Ok(await o.C.GetAsync($"/api/tech/bundle"))).GetProperty("jobs")[0].GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        var sig = Assert.Single(files, f => f.GetProperty("isSignature").GetBoolean());
        Assert.Equal("Mr. Customer", sig.GetProperty("signedBy").GetString());
    }
}
