using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Every move of a serial unit (0.24.0). A unit is an asset with a part; each move writes its <see cref="UnitMovement"/> and the
/// matching stock ledger entry (flagged <c>UnitMove</c>, the only way the ledger lets a serial part's stock move), so the part's
/// quantity in each store always equals its units there. The caller saves, inside its transaction.
/// </summary>
public static class SerialUnits
{
    private static void Move(CmmsDbContext db, ITenantContext ctx, Asset unit, UnitMovementKind kind, DateTimeOffset at, Guid? storeId = null, Guid? assetId = null,
        Guid? workOrderId = null, Guid? purchaseOrderId = null, Guid? lineId = null, UnitOutcome? outcome = null, string? note = null, decimal? cost = null) =>
        db.UnitMovements.Add(new UnitMovement
        {
            TenantId = unit.TenantId != Guid.Empty ? unit.TenantId : db.CurrentTenantId ?? Guid.Empty, UnitId = unit.Id, Kind = kind, At = at, StoreId = storeId,
            AssetId = assetId, WorkOrderId = workOrderId, PurchaseOrderId = purchaseOrderId, PurchaseOrderLineId = lineId, Outcome = outcome,
            Note = note.Clean() is { } n && n.Length > 500 ? n[..500] : note.Clean(), Cost = cost, ByUserId = ctx.UserId, ByName = ctx.UserName,
        });

    /// <summary>
    /// Checks serial numbers for new units of a part: <paramref name="expected"/> of them, all different (ignoring case, as the
    /// database compares them) and not used by a live unit of the part.
    /// </summary>
    public static async Task CheckNewSerialsAsync(CmmsDbContext db, Part part, IReadOnlyList<string> serials, int expected, CancellationToken ct)
    {
        if (serials.Count != expected)
            throw new DomainException(serials.Count == 0 || expected == 1 ? StockLedger.SerialRefusal(StockTransactionType.Receipt) : $"Enter {expected} serial numbers, one for each piece received.");
        if (serials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != serials.Count) throw new DomainException("Serial numbers must be different.");
        if (serials.Any(s => s.Length > 200)) throw new DomainException("A serial number can be at most 200 characters.");
        // Units added earlier in the same request (another line of one receipt) aren't saved yet: check them too.
        var used = db.Assets.Local.Where(a => a.PartId == part.Id && a.SerialNumber is not null && !a.IsDeleted)
            .Select(a => a.SerialNumber).FirstOrDefault(n => serials.Contains(n!, StringComparer.OrdinalIgnoreCase))
            ?? await db.Assets.AllSites().Where(a => a.PartId == part.Id && a.SerialNumber != null && serials.Contains(a.SerialNumber)).Select(a => a.SerialNumber).FirstOrDefaultAsync(ct);
        if (used is not null) throw new DomainException($"Serial number {serials.First(s => string.Equals(s, used, StringComparison.OrdinalIgnoreCase))} is already used by a unit of this part.");
    }

