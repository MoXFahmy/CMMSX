using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
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
    public sealed record ReportArgs(DateOnly From, DateOnly To, Guid? SiteId, string? GroupBy, int Days);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly Param Range = new("range", "dateRange");
    private static readonly Param SiteP = new("siteId", "site");
    private static Param GroupBy(object def, params (string, string)[] o) => new("groupBy", "select", o.Select(x => new Option(x.Item1, x.Item2)).ToList(), def);

    public static readonly List<ReportDef> Catalog =
    [
        new("wo-backlog", "Work", "Work order backlog", "Open work orders by priority and age, with overdue and on-hold counts.", Permissions.WorkOrdersView, [SiteP]),
        new("wo-throughput", "Work", "Work order throughput", "Work orders raised and completed per month, on-time completion and average turnaround.", Permissions.WorkOrdersView, [Range, SiteP]),
        new("pm-compliance", "Work", "PM compliance", "Preventive work due in the period: completed on time, late, or missed.", Permissions.PmView,
            [Range, SiteP, GroupBy("month", ("month", "Month"), ("schedule", "PM schedule"), ("trade", "Trade"))]),
        new("labor-hours", "Work", "Labour hours", "Hours, overtime and labour cost booked to work orders.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("employee", ("employee", "Employee"), ("trade", "Trade"), ("type", "Work type"))]),
        new("reliability", "Reliability", "Asset reliability (MTBF / MTTR)", "Breakdowns per asset with downtime, mean time between failures and mean time to repair.", Permissions.WorkOrdersView, [Range, SiteP]),
        new("failure-pareto", "Reliability", "Failure analysis", "Breakdowns by failure code — the few causes behind most of the downtime.", Permissions.WorkOrdersView, [Range, SiteP]),
        new("maintenance-costs", "Costs", "Maintenance costs", "Labour, parts and other costs booked in the period.", Permissions.WorkOrdersView,
            [Range, SiteP, GroupBy("asset", ("asset", "Asset"), ("site", "Site"), ("category", "Asset category"), ("type", "Work type"), ("trade", "Trade"), ("month", "Month"))]),
        new("assets-by-category", "Assets", "Asset register summary", "Assets by category and status with purchase and current value.", Permissions.AssetsView, [SiteP]),
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
            return Catalog.Where(r => mine.Contains(r.Permission));
        }).RequirePermission(Permissions.ReportsView);

        g.MapGet("/{key}", async (string key, HttpContext http, PermissionService perms, CmmsDbContext db, DateOnly? from, DateOnly? to, Guid? siteId, string? groupBy, int? days, CancellationToken ct) =>
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
            var a = new ReportArgs(f, t, siteId, gb, Math.Clamp(days ?? 180, 1, 3650));
            ReportResult r = key switch
            {
                "wo-backlog" => await BacklogAsync(db, a, ct),
                "wo-throughput" => await ThroughputAsync(db, a, ct),
                "pm-compliance" => await PmComplianceAsync(db, a, ct),
                "labor-hours" => await LaborAsync(db, a, ct),
                "reliability" => await ReliabilityAsync(db, a, ct),
                "failure-pareto" => await FailureParetoAsync(db, a, ct),
                "maintenance-costs" => await CostsAsync(db, a, ct),
                "assets-by-category" => await AssetsAsync(db, a, ct),
                "stock-valuation" => await ValuationAsync(db, a, ct),
                "stock-reorder" => await ReorderAsync(db, a, ct),
                "parts-consumption" => await ConsumptionAsync(db, a, ct),
                "dormant-stock" => await DormantAsync(db, a, ct),
                "spend-by-supplier" => await SpendAsync(db, a, ct),
                "open-purchase-orders" => await OpenPoAsync(db, a, ct),
                _ => throw new InvalidOperationException(),
            };
            return Results.Ok(r);
        }).RequirePermission(Permissions.ReportsView);
    }

    // ---------------------------------------------------------------- helpers
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) => cells.ToDictionary(c => c.Key, c => c.Value);
    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static DateTimeOffset End(DateOnly d) => Start(d.AddDays(1));
    private static string Period(ReportArgs a) => $"{a.From:d MMM yyyy} – {a.To:d MMM yyyy}";
    private static decimal R2(decimal v) => Math.Round(v, 2);
    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1);
    private static IEnumerable<(int Y, int M)> Months(ReportArgs a)
    {
        for (var d = new DateOnly(a.From.Year, a.From.Month, 1); d <= a.To; d = d.AddMonths(1)) yield return (d.Year, d.Month);
    }
    private static string MonthLabel(int y, int m) => new DateOnly(y, m, 1).ToString("MMM yyyy");
    private static bool IsFailure(WorkOrder w) => w.Type == WorkType.Breakdown || w.AssetWasDown;

    private static IQueryable<WorkOrder> Wo(CmmsDbContext db, ReportArgs a) => a.SiteId is { } s ? db.WorkOrders.AsNoTracking().Where(w => w.SiteId == s) : db.WorkOrders.AsNoTracking();

    // ---------------------------------------------------------------- work
    private static async Task<ReportResult> BacklogAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var open = await Wo(db, a).Where(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
            .Select(w => new { w.Priority, w.CreatedAt, w.DueDate, w.Status }).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        string Bucket(DateTimeOffset c) => (now - c).TotalDays switch { <= 7 => "d0_7", <= 30 => "d8_30", <= 90 => "d31_90", _ => "d90" };
        var rows = Enum.GetValues<WorkPriority>().OrderByDescending(p => p).Select(p =>
        {
            var xs = open.Where(w => w.Priority == p).ToList();
            return Row(("priority", p.ToString()), ("d0_7", xs.Count(w => Bucket(w.CreatedAt) == "d0_7")), ("d8_30", xs.Count(w => Bucket(w.CreatedAt) == "d8_30")),
                ("d31_90", xs.Count(w => Bucket(w.CreatedAt) == "d31_90")), ("d90", xs.Count(w => Bucket(w.CreatedAt) == "d90")),
                ("onHold", xs.Count(w => w.Status == WorkOrderStatus.OnHold)), ("overdue", xs.Count(w => w.DueDate < Today)), ("total", xs.Count));
        }).ToList();
        return new("wo-backlog", "Work order backlog", $"As of {Today:d MMM yyyy}",
            [new("priority", "Priority"), new("d0_7", "0–7 days", "number"), new("d8_30", "8–30 days", "number"), new("d31_90", "31–90 days", "number"), new("d90", "Over 90 days", "number"),
             new("onHold", "On hold", "number"), new("overdue", "Overdue", "number"), new("total", "Total", "number")],
            rows, new Chart("bar", "priority", ["d0_7", "d8_30", "d31_90", "d90"], true),
            [new("Open work orders", open.Count), new("Overdue", open.Count(w => w.DueDate < Today)), new("On hold", open.Count(w => w.Status == WorkOrderStatus.OnHold)),
             new("Average age (days)", open.Count == 0 ? 0 : Math.Round(open.Average(w => (now - w.CreatedAt).TotalDays), 1))]);
    }

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
                Trade = w.Trade!.Name }).ToListAsync(ct);
        string Outcome(DateOnly due, DateTimeOffset? completed, WorkOrderStatus st) =>
            st == WorkOrderStatus.Cancelled ? "skipped"
            : completed is { } c ? (DateOnly.FromDateTime(c.UtcDateTime) <= due ? "onTime" : "late")
            : due < Today ? "missed" : "pending";
        var tagged = items.Select(x => new { x.Due, x.Schedule, x.Trade, O = Outcome(x.Due, x.CompletedAt, x.Status) }).ToList();
        var groups = a.GroupBy switch
        {
            "schedule" => tagged.GroupBy(x => x.Schedule ?? "—").OrderBy(g => g.Key).Select(g => (g.Key, g.ToList())),
            "trade" => tagged.GroupBy(x => x.Trade ?? "No trade").OrderBy(g => g.Key).Select(g => (g.Key, g.ToList())),
            _ => Months(a).Select(m => (MonthLabel(m.Y, m.M), tagged.Where(x => x.Due.Year == m.Y && x.Due.Month == m.M).ToList())),
        };
        var rows = groups.Select(g =>
        {
            int C(string o) => g.Item2.Count(x => x.O == o);
            return Row(("group", g.Item1), ("due", g.Item2.Count), ("onTime", C("onTime")), ("late", C("late")), ("missed", C("missed")), ("pending", C("pending")), ("skipped", C("skipped")),
                ("compliance", Pct(C("onTime"), C("onTime") + C("late") + C("missed"))));
        }).ToList();
        int T(string o) => tagged.Count(x => x.O == o);
        var label = a.GroupBy switch { "schedule" => "PM schedule", "trade" => "Trade", _ => "Month due" };
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
                select new { l.EmployeeId, Employee = l.Employee!.FullName, l.Employee.IsContractor, l.Employee.TradeIds, Trade = w.Trade!.Name, w.Type, l.WorkOrderId, l.Hours, l.OvertimeHours, Cost = l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate };
        var xs = await q.ToListAsync(ct);
        var groups = a.GroupBy switch
        {
            "trade" => xs.GroupBy(x => x.Trade ?? "No trade"),
            "type" => xs.GroupBy(x => x.Type.ToString()),
            _ => xs.GroupBy(x => x.Employee + (x.IsContractor ? " (contractor)" : "")),
        };
        var rows = groups.Select(g => Row(("group", g.Key), ("hours", g.Sum(x => x.Hours)), ("overtime", g.Sum(x => x.OvertimeHours)), ("total", g.Sum(x => x.Hours + x.OvertimeHours)),
            ("cost", R2(g.Sum(x => x.Cost))), ("workOrders", g.Select(x => x.WorkOrderId).Distinct().Count()))).OrderByDescending(r => (decimal)r["total"]!).ToList();
        var label = a.GroupBy switch { "trade" => "Trade (work order)", "type" => "Work type", _ => "Employee" };
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
            .Select(w => new { AssetId = w.AssetId!.Value, w.Asset!.AssetNumber, w.Asset.Name, w.Asset.DowntimeCostPerHour, Code = w.FailureCode!.Code, CodeName = w.FailureCode.Name, w.DowntimeHours, w.StartedAt, w.CompletedAt, w.CreatedAt })
            .ToListAsync(ct);
        return xs.Select(x => new Failure(x.AssetId, x.AssetNumber, x.Name, x.Code, x.CodeName, x.DowntimeHours ?? 0,
            x.CompletedAt is { } c ? (c - (x.StartedAt ?? x.CreatedAt)).TotalHours : null, x.DowntimeCostPerHour)).ToList();
    }

    private static async Task<ReportResult> ReliabilityAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var fs = await FailuresAsync(db, a, ct);
        var periodHours = (decimal)(a.To.DayNumber - a.From.DayNumber + 1) * 24;
        decimal? Mttr(IEnumerable<Failure> g)
        {
            var l = g.ToList();
            var withDown = l.Where(f => f.Downtime > 0).ToList();
            if (withDown.Count > 0) return Math.Round(withDown.Average(f => f.Downtime), 1);
            var rep = l.Where(f => f.RepairHours is not null).ToList();
            return rep.Count == 0 ? null : Math.Round((decimal)rep.Average(f => f.RepairHours!.Value), 1);
        }
        var rows = fs.GroupBy(f => f.AssetId).Select(g =>
        {
            var down = g.Sum(f => f.Downtime);
            return Row(("asset", $"{g.First().AssetNumber} · {g.First().AssetName}"), ("assetId", g.Key), ("failures", g.Count()), ("downtime", down),
                ("mttr", Mttr(g)), ("mtbf", Math.Round(Math.Max(periodHours - down, 0) / g.Count(), 0)),
                ("availability", Pct(Math.Max(periodHours - down, 0), periodHours)), ("downtimeCost", g.First().CostPerHour is { } cph ? R2(cph * down) : null));
        }).OrderByDescending(r => (int)r["failures"]!).ThenByDescending(r => (decimal)r["downtime"]!).Take(100).ToList();
        var assets = fs.Select(f => f.AssetId).Distinct().Count();
        return new("reliability", "Asset reliability", $"{Period(a)} · breakdowns are breakdown work orders or work where the asset was down",
            [new("asset", "Asset"), new("failures", "Breakdowns", "number"), new("downtime", "Downtime (h)", "hours"), new("mttr", "MTTR (h)", "hours"), new("mtbf", "MTBF (h)", "hours"),
             new("availability", "Availability", "percent"), new("downtimeCost", "Downtime cost", "currency")],
            rows, new Chart("bar", "asset", ["failures"]),
            [new("Breakdowns", fs.Count), new("Assets affected", assets), new("Total downtime (h)", fs.Sum(f => f.Downtime), "hours"), new("Overall MTTR (h)", Mttr(fs), "hours")]);
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

    // ---------------------------------------------------------------- costs
    private sealed record CostLine(Guid WoId, int Y, int M, decimal Labor, decimal Parts, decimal Other);

    private static async Task<ReportResult> CostsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var (s, e) = (Start(a.From), End(a.To));
        var wos = Wo(db, a);
        var labor = await (from l in db.WorkOrderLabor.AsNoTracking() join w in wos on l.WorkOrderId equals w.Id
                           where l.WorkDate >= a.From && l.WorkDate <= a.To
                           group l by new { l.WorkOrderId, l.WorkDate.Year, l.WorkDate.Month } into g
                           select new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, g.Sum(x => x.Hours * x.Rate + x.OvertimeHours * x.OvertimeRate), 0, 0)).ToListAsync(ct);
        var parts = await (from t in db.StockTransactions.AsNoTracking() join w in wos on t.WorkOrderId equals w.Id
                           where t.Date >= a.From && t.Date <= a.To && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return)
                           group t by new { WorkOrderId = t.WorkOrderId!.Value, t.Date.Year, t.Date.Month } into g
                           select new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, 0, g.Sum(x => -x.Quantity * x.UnitCost), 0)).ToListAsync(ct);
        var other = await (from c in db.WorkOrderCosts.AsNoTracking() join w in wos on c.WorkOrderId equals w.Id
                           where c.CreatedAt >= s && c.CreatedAt < e
                           group c by new { c.WorkOrderId, c.CreatedAt.Year, c.CreatedAt.Month } into g
                           select new CostLine(g.Key.WorkOrderId, g.Key.Year, g.Key.Month, 0, 0, g.Sum(x => x.Quantity * x.UnitCost))).ToListAsync(ct);
        var lines = labor.Concat(parts).Concat(other).ToList();
        var ids = lines.Select(l => l.WoId).Distinct().ToList();
        var dims = new Dictionary<Guid, (string Site, string Type, string Trade, string Asset, string Category)>();
        foreach (var chunk in ids.Chunk(1000))
            foreach (var d in await db.WorkOrders.AsNoTracking().Where(w => chunk.Contains(w.Id)).Select(w => new { w.Id, Site = w.Site!.Name, w.Type, Trade = w.Trade!.Name,
                         Asset = w.Asset == null ? null : w.Asset.AssetNumber + " · " + w.Asset.Name, Category = w.Asset!.Category!.Name }).ToListAsync(ct))
                dims[d.Id] = (d.Site, d.Type.ToString(), d.Trade ?? "No trade", d.Asset ?? "No asset (location work)", d.Category ?? "Uncategorised");
        string Key(CostLine l) => a.GroupBy switch
        {
            "site" => dims[l.WoId].Site, "type" => dims[l.WoId].Type, "trade" => dims[l.WoId].Trade, "category" => dims[l.WoId].Category,
            "month" => $"{l.Y:0000}-{l.M:00}", _ => dims[l.WoId].Asset,
        };
        var grouped = lines.GroupBy(Key).Select(g => Row(("group", a.GroupBy == "month" ? MonthLabel(int.Parse(g.Key[..4]), int.Parse(g.Key[5..])) : g.Key), ("sortKey", g.Key),
            ("labor", R2(g.Sum(x => x.Labor))), ("parts", R2(g.Sum(x => x.Parts))), ("other", R2(g.Sum(x => x.Other))), ("total", R2(g.Sum(x => x.Labor + x.Parts + x.Other))),
            ("workOrders", g.Select(x => x.WoId).Distinct().Count())));
        var rows = (a.GroupBy == "month" ? grouped.OrderBy(r => (string)r["sortKey"]!) : grouped.OrderByDescending(r => (decimal)r["total"]!)).Take(a.GroupBy == "asset" ? 200 : 1000).ToList();
        foreach (var r in rows) r.Remove("sortKey");
        var total = lines.Sum(x => x.Labor + x.Parts + x.Other);
        var label = a.GroupBy switch { "site" => "Site", "type" => "Work type", "trade" => "Trade", "category" => "Asset category", "month" => "Month", _ => "Asset" };
        return new("maintenance-costs", "Maintenance costs", Period(a),
            [new("group", label), new("labor", "Labour", "currency"), new("parts", "Parts", "currency"), new("other", "Other", "currency"), new("total", "Total", "currency"), new("workOrders", "Work orders", "number")],
            rows, new Chart("bar", "group", ["labor", "parts", "other"], true),
            [new("Total cost", R2(total), "currency"), new("Labour", R2(lines.Sum(x => x.Labor)), "currency"), new("Parts", R2(lines.Sum(x => x.Parts)), "currency"), new("Other", R2(lines.Sum(x => x.Other)), "currency")]);
    }

    private static async Task<ReportResult> AssetsAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        var q = db.Assets.AsNoTracking();
        if (a.SiteId is { } s) q = q.Where(x => x.SiteId == s);
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
        var xs = await Levels(db, a).Where(l => l.ReorderQuantity > 0 && l.QuantityOnHand <= l.ReorderLevel && l.Part!.IsActive).Select(l => new
        {
            l.PartId, l.Part!.PartNumber, l.Part.Name, l.Part.UnitOfMeasure, l.Part.IsCritical, Store = l.Store!.Code, l.QuantityOnHand, l.MinLevel, l.ReorderLevel, l.MaxLevel, l.ReorderQuantity,
            OnOrder = db.PurchaseOrderLines.Where(x => x.PartId == l.PartId && x.StoreId == l.StoreId && db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId && open.Contains(o.Status)))
                .Sum(x => (decimal?)(x.Quantity - x.QuantityReceived)) ?? 0,
            Price = l.Part.LastPurchasePrice ?? l.Part.AverageCost,
        }).ToListAsync(ct);
        var rows = xs.OrderByDescending(x => x.IsCritical).ThenBy(x => x.QuantityOnHand - x.MinLevel).Select(x =>
        {
            var need = Math.Max(x.ReorderQuantity, x.MaxLevel > 0 ? x.MaxLevel - x.QuantityOnHand - x.OnOrder : 0);
            var uncovered = x.QuantityOnHand + x.OnOrder <= x.ReorderLevel;
            return Row(("partId", x.PartId), ("part", $"{x.PartNumber} · {x.Name}"), ("store", x.Store), ("onHand", x.QuantityOnHand), ("minLevel", x.MinLevel), ("reorderLevel", x.ReorderLevel),
                ("onOrder", x.OnOrder), ("suggested", uncovered ? Math.Max(need, 0) : 0), ("value", uncovered ? R2(Math.Max(need, 0) * x.Price) : 0), ("critical", x.IsCritical ? "Yes" : ""),
                ("status", x.QuantityOnHand <= 0 ? "Out of stock" : x.QuantityOnHand < x.MinLevel ? "Below minimum" : uncovered ? "Re-order" : "On order"));
        }).ToList();
        return new("stock-reorder", "Stock below re-order level", $"As of {Today:d MMM yyyy}",
            [new("part", "Part"), new("store", "Store"), new("status", "Status"), new("critical", "Critical"), new("onHand", "On hand", "number"), new("minLevel", "Minimum", "number"),
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
