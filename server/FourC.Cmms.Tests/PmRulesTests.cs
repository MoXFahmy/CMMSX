using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class PmRulesTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string? Skipped(JsonElement r) => r.GetProperty("skipped").ValueKind == JsonValueKind.String ? r.GetProperty("skipped").GetString() : null;
    private static async Task<JsonElement> WoAsync(HttpClient c, JsonElement result) => await Ok(await c.GetAsync($"/api/work-orders/{result.GetProperty("workOrderId").GetGuid()}"));
    private static async Task<string> NumberAsync(HttpClient c, Guid asset) => (await Ok(await c.GetAsync($"/api/assets/{asset}"))).GetProperty("assetNumber").GetString()!;
    private static Dictionary<string, object?> Rules(object? skip = null, object? nonWorking = null, bool? noStacking = null) =>
        new() { ["managesRules"] = true, ["skipStatusesOverride"] = skip, ["nonWorkingDaysOverride"] = nonWorking, ["noStackingOverride"] = noStacking };

    [SkippableFact]
    public async Task Pm_override_skips_statuses_the_org_does_not()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var down = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Broken", siteId = org.Site, status = "UnderRepair", criticality = "High", warrantyType = "None" })));
        var t = await TemplateAsync(org.Admin, "T");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, down, extra: Rules(skip: new[] { "UnderRepair" }))));
        Assert.Equal("Skipped: the asset is Under repair", Skipped((await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single()));
    }

    [SkippableFact]
    public async Task Pm_override_clears_an_org_rule()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await TestSettings.SetSettingsAsync(org.Admin, s => s["pmSkipAssetStatuses"] = new JsonArray("UnderRepair"));
        var down = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Broken", siteId = org.Site, status = "UnderRepair", criticality = "High", warrantyType = "None" })));
        var t = await TemplateAsync(org.Admin, "T");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, down, extra: Rules(skip: Array.Empty<string>()))));
        Assert.Null(Skipped((await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single()));
        var detail = await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.Equal(0, detail.GetProperty("skipStatusesOverride").GetArrayLength());
    }

    [SkippableFact]
    public async Task Pm_non_working_days_override()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var t = await TemplateAsync(org.Admin, "T");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today, asset, extra: Rules(nonWorking: new[] { Today.DayOfWeek.ToString() }))));
        var wo = await WoAsync(org.Admin, (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single());
        Assert.Equal(Iso(Today.AddDays(1)), wo.GetProperty("plannedStart").GetString());
    }

    [SkippableFact]
    public async Task Pm_non_working_days_keep_a_working_day()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var t = await TemplateAsync(org.Admin, "T");
        var all = Enum.GetNames<DayOfWeek>();
        var r = await org.Admin.PostAsJsonAsync("/api/pm-schedules", Schedule(org.Site, t, Today, asset, extra: Rules(nonWorking: all)));
        Assert.Contains("nonWorkingDaysOverride", await Bad(r));
    }

    [SkippableFact]
    public async Task Pm_no_stacking_override()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Fan");
        var t = await TemplateAsync(org.Admin, "T");
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, t, Today.AddMonths(-1), asset, extra: Rules(noStacking: true))));
        var results = (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.Null(Skipped(results[0]));
        Assert.Equal("Skipped: the previous work order is still open", Skipped(results[1]));
    }

    [SkippableFact]
    public async Task Round_uses_each_assets_template()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { defaultStoreId = store });
        var belt = await PartAsync(org.Admin, store, "BELT");
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var routine = await TemplateAsync(org.Admin, "R", ["Routine step"]);
        var special = await TemplateAsync(org.Admin, "X", ["Special step"], new() { ["parts"] = new[] { new { partId = belt, quantity = 1 } } });
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today, assets: [a, b],
            extra: new() { ["assetTemplates"] = new[] { new { assetId = b, pmTaskId = (Guid?)special } } })));
        var wo = await WoAsync(org.Admin, (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Single());
        var steps = wo.GetProperty("tasks").EnumerateArray().Select(x => (x.GetProperty("assetId").GetGuid(), x.GetProperty("description").GetString())).ToList();
        Assert.Equal([(a, "Routine step"), (b, "Special step")], steps);
        Assert.Equal(1m, wo.GetProperty("plannedParts").EnumerateArray().Single(p => p.GetProperty("partId").GetGuid() == belt).GetProperty("quantity").GetDecimal());
        var detail = await Ok(await org.Admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.Equal(special, detail.GetProperty("assets").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == b).GetProperty("pmTaskId").GetGuid());
    }

    [SkippableFact]
    public async Task Every_nth_template_applies_to_every_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var routine = await TemplateAsync(org.Admin, "R", ["Routine step"]);
        var special = await TemplateAsync(org.Admin, "X", ["Special step"]);
        var big = await TemplateAsync(org.Admin, "N", ["Overhaul"]);
        var pm = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today.AddMonths(-1), assets: [a, b], extra: new()
        {
            ["assetTemplates"] = new[] { new { assetId = b, pmTaskId = (Guid?)special } }, ["extraTasks"] = new[] { new { pmTaskId = big, everyNth = 2 } },
        })));
        var second = await WoAsync(org.Admin, (await GenerateAsync(org.Admin, Today, pm)).EnumerateArray().Last());
        Assert.All(second.GetProperty("tasks").EnumerateArray(), x => Assert.Equal("Overhaul", x.GetProperty("description").GetString()));
        Assert.Equal(2, second.GetProperty("tasks").GetArrayLength());
    }

    [SkippableFact]
    public async Task Saving_warns_when_an_asset_gets_no_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "Fan A");
        var b = await AssetAsync(org.Admin, org.Site, "Fan B");
        var routine = await TemplateAsync(org.Admin, "R", ["Routine step"]);
        var empty = await TemplateAsync(org.Admin, "E", []);
        var saved = await SaveScheduleAsync(org.Admin, Schedule(org.Site, routine, Today, assets: [a, b],
            extra: new() { ["assetTemplates"] = new[] { new { assetId = b, pmTaskId = (Guid?)empty } } }));
        Assert.Equal($"These assets get no checklist steps: {await NumberAsync(org.Admin, b)}.", saved.GetProperty("warnings").EnumerateArray().Single().GetString());
    }
}
