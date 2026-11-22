using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>Asset work history (0.14.0), PM compliance by asset category, and maintenance costs by technician.</summary>
public class AssetHistoryReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Period = "from=2026-01-01&to=2026-06-30";
    private static DateTimeOffset March(int day) => new(2026, 3, day, 10, 0, 0, TimeSpan.Zero);

    private Task<Guid> WorkAsync(Loop.Org org, Guid? asset, DateTimeOffset created, Guid? symptom = null, decimal otherCost = 0, Guid? pm = null, DateOnly? due = null,
        DateTimeOffset? completed = null, Guid[]? roundAssets = null) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder
        {
            TenantId = org.Tenant, Number = $"H-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = org.Site, AssetId = asset, SymptomId = symptom, PmScheduleId = pm, PmDueDate = due,
            DueDate = due, Status = completed is null ? WorkOrderStatus.Open : WorkOrderStatus.Completed, CompletedAt = completed,
        };
        foreach (var (a, i) in (roundAssets ?? []).Select((a, i) => (a, i))) w.Assets.Add(new WorkOrderAsset { WorkOrderId = w.Id, AssetId = a, Sequence = i + 1 });
        db.WorkOrders.Add(w);
        db.Entry(w).Property(x => x.CreatedAt).CurrentValue = created;
        if (otherCost > 0) db.WorkOrderCosts.Add(new WorkOrderCost { TenantId = org.Tenant, WorkOrderId = w.Id, Description = "Parts bought", Quantity = 1, UnitCost = otherCost });
        await db.SaveChangesAsync();
        // Created time as the test wants it (saving stamps now).
        await db.WorkOrders.Where(x => x.Id == w.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, created));
        return w.Id;
    });

    private static List<JsonElement> Rows(JsonElement report) => report.GetProperty("rows").EnumerateArray().ToList();

    [SkippableFact]
    public async Task Asset_history_lists_its_work_with_codes_and_costs()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var pump = await AssetAsync(org.Admin, org.Site, "Pump");
        var motor = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Motor", siteId = org.Site, parentAssetId = pump, status = "Active", criticality = "High", warrantyType = "None" })));
        var leak = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/symptoms", new { code = "LEAK", name = "Leaking", isActive = true })));
        var coded = await WorkAsync(org, pump, March(3), leak, otherCost: 75);
        await WorkAsync(org, pump, March(4));
        await WorkAsync(org, motor, March(5));
        await WorkAsync(org, null, March(6), roundAssets: [pump]); // a round that only lists it: left out
        var own = Rows(await Ok(await org.Admin.GetAsync($"/api/reports/asset-work-history?assetId={pump}&{Period}")));
        Assert.Equal(2, own.Count);
        var row = own.Single(r => r.GetProperty("workOrderId").GetGuid() == coded);
        Assert.Equal(("LEAK · Leaking", 75m), (row.GetProperty("symptom").GetString(), row.GetProperty("cost").GetDecimal()));
        Assert.Equal(3, Rows(await Ok(await org.Admin.GetAsync($"/api/reports/asset-work-history?assetId={pump}&components=true&{Period}"))).Count);
    }

    [SkippableFact]
    public async Task Asset_report_needs_an_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("Choose an asset.", await Bad(await org.Admin.GetAsync($"/api/reports/asset-work-history?{Period}")));
    }

    [SkippableFact]
    public async Task Asset_history_of_an_asset_I_cannot_see_is_not_found()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var hidden = await AssetAsync(org.Admin, other, "Hidden pump");
        var user = await UserAsync(fx, org.Admin, "Maintenance Manager", [org.Site]);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/reports/asset-work-history?assetId={hidden}&{Period}")).StatusCode);
    }

    [SkippableFact]
    public async Task Pm_compliance_groups_by_category()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var pumps = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "CPMP", name = "Pump kit" })));
        var fans = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "CFAN", name = "Fan kit" })));
        var (p, f) = (await AssetAsync(org.Admin, org.Site, "Pump", category: pumps), await AssetAsync(org.Admin, org.Site, "Fan", category: fans));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "CPM"), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2), p)));
        await WorkAsync(org, p, March(1), pm: pm, due: new DateOnly(2026, 3, 1), completed: March(1));
        await WorkAsync(org, f, March(1), pm: pm, due: new DateOnly(2026, 3, 2), completed: March(5));
        var groups = Rows(await Ok(await org.Admin.GetAsync($"/api/reports/pm-compliance?{Period}&groupBy=category")))
            .ToDictionary(r => r.GetProperty("group").GetString()!, r => (r.GetProperty("onTime").GetInt32(), r.GetProperty("late").GetInt32()));
        Assert.Equal(new Dictionary<string, (int, int)> { ["Pump kit"] = (1, 0), ["Fan kit"] = (0, 1) }, groups);
    }

    [SkippableFact]
    public async Task Costs_by_technician_show_their_labour_and_hours()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var ann = await EmployeeAsync(org.Admin, "ANN");
        var bob = await EmployeeAsync(org.Admin, "BOB");
        var wo = await WorkAsync(org, null, March(2), otherCost: 30);
        await DbAsync(fx, db =>
        {
            db.WorkOrderLabor.Add(new WorkOrderLabor { TenantId = org.Tenant, WorkOrderId = wo, EmployeeId = ann.Id, WorkDate = new DateOnly(2026, 3, 2), Hours = 2, Rate = 50 });
            db.WorkOrderLabor.Add(new WorkOrderLabor { TenantId = org.Tenant, WorkOrderId = wo, EmployeeId = bob.Id, WorkDate = new DateOnly(2026, 3, 2), Hours = 1, Rate = 40, OvertimeHours = 1, OvertimeRate = 60 });
            return db.SaveChangesAsync();
        });
        var rows = Rows(await Ok(await org.Admin.GetAsync($"/api/reports/maintenance-costs?{Period}&groupBy=technician")));
        var byName = rows.ToDictionary(r => r.GetProperty("group").GetString()!, r => r);
        Assert.Equal(2, byName.Count);
        var bobRow = byName.Single(x => x.Key.Contains("BOB")).Value;
        Assert.Equal((100m, 2m, JsonValueKind.Null, 1), (bobRow.GetProperty("labor").GetDecimal(), bobRow.GetProperty("hours").GetDecimal(), bobRow.GetProperty("parts").ValueKind,
            bobRow.GetProperty("workOrders").GetInt32()));
        var byAsset = Rows(await Ok(await org.Admin.GetAsync($"/api/reports/maintenance-costs?{Period}"))).Single();
        Assert.Equal(4m, byAsset.GetProperty("hours").GetDecimal()); // overtime hours count too
    }
}
