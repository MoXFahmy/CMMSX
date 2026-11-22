using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class PhoneJobTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<(Loop.Org Org, Guid Me, Guid Wo)> JobAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Service fan", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id } })));
        return (org, me.Id, wo);
    }

    private static async Task<JsonElement> WoAsync(HttpClient c, Guid wo) => await Ok(await c.GetAsync($"/api/work-orders/{wo}"));

    [SkippableFact]
    public async Task A_checklist_comes_from_a_pm_task()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, wo) = await JobAsync();
        var task = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/pm-tasks", new { code = "FAN", name = "Fan service", steps = new[] { "Isolate", "Clean blades", "Test run" } })));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "tasks.fromPm", new { pmTaskId = task }, wo)));
        var steps = (await WoAsync(org.Admin, wo)).GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("description").GetString()).ToArray();
        Assert.Equal(["Isolate", "Clean blades", "Test run"], steps);
    }

    [SkippableFact]
    public async Task A_crew_leader_logs_hours_for_another_technician()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, me, wo) = await JobAsync();
        var mate = await EmployeeAsync(org.Admin, "MATE");
        Assert.Equal("done", Status(await ActAsync(org.Admin, "labor.add", new { hours = 2, overtimeHours = 0, employeeId = mate.Id }, wo)));
        var labor = (await WoAsync(org.Admin, wo)).GetProperty("labor").EnumerateArray().Single();
        Assert.Equal(mate.Id, labor.GetProperty("employeeId").GetGuid());

        var tech = await UserAsync(fx, org.Admin, "Technician");
        var techEmp = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech", userId = tech.Id, isContractor = false })));
        var answer = await ActAsync(tech.Client, "labor.add", new { hours = 1, overtimeHours = 0, employeeId = mate.Id }, wo);
        Assert.Equal("rejected", Status(answer));
    }

    [SkippableFact]
    public async Task Cancelling_on_the_phone_needs_close_and_a_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, wo) = await JobAsync();
        var reason = await AnyCancelReasonAsync(org.Admin);
        Assert.Equal("rejected", Status(await ActAsync(org.Admin, "status.change", new { status = "Cancelled" }, wo)));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "status.change", new { status = "Cancelled", cancelReasonId = reason, note = "Duplicate job" }, wo)));
        var w = await WoAsync(org.Admin, wo);
        Assert.Equal("Cancelled", w.GetProperty("status").GetString());
        Assert.Equal(reason, w.GetProperty("cancelReason").GetProperty("id").GetGuid());

        var tech = await UserAsync(fx, org.Admin, "Technician");
        var techEmp = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech", userId = tech.Id, isContractor = false })));
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Other", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { techEmp } })));
        Assert.Equal("rejected", Status(await ActAsync(tech.Client, "status.change", new { status = "Cancelled", cancelReasonId = reason }, other)));
    }

    [SkippableFact]
    public async Task A_work_order_can_be_created_on_the_phone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, me, _) = await JobAsync();
        var id = Guid.NewGuid();
        Assert.Equal("done", Status(await ActAsync(org.Admin, "workorder.create", new { title = "Loose guard", siteId = org.Site, locationId = org.Location, priority = "High", type = "Corrective" }, clientId: id)));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "workorder.create", new { title = "Loose guard", siteId = org.Site, locationId = org.Location, priority = "High", type = "Corrective" }, clientId: id)));
        var jobs = (await Ok(await org.Admin.GetAsync("/api/tech/bundle"))).GetProperty("jobs").EnumerateArray().Where(j => j.GetProperty("title").GetString() == "Loose guard").ToList();
        Assert.Single(jobs);

        var requester = await UserAsync(fx, org.Admin, "Requester");
        await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "R1", fullName = "Req", userId = requester.Id, isContractor = false }));
        Assert.Equal("rejected", Status(await ActAsync(requester.Client, "workorder.create", new { title = "No", siteId = org.Site, priority = "Low", type = "Corrective" })));
    }
}

public class PhoneFileTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> UploadAsync(HttpClient c, Guid workOrderId, string kind, byte[] bytes, string contentType, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "clientId" }, { new StringContent(DateTimeOffset.UtcNow.ToString("O")), "at" },
            { new StringContent(workOrderId.ToString()), "workOrderId" }, { new StringContent(kind), "kind" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await Ok(await c.PostAsync("/api/tech/files", form));
    }

    private async Task<(Loop.Org Org, Guid Wo)> JobAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Film the fault", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective", assigneeIds = new[] { me.Id } })));
        return (org, wo);
    }

    [SkippableFact]
    public async Task A_pdf_and_a_video_attach_to_the_job()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await JobAsync();
        Assert.Equal("done", Status(await UploadAsync(org.Admin, wo, "file", "%PDF-1.4 test"u8.ToArray(), "application/pdf", "manual.pdf")));
        Assert.Equal("done", Status(await UploadAsync(org.Admin, wo, "file", new byte[2048], "video/mp4", "noise.mp4")));
        var files = await Ok(await org.Admin.GetAsync($"/api/attachments?entityType=WorkOrder&entityId={wo}"));
        Assert.Equal(["manual.pdf", "noise.mp4"], files.EnumerateArray().Select(f => f.GetProperty("fileName").GetString()!).Order().ToArray());
        Assert.Equal("rejected", Status(await UploadAsync(org.Admin, wo, "file", new byte[10], "application/x-msdownload", "tool.exe")));
    }

    [SkippableFact]
    public async Task Size_limits_are_50_MB_for_files_and_10_MB_for_photos()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, wo) = await JobAsync();
        var big = await UploadAsync(org.Admin, wo, "file", new byte[50 * 1024 * 1024 + 1], "video/mp4", "long.mp4");
        Assert.Equal("rejected", Status(big));
        Assert.Equal("long.mp4 is larger than 50 MB.", Message(big));
        Assert.Equal("rejected", Status(await UploadAsync(org.Admin, wo, "photo", new byte[10 * 1024 * 1024 + 1], "image/jpeg", "huge.jpg")));
    }
}
