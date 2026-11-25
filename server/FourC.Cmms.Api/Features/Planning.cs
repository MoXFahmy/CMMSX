using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Planned hours and what they cost.</summary>
public sealed record PlannedLabour(decimal Hours, decimal Cost);

/// <summary>A planned part line as the work order page shows it.</summary>
public sealed record PlannedPartView(Guid Id, Guid PartId, string PartNumber, string PartName, string UnitOfMeasure, Guid? StoreId, string? StoreCode,
    decimal Quantity, decimal Issued, decimal Reserved, decimal Short, decimal UnitCost, decimal PlannedCost, string? Notes,
    // 0.28.0: PartName is in the reader's language; a purchase request raised from the line takes this main name.
    string? MainPartName = null);

/// <summary>Queries behind planned parts and reservations, shared by the work order, inventory, purchasing and PM endpoints.</summary>
public static class Planning
{
    private static readonly WorkOrderStatus[] OpenStatuses = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold];

    /// <summary>
    /// The store a planned part defaults to: among active stores with a stock level for the part (any quantity), those on the
    /// work order's site first, then any site (a central store); most available, then most on hand, then store code.
    /// </summary>
    public static async Task<Guid?> DefaultStoreAsync(CmmsDbContext db, Guid partId, Guid siteId, CancellationToken ct)
    {
        // The site's own default store comes first (0.9.0).
        var siteDefault = await db.Sites.AllSites().Where(s => s.Id == siteId).Select(s => s.Settings).FirstOrDefaultAsync(ct);
        if (siteDefault?.DefaultStoreId is { } preferred && await db.Stores.AnyAsync(s => s.Id == preferred && s.IsActive, ct)) return preferred;
        var rows = await db.StockLevels.AsNoTracking().Where(l => l.PartId == partId && l.Store!.IsActive)
            .Select(l => new
            {
                l.StoreId, l.Store!.SiteId, l.Store.Code, l.QuantityOnHand,
                Reserved = db.StockReservations.Where(r => r.PartId == partId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0,
            }).ToListAsync(ct);
        return rows.OrderByDescending(r => r.SiteId == siteId).ThenByDescending(r => r.QuantityOnHand - r.Reserved).ThenByDescending(r => r.QuantityOnHand)
            .ThenBy(r => r.Code, StringComparer.Ordinal).Select(r => (Guid?)r.StoreId).FirstOrDefault();
    }

    /// <summary>The planned lines of open work orders holding a part in a store, with what each still reserves.</summary>
    public static async Task<List<PlanLine>> OpenLinesAsync(CmmsDbContext db, Guid partId, Guid storeId, CancellationToken ct)
    {
        var rows = await db.WorkOrderParts.AsNoTracking().Where(p => p.PartId == partId && p.StoreId == storeId)
            .Join(db.WorkOrders.Where(w => OpenStatuses.Contains(w.Status)), p => p.WorkOrderId, w => w.Id, (p, w) => new
            {
                p.Id, p.Quantity, WorkOrderId = w.Id, w.Number, w.DueDate, w.PlannedStart, w.Status,
                Net = -(db.StockTransactions.Where(t => t.WorkOrderId == w.Id && t.PartId == partId && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return))
                    .Sum(t => (decimal?)t.Quantity) ?? 0),
            }).ToListAsync(ct);
        return rows.Select(r => new PlanLine(r.Id, r.WorkOrderId, r.Number, r.DueDate, r.PlannedStart, Reservations.StillReserved(r.Quantity, r.Net, r.Status))).ToList();
    }

    /// <summary>
    /// <see cref="OpenLinesAsync(CmmsDbContext, Guid, Guid, CancellationToken)"/> for several parts and stores in one query,
    /// by (part, store).
    /// </summary>
    public static async Task<ILookup<(Guid PartId, Guid StoreId), PlanLine>> OpenLinesAsync(CmmsDbContext db, IReadOnlyCollection<Guid> partIds,
        IReadOnlyCollection<Guid> storeIds, CancellationToken ct)
    {
        var rows = await db.WorkOrderParts.AsNoTracking().Where(p => partIds.Contains(p.PartId) && p.StoreId != null && storeIds.Contains(p.StoreId.Value))
            .Join(db.WorkOrders.Where(w => OpenStatuses.Contains(w.Status)), p => p.WorkOrderId, w => w.Id, (p, w) => new
            {
                p.Id, p.PartId, StoreId = p.StoreId!.Value, p.Quantity, WorkOrderId = w.Id, w.Number, w.DueDate, w.PlannedStart, w.Status,
                Net = -(db.StockTransactions.Where(t => t.WorkOrderId == w.Id && t.PartId == p.PartId && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return))
                    .Sum(t => (decimal?)t.Quantity) ?? 0),
            }).ToListAsync(ct);
        return rows.ToLookup(r => (r.PartId, r.StoreId),
            r => new PlanLine(r.Id, r.WorkOrderId, r.Number, r.DueDate, r.PlannedStart, Reservations.StillReserved(r.Quantity, r.Net, r.Status)));
    }

    /// <summary>Net quantity issued to a work order per part (issues less returns), from any store.</summary>
    public static async Task<Dictionary<Guid, decimal>> NetIssuedAsync(CmmsDbContext db, Guid workOrderId, CancellationToken ct) =>
        (await db.StockTransactions.AsNoTracking().Where(t => t.WorkOrderId == workOrderId && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return))
            .GroupBy(t => t.PartId).Select(g => new { g.Key, Net = -g.Sum(t => t.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Net);

    /// <summary>A store level's on-hand stock (0 when the part has no level there).</summary>
    public static async Task<decimal> OnHandAsync(CmmsDbContext db, Guid partId, Guid storeId, CancellationToken ct) =>
        await db.StockLevels.AsNoTracking().Where(l => l.PartId == partId && l.StoreId == storeId).Select(l => (decimal?)l.QuantityOnHand).FirstOrDefaultAsync(ct) ?? 0;

    /// <summary>A work order's planned lines with issued, still reserved and short quantities and the planned cost.</summary>
    public static async Task<List<PlannedPartView>> LinesAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        var lines = await db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrderId == w.Id).OrderBy(p => p.Part!.PartNumber)
            .Select(p => new { p.Id, p.PartId, p.Part!.PartNumber, Name = Bilingual.Arabic && p.Part.NameAr != null ? p.Part.NameAr : p.Part.Name, MainName = p.Part.Name, p.Part.UnitOfMeasure, p.Part.AverageCost, p.StoreId, StoreCode = p.Store != null ? p.Store.Code : null, p.Quantity, p.Notes })
            .ToListAsync(ct);
        if (lines.Count == 0) return [];
        var issued = await NetIssuedAsync(db, w.Id, ct);
        // Stock and the other open work holding these parts, fetched once for all lines (not two queries per line).
        var partIds = lines.Where(l => l.StoreId != null).Select(l => l.PartId).Distinct().ToList();
        var storeIds = lines.Where(l => l.StoreId != null).Select(l => l.StoreId!.Value).Distinct().ToList();
        var onHand = partIds.Count == 0 ? [] : (await db.StockLevels.AsNoTracking().Where(l => partIds.Contains(l.PartId) && storeIds.Contains(l.StoreId))
            .Select(l => new { l.PartId, l.StoreId, l.QuantityOnHand }).ToListAsync(ct)).ToDictionary(x => (x.PartId, x.StoreId), x => x.QuantityOnHand);
        var open = partIds.Count == 0 ? null : await OpenLinesAsync(db, partIds, storeIds, ct);
        var result = new List<PlannedPartView>();
        foreach (var l in lines)
        {
            var net = issued.GetValueOrDefault(l.PartId);
            var reserved = l.StoreId is null ? 0 : Reservations.StillReserved(l.Quantity, net, w.Status);
            var shortQty = 0m;
            if (l.StoreId is { } store && reserved > 0 && open is not null)
                shortQty = Reservations.Shortages(onHand.GetValueOrDefault((l.PartId, store)), open[(l.PartId, store)]).GetValueOrDefault(l.Id);
            result.Add(new PlannedPartView(l.Id, l.PartId, l.PartNumber, l.Name, l.UnitOfMeasure, l.StoreId, l.StoreCode, l.Quantity, net, reserved, shortQty,
                l.AverageCost, Math.Round(l.Quantity * l.AverageCost, 2), l.Notes, l.MainName));
        }
        return result;
    }

    /// <summary>Average of the known hourly rates (technicians without a rate are left out); 0 when none is known.</summary>
    public static decimal AverageRate(IEnumerable<decimal?> rates)
    {
        var known = rates.Where(r => r is not null).Select(r => r!.Value).ToList();
        return known.Count == 0 ? 0 : known.Average();
    }

    /// <summary>
    /// Planned labour: the technicians' planned hours at their own rates; when nobody has planned hours, the work order's estimate
    /// at the average rate of its technicians.
    /// </summary>
    public static PlannedLabour Labour(IReadOnlyCollection<(decimal? Planned, decimal? Rate)> assignees, decimal? estimatedHours)
    {
        if (assignees.Any(a => a.Planned is not null))
            return new PlannedLabour(assignees.Sum(a => a.Planned ?? 0), Math.Round(assignees.Sum(a => (a.Planned ?? 0) * (a.Rate ?? 0)), 2));
        var hours = estimatedHours ?? 0;
        return new PlannedLabour(hours, Math.Round(hours * AverageRate(assignees.Select(a => a.Rate)), 2));
    }

    /// <summary>Copies another work order's planned parts (stores re-picked for the new site) and the planned hours of technicians on both.</summary>
    public static async Task CopyPlanAsync(CmmsDbContext db, Guid fromWorkOrderId, WorkOrder to, CancellationToken ct)
    {
        var lines = await db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrderId == fromWorkOrderId).ToListAsync(ct);
        foreach (var l in lines)
            db.WorkOrderParts.Add(new WorkOrderPart { WorkOrderId = to.Id, PartId = l.PartId, StoreId = await DefaultStoreAsync(db, l.PartId, to.SiteId, ct), Quantity = l.Quantity, Notes = l.Notes });
        var hours = await db.WorkOrderAssignees.AsNoTracking().Where(a => a.WorkOrderId == fromWorkOrderId && a.PlannedHours != null)
            .ToDictionaryAsync(a => a.EmployeeId, a => a.PlannedHours, ct);
        foreach (var a in to.Assignees) if (hours.TryGetValue(a.EmployeeId, out var h)) a.PlannedHours = h;
    }
}
