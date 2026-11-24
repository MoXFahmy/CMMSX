using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Every change entry says where it came from and which action saved it, and child lines are recorded (0.25.0).</summary>
public class ActivitySourceTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<List<AuditEntry>> EntriesAsync(Func<AuditEntry, bool> which)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return (await db.AuditEntries.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Where(which).ToList();
    }

    [SkippableFact]
    public async Task Web_phone_and_job_changes_say_where_they_came_from()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Chiller", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var web = Assert.Single(await EntriesAsync(e => e.EntityId == asset));
        Assert.Equal("Web", web.Source);
        Assert.NotEqual(Guid.Empty, web.ChangeId);

        var me = await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low", assigneeIds = new[] { me.Id } })));
        Assert.Equal("done", Phone.Status(await Phone.ActAsync(org.Admin, "labor.add", new { hours = 2, overtimeHours = 0 }, wo)));
        var labour = Assert.Single(await EntriesAsync(e => e.EntityType == "WorkOrderLabor" && e.ParentId == wo));
        Assert.Equal(("Phone", "WorkOrder", "Created"), (labour.Source, labour.ParentType, labour.Action));

        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
            await using var db = new CmmsDbContext(options, new FixedTenantContext(org.Tenant, null, "PM scheduler"));
            var a = await db.Assets.FirstAsync(x => x.Id == asset);
            a.Notes = "Checked by the scheduler";
            await db.SaveChangesAsync();
        }
        var job = (await EntriesAsync(e => e.EntityId == asset)).OrderBy(e => e.Timestamp).Last();
        Assert.Equal(("Job", "PM scheduler"), (job.Source, job.UserName));
    }

    [SkippableFact]
    public async Task One_action_shares_one_change_number()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Service", siteId = org.Site, type = "Preventive", priority = "Low", tasks = new[] { "Check oil", "Check belts" },
        })));
        var entries = await EntriesAsync(e => e.EntityId == wo || e.ParentId == wo);
        Assert.True(entries.Count >= 3, $"{entries.Count} entries");
        Assert.Single(entries.Select(e => e.ChangeId).Distinct());
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/comments", new { body = "Started" }));
        Assert.Equal(2, (await EntriesAsync(e => e.EntityId == wo || e.ParentId == wo)).Select(e => e.ChangeId).Distinct().Count());
    }

    [SkippableFact]
    public async Task Child_lines_are_recorded()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var emp = await EmployeeAsync(org.Admin, "T1");
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low" })));
        var labour = Id(await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp.Id, hours = 2, overtimeHours = 0, workDate = DateOnly.FromDateTime(DateTime.UtcNow) })));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/labor/{labour}", new { employeeId = emp.Id, hours = 3, overtimeHours = 0, workDate = DateOnly.FromDateTime(DateTime.UtcNow) }));
        var lines = await EntriesAsync(e => e.EntityId == labour);
        Assert.Equal(["Created", "Updated"], lines.OrderBy(e => e.Timestamp).Select(e => e.Action));
        Assert.All(lines, e => Assert.Equal(("WorkOrder", (Guid?)wo), (e.ParentType, e.ParentId)));
        Assert.Contains("\"Hours\"", lines.Single(e => e.Action == "Updated").Changes);
        var history = (await Ok(await org.Admin.GetAsync($"/api/audit?entityType=WorkOrder&entityId={wo}"))).EnumerateArray().ToList();
        Assert.Contains(history, h => h.GetProperty("entityType").GetString() == "WorkOrderLabor" && h.GetProperty("source").GetString() == "Web");
    }
}
