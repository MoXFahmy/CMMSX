using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The AI diagnosis (0.15.0): the server picks the asset's related history, Claude answers once, and only what was sent is kept.</summary>
public class AiDiagnosisTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Plant(Loop.Org Org, Guid Category, Guid A, Guid B, Guid C, Guid D, Guid Wo, string TechEmail);

    private async Task<Guid> AssetAsync(Loop.Org org, string name, Guid? category, string? maker, string? model, Guid? site = null) =>
        Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new
        {
            name, siteId = site ?? org.Site, categoryId = category, manufacturer = maker, model, status = "Active", criticality = "High", warrantyType = "None",
        })));

    /// <summary>Pumps A and B (same category, maker and model), C (same category, other maker), D (other category); an open job on A with a technician.</summary>
    private async Task<Plant> PlantAsync()
    {
        fx.Ai.Reset();
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true));
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var k = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = $"K{tag}", name = "Pumps", isActive = true })));
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = $"F{tag}", name = "Fans", isActive = true })));
        var a = await AssetAsync(org, "Pump A", k, "Grundfos", "CR5");
        var b = await AssetAsync(org, "Pump B", k, "Grundfos", "CR5");
        var c = await AssetAsync(org, "Pump C", k, "KSB", "Etanorm");
        var d = await AssetAsync(org, "Fan D", other, "Grundfos", "CR5");
        var tech = await EmployeeAsync(org.Admin, $"T{tag}");
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Pump A leaking", description = "Water under the seal", type = "Corrective", priority = "High", siteId = org.Site, assetId = a, assigneeIds = new[] { tech.Id },
        })));
        return new Plant(org, k, a, b, c, d, wo, tech.Email);
    }

    /// <summary>A completed work order written straight to the database; returns its number.</summary>
    private Task<(Guid Id, string Number)> DoneAsync(Loop.Org org, Guid asset, DateTimeOffset completed, Guid? site = null, string notes = "Replaced the seal",
        WorkOrderStatus status = WorkOrderStatus.Completed, (Guid Part, decimal Qty)? issued = null) => DbAsync(fx, async db =>
    {
        var w = new WorkOrder
        {
            TenantId = org.Tenant, Number = $"H-{Guid.NewGuid():N}"[..14].ToUpperInvariant(), Title = $"Fix {notes}", SiteId = site ?? org.Site, AssetId = asset,
            Type = WorkType.Corrective, Status = status, CompletedAt = status == WorkOrderStatus.Completed ? completed : null, CompletionNotes = notes, CauseNotes = "Wear",
        };
        db.WorkOrders.Add(w);
        if (issued is { } i)
        {
            var store = await db.Stores.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == org.Tenant)
                        ?? db.Stores.Add(new Store { TenantId = org.Tenant, Code = "MAIN", Name = "Main", SiteId = org.Site }).Entity;
            db.StockTransactions.Add(new StockTransaction
            {
                TenantId = org.Tenant, Type = StockTransactionType.Issue, PartId = i.Part, StoreId = store.Id, Quantity = -i.Qty, UnitCost = 5, Date = DateOnly.FromDateTime(completed.UtcDateTime), WorkOrderId = w.Id,
            });
        }
        await db.SaveChangesAsync();
        return (w.Id, w.Number);
    });

    private static DateTimeOffset Day(int d) => new(2026, 3, d, 10, 0, 0, TimeSpan.Zero);

    private static object Diagnosis(params object[] actions) => new
    {
        context = "Pump A, Grundfos CR5", observed = "Leak at the seal", probableCause = "Worn mechanical seal", confidence = "high",
        actions, historyTooThin = false, message = (string?)null,
    };

    private static object Action(string title, string[] sources, params (string No, decimal Qty)[] parts) =>
        new { title, why = "Seen before", parts = parts.Select(p => new { partNumber = p.No, quantity = p.Qty }).ToArray(), hours = 2m, sources };

    private void Answer(object json) => fx.Ai.Responder = _ => Task.FromResult(FakeAiClient.Answer(JsonSerializer.Serialize(json)));

    private static Task<HttpResponseMessage> DiagnoseAsync(HttpClient c, Guid wo) => c.PostAsync($"/api/work-orders/{wo}/diagnose", null);

    [SkippableFact]
    public async Task Diagnosis_sends_related_history_newest_first_and_caps_at_40()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        var a1 = await DoneAsync(p.Org, p.A, Day(1));
        var a2 = await DoneAsync(p.Org, p.A, Day(5));
        var b1 = await DoneAsync(p.Org, p.B, Day(10));
        var c1 = await DoneAsync(p.Org, p.C, Day(20));
        var d1 = await DoneAsync(p.Org, p.D, Day(21));
        var open = await DoneAsync(p.Org, p.A, Day(22), status: WorkOrderStatus.InProgress);
        Answer(Diagnosis(Action("Replace the seal", [a2.Number])));
        var r = await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo));
        Assert.Equal(4, r.GetProperty("sentCount").GetInt32());
        var ask = Assert.Single(fx.Ai.Asks);
        Assert.Equal(FourC.Cmms.Api.Features.Ai.AiEffort.Medium, ask.Effort);
        var order = new[] { a2.Number, a1.Number, b1.Number, c1.Number }.Select(n => ask.Prompt.IndexOf(n, StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.DoesNotContain(d1.Number, ask.Prompt);
        Assert.DoesNotContain(open.Number, ask.Prompt);
        Assert.Contains("Grundfos", ask.Prompt);
        Assert.Contains("Water under the seal", ask.Prompt);

        for (var i = 0; i < 40; i++) await DoneAsync(p.Org, p.C, Day(1).AddHours(i));
        Assert.Equal(40, (await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo))).GetProperty("sentCount").GetInt32());
    }

    [SkippableFact]
    public async Task Diagnosis_excludes_other_sites_and_people()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        var s2 = Id(await Ok(await p.Org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var e = await AssetAsync(p.Org, "Pump E", p.Category, "Grundfos", "CR5", s2);
        var far = await DoneAsync(p.Org, e, Day(25), site: s2);
        for (var i = 1; i <= 3; i++) await DoneAsync(p.Org, p.A, Day(i));
        var local = await UserAsync(fx, p.Org.Admin, "Maintenance Manager", [p.Org.Site], name: "Laila Local");
        Answer(Diagnosis(Action("Replace the seal", [])));
        await Ok(await DiagnoseAsync(local.Client, p.Wo));
        var ask = Assert.Single(fx.Ai.Asks);
        Assert.DoesNotContain(far.Number, ask.Prompt);
        Assert.DoesNotContain("Tech T", ask.Prompt);
        Assert.DoesNotContain(p.TechEmail, ask.Prompt);
        Assert.DoesNotContain("Laila", ask.Prompt);
        Assert.DoesNotContain(p.Org.AdminEmail, ask.Prompt);
    }

    [SkippableFact]
    public async Task Diagnosis_keeps_only_sources_and_parts_that_were_sent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var seal = await AiReportTests.PartAsync(p.Org.Admin, $"SEAL-{tag}", "Mechanical seal");
        await AiReportTests.PartAsync(p.Org.Admin, $"NEVER-{tag}", "Unused part");
        var h1 = await DoneAsync(p.Org, p.A, Day(1), issued: (seal, 1));
        var h2 = await DoneAsync(p.Org, p.A, Day(2));
        await DoneAsync(p.Org, p.B, Day(3));
        Assert.Contains($"SEAL-{tag}", "" + (await DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(x => x.Id == seal).Select(x => x.PartNumber).FirstAsync())));
        Answer(Diagnosis(
            Action("Replace the seal", [h1.Number, "WO-NOPE", h2.Number], ($"SEAL-{tag}", 1), ($"NEVER-{tag}", 2), ($"SEAL-{tag}", 0)),
            Action("Two", []), Action("Three", []), Action("Four", []), Action("Five", []), Action("Six", [])));
        var r = await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo));
        Assert.Contains($"SEAL-{tag}", fx.Ai.Asks[0].Prompt);
        var actions = r.GetProperty("actions").EnumerateArray().ToList();
        Assert.Equal(5, actions.Count);
        var first = actions[0];
        Assert.Equal([h1.Id, h2.Id], first.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(h1.Number, first.GetProperty("sources")[0].GetProperty("number").GetString());
        var part = Assert.Single(first.GetProperty("parts").EnumerateArray());
        Assert.Equal((seal, "Mechanical seal", 1m), (part.GetProperty("partId").GetGuid(), part.GetProperty("name").GetString(), part.GetProperty("quantity").GetDecimal()));
        Assert.Equal("high", r.GetProperty("confidence").GetString());
        Assert.Equal("Worn mechanical seal", r.GetProperty("probableCause").GetString());
    }

    [SkippableFact]
    public async Task Thin_history_answers_without_calling()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        await DoneAsync(p.Org, p.A, Day(1));
        await DoneAsync(p.Org, p.C, Day(2));
        var r = await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo));
        Assert.True(r.GetProperty("historyTooThin").GetBoolean());
        Assert.Contains("only 2 completed work orders", r.GetProperty("message").GetString());
        Assert.Empty(r.GetProperty("actions").EnumerateArray());
        Assert.Empty(fx.Ai.Asks);
        Assert.Equal(0, (await AiSettingsTests.StatusAsync(p.Org.Admin)).GetProperty("used").GetInt32());
        Assert.Empty((await Ok(await p.Org.Admin.GetAsync($"/api/work-orders/{p.Wo}/diagnoses"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task Diagnosis_is_stored_and_listed_with_the_request_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        for (var i = 1; i <= 3; i++) await DoneAsync(p.Org, p.A, Day(i));
        var request = Id(await Ok(await p.Org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = p.Org.Site, assetId = p.A, priority = "High", type = "Corrective" })));
        Answer(Diagnosis(Action("From the request", [])));
        var fromRequest = await Ok(await p.Org.Admin.PostAsync($"/api/work-requests/{request}/diagnose", null));
        Assert.Equal(request, fromRequest.GetProperty("workRequestId").GetGuid());
        await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.Id == p.Wo).ExecuteUpdateAsync(u => u.SetProperty(w => w.WorkRequestId, request)));
        await Task.Delay(10);
        Answer(Diagnosis(Action("From the work order", [])));
        await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo));
        var list = (await Ok(await p.Org.Admin.GetAsync($"/api/work-orders/{p.Wo}/diagnoses"))).EnumerateArray().ToList();
        Assert.Equal(["From the work order", "From the request"], list.Select(d => d.GetProperty("actions")[0].GetProperty("title").GetString()));
        Assert.All(list, d => Assert.Equal("Admin", d.GetProperty("createdByName").GetString()));
        Assert.Single((await Ok(await p.Org.Admin.GetAsync($"/api/work-requests/{request}/diagnoses"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task Votes_are_one_per_person_and_changeable()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        for (var i = 1; i <= 3; i++) await DoneAsync(p.Org, p.A, Day(i));
        Answer(Diagnosis(Action("Replace the seal", [])));
        var id = Id(await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo)));
        var other = await UserAsync(fx, p.Org.Admin, "Maintenance Manager");
        async Task<(int, int, bool?)> Vote(HttpClient c, bool? up)
        {
            var v = await Ok(await c.PutAsJsonAsync($"/api/ai/diagnoses/{id}/vote", new { up }));
            return (v.GetProperty("up").GetInt32(), v.GetProperty("down").GetInt32(), v.GetProperty("myVote").ValueKind == JsonValueKind.Null ? null : v.GetProperty("myVote").GetBoolean());
        }
        Assert.Equal((1, 0, (bool?)true), await Vote(p.Org.Admin, true));
        Assert.Equal((1, 1, (bool?)false), await Vote(other.Client, false));
        Assert.Equal((0, 2, (bool?)false), await Vote(p.Org.Admin, false));
        Assert.Equal((0, 1, (bool?)null), await Vote(p.Org.Admin, null));
        var listed = Assert.Single((await Ok(await other.Client.GetAsync($"/api/work-orders/{p.Wo}/diagnoses"))).EnumerateArray());
        Assert.Equal((0, 1, false), (listed.GetProperty("up").GetInt32(), listed.GetProperty("down").GetInt32(), listed.GetProperty("myVote").GetBoolean()));
    }

    [SkippableFact]
    public async Task Diagnose_needs_an_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        var bare = Id(await Ok(await p.Org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "No asset", type = "Corrective", priority = "Low", siteId = p.Org.Site })));
        Assert.Contains("Choose an asset first: a diagnosis works from the asset's history.", await Bad(await DiagnoseAsync(p.Org.Admin, bare)));
        Assert.Empty(fx.Ai.Asks);
    }

    [SkippableFact]
    public async Task Listed_sources_hide_what_the_viewer_cannot_see()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var p = await PlantAsync();
        var s2 = Id(await Ok(await p.Org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var e = await AssetAsync(p.Org, "Pump E", p.Category, "Grundfos", "CR5", s2);
        var far = await DoneAsync(p.Org, e, Day(25), site: s2);
        var near = await DoneAsync(p.Org, p.A, Day(1));
        await DoneAsync(p.Org, p.A, Day(2));
        Answer(Diagnosis(Action("Replace the seal", [far.Number, near.Number])));
        var mine = await Ok(await DiagnoseAsync(p.Org.Admin, p.Wo));
        Assert.Equal(2, mine.GetProperty("actions")[0].GetProperty("sources").GetArrayLength());
        var local = await UserAsync(fx, p.Org.Admin, "Maintenance Manager", [p.Org.Site]);
        var seen = Assert.Single((await Ok(await local.Client.GetAsync($"/api/work-orders/{p.Wo}/diagnoses"))).EnumerateArray());
        Assert.Equal([near.Id], seen.GetProperty("actions")[0].GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
    }
}
