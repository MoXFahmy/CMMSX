using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Core.Inventory;

/// <summary>A planned line of an open work order, as the reservation rules see it.</summary>
/// <param name="StillReserved">Planned less net issued, never below 0 (see <see cref="Reservations.StillReserved"/>).</param>
public sealed record PlanLine(Guid LineId, Guid WorkOrderId, string WorkOrderNumber, DateOnly? DueDate, DateOnly? PlannedStart, decimal StillReserved);

/// <summary>A work order that a stock move would leave short, or more short, and by how much more.</summary>
public sealed record ShortfallChange(Guid WorkOrderId, string WorkOrderNumber, decimal MoreShort);

/// <summary>
/// Stock reserved for open work, per part and store, as the database view <c>StockReservations</c> sums it. Read-only:
/// the view applies <see cref="Reservations.StillReserved"/> in SQL, so the figure is always derived, never stored.
/// </summary>
public class StockReservation : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PartId { get; set; }
    public Guid StoreId { get; set; }
    public decimal Reserved { get; set; }
}

/// <summary>The reservation rules — pure, no database access.</summary>
public static class Reservations
{
    public const string ViewName = "StockReservations";

    /// <summary>What a planned line still holds: planned less net issued, never below 0, and only while the work order is open.</summary>
    public static decimal StillReserved(decimal planned, decimal netIssued, WorkOrderStatus status) =>
        WorkOrderWorkflow.IsActive(status) ? Math.Max(planned - netIssued, 0m) : 0m;

    /// <summary>
    /// How short each line is when a store's on-hand stock goes to its open jobs in order of need: due date, then planned
    /// start, then work order number (undated last). On hand below zero counts as none.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> Shortages(decimal onHand, IEnumerable<PlanLine> lines)
    {
        var left = Math.Max(onHand, 0m);
        var result = new Dictionary<Guid, decimal>();
        foreach (var l in InOrderOfNeed(lines))
        {
            var covered = Math.Min(left, l.StillReserved);
            left -= covered;
            result[l.LineId] = l.StillReserved - covered;
        }
        return result;
    }

    /// <summary>
    /// The other work orders that taking <paramref name="quantity"/> out of the store would leave short or more short.
    /// A move to a work order uses that work order's own still-reserved quantity first.
    /// </summary>
    public static IReadOnlyList<ShortfallChange> WouldShort(decimal onHand, IReadOnlyList<PlanLine> lines, decimal quantity, Guid? toWorkOrderId)
    {
        var before = ShortByWorkOrder(Shortages(onHand, lines), lines);
        var own = quantity;
        var after = lines.Select(l =>
        {
            if (l.WorkOrderId != toWorkOrderId || own <= 0) return l;
            var used = Math.Min(own, l.StillReserved);
            own -= used;
            return l with { StillReserved = l.StillReserved - used };
        }).ToList();
        var now = ShortByWorkOrder(Shortages(onHand - quantity, after), after);
        return lines.Where(l => l.WorkOrderId != toWorkOrderId).DistinctBy(l => l.WorkOrderId)
            .Select(l => new ShortfallChange(l.WorkOrderId, l.WorkOrderNumber, now.GetValueOrDefault(l.WorkOrderId) - before.GetValueOrDefault(l.WorkOrderId)))
            .Where(c => c.MoreShort > 0).ToList();
    }

    private static IEnumerable<PlanLine> InOrderOfNeed(IEnumerable<PlanLine> lines) =>
        lines.OrderBy(l => l.DueDate ?? DateOnly.MaxValue).ThenBy(l => l.PlannedStart ?? DateOnly.MaxValue).ThenBy(l => l.WorkOrderNumber, StringComparer.Ordinal);

    private static Dictionary<Guid, decimal> ShortByWorkOrder(IReadOnlyDictionary<Guid, decimal> shortages, IEnumerable<PlanLine> lines) =>
        lines.GroupBy(l => l.WorkOrderId).ToDictionary(g => g.Key, g => g.Sum(l => shortages.GetValueOrDefault(l.LineId)));
}
