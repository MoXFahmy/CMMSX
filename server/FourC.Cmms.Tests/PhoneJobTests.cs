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
