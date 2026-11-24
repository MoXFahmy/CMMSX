using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Serial-numbered parts (0.24.0): switching tracking on, registering, fitting, removing and moving units.</summary>
public static class SerialUnitEndpoints
{
    public sealed record StoreSerials(Guid StoreId, List<string>? Serials);
    public sealed record SerialTrackingInput(bool On, int? UnitLifeMonths, List<StoreSerials>? Stores);

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
