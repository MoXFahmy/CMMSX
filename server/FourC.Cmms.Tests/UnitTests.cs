using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: the units list — parts, measurement points and template steps keep the symbol as their text.</summary>
public class UnitTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static Task<HttpResponseMessage> PostUnitAsync(HttpClient c, string symbol, string name) => c.PostAsJsonAsync("/api/units", new { symbol, name, isActive = true });

    [SkippableFact]
    public async Task Renaming_a_unit_updates_parts_points_and_template_steps_not_work_orders()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var unit = Id(await Ok(await PostUnitAsync(o.Admin, "BX", "Boxes")));
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P-1", name = "Seal", unitOfMeasure = "BX", standardPrice = 1, isCritical = false, isShelfItem = false, allowNegativeStock = false, levels = Array.Empty<object>(),
        })));
        var point = await Ck13.PointAsync(o.Admin, "CNT", new() { ["unit"] = "BX" });
        var template = await Ck13.TypedTemplateAsync(o.Admin, "T1", [new { description = "Count", type = "Number", unit = "BX" }]);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Count", siteId = o.Site, priority = "Medium", type = "Preventive", templateId = template })));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/units/{unit}", new { symbol = "box", name = "Boxes", isActive = true }));
        Assert.Equal("box", (await Ok(await o.Admin.GetAsync($"/api/parts/{part}"))).GetProperty("unitOfMeasure").GetString());
        Assert.Equal("box", (await Ok(await o.Admin.GetAsync($"/api/measurement-points/{point}"))).GetProperty("unit").GetString());
        Assert.Equal("box", (await Ok(await o.Admin.GetAsync($"/api/pm-tasks/{template}"))).GetProperty("stepDetails")[0].GetProperty("unit").GetString());
        var stepUnit = await Loop.DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == wo).Select(t => t.Unit).FirstAsync());
        Assert.Equal("BX", stepUnit);
    }

    [SkippableFact]
    public async Task Units_are_case_sensitive()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var mw = Id(await Ok(await PostUnitAsync(o.Admin, "mW", "Milliwatts")));
        var bigMw = Id(await Ok(await PostUnitAsync(o.Admin, "MW", "Megawatts")));
        var again = await PostUnitAsync(o.Admin, "mW", "Again");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("This symbol is already used.", await again.Content.ReadAsStringAsync());
        var conflict = await o.Admin.PutAsJsonAsync($"/api/units/{bigMw}", new { symbol = "mW", name = "Megawatts", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(mw, (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mergeWith").GetGuid());
        var symbols = (await Ok(await o.Admin.GetAsync("/api/units"))).EnumerateArray().Select(u => u.GetProperty("symbol").GetString()).ToList();
        Assert.Contains("mW", symbols);
        Assert.Contains("MW", symbols);
        Assert.Contains("kWh", symbols); // a new organization's starter units
    }
}
