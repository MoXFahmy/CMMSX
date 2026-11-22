using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmTriggerGenerationTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> CalendarAsync(HttpClient c, string code, params DateOnly[] dates) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/pm-calendars", new { code, name = code, kind = "Dates", dates = dates.Select(Iso) })));

    private static List<string?> DueDates(JsonElement results) =>
        results.EnumerateArray().Where(r => r.GetProperty("workOrderId").ValueKind == JsonValueKind.String).Select(r => r.GetProperty("dueDate").GetString()).ToList();

    private static async Task<JsonElement> DetailAsync(HttpClient c, Guid pm) => await Ok(await c.GetAsync($"/api/pm-schedules/{pm}"));

    private static async Task ReadAsync(HttpClient c, Guid meter, decimal value) =>
        await Ok(await c.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value, confirm = true }));

    [SkippableFact]
    public async Task Existing_schedules_generate_as_before()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var t = await TemplateAsync(org.Admin, "T", ["Grease"]);
        var start = Today.AddMonths(-2);
        var monthly = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, start, asset, extra: new() { ["name"] = "Monthly" })));
        Assert.Equal([Iso(start), Iso(start.AddMonths(1)), Iso(start.AddMonths(2))], DueDates(await GenerateAsync(org.Admin, Today, monthly)));
        Assert.Equal(Iso(start.AddMonths(3)), (await DetailAsync(org.Admin, monthly)).GetProperty("nextDueDate").GetString());

        var meter = await MeterAsync(org.Admin, asset, "PH");
        var mixed = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today.AddDays(10), asset, extra: new()
        {
            ["name"] = "Hours or month", ["type"] = "Meter", ["meterId"] = meter, ["meterTrigger"] = "Incremental", ["meterInterval"] = 100, ["timeBased"] = true,
        })));
        await ReadAsync(org.Admin, meter, 120);
        var d = await DetailAsync(org.Admin, mixed);
        Assert.Equal(1, d.GetProperty("generationCount").GetInt32());
        Assert.Equal(220m, d.GetProperty("nextDueReading").GetDecimal());
        Assert.Equal(Iso(Today.AddMonths(1)), d.GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Calendar_pm_generates_on_listed_dates()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Chiller");
        var cal = await CalendarAsync(org.Admin, "LOW", Today.AddDays(-10), Today, Today.AddDays(20));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today.AddDays(-30), asset,
            extra: new() { ["type"] = "Calendar", ["calendarIds"] = new[] { cal } })));
        Assert.Equal(Iso(Today.AddDays(-10)), (await DetailAsync(org.Admin, pm)).GetProperty("nextDueDate").GetString());
        Assert.Equal([Iso(Today.AddDays(-10)), Iso(Today)], DueDates(await GenerateAsync(org.Admin, Today, pm)));
        var d = await DetailAsync(org.Admin, pm);
        Assert.Equal(Iso(Today.AddDays(20)), d.GetProperty("nextDueDate").GetString());
        Assert.Equal(cal, d.GetProperty("calendarIds").EnumerateArray().Single().GetGuid());
        Assert.Contains(Iso(Today.AddDays(20)), d.GetProperty("upcoming").EnumerateArray().Select(x => x.GetString()));
    }

    [SkippableFact]
    public async Task Own_rule_or_calendar_whichever_first()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Boiler");
        var cal = await CalendarAsync(org.Admin, "EXTRA", Today.AddDays(-25));
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today.AddDays(-40), asset,
            extra: new() { ["calendarIds"] = new[] { cal } })));
        Assert.Equal([Iso(Today.AddDays(-40)), Iso(Today.AddDays(-25))], DueDates(await GenerateAsync(org.Admin, Today, pm)));
        Assert.Equal(Iso(Today.AddDays(-25).AddMonths(1)), (await DetailAsync(org.Admin, pm)).GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Extra_meter_triggers_work_and_resets_all_meters()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var truck = await AssetAsync(org.Admin, org.Site, "Truck");
        var hours = await MeterAsync(org.Admin, truck, "HRS");
        var km = await MeterAsync(org.Admin, truck, "KM", "km");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today, truck, extra: new()
        {
            ["type"] = "Meter", ["meterId"] = hours, ["meterTrigger"] = "Incremental", ["meterInterval"] = 100,
            ["extraMeters"] = new[] { new { meterId = km, interval = 50m } },
        })));
        await ReadAsync(org.Admin, hours, 30);
        await ReadAsync(org.Admin, km, 60);
        var d = await DetailAsync(org.Admin, pm);
        Assert.Equal(1, d.GetProperty("generationCount").GetInt32());
        Assert.Equal(130m, d.GetProperty("nextDueReading").GetDecimal());
        Assert.Equal(110m, d.GetProperty("extraMeters").EnumerateArray().Single().GetProperty("nextDueReading").GetDecimal());
    }

    [SkippableFact]
    public async Task Calendar_needs_scheduled_basis()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var cal = await CalendarAsync(org.Admin, "C", Today.AddDays(5));
        var body = Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today, asset, extra: new() { ["basis"] = "Actual", ["calendarIds"] = new[] { cal } });
        Assert.Contains("Shared calendars need the Scheduled basis.", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules", body)));
        var none = Schedule(org.Site, await TemplateAsync(org.Admin, "U"), Today, asset, extra: new() { ["type"] = "Calendar", ["calendarIds"] = Array.Empty<Guid>() });
        Assert.Contains("calendarIds", await Bad(await org.Admin.PostAsJsonAsync("/api/pm-schedules", none)));
    }

    [SkippableFact]
    public async Task No_next_date_when_calendars_run_out()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var cal = await CalendarAsync(org.Admin, "ONCE", Today);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, await TemplateAsync(org.Admin, "T"), Today, asset,
            extra: new() { ["type"] = "Calendar", ["calendarIds"] = new[] { cal } })));
        var results = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().ToList();
        Assert.Equal(Iso(Today), results[0].GetProperty("dueDate").GetString());
        Assert.Equal($"No next date: its calendars have no dates after {Iso(Today)}.", results[1].GetProperty("skipped").GetString());
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(org.Admin, pm)).GetProperty("nextDueDate").ValueKind);
    }
}
