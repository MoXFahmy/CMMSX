using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: Arabic texts are saved beside the main texts and come back raw in the data edit forms use, whatever the reader's language.</summary>
public class BilingualEditTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal const string Pump = "Chiller pump 2", PumpAr = "مضخة التبريد 2";

    internal static Task<HttpResponseMessage> SendAsync(HttpClient c, HttpMethod method, string url, string language, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.AcceptLanguage.ParseAdd(language);
        if (body is not null) r.Content = JsonContent.Create(body);
        return c.SendAsync(r);
    }

    internal static async Task<JsonElement> GetAsync(HttpClient c, string url, string language) => await Ok(await SendAsync(c, HttpMethod.Get, url, language));

    internal static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name = Pump, string? nameAr = PumpAr, Guid? location = null, Guid? category = null,
        string? notes = null, string? notesAr = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new
        {
            name, nameAr, siteId = site, locationId = location, categoryId = category, status = "Active", criticality = "High", warrantyType = "None",
            notesToTechnician = notes, notesToTechnicianAr = notesAr,
        })));

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    [SkippableFact]
    public async Task Asset_keeps_both_texts_raw_in_its_edit_data()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var id = await AssetAsync(o.Admin, o.Site, notes: "Isolate first", notesAr: "افصل أولاً");
        foreach (var lang in new[] { "ar", "en" })
        {
            var a = await GetAsync(o.Admin, $"/api/assets/{id}", lang);
            Assert.Equal(Pump, S(a, "name"));
            Assert.Equal(PumpAr, S(a, "nameAr"));
            Assert.Equal("Isolate first", S(a, "notesToTechnician"));
            Assert.Equal("افصل أولاً", S(a, "notesToTechnicianAr"));
        }
    }

    [SkippableFact]
    public async Task Saving_the_edit_data_under_arabic_keeps_the_main_text()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var id = await AssetAsync(o.Admin, o.Site);
        var a = await GetAsync(o.Admin, $"/api/assets/{id}", "ar");
        var body = new
        {
            assetNumber = S(a, "assetNumber"), name = S(a, "name"), nameAr = S(a, "nameAr"), siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None",
            rowVersion = S(a, "rowVersion"),
        };
        await Ok(await SendAsync(o.Admin, HttpMethod.Put, $"/api/assets/{id}", "ar", body));
        var after = await GetAsync(o.Admin, $"/api/assets/{id}", "en");
        Assert.Equal(Pump, S(after, "name"));
        Assert.Equal(PumpAr, S(after, "nameAr"));
    }

    [SkippableFact]
    public async Task Location_and_part_keep_both_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var loc = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new
        {
            siteId = o.Site, code = "PR", name = "Plant room", nameAr = "غرفة المعدات", type = "Room", criticality = "Medium", isActive = true,
            notesToTechnician = "Ear protection", notesToTechnicianAr = "واقي الأذن",
        })));
        var l = await GetAsync(o.Admin, $"/api/locations/{loc}", "ar");
        Assert.Equal("Plant room", S(l, "name"));
        Assert.Equal("غرفة المعدات", S(l, "nameAr"));
        Assert.Equal("واقي الأذن", S(l, "notesToTechnicianAr"));
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "B6204", name = "Bearing 6204", nameAr = "رولمان 6204", unitOfMeasure = "pcs", standardPrice = 10, isCritical = false, isShelfItem = false,
            allowNegativeStock = false, isActive = true,
        })));
        var p = await GetAsync(o.Admin, $"/api/parts/{part}", "ar");
        Assert.Equal("Bearing 6204", S(p, "name"));
        Assert.Equal("رولمان 6204", S(p, "nameAr"));
    }

    [SkippableFact]
    public async Task Settings_lists_keep_both_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await Ok(await o.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "PMP", name = "Pumps", nameAr = "مضخات", isActive = true }));
        foreach (var path in new[] { "symptoms", "failure-codes", "remedies", "cancel-reasons" })
            await Ok(await o.Admin.PostAsJsonAsync($"/api/{path}", new { code = "BI", name = $"Name {path}", nameAr = $"اسم {path}", isActive = true }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/request-types", new { code = "BI", name = "Leak report", nameAr = "بلاغ تسريب", isActive = true, autoApprove = false }));

        var cats = await GetAsync(o.Admin, "/api/asset-categories", "ar");
        var cat = cats.EnumerateArray().Single(c => S(c, "code") == "PMP");
        Assert.Equal("Pumps", S(cat, "name"));
        Assert.Equal("مضخات", S(cat, "nameAr"));
        foreach (var path in new[] { "symptoms", "failure-codes", "remedies", "cancel-reasons" })
        {
            var row = (await GetAsync(o.Admin, $"/api/{path}", "ar")).EnumerateArray().Single(c => S(c, "code") == "BI");
            Assert.Equal($"Name {path}", S(row, "name"));
            Assert.Equal($"اسم {path}", S(row, "nameAr"));
        }
        var type = (await GetAsync(o.Admin, "/api/request-types", "ar")).EnumerateArray().Single(c => S(c, "code") == "BI");
        Assert.Equal("Leak report", S(type, "name"));
        Assert.Equal("بلاغ تسريب", S(type, "nameAr"));
        await Ok(await o.Admin.PostAsJsonAsync("/api/trades", new { code = "BI", name = "Fitter", isActive = true }));
        var trade = (await GetAsync(o.Admin, "/api/trades", "ar")).EnumerateArray().Single(c => S(c, "code") == "BI");
        Assert.False(trade.TryGetProperty("nameAr", out _));
    }

    [SkippableFact]
    public async Task Template_steps_and_schedule_keep_both_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", extra: new()
        {
            ["name"] = "Chiller check", ["nameAr"] = "فحص المبرد", ["title"] = "Monthly chiller check", ["titleAr"] = "فحص المبرد الشهري",
            ["safetyNotes"] = "Lock out the pump", ["safetyNotesAr"] = "اقفل المضخة",
            ["stepDetails"] = new[] { new { description = "Check coupling", descriptionAr = "افحص الوصلة", type = "Check" } },
        });
        var t = await GetAsync(o.Admin, $"/api/pm-tasks/{task}", "ar");
        Assert.Equal("Chiller check", S(t, "name"));
        Assert.Equal("فحص المبرد", S(t, "nameAr"));
        Assert.Equal("Monthly chiller check", S(t, "title"));
        Assert.Equal("فحص المبرد الشهري", S(t, "titleAr"));
        Assert.Equal("Lock out the pump", S(t, "safetyNotes"));
        Assert.Equal("اقفل المضخة", S(t, "safetyNotesAr"));
        var step = t.GetProperty("stepDetails").EnumerateArray().Single();
        Assert.Equal("Check coupling", S(step, "description"));
        Assert.Equal("افحص الوصلة", S(step, "descriptionAr"));

        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, await AssetAsync(o.Admin, o.Site), extra: new() { ["name"] = "Monthly chiller PM", ["nameAr"] = "صيانة المبرد الشهرية" })));
        var s = await GetAsync(o.Admin, $"/api/pm-schedules/{pm}", "ar");
        Assert.Equal("Monthly chiller PM", S(s, "name"));
        Assert.Equal("صيانة المبرد الشهرية", S(s, "nameAr"));
    }

    [SkippableFact]
    public async Task Work_order_keeps_title_safety_and_steps_in_both_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix pump", titleAr = "إصلاح المضخة", siteId = o.Site, type = "Corrective", priority = "Medium",
            safetyNotes = "Lock out the pump", safetyNotesAr = "اقفل المضخة",
        })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/tasks", new { description = "Check seal", descriptionAr = "افحص الحشوة" }));
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar");
        Assert.Equal("Fix pump", S(w, "title"));
        Assert.Equal("إصلاح المضخة", S(w, "titleAr"));
        Assert.Equal("Lock out the pump", S(w, "safetyNotes"));
        Assert.Equal("اقفل المضخة", S(w, "safetyNotesAr"));
        var step = w.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal("Check seal", S(step, "description"));
        Assert.Equal("افحص الحشوة", S(step, "descriptionAr"));
    }

    [SkippableFact]
    public async Task Blank_arabic_text_is_stored_as_empty()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var id = await AssetAsync(o.Admin, o.Site, nameAr: "   ");
        Assert.Null(S(await GetAsync(o.Admin, $"/api/assets/{id}", "ar"), "nameAr"));
    }

    [SkippableFact]
    public async Task Too_long_arabic_text_is_refused()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var r = await o.Admin.PostAsJsonAsync("/api/assets", new
        {
            name = Pump, nameAr = new string('م', 301), siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None",
        });
        Assert.Contains("nameAr", await Bad(r));
    }

    [SkippableFact]
    public async Task Arabic_text_changes_show_in_history()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var id = await AssetAsync(o.Admin, o.Site, nameAr: null);
        var a = await GetAsync(o.Admin, $"/api/assets/{id}", "en");
        await Ok(await o.Admin.PutAsJsonAsync($"/api/assets/{id}", new
        {
            assetNumber = S(a, "assetNumber"), name = Pump, nameAr = PumpAr, siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None", rowVersion = S(a, "rowVersion"),
        }));
        var history = (await Ok(await o.Admin.GetAsync($"/api/audit?entityType=Asset&entityId={id}"))).EnumerateArray().ToList();
        Assert.Contains(history, h => (S(h, "changes") ?? "").Contains("NameAr"));
    }
}
