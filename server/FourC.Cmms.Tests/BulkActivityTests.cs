using System.Net.Http.Json;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>Changes made in bulk still reach the activity log (0.25.0).</summary>
public class BulkActivityTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<List<AuditEntry>> EntriesAsync(Func<AuditEntry, bool> which)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return (await db.AuditEntries.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Where(which).ToList();
    }

    [SkippableFact]
    public async Task Deleting_lines_logs_each_line()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var emp = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech One", isActive = true, isContractor = false })));
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, type = "Corrective", priority = "Low" })));
        var labour = Id(await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = emp, hours = 2, overtimeHours = 0, workDate = DateOnly.FromDateTime(DateTime.UtcNow) })));
        var cost = Id(await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/costs", new { description = "Crane hire", quantity = 1m, unitCost = 40m })));
        Assert.True((await o.Admin.DeleteAsync($"/api/work-orders/{wo}/labor/{labour}")).IsSuccessStatusCode);
        Assert.True((await o.Admin.DeleteAsync($"/api/work-orders/{wo}/costs/{cost}")).IsSuccessStatusCode);
        var gone = await EntriesAsync(e => e.Action == "Deleted" && e.ParentId == wo);
        Assert.Contains(gone, e => e.EntityType == "WorkOrderLabor" && e.EntityId == labour);
        var c = Assert.Single(gone, e => e.EntityType == "WorkOrderCost");
        Assert.Contains("Crane hire", c.Changes);
    }

    [SkippableFact]
    public async Task Merges_write_a_summary()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var grundfos = await BrandTests.BrandAsync(o.Admin, "Grundfos");
        var pumps = await BrandTests.BrandAsync(o.Admin, "Grundfos Pumps");
        await BrandTests.AssetAsync(o.Admin, o.Site, "Pump B", pumps, null);
        await Ok(await o.Admin.PutAsJsonAsync($"/api/brands/{pumps}", new { name = "Grundfos", isActive = true, merge = true }));
        var summary = Assert.Single(await EntriesAsync(e => e.EntityId == grundfos && e.Changes?.Contains("Summary") == true));
        Assert.Contains("Brand merged", summary.Changes);
        Assert.Contains("1 asset", summary.Changes);
    }

    [SkippableFact]
    public async Task Unit_rename_writes_a_summary()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var unit = Id(await Ok(await UnitTests.PostUnitAsync(o.Admin, "BX", "Box")));
        await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P-1", name = "Seal", unitOfMeasure = "BX", standardPrice = 1, isCritical = false, isShelfItem = false, allowNegativeStock = false, levels = Array.Empty<object>(),
        }));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/units/{unit}", new { symbol = "box", name = "Boxes", isActive = true }));
        var summary = Assert.Single(await EntriesAsync(e => e.EntityId == unit && e.Changes?.Contains("Summary") == true));
        Assert.Contains("Unit renamed to box", summary.Changes);
        Assert.Contains("1 part", summary.Changes);
    }
}
