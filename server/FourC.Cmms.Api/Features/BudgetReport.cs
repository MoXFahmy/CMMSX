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

        var lines = await CostLinesAsync(db, db.WorkOrders.AllSites().AsNoTracking(), a.From, a.To, ct);
        var woIds = lines.Select(l => l.WoId).Distinct().ToList();
        var centreOf = new Dictionary<Guid, Guid?>();
        foreach (var chunk in woIds.Chunk(1000))
            foreach (var w in await db.WorkOrders.AllSites().AsNoTracking().Where(w => chunk.Contains(w.Id)).Select(w => new { w.Id, w.CostCentreId }).ToListAsync(ct))
                centreOf[w.Id] = w.CostCentreId;
        foreach (var l in lines)
        {
            var f = At(centreOf.GetValueOrDefault(l.WoId), l.Y, l.M);
            f.Labor += l.Labor; f.Parts += l.Parts; f.Contractor += l.Contractor; f.Other += l.Other;
        }

        var direct = await db.StockTransactions.AsNoTracking()
            .Where(t => t.WorkOrderId == null && t.LocationId != null && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return)
                        && t.Date >= a.From && t.Date <= a.To)
            .GroupBy(t => new { t.CostCentreId, t.Date.Year, t.Date.Month })
            .Select(g => new { g.Key.CostCentreId, g.Key.Year, g.Key.Month, Amount = g.Sum(t => -t.Quantity * t.UnitCost) }).ToListAsync(ct);
        foreach (var d in direct) At(d.CostCentreId, d.Year, d.Month).Direct += d.Amount;

        // Committed: the open part of approved order lines for work orders, dated by the order's expected date, else its order date.
        var open = await (from l in db.PurchaseOrderLines.AsNoTracking()
                          join o in db.PurchaseOrders.AllSites().AsNoTracking() on l.PurchaseOrderId equals o.Id
                          join w in db.WorkOrders.AllSites().AsNoTracking() on l.WorkOrderId equals w.Id
                          where (o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.PartiallyReceived) && l.Quantity > l.QuantityReceived
                                && (o.ExpectedDate ?? o.OrderDate) >= a.From && (o.ExpectedDate ?? o.OrderDate) <= a.To
                          select new { w.CostCentreId, Day = o.ExpectedDate ?? o.OrderDate, l.Quantity, l.QuantityReceived, l.UnitPrice, l.DiscountPercent, o.ExchangeRate })
            .ToListAsync(ct);
        foreach (var c in open)
            At(c.CostCentreId, c.Day.Year, c.Day.Month).Committed +=
                Math.Round((c.Quantity - c.QuantityReceived) * Math.Round(c.UnitPrice * (1 - c.DiscountPercent / 100m), 4) * c.ExchangeRate, 2);

        var firstMonth = a.From.Year * 12 + a.From.Month - 1;
        var lastMonth = a.To.Year * 12 + a.To.Month - 1;
        foreach (var b in await db.Budgets.AsNoTracking().Where(b => b.Year * 12 + b.Month - 1 >= firstMonth && b.Year * 12 + b.Month - 1 <= lastMonth).ToListAsync(ct))
            At(b.CostCentreId, b.Year, b.Month).Budget += b.Amount;

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
        static Dictionary<string, object?> RowOf(string group, int depth, Figures f) => new()
        {
            ["group"] = group, ["depth"] = depth, ["budget"] = Math.Round(f.Budget, 2), ["committed"] = Math.Round(f.Committed, 2), ["labor"] = Math.Round(f.Labor, 2),
            ["parts"] = Math.Round(f.Parts, 2), ["contractor"] = Math.Round(f.Contractor, 2), ["other"] = Math.Round(f.Other, 2), ["directIssues"] = Math.Round(f.Direct, 2),
            ["actual"] = Math.Round(f.Actual, 2), ["variance"] = Math.Round(f.Budget - f.Actual - f.Committed, 2),
        };

        var rows = new List<Dictionary<string, object?>>();
        Figures total;
        if (a.GroupBy == "month")
        {
            HashSet<Guid>? scope = a.CostCentreId is { } c ? Subtree(c) : null;
            total = new Figures();
            for (var m = firstMonth; m <= lastMonth; m++)
            {
                int y = m / 12, mo = m % 12 + 1;
                var f = Sum(k => k.Y == y && k.M == mo && (scope is null || (k.Cc is { } id && scope.Contains(id))));
                rows.Add(RowOf(new DateOnly(y, mo, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), 0, f));
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
}
