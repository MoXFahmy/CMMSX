using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
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
        if (serials.Count != expected) throw new DomainException($"Enter {expected} serial numbers, one for each piece received.");
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
}
