using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Api.Features.ReportEndpoints;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Budget against actual (0.20.0): per cost centre (or per month), the budget, committed spend (approved order lines for work
/// orders, not yet received) and actual cost — the Maintenance costs report's figures plus parts issued straight to locations —
/// with the variance. A cost centre's figures cover all its work at every site; parents add up their children.
/// </summary>
public static class BudgetReport
{
    private sealed class Figures
    {
        public decimal Budget, Committed, Labor, Parts, Contractor, Other, Direct;
        public decimal Actual => Labor + Parts + Contractor + Other + Direct;
        public void Add(Figures f) { Budget += f.Budget; Committed += f.Committed; Labor += f.Labor; Parts += f.Parts; Contractor += f.Contractor; Other += f.Other; Direct += f.Direct; }
    }

    public static async Task<ReportResult> BuildAsync(CmmsDbContext db, ReportArgs a, CancellationToken ct)
    {
        // Own figures by (cost centre, year, month); a null cost centre is work without one.
        var own = new Dictionary<(Guid?, int, int), Figures>();
        Figures At(Guid? cc, int y, int m) => own.TryGetValue((cc, y, m), out var f) ? f : own[(cc, y, m)] = new Figures();

        // A cost centre's figures are whole (every site), but cost with no cost centre counts only at the viewer's own sites:
        // the work orders and direct issues the viewer can see.
        async Task<HashSet<Guid>> SeenAsync(IEnumerable<Guid> workOrderIds)
        {
            var seen = new HashSet<Guid>();
            foreach (var chunk in workOrderIds.Distinct().Chunk(1000))
                seen.UnionWith(await db.WorkOrders.AsNoTracking().Where(w => chunk.Contains(w.Id)).Select(w => w.Id).ToListAsync(ct));
            return seen;
        }

        var lines = await CostLinesAsync(db, db.WorkOrders.AllSites().AsNoTracking(), a.From, a.To, ct);
        var woIds = lines.Select(l => l.WoId).Distinct().ToList();
        var centreOf = new Dictionary<Guid, Guid?>();
        foreach (var chunk in woIds.Chunk(1000))
            foreach (var w in await db.WorkOrders.AllSites().AsNoTracking().Where(w => chunk.Contains(w.Id)).Select(w => new { w.Id, w.CostCentreId }).ToListAsync(ct))
                centreOf[w.Id] = w.CostCentreId;
        var seenWithout = await SeenAsync(woIds.Where(id => centreOf.GetValueOrDefault(id) is null));
        foreach (var l in lines)
        {
            var cc = centreOf.GetValueOrDefault(l.WoId);
            if (cc is null && !seenWithout.Contains(l.WoId)) continue;
            var f = At(cc, l.Y, l.M);
            f.Labor += l.Labor; f.Parts += l.Parts; f.Contractor += l.Contractor; f.Other += l.Other;
        }

        IQueryable<StockTransaction> Direct(IQueryable<StockTransaction> q) => q.AsNoTracking()
            .Where(t => t.WorkOrderId == null && t.LocationId != null && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return)
                        && t.Date >= a.From && t.Date <= a.To);
        var direct = await Direct(db.StockTransactions.AllSites()).Where(t => t.CostCentreId != null)
            .GroupBy(t => new { t.CostCentreId, t.Date.Year, t.Date.Month })
            .Select(g => new { g.Key.CostCentreId, g.Key.Year, g.Key.Month, Amount = g.Sum(t => -t.Quantity * t.UnitCost) }).ToListAsync(ct);
        direct.AddRange(await Direct(db.StockTransactions).Where(t => t.CostCentreId == null)
            .GroupBy(t => new { t.CostCentreId, t.Date.Year, t.Date.Month })
            .Select(g => new { g.Key.CostCentreId, g.Key.Year, g.Key.Month, Amount = g.Sum(t => -t.Quantity * t.UnitCost) }).ToListAsync(ct));
        foreach (var d in direct) At(d.CostCentreId, d.Year, d.Month).Direct += d.Amount;

        // Committed: the open part of approved order lines for work orders, dated by the order's expected date, else its order date.
        var open = await (from l in db.PurchaseOrderLines.AsNoTracking()
                          join o in db.PurchaseOrders.AllSites().AsNoTracking() on l.PurchaseOrderId equals o.Id
                          join w in db.WorkOrders.AllSites().AsNoTracking() on l.WorkOrderId equals w.Id
                          where (o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.PartiallyReceived) && l.Quantity > l.QuantityReceived
                                && (o.ExpectedDate ?? o.OrderDate) >= a.From && (o.ExpectedDate ?? o.OrderDate) <= a.To
                          select new { WoId = w.Id, w.CostCentreId, Day = o.ExpectedDate ?? o.OrderDate, l.Quantity, l.QuantityReceived, l.UnitPrice, l.DiscountPercent, o.ExchangeRate })
            .ToListAsync(ct);
        var seenOpen = await SeenAsync(open.Where(c => c.CostCentreId is null).Select(c => c.WoId));
        foreach (var c in open.Where(c => c.CostCentreId is not null || seenOpen.Contains(c.WoId)))
            At(c.CostCentreId, c.Day.Year, c.Day.Month).Committed +=
                Math.Round((c.Quantity - c.QuantityReceived) * Math.Round(c.UnitPrice * (1 - c.DiscountPercent / 100m), 4) * c.ExchangeRate, 2);

        var firstMonth = a.From.Year * 12 + a.From.Month - 1;
        var lastMonth = a.To.Year * 12 + a.To.Month - 1;
        foreach (var b in await db.Budgets.AsNoTracking().Where(b => b.Year * 12 + b.Month - 1 >= firstMonth && b.Year * 12 + b.Month - 1 <= lastMonth).ToListAsync(ct))
            At(b.CostCentreId, b.Year, b.Month).Budget += b.Amount * CoveredShare(b.Year, b.Month, a.From, a.To);

        // The tree (every site), what the viewer may see, and each cost centre with its children.
        var tree = await db.CostCentres.AllSites().AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var children = tree.Where(c => c.ParentId != null).ToLookup(c => c.ParentId!.Value, c => c.Id);
        HashSet<Guid> Subtree(Guid id)
        {
            var set = new HashSet<Guid>();
            var stack = new Stack<Guid>([id]);
            while (stack.Count > 0) { var x = stack.Pop(); if (set.Add(x)) foreach (var ch in children[x]) stack.Push(ch); }
            return set;
        }
        var paths = await CostCentreEndpoints.PathsAsync(db, ct);
        var visible = await db.CostCentres.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var shown = a.CostCentreId is { } chosen ? visible.Where(v => Subtree(chosen).Contains(v.Id)).ToList() : visible;
        var shownIds = shown.Select(v => v.Id).ToHashSet();
        var parentOf = tree.ToDictionary(c => c.Id, c => c.ParentId);
        int Depth(Guid id)
        {
            var d = 0;
            var seen = new HashSet<Guid>();
            for (var p = parentOf.GetValueOrDefault(id); p is { } x && seen.Add(x); p = parentOf.GetValueOrDefault(x)) if (shownIds.Contains(x)) d++;
            return d;
        }

        Figures Sum(Func<(Guid? Cc, int Y, int M), bool> where)
        {
            var f = new Figures();
            foreach (var (k, v) in own) if (where(k)) f.Add(v);
            return f;
        }
        // A tree row carries its level (as the 0.14.0 tree reports do), so the page indents it and keeps the order; month rows don't.
        static Dictionary<string, object?> RowOf(string group, int? level, Figures f)
        {
            var row = new Dictionary<string, object?>
            {
                ["group"] = group, ["budget"] = Math.Round(f.Budget, 2), ["committed"] = Math.Round(f.Committed, 2), ["labor"] = Math.Round(f.Labor, 2),
                ["parts"] = Math.Round(f.Parts, 2), ["contractor"] = Math.Round(f.Contractor, 2), ["other"] = Math.Round(f.Other, 2), ["directIssues"] = Math.Round(f.Direct, 2),
                ["actual"] = Math.Round(f.Actual, 2), ["variance"] = Math.Round(f.Budget - f.Actual - f.Committed, 2),
            };
            if (level is not null) row["level"] = level;
            return row;
        }

        var rows = new List<Dictionary<string, object?>>();
        Figures total;
        if (a.GroupBy == "month")
        {
            // The chosen cost centre with its children, else every cost centre the viewer sees (with theirs) and cost with none.
            var scope = a.CostCentreId is { } c ? Subtree(c) : visible.SelectMany(v => Subtree(v.Id)).ToHashSet();
            var withNone = a.CostCentreId is null;
            total = new Figures();
            for (var m = firstMonth; m <= lastMonth; m++)
            {
                int y = m / 12, mo = m % 12 + 1;
                var f = Sum(k => k.Y == y && k.M == mo && (k.Cc is { } id ? scope.Contains(id) : withNone));
                rows.Add(RowOf(new DateOnly(y, mo, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), null, f));
                total.Add(f);
            }
        }
        else
        {
            total = new Figures();
            foreach (var c in shown.OrderBy(c => paths.GetValueOrDefault(c.Id)))
            {
                var mine = Subtree(c.Id);
                var f = Sum(k => k.Cc is { } id && mine.Contains(id));
                var depth = Depth(c.Id);
                rows.Add(RowOf(paths.GetValueOrDefault(c.Id)?.Split(" › ").Last() ?? "", depth, f));
                if (depth == 0) total.Add(f);
            }
            if (a.CostCentreId is null)
            {
                var none = Sum(k => k.Cc is null);
                rows.Add(RowOf(FourC.Cmms.Api.Infrastructure.Translator.T("No cost centre"), 0, none));
                total.Add(none);
            }
        }

        return new ReportResult("budget-vs-actual", "Budget against actual", $"{a.From:d MMM yyyy} – {a.To:d MMM yyyy}",
            [new(a.GroupBy == "month" ? "group" : "group", a.GroupBy == "month" ? "Month" : "Cost centre"), new("budget", "Budget", "currency"), new("committed", "Committed", "currency"),
             new("labor", "Labour", "currency"), new("parts", "Parts", "currency"), new("contractor", "Contractor", "currency"), new("other", "Other", "currency"),
             new("directIssues", "Direct issues", "currency"), new("actual", "Actual", "currency"), new("variance", "Variance", "currency")],
            rows, new Chart("bar", "group", ["budget", "actual", "committed"]),
            [new("Budget", Math.Round(total.Budget, 2), "currency"), new("Actual", Math.Round(total.Actual, 2), "currency"),
             new("Committed", Math.Round(total.Committed, 2), "currency"), new("Variance", Math.Round(total.Budget - total.Actual - total.Committed, 2), "currency")]);
    }

    /// <summary>
    /// How much of a month's budget a report range covers (0.29.1): all of it when the range holds the whole month, else the days it
    /// covers over the month's days (a range ending on day 15 of a 30-day month counts half).
    /// </summary>
    internal static decimal CoveredShare(int year, int month, DateOnly from, DateOnly to)
    {
        var first = new DateOnly(year, month, 1);
        var days = DateTime.DaysInMonth(year, month);
        var last = first.AddDays(days - 1);
        var start = from > first ? from : first;
        var end = to < last ? to : last;
        return end < start ? 0m : (end.DayNumber - start.DayNumber + 1) / (decimal)days;
    }
}
