using System.Globalization;
using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Reports return a uniform, UI-agnostic shape (columns, rows, optional chart and KPI summary) so the web client
/// can render, chart, print and export any report the same way.
/// </summary>
public static class ReportEndpoints
{
    public sealed record Column(string Key, string Label, string Type = "text");
    public sealed record Chart(string Type, string X, List<string> Series, bool Stacked = false);
    public sealed record Kpi(string Label, object? Value, string Type = "number");
    public sealed record ReportResult(string Key, string Title, string? Subtitle, List<Column> Columns, List<Dictionary<string, object?>> Rows, Chart? Chart, List<Kpi> Summary);
    public sealed record Option(string Value, string Label);
    public sealed record Param(string Name, string Type, List<Option>? Options = null, object? Default = null);
    public sealed record ReportDef(string Key, string Category, string Title, string Description, string Permission, List<Param> Params);
    /// <summary>A report's choices. 0.14.0: one asset (with its components), and a year.</summary>
    public sealed record ReportArgs(DateOnly From, DateOnly To, Guid? SiteId, string? GroupBy, int Days, Guid? AssetId = null, int Year = 0, bool Components = false,
        Guid? CostCentreId = null, FourC.Cmms.Core.Tenancy.TenantSettings? Settings = null, Guid? AssetGroupId = null);
    /// <summary>A month of the PM annual plan (0.14.0): column type "plan".</summary>
    public sealed record PlanCell(int Done, int Late, int Missed, int Cancelled, int Planned);
    public const string ChooseAsset = "Choose an asset.";
    public const string ChooseYear = "Choose a year from 2000 to 2100.";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly Param Range = new("range", "dateRange");
    private static readonly Param SiteP = new("siteId", "site");
    private static readonly Param YearP = new("year", "year");
    private static readonly Param AssetP = new("assetId", "asset");
    private static readonly Param ComponentsP = new("components", "bool");
    /// <summary>A cost centre with its children (0.20.0).</summary>
    private static readonly Param CostCentreP = new("costCentreId", "costCentre");
    // 0.21.0: an asset group, by each row's own asset.
    private static readonly Param AssetGroupP = new("assetGroupId", "assetGroup");
    private static Param GroupBy(object def, params (string, string)[] o) => new("groupBy", "select", o.Select(x => new Option(x.Item1, x.Item2)).ToList(), def);

    public static readonly List<ReportDef> Catalog =
    [
        new("wo-backlog", "Work", "Work order backlog", "Open work orders by priority and age, with overdue and on-hold counts.", Permissions.WorkOrdersView, [SiteP]),
        new("targets-met", "Work", "Targets met", "Requests and work orders that met their response and completion targets, by month or priority.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("month", ("month", "Month"), ("priority", "Priority"))]),
        new("responsiveness", "Work", "Responsiveness", "Each request and work order against its response and completion targets, with averages by priority and trade.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("order", ("order", "Order"), ("priority", "Priority"), ("trade", "Trade"))]),
        new("wo-throughput", "Work", "Work order throughput", "Work orders raised and completed per month, on-time completion and average turnaround.", Permissions.WorkOrdersView, [Range, SiteP]),
        new("pm-compliance", "Work", "PM compliance", "Preventive work due in the period: completed on time, late, or missed.", Permissions.PmView,
            [Range, SiteP, AssetGroupP, GroupBy("month", ("month", "Month"), ("schedule", "PM schedule"), ("trade", "Trade"), ("category", "Asset category"))]),
        new("pm-annual-plan", "Work", "PM annual plan", "Each PM's year, month by month: what was done, late, missed or cancelled, and what is still planned.", Permissions.PmView, [YearP, SiteP]),
        new("requester-satisfaction", "Work", "Requester satisfaction", "Requesters' answers to finished work: accepted or refused, and their ratings.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("month", ("month", "Month"), ("type", "Request type"), ("trade", "Trade"), ("team", "Team"), ("site", "Site"))]),
        new("cancelled-work", "Work", "Cancelled work", "Work orders cancelled in the period, by reason.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("reason", ("reason", "Reason"), ("month", "Month"), ("trade", "Trade"), ("site", "Site"))]),
        new("labor-hours", "Work", "Labour hours", "Hours, overtime and labour cost booked to work orders.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("employee", ("employee", "Employee"), ("trade", "Trade"), ("team", "Team"), ("type", "Work type"))]),
        new("reliability", "Reliability", "Asset reliability (MTBF / MTTR)", "Breakdowns per asset with downtime, mean time between failures and mean time to repair.", Permissions.WorkOrdersView, [Range, SiteP, AssetGroupP]),
        new("downtime", "Reliability", "Downtime log", "Every stop in the period with how long the asset was stopped during its running time.", Permissions.WorkOrdersView, [Range, SiteP, AssetGroupP]),
        new("out-of-limits", "Reliability", "Out of limits", "Readings at an alert or alarm level, with the work they raised.", Permissions.WorkOrdersView, [Range, SiteP]),
        new("failure-tree", "Reliability", "Failure tree", "Completed work by symptom, cause and remedy, with hours, downtime and cost.", Permissions.WorkOrdersView, [Range, SiteP, AssetGroupP]),
        new("failure-pareto", "Reliability", "Failure analysis", "Breakdowns by failure code — the few causes behind most of the downtime.", Permissions.WorkOrdersView, [Range, SiteP, AssetGroupP]),
        new("maintenance-costs", "Costs", "Maintenance costs", "Labour, parts and other costs booked in the period.", Permissions.WorkOrdersView,
            [Range, SiteP, CostCentreP, AssetGroupP, GroupBy("asset", ("asset", "Asset"), ("site", "Site"), ("category", "Asset category"), ("type", "Work type"), ("trade", "Trade"), ("team", "Team"), ("technician", "Technician"), ("month", "Month"), ("costCentre", "Cost centre"))]),
        new("budget-vs-actual", "Costs", "Budget against actual", "Budget, committed spend and actual cost by cost centre or month, with the variance.", Permissions.ReportsView,
            [Range, CostCentreP, GroupBy("costCentre", ("costCentre", "Cost centre"), ("month", "Month"))]),
        new("contracts", "Costs", "Contracts", "Each contract's value, spend and completed work in the period against what it expects.", Permissions.ReportsView, [Range, SiteP]),
        new("asset-work-history", "Assets", "Asset work history", "Every work order on one asset in the period, with its codes, hours, cost and downtime.", Permissions.WorkOrdersView,
            [AssetP, Range, ComponentsP]),
        new("assets-by-category", "Assets", "Asset register summary", "Assets by category and status with purchase and current value.", Permissions.AssetsView, [SiteP, AssetGroupP]),
        new("stock-valuation", "Inventory", "Stock valuation", "Value of stock on hand at average cost.", Permissions.InventoryView,
            [SiteP, GroupBy("store", ("store", "Store"), ("group", "Part group"))]),
        new("stock-reorder", "Inventory", "Stock below re-order level", "Store levels at or below their re-order point, with quantity on order.", Permissions.InventoryView, [SiteP]),
        new("parts-consumption", "Inventory", "Parts consumption", "Parts issued to work (net of returns) in the period.", Permissions.InventoryView,
            [Range, SiteP, GroupBy("part", ("part", "Part"), ("group", "Part group"), ("asset", "Asset"))]),
        new("dormant-stock", "Inventory", "Dormant stock", "Stock with no issues for a number of days — candidates for reduction.", Permissions.InventoryView,
            [SiteP, new Param("days", "number", null, 180)]),
        new("spend-by-supplier", "Purchasing", "Spend by supplier", "Goods and services received per supplier, with on-time delivery.", Permissions.PurchasingView, [Range, SiteP]),
        new("open-purchase-orders", "Purchasing", "Open purchase orders", "Orders awaiting approval or delivery, with outstanding value and lateness.", Permissions.PurchasingView, [SiteP]),
    ];

    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/reports").WithTags("Reports");
        g.MapGet("/", async (HttpContext http, PermissionService perms) =>
        {
            var mine = await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            return Catalog.Where(r => mine.Contains(r.Permission)).Select(Localized);
        }).RequirePermission(Permissions.ReportsView);

        g.MapGet("/{key}", async (string key, HttpContext http, PermissionService perms, CmmsDbContext db, DateOnly? from, DateOnly? to, Guid? siteId, string? groupBy, int? days,
            Guid? assetId, int? year, bool? components, Guid? costCentreId, Guid? assetGroupId, CancellationToken ct) =>
        {
            var def = Catalog.FirstOrDefault(r => r.Key == key);
            if (def is null) return Results.NotFound();
            if (!(await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!))).Contains(def.Permission)) return Results.Forbid();
            var t = to ?? Today;
            var f = from ?? t.AddMonths(-12).AddDays(1);
            if (f > t) throw new FourC.Cmms.Core.Common.DomainException("The start date is after the end date.");
            if (t.DayNumber - f.DayNumber > 3700) throw new FourC.Cmms.Core.Common.DomainException("Choose a period of ten years or less.");
            var allowed = def.Params.FirstOrDefault(p => p.Name == "groupBy");
            var gb = allowed?.Options?.Any(o => o.Value == groupBy) == true ? groupBy : allowed?.Default as string;
            if (def.Params.Any(p => p.Type == "asset") && assetId is null) throw new FourC.Cmms.Core.Common.DomainException(ChooseAsset);
            if (assetId is { } chosen && def.Params.Any(p => p.Type == "asset") && !await db.Assets.AnyAsync(x => x.Id == chosen, ct)) return Results.NotFound();
            // A cost centre the person can't see (another site's) can't be reported on.
            if (costCentreId is { } centre && def.Params.Any(p => p.Type == "costCentre") && !await db.CostCentres.AnyAsync(x => x.Id == centre, ct)) return Results.NotFound();
            var byGroup = def.Params.Any(p => p.Type == "assetGroup") ? assetGroupId : null;
            if (byGroup is { } grp && !await db.AssetGroups.AnyAsync(x => x.Id == grp, ct)) return Results.NotFound();
            var y = year ?? Today.Year;
            if (def.Params.Any(p => p.Type == "year") && y is < 2000 or > 2100) throw new FourC.Cmms.Core.Common.DomainException(ChooseYear);
            // The organization's names for types, priorities, statuses and criticality (0.21.0) label the rows.
            var orgSettings = (await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == db.CurrentTenantId, ct)).Settings;
            var a = new ReportArgs(f, t, siteId, gb, Math.Clamp(days ?? 180, 1, 3650), assetId, y, components == true, costCentreId, orgSettings, byGroup);
            ReportResult r = key switch
            {
                "wo-backlog" => await BacklogAsync(db, a, ct),
                "wo-throughput" => await ThroughputAsync(db, a, ct),
                "targets-met" => await TargetsMetAsync(db, a, ct),
                "pm-compliance" => await PmComplianceAsync(db, a, ct),
                "pm-annual-plan" => await PmAnnualPlanAsync(db, a, ct),
                "asset-work-history" => await AssetHistoryAsync(db, a, ct),
                "labor-hours" => await LaborAsync(db, a, ct),
                "requester-satisfaction" => await SatisfactionAsync(db, a, ct),
                "cancelled-work" => await CancelledAsync(db, a, ct),
                "reliability" => await ReliabilityAsync(db, a, ct),
                "failure-pareto" => await FailureParetoAsync(db, a, ct),
                "failure-tree" => await FailureTreeAsync(db, a, ct),
                "responsiveness" => await ResponsivenessAsync(db, a, ct),
                "out-of-limits" => await OutOfLimitsAsync(db, a, ct),
                "downtime" => await DowntimeLogAsync(db, a, ct),
                "maintenance-costs" => await CostsAsync(db, a, ct),
                "budget-vs-actual" => await BudgetReport.BuildAsync(db, a, ct),
                "contracts" => await ContractsAsync(db, a, ct),
                "assets-by-category" => await AssetsAsync(db, a, ct),
                "stock-valuation" => await ValuationAsync(db, a, ct),
                "stock-reorder" => await ReorderAsync(db, a, ct),
                "parts-consumption" => await ConsumptionAsync(db, a, ct),
                "dormant-stock" => await DormantAsync(db, a, ct),
                "spend-by-supplier" => await SpendAsync(db, a, ct),
                "open-purchase-orders" => await OpenPoAsync(db, a, ct),
                _ => throw new InvalidOperationException(),
            };
            return Results.Ok(Localized(r));
        }).RequirePermission(Permissions.ReportsView);
    }

    // ---------------------------------------------------------------- helpers
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) => cells.ToDictionary(c => c.Key, c => c.Value);
    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static DateTimeOffset End(DateOnly d) => Start(d.AddDays(1));
    /// <summary>Month names in the person's language (Arabic months with the digits 0-9), else as before.</summary>
    private static CultureInfo Dates => Translator.CurrentLanguage == "ar" ? CultureInfo.GetCultureInfo("ar-EG") : CultureInfo.InvariantCulture;
    private static string Period(ReportArgs a) => $"{a.From.ToString("d MMM yyyy", Dates)} – {a.To.ToString("d MMM yyyy", Dates)}";

