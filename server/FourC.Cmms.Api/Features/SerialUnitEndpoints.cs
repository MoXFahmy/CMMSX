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

    public sealed record StoreMoveInput(Guid? StoreId, string? Note);
    public sealed record NoteInput(string? Note);
    public sealed record BackInput(Guid? StoreId, decimal? Cost, string? Note);
    public sealed record FoundInput(Guid? StoreId, string? Serial, string? Note);
    public sealed record FittedInput(Guid? AssetId, string? Serial, string? Note);
    public sealed record MakeUnitInput(Guid? PartId);

    /// <summary>A unit as the lists show it. <c>State</c>: InStore, Fitted, AtRepair or Scrapped.</summary>
    public sealed record UnitRow(Guid Id, string AssetNumber, string? SerialNumber, Guid? PartId, string? PartNumber, string? PartName, string State,
        Guid? StoreId, string? StoreName, Guid? FittedToId, string? FittedToNumber, string? FittedToName, Guid SiteId, string? SiteName, DateOnly? EndOfLifeOn,
        int RepairCount, decimal RepairCost);

    private static IQueryable<UnitRow> Rows(CmmsDbContext db, IQueryable<Asset> q) => q.Select(a => new UnitRow(
        a.Id, a.AssetNumber, a.SerialNumber, a.PartId,
        db.Parts.Where(p => p.Id == a.PartId).Select(p => p.PartNumber).FirstOrDefault(), db.Parts.Where(p => p.Id == a.PartId).Select(p => p.Name).FirstOrDefault(),
        a.Status == AssetStatus.Disposed ? "Scrapped" : a.Status == AssetStatus.UnderRepair ? "AtRepair" : a.ParentAssetId != null ? "Fitted" : "InStore",
        a.StoreId, db.Stores.AllSites().Where(x => x.Id == a.StoreId).Select(x => x.Name).FirstOrDefault(),
        a.ParentAssetId, db.Assets.AllSites().Where(x => x.Id == a.ParentAssetId).Select(x => x.AssetNumber).FirstOrDefault(),
        db.Assets.AllSites().Where(x => x.Id == a.ParentAssetId).Select(x => x.Name).FirstOrDefault(),
        a.SiteId, db.Sites.AllSites().Where(x => x.Id == a.SiteId).Select(x => x.Name).FirstOrDefault(), a.EndOfLifeOn,
        db.UnitMovements.Count(m => m.UnitId == a.Id && m.Kind == UnitMovementKind.BackFromRepair),
        db.UnitMovements.Where(m => m.UnitId == a.Id && m.Kind == UnitMovementKind.BackFromRepair).Sum(m => m.Cost) ?? 0));

    private static async Task<Store> StoreAsync(CmmsDbContext db, Guid? id, CancellationToken ct) =>
        id is { } sid ? await db.Stores.FirstOrDefaultAsync(s => s.Id == sid, ct) ?? throw new DomainException("Store not found.") : throw new DomainException("Choose a store.");

    /// <summary>Runs an action on a unit in one transaction and saves.</summary>
    private static async Task<IResult> OnUnitAsync(CmmsDbContext db, Guid id, Func<Asset, CancellationToken, Task> act, CancellationToken ct)
    {
        var unit = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.PartId != null, ct);
        if (unit is null) return Results.NotFound();
        await db.InTransactionAsync(async tk =>
        {
            await act(unit, tk);
            await db.SaveChangesAsync(tk);
            return true;
        }, ct);
        return Results.Ok(new { unit.Id });
    }

    private static async Task<Part> SerialPartAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        await db.Parts.FirstOrDefaultAsync(p => p.Id == id, ct) is { IsSerialTracked: true } part ? part : throw new DomainException("This part isn't serial-tracked.");

    private static string SerialOf(string? serial) =>
        serial.Clean() is { } s ? s : throw new DomainException("Enter the serial number.");

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
                // Every store holding the part, at every site: a store left without units would be stuck (its stock can't move any more).
                var levels = await db.StockLevels.AllSites().Where(l => l.PartId == id && l.QuantityOnHand != 0).Select(l => new { l.StoreId, l.QuantityOnHand, l.Store!.Code }).ToListAsync(ct);
                var visible = await db.Stores.Select(s => s.Id).ToListAsync(ct);
                if (levels.Any(l => !visible.Contains(l.StoreId))) throw new DomainException("This part has stock at sites you can't see.");
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
                        var store = await db.Stores.AllSites().FirstAsync(s => s.Id == l.StoreId, tk);
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
            // Without a work order, only units the person can see; on a work order, the job's rules decide (its asset may be another branch's).
            var unit = i.WorkOrderId is null
                ? await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.PartId != null, ct)
                : await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == id && a.PartId != null, ct);
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

        // ---- Unit actions (inventory.transact) ----
        api.MapPost("/serial-units/{id:guid}/transfer", async (Guid id, StoreMoveInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var store = await StoreAsync(db, i.StoreId, ct);
            return await OnUnitAsync(db, id, (u, tk) => SerialUnits.TransferAsync(db, ctx, u, store, i.Note, DateTimeOffset.UtcNow, tk), ct);
        }).RequirePermission(Permissions.InventoryTransact);
        api.MapPost("/serial-units/{id:guid}/send-for-repair", (Guid id, NoteInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            OnUnitAsync(db, id, (u, tk) => SerialUnits.SendForRepairAsync(db, ctx, u, i.Note, DateTimeOffset.UtcNow, tk), ct)).RequirePermission(Permissions.InventoryTransact);
        api.MapPost("/serial-units/{id:guid}/back-from-repair", async (Guid id, BackInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var store = await StoreAsync(db, i.StoreId, ct);
            return await OnUnitAsync(db, id, (u, tk) => SerialUnits.BackFromRepairAsync(db, ctx, u, store, i.Cost, i.Note, DateTimeOffset.UtcNow, tk), ct);
        }).RequirePermission(Permissions.InventoryTransact);
        api.MapPost("/serial-units/{id:guid}/scrap", (Guid id, NoteInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            OnUnitAsync(db, id, (u, tk) => SerialUnits.ScrapAsync(db, ctx, u, i.Note, DateTimeOffset.UtcNow, tk), ct)).RequirePermission(Permissions.InventoryTransact);

        api.MapPost("/parts/{id:guid}/units/found", async (Guid id, FoundInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var part = await SerialPartAsync(db, id, ct);
            var store = await StoreAsync(db, i.StoreId, ct);
            var serial = SerialOf(i.Serial);
            await SerialUnits.CheckNewSerialsAsync(db, part, [serial], 1, ct);
            var units = await db.InTransactionAsync(async tk =>
            {
                var made = await SerialUnits.RegisterInStoreAsync(db, ctx, part, store, [serial], moveStock: true, i.Note.Clean() ?? "Unit found", tk);
                await db.SaveChangesAsync(tk);
                return made;
            }, ct);
            return Results.Ok(new { units[0].Id });
        }).RequirePermission(Permissions.InventoryTransact);

        api.MapPost("/parts/{id:guid}/units/fitted", async (Guid id, FittedInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var part = await SerialPartAsync(db, id, ct);
            var target = i.AssetId is { } aid ? await db.Assets.FirstOrDefaultAsync(a => a.Id == aid, ct) : null;
            if (target is null) throw new DomainException("Choose the asset it is on.");
            var serial = SerialOf(i.Serial);
            var unit = await db.InTransactionAsync(async tk =>
            {
                var made = await SerialUnits.RegisterFittedAsync(db, ctx, part, target, serial, i.Note, DateTimeOffset.UtcNow, tk);
                await db.SaveChangesAsync(tk);
                return made;
            }, ct);
            return Results.Ok(new { unit.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        api.MapPost("/assets/{id:guid}/make-unit", async (Guid id, MakeUnitInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            var part = i.PartId is { } pid ? await db.Parts.FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
            await SerialUnits.MakeUnitAsync(db, ctx, asset, part, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { asset.Id });
        }).RequirePermission(Permissions.AssetsEdit);

        // ---- Lists and history ----
        api.MapGet("/serial-units", async (CmmsDbContext db, Guid? partId, string? state, Guid? storeId, Guid? siteId, int? endingWithinDays, string? search, CancellationToken ct) =>
        {
            var q = db.Assets.AsNoTracking().Where(a => a.PartId != null);
            if (partId is not null) q = q.Where(a => a.PartId == partId);
            if (storeId is not null) q = q.Where(a => a.StoreId == storeId);
            if (siteId is not null) q = q.Where(a => a.SiteId == siteId);
            q = state?.Trim().ToLowerInvariant() switch
            {
                "instore" => q.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.UnderRepair && a.ParentAssetId == null),
                "fitted" => q.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.UnderRepair && a.ParentAssetId != null),
                "atrepair" => q.Where(a => a.Status == AssetStatus.UnderRepair),
                "scrapped" => q.Where(a => a.Status == AssetStatus.Disposed),
                _ => q,
            };
            if (endingWithinDays is { } days)
            {
                var limit = (await ContractEndpoints.TodayAsync(db, ct)).AddDays(days);
                q = q.Where(a => a.EndOfLifeOn != null && a.EndOfLifeOn <= limit && a.Status != AssetStatus.Disposed);
            }
            if (search.Clean() is { } t)
                q = q.Where(a => a.SerialNumber!.Contains(t) || a.AssetNumber.Contains(t) || db.Parts.Any(p => p.Id == a.PartId && (p.PartNumber.Contains(t) || p.Name.Contains(t))));
            return await Rows(db, q.OrderBy(a => a.SerialNumber).Take(500)).ToListAsync(ct);
        }).RequirePermission(Permissions.InventoryView);

        api.MapGet("/serial-units/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var r = await Rows(db, db.Assets.AsNoTracking().Where(a => a.Id == id && a.PartId != null)).FirstOrDefaultAsync(ct);
            if (r is null) return Results.NotFound();
            var movements = await db.UnitMovements.AsNoTracking().Where(m => m.UnitId == id).OrderByDescending(m => m.At).Select(m => new
            {
                m.Kind, m.At, m.StoreId, StoreName = db.Stores.AllSites().Where(x => x.Id == m.StoreId).Select(x => x.Name).FirstOrDefault(),
                m.AssetId, AssetNumber = db.Assets.AllSites().Where(x => x.Id == m.AssetId).Select(x => x.AssetNumber).FirstOrDefault(),
                AssetName = db.Assets.AllSites().Where(x => x.Id == m.AssetId).Select(x => x.Name).FirstOrDefault(),
                m.WorkOrderId, WorkOrderNumber = db.WorkOrders.AllSites().Where(x => x.Id == m.WorkOrderId).Select(x => x.Number).FirstOrDefault(),
                m.PurchaseOrderId, PurchaseOrderNumber = db.PurchaseOrders.AllSites().Where(x => x.Id == m.PurchaseOrderId).Select(x => x.Number).FirstOrDefault(),
                m.Outcome, m.Note, m.Cost, m.ByName,
            }).ToListAsync(ct);
            return Results.Ok(new
            {
                r.Id, r.AssetNumber, r.SerialNumber, r.PartId, r.PartNumber, r.PartName, r.State, r.StoreId, r.StoreName, r.FittedToId, r.FittedToNumber, r.FittedToName,
                r.SiteId, r.SiteName, r.EndOfLifeOn, r.RepairCount, r.RepairCost, Movements = movements,
            });
        }).RequirePermission(Permissions.InventoryView);

        // The units on an asset now, and those taken off it before.
        api.MapGet("/assets/{id:guid}/fitted-units", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var now = await db.Assets.AllSites().AsNoTracking()
                .Where(a => a.ParentAssetId == id && a.PartId != null && a.Status != AssetStatus.Disposed && a.Status != AssetStatus.UnderRepair).OrderBy(a => a.AssetNumber)
                .Select(a => new
                {
                    UnitId = a.Id, a.AssetNumber, a.SerialNumber, PartName = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.Name).FirstOrDefault(),
                    Since = db.UnitMovements.Where(m => m.UnitId == a.Id && m.AssetId == id && (m.Kind == UnitMovementKind.Fitted || m.Kind == UnitMovementKind.Registered))
                        .Max(m => (DateTimeOffset?)m.At),
                }).ToListAsync(ct);
            var removed = await db.UnitMovements.AsNoTracking().Where(m => m.AssetId == id && m.Kind == UnitMovementKind.Removed).OrderByDescending(m => m.At).Take(200).ToListAsync(ct);
            var unitIds = removed.Select(m => m.UnitId).Distinct().ToList();
            var units = await db.Assets.AllSites().AsNoTracking().Where(a => unitIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AssetNumber, a.SerialNumber, PartName = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.Name).FirstOrDefault() }).ToDictionaryAsync(a => a.Id, ct);
            var fits = await db.UnitMovements.AsNoTracking()
                .Where(m => m.AssetId == id && unitIds.Contains(m.UnitId) && (m.Kind == UnitMovementKind.Fitted || m.Kind == UnitMovementKind.Registered)).ToListAsync(ct);
            var before = removed.Where(m => units.ContainsKey(m.UnitId)).Select(m => new
            {
                m.UnitId, units[m.UnitId].AssetNumber, units[m.UnitId].SerialNumber, units[m.UnitId].PartName,
                From = fits.Where(f => f.UnitId == m.UnitId && f.At <= m.At).Max(f => (DateTimeOffset?)f.At), To = m.At, m.Outcome,
            }).ToList();
            return Results.Ok(new { Now = now, Before = before });
        }).RequirePermission(Permissions.AssetsView);
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
