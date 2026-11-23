using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.AvailabilityTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: the phone's bundle carries my last 30 days of hours on every work order and my available hours, for My day.</summary>
public class TechDayTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The admin as an employee (the phone works for a person), every day 08:00–16:00.</summary>
    private static async Task<Guid> MeAsync(PurchaseRequestTests.Org o)
    {
        var me = (await Ok(await o.Admin.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var days = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), start = "08:00", end = "16:00" });
        return Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "ME", fullName = "Admin", isContractor = false, userId = me, siteIds = new[] { o.Site }, workingHours = days })));
    }

    [SkippableFact]
    public async Task Bundle_carries_my_last_30_days_of_hours_on_every_work_order()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var me = await MeAsync(o);
        var job = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Pump", siteId = o.Site, type = "Corrective", priority = "Medium", assigneeIds = new[] { me } })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{job}/labor", new { employeeId = me, workDate = D(Today), hours = 2m, overtimeHours = 0m }));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{job}/labor", new { employeeId = me, workDate = D(Today.AddDays(-40)), hours = 5m, overtimeHours = 0m }));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{job}/status", new { status = "InProgress" }));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{job}/status", new { status = "Completed", completionNotes = "Done" }));
        var bundle = await Ok(await o.Admin.GetAsync("/api/tech/bundle"));
        Assert.DoesNotContain(bundle.GetProperty("jobs").EnumerateArray(), j => Id(j) == job);
        var labour = bundle.GetProperty("myDay").GetProperty("labour").EnumerateArray().ToList();
        var entry = Assert.Single(labour);
        Assert.Equal(job, entry.GetProperty("workOrderId").GetGuid());
        Assert.Equal((D(Today), 2m), (entry.GetProperty("workDate").GetString(), entry.GetProperty("hours").GetDecimal()));
        Assert.Equal("Pump", entry.GetProperty("title").GetString());
        Assert.StartsWith("WO-", entry.GetProperty("number").GetString());
    }

    [SkippableFact]
    public async Task Bundle_carries_my_available_hours()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var me = await MeAsync(o);
        var types = await TypesAsync(o.Admin);
        await AddEntryAsync(o.Admin, Entry(types["HOL"], person: me, from: Today));
        var available = (await Ok(await o.Admin.GetAsync("/api/tech/bundle"))).GetProperty("myDay").GetProperty("available").EnumerateArray()
            .ToDictionary(d => d.GetProperty("day").GetString()!);
        Assert.Equal(31, available.Count);
        Assert.Equal(0m, available[D(Today)].GetProperty("hours").GetDecimal());
        Assert.Equal(["Holiday"], available[D(Today)].GetProperty("away").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(8m, available[D(Today.AddDays(-1))].GetProperty("hours").GetDecimal());
    }
}
