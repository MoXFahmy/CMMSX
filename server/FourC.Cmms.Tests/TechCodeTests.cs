using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>Symptom, cause and remedy on the phone (0.14.0), offline-friendly.</summary>
public class TechCodeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> CodeAsync(HttpClient c, string path, string code, bool active = true) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name = $"{code} name", isActive = active })));

    private static readonly Dictionary<Guid, Guid> Mine = [];

    /// <summary>A job assigned to the admin's own employee (made once per organization).</summary>
    private static async Task<Guid> JobAsync(Loop.Org org)
    {
        if (!Mine.TryGetValue(org.Tenant, out var me))
            Mine[org.Tenant] = me = (await EmployeeAsync(org.Admin, $"ME{Guid.NewGuid():N}"[..8], userId: org.AdminId)).Id;
        return Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = org.Site, priority = "High", type = "Corrective", assigneeIds = new[] { me } })));
    }

    private Task<WorkOrder> WoAsync(Guid id) => DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == id));

    private static object Completion(Guid? failureCodeId = null, Guid? remedyId = null) => failureCodeId is null && remedyId is null
        ? new { status = "Completed", completionNotes = "Done" }
        : new { status = "Completed", completionNotes = "Done", failureCodeId, remedyId };

    [SkippableFact]
    public async Task Phone_request_and_work_order_take_a_symptom()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        Assert.Equal("done", Status(await ActAsync(org.Admin, "request.create", new { title = "Phone leak", siteId = org.Site, symptomId = s1 })));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "workorder.create", new { title = "Phone job", siteId = org.Site, symptomId = s1 })));
        var (request, work) = await DbAsync(fx, async db => (
            await db.WorkRequests.IgnoreQueryFilters().SingleAsync(r => r.TenantId == org.Tenant && r.Title == "Phone leak"),
            await db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.TenantId == org.Tenant && w.Title == "Phone job")));
        Assert.Equal((s1, s1), (request.SymptomId!.Value, work.SymptomId!.Value));
    }

    [SkippableFact]
    public async Task Phone_completion_records_cause_and_remedy()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (cause, remedy) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXP1"), await CodeAsync(org.Admin, "/api/remedies", "R1"));
        var wo = await JobAsync(org);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "status.change", Completion(cause, remedy), wo)));
        var w = await WoAsync(wo);
        Assert.Equal((cause, remedy), (w.FailureCodeId!.Value, w.RemedyId!.Value));
    }

    [SkippableFact]
    public async Task Old_phone_completion_keeps_codes_set_on_the_web()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (cause, remedy) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXP2"), await CodeAsync(org.Admin, "/api/remedies", "R2"));
        var wo = await JobAsync(org);
        await DbAsync(fx, async db => { var w = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == wo); w.FailureCodeId = cause; w.RemedyId = remedy; return await db.SaveChangesAsync(); });
        Assert.Equal("done", Status(await ActAsync(org.Admin, "status.change", Completion(), wo))); // a 0.13 phone sends neither
        var after = await WoAsync(wo);
        Assert.Equal((WorkOrderStatus.Completed, cause, remedy), (after.Status, after.FailureCodeId!.Value, after.RemedyId!.Value));
    }

    [SkippableFact]
    public async Task Phone_inactive_code_already_on_the_job_is_accepted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var remedy = await CodeAsync(org.Admin, "/api/remedies", "R3");
        var cause = await CodeAsync(org.Admin, "/api/failure-codes", "CXP3", active: false); // made inactive while the phone was offline
        var wo = await JobAsync(org);
        await DbAsync(fx, async db => { (await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == wo)).RemedyId = remedy; return await db.SaveChangesAsync(); });
        await Ok(await org.Admin.PutAsJsonAsync($"/api/remedies/{remedy}", new { code = "R3", name = "R3 name", isActive = false }));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "status.change", Completion(cause, remedy), wo)));
        var w = await WoAsync(wo);
        Assert.Equal((cause, remedy), (w.FailureCodeId!.Value, w.RemedyId!.Value));
    }

    [SkippableFact]
    public async Task Bundle_has_codes_and_pairs_for_my_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        await CodeAsync(org.Admin, "/api/symptoms", "OLD", active: false);
        var cause = await CodeAsync(org.Admin, "/api/failure-codes", "CXP4");
        var remedy = await CodeAsync(org.Admin, "/api/remedies", "R4");
        var wo = await JobAsync(org);
        var done = await JobAsync(org);
        await DbAsync(fx, async db =>
        {
            (await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == wo)).SymptomId = s1;
            var d = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == done);
            d.SymptomId = s1; d.FailureCodeId = cause; d.RemedyId = remedy; d.Status = WorkOrderStatus.Completed; d.CompletedAt = DateTimeOffset.UtcNow.AddDays(-1);
            return await db.SaveChangesAsync();
        });
        var bundle = await Ok(await org.Admin.GetAsync("/api/tech/bundle"));
        Assert.Equal(["S1"], bundle.GetProperty("symptoms").EnumerateArray().Select(x => x.GetProperty("code").GetString()));
        Assert.Contains(bundle.GetProperty("remedies").EnumerateArray(), x => x.GetProperty("id").GetGuid() == remedy);
        var pair = bundle.GetProperty("codePairs").GetProperty("symptomCause").EnumerateArray().Single();
        Assert.Equal((s1, cause, 1), (pair.GetProperty("first").GetGuid(), pair.GetProperty("second").GetGuid(), pair.GetProperty("count").GetInt32()));
        var job = bundle.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.Equal(s1, job.GetProperty("symptomId").GetGuid());
    }
}
