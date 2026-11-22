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
                .Select(l => new { l.PartId, l.Part!.PartNumber, PartName = l.Part.Name, l.Part.UnitOfMeasure, l.CountedQuantity, l.CountedByName, l.CountedAt }).ToListAsync(ct);
            var partIds = lines.Select(l => l.PartId).ToList();
            var levels = await db.StockLevels.AsNoTracking().Where(l => l.StoreId == c.StoreId && partIds.Contains(l.PartId))
                .ToDictionaryAsync(l => l.PartId, l => new { l.QuantityOnHand, l.Bin }, ct);
            return Results.Ok(new
            {
                c.Id, c.Name, c.StoreId, Store = new { c.Store!.Id, c.Store.Code, c.Store.Name }, c.Status, c.CreatedAt, c.PostedAt,
                Lines = lines.OrderBy(l => levels.GetValueOrDefault(l.PartId)?.Bin).ThenBy(l => l.PartNumber).Select(l =>
                {
                    // Expected = on hand now (until posted): the difference is what posting changes.
                    var expected = levels.GetValueOrDefault(l.PartId)?.QuantityOnHand ?? 0;
                    return new
                    {
                        l.PartId, l.PartNumber, l.PartName, l.UnitOfMeasure, levels.GetValueOrDefault(l.PartId)?.Bin, Expected = expected, Counted = l.CountedQuantity,
                        Difference = c.Status == StockCountStatus.Open ? l.CountedQuantity - expected : 0, l.CountedByName, l.CountedAt,
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
                var c = await db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, tk) ?? throw new DomainException("Count not found.");
                if (c.Status != StockCountStatus.Open) throw new DomainException(Closed);
                var n = await InventoryEndpoints.PostCountAsync(db, ctx, c.StoreId, DateOnly.FromDateTime(DateTime.UtcNow),
                    c.Lines.Select(l => (l.PartId, l.CountedQuantity)).ToList(), $"Stock count {c.Name}", tk);
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

    /// <summary>A counted quantity from the phone; a part counted again replaces its line only when counted later. The caller saves.</summary>
    internal static async Task CountLineAsync(CmmsDbContext db, ITenantContext ctx, Guid countId, Guid partId, decimal quantity, DateTimeOffset at, CancellationToken ct)
    {
        var c = await db.StockCounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == countId, ct) ?? throw new DomainException("This count no longer exists.");
        if (c.Status != StockCountStatus.Open) throw new DomainException(Closed);
        if (quantity < 0) throw new DomainException("Counted quantities cannot be negative.");
        if (!await db.Parts.AnyAsync(p => p.Id == partId, ct)) throw new DomainException("Part not found.");
        var line = await db.StockCountLines.FirstOrDefaultAsync(l => l.StockCountId == countId && l.PartId == partId, ct);
        if (line is null)
            db.StockCountLines.Add(new StockCountLine { StockCountId = countId, PartId = partId, CountedQuantity = quantity, CountedById = ctx.UserId, CountedByName = ctx.UserName, CountedAt = at });
        else if (at >= line.CountedAt)
        {
            line.CountedQuantity = quantity; line.CountedById = ctx.UserId; line.CountedByName = ctx.UserName; line.CountedAt = at;
        }
    }
}
