using System.Linq.Expressions;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class InventoryEndpoints
{
    public sealed record PartLookupRow(Guid Id, string PartNumber, string Name, string? Barcode, string UnitOfMeasure, decimal AverageCost, decimal? LastPurchasePrice,
        bool IsShelfItem, Guid? PreferredSupplierId, decimal OnHand, decimal? OnAsset);

    public sealed record StoreInput(string Code, string Name, Guid? SiteId, string? Description, bool IsActive = true);
    public sealed record PartInput(string PartNumber, string Name, string? Specification, string? Manufacturer, string? ManufacturerPartNumber, string UnitOfMeasure,
        Guid? GroupId, decimal StandardPrice, int? LeadTimeDays, bool IsCritical, bool IsShelfItem, bool AllowNegativeStock, Guid? PreferredSupplierId,
        string? Barcode, List<Guid>? AlternatePartIds, string? Notes, bool IsActive = true, List<LevelInput>? Levels = null, string? RowVersion = null);
    public sealed record LevelInput(Guid StoreId, string? Bin, decimal MinLevel, decimal ReorderLevel, decimal MaxLevel, decimal ReorderQuantity);
    public sealed record ReceiveInput(Guid? PartId, Guid? StoreId, decimal Quantity, decimal UnitCost, DateOnly? Date, Guid? SupplierId, string? Reference, string? BatchNumber, DateOnly? ExpiryDate, string? Notes);
    /// <param name="ConfirmReserved">Issue even if it takes stock reserved for other work (the answer to "Issue anyway?").</param>
    /// <param name="CostCentreId">For parts issued to or returned from a location without a work order (0.20.0); none = the location's in force.</param>
    public sealed record IssueInput(Guid? PartId, Guid? StoreId, decimal Quantity, DateOnly? Date, Guid? WorkOrderId, Guid? LocationId, string? BatchNumber, string? Notes,
        bool ConfirmReserved = false, Guid? CostCentreId = null);
    public sealed record CostCentreChange(Guid? CostCentreId);
    public sealed record AdjustInput(Guid? PartId, Guid? StoreId, decimal Quantity, DateOnly? Date, string? BatchNumber, string Notes);
    public sealed record TransferInput(Guid? PartId, Guid? FromStoreId, Guid? ToStoreId, decimal Quantity, DateOnly? Date, string? BatchNumber, string? Notes,
        bool ConfirmReserved = false);
    public sealed record CountLine(Guid PartId, decimal CountedQuantity);
    public sealed record StockTakeInput(Guid StoreId, DateOnly? Date, List<CountLine> Lines, string? Notes);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly PurchaseOrderStatus[] OpenPo = [PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];

    private static readonly Dictionary<string, Expression<Func<Part, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["partNumber"] = p => p.PartNumber, ["name"] = p => p.Name, ["group"] = p => p.Group!.Name, ["averageCost"] = p => p.AverageCost, ["manufacturer"] = p => p.Manufacturer,
    };

    public sealed class PartFilter
    {
        public string? Search { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? StoreId { get; set; }
        public bool? BelowReorder { get; set; }
        public bool? Critical { get; set; }
        public bool? ActiveOnly { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record PartListItem(Guid Id, string PartNumber, string Name, string? Manufacturer, string UnitOfMeasure, string? GroupName, decimal AverageCost,
        bool IsCritical, bool IsShelfItem, bool IsActive, decimal OnHand, decimal Reserved, decimal Available, decimal OnOrder, bool Low, decimal Value);

    /// <summary>One page of the part list (the list screen and its export share it).</summary>
    internal static async Task<PagedResult<PartListItem>> ListAsync(CmmsDbContext db, PartFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 500)
    {
        var storeId = f.StoreId;
        var q = db.Parts.AsNoTracking();
        if (f.Search.Clean() is { } s) q = q.Where(p => p.PartNumber.Contains(s) || p.Name.Contains(s) || p.Manufacturer!.Contains(s) || p.ManufacturerPartNumber!.Contains(s) || p.Barcode == s);
        if (f.GroupId is not null) q = q.Where(p => p.GroupId == f.GroupId);
        if (storeId is not null) q = q.Where(p => db.StockLevels.Any(l => l.PartId == p.Id && l.StoreId == storeId));
        if (f.Critical == true) q = q.Where(p => p.IsCritical);
        if (f.ActiveOnly == true) q = q.Where(p => p.IsActive);
        // Re-order checks use available stock (on hand less what open work has reserved).
        if (f.BelowReorder == true) q = q.Where(p => db.StockLevels.Any(l => l.PartId == p.Id && (storeId == null || l.StoreId == storeId) && l.ReorderQuantity > 0
            && l.QuantityOnHand - (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0) <= l.ReorderLevel));
        var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "partNumber").Select(p => new
        {
            p.Id, p.PartNumber, p.Name, p.Manufacturer, p.UnitOfMeasure, GroupName = p.Group!.Name, p.AverageCost, p.IsCritical, p.IsShelfItem, p.IsActive,
            OnHand = db.StockLevels.Where(l => l.PartId == p.Id && (storeId == null || l.StoreId == storeId)).Sum(l => (decimal?)l.QuantityOnHand) ?? 0,
            Reserved = db.StockReservations.Where(r => r.PartId == p.Id && (storeId == null || r.StoreId == storeId)).Sum(r => (decimal?)r.Reserved) ?? 0,
            Low = db.StockLevels.Any(l => l.PartId == p.Id && (storeId == null || l.StoreId == storeId) && l.ReorderQuantity > 0 && l.QuantityOnHand - (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0) <= l.ReorderLevel),
            OnOrder = db.PurchaseOrderLines.Where(x => x.PartId == p.Id && db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId && OpenPo.Contains(o.Status)))
                .Sum(x => (decimal?)((x.Quantity - x.QuantityReceived) * x.PackSize)) ?? 0,
        }).ToPagedAsync(page, pageSize, ct, maxPageSize);
        return new PagedResult<PartListItem>(paged.Items.Select(p => new PartListItem(p.Id, p.PartNumber, p.Name, p.Manufacturer, p.UnitOfMeasure, p.GroupName,
            p.AverageCost, p.IsCritical, p.IsShelfItem, p.IsActive, p.OnHand, p.Reserved, p.OnHand - p.Reserved, p.OnOrder, p.Low, Math.Round(p.OnHand * p.AverageCost, 2))).ToList(),
            paged.Total, paged.Page, paged.PageSize);
    }

    public static void MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        PeopleEndpoints.MapLookup(api, "/part-groups", "Part groups", db => db.PartGroups, () => new PartGroup { Code = "", Name = "" }, Permissions.InventoryManage,
            async (db, id, ct) => await db.Parts.AnyAsync(p => p.GroupId == id, ct), null, null);

        // ---------------- Stores ----------------
        var st = api.MapGroup("/stores").WithTags("Stores");
        st.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var value = await db.StockLevels.GroupBy(l => l.StoreId).Select(g => new { g.Key, Lines = g.Count(), Value = g.Sum(l => l.QuantityOnHand * l.Part!.AverageCost) })
                .ToDictionaryAsync(x => x.Key, ct);
            var list = await db.Stores.AsNoTracking().OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name, s.SiteId, SiteName = s.Site!.Name, s.Description, s.IsActive }).ToListAsync(ct);
            return list.Select(s => new { s.Id, s.Code, s.Name, s.SiteId, s.SiteName, s.Description, s.IsActive, Parts = value.GetValueOrDefault(s.Id)?.Lines ?? 0, StockValue = Math.Round(value.GetValueOrDefault(s.Id)?.Value ?? 0, 2) });
        }).RequirePermission(Permissions.InventoryView);
        st.MapPost("/", async (StoreInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateStoreAsync(i, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var s = CreateStore(db, i);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/stores/{s.Id}", new { s.Id });
        }).RequirePermission(Permissions.InventoryManage);
        st.MapPut("/{id:guid}", async (Guid id, StoreInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.Stores.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var v = await ValidateStoreAsync(i, id, db, ct);
            if (s.SiteId != i.SiteId && await db.StockLevels.AnyAsync(l => l.StoreId == id && l.QuantityOnHand != 0, ct)) v.Add("siteId", "Move the stock out before changing the store's site.");
            if (!v.IsValid) return v.Problem();
            UpdateStore(s, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { s.Id });
        }).RequirePermission(Permissions.InventoryManage);
        st.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.Stores.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            if (await db.StockTransactions.AnyAsync(t => t.StoreId == id, ct)) throw new DomainException("This store has stock history. Mark it inactive instead.");
            await db.StockLevels.Where(l => l.StoreId == id).ExecuteDeleteAsync(ct);
            db.Stores.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.InventoryManage);

        // ---------------- Parts ----------------
        var g = api.MapGroup("/parts").WithTags("Parts");
        g.MapGet("/", ([AsParameters] PartFilter f, CmmsDbContext db, CancellationToken ct) =>
            ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 25, ct)).RequirePermission(Permissions.InventoryView);

        g.MapGet("/export", ([AsParameters] PartFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "parts", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct))
            .RequirePermission(Permissions.InventoryView);

        g.MapPost("/bulk", async (BulkActiveInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var ids = Bulk.Ids(input.Ids, "parts");
            var rows = await db.Parts.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            foreach (var p in rows) p.IsActive = input.Active;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new BulkResult(rows.Count, ids.Except(rows.Select(r => r.Id)).Select(id => new BulkSkip(id, "", "Not found.")).ToList()));
        }).RequirePermission(Permissions.InventoryManage);

        // With an asset (planning or issuing parts for its work): the asset's spare parts first, with the quantity from its list.
        g.MapGet("/lookup", async (CmmsDbContext db, string? search, Guid? storeId, Guid? assetId, CancellationToken ct) =>
        {
            var q = db.Parts.AsNoTracking().Where(p => p.IsActive);
            if (search.Clean() is { } s) q = q.Where(p => p.PartNumber.Contains(s) || p.Name.Contains(s) || p.Barcode == s);
            var onAsset = assetId is { } aid ? await db.AssetParts.AsNoTracking().Where(x => x.AssetId == aid).ToDictionaryAsync(x => x.PartId, x => x.Quantity, ct) : [];
            var listed = onAsset.Keys.ToList();
            async Task<List<PartLookupRow>> Find(IQueryable<FourC.Cmms.Core.Inventory.Part> parts) => await parts.OrderBy(p => p.PartNumber).Take(30)
                .Select(p => new PartLookupRow(p.Id, p.PartNumber, p.Name, p.Barcode, p.UnitOfMeasure, p.AverageCost, p.LastPurchasePrice, p.IsShelfItem, p.PreferredSupplierId,
                    db.StockLevels.Where(l => l.PartId == p.Id && (storeId == null || l.StoreId == storeId)).Sum(l => (decimal?)l.QuantityOnHand) ?? 0, null))
                .ToListAsync(ct);
            var first = listed.Count == 0 ? [] : (await Find(q.Where(p => listed.Contains(p.Id)))).Select(r => r with { OnAsset = onAsset[r.Id] }).ToList();
            return first.Concat(await Find(listed.Count == 0 ? q : q.Where(p => !listed.Contains(p.Id)))).ToList();
        }).RequirePermission(Permissions.InventoryView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.Parts.AsNoTracking().Include(x => x.Group).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var levels = await db.StockLevels.AsNoTracking().Where(l => l.PartId == id).OrderBy(l => l.Store!.Code)
                .Select(l => new { l.Id, l.StoreId, StoreCode = l.Store!.Code, StoreName = l.Store.Name, l.Bin, l.MinLevel, l.ReorderLevel, l.MaxLevel, l.ReorderQuantity, l.QuantityOnHand,
                    Reserved = (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0) })
                .ToListAsync(ct);
            // Who holds the stock: open work orders with something still reserved, in the order stock goes to them.
            var holders = new List<(PlanLine Line, string StoreCode, decimal Short)>();
            foreach (var l in levels.Where(l => l.Reserved > 0))
            {
                var lines = await Planning.OpenLinesAsync(db, id, l.StoreId, ct);
                var shortages = Reservations.Shortages(l.QuantityOnHand, lines);
                holders.AddRange(lines.Where(x => x.StillReserved > 0).Select(x => (x, l.StoreCode, shortages.GetValueOrDefault(x.LineId))));
            }
            var order = Reservations.InOrderOfNeed(holders.Select(h => h.Line)).Select((x, n) => (x.LineId, n)).ToDictionary(x => x.LineId, x => x.n);
            var holderIds = holders.Select(h => h.Line.WorkOrderId).ToList();
            var titles = await db.WorkOrders.AsNoTracking().Where(w => holderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Title, ct);
            var reservedFor = holders.OrderBy(h => order[h.Line.LineId]).Select(h => new
            {
                h.Line.WorkOrderId, h.Line.WorkOrderNumber, Title = titles.GetValueOrDefault(h.Line.WorkOrderId), h.StoreCode, Reserved = h.Line.StillReserved, h.Short, h.Line.DueDate,
            }).ToList();
            var batches = p.IsShelfItem ? await db.StockTransactions.AsNoTracking().Where(t => t.PartId == id && t.BatchNumber != null).GroupBy(t => new { t.StoreId, t.BatchNumber })
                .Select(b => new { b.Key.StoreId, b.Key.BatchNumber, Quantity = b.Sum(t => t.Quantity), Expiry = b.Max(t => t.ExpiryDate) }).Where(b => b.Quantity != 0).ToListAsync(ct) : [];
            var alternates = await db.Parts.AsNoTracking().Where(x => p.AlternatePartIds.Contains(x.Id)).Select(x => new { x.Id, x.PartNumber, x.Name }).ToListAsync(ct);
            var supplier = p.PreferredSupplierId is { } sid ? await db.Suppliers.AsNoTracking().Where(x => x.Id == sid).Select(x => new { x.Id, x.Code, x.Name }).FirstOrDefaultAsync(ct) : null;
            var onOrder = await db.PurchaseOrderLines.Where(x => x.PartId == id && db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId && OpenPo.Contains(o.Status)))
                .SumAsync(x => (decimal?)((x.Quantity - x.QuantityReceived) * x.PackSize), ct) ?? 0;
            var onHand = levels.Sum(l => l.QuantityOnHand);
            var reservedTotal = levels.Sum(l => l.Reserved);
            var usedOn = await db.AssetParts.AsNoTracking().Where(x => x.PartId == id).OrderBy(x => x.Asset!.AssetNumber)
                .Select(x => new { x.AssetId, x.Asset!.AssetNumber, AssetName = x.Asset.Name, x.Quantity, x.Note }).ToListAsync(ct);
            return Results.Ok(new
            {
                p.Id, p.PartNumber, p.Name, p.Specification, p.Manufacturer, p.ManufacturerPartNumber, p.UnitOfMeasure, p.GroupId, GroupName = p.Group?.Name, p.StandardPrice,
                p.AverageCost, p.LastPurchasePrice, p.LeadTimeDays, p.IsCritical, p.IsShelfItem, p.AllowNegativeStock, p.PreferredSupplierId, PreferredSupplier = supplier,
                p.Barcode, p.AlternatePartIds, Alternates = alternates, p.Notes, p.IsActive,
                Levels = levels.Select(l => new { l.Id, l.StoreId, l.StoreCode, l.StoreName, l.Bin, l.MinLevel, l.ReorderLevel, l.MaxLevel, l.ReorderQuantity, l.QuantityOnHand,
                    l.Reserved, Available = l.QuantityOnHand - l.Reserved }),
                Batches = batches, ReservedFor = reservedFor, UsedOn = usedOn,
                OnHand = onHand, Reserved = reservedTotal, Available = onHand - reservedTotal, OnOrder = onOrder, StockValue = Math.Round(onHand * p.AverageCost, 2), p.LegacyRef, RowVersion = Convert.ToBase64String(p.RowVersion),
            });
        }).RequirePermission(Permissions.InventoryView);

        g.MapPost("/", async (PartInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidatePartAsync(i, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var p = CreatePart(db, i);
            foreach (var l in i.Levels ?? []) db.StockLevels.Add(new StockLevel { PartId = p.Id, StoreId = l.StoreId, Bin = l.Bin.Clean(), MinLevel = l.MinLevel, ReorderLevel = l.ReorderLevel, MaxLevel = l.MaxLevel, ReorderQuantity = l.ReorderQuantity });
            // The preferred supplier always has a catalogue row (0.18.0).
            if (p.PreferredSupplierId is { } preferred) await CatalogueEndpoints.EnsureRowAsync(db, p, preferred, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/parts/{p.Id}", new { p.Id });
        }).RequirePermission(Permissions.InventoryManage);

        g.MapPut("/{id:guid}", async (Guid id, PartInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.Parts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var v = await ValidatePartAsync(i with { PartNumber = p.PartNumber }, id, db, ct); // part numbers are permanent (as in 4CSQL)
            if (!v.IsValid) return v.Problem();
            if (i.RowVersion is not null) db.Entry(p).Property(x => x.RowVersion).OriginalValue = i.RowVersion.ToVersion();
            UpdatePart(p, i);
            if (p.PreferredSupplierId is { } preferred) await CatalogueEndpoints.EnsureRowAsync(db, p, preferred, ct);
            if (i.Levels is not null)
            {
                var existing = await db.StockLevels.Where(l => l.PartId == id).ToListAsync(ct);
                foreach (var l in existing.Where(e => i.Levels.All(x => x.StoreId != e.StoreId)))
                {
                    if (l.QuantityOnHand != 0) throw new DomainException("A store that still holds this part cannot be removed. Transfer or adjust the stock first.");
                    db.StockLevels.Remove(l);
                }
                foreach (var x in i.Levels)
                {
                    var l = existing.FirstOrDefault(e => e.StoreId == x.StoreId);
                    if (l is null) { l = new StockLevel { PartId = id, StoreId = x.StoreId }; db.StockLevels.Add(l); }
                    l.Bin = x.Bin.Clean(); l.MinLevel = x.MinLevel; l.ReorderLevel = x.ReorderLevel; l.MaxLevel = x.MaxLevel; l.ReorderQuantity = x.ReorderQuantity;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { p.Id });
        }).RequirePermission(Permissions.InventoryManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.Parts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            // A part is shared by every site: whether it's in use counts in all of them, not only the ones this person sees.
            if (await db.StockTransactions.AllSites().AnyAsync(t => t.PartId == id, ct) || await db.PurchaseOrderLines.AllSites().AnyAsync(l => l.PartId == id, ct))
                throw new DomainException("This part has stock or purchasing history. Mark it inactive instead.");
            // Only work orders and PM tasks still in use count (deleted ones keep their rows but are filtered out of WorkOrders/PmTasks).
            if (await db.WorkOrderParts.AllSites().AnyAsync(l => l.PartId == id && db.WorkOrders.Any(w => w.Id == l.WorkOrderId), ct)
                || await db.PmTaskParts.AnyAsync(l => l.PartId == id && db.PmTasks.Any(x => x.Id == l.PmTaskId), ct))
                throw new DomainException("This part is planned on work orders or PM tasks. Remove it from them, or mark it inactive instead.");
            if (await db.AssetParts.AllSites().AnyAsync(x => x.PartId == id, ct))
                throw new DomainException("This part is on assets' spare-parts lists. Remove it from them, or mark it inactive instead.");
            await db.StockLevels.AllSites().Where(l => l.PartId == id).ExecuteDeleteAsync(ct);
            db.SupplierParts.RemoveRange(await db.SupplierParts.Where(r => r.PartId == id).ToListAsync(ct)); // its catalogue goes with it (0.18.0)
            db.Parts.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.InventoryManage);

        // ---------------- Transactions ----------------
        var t = api.MapGroup("/inventory").WithTags("Inventory");
        t.MapGet("/transactions", async (CmmsDbContext db, Guid? partId, Guid? storeId, Guid? workOrderId, Guid? purchaseOrderId, StockTransactionType? type,
            DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.StockTransactions.AsNoTracking();
            if (partId is not null) q = q.Where(x => x.PartId == partId);
            if (storeId is not null) q = q.Where(x => x.StoreId == storeId);
            if (workOrderId is not null) q = q.Where(x => x.WorkOrderId == workOrderId);
            if (purchaseOrderId is not null) q = q.Where(x => x.PurchaseOrderId == purchaseOrderId);
            if (type is not null) q = q.Where(x => x.Type == type);
            if (from is not null) q = q.Where(x => x.Date >= from);
            if (to is not null) q = q.Where(x => x.Date <= to);
            return await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id, x.Type, x.Date, x.PartId, PartNumber = x.Part!.PartNumber, PartName = x.Part.Name, Unit = x.Part.UnitOfMeasure, x.StoreId, StoreCode = x.Store!.Code,
                x.Quantity, x.UnitCost, Total = Math.Round(x.Quantity * x.UnitCost, 2), x.WorkOrderId, WorkOrderNumber = db.WorkOrders.Where(w => w.Id == x.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
                x.PurchaseOrderId, x.Reference, x.BatchNumber, x.ExpiryDate, x.Notes, x.CreatedByName, x.CreatedAt, x.LocationId, x.CostCentreId,
            }).ToPagedAsync(page ?? 1, pageSize ?? 50, ct);
        }).RequirePermission(Permissions.InventoryView);

        t.MapPost("/receive", async (ReceiveInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (Missing(i.PartId, i.StoreId) is { } bad) return bad;
            if (i.SupplierId is not null && !await db.Suppliers.AnyAsync(s => s.Id == i.SupplierId, ct)) throw new DomainException("Supplier not found.");
            var tx = await db.InTransactionAsync(tk => StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Receipt, i.PartId!.Value, i.StoreId!.Value, i.Quantity, i.Date ?? Today,
                i.UnitCost, SupplierId: i.SupplierId, Reference: i.Reference, BatchNumber: i.BatchNumber, ExpiryDate: i.ExpiryDate, Notes: i.Notes), tk), ct);
            return Results.Ok(new { tx.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        t.MapPost("/issue", async (IssueInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (Missing(i.PartId, i.StoreId) is { } bad) return bad;
            await RequireIssueTargetAsync(db, i.WorkOrderId, i.LocationId, ct);
            var post = new StockPost(StockTransactionType.Issue, i.PartId!.Value, i.StoreId!.Value, i.Quantity, i.Date ?? Today,
                WorkOrderId: i.WorkOrderId, LocationId: i.LocationId, BatchNumber: i.BatchNumber, Notes: i.Notes, CostCentreId: await DirectCostCentreAsync(db, i, ct));
            await StockLedger.CheckAsync(db, post, ct); // a batch that expired or ran out is said before "Issue anyway?"
            if (!i.ConfirmReserved && await ReservedConflictAsync(db, i.PartId!.Value, i.StoreId!.Value, i.Quantity, i.WorkOrderId, ct) is { } reserved) return reserved;
            var tx = await db.InTransactionAsync(tk => StockLedger.PostAsync(db, ctx, post, tk), ct);
            return Results.Ok(new { tx.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        t.MapPost("/return", async (IssueInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (Missing(i.PartId, i.StoreId) is { } bad) return bad;
            Guid? lastIssue = null;
            if (i.WorkOrderId is { } wo)
            {
                // Cannot return more than was (net) issued to the work order.
                var net = await db.StockTransactions.Where(x => x.WorkOrderId == wo && x.PartId == i.PartId && (x.Type == StockTransactionType.Issue || x.Type == StockTransactionType.Return))
                    .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
                if (i.Quantity > -net) throw new DomainException($"Only {-net:0.###} were issued to this work order and not yet returned.");
                lastIssue = await db.StockTransactions.Where(x => x.WorkOrderId == wo && x.PartId == i.PartId && x.Type == StockTransactionType.Issue).OrderByDescending(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            }
            var costCentre = await DirectCostCentreAsync(db, i, ct);
            var tx = await db.InTransactionAsync(tk => StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Return, i.PartId!.Value, i.StoreId!.Value, i.Quantity, i.Date ?? Today,
                WorkOrderId: i.WorkOrderId, LocationId: i.LocationId, BatchNumber: i.BatchNumber, Notes: i.Notes, RelatedTransactionId: lastIssue,
                CostCentreId: costCentre), tk), ct);
            return Results.Ok(new { tx.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        // A direct issue's or return's cost centre (0.20.0); work-order issues follow their work order's.
        t.MapPut("/transactions/{id:guid}/cost-centre", async (Guid id, CostCentreChange i, CmmsDbContext db, CancellationToken ct) =>
        {
            var tx = await db.StockTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (tx is null) return Results.NotFound();
            if (tx.WorkOrderId is not null || tx.Type is not (StockTransactionType.Issue or StockTransactionType.Return))
                throw new DomainException("Only parts issued to a location have their own cost centre.");
            if (i.CostCentreId is { } cc && !await db.CostCentres.AnyAsync(c => c.Id == cc, ct)) throw new DomainException("Choose an active cost centre.");
            tx.CostCentreId = i.CostCentreId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { tx.Id, tx.CostCentreId });
        }).RequirePermission(Permissions.InventoryManage);

        t.MapPost("/adjust", async (AdjustInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (Missing(i.PartId, i.StoreId) is { } bad) return bad;
            if (string.IsNullOrWhiteSpace(i.Notes)) return new Validator().Required("notes", i.Notes, "Reason").Problem();
            var tx = await db.InTransactionAsync(tk => StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Adjustment, i.PartId!.Value, i.StoreId!.Value, i.Quantity, i.Date ?? Today,
                BatchNumber: i.BatchNumber, Notes: i.Notes), tk), ct);
            return Results.Ok(new { tx.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        t.MapPost("/transfer", async (TransferInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (Missing(i.PartId, i.FromStoreId) is { } bad) return bad;
            if (i.ToStoreId is null) return new Validator().When(true, "toStoreId", "Choose the receiving store.").Problem();
            await StockLedger.CheckAsync(db, new StockPost(StockTransactionType.TransferOut, i.PartId!.Value, i.FromStoreId!.Value, i.Quantity, i.Date ?? Today,
                BatchNumber: i.BatchNumber.Clean()), ct);
            if (!i.ConfirmReserved && i.ToStoreId != i.FromStoreId && await ReservedConflictAsync(db, i.PartId!.Value, i.FromStoreId!.Value, i.Quantity, null, ct) is { } reserved) return reserved;
            var (o, _) = await db.InTransactionAsync(tk => StockLedger.TransferAsync(db, ctx, i.PartId!.Value, i.FromStoreId!.Value, i.ToStoreId.Value, i.Quantity, i.Date ?? Today, i.BatchNumber.Clean(), i.Notes, tk), ct);
            return Results.Ok(new { o.Id });
        }).RequirePermission(Permissions.InventoryTransact);

        // Stock take: counted quantities become adjustments for the differences.
        t.MapGet("/stock-take", async (Guid storeId, CmmsDbContext db, CancellationToken ct) =>
            await db.StockLevels.AsNoTracking().Where(l => l.StoreId == storeId && l.Part!.IsActive).OrderBy(l => l.Bin).ThenBy(l => l.Part!.PartNumber)
                .Select(l => new { l.PartId, l.Part!.PartNumber, l.Part.Name, l.Part.UnitOfMeasure, l.Bin, l.QuantityOnHand, l.Part.IsShelfItem }).ToListAsync(ct))
            .RequirePermission(Permissions.InventoryView);
        t.MapPost("/stock-take", async (StockTakeInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var date = i.Date ?? Today;
            var note = $"Stock take {date:yyyy-MM-dd}" + (i.Notes.Clean() is { } n ? $": {n}" : "");
            var adjusted = await db.InTransactionAsync(tk =>
                PostCountAsync(db, ctx, i.StoreId, date, i.Lines.GroupBy(l => l.PartId).Select(g => (g.Key, g.Last().CountedQuantity)).ToList(), note, tk), ct);
            return Results.Ok(new { adjusted });
        }).RequirePermission(Permissions.InventoryTransact);

        // Short for work (0.11.0): planned parts on open work that their store can't cover, in order of need — the store keeper's list.
        t.MapGet("/shortages", async (Guid? storeId, CmmsDbContext db, CancellationToken ct) =>
        {
            var pairs = await db.WorkOrderParts.AsNoTracking().Where(p => p.StoreId != null && (storeId == null || p.StoreId == storeId)
                    && db.WorkOrders.Any(w => w.Id == p.WorkOrderId && (w.Status == FourC.Cmms.Core.Work.WorkOrderStatus.Open
                        || w.Status == FourC.Cmms.Core.Work.WorkOrderStatus.InProgress || w.Status == FourC.Cmms.Core.Work.WorkOrderStatus.OnHold))
                    && db.Stores.Any(s => s.Id == p.StoreId))
                .Select(p => new { p.PartId, StoreId = p.StoreId!.Value }).Distinct().Take(2000).ToListAsync(ct);
            if (pairs.Count == 0) return Results.Ok(Array.Empty<object>());
            var partIds = pairs.Select(p => p.PartId).Distinct().ToList();
            var storeIds = pairs.Select(p => p.StoreId).Distinct().ToList();
            var lines = await Planning.OpenLinesAsync(db, partIds, storeIds, ct);
            var onHand = await db.StockLevels.AsNoTracking().Where(l => partIds.Contains(l.PartId) && storeIds.Contains(l.StoreId))
                .ToDictionaryAsync(l => (l.PartId, l.StoreId), l => l.QuantityOnHand, ct);
            var shorts = new List<(Guid LineId, Guid PartId, Guid StoreId, decimal Short)>();
            foreach (var pair in pairs)
                foreach (var (lineId, qty) in Reservations.Shortages(onHand.GetValueOrDefault((pair.PartId, pair.StoreId)), lines[(pair.PartId, pair.StoreId)]))
                    if (qty > 0) shorts.Add((lineId, pair.PartId, pair.StoreId, qty));
            var lineIds = shorts.Select(s => s.LineId).ToList();
            var info = await db.WorkOrderParts.AsNoTracking().Where(p => lineIds.Contains(p.Id))
                .Select(p => new { p.Id, p.WorkOrderId, WorkOrderNumber = db.WorkOrders.Where(w => w.Id == p.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
                    DueDate = db.WorkOrders.Where(w => w.Id == p.WorkOrderId).Select(w => w.DueDate).FirstOrDefault(),
                    p.Part!.PartNumber, PartName = p.Part.Name, p.Part.UnitOfMeasure, StoreCode = p.Store!.Code })
                .ToDictionaryAsync(p => p.Id, ct);
            return Results.Ok(shorts.Where(s => info.ContainsKey(s.LineId)).Select(s => new
            {
                info[s.LineId].WorkOrderId, info[s.LineId].WorkOrderNumber, info[s.LineId].DueDate, s.PartId, info[s.LineId].PartNumber, info[s.LineId].PartName,
                info[s.LineId].UnitOfMeasure, s.StoreId, info[s.LineId].StoreCode, Short = s.Short,
            }).OrderBy(x => x.DueDate ?? DateOnly.MaxValue).ThenBy(x => x.WorkOrderNumber));
        }).RequirePermission(Permissions.InventoryView);

        // Purchase proposal (4CSQL): store levels at or below re-order, considering quantity already on order.
        t.MapGet("/proposal", async (CmmsDbContext db, Guid? siteId, Guid? storeId, CancellationToken ct) =>
        {
            var q = db.StockLevels.AsNoTracking().Where(l => l.ReorderQuantity > 0 && l.Part!.IsActive);
            if (storeId is not null) q = q.Where(l => l.StoreId == storeId);
            if (siteId is not null) q = q.Where(l => l.Store!.SiteId == siteId);
            var rows = await q.Select(l => new
            {
                l.PartId, l.Part!.PartNumber, PartName = l.Part.Name, l.Part.UnitOfMeasure, l.Part.IsCritical, l.StoreId, StoreCode = l.Store!.Code, l.Store.SiteId,
                l.QuantityOnHand, l.MinLevel, l.ReorderLevel, l.MaxLevel, l.ReorderQuantity, l.Part.PreferredSupplierId, Price = l.Part.LastPurchasePrice ?? l.Part.AverageCost,
                Reserved = (db.StockReservations.Where(r => r.PartId == l.PartId && r.StoreId == l.StoreId).Sum(r => (decimal?)r.Reserved) ?? 0),
                OnOrder = db.PurchaseOrderLines.Where(x => x.PartId == l.PartId && x.StoreId == l.StoreId && db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId &&
                    (o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Submitted || OpenPo.Contains(o.Status)))).Sum(x => (decimal?)((x.Quantity - x.QuantityReceived) * x.PackSize)) ?? 0,
            }).ToListAsync(ct);
            var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            // The supplier catalogue (0.18.0): each part's active rows from active suppliers, preferred first, and who it was last received from.
            var partIds = rows.Select(r => r.PartId).Distinct().ToList();
            var catalogue = (await db.SupplierParts.AsNoTracking().Where(c => partIds.Contains(c.PartId) && c.IsActive && c.Supplier!.IsActive && c.Supplier.IsSupplier)
                    .Select(c => new { c.PartId, c.SupplierId, SupplierName = c.Supplier!.Name, c.PackName, c.PackSize, c.PackPrice, c.Currency, c.MinPacks, c.DiscountPercent })
                    .ToListAsync(ct)).ToLookup(c => c.PartId);
            var lastFrom = (await db.StockTransactions.AllSites().AsNoTracking()
                    .Where(t => partIds.Contains(t.PartId) && t.Type == StockTransactionType.Receipt && t.SupplierId != null)
                    .GroupBy(t => t.PartId).Select(g => new { PartId = g.Key, SupplierId = g.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).First().SupplierId })
                    .ToListAsync(ct)).ToDictionary(x => x.PartId, x => x.SupplierId);
            Guid? Suggested(Guid partId, Guid? preferred) =>
                preferred ?? (catalogue[partId].Select(c => c.SupplierId).Distinct().Count() == 1 ? (Guid?)catalogue[partId].First().SupplierId : null) ?? lastFrom.GetValueOrDefault(partId);
            // A level is proposed when available stock plus what is on order is at or below its re-order level.
            return rows.Where(r => r.QuantityOnHand - r.Reserved + r.OnOrder <= r.ReorderLevel).Select(r =>
            {
                var supplier = Suggested(r.PartId, r.PreferredSupplierId);
                var options = catalogue[r.PartId].OrderByDescending(c => c.SupplierId == r.PreferredSupplierId).ThenBy(c => c.SupplierName)
                    .Select(c => new { c.SupplierId, c.SupplierName, c.PackName, c.PackSize, c.PackPrice, c.Currency, c.MinPacks, c.DiscountPercent, IsPreferred = c.SupplierId == r.PreferredSupplierId })
                    .ToList();
                return new { r.PartId, r.PartNumber, r.PartName, r.UnitOfMeasure, r.IsCritical, r.StoreId, r.StoreCode, r.SiteId, r.QuantityOnHand, r.Reserved, Available = r.QuantityOnHand - r.Reserved,
                    r.OnOrder, r.MinLevel, r.ReorderLevel, r.MaxLevel,
                    SuggestedQuantity = SuggestedQuantity(r.QuantityOnHand, r.Reserved, r.OnOrder, r.ReorderQuantity, r.MaxLevel), PreferredSupplierId = supplier, SupplierName = supplier is { } sid ? suppliers.GetValueOrDefault(sid) : null, Catalogue = options, UnitPrice = r.Price,
                    BelowMinimum = r.QuantityOnHand < r.MinLevel };
            }).OrderByDescending(r => r.IsCritical).ThenByDescending(r => r.BelowMinimum).ThenBy(r => r.PartNumber);
        }).RequirePermission(Permissions.InventoryView);
    }

    /// <summary>Field errors for a missing part or store, so the dialog can mark the fields instead of failing the whole request.</summary>
    private static IResult? Missing(Guid? partId, Guid? storeId)
    {
        var v = new Validator().When(partId is null, "partId", "Choose a part.").When(storeId is null, "storeId", "Choose a store.");
        return v.IsValid ? null : v.Problem();
    }

    /// <summary>
    /// What the re-order proposal suggests for a store level: the re-order quantity, enough to reach the max level from available
    /// stock and open orders when a max level is set, or enough to cover planned need beyond stock and open orders — whichever is
    /// most, never below 0. With nothing reserved the last term is never positive, so the suggestion is the one before 0.3.0.
    /// </summary>
    /// <summary>
    /// Posts counted quantities as adjustments (the one-step stock-take and posting a stock count, 0.11.0). Returns how many parts
    /// changed. The caller holds the transaction.
    /// </summary>
    internal static async Task<int> PostCountAsync(CmmsDbContext db, ITenantContext ctx, Guid storeId, DateOnly date, IReadOnlyList<(Guid PartId, decimal Counted)> lines,
        string note, CancellationToken tk)
    {
        var count = 0;
        foreach (var (partId, counted) in lines)
        {
            if (counted < 0) throw new DomainException("Counted quantities cannot be negative.");
            var onHand = await db.StockLevels.Where(l => l.StoreId == storeId && l.PartId == partId).Select(l => (decimal?)l.QuantityOnHand).FirstOrDefaultAsync(tk) ?? 0;
            var diff = counted - onHand;
            if (diff == 0) continue;
            if (await db.Parts.Where(p => p.Id == partId).Select(p => p.IsShelfItem).FirstOrDefaultAsync(tk))
                throw new DomainException("Count shelf-life items per batch with an adjustment.");
            await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Adjustment, partId, storeId, diff, date, Notes: note), tk);
            count++;
        }
        return count;
    }

    internal static decimal SuggestedQuantity(decimal onHand, decimal reserved, decimal onOrder, decimal reorderQuantity, decimal maxLevel)
    {
        var available = onHand - reserved;
        var toMax = maxLevel > 0 ? maxLevel - available - onOrder : 0;
        var plannedNeed = reserved - Math.Max(onHand, 0) - onOrder;
        return Math.Max(Math.Max(reorderQuantity, toMax), Math.Max(plannedNeed, 0));
    }

    /// <summary>
    /// The "Issue anyway?" check: a 409 listing the other open work orders that taking <paramref name="quantity"/> from the store
    /// would leave short or more short, or null when it shorts nobody. A move the ledger will refuse anyway (not enough stock)
    /// gets that answer instead.
    /// </summary>
    private static async Task<IResult?> ReservedConflictAsync(CmmsDbContext db, Guid partId, Guid storeId, decimal quantity, Guid? toWorkOrderId, CancellationToken ct)
    {
        if (quantity <= 0) return null;
        var lines = await Planning.OpenLinesAsync(db, partId, storeId, ct);
        if (!lines.Any(l => l.StillReserved > 0 && l.WorkOrderId != toWorkOrderId)) return null;
        var onHand = await Planning.OnHandAsync(db, partId, storeId, ct);
        if (quantity > onHand && !await db.Parts.Where(p => p.Id == partId).Select(p => p.AllowNegativeStock).FirstOrDefaultAsync(ct)) return null;
        var changes = Reservations.WouldShort(onHand, lines, quantity, toWorkOrderId);
        if (changes.Count == 0) return null;
        var ids = changes.Select(c => c.WorkOrderId).ToList();
        var titles = await db.WorkOrders.AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        return Results.Json(new
        {
            Status = 409, Title = "This takes stock reserved for other work.",
            Reserved = changes.Select(c => new { c.WorkOrderId, c.WorkOrderNumber, Title = titles.GetValueOrDefault(c.WorkOrderId), c.MoreShort }),
        }, statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task RequireIssueTargetAsync(CmmsDbContext db, Guid? workOrderId, Guid? locationId, CancellationToken ct)
    {
        if (workOrderId is null && locationId is null) throw new DomainException("Issue to a work order or a location.");
        if (workOrderId is { } wo)
        {
            var status = await db.WorkOrders.Where(w => w.Id == wo).Select(w => (FourC.Cmms.Core.Work.WorkOrderStatus?)w.Status).FirstOrDefaultAsync(ct) ?? throw new DomainException("Work order not found.");
            if (status is FourC.Cmms.Core.Work.WorkOrderStatus.Closed or FourC.Cmms.Core.Work.WorkOrderStatus.Cancelled) throw new DomainException("This work order is closed.");
        }
        if (locationId is { } loc && !await db.Locations.AnyAsync(l => l.Id == loc, ct)) throw new DomainException("Location not found.");
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator StoreRules(StoreInput i) => new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200);

    internal static Store CreateStore(CmmsDbContext db, StoreInput i)
    {
        var s = new Store { Code = i.Code.Trim(), Name = i.Name.Trim(), SiteId = i.SiteId!.Value };
        UpdateStore(s, i);
        db.Stores.Add(s);
        return s;
    }

    internal static void UpdateStore(Store s, StoreInput i)
    {
        s.Code = i.Code.Trim(); s.Name = i.Name.Trim(); s.SiteId = i.SiteId!.Value; s.Description = i.Description.Clean(); s.IsActive = i.IsActive;
    }

    private static async Task<Validator> ValidateStoreAsync(StoreInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = StoreRules(i);
        if (i.SiteId is null || !await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.Stores, s => s.Code == i.Code.Trim() && s.Id != id,
                "Another store uses this code.", "This code is already used by a store in a site you can't see.", ct) is { } storeClash)
            v.Add("code", storeClash);
        return v;
    }

    /// <summary>The checks that need no database (shared with the import). Stock levels have their own, <see cref="LevelRules"/>.</summary>
    internal static Validator PartRules(PartInput i) => new Validator().Required("partNumber", i.PartNumber, "Part no.", 100).Required("name", i.Name, "Description", 300).Required("unitOfMeasure", i.UnitOfMeasure, "Unit", 30)
        .MaxLength("manufacturer", i.Manufacturer, "Manufacturer", 200).When(i.StandardPrice < 0, "standardPrice", "Price cannot be negative.")
        .When(i.LeadTimeDays < 0, "leadTimeDays", "Lead time cannot be negative.");

    /// <summary>Stock levels: none negative, minimum and re-order level within the maximum (shared with the import).</summary>
    internal static void LevelRules(Validator v, IReadOnlyCollection<LevelInput> levels)
    {
        if (levels.Any(l => l.MinLevel < 0 || l.ReorderLevel < 0 || l.MaxLevel < 0 || l.ReorderQuantity < 0)) v.Add("levels", "Levels cannot be negative.");
        if (levels.Any(l => l.MaxLevel > 0 && (l.ReorderLevel > l.MaxLevel || l.MinLevel > l.MaxLevel))) v.Add("levels", "Minimum and re-order levels must not exceed the maximum.");
    }

    /// <summary>A new part; its average cost starts at the standard price.</summary>
    internal static Part CreatePart(CmmsDbContext db, PartInput i)
    {
        var p = new Part { PartNumber = i.PartNumber.Trim(), Name = i.Name.Trim(), UnitOfMeasure = i.UnitOfMeasure.Trim(), AverageCost = i.StandardPrice };
        ApplyPart(p, i);
        db.Parts.Add(p);
        return p;
    }

    /// <summary>Part numbers are permanent (as in 4CSQL): the number isn't changed.</summary>
    internal static void UpdatePart(Part p, PartInput i)
    {
        p.Name = i.Name.Trim(); p.UnitOfMeasure = i.UnitOfMeasure.Trim();
        ApplyPart(p, i);
    }

    private static async Task<Validator> ValidatePartAsync(PartInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = PartRules(i);
        if (!string.IsNullOrWhiteSpace(i.PartNumber) && await db.Parts.AnyAsync(p => p.PartNumber == i.PartNumber.Trim() && p.Id != id, ct)) v.Add("partNumber", "Another part uses this number.");
        if (i.GroupId is not null && !await db.PartGroups.AnyAsync(x => x.Id == i.GroupId, ct)) v.Add("groupId", "Unknown group.");
        if (i.PreferredSupplierId is not null && !await db.Suppliers.AnyAsync(x => x.Id == i.PreferredSupplierId, ct)) v.Add("preferredSupplierId", "Unknown supplier.");
        var levels = i.Levels ?? [];
        if (levels.Select(l => l.StoreId).Distinct().Count() != levels.Count) v.Add("levels", "Each store can appear only once.");
        if (levels.Count > 0 && await db.Stores.CountAsync(s => levels.Select(l => l.StoreId).Contains(s.Id), ct) != levels.Select(l => l.StoreId).Distinct().Count()) v.Add("levels", "Unknown store.");
        LevelRules(v, levels);
        return v;
    }

    internal static void ApplyPart(Part p, PartInput i)
    {
        p.Specification = i.Specification.Clean(); p.Manufacturer = i.Manufacturer.Clean(); p.ManufacturerPartNumber = i.ManufacturerPartNumber.Clean(); p.GroupId = i.GroupId;
        p.StandardPrice = i.StandardPrice; p.LeadTimeDays = i.LeadTimeDays; p.IsCritical = i.IsCritical; p.IsShelfItem = i.IsShelfItem; p.AllowNegativeStock = i.AllowNegativeStock;
        p.PreferredSupplierId = i.PreferredSupplierId; p.Barcode = i.Barcode.Clean(); p.AlternatePartIds = (i.AlternatePartIds ?? []).Where(x => x != p.Id).Distinct().ToList();
        p.Notes = i.Notes.Clean(); p.IsActive = i.IsActive;
    }

    /// <summary>The cost centre of parts issued to or returned from a location without a work order (0.20.0): the one chosen, else the location's in force.</summary>
    private static async Task<Guid?> DirectCostCentreAsync(CmmsDbContext db, IssueInput i, CancellationToken ct)
    {
        if (i.WorkOrderId is not null) return null;
        if (i.CostCentreId is { } cc)
            return await db.CostCentres.AnyAsync(c => c.Id == cc, ct) ? cc : throw new DomainException("Choose an active cost centre.");
        return i.LocationId is { } l ? await CostCentreQueries.InForceAsync(db, null, l, ct) : null;
    }
}
