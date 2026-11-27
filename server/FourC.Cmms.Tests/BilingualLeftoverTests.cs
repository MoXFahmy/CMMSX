using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Features.Ai;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.29.2 (0.28.0 left-overs): reports, calendars, checklists, sorting, the phone's other-language texts, safety notes and AI examples.</summary>
public class BilingualLeftoverTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Period = "from=2026-01-01&to=2026-06-30";

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IEnumerable<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray();

    private static async Task<string> TextAsync(HttpClient c, string url, string language) =>
        await (await SendAsync(c, HttpMethod.Get, url, language)).Content.ReadAsStringAsync();

    // ---------------------------------------------------------------- 0.28.0-1: reports, calendar, checklist names

    [SkippableFact]
    public async Task Pm_compliance_shows_schedule_and_category_names_in_the_readers_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var cat = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "PMP", name = "Pump kit", nameAr = "مجموعة المضخات", isActive = true })));
        var asset = await AssetAsync(o.Admin, o.Site, category: cat);
        var task = await Pm12.TemplateAsync(o.Admin, "CPM");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2), asset,
            extra: new() { ["name"] = "Monthly pump PM", ["nameAr"] = "صيانة المضخة الشهرية" })));
        await DbAsync(fx, async db =>
        {
            db.WorkOrders.Add(new WorkOrder
            {
                TenantId = o.Tenant, Number = $"H-{Guid.NewGuid():N}"[..20], Title = "Work", SiteId = o.Site, AssetId = asset, PmScheduleId = pm, PmDueDate = new DateOnly(2026, 3, 1),
                DueDate = new DateOnly(2026, 3, 1), Status = WorkOrderStatus.Completed, CompletedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var bySchedule = await TextAsync(o.Admin, $"/api/reports/pm-compliance?{Period}&groupBy=schedule", "ar");
        Assert.Contains("صيانة المضخة الشهرية", bySchedule);
        Assert.DoesNotContain("Monthly pump PM", bySchedule);
        Assert.Contains("Monthly pump PM", await TextAsync(o.Admin, $"/api/reports/pm-compliance?{Period}&groupBy=schedule", "en"));
        Assert.Contains("مجموعة المضخات", await TextAsync(o.Admin, $"/api/reports/pm-compliance?{Period}&groupBy=category", "ar"));
        var en = await TextAsync(o.Admin, $"/api/reports/pm-compliance?{Period}&groupBy=category", "en");
        Assert.Contains("Pump kit", en);
        Assert.DoesNotContain("مجموعة المضخات", en);
    }

    [SkippableFact]
    public async Task Calendar_used_by_and_asset_checklist_names_follow_the_reader()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(o.Admin, o.Site);
        var task = await Pm12.TemplateAsync(o.Admin, "CK", extra: new() { ["name"] = "Safety check", ["nameAr"] = "فحص السلامة" });
        var cal = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/pm-calendars", new { code = "SHUT", name = "Shutdowns", kind = "Dates", dates = new[] { Iso(Today.AddDays(30)) } })));
        await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, asset, extra: new() { ["name"] = "Monthly pump PM", ["nameAr"] = "صيانة المضخة الشهرية", ["calendarIds"] = new[] { cal } }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, pmTaskId = task }));

        var ar = await GetAsync(o.Admin, $"/api/pm-calendars/{cal}", "ar");
        Assert.Equal("صيانة المضخة الشهرية", S(ar.GetProperty("usedBy").EnumerateArray().Single(), "name"));
        Assert.Equal("Monthly pump PM", S((await GetAsync(o.Admin, $"/api/pm-calendars/{cal}", "en")).GetProperty("usedBy").EnumerateArray().Single(), "name"));
        var list = await GetAsync(o.Admin, $"/api/asset-checklists?assetId={asset}", "ar");
        Assert.Equal("فحص السلامة", S(list.EnumerateArray().Single(), "name"));
        Assert.Equal("Safety check", S((await GetAsync(o.Admin, $"/api/asset-checklists?assetId={asset}", "en")).EnumerateArray().Single(), "name"));
    }

    /// <summary>M4 (0.29.2 final review): the measurement point's and the template's "used by" lists show the names the reader reads.</summary>
    [SkippableFact]
    public async Task Point_and_template_used_by_follow_the_reader()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var point = await Ck13.PointAsync(o.Admin, "UBT");
        var task = await Ck13.TypedTemplateAsync(o.Admin, "UB", [new { description = "Supply air", type = "Number", measurementPointId = point }],
            new() { ["name"] = "Safety check", ["nameAr"] = "فحص السلامة" });
        Assert.Equal("فحص السلامة", S((await GetAsync(o.Admin, $"/api/measurement-points/{point}", "ar")).GetProperty("usedBy").EnumerateArray().Single(), "name"));
        Assert.Equal("Safety check", S((await GetAsync(o.Admin, $"/api/measurement-points/{point}", "en")).GetProperty("usedBy").EnumerateArray().Single(), "name"));

        // The template's schedules and its checklists' asset and category names.
        var cat = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "UBK", name = "Pump kit", nameAr = "مجموعة المضخات", isActive = true })));
        var asset = await AssetAsync(o.Admin, o.Site, category: cat);
        var number = S(await GetAsync(o.Admin, $"/api/assets/{asset}", "en"), "assetNumber");
        await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, asset, extra: new() { ["name"] = "Monthly pump PM", ["nameAr"] = "صيانة المضخة الشهرية" }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/asset-checklists", new { assetId = asset, pmTaskId = task }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/asset-checklists", new { assetCategoryId = cat, pmTaskId = task }));

        var ar = (await GetAsync(o.Admin, $"/api/pm-tasks/{task}", "ar")).GetProperty("usedBy");
        Assert.Equal("صيانة المضخة الشهرية", S(ar.GetProperty("schedules").EnumerateArray().Single(), "name"));
        Assert.Equal(new[] { $"{number} · {PumpAr}", "مجموعة المضخات" }.Order(), ar.GetProperty("checklists").EnumerateArray().Select(c => S(c, "name")!).Order());
        var en = (await GetAsync(o.Admin, $"/api/pm-tasks/{task}", "en")).GetProperty("usedBy");
        Assert.Equal("Monthly pump PM", S(en.GetProperty("schedules").EnumerateArray().Single(), "name"));
        Assert.Equal(new[] { $"{number} · {Pump}", "Pump kit" }.Order(), en.GetProperty("checklists").EnumerateArray().Select(c => S(c, "name")!).Order());
    }

    // ---------------------------------------------------------------- 0.28.0-3: PM schedule sort

    [SkippableFact]
    public async Task Pm_schedules_sort_by_the_name_the_reader_sees()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(o.Admin, o.Site);
        var task = await Pm12.TemplateAsync(o.Admin, "SRT");
        var alpha = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, asset, extra: new() { ["name"] = "Alpha", ["nameAr"] = "ياء" })));
        var beta = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, asset, extra: new() { ["name"] = "Beta", ["nameAr"] = "ألف" })));
        var ar = Items(await GetAsync(o.Admin, "/api/pm-schedules?sort=name", "ar")).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(ar.IndexOf(beta) < ar.IndexOf(alpha));
        var en = Items(await GetAsync(o.Admin, "/api/pm-schedules?sort=name", "en")).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(en.IndexOf(alpha) < en.IndexOf(beta));
    }

    // ---------------------------------------------------------------- 0.28.0-4: other-language names for search

    [SkippableFact]
    public async Task Phone_stock_parts_and_templates_carry_the_other_name()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        await EmployeeAsync(o.Admin, "ME", userId: o.AdminId);
        var store = await Phone.StoreAsync(o.Admin, o.Site);
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "B6204", name = "Bearing 6204", nameAr = "رولمان 6204", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        var plain = await Phone.PartAsync(o.Admin, store, "P2");
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", extra: new() { ["name"] = "Chiller check", ["nameAr"] = "فحص المبرد" });

        var ar = await GetAsync(o.Admin, "/api/tech/stock", "ar");
        var arPart = ar.GetProperty("parts").EnumerateArray().Single(p => Id(p) == part);
        Assert.Equal("رولمان 6204", S(arPart, "name"));
        Assert.Equal("Bearing 6204", S(arPart, "otherName"));
        Assert.Null(S(ar.GetProperty("parts").EnumerateArray().Single(p => Id(p) == plain), "otherName"));
        var arTask = ar.GetProperty("pmTasks").EnumerateArray().Single(t => Id(t) == task);
        Assert.Equal("فحص المبرد", S(arTask, "name"));
        Assert.Equal("Chiller check", S(arTask, "otherName"));

        var en = await GetAsync(o.Admin, "/api/tech/stock", "en");
        Assert.Equal("رولمان 6204", S(en.GetProperty("parts").EnumerateArray().Single(p => Id(p) == part), "otherName"));
        Assert.Equal("فحص المبرد", S(en.GetProperty("pmTasks").EnumerateArray().Single(t => Id(t) == task), "otherName"));
    }

    [SkippableFact]
    public async Task Location_hierarchy_carries_the_other_name()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var room = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new
        {
            siteId = o.Site, code = "PR", name = "Plant room", nameAr = "غرفة المعدات", type = "Room", criticality = "Medium", isActive = true,
        })));
        var plain = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new { siteId = o.Site, code = "ST", name = "Store", type = "Room", criticality = "Medium", isActive = true })));
        var ar = (await GetAsync(o.Admin, "/api/locations/hierarchy", "ar")).GetProperty("locations").EnumerateArray().ToList();
        Assert.Equal("غرفة المعدات", S(ar.Single(l => Id(l) == room), "name"));
        Assert.Equal("Plant room", S(ar.Single(l => Id(l) == room), "otherName"));
        Assert.Null(S(ar.Single(l => Id(l) == plain), "otherName"));
        var en = (await GetAsync(o.Admin, "/api/locations/hierarchy", "en")).GetProperty("locations").EnumerateArray().ToList();
        Assert.Equal("Plant room", S(en.Single(l => Id(l) == room), "name"));
        Assert.Equal("غرفة المعدات", S(en.Single(l => Id(l) == room), "otherName"));
    }

    // ---------------------------------------------------------------- 0.28.0-8: safety notes are never hidden

    [Fact]
    public void Either_shows_the_readers_text_else_the_other_and_treats_blank_as_missing()
    {
        using (Translator.Use("en"))
        {
            Assert.Equal("Isolate", Bilingual.Either("Isolate", "افصل"));
            Assert.Equal("احذر", Bilingual.Either(null, "احذر"));
            Assert.Equal("احذر", Bilingual.Either("", "احذر"));
            Assert.Equal("احذر", Bilingual.Either("  ", "احذر"));
            Assert.Null(Bilingual.Either(null, null));
            Assert.Null(Bilingual.Either("", " "));
        }
        using (Translator.Use("ar"))
        {
            Assert.Equal("افصل", Bilingual.Either("Isolate", "افصل"));
            Assert.Equal("Isolate", Bilingual.Either("Isolate", null));
            Assert.Equal("Isolate", Bilingual.Either("Isolate", "  "));
            Assert.Equal("احذر", Bilingual.Either("", "احذر"));
        }
    }

    [SkippableFact]
    public async Task Arabic_only_safety_notes_reach_every_reader_by_mail_and_on_the_phone()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var mail = fx.Factory!.Services.GetRequiredService<EmailSenderService>();
        var arabic = await TechAsync(o, "ar");
        var english = await TechAsync(o, "en");
        var asset = await AssetAsync(o.Admin, o.Site);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, assetId = asset, type = "Corrective", priority = "Medium", safetyNotesAr = "احذر الكهرباء",
            assigneeIds = new[] { arabic.Employee, english.Employee },
        })));
        await mail.RunOnceAsync();
        Assert.Contains("احذر الكهرباء", Assert.Single(fx.Emails.To(arabic.Email)).Html);
        Assert.Contains("احذر الكهرباء", Assert.Single(fx.Emails.To(english.Email)).Html);

        foreach (var (user, language) in new[] { (arabic, "ar"), (english, "en") })
        {
            var job = (await GetAsync(user.Client, "/api/tech/bundle", language)).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
            Assert.Equal("احذر الكهرباء", S(job, "safetyNotes"));
        }
    }

    [SkippableFact]
    public async Task Blank_main_safety_note_falls_back_on_the_phone()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var english = await TechAsync(o, "en");
        var asset = await AssetAsync(o.Admin, o.Site);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, assetId = asset, type = "Corrective", priority = "Medium", assigneeIds = new[] { english.Employee },
        })));
        // An empty main text (not null), as an import can leave it, with an Arabic one.
        await DbAsync(fx, async db =>
        {
            var w = db.WorkOrders.IgnoreQueryFilters().Single(x => x.Id == wo);
            w.SafetyNotes = "";
            w.SafetyNotesAr = "احذر الكهرباء";
            await db.SaveChangesAsync();
            return 0;
        });
        var job = (await GetAsync(english.Client, "/api/tech/bundle", "en")).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.Equal("احذر الكهرباء", S(job, "safetyNotes"));
    }

    /// <summary>A technician login in <paramref name="language"/>, linked to an employee.</summary>
    private async Task<(Guid Employee, string Email, HttpClient Client)> TechAsync(Loop.Org o, string language)
    {
        var user = await UserAsync(fx, o.Admin, "Technician");
        await Ok(await user.Client.PutAsJsonAsync("/api/auth/me/language", new { language }));
        var emp = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = $"T{Guid.NewGuid():N}"[..8], fullName = "Tech", userId = user.Id, isContractor = false })));
        return (emp, user.Email, user.Client);
    }

    // ---------------------------------------------------------------- 0.28.0-7: AI examples for steps, newest template first

    private static string Step(string text, string ar) => $"{text} ⇒ {ar}";

    private async Task<(Loop.Org Org, Guid A, Guid B, Guid C)> ThreeTemplatesAsync()
    {
        fx.Ai.Reset();
        var o = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(o.Admin, s => s["aiEnabled"] = true));
        async Task<Guid> Template(string code, string step, string stepAr) => await Pm12.TemplateAsync(o.Admin, code, extra: new()
        {
            ["name"] = $"Template {code}", ["nameAr"] = $"قالب {code}",
            ["stepDetails"] = new[] { new { description = step, descriptionAr = stepAr, type = "Check" } },
        });
        var a = await Template("TA", "Check A", "افحص أ");
        var b = await Template("TB", "Check B", "افحص ب");
        var c = await Template("TC", "Check C", "افحص ج");
        // A template with a step still missing its Arabic text, so the steps are what gets drafted.
        await Pm12.TemplateAsync(o.Admin, "TD", extra: new() { ["name"] = "Template TD", ["nameAr"] = "قالب د", ["stepDetails"] = new[] { new { description = "Check D", type = "Check" } } });
        fx.Ai.Responder = ask =>
        {
            var items = JsonNode.Parse(ask.Prompt)!.AsArray();
            var answer = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["id"] = i!["id"]!.GetValue<string>(), ["ar"] = "ع " + i["text"]!.GetValue<string>() }).ToArray());
            return Task.FromResult(FakeAiClient.Answer(new JsonObject { ["items"] = answer }.ToJsonString()));
        };
        return (o, a, b, c);
    }

    private static List<string> ExampleLines(string context) => context.Split('\n').Where(l => l.Contains(" ⇒ ")).ToList();

    [SkippableFact]
    public async Task Step_examples_come_newest_template_first()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, _, _) = await ThreeTemplatesAsync();
        var r = await Ok(await o.Admin.PostAsJsonAsync("/api/translations/draft", new { list = "templates" }));
        Assert.Equal("steps", S(r, "field"));
        Assert.Equal([Step("Check C", "افحص ج"), Step("Check B", "افحص ب"), Step("Check A", "افحص أ")], ExampleLines(Assert.Single(fx.Ai.Asks).SharedContext));
    }

    [SkippableFact]
    public async Task A_steps_only_edit_moves_its_template_to_the_front()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _, b, _) = await ThreeTemplatesAsync();
        var t = await Ok(await o.Admin.GetAsync($"/api/pm-tasks/{b}"));
        // Only the steps change: a second step is added; the name, notes and the rest are sent back as they were.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/pm-tasks/{b}", new
        {
            code = S(t, "code"), name = S(t, "name"), nameAr = S(t, "nameAr"), isActive = true, rowVersion = S(t, "rowVersion"),
            stepDetails = new[] { new { description = "Check B", descriptionAr = "افحص ب", type = "Check" }, new { description = "Check B2", descriptionAr = "افحص ب 2", type = "Check" } },
        }));
        var r = await Ok(await o.Admin.PostAsJsonAsync("/api/translations/draft", new { list = "templates" }));
        Assert.Equal("steps", S(r, "field"));
        Assert.Equal([Step("Check B", "افحص ب"), Step("Check B2", "افحص ب 2"), Step("Check C", "افحص ج"), Step("Check A", "افحص أ")], ExampleLines(Assert.Single(fx.Ai.Asks).SharedContext));
    }
}
