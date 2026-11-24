using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Parts used on a job from the phone (0.11.0). The part is already in the technician's hand when the change arrives, so it
/// is issued even when that takes stock reserved for other work (the answer says which), and what the store's stock can't
/// cover becomes a planned line on the job: short, so it shows on the store keeper's list. Stock never goes negative unless
/// the part allows it, exactly as on the web.
/// </summary>
public static class PhoneStock
{
    public const string ShortMessage = "Only {0} in the store; the rest is on the store keeper's list.";
    public const string ReservedMessage = "This took parts reserved for {0}.";
    public const string AskedMessage = "Added to the job's parts; the store keeper issues them.";
    public const string ShelfMessage = "{0} is a shelf-life part: added to the job's parts; the store keeper issues it with its batch.";

    /// <summary>Issues <paramref name="quantity"/> to the job. Returns the note for the technician, or null. The caller saves.</summary>
    public static async Task<string?> UseAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, Guid partId, Guid storeId, decimal quantity, DateTimeOffset at,
        CancellationToken ct)
    {
        var (part, _) = await CheckAsync(db, partId, storeId, quantity, ct, allowShelf: true);
        if (part.IsShelfItem)
        {
            // Issued by batch, which the office chooses: the part taken is recorded on the job for the store keeper.
            await PlanAsync(db, w.Id, partId, storeId, quantity, ct);
            return string.Format(ShelfMessage, part.PartNumber);
        }
        var onHand = await Planning.OnHandAsync(db, partId, storeId, ct);
        var toIssue = part.AllowNegativeStock ? quantity : Math.Min(quantity, Math.Max(onHand, 0));
        var notes = new List<string>();
        if (toIssue > 0)
        {
            var lines = await Planning.OpenLinesAsync(db, partId, storeId, ct);
            var others = Reservations.WouldShort(onHand, lines, toIssue, w.Id);
            if (others.Count > 0) notes.Add(string.Format(ReservedMessage, string.Join(", ", others.Select(o => o.WorkOrderNumber).Distinct())));
            await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Issue, partId, storeId, toIssue, await DayAsync(db, at, ct),
                WorkOrderId: w.Id, Notes: "From the phone"), ct);
        }
        var rest = quantity - toIssue;
        if (rest > 0)
        {
            // Still needed: plan it on the job, where it is short until the store has it.
            await PlanAsync(db, w.Id, partId, storeId, rest, ct);
            notes.Insert(0, string.Format(ShortMessage, Math.Max(onHand, 0).ToString("0.###")));
        }
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>Someone who can't take parts from the store asks for them: the quantity is added to the job's planned parts.</summary>
    public static async Task<string> AskAsync(CmmsDbContext db, WorkOrder w, Guid partId, Guid storeId, decimal quantity, CancellationToken ct)
    {
        await CheckAsync(db, partId, storeId, quantity, ct, allowShelf: true);
        await PlanAsync(db, w.Id, partId, storeId, quantity, ct);
        return AskedMessage;
    }

    /// <summary>Returns <paramref name="quantity"/> from the job to the store, never more than was issued to it. The caller saves.</summary>
    public static async Task ReturnAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, Guid partId, Guid storeId, decimal quantity, DateTimeOffset at,
        CancellationToken ct)
    {
        await StockLedger.RefuseSerialAsync(db, partId, StockTransactionType.Return, ct);
        await CheckAsync(db, partId, storeId, quantity, ct);
        var net = -(await db.StockTransactions.Where(x => x.WorkOrderId == w.Id && x.PartId == partId && (x.Type == StockTransactionType.Issue || x.Type == StockTransactionType.Return))
            .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0);
        if (quantity > net) throw new DomainException($"Only {net:0.###} were issued to this work order and not yet returned.");
        var lastIssue = await db.StockTransactions.Where(x => x.WorkOrderId == w.Id && x.PartId == partId && x.Type == StockTransactionType.Issue)
            .OrderByDescending(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Return, partId, storeId, quantity, await DayAsync(db, at, ct),
            WorkOrderId: w.Id, Notes: "From the phone", RelatedTransactionId: lastIssue), ct);
    }

    /// <summary>The part and store (only a store of the person's sites), and a quantity that makes sense.</summary>
    private static async Task<(Part Part, Store Store)> CheckAsync(CmmsDbContext db, Guid partId, Guid storeId, decimal quantity, CancellationToken ct,
        bool allowShelf = false)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, ct) ?? throw new DomainException("Store not found.");
        if (!store.IsActive) throw new DomainException($"Store {store.Code} is inactive.");
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct) ?? throw new DomainException("Part not found.");
        // Shelf-life parts are issued by batch, which the office chooses.
        if (part.IsShelfItem && !allowShelf) throw new DomainException($"{part.PartNumber} is a shelf-life item: issue it with its batch in the office.");
        return (part, store);
    }

    /// <summary>Makes the job's planned quantity of the part in the store cover what was issued plus <paramref name="more"/>.</summary>
    private static async Task PlanAsync(CmmsDbContext db, Guid workOrderId, Guid partId, Guid storeId, decimal more, CancellationToken ct)
    {
        var issuedHere = -(await db.StockTransactions.Where(x => x.WorkOrderId == workOrderId && x.PartId == partId && x.StoreId == storeId
                && (x.Type == StockTransactionType.Issue || x.Type == StockTransactionType.Return)).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0)
            + db.ChangeTracker.Entries<StockTransaction>().Where(e => e.State == EntityState.Added && e.Entity.WorkOrderId == workOrderId && e.Entity.PartId == partId
                && e.Entity.StoreId == storeId).Sum(e => -e.Entity.Quantity);
        var line = await db.WorkOrderParts.FirstOrDefaultAsync(p => p.WorkOrderId == workOrderId && p.PartId == partId && p.StoreId == storeId, ct);
        var needed = Math.Max(issuedHere, 0) + more;
        if (line is null)
            db.WorkOrderParts.Add(new WorkOrderPart { WorkOrderId = workOrderId, PartId = partId, StoreId = storeId, Quantity = needed, Notes = "From the phone" });
        else if (line.Quantity < needed) line.Quantity = needed;
    }

    /// <summary>The organization's local day of <paramref name="at"/> (stock moves are dated by day).</summary>
    private static async Task<DateOnly> DayAsync(CmmsDbContext db, DateTimeOffset at, CancellationToken ct) =>
        Sla.LocalDay(at, Sla.Zone((await SlaQueries.SettingsAsync(db, ct)).TimeZone));
}
