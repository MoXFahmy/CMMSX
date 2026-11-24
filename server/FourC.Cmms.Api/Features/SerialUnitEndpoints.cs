using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FourC.Cmms.Api.Features;

/// <summary>Serial-numbered parts (0.24.0): switching tracking on, registering, fitting, removing and moving units.</summary>
public static class SerialUnitEndpoints
{
    public sealed record StoreSerials(Guid StoreId, List<string>? Serials);
    public sealed record SerialTrackingInput(bool On, int? UnitLifeMonths, List<StoreSerials>? Stores);
    /// <param name="CountHours">The unit's running hours follow the asset's hours meter (<paramref name="HoursMeterId"/>, or its first one in hours).</param>
    public sealed record FitInput(Guid UnitId, Guid AssetId, bool CountHours = true, Guid? HoursMeterId = null);
    /// <param name="Outcome">Store, Repair or Scrap.</param>
    public sealed record RemoveInput(Guid? WorkOrderId, string? Outcome, Guid? StoreId, string? Note);

    /// <summary>The asset a work order is on, or a round's assets.</summary>
    internal static async Task<HashSet<Guid>> WorkOrderAssetsAsync(CmmsDbContext db, WorkOrder wo, CancellationToken ct)
    {
        var ids = await db.WorkOrderAssets.Where(x => x.WorkOrderId == wo.Id).Select(x => x.AssetId).ToListAsync(ct);
        if (wo.AssetId is { } a) ids.Add(a);
        return [.. ids];
    }

    internal static UnitOutcome? OutcomeOf(string? text) =>
        Enum.TryParse<UnitOutcome>(text?.Trim(), ignoreCase: true, out var o) && Enum.IsDefined(o) && !int.TryParse(text, out _) ? o : null;

    public static void MapSerialUnitEndpoints(this RouteGroupBuilder api)
    {
        // Serial tracking on an existing part: on, with a serial for each piece in stock; off only when no unit is left.
        api.MapPost("/parts/{id:guid}/serial-tracking", async (Guid id, SerialTrackingInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (part is null) return Results.NotFound();
            UnitRules.CheckLife(i.UnitLifeMonths);
            if (!i.On)
            {
                if (await db.Assets.AllSites().AnyAsync(a => a.PartId == id && a.Status != AssetStatus.Disposed, ct))
                    throw new DomainException("This part still has units that aren't scrapped.");
                part.IsSerialTracked = false;
                part.UnitLifeMonths = i.UnitLifeMonths;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { part.Id, part.IsSerialTracked });
            }
            part.UnitLifeMonths = i.UnitLifeMonths;
            if (!part.IsSerialTracked)
            {
                var given = (i.Stores ?? []).ToDictionary(s => s.StoreId, s => UnitRules.CleanSerials(s.Serials ?? []));
                var levels = await db.StockLevels.Where(l => l.PartId == id && l.QuantityOnHand != 0).Select(l => new { l.StoreId, l.QuantityOnHand, l.Store!.Code }).ToListAsync(ct);
                foreach (var l in levels)
                    if (l.QuantityOnHand != Math.Floor(l.QuantityOnHand) || given.GetValueOrDefault(l.StoreId)?.Count != (int)l.QuantityOnHand)
                        throw new DomainException($"Enter one serial number for each piece in stock: {l.Code} has {l.QuantityOnHand:0.###}.");
                var all = levels.SelectMany(l => given[l.StoreId]).ToList();
                await SerialUnits.CheckNewSerialsAsync(db, part, all, all.Count, ct);
                await db.InTransactionAsync(async tk =>
                {
                    part.IsSerialTracked = true;
                    foreach (var l in levels)
                    {
                        var store = await db.Stores.FirstAsync(s => s.Id == l.StoreId, tk);
                        await SerialUnits.RegisterInStoreAsync(db, ctx, part, store, given[l.StoreId], moveStock: false, "Serial tracking switched on", tk);
                    }
                    await db.SaveChangesAsync(tk);
                    return true;
                }, ct);
                return Results.Ok(new { part.Id, part.IsSerialTracked });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { part.Id, part.IsSerialTracked });
        }).RequirePermission(Permissions.InventoryManage);