    private static async Task<FourC.Cmms.Core.Tenancy.TenantSettings> SettingsAsync(CmmsDbContext db, CancellationToken ct) =>
        (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;

    /// <summary>A new unit record: an asset at the store's site, named after the part, in the store.</summary>
    private static async Task<Asset> NewUnitAsync(CmmsDbContext db, FourC.Cmms.Core.Tenancy.TenantSettings settings, Part part, Store store, string serial, decimal cost,
        DateOnly date, CancellationToken ct)
    {
        var unit = new Asset
        {
            AssetNumber = await AssetEndpoints.NextAssetNumberAsync(db, settings, ct, store.SiteId), Name = part.Name, SiteId = store.SiteId,
            Status = AssetStatus.InStorage, PartId = part.Id, StoreId = store.Id, SerialNumber = serial, Manufacturer = part.Manufacturer,
            Model = part.ManufacturerPartNumber, PurchasePrice = cost, EndOfLifeOn = UnitRules.EndOfLife(date, part.UnitLifeMonths),
        };
        db.Assets.Add(unit);
        return unit;
    }

    /// <summary>Received units: one receipt entry for all of them (with the template's supplier, order and reference) and one unit per serial.</summary>
    public static async Task<List<Asset>> ReceiveAsync(CmmsDbContext db, ITenantContext ctx, Part part, Store store, IReadOnlyList<string> serials, DateOnly date,
        decimal unitCost, StockPost template, CancellationToken ct)
    {
        await StockLedger.PostAsync(db, ctx, template with { Type = StockTransactionType.Receipt, PartId = part.Id, StoreId = store.Id, Quantity = serials.Count, Date = date, UnitCost = unitCost, UnitMove = true }, ct);
        var settings = await SettingsAsync(db, ct);
        var units = new List<Asset>();
        var at = DateTimeOffset.UtcNow;
        foreach (var serial in serials)
        {
            var unit = await NewUnitAsync(db, settings, part, store, serial, unitCost, date, ct);
            Move(db, ctx, unit, UnitMovementKind.Received, at, storeId: store.Id, purchaseOrderId: template.PurchaseOrderId, lineId: template.PurchaseOrderLineId);
            units.Add(unit);
        }
        return units;
    }

    /// <summary>
    /// Units registered for pieces already in a store: when serial tracking is switched on (<paramref name="moveStock"/> false: the
    /// stock is already counted) or for a piece found in the store (true: stock +1 each).
    /// </summary>
    public static async Task<List<Asset>> RegisterInStoreAsync(CmmsDbContext db, ITenantContext ctx, Part part, Store store, IReadOnlyList<string> serials, bool moveStock,
        string? note, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        if (moveStock)
            await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Adjustment, part.Id, store.Id, serials.Count, date, Notes: note.Clean() ?? "Unit found", UnitMove: true), ct);
        var settings = await SettingsAsync(db, ct);
        var units = new List<Asset>();
        var at = DateTimeOffset.UtcNow;
        foreach (var serial in serials)
        {
            var unit = await NewUnitAsync(db, settings, part, store, serial, part.AverageCost, date, ct);
            Move(db, ctx, unit, UnitMovementKind.Registered, at, storeId: store.Id, note: note);
            units.Add(unit);
        }
        return units;
    }

    /// <summary>
    /// Fits a unit from its store onto <paramref name="target"/>: an issue of 1 (to the work order when there is one), the unit takes
    /// the target's site and location, and, when <paramref name="countHours"/>, its running-hours meter follows the target's hours meter.
    /// </summary>
    public static async Task FitAsync(CmmsDbContext db, ITenantContext ctx, Asset unit, Asset target, WorkOrder? wo, bool countHours, Guid? hoursMeterId,
        DateTimeOffset at, CancellationToken ct)
    {
        if (unit.PartId is not { } partId || UnitRules.StateOf(unit) != UnitState.InStore || unit.StoreId is not { } storeId)
            throw new DomainException("This unit isn't in a store.");
        var store = await db.Stores.AllSites().AsNoTracking().FirstAsync(s => s.Id == storeId, ct);
        if (store.SiteId != target.SiteId) throw new DomainException("Move the unit to a store at that site first.");
        if (await IsUnitOrUnderAsync(db, target, unit.Id, ct)) throw new DomainException("A unit can't be fitted to itself or to one of its own units.");
        Meter? source = null;
        if (countHours)
            source = hoursMeterId is { } mid
                ? await db.Meters.AllSites().FirstOrDefaultAsync(m => m.Id == mid && m.AssetId == target.Id, ct) ?? throw new DomainException("Choose a meter of that asset.")
                : (await db.Meters.AllSites().Where(m => m.AssetId == target.Id && m.IsActive).OrderBy(m => m.Code).ToListAsync(ct)).FirstOrDefault(m => UnitRules.IsHoursUnit(m.Unit));

        await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Issue, partId, storeId, 1, DateOnly.FromDateTime(at.UtcDateTime), WorkOrderId: wo?.Id,
            LocationId: wo is null ? target.LocationId : null, Notes: $"Unit {unit.SerialNumber} fitted to {target.AssetNumber}", UnitMove: true), ct);
        unit.ParentAssetId = target.Id;
        unit.SiteId = target.SiteId;
        unit.LocationId = target.LocationId;
        unit.StoreId = null;
        unit.Status = AssetStatus.Active;
        Move(db, ctx, unit, UnitMovementKind.Fitted, at, storeId: storeId, assetId: target.Id, workOrderId: wo?.Id);

        if (source is not null)
        {
            var own = (await db.Meters.AllSites().Where(m => m.AssetId == unit.Id).ToListAsync(ct)).FirstOrDefault(m => UnitRules.IsHoursUnit(m.Unit));
            if (own is null)
            {
                // Meter codes are the organization's: the unit's asset number keeps each unit's own.
                var code = $"RH-{unit.AssetNumber}";
                own = new Meter { Code = code.Length > 50 ? code[..50] : code, Name = "Running hours", AssetId = unit.Id, Unit = "h" };
                db.Meters.Add(own);
            }
            own.SourceMeterId = source.Id;
            own.IsActive = true;
        }
    }

    /// <summary>The target is the unit itself or sits (at any depth) under it.</summary>
    private static async Task<bool> IsUnitOrUnderAsync(CmmsDbContext db, Asset target, Guid unitId, CancellationToken ct)
    {
        Guid? id = target.Id;
        for (var depth = 0; id is { } current && depth < 50; depth++)
        {
            if (current == unitId) return true;
            id = await db.Assets.AllSites().Where(a => a.Id == current).Select(a => a.ParentAssetId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    /// <summary>
    /// Takes a fitted unit off its asset: back to a store (a return of 1 that names no work order: the job that fitted it keeps its
    /// cost), out for repair, or scrapped. Its running hours stop following the asset.
    /// </summary>
    public static async Task RemoveAsync(CmmsDbContext db, ITenantContext ctx, Asset unit, WorkOrder? wo, UnitOutcome outcome, Guid? storeId, string? note,
        DateTimeOffset at, CancellationToken ct)
    {
        if (unit.PartId is not { } partId || UnitRules.StateOf(unit) != UnitState.Fitted || unit.ParentAssetId is not { } from)
            throw new DomainException("This unit isn't fitted.");
        Store? store = null;
        if (outcome == UnitOutcome.Store)
            store = storeId is { } sid
                ? await db.Stores.FirstOrDefaultAsync(s => s.Id == sid, ct) ?? throw new DomainException("Store not found.")
                : throw new DomainException("Choose the store it goes back to.");
        foreach (var m in await db.Meters.AllSites().Where(m => m.AssetId == unit.Id && m.SourceMeterId != null).ToListAsync(ct))
            if (UnitRules.IsHoursUnit(m.Unit)) m.SourceMeterId = null;

        unit.ParentAssetId = null;
        unit.LocationId = null;
        switch (outcome)
        {
            case UnitOutcome.Store:
                await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Return, partId, store!.Id, 1, DateOnly.FromDateTime(at.UtcDateTime),
                    Notes: $"Unit {unit.SerialNumber} removed", UnitMove: true), ct);
                unit.StoreId = store.Id;
                unit.SiteId = store.SiteId;
                unit.Status = AssetStatus.InStorage;
                break;
            case UnitOutcome.Repair:
                unit.Status = AssetStatus.UnderRepair;
                break;
            default:
                unit.Status = AssetStatus.Disposed;
                unit.DisposedOn = await ContractEndpoints.TodayAsync(db, ct);
                break;
        }
        Move(db, ctx, unit, UnitMovementKind.Removed, at, storeId: store?.Id, assetId: from, workOrderId: wo?.Id, outcome: outcome, note: note);
    }
}
