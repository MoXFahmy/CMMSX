using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: reports, labels and AI pages show names in the reader's language; AI prompts keep the main texts.</summary>
public class BilingualReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Period = "from=2026-01-01&to=2026-06-30";
    private static readonly DateTimeOffset March = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);

    private static async Task<string> TextAsync(HttpClient c, string url, string language) =>
        await (await SendAsync(c, HttpMethod.Get, url, language)).Content.ReadAsStringAsync();

    /// <summary>A completed work order in March on <paramref name="asset"/>, with a symptom and a cost line, written straight to the database.</summary>
    private Task<Guid> DoneAsync(Loop.Org org, Guid asset, Guid symptom) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder
        {
            TenantId = org.Tenant, Number = $"T-{Guid.NewGuid():N}"[..20], Title = "Seal change", TitleAr = "تغيير الحشوة", SiteId = org.Site, AssetId = asset,
            Type = WorkType.Corrective, Status = WorkOrderStatus.Completed, CompletedAt = March, SymptomId = symptom,
        };
        db.WorkOrders.Add(w);
        db.WorkOrderCosts.Add(new WorkOrderCost { TenantId = org.Tenant, WorkOrderId = w.Id, Description = "Contractor", Quantity = 1, UnitCost = 40 });
        await db.SaveChangesAsync();
        return w.Id;
    });

    [SkippableFact]
    public async Task Reports_show_names_in_the_readers_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var symptom = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/symptoms", new { code = "LK", name = "Leak", nameAr = "تسريب", isActive = true })));
        var asset = await AssetAsync(o.Admin, o.Site);
        await DoneAsync(o, asset, symptom);

        Assert.Contains("تسريب", await TextAsync(o.Admin, $"/api/reports/failure-tree?{Period}", "ar"));
        Assert.Contains("Leak", await TextAsync(o.Admin, $"/api/reports/failure-tree?{Period}", "en"));
        var costs = await TextAsync(o.Admin, $"/api/reports/maintenance-costs?groupBy=asset&from=2026-01-01&to=2027-12-31", "ar");
        Assert.Contains(PumpAr, costs);
        Assert.Contains(Pump, await TextAsync(o.Admin, $"/api/reports/maintenance-costs?groupBy=asset&from=2026-01-01&to=2027-12-31", "en"));
        var history = await TextAsync(o.Admin, $"/api/reports/asset-work-history?assetId={asset}&from=2026-01-01&to=2027-12-31", "ar");
        Assert.Contains("تغيير الحشوة", history);
        Assert.Contains("Seal change", await TextAsync(o.Admin, $"/api/reports/asset-work-history?assetId={asset}&from=2026-01-01&to=2027-12-31", "en"));
    }

    [SkippableFact]
    public async Task Labels_print_names_in_the_printers_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(o.Admin, o.Site);
        var ar = await Ok(await SendAsync(o.Admin, HttpMethod.Post, "/api/labels", "ar", new { type = "asset", ids = new[] { asset } }));
        Assert.Contains(PumpAr, ar.GetRawText());
        var en = await Ok(await SendAsync(o.Admin, HttpMethod.Post, "/api/labels", "en", new { type = "asset", ids = new[] { asset } }));
        Assert.Contains(Pump, en.GetRawText());
        Assert.DoesNotContain(PumpAr, en.GetRawText());
    }

    [SkippableFact]
    public async Task Ai_prompts_keep_main_names()
    {
        Skip.IfNot(fx.Enabled);
        fx.Ai.Reset();
        var o = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => s["aiEnabled"] = true));
        var asset = await AssetAsync(o.Admin, o.Site);
        var symptom = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/symptoms", new { code = "LK", name = "Leak", nameAr = "تسريب", isActive = true })));
        for (var i = 0; i < 3; i++) await DoneAsync(o, asset, symptom); // the diagnosis needs some finished work on the asset
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Pump leaking", titleAr = "تسريب المضخة", siteId = o.Site, assetId = asset, type = "Corrective", priority = "High",
        })));
        await SendAsync(o.Admin, HttpMethod.Post, $"/api/work-orders/{wo}/diagnose", "ar", new { });
        var ask = Assert.Single(fx.Ai.Asks);
        Assert.Contains(Pump, ask.Prompt + ask.SharedContext);
        Assert.DoesNotContain(PumpAr, ask.Prompt + ask.SharedContext);
    }
}
