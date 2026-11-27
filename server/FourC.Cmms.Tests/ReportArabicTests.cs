using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>
/// G1/G2 (0.29.2 last review): every report reaches an Arabic reader in Arabic. Its title, subtitle, column names and summary labels, and every
/// cell CMX writes itself (a status, a yes, a level, a "met" or "missed"), hold no English word; names, codes, units and other data stay as
/// stored. English stays word for word as before.
/// </summary>
public class ReportArabicTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>
    /// The text columns that hold data, per report: what people typed or picked (names, numbers, titles, codes, units, reasons) is shown as
    /// stored. The first column is data too (names, or a fixed word the report's first-column pass translates, see <see cref="FixedWords"/>).
    /// Every other text column is CMX's own words and must be Arabic all through. A new report column is checked unless it is listed here.
    /// </summary>
    private static readonly Dictionary<string, string[]> DataColumns = new()
    {
        ["responsiveness"] = ["number", "title", "trade"],
        ["pm-annual-plan"] = ["pm"],
        ["downtime"] = ["reason", "from"], // a stop's reason (typed, or its work order's title) and its source number
        ["out-of-limits"] = ["asset", "point", "unit", "workOrder", "followUp"],
        ["contracts"] = ["number", "title", "company", "type", "currency"], // type: the organization's contract type name
        ["asset-work-history"] = ["number", "asset", "title", "symptom", "cause", "remedy"],
        ["stock-reorder"] = ["part", "store"],
        ["parts-consumption"] = ["unit"],
        ["dormant-stock"] = ["part", "store"],
        ["open-purchase-orders"] = ["number", "supplier"],
    };

    /// <summary>Fixed words CMX writes into a report's data columns (a missing trade, a stop entered by hand…): never left in English for an Arabic reader.</summary>
    public static readonly string[] FixedWords =
        ["No trade", "No team", "No type", "No reason", "Uncategorised", "Ungrouped", "No asset", "No asset (location work)", "Issued to a location", "No cost centre",
         "Not coded", "Not recorded", "By hand"];

    /// <summary>Every report, with each of its groupings.</summary>
    private static IEnumerable<string> Urls(Guid asset) =>
        ReportEndpoints.Catalog.SelectMany(def =>
            (def.Params.FirstOrDefault(p => p.Name == "groupBy")?.Options?.Select(o => (string?)o.Value) ?? [null]).Select(g =>
                $"/api/reports/{def.Key}?" + string.Join("&", new[]
                {
                    def.Params.Any(p => p.Type == "asset") ? $"assetId={asset}&components=true" : null, g is null ? null : $"groupBy={g}",
                }.OfType<string>())));

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A sample-data organization (sites, assets, a year of work, labour by employees and contractors, stock and purchase orders).</summary>
    private async Task<(Loop.Org Org, Guid Asset)> SampleOrgAsync()
    {
        var o = await OrgAsync(fx, emailOn: false);
        await Ok(await o.Admin.PostAsync("/api/settings/sample-data", null));
        var asset = await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.TenantId == o.Tenant && w.AssetId != null)
            .GroupBy(w => w.AssetId!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync());
        return (o, asset);
    }

    [SkippableFact]
    public async Task Every_report_is_arabic_for_arabic_readers()
    {
        Skip.IfNot(fx.Enabled);
        var (o, asset) = await SampleOrgAsync();
        // Rows the sample data has none of: a stop entered by hand, a contract, and work cancelled with no reason.
        await DbAsync(fx, async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var a = await db.Assets.IgnoreQueryFilters().FirstAsync(x => x.Id == asset);
            var supplier = await db.Suppliers.IgnoreQueryFilters().Where(s => s.TenantId == o.Tenant).OrderBy(s => s.Name).FirstAsync();
            db.DowntimeEvents.Add(new Core.Assets.DowntimeEvent { TenantId = o.Tenant, AssetId = asset, StartedAt = now.AddDays(-10), EndedAt = now.AddDays(-10).AddHours(3),
                Reason = "Belt broke", Source = Core.Assets.DowntimeSource.Manual });
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            db.Contracts.Add(new Core.Contracts.Contract { TenantId = o.Tenant, Number = "CT-T1", Title = "Chiller service", SupplierId = supplier.Id, StartDate = today.AddMonths(-3), EndDate = today.AddMonths(9) });
            var w = new Core.Work.WorkOrder { TenantId = o.Tenant, Number = "T-CANCELLED", Title = "Not needed", SiteId = a.SiteId, AssetId = asset, Type = Core.Work.WorkType.Corrective,
                Status = Core.Work.WorkOrderStatus.Cancelled };
            db.WorkOrders.Add(w);
            db.WorkOrderStatusChanges.Add(new Core.Work.WorkOrderStatusChange { TenantId = o.Tenant, WorkOrderId = w.Id, ToStatus = Core.Work.WorkOrderStatus.Cancelled, ChangedAt = now.AddDays(-5) });
            await db.SaveChangesAsync();
            return 0;
        });
        var problems = new List<string>();
        var cells = 0;
        var withRows = new HashSet<string>();
        foreach (var url in Urls(asset))
        {
            var r = await GetAsync(o.Admin, url, "ar");
            var key = Str(r, "key")!;
            void Check(string where, string? text)
            {
                if (text is not null && ArabicCoverageTests.EnglishLeft(text) is { Count: > 0 } left) problems.Add($"{url} {where}: \"{text}\" (English: {string.Join(", ", left)})");
            }
            Check("title", Str(r, "title"));
            // The asset history's subtitle starts with the asset's number and name (data).
            var subtitle = Str(r, "subtitle");
            Check("subtitle", key == "asset-work-history" && subtitle is not null ? string.Join(" · ", subtitle.Split(" · ").Skip(2)) : subtitle);
            var columns = r.GetProperty("columns").EnumerateArray().Select(c => (Key: Str(c, "key")!, Label: Str(c, "label"))).ToList();
            foreach (var c in columns) Check($"column {c.Key}", c.Label);
            foreach (var k in r.GetProperty("summary").EnumerateArray()) Check("summary", Str(k, "label"));
            var data = DataColumns.GetValueOrDefault(key) ?? [];
            if (r.GetProperty("rows").GetArrayLength() > 0) withRows.Add(key);
            foreach (var row in r.GetProperty("rows").EnumerateArray())
                foreach (var (c, i) in columns.Select((c, i) => (c, i)))
                {
                    if (Str(row, c.Key) is not { } value) continue;
                    cells++;
                    if (i == 0 || data.Contains(c.Key))
                    {
                        if (FixedWords.Contains(value) || value.EndsWith(" (contractor)", StringComparison.Ordinal)) problems.Add($"{url} {c.Key}: \"{value}\" (a fixed word left in English)");
                    }
                    else Check($"cell {c.Key}", value);
                }
        }
        Assert.True(cells > 1000, $"only {cells} text cells: the sample data no longer fills the reports");
        var found = problems.Distinct().ToList();
        Assert.True(found.Count == 0, $"{found.Count} English text(s) in Arabic reports:\n" + string.Join("\n", found.Take(80)));
        // Every report had rows to check, apart from the two the sample data gives nothing to (no readings, no requester answers): their
        // cells are a level word (translated) and group names.
        Assert.Subset(new HashSet<string> { "out-of-limits", "requester-satisfaction" }, ReportEndpoints.Catalog.Select(d => d.Key).Where(k => !withRows.Contains(k)).ToHashSet());
    }

    /// <summary>The reports G2 named, read in Arabic and in English: the Arabic words, and English word for word as before.</summary>
    [SkippableFact]
    public async Task Status_words_follow_the_reader_and_english_is_unchanged()
    {
        Skip.IfNot(fx.Enabled);
        var (o, _) = await SampleOrgAsync();
        // The sample data leaves no line in some states: one of each, written straight to the database.
        await DbAsync(fx, async db =>
        {
            var levels = await db.StockLevels.IgnoreQueryFilters().Include(l => l.Part).Where(l => l.TenantId == o.Tenant && l.ReorderQuantity > 0 && l.Part!.IsActive)
                .OrderBy(l => l.Id).Take(2).ToListAsync();
            levels[0].QuantityOnHand = 0; // out of stock, of a critical part
            levels[0].Part!.IsCritical = true;
            levels[1].MinLevel = Math.Max(levels[1].MinLevel, 2); // below its minimum
            levels[1].QuantityOnHand = 1;
            levels[1].ReorderLevel = Math.Max(levels[1].ReorderLevel, levels[1].MinLevel);
            await db.SaveChangesAsync();
            return 0;
        });

        static List<JsonElement> Rows(JsonElement r) => r.GetProperty("rows").EnumerateArray().ToList();
        string[] Column(JsonElement r, string column) => Rows(r).Select(x => Str(x, column)).OfType<string>().ToArray();

        var reorderEn = await GetAsync(o.Admin, "/api/reports/stock-reorder", "en");
        var reorderAr = await GetAsync(o.Admin, "/api/reports/stock-reorder", "ar");
        Assert.NotEmpty(Rows(reorderEn));
        Assert.All(Column(reorderEn, "status"), s => Assert.Contains(s, new[] { "Out of stock", "Below minimum", "Re-order", "On order" }));
        Assert.Contains("Out of stock", Column(reorderEn, "status"));
        Assert.Contains("Below minimum", Column(reorderEn, "status"));
        Assert.All(Column(reorderEn, "critical"), s => Assert.Contains(s, new[] { "Yes", "" }));
        Assert.Contains("Yes", Column(reorderEn, "critical"));
        Assert.All(Column(reorderAr, "status"), s => Assert.Contains(s, new[] { "نفد المخزون", "أقل من الحد الأدنى", "إعادة الطلب", "تحت الطلب" }));
        Assert.All(Column(reorderAr, "critical"), s => Assert.Contains(s, new[] { "نعم", "" }));
        Assert.Contains("نعم", Column(reorderAr, "critical"));

        var posEn = await GetAsync(o.Admin, "/api/reports/open-purchase-orders", "en");
        var posAr = await GetAsync(o.Admin, "/api/reports/open-purchase-orders", "ar");
        Assert.Contains("Submitted", Column(posEn, "status"));
        Assert.All(Column(posEn, "status"), s => Assert.Contains(s, new[] { "Submitted", "Approved", "PartiallyReceived" })); // as before, word for word
        Assert.Contains("بانتظار الاعتماد", Column(posAr, "status"));
        Assert.All(Column(posAr, "status"), s => Assert.Contains(s, new[] { "بانتظار الاعتماد", "معتمد", "مستلم جزئيًا" }));

        var laborEn = Column(await GetAsync(o.Admin, "/api/reports/labor-hours", "en"), "group");
        var laborAr = Column(await GetAsync(o.Admin, "/api/reports/labor-hours", "ar"), "group");
        var contractor = laborEn.First(g => g.EndsWith(" (contractor)", StringComparison.Ordinal)); // the sample data's contract technicians
        Assert.Contains($"{contractor[..^" (contractor)".Length]} (مقاول)", laborAr);
        Assert.DoesNotContain(laborAr, g => g.Contains("contractor", StringComparison.OrdinalIgnoreCase));

        var backlogEn = (await GetAsync(o.Admin, "/api/reports/wo-backlog", "en")).GetProperty("columns").EnumerateArray().Select(c => Str(c, "label")).ToList();
        var backlogAr = (await GetAsync(o.Admin, "/api/reports/wo-backlog", "ar")).GetProperty("columns").EnumerateArray().Select(c => Str(c, "label")).ToList();
        Assert.Equal(["Priority", "0–7 days", "8–30 days", "31–90 days", "Over 90 days", "On hold", "Overdue", "Total"], backlogEn);
        Assert.Equal(["0–7 أيام", "8–30 يومًا", "31–90 يومًا", "أكثر من 90 يومًا"], backlogAr.Skip(1).Take(4));
    }

    [Theory]
    [MemberData(nameof(Words))]
    public void Report_words_have_arabic(string english) => Assert.Empty(ArabicCoverageTests.EnglishLeft(Translator.T(english, "ar")));

    public static IEnumerable<object[]> Words() =>
        FixedWords.Concat(["0–7 days", "8–30 days", "31–90 days", "{0} (contractor)", "Out of stock", "Below minimum", "Re-order", "On order", "Yes",
            "Submitted", "Approved", "PartiallyReceived"]).Select(w => new object[] { w });
}
