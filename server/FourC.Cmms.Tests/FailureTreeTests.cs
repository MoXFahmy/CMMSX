using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The failure tree report (0.14.0): symptom → cause → remedy with totals, and links that open the same work orders.</summary>
public class FailureTreeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Period = "from=2026-01-01&to=2026-06-30";

    private static async Task<Guid> CodeAsync(HttpClient c, string path, string code, string name) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name, isActive = true })));

    private static DateTimeOffset March(int day) => new(2026, 3, day, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A completed work order written straight to the database.</summary>
    private Task<Guid> WorkAsync(Loop.Org org, Guid site, DateTimeOffset completed, Guid? symptom, Guid? cause, Guid? remedy, WorkType type = WorkType.Corrective,
        decimal? downtime = null, decimal otherCost = 0, (Guid Employee, decimal Hours, decimal Rate)? labour = null) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder
        {
            TenantId = org.Tenant, Number = $"T-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = site, Type = type, Status = WorkOrderStatus.Completed, CompletedAt = completed,
            SymptomId = symptom, FailureCodeId = cause, RemedyId = remedy, DowntimeHours = downtime, AssetWasDown = downtime is not null,
        };
        db.WorkOrders.Add(w);
        if (otherCost > 0) db.WorkOrderCosts.Add(new WorkOrderCost { TenantId = org.Tenant, WorkOrderId = w.Id, Description = "Contractor", Quantity = 1, UnitCost = otherCost });
        if (labour is { } l) db.WorkOrderLabor.Add(new WorkOrderLabor { TenantId = org.Tenant, WorkOrderId = w.Id, EmployeeId = l.Employee, WorkDate = DateOnly.FromDateTime(completed.UtcDateTime), Hours = l.Hours, Rate = l.Rate });
        await db.SaveChangesAsync();
        return w.Id;
    });

    private static List<JsonElement> Rows(JsonElement report) => report.GetProperty("rows").EnumerateArray().ToList();

    private async Task<Loop.Org> TreeAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var me = (await EmployeeAsync(org.Admin, $"E{Guid.NewGuid():N}"[..8])).Id;
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1", "Leak");
        var (c1, c2) = (await CodeAsync(org.Admin, "/api/failure-codes", "CXA", "Wear"), await CodeAsync(org.Admin, "/api/failure-codes", "CXB", "Dirt"));
        var (r1, r2) = (await CodeAsync(org.Admin, "/api/remedies", "R1", "Seal"), await CodeAsync(org.Admin, "/api/remedies", "R2", "Belt"));
        await WorkAsync(org, org.Site, March(5), s1, c1, r1, downtime: 1, labour: (me, 2, 50));
        await WorkAsync(org, org.Site, March(6), s1, c1, r1, otherCost: 40);
        await WorkAsync(org, org.Site, March(7), s1, c1, r2);
        await WorkAsync(org, org.Site, March(8), s1, c2, null);
        await WorkAsync(org, org.Site, March(9), null, null, null, WorkType.Breakdown, downtime: 2);
        await WorkAsync(org, org.Site, March(10), null, null, null, WorkType.Preventive); // no codes, not a breakdown: left out
        await WorkAsync(org, org.Site, new DateTimeOffset(2025, 12, 1, 10, 0, 0, TimeSpan.Zero), s1, c1, r1); // before the period
        return org;
    }

    [SkippableFact]
    public async Task Tree_has_symptom_cause_remedy_levels_with_totals()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await TreeAsync();
        var rows = Rows(await Ok(await org.Admin.GetAsync($"/api/reports/failure-tree?{Period}")));
        Assert.Equal([
            (0, "S1 · Leak", 4), (1, "CXA · Wear", 3), (2, "R1 · Seal", 2), (2, "R2 · Belt", 1), (1, "CXB · Dirt", 1), (2, "Not recorded", 1),
            (0, "Not recorded", 1), (1, "Not recorded", 1), (2, "Not recorded", 1),
        ], rows.Select(r => (r.GetProperty("level").GetInt32(), r.GetProperty("node").GetString()!, r.GetProperty("workOrders").GetInt32())));
        var s1 = rows[0];
        Assert.Equal((2m, 140m, 1m), (s1.GetProperty("hours").GetDecimal(), s1.GetProperty("cost").GetDecimal(), s1.GetProperty("downtime").GetDecimal()));
        Assert.Equal(2m, rows[6].GetProperty("downtime").GetDecimal());
    }

    [SkippableFact]
    public async Task Tree_line_opens_the_same_work_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await TreeAsync();
        foreach (var row in Rows(await Ok(await org.Admin.GetAsync($"/api/reports/failure-tree?{Period}"))))
        {
            var query = row.GetProperty("listQuery").GetString()!;
            Assert.StartsWith("tab=all&", query); // the list page shows the All tab, not only open work
            var list = await Ok(await org.Admin.GetAsync($"/api/work-orders?pageSize=1&{query}"));
            Assert.Equal(row.GetProperty("workOrders").GetInt32(), list.GetProperty("total").GetInt32());
        }
    }

    [SkippableFact]
    public async Task Tree_follows_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var s1 = await CodeAsync(org.Admin, "/api/symptoms", "S1", "Leak");
        await WorkAsync(org, org.Site, March(5), s1, null, null);
        await WorkAsync(org, other, March(5), s1, null, null);
        await WorkAsync(org, other, March(6), s1, null, null);
        var user = await UserAsync(fx, org.Admin, "Maintenance Manager", [org.Site]);
        var rows = Rows(await Ok(await user.Client.GetAsync($"/api/reports/failure-tree?{Period}")));
        Assert.Equal(1, rows[0].GetProperty("workOrders").GetInt32());
        Assert.Equal(3, Rows(await Ok(await org.Admin.GetAsync($"/api/reports/failure-tree?{Period}")))[0].GetProperty("workOrders").GetInt32());
    }
}
