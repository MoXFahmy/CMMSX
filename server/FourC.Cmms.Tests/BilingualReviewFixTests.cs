using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Features.Ai;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: the fixes from the final review.</summary>
public class BilingualReviewFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Part = "Bearing 6204", PartAr = "رولمان 6204";

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private EmailSenderService Mail => fx.Factory!.Services.GetRequiredService<EmailSenderService>();

    private async Task<(Guid Employee, string Email)> TechAsync(Loop.Org o, string language)
    {
        var user = await UserAsync(fx, o.Admin, "Technician");
        await Ok(await user.Client.PutAsJsonAsync("/api/auth/me/language", new { language }));
        var emp = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = $"T{Guid.NewGuid():N}"[..8], fullName = "Tech", userId = user.Id, isContractor = false })));
        return (emp, user.Email);
    }

    // Critical 1: purchasing screens and flows keep the part's main name.
    [SkippableFact]
    public async Task Purchasing_screens_and_planned_parts_give_the_main_part_name()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var store = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/stores", new { code = "ST1", name = "Main store", siteId = o.Site, isActive = true })));
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "B6204", name = Part, nameAr = PartAr, unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel = 5, maxLevel = 20, reorderQuantity = 10 } },
        })));
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = part, storeId = store, quantity = 3 }));

        var proposal = (await GetAsync(o.Admin, "/api/inventory/proposal", "ar")).EnumerateArray().Single(r => r.GetProperty("partId").GetGuid() == part);
        Assert.Equal(Part, S(proposal, "partName"));
        var shortage = (await GetAsync(o.Admin, $"/api/inventory/shortages?storeId={store}", "ar")).EnumerateArray().Single(r => r.GetProperty("partId").GetGuid() == part);
        Assert.Equal(Part, S(shortage, "partName"));
        var line = (await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar")).GetProperty("plannedParts").EnumerateArray().Single(l => l.GetProperty("partId").GetGuid() == part);
        Assert.Equal(PartAr, S(line, "partName")); // shown in the reader's language…
        Assert.Equal(Part, S(line, "mainPartName")); // …and the main name for raising a purchase request
    }

    // Important 2: the phone's codes follow its language.
    [SkippableFact]
    public async Task Phone_codes_follow_the_phones_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        await EmployeeAsync(o.Admin, "ME", userId: o.AdminId);
        await Ok(await o.Admin.PostAsJsonAsync("/api/symptoms", new { code = "LK", name = "Leak", nameAr = "تسريب", isActive = true }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/failure-codes", new { code = "WR", name = "Wear", nameAr = "تآكل", isActive = true }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/remedies", new { code = "SL", name = "Seal", nameAr = "حشوة", isActive = true }));
        var ar = await GetAsync(o.Admin, "/api/tech/bundle", "ar");
        Assert.Contains(ar.GetProperty("symptoms").EnumerateArray(), x => S(x, "name") == "تسريب");
        Assert.Contains(ar.GetProperty("failureCodes").EnumerateArray(), x => S(x, "name") == "تآكل");
        Assert.Contains(ar.GetProperty("remedies").EnumerateArray(), x => S(x, "name") == "حشوة");
        Assert.Contains((await GetAsync(o.Admin, "/api/tech/bundle", "en")).GetProperty("symptoms").EnumerateArray(), x => S(x, "name") == "Leak");
    }

    // Important 3: the location's notes to technician follow the reader on the work order (page and job card).
    [SkippableFact]
    public async Task Work_order_location_notes_follow_the_reader()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var room = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new
        {
            siteId = o.Site, code = "PR", name = "Plant room", type = "Room", criticality = "Medium", isActive = true,
            notesToTechnician = "Ear protection", notesToTechnicianAr = "واقي الأذن",
        })));
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Job", siteId = o.Site, locationId = room, type = "Corrective", priority = "Medium" })));
        Assert.Equal("واقي الأذن", S(await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar"), "locationNotesToTechnician"));
        Assert.Equal("Ear protection", S(await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en"), "locationNotesToTechnician"));
    }

    // Important 4: safety notes reach the assignment e-mail, in each recipient's language.
    [SkippableFact]
    public async Task Assignment_email_carries_the_safety_notes_in_each_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var arabic = await TechAsync(o, "ar");
        var english = await TechAsync(o, "en");
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", extra: new() { ["safetyNotes"] = "Lock out the pump", ["safetyNotesAr"] = "اقفل المضخة" });
        await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check", siteId = o.Site, type = "Preventive", priority = "Medium", templateId = task, assigneeIds = new[] { arabic.Employee, english.Employee },
        }));
        await Mail.RunOnceAsync();
        Assert.Contains("اقفل المضخة", Assert.Single(fx.Emails.To(arabic.Email)).Html);
        Assert.Contains("Lock out the pump", Assert.Single(fx.Emails.To(english.Email)).Html);
    }

    // Important 5: duplicating a work order keeps each step's Arabic text (the page sends the steps as texts).
    [SkippableFact]
    public async Task Duplicate_keeps_the_steps_arabic_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var source = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix pump", siteId = o.Site, type = "Corrective", priority = "Medium" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{source}/tasks", new { description = "Check seal", descriptionAr = "افحص الحشوة" }));
        var copy = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix pump", siteId = o.Site, type = "Corrective", priority = "Medium", copyPlanFromId = source, tasks = new[] { "Check seal", "Tighten bolts" },
        })));
        var steps = (await GetAsync(o.Admin, $"/api/work-orders/{copy}", "en")).GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal("افحص الحشوة", S(steps.Single(s => S(s, "description") == "Check seal"), "descriptionAr"));
        Assert.Null(S(steps.Single(s => S(s, "description") == "Tighten bolts"), "descriptionAr"));
    }

    // Important 6: the current-data download fills the PM steps' Arabic column.
    [SkippableFact]
    public async Task Current_data_download_has_the_steps_arabic_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        await Pm12.TemplateAsync(o.Admin, "CHK", extra: new() { ["stepDetails"] = new[] { new { description = "Check coupling", descriptionAr = "افحص الوصلة", type = "Check" } } });
        var bytes = await (await o.Admin.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var sheet = SpreadsheetReader.Read(new MemoryStream(bytes), "current.xlsx").Sheets.Single(x => x.Name == "PM steps");
        var heads = sheet.Rows[0].Select(x => x?.ToString()).ToList();
        var col = heads.IndexOf("Description (Arabic)");
        Assert.True(col >= 0, string.Join(" | ", heads));
        Assert.Equal("افحص الوصلة", sheet.Rows.Skip(1).Single(r => r[0]?.ToString() == "CHK")[col]?.ToString());
    }

    // Important 7: AI prompts use short ids (fewer answer tokens), mapped back to the records.
    [SkippableFact]
    public async Task Ai_prompt_uses_short_ids_mapped_back()
    {
        Skip.IfNot(fx.Enabled);
        fx.Ai.Reset();
        var o = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => s["aiEnabled"] = true));
        var a = await AssetAsync(o.Admin, o.Site, name: "Pump A", nameAr: null);
        var b = await AssetAsync(o.Admin, o.Site, name: "Pump B", nameAr: null);
        fx.Ai.Responder = ask =>
        {
            var items = JsonNode.Parse(ask.Prompt)!.AsArray();
            var answer = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["id"] = i!["id"]!.GetValue<string>(), ["ar"] = "ع " + i["text"]!.GetValue<string>() }).ToArray());
            return Task.FromResult(FakeAiClient.Answer(new JsonObject { ["items"] = answer }.ToJsonString()));
        };
        var r = await Ok(await o.Admin.PostAsJsonAsync("/api/translations/draft", new { list = "assets" }));
        var sent = JsonNode.Parse(Assert.Single(fx.Ai.Asks).Prompt)!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(["1", "2"], sent);
        var items = r.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("ع Pump A", S(items.Single(i => i.GetProperty("id").GetGuid() == a), "draft"));
        Assert.Equal("ع Pump B", S(items.Single(i => i.GetProperty("id").GetGuid() == b), "draft"));
    }

    // Important 8: a step failed by someone reading in Arabic still gives its follow-up a main title in the organization's language.
    [SkippableFact]
    public async Task Follow_up_from_an_arabic_answer_keeps_the_main_title_in_the_organizations_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var follow = await Pm12.TemplateAsync(o.Admin, "FIX", extra: new() { ["title"] = "Fix coupling", ["titleAr"] = "إصلاح الوصلة" });
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Check", siteId = o.Site, type = "Preventive", priority = "Medium" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/tasks", new { description = "Check coupling" }));
        var step = (await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en")).GetProperty("tasks").EnumerateArray().Single();
        await TestSettings.SetSettingsAsync(o.Admin, s => { s["followUpDefault"] = "WorkOrder"; s["followUpTemplateId"] = follow.ToString(); });
        var answer = await Ok(await SendAsync(o.Admin, HttpMethod.Put, $"/api/work-orders/{wo}/tasks/{step.GetProperty("id").GetGuid()}", "ar", new { answer = "NotOk" }));
        var number = answer.GetProperty("followUp").GetProperty("number").GetString();
        var row = (await GetAsync(o.Admin, "/api/work-orders?search=" + number, "en")).GetProperty("items").EnumerateArray().Single();
        var f = await GetAsync(o.Admin, $"/api/work-orders/{row.GetProperty("id").GetGuid()}", "en");
        Assert.StartsWith("Fix coupling — from step 1 of", S(f, "title"));
        Assert.Contains("إصلاح الوصلة — من الخطوة 1", S(f, "titleAr"));
    }

    // Re-graded from minor (the release rule "Arabic for every new text"): the new server labels and messages have Arabic wording.
    [Fact]
    public void New_labels_and_messages_have_arabic_wording()
    {
        foreach (var english in new[] { "Name (Arabic)", "Title (Arabic)", "Safety notes (Arabic)", "Description (Arabic)", "Notes to technician (Arabic)",
                     AiTranslate.UnknownList, AiTranslate.UnknownField, "Name (Arabic) must be at most 300 characters." })
            Assert.NotEqual(english, Translator.T(english, "ar"));
    }
}
