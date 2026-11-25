using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Stock counts (0.11.0): the office opens a count of a store, people count on the phone, the office reviews and posts.</summary>
public static class StockCountEndpoints
{
    public sealed record CountInput(Guid StoreId, string? Name);
    public const string Closed = "This count was already posted or cancelled.";
    public const string ShelfCount = "Count shelf-life parts by batch in the office.";
    /// <summary>Serial parts (0.24.0): their stock is their units, checked on the part's units list.</summary>
    public const string SerialCount = "Count serial parts by their units on the part's units list.";

    public static void MapStockCountEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/stock-counts").WithTags("Stock counts");

        g.MapGet("/", async (Guid? storeId, StockCountStatus? status, CmmsDbContext db, CancellationToken ct) =>
            await db.StockCounts.AsNoTracking().Where(c => (storeId == null || c.StoreId == storeId) && (status == null || c.Status == status))
                .OrderByDescending(c => c.CreatedAt).Take(200)
                .Select(c => new { c.Id, c.Name, c.StoreId, StoreCode = c.Store!.Code, c.Status, Lines = c.Lines.Count, c.CreatedAt, c.PostedAt }).ToListAsync(ct))
            .RequirePermission(Permissions.InventoryView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.StockCounts.AsNoTracking().Include(x => x.Store).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var lines = await db.StockCountLines.AsNoTracking().Where(l => l.StockCountId == id)
                .Select(l => new { l.PartId, l.Part!.PartNumber, PartName = Bilingual.Arabic && l.Part.NameAr != null ? l.Part.NameAr : l.Part.Name, l.Part.UnitOfMeasure, l.CountedQuantity, l.CountedByName, l.CountedAt }).ToListAsync(ct);
            var partIds = lines.Select(l => l.PartId).ToList();
            var levels = await db.StockLevels.AsNoTracking().Where(l => l.StoreId == c.StoreId && partIds.Contains(l.PartId))
                .ToDictionaryAsync(l => l.PartId, l => new { l.QuantityOnHand, l.Bin }, ct);
            var moved = c.Status == StockCountStatus.Open ? await MovedSinceAsync(db, c.StoreId, lines.Select(l => (l.PartId, l.CountedAt)).ToList(), ct) : [];
            return Results.Ok(new
            {
                c.Id, c.Name, c.StoreId, Store = new { c.Store!.Id, c.Store.Code, c.Store.Name }, c.Status, c.CreatedAt, c.PostedAt,
                SerialPartsLeftOut = await db.StockLevels.CountAsync(l => l.StoreId == c.StoreId && l.Part!.IsSerialTracked, ct),
                Lines = lines.OrderBy(l => levels.GetValueOrDefault(l.PartId)?.Bin).ThenBy(l => l.PartNumber).Select(l =>
                {
                    // Expected = on hand now (until posted). Stock moved after the part was counted stays: the difference is
                    // counted + moved since − on hand, which is what posting changes.
                    var expected = levels.GetValueOrDefault(l.PartId)?.QuantityOnHand ?? 0;
                    var since = moved.GetValueOrDefault(l.PartId);
                    return new
                    {
                        l.PartId, l.PartNumber, l.PartName, l.UnitOfMeasure, levels.GetValueOrDefault(l.PartId)?.Bin, Expected = expected, Counted = l.CountedQuantity,
                        MovedSince = since, Difference = c.Status == StockCountStatus.Open ? l.CountedQuantity + since - expected : 0, l.CountedByName, l.CountedAt,
                    };
                }),
            });
        }).RequirePermission(Permissions.InventoryView);

        g.MapPost("/", async (CountInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.StoreId, ct);
            var v = new Validator().MaxLength("name", input.Name, "Name", 200).When(store is null, "storeId", "Choose a store.");
            if (!v.IsValid) return v.Problem();
            var count = new StockCount { StoreId = input.StoreId, Name = input.Name.Clean() ?? $"{store!.Code} {DateTime.UtcNow:yyyy-MM-dd}" };
            db.StockCounts.Add(count);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/stock-counts/{count.Id}", new { count.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        g.MapPost("/{id:guid}/post", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var adjusted = await db.InTransactionAsync(async tk =>
            {
                db.ChangeTracker.Clear(); // may run again after a dropped connection: the count and the stock are loaded afresh (0.29.1)
                var c = await db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, tk) ?? throw new DomainException("Count not found.");
                if (c.Status != StockCountStatus.Open) throw new DomainException(Closed);
                // What was on the shelf when each part was counted, plus what moved in the store since then.
                var moved = await MovedSinceAsync(db, c.StoreId, c.Lines.Select(l => (l.PartId, l.CountedAt)).ToList(), tk);
                var n = await InventoryEndpoints.PostCountAsync(db, ctx, c.StoreId, DateOnly.FromDateTime(DateTime.UtcNow),
                    c.Lines.Select(l => (l.PartId, l.CountedQuantity + moved.GetValueOrDefault(l.PartId))).ToList(), $"Stock count {c.Name}", tk);
                c.Status = StockCountStatus.Posted; c.PostedAt = DateTimeOffset.UtcNow; c.PostedById = ctx.UserId;
                await db.SaveChangesAsync(tk);
                return n;
            }, ct);
            return Results.Ok(new { adjusted });
        }).RequirePermission(Permissions.InventoryTransact);

        g.MapPost("/{id:guid}/cancel", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.StockCounts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (c.Status != StockCountStatus.Open) throw new DomainException(Closed);
            c.Status = StockCountStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.InventoryTransact);
    }

    /// <summary>Per counted part: the net stock moved in the store after it was counted (receipts in, issues out, adjustments).</summary>
    private static async Task<Dictionary<Guid, decimal>> MovedSinceAsync(CmmsDbContext db, Guid storeId, IReadOnlyList<(Guid PartId, DateTimeOffset CountedAt)> lines,
        CancellationToken ct)
    {
        if (lines.Count == 0) return [];
        var partIds = lines.Select(l => l.PartId).ToList();
        var from = lines.Min(l => l.CountedAt);
        var moves = await db.StockTransactions.AsNoTracking().Where(t => t.StoreId == storeId && partIds.Contains(t.PartId) && t.CreatedAt > from)
            .Select(t => new { t.PartId, t.Quantity, t.CreatedAt }).ToListAsync(ct);
        return lines.ToDictionary(l => l.PartId, l => moves.Where(m => m.PartId == l.PartId && m.CreatedAt > l.CountedAt).Sum(m => m.Quantity));
    }

    /// <summary>A counted quantity from the phone; a part counted again replaces its line only when counted later. The caller saves.</summary>
    internal static async Task CountLineAsync(CmmsDbContext db, ITenantContext ctx, Guid countId, Guid partId, decimal quantity, DateTimeOffset at, CancellationToken ct)
    {
        var c = await db.StockCounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == countId, ct) ?? throw new DomainException("This count no longer exists.");
        if (c.Status != StockCountStatus.Open) throw new DomainException(Closed);
        if (quantity < 0) throw new DomainException("Counted quantities cannot be negative.");
        var kind = await db.Parts.Where(p => p.Id == partId).Select(p => new { p.IsShelfItem, p.IsSerialTracked }).FirstOrDefaultAsync(ct)
                   ?? throw new DomainException("Part not found.");
        if (kind.IsSerialTracked) throw new DomainException(SerialCount);
        // Posting can't adjust them without a batch: refused here, before anyone's count of the store depends on it.
        if (kind.IsShelfItem) throw new DomainException(ShelfCount);
        var line = await db.StockCountLines.FirstOrDefaultAsync(l => l.StockCountId == countId && l.PartId == partId, ct);
        if (line is null)
            db.StockCountLines.Add(new StockCountLine { StockCountId = countId, PartId = partId, CountedQuantity = quantity, CountedById = ctx.UserId, CountedByName = ctx.UserName, CountedAt = at });
        else if (at >= line.CountedAt)
        {
            line.CountedQuantity = quantity; line.CountedById = ctx.UserId; line.CountedByName = ctx.UserName; line.CountedAt = at;
        }
    }
}
