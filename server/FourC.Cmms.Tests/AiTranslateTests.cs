using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Features.Ai;
using FourC.Cmms.Core.Ai;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: "Fill in missing Arabic" — AI drafts of the missing Arabic texts, checked and saved by a person.</summary>
public class AiTranslateTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The fake answers every id it was sent with <paramref name="translate"/>(text) (default: "ع " + text).</summary>
    private void Echo(Func<string, string?>? translate = null) => fx.Ai.Responder = ask =>
    {
        var items = JsonNode.Parse(ask.Prompt)!.AsArray();
        var answer = new JsonArray(items.Select(i => (JsonNode)new JsonObject
        {
            ["id"] = i!["id"]!.GetValue<string>(), ["ar"] = (translate ?? (t => "ع " + t))(i["text"]!.GetValue<string>()),
        }).ToArray());
        return Task.FromResult(FakeAiClient.Answer(new JsonObject { ["items"] = answer }.ToJsonString()));
    };

    private async Task<Loop.Org> AiOrgAsync()
    {
        fx.Ai.Reset();
        var o = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => s["aiEnabled"] = true));
        return o;
    }

    private static Task<HttpResponseMessage> DraftAsync(HttpClient c, string list) => c.PostAsJsonAsync("/api/translations/draft", new { list });

    private static async Task<JsonElement> SaveAsync(HttpClient c, string list, string field, params (Guid Id, string Ar)[] items) =>
        await Ok(await c.PostAsJsonAsync("/api/translations/save", new { list, field, items = items.Select(i => new { id = i.Id, ar = i.Ar }) }));

    private static List<string> SentIds(AiAsk ask) => JsonNode.Parse(ask.Prompt)!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToList();

    [SkippableFact]
    public async Task Draft_sends_only_texts_missing_arabic()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        var a = await AssetAsync(o.Admin, o.Site, name: "Pump A", nameAr: null);
        var b = await AssetAsync(o.Admin, o.Site, name: "Pump B", nameAr: null);
        await AssetAsync(o.Admin, o.Site, name: "Pump C", nameAr: "المضخة ج");
        Echo();
        var r = await Ok(await DraftAsync(o.Admin, "assets"));
        Assert.Equal("name", S(r, "field"));
        var items = r.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == a && S(i, "text") == "Pump A" && S(i, "draft") == "ع Pump A");
        Assert.Equal(new[] { a, b }.Order(), SentIds(Assert.Single(fx.Ai.Asks)).Select(Guid.Parse).Order());
        Assert.Equal(0, r.GetProperty("remaining").GetInt32());
    }

    [SkippableFact]
    public async Task Draft_takes_at_most_the_batch()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        for (var i = 0; i < 55; i++) await AssetAsync(o.Admin, o.Site, name: $"Pump {i:00}", nameAr: null);
        Echo();
        var r = await Ok(await DraftAsync(o.Admin, "assets"));
        Assert.Equal(50, r.GetProperty("items").GetArrayLength());
        Assert.Equal(5, r.GetProperty("remaining").GetInt32());
    }

    [SkippableFact]
    public async Task Draft_moves_to_the_next_field_when_names_are_done()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        await AssetAsync(o.Admin, o.Site, notes: "Isolate first");
        await AssetAsync(o.Admin, o.Site, name: "Boiler 1", nameAr: "غلاية 1", notes: "Check the gauge");
        Echo();
        var r = await Ok(await DraftAsync(o.Admin, "assets"));
        Assert.Equal("notesToTechnician", S(r, "field"));
        Assert.Equal(2, r.GetProperty("items").GetArrayLength());
    }

    [SkippableFact]
    public async Task Template_steps_are_drafted_after_names_titles_and_safety_notes()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", extra: new()
        {
            ["name"] = "Chiller check", ["nameAr"] = "فحص المبرد", ["title"] = "Monthly check", ["titleAr"] = "فحص شهري", ["safetyNotes"] = "Lock out the pump",
            ["stepDetails"] = new[] { new { description = "Check coupling", type = "Check" }, new { description = "Check belt", type = "Check" } },
        });
        Echo();
        var first = await Ok(await DraftAsync(o.Admin, "templates"));
        Assert.Equal("safetyNotes", S(first, "field"));
        Assert.Equal(task, first.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        await SaveAsync(o.Admin, "templates", "safetyNotes", (task, "اقفل المضخة"));
        var second = await Ok(await DraftAsync(o.Admin, "templates"));
        Assert.Equal("steps", S(second, "field"));
        Assert.Equal(["Check coupling", "Check belt"], second.GetProperty("items").EnumerateArray().Select(i => S(i, "text")).ToArray());
        var steps = second.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("id").GetGuid(), S(i, "draft")!)).ToArray();
        Assert.Equal(2, (await SaveAsync(o.Admin, "templates", "steps", steps)).GetProperty("saved").GetInt32());
        var t = await Ok(await o.Admin.GetAsync($"/api/pm-tasks/{task}"));
        Assert.Equal("اقفل المضخة", S(t, "safetyNotesAr"));
        Assert.Equal("ع Check coupling", S(t.GetProperty("stepDetails")[0], "descriptionAr"));
    }

    [SkippableFact]
    public async Task Save_skips_texts_filled_meanwhile()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        var a = await AssetAsync(o.Admin, o.Site, name: "Pump A", nameAr: null);
        var b = await AssetAsync(o.Admin, o.Site, name: "Pump B", nameAr: null);
        Echo();
        await Ok(await DraftAsync(o.Admin, "assets"));
        var detail = await Ok(await o.Admin.GetAsync($"/api/assets/{a}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/assets/{a}", new
        {
            assetNumber = S(detail, "assetNumber"), name = "Pump A", nameAr = "المضخة أ", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None",
            rowVersion = S(detail, "rowVersion"),
        }));
        var r = await SaveAsync(o.Admin, "assets", "name", (a, "ع Pump A"), (b, "ع Pump B"));
        Assert.Equal((1, 1), (r.GetProperty("saved").GetInt32(), r.GetProperty("skipped").GetInt32()));
        Assert.Equal("المضخة أ", S(await Ok(await o.Admin.GetAsync($"/api/assets/{a}")), "nameAr"));
        Assert.Equal("ع Pump B", S(await Ok(await o.Admin.GetAsync($"/api/assets/{b}")), "nameAr"));
    }

    [SkippableFact]
    public async Task Bad_answers_draft_only_valid_items()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        var a = await AssetAsync(o.Admin, o.Site, name: "Pump A", nameAr: null);
        var b = await AssetAsync(o.Admin, o.Site, name: "Pump B", nameAr: null);
        var c = await AssetAsync(o.Admin, o.Site, name: "Pump C", nameAr: null);
        fx.Ai.Responder = _ => Task.FromResult(FakeAiClient.Answer(new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["id"] = a.ToString(), ["ar"] = "المضخة أ" },
                new JsonObject { ["id"] = b.ToString(), ["ar"] = new string('م', 301) },
                new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["ar"] = "غريب" }),
        }.ToJsonString()));
        var r = await Ok(await DraftAsync(o.Admin, "assets"));
        Assert.Equal(a, r.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(2, r.GetProperty("couldNot").GetInt32()); // b too long, c missing; the unknown id is ignored
        Assert.DoesNotContain(r.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == c);
    }

    [SkippableFact]
    public async Task Drafting_counts_one_ai_use_logged_as_translation()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        await AssetAsync(o.Admin, o.Site, nameAr: null);
        Echo();
        await Ok(await DraftAsync(o.Admin, "assets"));
        var calls = await DbAsync(fx, db => db.AiCalls.IgnoreQueryFilters().Where(x => x.TenantId == o.Tenant).Select(x => x.Feature).ToListAsync());
        Assert.Equal(AiFeature.Translation, Assert.Single(calls));
    }

    [SkippableFact]
    public async Task Drafting_is_refused_when_ai_is_off_or_used_up()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        await AssetAsync(o.Admin, o.Site, nameAr: null);
        Echo();
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => s["aiMonthlyLimit"] = 0));
        var usedUp = await DraftAsync(o.Admin, "assets");
        Assert.Equal(HttpStatusCode.TooManyRequests, usedUp.StatusCode);
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => { s["aiEnabled"] = false; s["aiMonthlyLimit"] = 50; }));
        var off = await DraftAsync(o.Admin, "assets");
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Contains("The AI assistant is off for this organization.", await off.Content.ReadAsStringAsync());
        Assert.Empty(fx.Ai.Asks);
    }

    [SkippableFact]
    public async Task Drafting_and_saving_need_the_lists_edit_permission()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        var a = await AssetAsync(o.Admin, o.Site, nameAr: null);
        var tech = await UserAsync(fx, o.Admin, "Technician");
        Echo();
        Assert.Equal(HttpStatusCode.Forbidden, (await DraftAsync(tech.Client, "assets")).StatusCode);
        var save = await tech.Client.PostAsJsonAsync("/api/translations/save", new { list = "assets", field = "name", items = new[] { new { id = a, ar = "مضخة" } } });
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DraftAsync(o.Admin, "suppliers")).StatusCode);
    }

    [SkippableFact]
    public async Task Shared_context_carries_existing_pairs()
    {
        Skip.IfNot(fx.Enabled);
        var o = await AiOrgAsync();
        await AssetAsync(o.Admin, o.Site, name: "Pump", nameAr: "مضخة");
        await AssetAsync(o.Admin, o.Site, name: "Pump 2", nameAr: null);
        Echo();
        await Ok(await DraftAsync(o.Admin, "assets"));
        Assert.Contains("Pump ⇒ مضخة", Assert.Single(fx.Ai.Asks).SharedContext);
        Assert.Equal(AiFeature.Translation, fx.Ai.Asks[0].Feature);
    }
}
