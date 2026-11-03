using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Inventory;

public sealed record StockPost(
    StockTransactionType Type, Guid PartId, Guid StoreId, decimal Quantity, DateOnly Date,
    decimal? UnitCost = null, Guid? WorkOrderId = null, Guid? LocationId = null, Guid? SupplierId = null,
    Guid? PurchaseOrderId = null, Guid? PurchaseOrderLineId = null, string? Reference = null, string? BatchNumber = null,
    DateOnly? ExpiryDate = null, string? Notes = null, Guid? RelatedTransactionId = null, string? LegacyRef = null);

/// <summary>
/// The only place stock quantities change. Each call writes one immutable ledger entry and moves the store balance with a
/// single conditional UPDATE, so concurrent issues can never take a non-negative part below zero.
/// Callers wrap related posts in a transaction (<see cref="CmmsDbContext.InTransactionAsync{T}"/>).
/// </summary>
public static class StockLedger
{
    public static async Task<StockTransaction> PostAsync(CmmsDbContext db, ITenantContext ctx, StockPost p, CancellationToken ct = default)
    {
        if (p.Quantity == 0) throw new DomainException("Quantity must not be zero.");
        if (p.Type != StockTransactionType.Adjustment && p.Quantity < 0) throw new DomainException("Quantity must be positive.");
        var part = await db.Parts.FirstOrDefaultAsync(x => x.Id == p.PartId, ct) ?? throw new DomainException("Part not found.");
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.StoreId, ct) ?? throw new DomainException("Store not found.");
        if (!store.IsActive) throw new DomainException($"Store {store.Code} is inactive.");
        var delta = p.Type switch
        {
            StockTransactionType.Receipt or StockTransactionType.Return or StockTransactionType.TransferIn => p.Quantity,
            StockTransactionType.Issue or StockTransactionType.TransferOut => -p.Quantity,
            _ => p.Quantity,
        };
        var batch = string.IsNullOrWhiteSpace(p.BatchNumber) ? null : p.BatchNumber.Trim();
        if (part.IsShelfItem && p.LegacyRef is null)
        {
            if (batch is null) throw new DomainException($"{part.PartNumber} is a shelf-life item: enter the batch number.");
            if (p.Type == StockTransactionType.Receipt && p.ExpiryDate is null) throw new DomainException($"{part.PartNumber} is a shelf-life item: enter the batch expiry date.");
            if (delta < 0)
            {
                var b = await db.StockTransactions.Where(t => t.PartId == part.Id && t.StoreId == store.Id && t.BatchNumber == batch)
                    .GroupBy(_ => 1).Select(g => new { Qty = g.Sum(t => t.Quantity), Expiry = g.Max(t => t.ExpiryDate) }).FirstOrDefaultAsync(ct);
                if (b is null || b.Qty < -delta) throw new DomainException($"Batch {batch} has only {b?.Qty ?? 0:0.###} {part.UnitOfMeasure} in {store.Code}.");
                if (b.Expiry is { } exp && exp < p.Date && p.Type == StockTransactionType.Issue) throw new DomainException($"Batch {batch} expired on {exp:yyyy-MM-dd} and cannot be issued.");
            }
        }

        // Unit cost: receipts at the purchase price (recomputing the weighted average); everything else at average cost.
        var unitCost = p.UnitCost ?? part.AverageCost;
        if (p.Type == StockTransactionType.Receipt)
        {
            if (p.UnitCost is not { } price || price < 0) throw new DomainException("Enter the unit price.");
            var balance = await db.StockLevels.Where(l => l.PartId == part.Id).SumAsync(l => (decimal?)l.QuantityOnHand, ct) ?? 0;
            var basis = Math.Max(balance, 0);
            part.AverageCost = basis + p.Quantity > 0 ? Math.Round((part.AverageCost * basis + price * p.Quantity) / (basis + p.Quantity), 4) : price;
            part.LastPurchasePrice = price;
        }
        else if (p.Type == StockTransactionType.TransferIn && p.RelatedTransactionId is { } outId)
            unitCost = await db.StockTransactions.Where(t => t.Id == outId).Select(t => t.UnitCost).FirstAsync(ct);
        else if (p.Type == StockTransactionType.Return && p.RelatedTransactionId is { } issueId)
            unitCost = await db.StockTransactions.Where(t => t.Id == issueId).Select(t => t.UnitCost).FirstOrDefaultAsync(ct);

        var level = await db.StockLevels.FirstOrDefaultAsync(l => l.PartId == part.Id && l.StoreId == store.Id, ct);
        if (level is null)
        {
            if (delta < 0 && !part.AllowNegativeStock && p.LegacyRef is null) throw new DomainException($"{part.PartNumber} has no stock in {store.Code}.");
            level = new StockLevel { PartId = part.Id, StoreId = store.Id };
            db.StockLevels.Add(level);
            await db.SaveChangesAsync(ct);
        }
        var allowNegative = part.AllowNegativeStock || p.LegacyRef is not null;
        var moved = await db.StockLevels.Where(l => l.Id == level.Id && (allowNegative || l.QuantityOnHand + delta >= 0))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.QuantityOnHand, l => l.QuantityOnHand + delta), ct);
        if (moved == 0)
        {
            var onHand = await db.StockLevels.Where(l => l.Id == level.Id).Select(l => l.QuantityOnHand).FirstAsync(ct);
            throw new DomainException($"Not enough stock: {part.PartNumber} has {onHand:0.###} {part.UnitOfMeasure} in {store.Code}.");
        }

        var tx = new StockTransaction
        {
            Type = p.Type, PartId = part.Id, StoreId = store.Id, Quantity = delta, UnitCost = Math.Round(unitCost, 4), Date = p.Date,
            WorkOrderId = p.WorkOrderId, LocationId = p.LocationId, SupplierId = p.SupplierId, PurchaseOrderId = p.PurchaseOrderId, PurchaseOrderLineId = p.PurchaseOrderLineId,
            Reference = Trim(p.Reference, 100), BatchNumber = Trim(batch, 100), ExpiryDate = p.ExpiryDate, Notes = Trim(p.Notes, 1000), RelatedTransactionId = p.RelatedTransactionId,
            CreatedAt = DateTimeOffset.UtcNow, CreatedById = ctx.UserId, CreatedByName = ctx.UserName, LegacyRef = p.LegacyRef,
        };
        db.StockTransactions.Add(tx);
        await db.SaveChangesAsync(ct);
        return tx;
    }

    /// <summary>Moves stock between stores at the outgoing average cost; returns (out, in).</summary>
    public static async Task<(StockTransaction Out, StockTransaction In)> TransferAsync(CmmsDbContext db, ITenantContext ctx, Guid partId, Guid fromStore, Guid toStore,
        decimal qty, DateOnly date, string? batch, string? notes, CancellationToken ct = default)
    {
        if (fromStore == toStore) throw new DomainException("Choose two different stores.");
        var expiry = batch is null ? null : await db.StockTransactions.Where(t => t.PartId == partId && t.StoreId == fromStore && t.BatchNumber == batch).MaxAsync(t => t.ExpiryDate, ct);
        var o = await PostAsync(db, ctx, new StockPost(StockTransactionType.TransferOut, partId, fromStore, qty, date, BatchNumber: batch, Notes: notes), ct);
        var i = await PostAsync(db, ctx, new StockPost(StockTransactionType.TransferIn, partId, toStore, qty, date, BatchNumber: batch, ExpiryDate: expiry, Notes: notes, RelatedTransactionId: o.Id), ct);
        o.RelatedTransactionId = i.Id;
        await db.SaveChangesAsync(ct);
        return (o, i);
    }

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
