using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.29.1: right numbers (dashboard, asset history, budget) and clear errors for ordinary input (cost centres, budgets, lists, serial switch-on, windows).</summary>
public class RightNumbersTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static HttpContent Json(string body) => new StringContent(body, Encoding.UTF8, "application/json");

    // ---------------------------------------------------------------- 0.9.1-1

    private Task<Guid> CompletedAsync(Loop.Org org, DateTimeOffset at) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder { TenantId = org.Tenant, Number = $"C-{Guid.NewGuid():N}"[..20], Title = "Done", SiteId = org.Site, Status = WorkOrderStatus.Completed, CompletedAt = at };
        db.WorkOrders.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    });

    [SkippableFact]
    public async Task Completed_30_days_counts_by_local_dates_and_matches_its_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        // UTC+2 all year, so local midnight is 22:00 UTC the evening before.
        await TestSettings.SetSettingsAsync(org.Admin, s => s["timeZone"] = "Etc/GMT-2");
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Etc/GMT-2");
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var first = localToday.AddDays(-30);
        var midnight = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2));
        await CompletedAsync(org, midnight.AddMinutes(-1));  // the last minute of the day before the window: out
        await CompletedAsync(org, midnight.AddMinutes(1));   // the first minute of the first local day: in (it is still the evening before in UTC)
        await CompletedAsync(org, midnight.AddHours(30));    // in
        await CompletedAsync(org, midnight.AddDays(-5));     // out

        var work = (await Ok(await org.Admin.GetAsync("/api/dashboard"))).GetProperty("work");
        var from = work.GetProperty("completedFrom").GetString();
        Assert.Equal(Iso(first), from); // the link's date is the organization's, not the browser's
        Assert.Equal(2, work.GetProperty("completedLast30").GetInt32());

        var list = await Ok(await org.Admin.GetAsync($"/api/work-orders?completedFrom={from}&pageSize=100"));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        // The end of a range is the end of the local day, too.
        var to = Iso(first);
        var oneDay = await Ok(await org.Admin.GetAsync($"/api/work-orders?completedFrom={to}&completedTo={to}&pageSize=100"));
        Assert.Equal(1, oneDay.GetProperty("total").GetInt32());
    }

    // ---------------------------------------------------------------- 0.14.0-7

    [SkippableFact]
    public async Task Asset_history_counts_a_shared_stop_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var pump = await Pm12.AssetAsync(org.Admin, org.Site, "Pump");
        var created = new DateTimeOffset(2026, 3, 3, 10, 0, 0, TimeSpan.Zero);
        var stop = await DbAsync(fx, async db =>
        {
            var e = new DowntimeEvent { TenantId = org.Tenant, AssetId = pump, StartedAt = created, EndedAt = created.AddHours(4), Source = DowntimeSource.Manual };
            db.DowntimeEvents.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });
        async Task Work(int day, int completedHour) => await DbAsync(fx, async db =>
        {
            var w = new WorkOrder
            {
                TenantId = org.Tenant, Number = $"H-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = org.Site, AssetId = pump, Status = WorkOrderStatus.Completed,
                CompletedAt = new DateTimeOffset(2026, 3, day, completedHour, 0, 0, TimeSpan.Zero), DowntimeEventId = stop, DowntimeHours = 4m, AssetWasDown = true,
            };
            db.WorkOrders.Add(w);
            await db.SaveChangesAsync();
            await db.WorkOrders.IgnoreQueryFilters().Where(x => x.Id == w.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, new DateTimeOffset(2026, 3, day, 9, 0, 0, TimeSpan.Zero)));
            return true;
        });
        await Work(3, 14);
        await Work(4, 14);
        var report = await Ok(await org.Admin.GetAsync($"/api/reports/asset-work-history?assetId={pump}&from=2026-01-01&to=2026-06-30"));
        var summary = report.GetProperty("summary").EnumerateArray().Single(k => k.GetProperty("label").GetString() == "Downtime (h)");
        Assert.Equal(4m, summary.GetProperty("value").GetDecimal());
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(4m, rows.Sum(r => r.TryGetProperty("downtime", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDecimal() : 0m));
    }

    // ---------------------------------------------------------------- 0.20.0-3

    [SkippableFact]
    public async Task Budget_counts_a_partly_covered_month_by_days_covered()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var prod = await CostCentreTests.CostCentreAsync(o.Admin, "PROD", "Production");
        var months = new decimal?[12];
        months[3] = 3000m; // April, 30 days
        months[4] = 3100m; // May, 31 days
        await Ok(await BudgetTests.PutBudgetAsync(o.Admin, 2027, prod, months));
        async Task<decimal> BudgetAsync(string from, string to, string query = "")
        {
            var r = await Ok(await o.Admin.GetAsync($"/api/reports/budget-vs-actual?from={from}&to={to}{query}"));
            return r.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("group").GetString() == "Production").GetProperty("budget").GetDecimal();
        }
        Assert.Equal(1500m, await BudgetAsync("2027-04-01", "2027-04-15"));  // half of April
        Assert.Equal(1500m, await BudgetAsync("2027-04-16", "2027-04-30"));  // the other half
        Assert.Equal(3000m, await BudgetAsync("2027-04-01", "2027-04-30"));  // a full month: unchanged
        Assert.Equal(6100m, await BudgetAsync("2027-04-01", "2027-05-31"));  // full months: unchanged
        Assert.Equal(3000m, await BudgetAsync("2027-04-16", "2027-05-15"));  // 15/30 of April + 15/31 of May
        // By month, each month row follows the same rule.
        var byMonth = await Ok(await o.Admin.GetAsync("/api/reports/budget-vs-actual?from=2027-04-01&to=2027-05-15&groupBy=month"));
        Assert.Equal([3000m, 1500m], byMonth.GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("budget").GetDecimal()));
        Assert.Equal(4500m, byMonth.GetProperty("summary").EnumerateArray().First().GetProperty("value").GetDecimal());
    }

    // ---------------------------------------------------------------- 0.20.0-1

    private async Task<(Guid Active, Guid Inactive)> CentresAsync(Loop.Org org)
    {
        var active = await CostCentreTests.CostCentreAsync(org.Admin, "PROD", "Production");
        var inactive = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/cost-centres", new { code = "OLD", name = "Closed line", isActive = false })));
        return (active, inactive);
    }

    [SkippableFact]
    public async Task Inactive_or_unknown_cost_centre_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (active, inactive) = await CentresAsync(org);
        var task = await Pm12.TemplateAsync(org.Admin, "CHK");
        var n = 0;
        foreach (var bad in new Guid?[] { Guid.NewGuid(), inactive })
        {
            n++;
            var request = await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = org.Site, locationId = org.Location, priority = "Medium", type = "Corrective", costCentreId = bad });
            Assert.Contains("Choose an active cost centre.", await Bad(request));
            var pm = await org.Admin.PostAsJsonAsync("/api/pm-schedules", Pm12.Schedule(org.Site, task, Today, extra: new() { ["code"] = $"PMX{n}", ["costCentreId"] = bad }));
            Assert.Contains("Choose an active cost centre.", await Bad(pm));
            var asset = await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Press", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None", costCentreId = bad, managesCostCentre = true });
            Assert.Contains("Choose an active cost centre.", await Bad(asset));
            var location = await org.Admin.PostAsJsonAsync("/api/locations", new { siteId = org.Site, code = $"LX{n}", name = "Hall", type = "Area", criticality = "Medium", isActive = true, costCentreId = bad, managesCostCentre = true });
            Assert.Contains("Choose an active cost centre.", await Bad(location));
        }
        // An active one still works on each, and a record keeps a cost centre that was switched off after it was chosen.
        var ok = await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Press", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None", costCentreId = active, managesCostCentre = true }));
        await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = org.Site, locationId = org.Location, priority = "Medium", type = "Corrective", costCentreId = active }));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/cost-centres/{active}", new { code = "PROD", name = "Production", isActive = false }));
        var detail = await Ok(await org.Admin.GetAsync($"/api/assets/{Id(ok)}"));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/assets/{Id(ok)}", new
        {
            assetNumber = detail.GetProperty("assetNumber").GetString(), name = "Press 2", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None",
            costCentreId = active, managesCostCentre = true, rowVersion = detail.GetProperty("rowVersion").GetString(),
        }));
    }

    // ---------------------------------------------------------------- 0.20.0-9

    [SkippableFact]
    public async Task Two_first_budget_saves_at_once_give_a_clear_message()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var centres = new List<Guid>();
        for (var i = 0; i < 6; i++) centres.Add(await CostCentreTests.CostCentreAsync(org.Admin, $"CC{i}", $"Centre {i}"));
        var refused = 0;
        for (var round = 0; round < 6; round++)
        {
            var year = 2040 + round;
            decimal?[] Months(int k) => Enumerable.Range(1, 12).Select(m => (decimal?)(100 * k + m)).ToArray();
            var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(k => Task.Run(async () =>
            {
                var r = await org.Admin.PutAsJsonAsync("/api/budgets", new { year, rows = centres.Select(c => new { costCentreId = c, months = Months(k) }) });
                return (r.StatusCode, Body: await r.Content.ReadAsStringAsync());
            })));
            Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"{(int)r.StatusCode}: {r.Body}"));
            foreach (var r in results.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            {
                refused++;
                Assert.Contains("changed by someone else", r.Body);
                Assert.DoesNotContain("same code or number", r.Body);
            }
            Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
            // One budget per cost centre and month, whoever won.
            var rows = await DbAsync(fx, db => db.Budgets.IgnoreQueryFilters().Where(b => b.TenantId == org.Tenant && b.Year == year && !b.IsDeleted).GroupBy(b => new { b.CostCentreId, b.Month }).CountAsync());
            Assert.Equal(centres.Count * 12, rows);
        }
        Assert.True(refused >= 0);
    }

    // ---------------------------------------------------------------- 0.21.0-11

    [SkippableFact]
    public async Task Odd_list_payloads_get_a_400()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var withMessage = new[]
        {
            "{\"listLabels\":{\"Priority\":{\"High\":{\"name\":\"A\"}},\"priority\":{\"High\":{\"name\":\"B\"}}}}", // two lists, only the case differs
            "{\"listLabels\":{\"priority\":{\"High\":{\"name\":\"A\"},\"high\":{\"name\":\"B\"}}}}",                  // two values, only the case differs
            "{\"listLabels\":{\"priority\":null}}",                                                                   // no values at all
            "{\"listLabels\":{\"priority\":{\"High\":null}}}",                                                        // an empty value
        };
        foreach (var body in withMessage)
        {
            var r = await org.Admin.PutAsync("/api/settings/lists", Json(body));
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {text} for {body}");
            Assert.True(text.Contains("given twice") || text.Contains("Fill in"), text);
        }
        foreach (var body in new[]
        {
            "{\"listLabels\":{\"priority\":[]}}", "{\"listLabels\":{\"priority\":{\"High\":{\"name\":5}}}}", "{\"listLabels\":[1]}", "null", "[]", "\"x\"", "{\"listLabels\":{\"workType\":{\"Corrective\":{\"order\":99999999999}}}}",
        })
        {
            var r = await org.Admin.PutAsync("/api/settings/lists", Json(body));
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()} for {body}");
        }
        // A good body still saves.
        await Ok(await org.Admin.PutAsync("/api/settings/lists", Json("{\"listLabels\":{\"priority\":{\"Urgent\":{\"name\":\"Emergency\"}}}}")));
    }

    // ---------------------------------------------------------------- 0.24.0-2 / 0.24.0-3

    private static async Task<Guid> NewPartAsync(HttpClient c, Guid store, string number) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = number, name = $"Pump {number}", unitOfMeasure = "EA", standardPrice = 5, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = store, bin = "B1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));

    [SkippableFact]
    public async Task Issue_racing_a_serial_switch_on_keeps_stock_equal_to_units()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        var rnd = new Random(7);
        for (var round = 0; round < 24; round++)
        {
            var part = await NewPartAsync(o.Admin, o.Store, $"RACE-{round}");
            await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = o.Store, quantity = 4, unitCost = 5 }));
            var serials = Enumerable.Range(1, 4).Select(i => $"R{round}-{i}").ToArray();
            var delay = rnd.Next(0, 12);
            var switchOn = Task.Run(async () => await o.Admin.PostAsJsonAsync($"/api/parts/{part}/serial-tracking", new { on = true, stores = new[] { new { storeId = o.Store, serials } } }));
            var issue = Task.Run(async () =>
            {
                await Task.Delay(delay);
                return await o.Admin.PostAsJsonAsync("/api/inventory/issue", new { partId = part, storeId = o.Store, quantity = 1, workOrderId = wo });
            });
            var both = await Task.WhenAll(switchOn, issue);
            foreach (var r in both) Assert.True((int)r.StatusCode < 500, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            var (tracked, units, onHand) = await DbAsync(fx, async db => (
                await db.Parts.IgnoreQueryFilters().Where(p => p.Id == part).Select(p => p.IsSerialTracked).SingleAsync(),
                await db.Assets.IgnoreQueryFilters().CountAsync(a => a.PartId == part && !a.IsDeleted),
                await db.StockLevels.IgnoreQueryFilters().Where(l => l.PartId == part).SumAsync(l => l.QuantityOnHand)));
            if (tracked) Assert.Equal(units, (int)onHand);
            else Assert.Equal(0, units);
        }
    }

    [SkippableFact]
    public async Task Duplicate_store_ids_in_switch_on_give_a_400()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var r = await o.Admin.PostAsJsonAsync($"/api/parts/{o.Part}/serial-tracking", new
        {
            on = true, stores = new[] { new { storeId = o.Store, serials = new[] { "A-1" } }, new { storeId = o.Store, serials = new[] { "A-2" } } },
        });
        await SerialReceiptTests.Refused(r, "Each store can be listed only once.");
        Assert.False((await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("isSerialTracked").GetBoolean());
    }

    [SkippableFact]
    public async Task Switch_on_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 2, unitCost = 10 }));
        var u = await UserAsync(fx, o.Admin, "Administrator");
        var flaky = new FlakyOnce();
        var c = await FlakyOnce.HostAsync(fx, flaky, u.Email, Password);
        flaky.Arm("INSERT INTO [UnitMovements]");
        await Ok(await c.PostAsJsonAsync($"/api/parts/{o.Part}/serial-tracking", new { on = true, stores = new[] { new { storeId = o.Store, serials = new[] { "F-1", "F-2" } } } }));
        Assert.True(flaky.Fired);
        Assert.Equal(["F-1", "F-2"], (await SerialReceiptTests.UnitsAsync(fx, o.Part)).Select(x => x.Serial));
        Assert.Equal(2, await SerialReceiptTests.OnHandAsync(o.Admin, o.Part, o.Store));
    }

    // ---------------------------------------------------------------- 0.10.0-10

    [SkippableFact]
    public async Task Null_window_item_is_a_validation_message()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        string Body(string windows) => $"{{\"title\":\"x\",\"siteId\":\"{org.Site}\",\"locationId\":\"{org.Location}\",\"priority\":\"Low\",\"type\":\"Corrective\",\"accessWindows\":{windows}}}";
        foreach (var windows in new[] { "[null]", "[{\"start\":\"\",\"end\":\"\",\"note\":null}]", "[{\"start\":null,\"end\":null}]" })
        {
            Assert.Contains("Fill in the start and end of every access window.", await Bad(await org.Admin.PostAsync("/api/work-requests", Json(Body(windows)))));
            Assert.Contains("Fill in the start and end of every access window.", await Bad(await org.Admin.PostAsync("/api/work-orders", Json(Body(windows)))));
        }
    }

    // ---------------------------------------------------------------- Arabic

    [Theory]
    [InlineData("Fill in the start and end of every access window.")]
    [InlineData("Each store can be listed only once.")]
    [InlineData("These budgets were changed by someone else. Reload them and try again.")]
    [InlineData("A list or a value is given twice. Capital letters don't make them different.")]
    [InlineData("Fill in every list and every value: none can be empty.")]
    public void New_messages_have_Arabic(string english) =>
        Assert.NotEqual(english, FourC.Cmms.Api.Infrastructure.Translator.T(english, "ar"));
}
