using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Symptoms on requests and work orders (0.14.0), and the work order list's code filters.</summary>
public class SymptomTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> CodeAsync(HttpClient c, string path, string code, bool active = true) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name = $"{code} name", isActive = active })));

    private static Task<HttpResponseMessage> NewWorkAsync(Loop.Org org, object? symptomId) =>
        org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, priority = "High", type = "Corrective", symptomId });

    private static Task<HttpResponseMessage> EditAsync(Loop.Org org, Guid wo, Guid? symptomId, bool? managesSymptom) =>
        org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Fix", siteId = org.Site, priority = "High", type = "Corrective", symptomId, managesSymptom });

    private static async Task<Guid?> SymptomOfAsync(Loop.Org org, Guid wo) =>
        (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("symptom") is { ValueKind: JsonValueKind.Object } s ? s.GetProperty("id").GetGuid() : null;

    [SkippableFact]
    public async Task Request_symptom_reaches_its_work_order()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var leak = await CodeAsync(org.Admin, "/api/symptoms", "LEAK");
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = org.Site, priority = "High", type = "Corrective", symptomId = leak })));
        Assert.Equal("LEAK", (await Ok(await org.Admin.GetAsync($"/api/work-requests/{request}"))).GetProperty("symptom").GetProperty("code").GetString());
        var wo = (await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        Assert.Equal(leak, await SymptomOfAsync(org, wo));

        var trade = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/trades", new { code = "TPLX", name = "Pipe work", isActive = true })));
        var type = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/request-types", new { code = "PL", name = "Plumbing", isActive = true, autoApprove = true, defaultTradeId = trade })));
        var auto = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Drip", siteId = org.Site, priority = "High", type = "Corrective", symptomId = leak, requestTypeId = type })));
        var autoWo = (await Ok(await org.Admin.GetAsync($"/api/work-requests/{auto}"))).GetProperty("workOrder").GetProperty("id").GetGuid();
        Assert.Equal(leak, await SymptomOfAsync(org, autoWo));
    }

    [SkippableFact]
    public async Task Work_order_symptom_is_set_and_changed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (s1, s2) = (await CodeAsync(org.Admin, "/api/symptoms", "S1"), await CodeAsync(org.Admin, "/api/symptoms", "S2"));
        var wo = Id(await Ok(await NewWorkAsync(org, s1)));
        Assert.Equal(s1, await SymptomOfAsync(org, wo));
        await Ok(await EditAsync(org, wo, s2, true));
        Assert.Equal(s2, await SymptomOfAsync(org, wo));
        await Ok(await EditAsync(org, wo, null, null)); // a page from before 0.14.0: unchanged
        Assert.Equal(s2, await SymptomOfAsync(org, wo));
        await Ok(await EditAsync(org, wo, null, true));
        Assert.Null(await SymptomOfAsync(org, wo));
    }

    [SkippableFact]
    public async Task Inactive_symptom_is_kept_but_not_newly_set()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        var old = await CodeAsync(org.Admin, "/api/symptoms", "OLD", active: false);
        var wo = Id(await Ok(await NewWorkAsync(org, s1)));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/symptoms/{s1}", new { code = "S1", name = "S1 name", isActive = false }));
        await Ok(await EditAsync(org, wo, s1, true));
        Assert.Equal(s1, await SymptomOfAsync(org, wo));
        Assert.Contains("Unknown symptom.", await Bad(await EditAsync(org, wo, old, true)));
        Assert.Contains("Unknown symptom.", await Bad(await NewWorkAsync(org, old)));
        Assert.Contains("Unknown symptom.", await Bad(await org.Admin.PostAsJsonAsync("/api/work-requests",
            new { title = "Leak", siteId = org.Site, priority = "High", type = "Corrective", symptomId = old })));
    }

    [SkippableFact]
    public async Task Work_order_list_filters_by_codes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        var c1 = await CodeAsync(org.Admin, "/api/failure-codes", "C1");
        var r1 = await CodeAsync(org.Admin, "/api/remedies", "R1");
        var a = Id(await Ok(await NewWorkAsync(org, s1)));
        var b = Id(await Ok(await NewWorkAsync(org, null)));
        var c = Id(await Ok(await NewWorkAsync(org, null)));
        var day1 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        await DbAsync(fx, async db =>
        {
            var wa = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == a);
            wa.Status = WorkOrderStatus.Completed; wa.CompletedAt = day1;
            var wc = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == c);
            wc.FailureCodeId = c1; wc.RemedyId = r1; wc.Status = WorkOrderStatus.Completed; wc.CompletedAt = day1.AddDays(1);
            return await db.SaveChangesAsync();
        });
        async Task<List<Guid>> ListAsync(string query) => (await Ok(await org.Admin.GetAsync($"/api/work-orders?pageSize=100&{query}")))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).OrderBy(x => x).ToList();
        Assert.Equal([a], await ListAsync($"symptomId={s1}"));
        Assert.Equal(new[] { b, c }.OrderBy(x => x), await ListAsync("symptomId=none"));
        Assert.Equal([c], await ListAsync($"causeId={c1}&remedyId={r1}"));
        Assert.Equal(new[] { a, b }.OrderBy(x => x), await ListAsync("remedyId=none"));
        Assert.Equal([a], await ListAsync("completedFrom=2026-03-10&completedTo=2026-03-10"));
    }

    [SkippableFact]
    public async Task List_and_export_show_codes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1");
        var r1 = await CodeAsync(org.Admin, "/api/remedies", "R1");
        var wo = Id(await Ok(await NewWorkAsync(org, s1)));
        await DbAsync(fx, async db => { (await db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == wo)).RemedyId = r1; return await db.SaveChangesAsync(); });
        var item = (await Ok(await org.Admin.GetAsync("/api/work-orders?pageSize=100"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == wo);
        Assert.Equal(("S1 name", "R1 name"), (item.GetProperty("symptomName").GetString(), item.GetProperty("remedyName").GetString()));
        var csv = await (await org.Admin.GetAsync("/api/work-orders/export?format=csv&c=number&c=symptomName&c=causeName&c=remedyName")).Content.ReadAsStringAsync();
        Assert.Contains("S1 name", csv);
        Assert.Contains("R1 name", csv);
        await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = org.Site, priority = "High", type = "Corrective", symptomId = s1 }));
        var request = (await Ok(await org.Admin.GetAsync("/api/work-requests?pageSize=100"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("title").GetString() == "Leak");
        Assert.Equal("S1 name", request.GetProperty("symptomName").GetString());
    }
}
