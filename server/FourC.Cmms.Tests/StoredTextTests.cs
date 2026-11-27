using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

/// <summary>
/// 0.29.2 (spec decision 6, 0.25.0-5): lines CMX writes itself stay stored in exact English and are shown in the reader's language, old rows
/// too. Names, numbers and what a person typed inside them come through as written; text a person wrote is never translated.
/// </summary>
public class StoredTextTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>A summary row exactly as 0.29.1 stored it (AuditLog.Summary with AuditLog.Count's plural).</summary>
    private const string OldSummary = """{"Summary":{"to":"Brand renamed to Acme: 14 assets"}}""";

    public static IEnumerable<object[]> Forms() => StoredText.Forms.Select(f => new object[] { f });
    public static IEnumerable<object[]> PmForms() => StoredText.PmForms.Select(f => new object[] { f });

    /// <summary>The guard can't see stored lines (they aren't messages), so this is their coverage: every form has Arabic all through.</summary>
    [Theory, MemberData(nameof(Forms))]
    public void Every_stored_line_has_arabic(string english)
    {
        var sample = ArabicCoverageTests.Sample(english);
        var arabic = StoredText.Show(sample, "ar");
        Assert.NotEqual(sample, arabic);
        Assert.Empty(ArabicCoverageTests.EnglishLeft(arabic));
        Assert.Equal(sample, StoredText.Show(sample, "en"));
    }

    /// <summary>The same for PM generation results, through their own exit.</summary>
    [Theory, MemberData(nameof(PmForms))]
    public void Every_pm_result_has_arabic(string english)
    {
        var sample = ArabicCoverageTests.Sample(english);
        var arabic = StoredText.PmResult(sample, "ar");
        Assert.NotEqual(sample, arabic);
        Assert.Empty(ArabicCoverageTests.EnglishLeft(arabic));
        Assert.Equal(sample, StoredText.PmResult(sample, "en"));
    }

    [Fact]
    public void The_two_families_do_not_overlap() => Assert.Empty(StoredText.Forms.Intersect(StoredText.PmForms));

    [Fact]
    public void Counts_in_summaries_show_in_arabic_singular_and_plural()
    {
        Assert.Equal("أُعيدت تسمية العلامة التجارية إلى Acme: 14 أصل", StoredText.Show("Brand renamed to Acme: 14 assets", "ar"));
        Assert.Equal("دُمجت العلامة التجارية في Grundfos: 1 أصل", StoredText.Show("Brand merged into Grundfos: 1 asset", "ar"));
        Assert.Equal("أُعيدت تسمية الوحدة إلى box: 1 قطعة و3 من نقاط القياس والخطوات", StoredText.Show("Unit renamed to box: 1 part and 3 points and steps", "ar"));
        Assert.Equal("عُيّن مركز التكلفة على 2 أمر عمل و1 عملية صرف", StoredText.Show("Cost centre set on 2 work orders and 1 issue", "ar"));
        Assert.Equal("فُكّ ربط 3 طلب", StoredText.Show("3 requests unlinked", "ar"));
        Assert.Equal("Brand renamed to Acme: 14 assets", StoredText.Show("Brand renamed to Acme: 14 assets", "en"));
    }

    [Fact]
    public void Names_and_typed_words_come_through_as_written()
    {
        // "Inactive" and "Under repair" are known phrases with Arabic of their own: as a name or a person's words they stay as typed.
        Assert.Equal("رفضه Inactive: Under repair", StoredText.Show("Refused by Inactive: Under repair", "ar"));
        Assert.Equal("جرد المخزون 2026-11-27: Under repair", StoredText.Show("Stock take 2026-11-27: Under repair", "ar"));
        Assert.Equal("أُعيدت تسمية العلامة التجارية إلى Inactive: 2 أصل", StoredText.Show("Brand renamed to Inactive: 2 assets", "ar"));
        Assert.Equal("اعتُمد تلقائيًا (النوع: Inactive)", StoredText.Show("Approved automatically (type: Inactive)", "ar"));
    }

    [Fact]
    public void Only_cmx_lines_are_translated()
    {
        // A note a person typed is theirs, even when it happens to be a known phrase.
        Assert.Equal("Inactive", StoredText.Show("Inactive", "ar"));
        Assert.Equal("Pump is leaking again", StoredText.Show("Pump is leaking again", "ar"));
        Assert.Null(StoredText.Show(null, "ar"));
        Assert.Equal("", StoredText.Show("", "ar"));
    }

    /// <summary>
    /// I3 (0.29.2 final review): PM generation results are never stored, so their forms ("Skipped: {0}", "Waiting: {0}", "the asset is {0}")
    /// don't apply to the note columns people type in, and a typed note shaped like one is shown as typed.
    /// </summary>
    [Fact]
    public void Typed_notes_shaped_like_pm_results_stay_as_typed()
    {
        Assert.Equal("Waiting: parts", StoredText.Show("Waiting: parts", "ar"));
        Assert.Equal("the asset is running", StoredText.Show("the asset is running", "ar"));
        Assert.Equal("Skipped: Open", StoredText.Show("Skipped: Open", "ar"));
        Assert.Equal("Previous PM work order is still open", StoredText.Show("Previous PM work order is still open", "ar"));
    }

    /// <summary>I3 through the exits: a typed hold note, rejection note and stock note shaped like PM results come back as typed in Arabic.</summary>
    [SkippableFact]
    public async Task Typed_notes_shaped_like_pm_results_show_as_typed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, type = "Corrective", priority = "Low" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "OnHold", holdReason = "WaitingForParts", note = "Waiting: parts" }));
        var bodies = (await GetAsync(o.Admin, $"/api/work-orders/{wo}/activity", "ar")).EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString() == "status" && x.GetProperty("body").ValueKind == JsonValueKind.String).Select(x => x.GetProperty("body").GetString()).ToList();
        Assert.Contains("Waiting: parts", bodies);

        var request = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Noise", siteId = o.Site, priority = "Low", type = "Corrective" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-requests/{request}/reject", new { note = "the asset is running" }));
        Assert.Equal("the asset is running", (await GetAsync(o.Admin, $"/api/work-requests/{request}", "ar")).GetProperty("decisionNote").GetString());

        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 1, unitCost = 2, notes = "Skipped: Open" }));
        var notes = (await GetAsync(o.Admin, $"/api/inventory/transactions?partId={o.Part}", "ar")).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("notes").GetString()).ToList();
        Assert.Contains("Skipped: Open", notes);
    }

    [Fact]
    public void Pm_reasons_inside_a_line_are_translated_too()
    {
        Assert.Equal("تم التخطي: الأصل قيد الإصلاح", StoredText.PmResult("Skipped: the asset is Under repair", "ar"));
        Assert.Equal("بالانتظار: أمر العمل السابق لا يزال مفتوحًا", StoredText.PmResult("Waiting: the previous work order is still open", "ar"));
        Assert.Equal("تم التخطي: غيّرته عملية إنشاء أخرى", StoredText.PmResult("Skipped: changed by another generation run", "ar"));
        Assert.Equal("Skipped: the asset is Under repair", StoredText.PmResult("Skipped: the asset is Under repair", "en"));
        // A PM result never takes a stored line's form, and a stored line never takes a PM one.
        Assert.Equal("Created on the phone", StoredText.PmResult("Created on the phone", "ar"));
    }

    [Fact]
    public void Changes_translates_only_a_lone_summary()
    {
        var arabic = StoredText.Changes(OldSummary, "ar");
        Assert.Equal("أُعيدت تسمية العلامة التجارية إلى Acme: 14 أصل", SummaryOf(arabic));
        Assert.Equal(OldSummary, StoredText.Changes(OldSummary, "en"));
        // Field changes, a summary nobody wrote as a CMX line, bad JSON and nothing at all pass through untouched.
        const string fields = """{"Status":{"from":"Open","to":"InProgress"},"Reason":{"to":"Started automatically: labour logged"}}""";
        Assert.Equal(fields, StoredText.Changes(fields, "ar"));
        const string typed = """{"Summary":{"to":"Pump is leaking again"}}""";
        Assert.Equal(typed, StoredText.Changes(typed, "ar"));
        Assert.Equal("{", StoredText.Changes("{", "ar"));
        Assert.Null(StoredText.Changes(null, "ar"));
    }

    private static string? SummaryOf(string? changes) => JsonDocument.Parse(changes!).RootElement.GetProperty("Summary").GetProperty("to").GetString();

    private static JsonElement Row(JsonElement page, Guid entityId) =>
        page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("entityId").GetGuid() == entityId);

    /// <summary>Review focus 2: a summary written before 0.29.2 shows in Arabic in the activity log, the record's history and the export; the row stays English.</summary>
    [SkippableFact]
    public async Task Old_summary_rows_show_in_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var brand = Guid.NewGuid();
        await DbAsync(fx, async db =>
        {
            db.AuditEntries.Add(new AuditEntry
            {
                TenantId = o.Tenant, EntityType = "Brand", EntityId = brand, Action = "Updated", Changes = OldSummary, UserId = o.AdminId, UserName = "Admin",
                Timestamp = DateTimeOffset.UtcNow, ChangeId = Guid.NewGuid(),
            });
            return await db.SaveChangesAsync();
        });

        var arabic = Row(await GetAsync(o.Admin, "/api/activity?entityType=Brand", "ar"), brand).GetProperty("changes").GetString();
        Assert.Equal("أُعيدت تسمية العلامة التجارية إلى Acme: 14 أصل", SummaryOf(arabic));
        Assert.Equal(OldSummary, Row(await GetAsync(o.Admin, "/api/activity?entityType=Brand", "en"), brand).GetProperty("changes").GetString());

        var history = (await GetAsync(o.Admin, $"/api/audit?entityType=Brand&entityId={brand}", "ar")).EnumerateArray().Single();
        Assert.Equal("أُعيدت تسمية العلامة التجارية إلى Acme: 14 أصل", SummaryOf(history.GetProperty("changes").GetString()));
        var english = (await GetAsync(o.Admin, $"/api/audit?entityType=Brand&entityId={brand}", "en")).EnumerateArray().Single();
        Assert.Equal(OldSummary, english.GetProperty("changes").GetString());

        // The export writes the same rows: the Arabic summary is in the file, readable (not \u-escaped).
        var csv = await (await SendAsync(o.Admin, HttpMethod.Get, "/api/activity/export?format=csv&entityType=Brand&c=changes&t=Changes", "ar")).Content.ReadAsStringAsync();
        Assert.Contains("أُعيدت تسمية العلامة التجارية إلى Acme: 14 أصل", csv);
        var csvEnglish = await (await SendAsync(o.Admin, HttpMethod.Get, "/api/activity/export?format=csv&entityType=Brand&c=changes&t=Changes", "en")).Content.ReadAsStringAsync();
        Assert.Contains("Brand renamed to Acme: 14 assets", csvEnglish);

        // Reading never rewrites the row.
        var stored = await DbAsync(fx, db => db.AuditEntries.IgnoreQueryFilters().AsNoTracking().Where(a => a.EntityId == brand).Select(a => a.Changes).SingleAsync());
        Assert.Equal(OldSummary, stored);
    }

    /// <summary>Work order history notes CMX wrote (old rows seeded as stored) show in Arabic; a note a person typed stays as typed.</summary>
    [SkippableFact]
    public async Task History_notes_show_in_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, type = "Corrective", priority = "Low" })));
        var notes = new[] { "Generated from PM PM-7", "Refused by Rana: Inactive", "Started automatically: labour logged", "Inactive" };
        await DbAsync(fx, async db =>
        {
            var at = DateTimeOffset.UtcNow.AddMinutes(1);
            foreach (var (note, n) in notes.Select((x, n) => (x, n)))
                db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange
                {
                    TenantId = o.Tenant, WorkOrderId = wo, FromStatus = WorkOrderStatus.Open, ToStatus = WorkOrderStatus.InProgress, Note = note, ChangedAt = at.AddSeconds(n),
                    ChangedByName = "Admin",
                });
            return await db.SaveChangesAsync();
        });

        async Task<List<string>> BodiesAsync(string language) => (await GetAsync(o.Admin, $"/api/work-orders/{wo}/activity", language)).EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString() == "status" && x.GetProperty("body").ValueKind == JsonValueKind.String)
            .OrderBy(x => x.GetProperty("at").GetDateTimeOffset()).Select(x => x.GetProperty("body").GetString()!).ToList();

        Assert.Equal(new[] { "أُنشئ من الصيانة الوقائية PM-7", "رفضه Rana: Inactive", "بدأ تلقائيًا: سُجّلت ساعات عمل", "Inactive" }, await BodiesAsync("ar"));
        Assert.Equal(notes, await BodiesAsync("en"));
    }

    /// <summary>PM generation results (preview and generate) give their reason in the reader's language.</summary>
    [SkippableFact]
    public async Task Pm_generation_results_show_in_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var down = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Broken", siteId = o.Site, status = "UnderRepair", criticality = "High", warrantyType = "None" })));
        var t = await TemplateAsync(o.Admin, "T");
        var pm = Id(await SaveScheduleAsync(o.Admin, Schedule(o.Site, t, Today, down, extra: new() { ["managesRules"] = true, ["skipStatusesOverride"] = new[] { "UnderRepair" } })));

        static string? Reason(JsonElement results) => results.EnumerateArray().Single().GetProperty("skipped").GetString();
        Assert.Equal("Skipped: the asset is Under repair", Reason(await GetAsync(o.Admin, $"/api/pm/preview?until={Iso(Today)}", "en")));
        Assert.Equal("تم التخطي: الأصل قيد الإصلاح", Reason(await GetAsync(o.Admin, $"/api/pm/preview?until={Iso(Today)}", "ar")));
        var generated = await Ok(await SendAsync(o.Admin, HttpMethod.Post, "/api/pm/generate", "ar", new { until = Iso(Today), scheduleIds = new[] { pm } }));
        Assert.Equal("تم التخطي: الأصل قيد الإصلاح", Reason(generated.GetProperty("results")));
    }

    /// <summary>Stock movement notes CMX wrote show in Arabic in the transactions list and on the work order; the person's own words in them stay as typed.</summary>
    [SkippableFact]
    public async Task Stock_notes_show_in_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/stock-take", new { storeId = o.Store, date = Iso(Today), notes = "counted by Ali", lines = new[] { new { partId = o.Part, countedQuantity = 5 } } }));
        static string? Note(JsonElement page) => page.GetProperty("items").EnumerateArray().Single().GetProperty("notes").GetString();
        Assert.Equal($"جرد المخزون {Iso(Today)}: counted by Ali", Note(await GetAsync(o.Admin, $"/api/inventory/transactions?partId={o.Part}", "ar")));
        Assert.Equal($"Stock take {Iso(Today)}: counted by Ali", Note(await GetAsync(o.Admin, $"/api/inventory/transactions?partId={o.Part}", "en")));

        // A part used from the phone (an old row as stored), on the work order's parts.
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = o.Site, type = "Corrective", priority = "Low" })));
        await DbAsync(fx, async db =>
        {
            db.StockTransactions.Add(new StockTransaction
            {
                TenantId = o.Tenant, Type = StockTransactionType.Issue, PartId = o.Part, StoreId = o.Store, Quantity = -1, UnitCost = 10, Date = Today, WorkOrderId = wo,
                Notes = "From the phone", CreatedAt = DateTimeOffset.UtcNow, CreatedByName = "Tech",
            });
            return await db.SaveChangesAsync();
        });
        static string? PartNote(JsonElement w) => w.GetProperty("parts").EnumerateArray().Single().GetProperty("notes").GetString();
        Assert.Equal("من الهاتف", PartNote(await GetAsync(o.Admin, $"/api/work-orders/{wo}", "ar")));
        Assert.Equal("From the phone", PartNote(await GetAsync(o.Admin, $"/api/work-orders/{wo}", "en")));
    }

    /// <summary>A request approved automatically says so in the reader's language; the request type's name stays as stored.</summary>
    [SkippableFact]
    public async Task Request_auto_approval_note_shows_in_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var trade = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/trades", new { code = "AIRC", name = "Air con", isActive = true })));
        var type = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/request-types", new { code = "AC", name = "Type AC", isActive = true, autoApprove = true, defaultTradeId = trade })));
        var id = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests",
            new { title = "No cooling", siteId = o.Site, locationId = o.Location, priority = "Medium", type = "Corrective", requestTypeId = type })));
        Assert.Equal("Approved automatically (type: Type AC)", (await GetAsync(o.Admin, $"/api/work-requests/{id}", "en")).GetProperty("decisionNote").GetString());
        Assert.Equal("اعتُمد تلقائيًا (النوع: Type AC)", (await GetAsync(o.Admin, $"/api/work-requests/{id}", "ar")).GetProperty("decisionNote").GetString());
    }
}