    /// <summary>A report's name, description and choices in the person's language.</summary>
    private static ReportDef Localized(ReportDef d) => d with
    {
        Title = Translator.T(d.Title), Description = Translator.T(d.Description),
        Params = d.Params.Select(p => p with { Options = p.Options?.Select(o => o with { Label = Translator.T(o.Label) }).ToList() }).ToList(),
    };

    /// <summary>
    /// A report's texts in the person's language: title, subtitle, column names, summary labels, and the first column's
    /// group names where they are fixed words (statuses, priorities, "No asset"…); names people typed stay as they are.
    /// </summary>
    private static ReportResult Localized(ReportResult r)
    {
        if (Translator.CurrentLanguage == "en") return r;
        var first = r.Columns.FirstOrDefault()?.Key;
        return r with
        {
            Title = Translator.T(r.Title), Subtitle = r.Subtitle is null ? null : Translator.T(r.Subtitle),
            Columns = r.Columns.Select(c => c with { Label = Translator.T(c.Label) }).ToList(),
            Summary = r.Summary.Select(k => k with { Label = Translator.T(k.Label) }).ToList(),
            Rows = first is null ? r.Rows : r.Rows.Select(row => row.TryGetValue(first, out var v) && v is string s
                ? new Dictionary<string, object?>(row) { [first] = Translator.T(s) } : row).ToList(),
        };
    }
    private static decimal R2(decimal v) => Math.Round(v, 2);
    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1);
    private static IEnumerable<(int Y, int M)> Months(ReportArgs a)
    {
        for (var d = new DateOnly(a.From.Year, a.From.Month, 1); d <= a.To; d = d.AddMonths(1)) yield return (d.Year, d.Month);
    }
    private static string MonthLabel(int y, int m) => new DateOnly(y, m, 1).ToString("MMM yyyy", Dates);
    private static bool IsFailure(WorkOrder w) => w.Type == WorkType.Breakdown || w.AssetWasDown;

    private static IQueryable<WorkOrder> Wo(CmmsDbContext db, ReportArgs a)
    {
        var q = a.SiteId is { } s ? db.WorkOrders.AsNoTracking().Where(w => w.SiteId == s) : db.WorkOrders.AsNoTracking();
        // An asset group (0.21.0): work on one of its assets, or a round covering one.
        if (a.AssetGroupId is { } g) { var members = AssetGroupEndpoints.Members(db, g); q = q.Where(w => (w.AssetId != null && members.Contains(w.AssetId.Value)) || w.Assets.Any(x => members.Contains(x.AssetId))); }
        return q;
    }

    // ---------------------------------------------------------------- work
    private static async Task<ReportResult> BacklogAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var open = await Wo(db, a).Where(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
            .Select(w => new { w.Priority, w.CreatedAt, w.DueAt, w.Status }).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        string Bucket(DateTimeOffset c) => (now - c).TotalDays switch { <= 7 => "d0_7", <= 30 => "d8_30", <= 90 => "d31_90", _ => "d90" };
        var rows = Enum.GetValues<WorkPriority>().OrderByDescending(p => p).Select(p =>
        {
            var xs = open.Where(w => w.Priority == p).ToList();
            return Row(("priority", Lists.Name(a.Settings, p)), ("d0_7", xs.Count(w => Bucket(w.CreatedAt) == "d0_7")), ("d8_30", xs.Count(w => Bucket(w.CreatedAt) == "d8_30")),
                ("d31_90", xs.Count(w => Bucket(w.CreatedAt) == "d31_90")), ("d90", xs.Count(w => Bucket(w.CreatedAt) == "d90")),
                ("onHold", xs.Count(w => w.Status == WorkOrderStatus.OnHold)), ("overdue", xs.Count(w => w.DueAt < now)), ("total", xs.Count));
        }).ToList();
        return new("wo-backlog", "Work order backlog", $"As of {Today:d MMM yyyy}",
            [new("priority", "Priority"), new("d0_7", "0–7 days", "number"), new("d8_30", "8–30 days", "number"), new("d31_90", "31–90 days", "number"), new("d90", "Over 90 days", "number"),
             new("onHold", "On hold", "number"), new("overdue", "Overdue", "number"), new("total", "Total", "number")],
            rows, new Chart("bar", "priority", ["d0_7", "d8_30", "d31_90", "d90"], true),
            [new("Open work orders", open.Count), new("Overdue", open.Count(w => w.DueAt < now)), new("On hold", open.Count(w => w.Status == WorkOrderStatus.OnHold)),
             new("Average age (days)", open.Count == 0 ? 0 : Math.Round(open.Average(w => (now - w.CreatedAt).TotalDays), 1))]);
    }

    /// <summary>
    /// Deadlines falling in the period, met or missed (pending ones aren't counted). Response: requests (decision) and work
    /// orders not raised as a request (first action). Completion: work orders not generated by PM (PM compliance has its own report).
    /// </summary>
    private static async Task<ReportResult> TargetsMetAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var now = DateTimeOffset.UtcNow;
        var requests = db.WorkRequests.AsNoTracking().Where(r => r.Status != WorkRequestStatus.Cancelled && r.RespondBy >= s && r.RespondBy < e);
        if (a.SiteId is { } site) requests = requests.Where(r => r.SiteId == site);
        // Finished: decided, or completed/closed. Finished with no recorded time (imported history) can't be judged, so it isn't counted.
        var response = (await requests.Select(r => new { r.Priority, Deadline = r.RespondBy!.Value, Done = r.DecidedAt,
                    Finished = r.Status == WorkRequestStatus.Approved || r.Status == WorkRequestStatus.Closed || r.Status == WorkRequestStatus.Rejected }).ToListAsync(ct))
            .Concat(await Wo(db, a).Where(w => w.WorkRequestId == null && w.Status != WorkOrderStatus.Cancelled && w.StartBy >= s && w.StartBy < e)
                .Select(w => new { w.Priority, Deadline = w.StartBy!.Value, Done = w.RespondedAt,
                    Finished = w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed }).ToListAsync(ct))
            .Select(x => (x.Priority, x.Deadline, Met: Outcome(x.Deadline, x.Done, x.Finished, now))).Where(x => x.Met is not null).ToList();
        var completion = (await Wo(db, a).Where(w => w.PmScheduleId == null && w.Status != WorkOrderStatus.Cancelled && w.DueAt >= s && w.DueAt < e)
                .Select(w => new { w.Priority, Deadline = w.DueAt!.Value, Done = w.CompletedAt,
                    Finished = w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed }).ToListAsync(ct))
            .Select(x => (x.Priority, x.Deadline, Met: Outcome(x.Deadline, x.Done, x.Finished, now))).Where(x => x.Met is not null).ToList();

        var byPriority = a.GroupBy == "priority";
        var groups = byPriority
            ? Enum.GetValues<WorkPriority>().OrderByDescending(p => p).Select(p => (Label: Lists.Name(a.Settings, p), Match: (Func<WorkPriority, DateTimeOffset, bool>)((pr, _) => pr == p))).ToList()
            : Months(a).Select(m => (Label: MonthLabel(m.Y, m.M), Match: (Func<WorkPriority, DateTimeOffset, bool>)((_, d) => d.Year == m.Y && d.Month == m.M))).ToList();
        var key = byPriority ? "priority" : "month";
        var rows = groups.Select(g =>
        {
            var r = response.Where(x => g.Match(x.Priority, x.Deadline)).ToList();
            var c = completion.Where(x => g.Match(x.Priority, x.Deadline)).ToList();
            int rm = r.Count(x => x.Met == true), cm = c.Count(x => x.Met == true);
            return Row((key, g.Label), ("responseMet", rm), ("responseTotal", r.Count), ("responsePct", Pct(rm, r.Count)),
                ("completionMet", cm), ("completionTotal", c.Count), ("completionPct", Pct(cm, c.Count)));
        }).ToList();
        int allRm = response.Count(x => x.Met == true), allCm = completion.Count(x => x.Met == true);
        return new("targets-met", "Targets met", Period(a),
            [new(key, byPriority ? "Priority" : "Month"), new("responseMet", "Responded in time", "number"), new("responseTotal", "With a response target", "number"),
             new("responsePct", "Response %", "percent"), new("completionMet", "Completed in time", "number"), new("completionTotal", "With a due time", "number"),
             new("completionPct", "Completion %", "percent")],
            rows, new Chart("bar", key, ["responsePct", "completionPct"], false),
            [new("Response targets met", Pct(allRm, response.Count), "percent"), new("Completion targets met", Pct(allCm, completion.Count), "percent"),
             new("Missed responses", response.Count - allRm), new("Missed completions", completion.Count - allCm)]);
    }

    /// <summary>Met (done in time), missed (done late, or not done and past), or null while still pending or when finished with no time recorded.</summary>
    private static bool? Outcome(DateTimeOffset deadline, DateTimeOffset? done, bool finished, DateTimeOffset now) =>
        done is { } d ? d <= deadline : finished ? null : deadline < now ? false : null;

    private static async Task<ReportResult> ThroughputAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var created = await Wo(db, a).Where(w => w.CreatedAt >= s && w.CreatedAt < e && w.Status != WorkOrderStatus.Cancelled).Select(w => w.CreatedAt).ToListAsync(ct);
        var done = await Wo(db, a).Where(w => w.CompletedAt >= s && w.CompletedAt < e).Select(w => new { w.CompletedAt, w.CreatedAt, w.DueDate }).ToListAsync(ct);
        var rows = Months(a).Select(m =>
        {
            var c = created.Count(x => x.Year == m.Y && x.Month == m.M);
            var d = done.Where(x => x.CompletedAt!.Value.Year == m.Y && x.CompletedAt.Value.Month == m.M).ToList();
            var withDue = d.Where(x => x.DueDate is not null).ToList();
            var onTime = withDue.Count(x => DateOnly.FromDateTime(x.CompletedAt!.Value.UtcDateTime) <= x.DueDate);
            return Row(("month", MonthLabel(m.Y, m.M)), ("created", c), ("completed", d.Count), ("onTimePercent", Pct(onTime, withDue.Count)),
                ("avgDays", d.Count == 0 ? null : Math.Round((decimal)d.Average(x => (x.CompletedAt!.Value - x.CreatedAt).TotalDays), 1)));
        }).ToList();
        var allDue = done.Where(x => x.DueDate is not null).ToList();
        return new("wo-throughput", "Work order throughput", Period(a),
            [new("month", "Month"), new("created", "Raised", "number"), new("completed", "Completed", "number"), new("onTimePercent", "On time", "percent"), new("avgDays", "Avg. days to complete", "number")],
            rows, new Chart("bar", "month", ["created", "completed"]),
            [new("Raised", created.Count), new("Completed", done.Count),
             new("On time", Pct(allDue.Count(x => DateOnly.FromDateTime(x.CompletedAt!.Value.UtcDateTime) <= x.DueDate), allDue.Count), "percent"),
             new("Avg. days to complete", done.Count == 0 ? null : Math.Round(done.Average(x => (x.CompletedAt!.Value - x.CreatedAt).TotalDays), 1))]);
    }

    private static async Task<ReportResult> PmComplianceAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var items = await Wo(db, a).Where(w => w.PmScheduleId != null && (w.PmDueDate ?? w.DueDate) >= a.From && (w.PmDueDate ?? w.DueDate) <= a.To)
            .Select(w => new { Due = (w.PmDueDate ?? w.DueDate)!.Value, w.CompletedAt, w.Status, w.PmScheduleId, Schedule = db.PmSchedules.Where(p => p.Id == w.PmScheduleId).Select(p => p.Code + " " + p.Name).FirstOrDefault(),
                Trade = w.Trade!.Name, Category = w.Asset!.Category!.Name }).ToListAsync(ct);
        string Outcome(DateOnly due, DateTimeOffset? completed, WorkOrderStatus st) =>
            st == WorkOrderStatus.Cancelled ? "skipped"
            : completed is { } c ? (DateOnly.FromDateTime(c.UtcDateTime) <= due ? "onTime" : "late")
            : due < Today ? "missed" : "pending";
        var tagged = items.Select(x => new { x.Due, x.Schedule, x.Trade, x.Category, O = Outcome(x.Due, x.CompletedAt, x.Status) }).ToList();
        var groups = a.GroupBy switch
        {
            "schedule" => tagged.GroupBy(x => x.Schedule ?? "—").OrderBy(g => g.Key).Select(g => (g.Key, g.ToList())),
            "trade" => tagged.GroupBy(x => x.Trade ?? "No trade").OrderBy(g => g.Key).Select(g => (g.Key, g.ToList())),
            "category" => tagged.GroupBy(x => x.Category ?? "Uncategorised").OrderBy(g => g.Key).Select(g => (g.Key, g.ToList())),
            _ => Months(a).Select(m => (MonthLabel(m.Y, m.M), tagged.Where(x => x.Due.Year == m.Y && x.Due.Month == m.M).ToList())),
        };
        var rows = groups.Select(g =>
        {
            int C(string o) => g.Item2.Count(x => x.O == o);
            return Row(("group", g.Item1), ("due", g.Item2.Count), ("onTime", C("onTime")), ("late", C("late")), ("missed", C("missed")), ("pending", C("pending")), ("skipped", C("skipped")),
                ("compliance", Pct(C("onTime"), C("onTime") + C("late") + C("missed"))));
        }).ToList();
        int T(string o) => tagged.Count(x => x.O == o);
        var label = a.GroupBy switch { "schedule" => "PM schedule", "trade" => "Trade", "category" => "Asset category", _ => "Month due" };
        return new("pm-compliance", "PM compliance", Period(a),
            [new("group", label), new("due", "Due", "number"), new("onTime", "On time", "number"), new("late", "Late", "number"), new("missed", "Missed", "number"),
             new("pending", "Not yet due", "number"), new("skipped", "Cancelled", "number"), new("compliance", "Compliance", "percent")],
            rows, new Chart("bar", "group", ["onTime", "late", "missed"], true),
            [new("PM compliance", Pct(T("onTime"), T("onTime") + T("late") + T("missed")), "percent"), new("Due in period", tagged.Count), new("Completed late", T("late")), new("Missed", T("missed"))]);
    }

    private static async Task<ReportResult> LaborAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var q = from l in db.WorkOrderLabor.AsNoTracking()
                join w in Wo(db, a) on l.WorkOrderId equals w.Id
                where l.WorkDate >= a.From && l.WorkDate <= a.To
                select new { l.EmployeeId, Employee = l.Employee!.FullName, l.Employee.IsContractor, l.Employee.TradeIds, Trade = w.Trade!.Name, Team = w.Team!.Name, w.Type, l.WorkOrderId, l.Hours, l.OvertimeHours, Cost = l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate };
        var xs = await q.ToListAsync(ct);
        var groups = a.GroupBy switch
        {
            "trade" => xs.GroupBy(x => x.Trade ?? "No trade"),
            "team" => xs.GroupBy(x => x.Team ?? "No team"),
            "type" => xs.GroupBy(x => Lists.Name(a.Settings, x.Type)),
            _ => xs.GroupBy(x => x.Employee + (x.IsContractor ? " (contractor)" : "")),
        };
        var rows = groups.Select(g => Row(("group", g.Key), ("hours", g.Sum(x => x.Hours)), ("overtime", g.Sum(x => x.OvertimeHours)), ("total", g.Sum(x => x.Hours + x.OvertimeHours)),
            ("cost", R2(g.Sum(x => x.Cost))), ("workOrders", g.Select(x => x.WorkOrderId).Distinct().Count()))).OrderByDescending(r => (decimal)r["total"]!).ToList();
        var label = a.GroupBy switch { "trade" => "Trade (work order)", "team" => "Team (work order)", "type" => "Work type", _ => "Employee" };
        var hrs = xs.Sum(x => x.Hours + x.OvertimeHours);
        return new("labor-hours", "Labour hours", Period(a),
            [new("group", label), new("hours", "Normal hours", "hours"), new("overtime", "Overtime", "hours"), new("total", "Total hours", "hours"), new("cost", "Labour cost", "currency"), new("workOrders", "Work orders", "number")],
            rows, new Chart("bar", "group", ["hours", "overtime"], true),
            [new("Total hours", hrs, "hours"), new("Overtime share", Pct(xs.Sum(x => x.OvertimeHours), hrs), "percent"), new("Labour cost", R2(xs.Sum(x => x.Cost)), "currency"), new("Work orders", xs.Select(x => x.WorkOrderId).Distinct().Count())]);
    }

    // ---------------------------------------------------------------- reliability
    private sealed record Failure(Guid AssetId, string AssetNumber, string AssetName, string? Code, string? CodeName, decimal Downtime, double? RepairHours, decimal? CostPerHour);

    private static async Task<List<Failure>> FailuresAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var xs = await Wo(db, a).Where(w => w.AssetId != null && w.CreatedAt >= s && w.CreatedAt < e && w.Status != WorkOrderStatus.Cancelled && (w.Type == WorkType.Breakdown || w.AssetWasDown))
            .Select(w => new { w.Id, AssetId = w.AssetId!.Value, w.Asset!.AssetNumber, w.Asset.Name, w.Asset.DowntimeCostPerHour, Code = w.FailureCode!.Code, CodeName = w.FailureCode.Name, w.DowntimeHours, w.StartedAt, w.CompletedAt, w.CreatedAt, w.DowntimeEventId })
            .ToListAsync(ct);
        // Work orders sharing one stop (two requests about the same stop) carry its hours once: on the one completed first.
        var counted = xs.Where(x => x.DowntimeEventId != null).GroupBy(x => x.DowntimeEventId)
            .Select(g => g.OrderBy(x => x.CompletedAt ?? DateTimeOffset.MaxValue).ThenBy(x => x.CreatedAt).First().Id).ToHashSet();
        return xs.Select(x => new Failure(x.AssetId, x.AssetNumber, x.Name, x.Code, x.CodeName, x.DowntimeEventId is null || counted.Contains(x.Id) ? x.DowntimeHours ?? 0 : 0,
            x.CompletedAt is { } c ? (c - (x.StartedAt ?? x.CreatedAt)).TotalHours : null, x.DowntimeCostPerHour)).ToList();
    }

    // Since 0.8.0: breakdowns are stops that began in the period (plus work done the old way, "Asset was down" with hours typed and no stop),
    // and downtime and planned time count only each asset's running days and hours, in the organization's time zone.
    private sealed record AssetClock(Guid Id, string AssetNumber, string Name, decimal RunsHoursPerDay, List<DayOfWeek> RunsOn, decimal? DowntimeCostPerHour);

    private static async Task<(TimeZoneInfo Tz, DateTimeOffset Start, DateTimeOffset End)> LocalPeriodAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var tz = Downtime.Zone((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone);
        return (tz, FourC.Cmms.Core.Assets.DowntimeMath.Midnight(a.From, tz), FourC.Cmms.Core.Assets.DowntimeMath.Midnight(a.To.AddDays(1), tz));
    }

    private static async Task<ReportResult> ReliabilityAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (tz, s, e) = await LocalPeriodAsync(db, a, ct);
        var assets = db.Assets.AsNoTracking();
        if (a.SiteId is { } site) assets = assets.Where(x => x.SiteId == site);
        if (a.AssetGroupId is { } g) { var members = AssetGroupEndpoints.Members(db, g); assets = assets.Where(x => members.Contains(x.Id)); }
        var clocks = await assets
            .Where(x => db.DowntimeEvents.Any(d => d.AssetId == x.Id && d.StartedAt < e && (d.EndedAt == null || d.EndedAt > s))
                     || db.WorkOrders.Any(w => w.AssetId == x.Id && w.AssetWasDown && w.DowntimeEventId == null && w.CompletedAt >= s && w.CompletedAt < e))
            // The asset's cost per hour, else its category's (0.14.0).
            .Select(x => new AssetClock(x.Id, x.AssetNumber, x.Name, x.RunsHoursPerDay, x.RunsOn, x.DowntimeCostPerHour ?? (x.Category == null ? null : x.Category.DowntimeCostPerHour))).ToListAsync(ct);
        var history = await Downtime.LoadAsync(db, clocks.Select(x => x.Id).ToList(), a.From, a.To, tz, ct);
        var now = DateTimeOffset.UtcNow;
        var measured = clocks.Select(x =>
        {
            var h = history[x.Id];
            var m = FourC.Cmms.Core.Assets.DowntimeMath.Measure(h.Stops, h.Typed, a.From, a.To, now, x.RunsHoursPerDay, x.RunsOn, tz);
            return (Asset: x, Planned: m.PlannedHours, Down: m.DownHours, Breakdowns: h.Stops.Count(st => st.Start >= s && st.Start < e) + h.OldBreakdowns.Count);
        }).ToList();
        var rows = measured.Select(x => Row(("asset", $"{x.Asset.AssetNumber} · {x.Asset.Name}"), ("assetId", x.Asset.Id), ("failures", x.Breakdowns), ("downtime", x.Down),
                ("mttr", x.Breakdowns == 0 ? null : (decimal?)Math.Round(x.Down / x.Breakdowns, 1)),
                ("mtbf", x.Breakdowns == 0 ? null : (decimal?)Math.Round(Math.Max(x.Planned - x.Down, 0) / x.Breakdowns, 1)),
                ("availability", Pct(Math.Max(x.Planned - x.Down, 0), x.Planned)),
                ("downtimeCost", x.Asset.DowntimeCostPerHour is { } cph ? R2(cph * x.Down) : null)))
            .OrderByDescending(r => (int)r["failures"]!).ThenByDescending(r => (decimal)r["downtime"]!).Take(100).ToList();
        var breakdowns = measured.Sum(x => x.Breakdowns);
        var down = measured.Sum(x => x.Down);
        return new("reliability", "Asset reliability", $"{Period(a)} · breakdowns are stops that began in the period; downtime counts each asset's running time only",
            [new("asset", "Asset"), new("failures", "Breakdowns", "number"), new("downtime", "Downtime (h)", "hours"), new("mttr", "MTTR (h)", "hours"), new("mtbf", "MTBF (h)", "hours"),
             new("availability", "Availability", "percent"), new("downtimeCost", "Downtime cost", "currency")],
            rows, new Chart("bar", "asset", ["failures"]),
            [new("Breakdowns", breakdowns), new("Assets affected", measured.Count(x => x.Breakdowns > 0 || x.Down > 0)), new("Total downtime (h)", down, "hours"),
             new("Overall MTTR (h)", breakdowns == 0 ? null : Math.Round(down / breakdowns, 1), "hours")]);
    }

    private static async Task<ReportResult> DowntimeLogAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (tz, s, e) = await LocalPeriodAsync(db, a, ct);
        var q = db.DowntimeEvents.AsNoTracking().Where(d => d.StartedAt < e && (d.EndedAt == null || d.EndedAt > s));
        if (a.SiteId is { } site) q = q.Where(d => d.Asset!.SiteId == site);
        if (a.AssetGroupId is { } g) { var members = AssetGroupEndpoints.Members(db, g); q = q.Where(d => members.Contains(d.AssetId)); }
        const int most = 5000;
        var stops = await q.OrderBy(d => d.StartedAt).Take(most + 1)
            .Select(d => new { d.AssetId, d.Asset!.AssetNumber, AssetName = d.Asset.Name, d.Asset.RunsHoursPerDay, d.Asset.RunsOn, d.StartedAt, d.EndedAt, d.Reason, d.SourceNumber })
            .ToListAsync(ct);
        var cut = stops.Count > most;
        if (cut) stops = stops.Take(most).ToList();
        var now = DateTimeOffset.UtcNow;
        var byHand = Translator.T("By hand");
        var rows = stops.Select(x => Row(("asset", $"{x.AssetNumber} · {x.AssetName}"), ("assetId", x.AssetId), ("start", x.StartedAt), ("end", x.EndedAt),
            ("hours", FourC.Cmms.Core.Assets.DowntimeMath.Measure([new(x.StartedAt, x.EndedAt)], [], a.From, a.To, now, x.RunsHoursPerDay, x.RunsOn, tz).DownHours),
            ("reason", x.Reason), ("from", x.SourceNumber ?? byHand))).ToList();
        return new("downtime", "Downtime log", cut ? $"{Period(a)} · hours count each asset's running time only · the first 5,000 stops" : $"{Period(a)} · hours count each asset's running time only",
            [new("asset", "Asset"), new("start", "Start", "datetime"), new("end", "End", "datetime"), new("hours", "Hours", "hours"), new("reason", "Reason"), new("from", "Source")],
            rows, null,
            [new("Stops", rows.Count), new("Assets stopped", stops.Select(x => x.AssetId).Distinct().Count()), new("Total hours", rows.Sum(r => (decimal)r["hours"]!), "hours")]);
    }

    private static async Task<ReportResult> SatisfactionAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (from, to) = (Start(a.From), End(a.To));
        // Through the requests this person can see (0.9.0 access).
        var requests = a.SiteId is { } s ? db.WorkRequests.AsNoTracking().Where(r => r.SiteId == s) : db.WorkRequests.AsNoTracking();
        var answers = await db.RequestFeedback.AsNoTracking().Where(f => f.AnsweredAt >= from && f.AnsweredAt < to)
            .Join(requests, f => f.WorkRequestId, r => r.Id, (f, r) => new
            {
                f.Accepted, f.Rating, f.AnsweredAt, Site = r.Site!.Name, Type = r.RequestType!.Name,
                Trade = db.WorkOrders.Where(w => w.Id == f.WorkOrderId).Select(w => w.Trade!.Name).FirstOrDefault(),
                Team = db.WorkOrders.Where(w => w.Id == f.WorkOrderId).Select(w => w.Team!.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        string Key(DateTimeOffset at, string? site, string? type, string? trade, string? team) => a.GroupBy switch
        {
            "type" => type ?? "No type", "trade" => trade ?? "No trade", "team" => team ?? "No team", "site" => site ?? "",
            _ => MonthLabel(at.Year, at.Month),
        };
        var groups = answers.GroupBy(x => Key(x.AnsweredAt, x.Site, x.Type, x.Trade, x.Team)).ToDictionary(g => g.Key, g => g.ToList());
        var order = a.GroupBy is null or "month"
            ? Months(a).Select(m => MonthLabel(m.Y, m.M)).Where(groups.ContainsKey).ToList()
            : groups.Keys.OrderByDescending(k => groups[k].Count).ThenBy(k => k).ToList();
        decimal? Avg(IEnumerable<int?> ratings) => ratings.OfType<int>().ToList() is { Count: > 0 } r ? Math.Round((decimal)r.Average(), 1) : null;
        var rows = order.Select(k => Row(("group", k), ("answers", groups[k].Count), ("accepted", groups[k].Count(x => x.Accepted)), ("refused", groups[k].Count(x => !x.Accepted)),
            ("rating", Avg(groups[k].Select(x => x.Rating))))).ToList();
        return new("requester-satisfaction", "Requester satisfaction", Period(a),
            [new("group", a.GroupBy switch { "type" => "Request type", "trade" => "Trade", "team" => "Team", "site" => "Site", _ => "Month" }), new("answers", "Answers", "number"),
             new("accepted", "Accepted", "number"), new("refused", "Refused", "number"), new("rating", "Average rating", "number")],
            rows, new Chart("bar", "group", ["accepted", "refused"], Stacked: true),
            [new("Answers", answers.Count), new("Average rating", Avg(answers.Select(x => x.Rating))), new("Accepted", Pct(answers.Count(x => x.Accepted), answers.Count), "percent")]);
    }

    private static async Task<ReportResult> CancelledAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (from, to) = (Start(a.From), End(a.To));
        // Work that is cancelled now, counted on the day of its (last) cancellation.
        var cancelled = await Wo(db, a).Where(w => w.Status == WorkOrderStatus.Cancelled)
            .Select(w => new
            {
                Reason = w.CancelReason!.Name, Trade = w.Trade!.Name, Site = w.Site!.Name,
                At = db.WorkOrderStatusChanges.Where(c => c.WorkOrderId == w.Id && c.ToStatus == WorkOrderStatus.Cancelled).Max(c => (DateTimeOffset?)c.ChangedAt),
            }).Where(x => x.At >= from && x.At < to).ToListAsync(ct);
        string Key(DateTimeOffset at, string? reason, string? trade, string? site) => a.GroupBy switch
        {
            "month" => MonthLabel(at.Year, at.Month), "trade" => trade ?? "No trade", "site" => site ?? "", _ => reason ?? "No reason",
        };
        var groups = cancelled.GroupBy(x => Key(x.At!.Value, x.Reason, x.Trade, x.Site)).ToDictionary(g => g.Key, g => g.Count());
        var order = a.GroupBy == "month"
            ? Months(a).Select(m => MonthLabel(m.Y, m.M)).Where(groups.ContainsKey).ToList()
            : groups.Keys.OrderByDescending(k => groups[k]).ThenBy(k => k).ToList();
        var rows = order.Select(k => Row(("group", k), ("cancelled", groups[k]), ("share", Pct(groups[k], cancelled.Count)))).ToList();
        return new("cancelled-work", "Cancelled work", Period(a),
            [new("group", a.GroupBy switch { "month" => "Month", "trade" => "Trade", "site" => "Site", _ => "Reason" }), new("cancelled", "Cancelled", "number"),
             new("share", "Share", "percent")],
            rows, new Chart("bar", "group", ["cancelled"]),
            [new("Cancelled", cancelled.Count), new("Without a reason", cancelled.Count(x => x.Reason is null))]);
    }

    /// <summary>Measurement readings (0.13.0) at an alert or alarm level in the period, newest first, through the readings the person can see.</summary>
    private static async Task<ReportResult> OutOfLimitsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (from, to) = (Start(a.From), End(a.To));
        var q = db.MeasurementReadings.AsNoTracking().Where(r => r.Level != FourC.Cmms.Core.Pm.MeasureLevel.Normal && r.ReadAt >= from && r.ReadAt < to);
        if (a.SiteId is { } site) q = q.Where(r => r.SiteId == site);
        const int most = 5000;
        var list = await q.OrderByDescending(r => r.ReadAt).Take(most + 1).Select(r => new
        {
            r.ReadAt, r.Value, r.Level, r.AssetId, r.PointId, Asset = db.Assets.Where(x => x.Id == r.AssetId).Select(x => x.AssetNumber + " · " + x.Name).FirstOrDefault(),
            WorkOrder = db.WorkOrders.Where(w => w.Id == r.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
            FollowUp = db.WorkOrders.Where(w => w.Id == r.FollowUpWorkOrderId).Select(w => w.Number).FirstOrDefault()
                ?? db.WorkRequests.Where(w => w.Id == r.FollowUpRequestId).Select(w => w.Number).FirstOrDefault(),
        }).ToListAsync(ct);
        var cut = list.Count > most;
        if (cut) list = list.Take(most).ToList();
        // Points looked up on their own: IgnoreQueryFilters inside the readings query would lift the site filter from all of it.
        var points = (await Checklists.PointsAsync(db, list.Select(x => (Guid?)x.PointId), ct)).ToDictionary(p => p.Id);
        var rows = list.Select(x => Row(("date", x.ReadAt), ("asset", x.Asset), ("assetId", x.AssetId),
            ("point", points.TryGetValue(x.PointId, out var pt) ? $"{pt.Code} · {pt.Name}" : null), ("value", x.Value), ("unit", points.GetValueOrDefault(x.PointId)?.Unit),
            ("level", Translator.T(Checklists.LevelWord(x.Level))), ("workOrder", x.WorkOrder), ("followUp", x.FollowUp))).ToList();
        var alarms = list.Count(x => x.Level is FourC.Cmms.Core.Pm.MeasureLevel.AlarmHigh or FourC.Cmms.Core.Pm.MeasureLevel.AlarmLow);
        return new("out-of-limits", "Out of limits", cut ? $"{Period(a)} · the latest 5,000 readings" : Period(a),
            [new("date", "Read at", "datetime"), new("asset", "Asset"), new("point", "Measurement point"), new("value", "Value", "number"), new("unit", "Unit"),
             new("level", "Level"), new("workOrder", "Work order"), new("followUp", "Follow-up")],
            rows, null,
            [new("Alarms", alarms), new("Alerts", list.Count - alarms), new("Follow-ups raised", list.Count(x => x.FollowUp is not null))]);
    }

    private static async Task<ReportResult> FailureParetoAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var fs = await FailuresAsync(db, a, ct);
        var total = fs.Count;
        var cumulative = 0;
        var rows = fs.GroupBy(f => f.Code is null ? "Not coded" : $"{f.Code} · {f.CodeName}").OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(f => f.Downtime)).Select(g =>
        {
            cumulative += g.Count();
            return Row(("code", g.Key), ("failures", g.Count()), ("percent", Pct(g.Count(), total)), ("cumulative", Pct(cumulative, total)), ("downtime", g.Sum(f => f.Downtime)),
                ("assets", g.Select(f => f.AssetId).Distinct().Count()));
        }).ToList();
        return new("failure-pareto", "Failure analysis", Period(a),
            [new("code", "Failure code"), new("failures", "Breakdowns", "number"), new("percent", "Share", "percent"), new("cumulative", "Cumulative", "percent"),
             new("downtime", "Downtime (h)", "hours"), new("assets", "Assets", "number")],
            rows, new Chart("bar", "code", ["failures"]),
            [new("Breakdowns", total), new("Coded", Pct(fs.Count(f => f.Code is not null), total), "percent"), new("Distinct causes", rows.Count)]);
    }

    /// <summary>The failure tree's work (0.14.0): a symptom, cause or remedy recorded, or a breakdown (also the work order list's inFailureTree filter).</summary>
    internal static IQueryable<WorkOrder> InFailureTree(IQueryable<WorkOrder> q) =>
        q.Where(w => w.SymptomId != null || w.FailureCodeId != null || w.RemedyId != null || w.Type == WorkType.Breakdown || w.AssetWasDown);

    public const string NotRecorded = "Not recorded";

    private static async Task<ReportResult> FailureTreeAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var wos = InFailureTree(Wo(db, a).Where(w => w.CompletedAt >= s && w.CompletedAt < e));
        var items = await wos.Select(w => new
        {
            w.Id, w.SymptomId, SymptomCode = w.Symptom!.Code, SymptomName = w.Symptom.Name, w.FailureCodeId, CauseCode = w.FailureCode!.Code, CauseName = w.FailureCode.Name,
            w.RemedyId, RemedyCode = w.Remedy!.Code, RemedyName = w.Remedy.Name, w.DowntimeHours, w.DowntimeEventId, w.CompletedAt, w.CreatedAt,
        }).ToListAsync(ct);
        var costs = (await CostLinesAsync(db, wos, null, null, ct)).GroupBy(c => c.WoId)
            .ToDictionary(g => g.Key, g => (Hours: g.Sum(x => x.Hours), Cost: g.Sum(x => x.Total)));
        // Work orders sharing one stop carry its hours once, as in the failure analysis.
        var counted = items.Where(x => x.DowntimeEventId != null).GroupBy(x => x.DowntimeEventId)
            .Select(g => g.OrderBy(x => x.CompletedAt ?? DateTimeOffset.MaxValue).ThenBy(x => x.CreatedAt).First().Id).ToHashSet();
        decimal Down(Guid id, Guid? stop, decimal? hours) => stop is null || counted.Contains(id) ? hours ?? 0 : 0;
        // tab=all: the list page shows every status (its default tab is open work only); the API ignores it.
        var query = $"tab=all&inFailureTree=true&completedFrom={a.From:yyyy-MM-dd}&completedTo={a.To:yyyy-MM-dd}" + (a.SiteId is { } site ? $"&siteId={site}" : "");
        static string Q(Guid? id) => id?.ToString() ?? "none";
        var rows = new List<Dictionary<string, object?>>();
        void Line<T>(int level, string node, List<T> group, Func<T, Guid> id, Func<T, Guid?> stop, Func<T, decimal?> hours, string filter) => rows.Add(Row(
            ("node", node), ("workOrders", group.Count),
            ("hours", R2(group.Sum(x => costs.GetValueOrDefault(id(x)).Hours))), ("downtime", R2(group.Sum(x => Down(id(x), stop(x), hours(x))))),
            ("cost", R2(group.Sum(x => costs.GetValueOrDefault(id(x)).Cost))), ("level", level), ("listQuery", $"{query}&{filter}")));
        static IEnumerable<(List<T> Items, string Label, Guid? Key)> Groups<T>(IEnumerable<T> xs, Func<T, Guid?> key, Func<T, string> label) =>
            xs.GroupBy(key).Select(g => (Items: g.ToList(), Label: g.Key is null ? NotRecorded : label(g.First()), Key: g.Key))
                .OrderByDescending(g => g.Items.Count).ThenBy(g => g.Key is null).ThenBy(g => g.Label, StringComparer.CurrentCulture);
        foreach (var sg in Groups(items, x => x.SymptomId, x => $"{x.SymptomCode} · {x.SymptomName}"))
        {
            var sf = $"symptomId={Q(sg.Key)}";
            Line(0, sg.Label, sg.Items, x => x.Id, x => x.DowntimeEventId, x => x.DowntimeHours, sf);
            foreach (var cg in Groups(sg.Items, x => x.FailureCodeId, x => $"{x.CauseCode} · {x.CauseName}"))
            {
                var cf = $"{sf}&causeId={Q(cg.Key)}";
                Line(1, cg.Label, cg.Items, x => x.Id, x => x.DowntimeEventId, x => x.DowntimeHours, cf);
                foreach (var rg in Groups(cg.Items, x => x.RemedyId, x => $"{x.RemedyCode} · {x.RemedyName}"))
                    Line(2, rg.Label, rg.Items, x => x.Id, x => x.DowntimeEventId, x => x.DowntimeHours, $"{cf}&remedyId={Q(rg.Key)}");
            }
        }
        // The first column's fixed words ("Not recorded") are translated here, at every level.
        foreach (var row in rows.Where(r => (string)r["node"]! == NotRecorded)) row["node"] = Translator.T(NotRecorded);
        return new("failure-tree", "Failure tree", Period(a),
            [new("node", "Symptom → cause → remedy"), new("workOrders", "Work orders", "number"), new("hours", "Labour hours", "hours"), new("downtime", "Downtime (h)", "hours"),
             new("cost", "Cost", "currency")],
            rows, new Chart("bar", "node", ["workOrders"]),
            [new("Work orders", items.Count), new("With a symptom", Pct(items.Count(x => x.SymptomId != null), items.Count), "percent"),
             new("With a cause", Pct(items.Count(x => x.FailureCodeId != null), items.Count), "percent"), new("With a remedy", Pct(items.Count(x => x.RemedyId != null), items.Count), "percent")]);
    }

    private static async Task<ReportResult> ResponsivenessAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var now = DateTimeOffset.UtcNow;
        const int most = 5000;
        var rq = db.WorkRequests.AsNoTracking().Where(r => r.Status != WorkRequestStatus.Cancelled && r.RespondBy >= s && r.RespondBy < e);
        if (a.SiteId is { } site) rq = rq.Where(r => r.SiteId == site);
        var requests = await rq.OrderBy(r => r.RespondBy).Take(most).Select(r => new
        {
            r.Id, r.Number, r.Title, r.Priority, r.CreatedAt, RespondBy = r.RespondBy!.Value, r.DecidedAt,
            Finished = r.Status == WorkRequestStatus.Approved || r.Status == WorkRequestStatus.Closed || r.Status == WorkRequestStatus.Rejected,
            WorkTrade = db.WorkOrders.Where(w => w.Id == r.WorkOrderId).Select(w => w.Trade!.Name).FirstOrDefault(),
            TypeTrade = db.Trades.Where(t => t.Id == r.RequestType!.DefaultTradeId).Select(t => t.Name).FirstOrDefault(),
        }).ToListAsync(ct);
        var works = await Wo(db, a).Where(w => w.Status != WorkOrderStatus.Cancelled
                && (w.WorkRequestId == null && w.StartBy >= s && w.StartBy < e || w.PmScheduleId == null && w.DueAt >= s && w.DueAt < e))
            .OrderBy(w => w.StartBy ?? w.DueAt).Take(most).Select(w => new
            {
                w.Id, w.Number, w.Title, w.Priority, Trade = w.Trade!.Name, w.CreatedAt, w.WorkRequestId, w.PmScheduleId, w.StartBy, w.RespondedAt, w.DueAt, w.CompletedAt,
                Finished = w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed,
            }).ToListAsync(ct);
        string? Word(DateTimeOffset deadline, DateTimeOffset? done, bool finished) => Outcome(deadline, done, finished, now) switch
        {
            true => "Met", false => "Missed", null => done is null && !finished ? "Pending" : null, // finished with no time recorded: not judged
        };
        static decimal? Hours(DateTimeOffset? done, DateTimeOffset from) => done is { } d ? Math.Round((decimal)(d - from).TotalHours, 1) : null;
        var orders = requests.Select(r => new Order(r.Number, r.Title, r.Priority, r.WorkTrade ?? r.TypeTrade, r.Id, null,
                r.RespondBy, r.DecidedAt, Hours(r.DecidedAt, r.CreatedAt), Word(r.RespondBy, r.DecidedAt, r.Finished), null, null, null, null))
            .Concat(works.Select(w =>
            {
                var respond = w.WorkRequestId == null && w.StartBy >= s && w.StartBy < e ? w.StartBy : null;
                var due = w.PmScheduleId == null && w.DueAt >= s && w.DueAt < e ? w.DueAt : null;
                return new Order(w.Number, w.Title, w.Priority, w.Trade, null, w.Id,
                    respond, respond is null ? null : w.RespondedAt, respond is null ? null : Hours(w.RespondedAt, w.CreatedAt), respond is { } rb ? Word(rb, w.RespondedAt, w.Finished) : null,
                    due, due is null ? null : w.CompletedAt, due is null ? null : Hours(w.CompletedAt, w.CreatedAt), due is { } d ? Word(d, w.CompletedAt, w.Finished) : null);
            }))
            .OrderBy(o => o.RespondBy ?? o.DueAt).ToList();
        decimal? Avg(IEnumerable<decimal?> xs) => xs.OfType<decimal>().ToList() is { Count: > 0 } l ? Math.Round(l.Average(), 1) : null;
        decimal? MetPct(IEnumerable<string?> xs) { var l = xs.Where(x => x is "Met" or "Missed").ToList(); return Pct(l.Count(x => x == "Met"), l.Count); }
        var summary = new List<Kpi>
        {
            new("Response targets met", MetPct(orders.Select(o => o.Response)), "percent"), new("Avg. response (h)", Avg(orders.Select(o => o.ResponseHours)), "hours"),
            new("Completion targets met", MetPct(orders.Select(o => o.Completion)), "percent"), new("Avg. completion (h)", Avg(orders.Select(o => o.CompletionHours)), "hours"),
        };
        var subtitle = requests.Count >= most || works.Count >= most ? $"{Period(a)} · the first 5,000" : Period(a);
        if (a.GroupBy is "priority" or "trade")
        {
            var byPriority = a.GroupBy == "priority";
            var groups = byPriority
                ? orders.GroupBy(o => o.Priority).OrderByDescending(g => g.Key).Select(g => (Label: Lists.Name(a.Settings, g.Key), Items: g.ToList()))
                : orders.GroupBy(o => o.Trade ?? "No trade").OrderBy(g => g.Key == "No trade").ThenBy(g => g.Key).Select(g => (Label: g.Key, Items: g.ToList()));
            var rows = groups.Select(g => Row(("group", g.Label), ("orders", g.Items.Count), ("responseAvg", Avg(g.Items.Select(o => o.ResponseHours))),
                ("responsePct", MetPct(g.Items.Select(o => o.Response))), ("completionAvg", Avg(g.Items.Select(o => o.CompletionHours))),
                ("completionPct", MetPct(g.Items.Select(o => o.Completion))))).ToList();
            return new("responsiveness", "Responsiveness", subtitle,
                [new("group", byPriority ? "Priority" : "Trade"), new("orders", "Orders", "number"), new("responseAvg", "Avg. response (h)", "hours"), new("responsePct", "Response %", "percent"),
                 new("completionAvg", "Avg. completion (h)", "hours"), new("completionPct", "Completion %", "percent")],
                rows, new Chart("bar", "group", ["responsePct", "completionPct"]), summary);
        }
        string? T(string? word) => word is null ? null : Translator.T(word);
        var list = orders.Select(o => Row(("number", o.Number), ("title", o.Title), ("priority", Lists.Name(a.Settings, o.Priority)), ("trade", o.Trade),
            ("respondBy", o.RespondBy), ("respondedAt", o.RespondedAt), ("responseHours", o.ResponseHours), ("response", T(o.Response)),
            ("dueAt", o.DueAt), ("completedAt", o.CompletedAt), ("completionHours", o.CompletionHours), ("completion", T(o.Completion)),
            ("requestId", o.RequestId), ("workOrderId", o.WorkOrderId))).ToList();
        return new("responsiveness", "Responsiveness", subtitle,
            [new("number", "Number"), new("title", "Title"), new("priority", "Priority"), new("trade", "Trade"), new("respondBy", "Respond by", "datetime"),
             new("respondedAt", "Responded", "datetime"), new("responseHours", "Response (h)", "hours"), new("response", "Response"), new("dueAt", "Due", "datetime"),
             new("completedAt", "Completed", "datetime"), new("completionHours", "Completion (h)", "hours"), new("completion", "Completion")],
            list, null, summary);
    }

    /// <summary>One request or work order in the responsiveness report.</summary>
    private sealed record Order(string Number, string Title, WorkPriority Priority, string? Trade, Guid? RequestId, Guid? WorkOrderId,
        DateTimeOffset? RespondBy, DateTimeOffset? RespondedAt, decimal? ResponseHours, string? Response,
        DateTimeOffset? DueAt, DateTimeOffset? CompletedAt, decimal? CompletionHours, string? Completion);

    // ---------------------------------------------------------------- costs
    /// <summary>A work order's labour, parts and other costs in one month (and its labour hours, 0.14.0).</summary>
    /// <summary>
    /// Costs of one work order in one month. <see cref="Contractor"/> (0.17.0) is contractor lines plus labour of employees linked to a company,
    /// moved out of <see cref="Labor"/> so nothing counts twice; <see cref="Hours"/> stays all logged labour hours.
    /// </summary>
    internal sealed record CostLine(Guid WoId, int Y, int M, decimal Labor, decimal Parts, decimal Other, decimal Hours = 0, decimal Contractor = 0, decimal ContractorHours = 0)
    {
        public decimal Total => Labor + Parts + Other + Contractor;
    }

    /// <summary>
    /// The cost lines of <paramref name="wos"/> per work order and month: labour by its work date, parts issued (net of returns) by their date,
    /// other costs by when they were added; <paramref name="from"/> and <paramref name="to"/> bound the dates (null: all).
    /// </summary>
    internal static async Task<List<CostLine>> CostLinesAsync(CmmsDbContext db, IQueryable<WorkOrder> wos, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var lq = from l in db.WorkOrderLabor.AsNoTracking() join w in wos on l.WorkOrderId equals w.Id select l;
        if (from is { } lf) lq = lq.Where(l => l.WorkDate >= lf);
        if (to is { } lt) lq = lq.Where(l => l.WorkDate <= lt);
        var labor = await lq.GroupBy(l => new { l.WorkOrderId, l.WorkDate.Year, l.WorkDate.Month })
            .Select(g => new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month,
                g.Sum(x => x.Employee!.CompanyId == null ? x.Hours * x.Rate + x.OvertimeHours * x.OvertimeRate : 0), 0, 0, g.Sum(x => x.Hours + x.OvertimeHours),
                g.Sum(x => x.Employee!.CompanyId != null ? x.Hours * x.Rate + x.OvertimeHours * x.OvertimeRate : 0), 0))
            .ToListAsync(ct);
        // Contractor lines (0.17.0), dated by the day the hours were done.
        var kq = from k in db.WorkOrderContractors.AsNoTracking() join w in wos on k.WorkOrderId equals w.Id where k.HoursDone > 0 && k.DoneOn != null select k;
        if (from is { } kf) kq = kq.Where(k => k.DoneOn >= kf);
        if (to is { } kt) kq = kq.Where(k => k.DoneOn <= kt);
        var contractor = await kq.GroupBy(k => new { k.WorkOrderId, k.DoneOn!.Value.Year, k.DoneOn.Value.Month })
            .Select(g => new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, 0, 0, 0, 0, g.Sum(x => x.HoursDone * x.Rate), g.Sum(x => x.HoursDone))).ToListAsync(ct);
        var tq = from t in db.StockTransactions.AsNoTracking() join w in wos on t.WorkOrderId equals w.Id
                 where t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return select t;
        if (from is { } tf) tq = tq.Where(t => t.Date >= tf);
        if (to is { } tt) tq = tq.Where(t => t.Date <= tt);
        var parts = await tq.GroupBy(t => new { WorkOrderId = t.WorkOrderId!.Value, t.Date.Year, t.Date.Month })
            .Select(g => new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, 0, g.Sum(x => -x.Quantity * x.UnitCost), 0, 0)).ToListAsync(ct);
        var cq = from c in db.WorkOrderCosts.AsNoTracking() join w in wos on c.WorkOrderId equals w.Id select c;
        if (from is { } cf) { var cs = Start(cf); cq = cq.Where(c => c.CreatedAt >= cs); }
        if (to is { } ctd) { var ce = End(ctd); cq = cq.Where(c => c.CreatedAt < ce); }
        var other = await cq.GroupBy(c => new { c.WorkOrderId, c.CreatedAt.Year, c.CreatedAt.Month })
            .Select(g => new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, 0, 0, g.Sum(x => x.Quantity * x.UnitCost), 0)).ToListAsync(ct);
        return labor.Concat(parts).Concat(other).Concat(contractor).ToList();
    }

    private static async Task<ReportResult> CostsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var wos = Wo(db, a);
        // A cost centre with its children (0.20.0).
        if (a.CostCentreId is { } cc) { var scope = await CostCentreQueries.SubtreeAsync(db, cc, ct); wos = wos.Where(w => w.CostCentreId != null && scope.Contains(w.CostCentreId.Value)); }
        var lines = await CostLinesAsync(db, wos, a.From, a.To, ct);
        var centrePaths = a.GroupBy == "costCentre" ? await CostCentreEndpoints.PathsAsync(db, ct) : [];
        var ids = lines.Select(l => l.WoId).Distinct().ToList();
        var dims = new Dictionary<Guid, (string Site, string Type, string Trade, string Team, string Asset, string Category, string CostCentre)>();
        foreach (var chunk in ids.Chunk(1000))
            foreach (var d in await db.WorkOrders.AsNoTracking().Where(w => chunk.Contains(w.Id)).Select(w => new { w.Id, Site = w.Site!.Name, w.Type, Trade = w.Trade!.Name,
                         Team = w.Team!.Name, Asset = w.Asset == null ? null : w.Asset.AssetNumber + " · " + w.Asset.Name, Category = w.Asset!.Category!.Name, w.CostCentreId }).ToListAsync(ct))
                dims[d.Id] = (d.Site, Lists.Name(a.Settings, d.Type), d.Trade ?? "No trade", d.Team ?? "No team", d.Asset ?? "No asset (location work)", d.Category ?? "Uncategorised",
                    d.CostCentreId is { } dc ? centrePaths.GetValueOrDefault(dc) ?? "No cost centre" : "No cost centre");
        string Key(CostLine l) => a.GroupBy switch
        {
            "site" => dims[l.WoId].Site, "type" => dims[l.WoId].Type, "trade" => dims[l.WoId].Trade, "team" => dims[l.WoId].Team, "category" => dims[l.WoId].Category,
            "month" => $"{l.Y:0000}-{l.M:00}", "costCentre" => dims[l.WoId].CostCentre, _ => dims[l.WoId].Asset,
        };
        var grouped = a.GroupBy == "technician" ? await ByTechnicianAsync(db, wos, a, ct) : lines.GroupBy(Key).Select(g => Row(("group", a.GroupBy == "month" ? MonthLabel(int.Parse(g.Key[..4]), int.Parse(g.Key[5..])) : g.Key), ("sortKey", g.Key),
            ("labor", R2(g.Sum(x => x.Labor))), ("parts", R2(g.Sum(x => x.Parts))), ("other", R2(g.Sum(x => x.Other))), ("contractor", R2(g.Sum(x => x.Contractor))),
            ("total", R2(g.Sum(x => x.Total))),
            ("workOrders", g.Select(x => x.WoId).Distinct().Count()), ("hours", R2(g.Sum(x => x.Hours)))));
        var rows = (a.GroupBy == "month" ? grouped.OrderBy(r => (string)r["sortKey"]!) : grouped.OrderByDescending(r => (decimal)r["total"]!)).Take(a.GroupBy == "asset" ? 200 : 1000).ToList();
        foreach (var r in rows) r.Remove("sortKey");
        var total = lines.Sum(x => x.Total);
        var label = a.GroupBy switch { "costCentre" => "Cost centre", "site" => "Site", "type" => "Work type", "trade" => "Trade", "team" => "Team", "category" => "Asset category", "technician" => "Technician", "month" => "Month", _ => "Asset" };
        return new("maintenance-costs", "Maintenance costs", Period(a),
            [new("group", label), new("labor", "Labour", "currency"), new("parts", "Parts", "currency"), new("other", "Other", "currency"), new("contractor", "Contractor", "currency"),
             new("total", "Total", "currency"), new("workOrders", "Work orders", "number"), new("hours", "Labour hours", "hours")],
            rows, new Chart("bar", "group", ["labor", "parts", "other", "contractor"], true),
            [new("Total cost", R2(total), "currency"), new("Labour", R2(lines.Sum(x => x.Labor)), "currency"), new("Parts", R2(lines.Sum(x => x.Parts)), "currency"), new("Other", R2(lines.Sum(x => x.Other)), "currency"),
             new("Contractor", R2(lines.Sum(x => x.Contractor)), "currency")]);
    }

    /// <summary>
    /// Contracts (0.17.0): each contract overlapping the period with the value of its periods overlapping it (in the contract's currency), the
    /// spend on work orders naming it dated in the period (base currency), the work orders completed in the period and the expected number.
    /// With a site: that site's and organization-wide contracts, counting only that site's work.
    /// </summary>
    private static async Task<ReportResult> ContractsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var today = await ContractEndpoints.TodayAsync(db, ct);
        var q = db.Contracts.AsNoTracking().Where(c => c.StartDate <= a.To && (c.EndDate >= a.From || (c.AutoRenew && c.TerminatedOn == null)));
        if (a.SiteId is { } site) q = q.Where(c => c.SiteId == null || c.SiteId == site);
        var contracts = await q.Include(c => c.Periods).OrderBy(c => c.Number)
            .Select(c => new { c, Company = c.Supplier!.Name, Type = c.ContractType == null ? null : c.ContractType.Name }).ToListAsync(ct);
        var ids = contracts.Select(x => x.c.Id).ToList();
        var wos = db.WorkOrders.AsNoTracking().Where(w => w.ContractId != null && ids.Contains(w.ContractId.Value));
        if (a.SiteId is { } s2) wos = wos.Where(w => w.SiteId == s2);
        var contractOf = await wos.Select(w => new { w.Id, w.ContractId, w.CompletedAt }).ToListAsync(ct);
        var byWork = contractOf.ToDictionary(w => w.Id, w => w.ContractId!.Value);
        var spend = (await CostLinesAsync(db, wos, a.From, a.To, ct)).GroupBy(l => byWork[l.WoId])
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Total));
        var (start, end) = (Start(a.From), End(a.To));
        var completed = contractOf.Where(w => w.CompletedAt >= start && w.CompletedAt < end).GroupBy(w => w.ContractId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var rows = contracts.Select(x =>
        {
            var c = x.c;
            var value = c.Periods.Where(p => p.StartDate <= a.To && p.EndDate >= a.From).Sum(p => p.Value ?? 0);
            // The part of the period the contract runs: from its start to its end (a renewing contract runs on), or the day before termination.
            var runsTo = c.TerminatedOn is { } t ? t.AddDays(-1) : c.AutoRenew ? a.To : c.EndDate;
            var (from, to) = (c.StartDate > a.From ? c.StartDate : a.From, runsTo < a.To ? runsTo : a.To);
            decimal? expected = c.ExpectedWorkOrders is { } n ? ContractTerms.ExpectedIn(n, c.ExpectedPer ?? ExpectedPer.Year, from, to) : null;
            return Row(("number", c.Number), ("title", c.Title), ("company", x.Company), ("type", x.Type),
                ("status", Translator.T(ContractTerms.StatusOn(today, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn).ToString())),
                ("value", R2(value)), ("currency", c.Currency), ("spend", R2(spend.GetValueOrDefault(c.Id))), ("completed", completed.GetValueOrDefault(c.Id)),
                ("expected", expected), ("contractId", c.Id));
        }).ToList();
        return new("contracts", "Contracts", Period(a),
            [new("number", "Number"), new("title", "Title"), new("company", "Company"), new("type", "Type"), new("status", "Status"), new("value", "Value", "number"),
             new("currency", "Currency"), new("spend", "Spend", "currency"), new("completed", "Completed", "number"), new("expected", "Expected", "number")],
            rows, null,
            [new("Contracts", rows.Count), new("Spend", R2(spend.Values.Sum()), "currency"), new("Completed", completed.Values.Sum())]);
    }

    /// <summary>Maintenance costs by technician (0.14.0): each person's labour hours and cost and the work orders they logged hours on; parts and other costs aren't per person.</summary>
    /// <summary>Labour by technician over the report's work orders (its site and cost-centre filters already applied).</summary>
    private static async Task<IEnumerable<Dictionary<string, object?>>> ByTechnicianAsync(CmmsDbContext db, IQueryable<WorkOrder> wos, ReportArgs a, CancellationToken ct)
    {
        var labour = await (from l in db.WorkOrderLabor.AsNoTracking() join w in wos on l.WorkOrderId equals w.Id
                            where l.WorkDate >= a.From && l.WorkDate <= a.To
                            select new { l.EmployeeId, l.WorkOrderId, Cost = l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate, Hours = l.Hours + l.OvertimeHours }).ToListAsync(ct);
        var ids = labour.Select(l => l.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AllSites().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => $"{e.FullName} · {e.Code}", ct);
        return labour.GroupBy(l => l.EmployeeId).Select(g => Row(("group", names.GetValueOrDefault(g.Key) ?? "—"), ("sortKey", names.GetValueOrDefault(g.Key) ?? "—"),
            ("labor", R2(g.Sum(x => x.Cost))), ("parts", null), ("other", null), ("total", R2(g.Sum(x => x.Cost))),
            ("workOrders", g.Select(x => x.WorkOrderId).Distinct().Count()), ("hours", R2(g.Sum(x => x.Hours)))));
    }

    // ---------------------------------------------------------------- assets
    private static async Task<ReportResult> PmAnnualPlanAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (first, last) = (new DateOnly(a.Year, 1, 1), new DateOnly(a.Year, 12, 31));
        var today = Today;
        var generated = await Wo(db, a).Where(w => w.PmScheduleId != null && (w.PmDueDate ?? w.DueDate) >= first && (w.PmDueDate ?? w.DueDate) <= last)
            .Select(w => new { Pm = w.PmScheduleId!.Value, Due = (w.PmDueDate ?? w.DueDate)!.Value, w.CompletedAt, w.Status, Key = w.GroupId ?? w.Id }).ToListAsync(ct);
        // As PM compliance: cancelled, done on time, late, missed (past due, not done) — work generated but not yet due is still planned.
        string Outcome(DateOnly due, DateTimeOffset? completed, WorkOrderStatus st) =>
            st == WorkOrderStatus.Cancelled ? "cancelled" : completed is { } c ? (DateOnly.FromDateTime(c.UtcDateTime) <= due ? "done" : "late") : due < today ? "missed" : "planned";
        static int Rank(string o) => o switch { "missed" => 4, "late" => 3, "planned" => 2, "done" => 1, _ => 0 };
        // A group's work orders are one visit: the worst outcome among them counts.
        var visits = generated.GroupBy(w => (w.Pm, w.Key)).Select(g => (g.Key.Pm, Due: g.Min(x => x.Due), Outcome: g.Select(x => Outcome(x.Due, x.CompletedAt, x.Status)).MaxBy(Rank)!))
            // Every visit of the year (no 400-per-PM or 5,000-row cap); a visit due before today that was never made is missed.
            .Concat((await PmEndpoints.ForecastAsync(db, first, last, a.SiteId, ct, perSchedule: 2000, total: int.MaxValue)).Where(f => f.Date >= first && f.Date <= last)
                .Select(f => (Pm: f.Id, Due: f.Date, Outcome: f.Date < today ? "missed" : "planned")))
            .ToList();
        var ids = visits.Select(v => v.Pm).Distinct().ToList();
        var pms = await db.PmSchedules.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, p.AssetId, AssetNumber = p.Asset!.AssetNumber, AssetName = p.Asset.Name, p.MultiAssetMode, Count = p.Assets.Count }).ToListAsync(ct);
        string Asset(Guid? assetId, string? number, string? name, MultiAssetMode mode, int count) => assetId is not null ? $"{number} · {name}"
            : count > 0 ? Translator.T(string.Format(mode == MultiAssetMode.Group ? "Group of {0} assets" : "Round of {0} assets", count)) : "—";
        var rows = pms.OrderBy(p => p.AssetNumber ?? "\uffff").ThenBy(p => p.Code).Select(p =>
        {
            var mine = visits.Where(v => v.Pm == p.Id).ToList();
            var row = Row(("asset", Asset(p.AssetId, p.AssetNumber, p.AssetName, p.MultiAssetMode, p.Count)), ("pm", $"{p.Code} · {p.Name}"), ("pmId", p.Id), ("assetId", p.AssetId));
            for (var m = 1; m <= 12; m++)
            {
                var month = mine.Where(v => v.Due.Month == m).ToList();
                int C(string o) => month.Count(v => v.Outcome == o);
                row[$"m{m}"] = month.Count == 0 ? null : new PlanCell(C("done"), C("late"), C("missed"), C("cancelled"), C("planned"));
            }
            row["total"] = mine.Count;
            return row;
        }).ToList();
        int T(string o) => visits.Where(v => pms.Any(p => p.Id == v.Pm)).Count(v => v.Outcome == o);
        return new("pm-annual-plan", "PM annual plan", a.Year.ToString(CultureInfo.InvariantCulture),
            [new("asset", "Asset"), new("pm", "PM"), .. Enumerable.Range(1, 12).Select(m => new Column($"m{m}", new DateOnly(a.Year, m, 1).ToString("MMM", Dates), "plan")),
             new("total", "Total", "number")],
            rows, null,
            [new("PMs", rows.Count), new("Done on time", T("done")), new("Late", T("late")), new("Missed", T("missed")), new("Planned", T("planned"))]);
    }

    private static async Task<ReportResult> AssetHistoryAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var root = await db.Assets.AsNoTracking().Where(x => x.Id == a.AssetId).Select(x => new { x.Id, x.AssetNumber, x.Name }).FirstAsync(ct);
        var ids = new HashSet<Guid> { root.Id };
        if (a.Components)
        {
            // Every level below it.
            var children = (await db.Assets.AsNoTracking().Where(x => x.ParentAssetId != null).Select(x => new { x.Id, Parent = x.ParentAssetId!.Value }).ToListAsync(ct))
                .ToLookup(x => x.Parent, x => x.Id);
            var queue = new Queue<Guid>(ids);
            while (queue.Count > 0) foreach (var c in children[queue.Dequeue()]) if (ids.Add(c)) queue.Enqueue(c);
        }
        var (s, e) = (Start(a.From), End(a.To));
        var wos = db.WorkOrders.AsNoTracking().Where(w => w.AssetId != null && ids.Contains(w.AssetId.Value) && w.CreatedAt >= s && w.CreatedAt < e);
        var items = await wos.OrderByDescending(w => w.CreatedAt).Select(w => new
        {
            w.Id, w.Number, w.Title, w.Type, w.CreatedAt, w.DowntimeHours, Asset = w.Asset!.AssetNumber,
            Symptom = w.Symptom == null ? null : w.Symptom.Code + " · " + w.Symptom.Name, Cause = w.FailureCode == null ? null : w.FailureCode.Code + " · " + w.FailureCode.Name,
            Remedy = w.Remedy == null ? null : w.Remedy.Code + " · " + w.Remedy.Name,
        }).Take(5000).ToListAsync(ct);
        var costs = (await CostLinesAsync(db, wos, null, null, ct)).GroupBy(c => c.WoId)
            .ToDictionary(g => g.Key, g => (Hours: g.Sum(x => x.Hours), Cost: g.Sum(x => x.Total)));
        var rows = items.Select(w => Row(("number", w.Number), ("date", w.CreatedAt), ("asset", w.Asset), ("type", Lists.Name(a.Settings, w.Type)), ("title", w.Title),
            ("symptom", w.Symptom), ("cause", w.Cause), ("remedy", w.Remedy), ("hours", R2(costs.GetValueOrDefault(w.Id).Hours)), ("cost", R2(costs.GetValueOrDefault(w.Id).Cost)),
            ("downtime", w.DowntimeHours), ("workOrderId", w.Id))).ToList();
        var subtitle = $"{root.AssetNumber} · {root.Name} · {Period(a)}" + (a.Components ? $" · {Translator.T("with its components")}" : "");
        return new("asset-work-history", "Asset work history", subtitle,
            [new("number", "Number"), new("date", "Raised", "date"), .. a.Components ? new[] { new Column("asset", "Asset") } : [], new("type", "Type"), new("title", "Title"),
             new("symptom", "Symptom"), new("cause", "Cause"), new("remedy", "Remedy"), new("hours", "Labour hours", "hours"), new("cost", "Cost", "currency"), new("downtime", "Downtime (h)", "hours")],
            rows, null,
            [new("Work orders", rows.Count), new("Labour hours", R2(costs.Values.Sum(c => c.Hours)), "hours"), new("Cost", R2(costs.Values.Sum(c => c.Cost)), "currency"),
             new("Downtime (h)", R2(items.Sum(w => w.DowntimeHours ?? 0)), "hours")]);
    }

    private static async Task<ReportResult> AssetsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var q = db.Assets.AsNoTracking();
        if (a.SiteId is { } s) q = q.Where(x => x.SiteId == s);
        if (a.AssetGroupId is { } g) { var members = AssetGroupEndpoints.Members(db, g); q = q.Where(x => members.Contains(x.Id)); }
        var xs = await q.Select(x => new { Category = x.Category!.Name, x.Status, x.PurchasePrice, x.CurrentValue }).ToListAsync(ct);
        var rows = xs.GroupBy(x => x.Category ?? "Uncategorised").OrderBy(g => g.Key).Select(g => Row(("category", g.Key),
            ("active", g.Count(x => x.Status == FourC.Cmms.Core.Assets.AssetStatus.Active)), ("underRepair", g.Count(x => x.Status == FourC.Cmms.Core.Assets.AssetStatus.UnderRepair)),
            ("inactive", g.Count(x => x.Status is FourC.Cmms.Core.Assets.AssetStatus.Inactive or FourC.Cmms.Core.Assets.AssetStatus.InStorage)),
            ("disposed", g.Count(x => x.Status == FourC.Cmms.Core.Assets.AssetStatus.Disposed)), ("total", g.Count()),
            ("purchaseValue", R2(g.Sum(x => x.PurchasePrice ?? 0))), ("currentValue", R2(g.Sum(x => x.CurrentValue ?? 0))))).ToList();
        return new("assets-by-category", "Asset register summary", $"As of {Today:d MMM yyyy}",
            [new("category", "Category"), new("active", "Active", "number"), new("underRepair", "Under repair", "number"), new("inactive", "Inactive / in storage", "number"),
             new("disposed", "Disposed", "number"), new("total", "Total", "number"), new("purchaseValue", "Purchase value", "currency"), new("currentValue", "Current value", "currency")],
            rows, new Chart("bar", "category", ["active", "underRepair", "inactive"], true),
            [new("Assets", xs.Count), new("Under repair", xs.Count(x => x.Status == FourC.Cmms.Core.Assets.AssetStatus.UnderRepair)),
             new("Purchase value", R2(xs.Sum(x => x.PurchasePrice ?? 0)), "currency"), new("Current value", R2(xs.Sum(x => x.CurrentValue ?? 0)), "currency")]);
    }

    // ---------------------------------------------------------------- inventory
    private static IQueryable<StockLevel> Levels(CmmsDbContext db, ReportArgs a) =>
        a.SiteId is { } s ? db.StockLevels.AsNoTracking().Where(l => l.Store!.SiteId == s) : db.StockLevels.AsNoTracking();

    private static async Task<ReportResult> ValuationAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var xs = await Levels(db, a).Where(l => l.QuantityOnHand != 0)
            .Select(l => new { Store = l.Store!.Code + " · " + l.Store.Name, Group = l.Part!.Group!.Name, l.PartId, l.QuantityOnHand, Value = l.QuantityOnHand * l.Part.AverageCost, l.Part.IsCritical }).ToListAsync(ct);
        var total = xs.Sum(x => x.Value);
        var rows = (a.GroupBy == "group" ? xs.GroupBy(x => x.Group ?? "Ungrouped") : xs.GroupBy(x => x.Store)).Select(g => Row(("group", g.Key), ("parts", g.Select(x => x.PartId).Distinct().Count()),
            ("quantity", g.Sum(x => x.QuantityOnHand)), ("value", R2(g.Sum(x => x.Value))), ("share", Pct(g.Sum(x => x.Value), total)), ("critical", g.Count(x => x.IsCritical))))
            .OrderByDescending(r => (decimal)r["value"]!).ToList();
        return new("stock-valuation", "Stock valuation", $"As of {Today:d MMM yyyy} · at average cost",
            [new("group", a.GroupBy == "group" ? "Part group" : "Store"), new("parts", "Parts in stock", "number"), new("quantity", "Quantity", "number"), new("value", "Value", "currency"),
             new("share", "Share", "percent"), new("critical", "Critical parts", "number")],
            rows, new Chart("donut", "group", ["value"]),
            [new("Stock value", R2(total), "currency"), new("Parts in stock", xs.Select(x => x.PartId).Distinct().Count()), new("Negative balances", xs.Count(x => x.QuantityOnHand < 0))]);
    }

    private static async Task<ReportResult> ReorderAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var open = new[] { PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived };
        // The parts list's "below re-order level" rule: available stock (on hand less reserved for open work).
        var xs = await Levels(db, a).Where(l => l.ReorderQuantity > 0 && l.QuantityOnHand - (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0) <= l.ReorderLevel && l.Part!.IsActive).Select(l => new
        {
            Reserved = (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0),
            l.PartId, l.Part!.PartNumber, l.Part.Name, l.Part.UnitOfMeasure, l.Part.IsCritical, Store = l.Store!.Code, l.QuantityOnHand, l.MinLevel, l.ReorderLevel, l.MaxLevel, l.ReorderQuantity,
            OnOrder = db.PurchaseOrderLines.Where(x => x.PartId == l.PartId && x.StoreId == l.StoreId && db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId && open.Contains(o.Status)))
                .Sum(x => (decimal?)((x.Quantity - x.QuantityReceived) * x.PackSize)) ?? 0,
            Price = l.Part.LastPurchasePrice ?? l.Part.AverageCost,
        }).ToListAsync(ct);
        var rows = xs.OrderByDescending(x => x.IsCritical).ThenBy(x => x.QuantityOnHand - x.MinLevel).Select(x =>
        {
            var need = InventoryEndpoints.SuggestedQuantity(x.QuantityOnHand, x.Reserved, x.OnOrder, x.ReorderQuantity, x.MaxLevel);
            var uncovered = x.QuantityOnHand - x.Reserved + x.OnOrder <= x.ReorderLevel;
            return Row(("partId", x.PartId), ("part", $"{x.PartNumber} · {x.Name}"), ("store", x.Store), ("onHand", x.QuantityOnHand), ("reserved", x.Reserved),
                ("available", x.QuantityOnHand - x.Reserved), ("minLevel", x.MinLevel), ("reorderLevel", x.ReorderLevel),
                ("onOrder", x.OnOrder), ("suggested", uncovered ? Math.Max(need, 0) : 0), ("value", uncovered ? R2(Math.Max(need, 0) * x.Price) : 0), ("critical", x.IsCritical ? "Yes" : ""),
                ("status", x.QuantityOnHand <= 0 ? "Out of stock" : x.QuantityOnHand < x.MinLevel ? "Below minimum" : uncovered ? "Re-order" : "On order"));
        }).ToList();
        return new("stock-reorder", "Stock below re-order level", $"As of {Today:d MMM yyyy}",
            [new("part", "Part"), new("store", "Store"), new("status", "Status"), new("critical", "Critical"), new("onHand", "On hand", "number"),
             new("reserved", "Reserved", "number"), new("available", "Available", "number"), new("minLevel", "Minimum", "number"),
             new("reorderLevel", "Re-order at", "number"), new("onOrder", "On order", "number"), new("suggested", "Suggested order", "number"), new("value", "Est. value", "currency")],
            rows, null,
            [new("Lines at re-order", rows.Count), new("Out of stock", xs.Count(x => x.QuantityOnHand <= 0)), new("Critical", xs.Count(x => x.IsCritical)),
             new("Est. order value", rows.Sum(r => (decimal)r["value"]!), "currency")]);
    }

    private static async Task<ReportResult> ConsumptionAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var q = db.StockTransactions.AsNoTracking().Where(t => t.Date >= a.From && t.Date <= a.To && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return));
        if (a.SiteId is { } s) q = q.Where(t => t.Store!.SiteId == s);
        var xs = await q.Select(t => new
        {
            t.PartId, Part = t.Part!.PartNumber + " · " + t.Part.Name, t.Part.UnitOfMeasure, Group = t.Part.Group!.Name, Qty = -t.Quantity, Cost = -t.Quantity * t.UnitCost, t.WorkOrderId,
            Asset = db.WorkOrders.Where(w => w.Id == t.WorkOrderId).Select(w => w.Asset!.AssetNumber + " · " + w.Asset.Name).FirstOrDefault(),
        }).ToListAsync(ct);
        var groups = a.GroupBy switch
        {
            "group" => xs.GroupBy(x => x.Group ?? "Ungrouped"),
            "asset" => xs.GroupBy(x => x.Asset ?? (x.WorkOrderId is null ? "Issued to a location" : "No asset")),
            _ => xs.GroupBy(x => x.Part),
        };
        var rows = groups.Select(g => Row(("group", g.Key), ("quantity", a.GroupBy is "part" or null ? g.Sum(x => x.Qty) : null), ("unit", a.GroupBy is "part" or null ? g.First().UnitOfMeasure : null),
            ("cost", R2(g.Sum(x => x.Cost))), ("workOrders", g.Where(x => x.WorkOrderId != null).Select(x => x.WorkOrderId).Distinct().Count()), ("issues", g.Count(x => x.Qty > 0))))
            .Where(r => (decimal)r["cost"]! != 0 || (r["quantity"] as decimal? ?? 0) != 0).OrderByDescending(r => (decimal)r["cost"]!).Take(500).ToList();
        var cols = new List<Column> { new("group", a.GroupBy switch { "group" => "Part group", "asset" => "Asset", _ => "Part" }) };
        if (a.GroupBy is "part" or null) { cols.Add(new("quantity", "Net quantity", "number")); cols.Add(new("unit", "Unit")); }
        cols.AddRange([new("cost", "Cost", "currency"), new("workOrders", "Work orders", "number"), new("issues", "Issues", "number")]);
        return new("parts-consumption", "Parts consumption", Period(a), cols, rows, new Chart("bar", "group", ["cost"]),
            [new("Parts cost", R2(xs.Sum(x => x.Cost)), "currency"), new("Distinct parts", xs.Select(x => x.PartId).Distinct().Count()), new("Work orders", xs.Where(x => x.WorkOrderId != null).Select(x => x.WorkOrderId).Distinct().Count())]);
    }

    private static async Task<ReportResult> DormantAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var cutoff = Today.AddDays(-a.Days);
        var xs = await Levels(db, a).Where(l => l.QuantityOnHand > 0).Select(l => new
        {
            l.PartId, l.Part!.PartNumber, l.Part.Name, Store = l.Store!.Code, l.QuantityOnHand, Value = l.QuantityOnHand * l.Part.AverageCost,
            LastIssue = db.StockTransactions.Where(t => t.PartId == l.PartId && t.StoreId == l.StoreId && t.Type == StockTransactionType.Issue).Max(t => (DateOnly?)t.Date),
            LastReceipt = db.StockTransactions.Where(t => t.PartId == l.PartId && t.StoreId == l.StoreId && (t.Type == StockTransactionType.Receipt || t.Type == StockTransactionType.TransferIn)).Max(t => (DateOnly?)t.Date),
        }).ToListAsync(ct);
        var total = xs.Sum(x => x.Value);
        var dormant = xs.Where(x => (x.LastIssue ?? DateOnly.MinValue) < cutoff && (x.LastReceipt ?? DateOnly.MinValue) < cutoff).OrderByDescending(x => x.Value).ToList();
        var rows = dormant.Select(x => Row(("partId", x.PartId), ("part", $"{x.PartNumber} · {x.Name}"), ("store", x.Store), ("onHand", x.QuantityOnHand), ("value", R2(x.Value)),
            ("lastIssue", x.LastIssue), ("lastReceipt", x.LastReceipt), ("idleDays", x.LastIssue is { } d ? Today.DayNumber - d.DayNumber : (int?)null))).ToList();
        return new("dormant-stock", "Dormant stock", $"No issues or receipts in the last {a.Days} days",
            [new("part", "Part"), new("store", "Store"), new("onHand", "On hand", "number"), new("value", "Value", "currency"), new("lastIssue", "Last issued", "date"),
             new("lastReceipt", "Last received", "date"), new("idleDays", "Days since issue", "number")],
            rows, null,
            [new("Dormant value", R2(dormant.Sum(x => x.Value)), "currency"), new("Share of stock value", Pct(dormant.Sum(x => x.Value), total), "percent"), new("Lines", dormant.Count),
             new("Never issued", dormant.Count(x => x.LastIssue is null))]);
    }

    // ---------------------------------------------------------------- purchasing
    private static async Task<ReportResult> SpendAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var rq = db.StockTransactions.AsNoTracking().Where(t => t.Type == StockTransactionType.Receipt && t.SupplierId != null && t.Date >= a.From && t.Date <= a.To);
        if (a.SiteId is { } site) rq = rq.Where(t => t.Store!.SiteId == site);
        var goods = await rq.GroupBy(t => t.SupplierId!.Value).Select(g => new { g.Key, Value = g.Sum(t => t.Quantity * t.UnitCost), Receipts = g.Count() }).ToListAsync(ct);
        var pos = db.PurchaseOrders.AsNoTracking();
        if (a.SiteId is { } site2) pos = pos.Where(o => o.SiteId == site2);
        var services = await (from c in db.WorkOrderCosts.AsNoTracking() join o in pos on c.Reference equals o.Number
                              where c.CreatedAt >= s && c.CreatedAt < e
                              group c by o.SupplierId into g select new { g.Key, Value = g.Sum(c => c.Quantity * c.UnitCost) }).ToListAsync(ct);
        var placed = await pos.Where(o => o.ApprovedAt >= s && o.ApprovedAt < e).GroupBy(o => o.SupplierId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        // On-time delivery: completed orders with an expected date whose last receipt landed by that date.
        var deliveries = await pos.Where(o => o.ExpectedDate != null && (o.Status == PurchaseOrderStatus.Received || o.Status == PurchaseOrderStatus.Closed))
            .Select(o => new { o.SupplierId, o.ExpectedDate, Last = db.StockTransactions.Where(t => t.PurchaseOrderId == o.Id && t.Type == StockTransactionType.Receipt).Max(t => (DateOnly?)t.Date) })
            .Where(x => x.Last >= a.From && x.Last <= a.To).ToListAsync(ct);
        var ids = goods.Select(x => x.Key).Concat(services.Select(x => x.Key)).Concat(placed.Select(x => x.Key)).Distinct().ToList();
        var names = await db.Suppliers.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var total = goods.Sum(x => x.Value) + services.Sum(x => x.Value);
        var rows = ids.Select(id =>
        {
            var gv = goods.FirstOrDefault(x => x.Key == id)?.Value ?? 0;
            var sv = services.FirstOrDefault(x => x.Key == id)?.Value ?? 0;
            var d = deliveries.Where(x => x.SupplierId == id).ToList();
            return Row(("supplier", names.GetValueOrDefault(id) ?? "—"), ("goods", R2(gv)), ("services", R2(sv)), ("total", R2(gv + sv)), ("share", Pct(gv + sv, total)),
                ("orders", placed.FirstOrDefault(x => x.Key == id)?.Count ?? 0), ("onTime", Pct(d.Count(x => x.Last <= x.ExpectedDate), d.Count)));
        }).OrderByDescending(r => (decimal)r["total"]!).ToList();
        return new("spend-by-supplier", "Spend by supplier", Period(a),
            [new("supplier", "Supplier"), new("goods", "Stock received", "currency"), new("services", "Direct / services", "currency"), new("total", "Total", "currency"), new("share", "Share", "percent"),
             new("orders", "Orders approved", "number"), new("onTime", "On-time delivery", "percent")],
            rows, new Chart("bar", "supplier", ["goods", "services"], true),
            [new("Total spend", R2(total), "currency"), new("Suppliers", rows.Count(r => (decimal)r["total"]! > 0)), new("Orders approved", placed.Sum(x => x.Count)),
             new("On-time delivery", Pct(deliveries.Count(x => x.Last <= x.ExpectedDate), deliveries.Count), "percent")]);
    }

    private static async Task<ReportResult> OpenPoAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var q = db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.Status >= PurchaseOrderStatus.Submitted && o.Status <= PurchaseOrderStatus.PartiallyReceived);
        if (a.SiteId is { } s) q = q.Where(o => o.SiteId == s);
        var xs = await q.Select(o => new { o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, Supplier = o.Supplier!.Name, o.ExchangeRate, o.Currency, o.TaxPercent, o.ShippingCost, o.Lines }).ToListAsync(ct);
        var rows = xs.OrderBy(x => x.ExpectedDate ?? DateOnly.MaxValue).Select(x =>
        {
            var po = new PurchaseOrder { Number = x.Number, TaxPercent = x.TaxPercent, ShippingCost = x.ShippingCost, ExchangeRate = x.ExchangeRate, Lines = x.Lines };
            var outstanding = R2(x.Lines.Sum(l => Math.Max(l.Quantity - l.QuantityReceived, 0) * l.NetUnitPrice) * x.ExchangeRate);
            var late = x.Status != PurchaseOrderStatus.Submitted && x.ExpectedDate < Today ? Today.DayNumber - x.ExpectedDate!.Value.DayNumber : (int?)null;
            return Row(("id", x.Id), ("number", x.Number), ("supplier", x.Supplier), ("status", x.Status.ToString()), ("orderDate", x.OrderDate), ("expectedDate", x.ExpectedDate),
                ("total", PurchaseOrderMath.TotalInBase(po)), ("outstanding", outstanding), ("daysLate", late));
        }).ToList();
        return new("open-purchase-orders", "Open purchase orders", $"As of {Today:d MMM yyyy} · values in base currency",
            [new("number", "PO"), new("supplier", "Supplier"), new("status", "Status"), new("orderDate", "Ordered", "date"), new("expectedDate", "Expected", "date"),
             new("total", "Order value", "currency"), new("outstanding", "Outstanding", "currency"), new("daysLate", "Days late", "number")],
            rows, null,
            [new("Open orders", rows.Count), new("Awaiting approval", xs.Count(x => x.Status == PurchaseOrderStatus.Submitted)), new("Overdue", rows.Count(r => r["daysLate"] is not null)),
             new("Outstanding value", rows.Sum(r => (decimal)r["outstanding"]!), "currency")]);
    }
}
