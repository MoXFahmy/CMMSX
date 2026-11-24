using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: work orders carry both texts of their title, steps and safety notes from their template or PM.</summary>
public class BilingualWorkTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Step = "Check coupling", StepAr = "افحص الوصلة", PmName = "Monthly chiller PM", PmNameAr = "صيانة المبرد الشهرية";

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Task<Guid> TemplateAsync(HttpClient c, string code, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Chiller check", ["nameAr"] = "فحص المبرد",
            ["stepDetails"] = new[] { new { description = Step, descriptionAr = StepAr, type = "Check" } },
        };
        foreach (var (k, v) in extra ?? []) body[k] = v;
        return Pm12.TemplateAsync(c, code, extra: body);
    }

    private static async Task<Guid> GeneratedAsync(HttpClient c, Guid site, Guid task, string language = "en")
    {
        var asset = await AssetAsync(c, site);
        var pm = Id(await Pm12.SaveScheduleAsync(c, Pm12.Schedule(site, task, Today, asset, extra: new() { ["name"] = PmName, ["nameAr"] = PmNameAr })));
        var r = await SendAsync(c, HttpMethod.Post, "/api/pm/generate", language, new { until = Iso(Today), scheduleIds = new[] { pm } });
        return (await Ok(r)).GetProperty("results").EnumerateArray().Single().GetProperty("workOrderId").GetGuid();
    }

    [SkippableFact]
    public async Task Pm_generation_copies_arabic_title_and_steps()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var wo = await GeneratedAsync(o.Admin, o.Site, await TemplateAsync(o.Admin, "CHK"));
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en");
        Assert.Equal(PmName, S(w, "title"));
        Assert.Equal(PmNameAr, S(w, "titleAr"));
        var step = w.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal(Step, S(step, "description"));
        Assert.Equal(StepAr, S(step, "descriptionAr"));
    }

    [SkippableFact]
    public async Task Generation_inside_an_arabic_request_keeps_main_texts_in_main_fields()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var wo = await GeneratedAsync(o.Admin, o.Site, await TemplateAsync(o.Admin, "CHK"), "ar");
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en");
        Assert.Equal(PmName, S(w, "title"));
        Assert.Equal(PmNameAr, S(w, "titleAr"));
        Assert.Equal(Step, S(w.GetProperty("tasks").EnumerateArray().Single(), "description"));
    }

    [SkippableFact]
    public async Task Safety_notes_get_their_own_fields_not_the_description()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = await TemplateAsync(o.Admin, "CHK", new() { ["safetyNotes"] = "Lock out the pump", ["safetyNotesAr"] = "اقفل المضخة" });
        var w = await GetAsync(o.Admin, $"/api/work-orders/{await GeneratedAsync(o.Admin, o.Site, task)}", "en");
        Assert.Equal("Lock out the pump", S(w, "safetyNotes"));
        Assert.Equal("اقفل المضخة", S(w, "safetyNotesAr"));
        Assert.DoesNotContain("Lock out", S(w, "description") ?? "");
    }

    [SkippableFact]
    public async Task New_from_template_copies_arabic_title_steps_and_safety_notes()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = await TemplateAsync(o.Admin, "CHK", new()
        {
            ["title"] = "Chiller check-up", ["titleAr"] = "فحص دوري للمبرد", ["safetyNotes"] = "Lock out the pump", ["safetyNotesAr"] = "اقفل المضخة",
        });
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, type = "Preventive", priority = "Medium", templateId = task,
        })));
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en");
        Assert.Equal("فحص دوري للمبرد", S(w, "titleAr"));
        Assert.Equal("Lock out the pump", S(w, "safetyNotes"));
        Assert.Equal("اقفل المضخة", S(w, "safetyNotesAr"));
        Assert.Equal(StepAr, S(w.GetProperty("tasks").EnumerateArray().Single(), "descriptionAr"));
    }

    [SkippableFact]
    public async Task Changing_the_template_later_leaves_the_work_order()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = await TemplateAsync(o.Admin, "CHK");
        var wo = await GeneratedAsync(o.Admin, o.Site, task);
        var t = await GetAsync(o.Admin, $"/api/pm-tasks/{task}", "en");
        await Ok(await o.Admin.PutAsJsonAsync($"/api/pm-tasks/{task}", new
        {
            code = "CHK", name = "Chiller check", isActive = true, rowVersion = S(t, "rowVersion"),
            stepDetails = new[] { new { description = Step, descriptionAr = "نص جديد", type = "Check" } },
        }));
        var w = await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en");
        Assert.Equal(StepAr, S(w.GetProperty("tasks").EnumerateArray().Single(), "descriptionAr"));
    }

    [SkippableFact]
    public async Task Follow_up_work_carries_the_arabic_title()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var follow = await Pm12.TemplateAsync(o.Admin, "FIX", extra: new() { ["title"] = "Fix coupling", ["titleAr"] = "إصلاح الوصلة" });
        var task = await TemplateAsync(o.Admin, "CHK", new()
        {
            ["stepDetails"] = new[] { new { description = Step, descriptionAr = StepAr, type = "Check", followUp = "WorkOrder", followUpTemplateId = (Guid?)follow } },
        });
        var wo = await GeneratedAsync(o.Admin, o.Site, task);
        var step = (await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en")).GetProperty("tasks").EnumerateArray().Single();
        var answer = await Ok(await o.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{step.GetProperty("id").GetGuid()}", new { answer = "NotOk" }));
        var followNumber = answer.GetProperty("followUp").GetProperty("number").GetString();
        var row = (await GetAsync(o.Admin, "/api/work-orders?search=" + followNumber, "en")).GetProperty("items").EnumerateArray().Single();
        var f = await GetAsync(o.Admin, $"/api/work-orders/{row.GetProperty("id").GetGuid()}", "en");
        Assert.Contains("إصلاح الوصلة", S(f, "titleAr") ?? "");
        Assert.StartsWith("Fix coupling", S(f, "title"));
    }

    [SkippableFact]
    public async Task Work_order_titles_follow_the_reader_where_shown()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var wo = await GeneratedAsync(o.Admin, o.Site, await TemplateAsync(o.Admin, "CHK"));
        var ar = (await GetAsync(o.Admin, "/api/work-orders", "ar")).GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == wo);
        Assert.Equal(PmNameAr, S(ar, "title"));
        var en = (await GetAsync(o.Admin, "/api/work-orders", "en")).GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == wo);
        Assert.Equal(PmName, S(en, "title"));
        var board = await SendAsync(o.Admin, HttpMethod.Get, $"/api/planning/board?from={Iso(Today.AddDays(-7))}&to={Iso(Today.AddDays(30))}", "ar");
        Assert.Contains(PmNameAr, await board.Content.ReadAsStringAsync());
        Assert.Equal(PmName, S(await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar"), "title"));
    }

    [SkippableFact]
    public async Task Template_copy_keeps_arabic_texts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var task = await TemplateAsync(o.Admin, "CHK", new() { ["title"] = "Chiller check-up", ["titleAr"] = "فحص دوري للمبرد", ["safetyNotes"] = "Lock", ["safetyNotesAr"] = "اقفل" });
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, await AssetAsync(o.Admin, o.Site), extra: new() { ["code"] = "PM7" })));
        var copy = (await Ok(await o.Admin.PostAsync($"/api/pm-schedules/{pm}/own-template", null))).GetProperty("pmTaskId").GetGuid();
        var c = await GetAsync(o.Admin, $"/api/pm-tasks/{copy}", "en");
        Assert.StartsWith("فحص المبرد", S(c, "nameAr"));
        Assert.Equal("فحص دوري للمبرد", S(c, "titleAr"));
        Assert.Equal("اقفل", S(c, "safetyNotesAr"));
        Assert.Equal(StepAr, S(c.GetProperty("stepDetails").EnumerateArray().Single(), "descriptionAr"));
    }
}