        // ---- Fitting and removing on a work order ----
        api.MapGet("/work-orders/{id:guid}/units", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var wo = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
            if (wo is null) return Results.NotFound();
            var assets = await WorkOrderAssetsAsync(db, wo, ct);
            var rows = await db.Assets.AllSites().AsNoTracking()
                .Where(a => a.PartId != null && a.ParentAssetId != null && assets.Contains(a.ParentAssetId.Value) && a.Status != AssetStatus.Disposed && a.Status != AssetStatus.UnderRepair)
                .OrderBy(a => a.AssetNumber).Select(a => new
                {
                    a.Id, a.AssetNumber, a.SerialNumber, a.PartId, PartNumber = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.PartNumber).FirstOrDefault(),
                    PartName = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.Name).FirstOrDefault(), FittedToId = a.ParentAssetId,
                    Since = db.UnitMovements.Where(m => m.UnitId == a.Id && m.Kind == UnitMovementKind.Fitted).Max(m => (DateTimeOffset?)m.At),
                }).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequirePermission(Permissions.WorkOrdersView);

        api.MapPost("/work-orders/{id:guid}/units/fit", async (Guid id, FitInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            await WorkOrderEndpoints.RequireOpenAsync(db, id, ct);
            var wo = await db.WorkOrders.FirstAsync(w => w.Id == id, ct);
            if (!(await WorkOrderAssetsAsync(db, wo, ct)).Contains(i.AssetId)) throw new DomainException("Choose an asset of this work order.");
            var target = await db.Assets.FirstOrDefaultAsync(a => a.Id == i.AssetId, ct) ?? throw new DomainException("Choose an asset of this work order.");
            var unit = await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == i.UnitId && a.PartId != null, ct) ?? throw new DomainException("Choose a unit.");
            await db.InTransactionAsync(async tk =>
            {
                await SerialUnits.FitAsync(db, ctx, unit, target, wo, i.CountHours, i.HoursMeterId, DateTimeOffset.UtcNow, tk);
                await db.SaveChangesAsync(tk);
                return true;
            }, ct);
            return Results.Ok(new { unit.Id });
        }).RequirePermission(Permissions.WorkOrdersExecute).RequirePermission(Permissions.InventoryTransact);

        api.MapPost("/serial-units/{id:guid}/remove", async (Guid id, RemoveInput i, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms,
            CancellationToken ct) =>
        {
            var outcome = OutcomeOf(i.Outcome) ?? throw new DomainException("Choose what happens to the unit.");
            var unit = await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == id && a.PartId != null, ct);
            if (unit is null) return Results.NotFound();
            WorkOrder? wo = null;
            if (i.WorkOrderId is { } woId)
            {
                // On a work order: the person works on it, it's open, and the unit is on one of its assets.
                if (!(await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!))).Contains(Permissions.WorkOrdersExecute)) return Results.Forbid();
                await WorkOrderEndpoints.RequireOpenAsync(db, woId, ct);
                wo = await db.WorkOrders.FirstAsync(w => w.Id == woId, ct);
                if (unit.ParentAssetId is not { } parent || !(await WorkOrderAssetsAsync(db, wo, ct)).Contains(parent))
                    throw new DomainException("This unit isn't fitted to this work order's asset.");
            }
            await db.InTransactionAsync(async tk =>
            {
                await SerialUnits.RemoveAsync(db, ctx, unit, wo, outcome, i.StoreId, i.Note, DateTimeOffset.UtcNow, tk);
                await db.SaveChangesAsync(tk);
                return true;
            }, ct);
            return Results.Ok(new { unit.Id });
        }).RequirePermission(Permissions.InventoryTransact);
    }

    /// <summary>
    /// Serial numbers for a receipt of <paramref name="pieces"/> pieces of a serial part: whole pieces, one serial each, all new.
    /// <paramref name="prefix"/> names the order line on a multi-line receipt ("Line 2: ").
    /// </summary>
    internal static async Task<IReadOnlyList<string>> ReceiptSerialsAsync(CmmsDbContext db, Part part, decimal pieces, List<string>? serials, CancellationToken ct, string? prefix = null)
    {
        try
        {
            if (pieces != Math.Floor(pieces) || pieces <= 0) throw new DomainException("Serial parts are received in whole pieces.");
            var clean = UnitRules.CleanSerials(serials ?? []);
            await SerialUnits.CheckNewSerialsAsync(db, part, clean, (int)pieces, ct);
            return clean;
        }
        catch (DomainException e) when (prefix is not null) { throw new DomainException(prefix + e.Message); }
    }
}
