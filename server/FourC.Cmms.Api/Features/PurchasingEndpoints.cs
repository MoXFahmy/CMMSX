using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class PurchasingEndpoints
{
    public sealed record SupplierInput(string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address, string? City, string? Country,
        string? TaxNumber, string? PaymentTerms, string? Currency, bool IsContractor, string? Notes, bool IsActive = true);
    public sealed record LineInput(Guid? Id, Guid? PartId, string? Description, string? UnitOfMeasure, decimal Quantity, decimal UnitPrice, decimal DiscountPercent,
        Guid? StoreId, Guid? WorkOrderId, string? AccountCode);
    public sealed record PoInput(Guid SiteId, Guid SupplierId, DateOnly? OrderDate, DateOnly? ExpectedDate, string? RequestedBy, string? Currency, decimal? ExchangeRate,
        string? PaymentTerms, string? ShippingMethod, string? DeliverTo, string? SupplierQuoteRef, decimal? TaxPercent, decimal ShippingCost, string? Notes,
        List<LineInput> Lines, string? RowVersion = null);
    public sealed record ReasonInput(string? Reason);
    public sealed record ReceiveLine(Guid LineId, decimal Quantity, Guid? StoreId, string? BatchNumber, DateOnly? ExpiryDate);
    public sealed record ReceiveInput(DateOnly? Date, string? Reference, List<ReceiveLine> Lines);
    public sealed record ProposalLine(Guid PartId, Guid StoreId, decimal Quantity, decimal? UnitPrice, Guid? SupplierId);
    public sealed record FromProposalInput(List<ProposalLine> Lines);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static readonly PurchaseOrderStatus[] Receivable = [PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];

    public static void MapPurchasingEndpoints(this RouteGroupBuilder api)
    {
        // ---------------- Suppliers ----------------
        var s = api.MapGroup("/suppliers").WithTags("Suppliers");
        s.MapGet("/", async (CmmsDbContext db, string? search, bool? activeOnly, bool? contractors, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.Suppliers.AsNoTracking();
            if (search.Clean() is { } t) q = q.Where(x => x.Code.Contains(t) || x.Name.Contains(t) || x.ContactName!.Contains(t) || x.Email!.Contains(t) || x.City!.Contains(t));
            if (activeOnly == true) q = q.Where(x => x.IsActive);
            if (contractors == true) q = q.Where(x => x.IsContractor);
            var since = new DateOnly(Today.Year, 1, 1);
            return await q.OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone, x.City, x.Country, x.Currency, x.PaymentTerms, x.IsContractor, x.IsActive,
                OpenOrders = db.PurchaseOrders.Count(o => o.SupplierId == x.Id && o.Status >= PurchaseOrderStatus.Submitted && o.Status <= PurchaseOrderStatus.PartiallyReceived),
                ReceivedYtd = db.StockTransactions.Where(r => r.SupplierId == x.Id && r.Type == StockTransactionType.Receipt && r.Date >= since).Sum(r => (decimal?)(r.Quantity * r.UnitCost)) ?? 0,
            }).ToPagedAsync(page ?? 1, pageSize ?? 50, ct);
        }).RequirePermission(Permissions.PurchasingView);
        s.MapGet("/lookup", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.Currency, x.PaymentTerms }).ToListAsync(ct))
            .RequireAuthorization();
        s.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } x ? Results.Ok(x) : Results.NotFound())
            .RequirePermission(Permissions.PurchasingView);
        s.MapPost("/", async (SupplierInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateSupplierAsync(i, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = new Supplier { Code = i.Code.Trim(), Name = i.Name.Trim() };
            ApplySupplier(x, i);
            db.Suppliers.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/suppliers/{x.Id}", new { x.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        s.MapPut("/{id:guid}", async (Guid id, SupplierInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Suppliers.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var v = await ValidateSupplierAsync(i, id, db, ct);
            if (!v.IsValid) return v.Problem();
            x.Code = i.Code.Trim(); x.Name = i.Name.Trim();
            ApplySupplier(x, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        s.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Suppliers.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PurchaseOrders.AnyAsync(o => o.SupplierId == id, ct) || await db.StockTransactions.AnyAsync(t => t.SupplierId == id, ct))
                throw new DomainException("This supplier has purchasing history. Mark it inactive instead.");
            await db.Parts.Where(p => p.PreferredSupplierId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.PreferredSupplierId, (Guid?)null), ct);
            db.Suppliers.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SuppliersManage);

        // ---------------- Purchase orders ----------------
        var g = api.MapGroup("/purchase-orders").WithTags("Purchase orders");
        g.MapGet("/", async (CmmsDbContext db, string? search, string? status, Guid? supplierId, Guid? siteId, bool? overdue, DateOnly? from, DateOnly? to,
            string? sort, string? dir, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.PurchaseOrders.AsNoTracking();
            if (search.Clean() is { } t) q = q.Where(o => o.Number.Contains(t) || o.Supplier!.Name.Contains(t) || o.SupplierQuoteRef!.Contains(t) || o.RequestedBy!.Contains(t));
            var statuses = (status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<PurchaseOrderStatus>(x, true, out var e) ? e : (PurchaseOrderStatus?)null)
                .Where(x => x is not null).Select(x => x!.Value).ToList();
            if (statuses.Count > 0) q = q.Where(o => statuses.Contains(o.Status));
            if (supplierId is not null) q = q.Where(o => o.SupplierId == supplierId);
            if (siteId is not null) q = q.Where(o => o.SiteId == siteId);
            if (from is not null) q = q.Where(o => o.OrderDate >= from);
            if (to is not null) q = q.Where(o => o.OrderDate <= to);
            if (overdue == true) q = q.Where(o => Receivable.Contains(o.Status) && o.ExpectedDate < Today);
            var map = new Dictionary<string, System.Linq.Expressions.Expression<Func<PurchaseOrder, object?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["number"] = o => o.Number, ["orderDate"] = o => o.OrderDate, ["expectedDate"] = o => o.ExpectedDate, ["supplier"] = o => o.Supplier!.Name, ["status"] = o => o.Status,
            };
            var paged = await q.SortBy(sort ?? "orderDate", dir ?? (sort is null ? "desc" : dir), map, "orderDate")
                .Select(o => new { o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, o.SupplierId, SupplierName = o.Supplier!.Name, SiteName = o.Site!.Name, o.Currency, o.TaxPercent, o.ShippingCost, o.RequestedBy })
                .ToPagedAsync(page ?? 1, pageSize ?? 25, ct);
            var ids = paged.Items.Select(o => o.Id).ToList();
            var lines = (await db.PurchaseOrderLines.AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId)).ToListAsync(ct)).ToLookup(l => l.PurchaseOrderId);
            return new PagedResult<object>(paged.Items.Select(o =>
            {
                var ls = lines[o.Id].ToList();
                var po = new PurchaseOrder { Number = o.Number, TaxPercent = o.TaxPercent, ShippingCost = o.ShippingCost, Lines = ls };
                var ordered = ls.Sum(l => l.Quantity);
                return (object)new
                {
                    o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, o.SupplierId, o.SupplierName, o.SiteName, o.Currency, o.RequestedBy, Lines = ls.Count,
                    Total = PurchaseOrderMath.Total(po), ReceivedPercent = ordered == 0 ? 0 : Math.Round(ls.Sum(l => Math.Min(l.QuantityReceived, l.Quantity)) / ordered * 100, 0),
                    IsOverdue = Receivable.Contains(o.Status) && o.ExpectedDate < Today,
                };
            }).ToList(), paged.Total, paged.Page, paged.PageSize);
        }).RequirePermission(Permissions.PurchasingView);

        g.MapGet("/{id:guid}", async (Guid id, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var o = await db.PurchaseOrders.AsNoTracking().Include(x => x.Supplier).Include(x => x.Site).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return Results.NotFound();
            var partIds = o.Lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
            var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.PartNumber, p.Name, p.IsShelfItem }, ct);
            var storeIds = o.Lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).ToList();
            var stores = await db.Stores.AsNoTracking().Where(x => storeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
            var woIds = o.Lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).ToList();
            var wos = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Number, ct);
            var receipts = await db.StockTransactions.AsNoTracking().Where(t => t.PurchaseOrderId == id && t.Type == StockTransactionType.Receipt).OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Date, t.PurchaseOrderLineId, t.Part!.PartNumber, StoreCode = t.Store!.Code, t.Quantity, t.UnitCost, t.Reference, t.BatchNumber, t.CreatedByName }).ToListAsync(ct);
            var mine = await perms.GetAsync(UserId(http));
            var limit = await db.Users.Where(u => u.Id == UserId(http)).Select(u => u.PurchaseApprovalLimit).FirstOrDefaultAsync(ct);
            var totalBase = PurchaseOrderMath.TotalInBase(o);
            var canApproveAmount = limit is null || totalBase <= limit;
            var hasReceipts = o.Lines.Any(l => l.QuantityReceived > 0);
            return Results.Ok(new
            {
                o.Id, o.Number, o.Status, o.SiteId, SiteName = o.Site!.Name, o.SupplierId, Supplier = new { o.Supplier!.Id, o.Supplier.Code, o.Supplier.Name, o.Supplier.ContactName, o.Supplier.Email, o.Supplier.Phone, o.Supplier.Address, o.Supplier.City, o.Supplier.Country, o.Supplier.TaxNumber },
                o.OrderDate, o.ExpectedDate, o.RequestedBy, o.Currency, o.ExchangeRate, o.PaymentTerms, o.ShippingMethod, o.DeliverTo, o.SupplierQuoteRef, o.TaxPercent, o.ShippingCost, o.Notes,
                o.SubmittedAt, o.ApprovedAt, o.ApprovedByName, o.CancelledAt, o.CancelReason, o.CreatedAt, o.LegacyRef,
                Lines = o.Lines.OrderBy(l => l.LineNumber).Select(l => new
                {
                    l.Id, l.LineNumber, l.PartId, PartNumber = l.PartId is { } p ? parts.GetValueOrDefault(p)?.PartNumber : null, IsShelfItem = l.PartId is { } p2 && (parts.GetValueOrDefault(p2)?.IsShelfItem ?? false),
                    l.Description, l.UnitOfMeasure, l.Quantity, l.UnitPrice, l.DiscountPercent, l.NetUnitPrice, l.LineTotal, l.QuantityReceived, Outstanding = Math.Max(l.Quantity - l.QuantityReceived, 0),
                    l.StoreId, StoreCode = l.StoreId is { } sid ? stores.GetValueOrDefault(sid) : null, l.WorkOrderId, WorkOrderNumber = l.WorkOrderId is { } wid ? wos.GetValueOrDefault(wid) : null, l.AccountCode,
                }),
                Totals = new { Subtotal = PurchaseOrderMath.Subtotal(o), Tax = PurchaseOrderMath.Tax(o), o.ShippingCost, Total = PurchaseOrderMath.Total(o), TotalInBase = totalBase },
                Receipts = receipts,
                IsOverdue = Receivable.Contains(o.Status) && o.ExpectedDate < Today,
                Actions = new
                {
                    Edit = o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate),
                    Submit = o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate),
                    Approve = o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted && mine.Contains(Permissions.PurchasingApprove) && canApproveAmount,
                    ApprovalLimitExceeded = o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted && mine.Contains(Permissions.PurchasingApprove) && !canApproveAmount,
                    Reject = o.Status == PurchaseOrderStatus.Submitted && mine.Contains(Permissions.PurchasingApprove),
                    Receive = Receivable.Contains(o.Status) && mine.Contains(Permissions.PurchasingReceive),
                    Close = o.Status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received && mine.Contains(Permissions.PurchasingApprove),
                    Cancel = o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Approved && !hasReceipts
                        && (mine.Contains(Permissions.PurchasingApprove) || (o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate))),
                    Delete = o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate),
                },
                RowVersion = Convert.ToBase64String(o.RowVersion),
            });
        }).RequirePermission(Permissions.PurchasingView);

        g.MapPost("/", async (PoInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var v = await ValidatePoAsync(i, db, ct);
            if (!v.IsValid) return v.Problem();
            var o = await CreateDraftAsync(db, ctx, i, ct);
            return Results.Created($"/api/purchase-orders/{o.Id}", new { o.Id, o.Number });
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPut("/{id:guid}", async (Guid id, PoInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var o = await db.PurchaseOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return Results.NotFound();
            if (o.Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be edited. Return it to draft first.");
            var v = await ValidatePoAsync(i, db, ct);
            if (!v.IsValid) return v.Problem();
            if (i.RowVersion is not null) db.Entry(o).Property(x => x.RowVersion).OriginalValue = i.RowVersion.ToVersion();
            await ApplyPoAsync(db, o, i, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id });
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var o = await db.PurchaseOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return Results.NotFound();
            if (o.Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be deleted. Cancel it instead.");
            db.PurchaseOrders.Remove(o);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPost("/{id:guid}/submit", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.Draft);
            await RequireReadyAsync(db, o, ct);
            o.Status = PurchaseOrderStatus.Submitted; o.SubmittedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted);
            await RequireReadyAsync(db, o, ct);
            var limit = await db.Users.Where(u => u.Id == UserId(http)).Select(u => u.PurchaseApprovalLimit).FirstOrDefaultAsync(ct);
            var total = PurchaseOrderMath.TotalInBase(o);
            if (limit is not null && total > limit) throw new DomainException($"This order ({total:N2}) is above your approval limit of {limit:N2}.");
            o.Status = PurchaseOrderStatus.Approved; o.SubmittedAt ??= DateTimeOffset.UtcNow;
            o.ApprovedAt = DateTimeOffset.UtcNow; o.ApprovedById = ctx.UserId; o.ApprovedByName = ctx.UserName;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        g.MapPost("/{id:guid}/reject", async (Guid id, ReasonInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.Submitted);
            if (i.Reason.Clean() is not { } reason) return new Validator().Required("reason", i.Reason, "Reason").Problem();
            o.Status = PurchaseOrderStatus.Draft; o.SubmittedAt = null;
            o.Notes = $"{o.Notes}{(o.Notes is null ? "" : "\n")}Returned by {ctx.UserName} on {Today:yyyy-MM-dd}: {reason}";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        g.MapPost("/{id:guid}/cancel", async (Guid id, ReasonInput i, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved);
            var mine = await perms.GetAsync(UserId(http));
            if (!mine.Contains(Permissions.PurchasingApprove) && !(o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate))) return Results.Forbid();
            if (o.Lines.Any(l => l.QuantityReceived > 0)) throw new DomainException("Goods have been received on this order. Close it instead.");
            if (i.Reason.Clean() is not { } reason) return new Validator().Required("reason", i.Reason, "Reason").Problem();
            o.Status = PurchaseOrderStatus.Cancelled; o.CancelledAt = DateTimeOffset.UtcNow; o.CancelReason = reason;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingView);

        g.MapPost("/{id:guid}/close", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received);
            o.Status = PurchaseOrderStatus.Closed;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        // Goods receipt: stock lines post to the ledger at the net price in base currency; work-order lines become direct costs.
        g.MapPost("/{id:guid}/receive", async (Guid id, ReceiveInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var date = i.Date ?? Today;
            if (date > Today) throw new DomainException("The receipt date cannot be in the future.");
            var received = await db.InTransactionAsync(async tk =>
            {
                var o = await LoadAsync(db, id, tk);
                Require(o, Receivable);
                var count = 0;
                foreach (var r in i.Lines.Where(x => x.Quantity != 0))
                {
                    var line = o.Lines.FirstOrDefault(l => l.Id == r.LineId) ?? throw new DomainException("Unknown order line.");
                    if (r.Quantity < 0) throw new DomainException($"Line {line.LineNumber}: quantity must be positive.");
                    var outstanding = line.Quantity - line.QuantityReceived;
                    if (r.Quantity > outstanding) throw new DomainException($"Line {line.LineNumber}: only {outstanding:0.###} outstanding.");
                    var unitCost = Math.Round(line.NetUnitPrice * o.ExchangeRate, 4);
                    var store = r.StoreId ?? line.StoreId;
                    if (line.PartId is { } partId && store is { } storeId)
                    {
                        var rx = await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Receipt, partId, storeId, r.Quantity, date, unitCost,
                            SupplierId: o.SupplierId, PurchaseOrderId: o.Id, PurchaseOrderLineId: line.Id, Reference: i.Reference.Clean() ?? o.Number,
                            BatchNumber: r.BatchNumber.Clean(), ExpiryDate: r.ExpiryDate), tk);
                        if (line.WorkOrderId is { } wo) // ordered for a job: receive and issue straight to it
                            await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Issue, partId, storeId, r.Quantity, date, WorkOrderId: wo,
                                BatchNumber: r.BatchNumber.Clean(), Notes: $"Received on {o.Number}", RelatedTransactionId: rx.Id), tk);
                    }
                    else if (line.PartId is not null)
                        throw new DomainException($"Line {line.LineNumber}: choose the store receiving this part.");
                    else if (line.WorkOrderId is { } wo)
                        db.WorkOrderCosts.Add(new WorkOrderCost { TenantId = o.TenantId, WorkOrderId = wo, Description = line.Description, Quantity = r.Quantity, UnitCost = unitCost,
                            Vendor = o.Supplier?.Name, Reference = o.Number });
                    line.QuantityReceived += r.Quantity;
                    count++;
                }
                if (count == 0) throw new DomainException("Enter the quantity received on at least one line.");
                o.Status = o.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
                await db.SaveChangesAsync(tk);
                return count;
            }, ct);
            return Results.Ok(new { received });
        }).RequirePermission(Permissions.PurchasingReceive);

        // Turn purchase-proposal lines into draft orders, one per supplier and site.
        g.MapPost("/from-proposal", async (FromProposalInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (i.Lines.Count == 0) throw new DomainException("Select at least one line.");
            var partIds = i.Lines.Select(l => l.PartId).Distinct().ToList();
            var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var storeIds = i.Lines.Select(l => l.StoreId).Distinct().ToList();
            var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            var resolved = i.Lines.Select(l =>
            {
                var part = parts.GetValueOrDefault(l.PartId) ?? throw new DomainException("Unknown part.");
                var store = stores.GetValueOrDefault(l.StoreId) ?? throw new DomainException("Unknown store.");
                var supplier = l.SupplierId ?? part.PreferredSupplierId ?? throw new DomainException($"Choose a supplier for {part.PartNumber}.");
                if (l.Quantity <= 0) throw new DomainException($"Enter a quantity for {part.PartNumber}.");
                return (Part: part, Store: store, SupplierId: supplier, l.Quantity, Price: l.UnitPrice ?? part.LastPurchasePrice ?? part.AverageCost);
            }).ToList();
            var supplierIds = resolved.Select(r => r.SupplierId).Distinct().ToList();
            var suppliers = await db.Suppliers.AsNoTracking().Where(x => supplierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            if (suppliers.Count != supplierIds.Count) throw new DomainException("Unknown supplier.");
            var created = await db.InTransactionAsync(async tk =>
            {
                var list = new List<object>();
                foreach (var grp in resolved.GroupBy(r => (r.SupplierId, r.Store.SiteId)))
                {
                    var sup = suppliers[grp.Key.SupplierId];
                    var input = new PoInput(grp.Key.SiteId, sup.Id, Today, null, ctx.UserName, sup.Currency, null, sup.PaymentTerms, null, null, null, null, 0,
                        "Created from the purchase proposal.", grp.Select(r => new LineInput(null, r.Part.Id, r.Part.Name, r.Part.UnitOfMeasure, r.Quantity, r.Price, 0, r.Store.Id, null, null)).ToList());
                    var o = await CreateDraftAsync(db, ctx, input, tk);
                    list.Add(new { o.Id, o.Number, Supplier = sup.Name, Lines = o.Lines.Count });
                }
                return list;
            }, ct);
            return Results.Ok(created);
        }).RequirePermission(Permissions.PurchasingCreate);
    }

    private static async Task<PurchaseOrder> CreateDraftAsync(CmmsDbContext db, ITenantContext ctx, PoInput i, CancellationToken ct)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var number = await Numbering.NextAsync(db, "PurchaseOrder", settings.PurchaseOrderNumberPrefix, 5,
            n => db.PurchaseOrders.AnyAsync(o => o.Number == n, ct), p => db.PurchaseOrders.Where(o => o.Number.StartsWith(p)).Select(o => o.Number).ToListAsync(ct), ct);
        var o = new PurchaseOrder { Number = number, SiteId = i.SiteId, SupplierId = i.SupplierId, Currency = settings.Currency, TaxPercent = settings.DefaultTaxPercent, RequestedBy = ctx.UserName };
        await ApplyPoAsync(db, o, i with { TaxPercent = i.TaxPercent ?? settings.DefaultTaxPercent, Currency = i.Currency.Clean() ?? settings.Currency, RequestedBy = i.RequestedBy.Clean() ?? ctx.UserName }, ct);
        db.PurchaseOrders.Add(o);
        await db.SaveChangesAsync(ct);
        return o;
    }

    private static async Task ApplyPoAsync(CmmsDbContext db, PurchaseOrder o, PoInput i, CancellationToken ct)
    {
        o.SiteId = i.SiteId; o.SupplierId = i.SupplierId; o.OrderDate = i.OrderDate ?? Today; o.ExpectedDate = i.ExpectedDate; o.RequestedBy = i.RequestedBy.Clean();
        o.Currency = i.Currency.Clean()?.ToUpperInvariant() ?? o.Currency; o.ExchangeRate = i.ExchangeRate is > 0 ? i.ExchangeRate.Value : 1;
        o.PaymentTerms = i.PaymentTerms.Clean(); o.ShippingMethod = i.ShippingMethod.Clean(); o.DeliverTo = i.DeliverTo.Clean(); o.SupplierQuoteRef = i.SupplierQuoteRef.Clean();
        o.TaxPercent = i.TaxPercent ?? o.TaxPercent; o.ShippingCost = i.ShippingCost; o.Notes = i.Notes.Clean();
        var partIds = i.Lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var gone in o.Lines.Where(l => i.Lines.All(x => x.Id != l.Id)).ToList()) o.Lines.Remove(gone);
        var n = 1;
        foreach (var x in i.Lines)
        {
            var line = x.Id is { } lid ? o.Lines.FirstOrDefault(l => l.Id == lid) : null;
            var part = x.PartId is { } pid ? parts[pid] : null;
            if (line is null) { line = new PurchaseOrderLine { Description = "" }; o.Lines.Add(line); }
            line.LineNumber = n++; line.PartId = x.PartId; line.Description = x.Description.Clean() ?? part?.Name ?? "";
            line.UnitOfMeasure = x.UnitOfMeasure.Clean() ?? part?.UnitOfMeasure; line.Quantity = x.Quantity; line.UnitPrice = x.UnitPrice; line.DiscountPercent = x.DiscountPercent;
            line.StoreId = x.StoreId; line.WorkOrderId = x.WorkOrderId; line.AccountCode = x.AccountCode.Clean();
        }
    }

    private static async Task<Validator> ValidatePoAsync(PoInput i, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().MaxLength("supplierQuoteRef", i.SupplierQuoteRef, "Quote ref.", 100).MaxLength("notes", i.Notes, "Notes", 4000)
            .When(i.ShippingCost < 0, "shippingCost", "Shipping cannot be negative.").When(i.TaxPercent is < 0 or > 100, "taxPercent", "Tax must be between 0 and 100%.")
            .When(i.ExchangeRate is <= 0, "exchangeRate", "Exchange rate must be positive.").When(i.ExpectedDate < i.OrderDate, "expectedDate", "Expected date is before the order date.");
        if (!await db.Sites.AnyAsync(x => x.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (!await db.Suppliers.AnyAsync(x => x.Id == i.SupplierId && x.IsActive, ct)) v.Add("supplierId", "Choose an active supplier.");
        var lines = i.Lines ?? [];
        for (var k = 0; k < lines.Count; k++)
        {
            var l = lines[k];
            if (l.PartId is null && string.IsNullOrWhiteSpace(l.Description)) v.Add($"lines[{k}].description", "Enter a description or choose a part.");
            if (l.Quantity <= 0) v.Add($"lines[{k}].quantity", "Quantity must be positive.");
            if (l.UnitPrice < 0) v.Add($"lines[{k}].unitPrice", "Price cannot be negative.");
            if (l.DiscountPercent is < 0 or > 100) v.Add($"lines[{k}].discountPercent", "Discount must be between 0 and 100%.");
            if (l.PartId is not null && l.StoreId is null) v.Add($"lines[{k}].storeId", "Choose the receiving store.");
        }
        var partIds = lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).Distinct().ToList();
        if (partIds.Count > 0 && await db.Parts.CountAsync(p => partIds.Contains(p.Id), ct) != partIds.Count) v.Add("lines", "Unknown part.");
        var storeIds = lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).Distinct().ToList();
        if (storeIds.Count > 0 && await db.Stores.CountAsync(s => storeIds.Contains(s.Id), ct) != storeIds.Count) v.Add("lines", "Unknown store.");
        var woIds = lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).Distinct().ToList();
        if (woIds.Count > 0 && await db.WorkOrders.CountAsync(w => woIds.Contains(w.Id) && w.Status != WorkOrderStatus.Closed && w.Status != WorkOrderStatus.Cancelled, ct) != woIds.Count)
            v.Add("lines", "A work order on this order is unknown or closed.");
        return v;
    }

    private static async Task<PurchaseOrder> LoadAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(o => o.Lines).Include(o => o.Supplier).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new BadHttpRequestException("Purchase order not found.", 404);

    private static void Require(PurchaseOrder o, params PurchaseOrderStatus[] allowed)
    {
        if (!allowed.Contains(o.Status)) throw new DomainException($"This action is not available while the order is {o.Status}.");
    }

    private static async Task RequireReadyAsync(CmmsDbContext db, PurchaseOrder o, CancellationToken ct)
    {
        if (o.Lines.Count == 0) throw new DomainException("Add at least one line.");
        if (!await db.Suppliers.AnyAsync(s => s.Id == o.SupplierId && s.IsActive, ct)) throw new DomainException("The supplier is inactive.");
    }

    private static async Task<Validator> ValidateSupplierAsync(SupplierInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("email", i.Email, "Email", 200)
            .When(i.Email.Clean() is { } e && !e.Contains('@'), "email", "Enter a valid email address.").When(i.Currency.Clean() is { Length: not 3 }, "currency", "Use a 3-letter currency code.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.Suppliers.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another supplier uses this code.");
        return v;
    }

    private static void ApplySupplier(Supplier x, SupplierInput i)
    {
        x.ContactName = i.ContactName.Clean(); x.Email = i.Email.Clean(); x.Phone = i.Phone.Clean(); x.Address = i.Address.Clean(); x.City = i.City.Clean(); x.Country = i.Country.Clean();
        x.TaxNumber = i.TaxNumber.Clean(); x.PaymentTerms = i.PaymentTerms.Clean(); x.Currency = i.Currency.Clean()?.ToUpperInvariant(); x.IsContractor = i.IsContractor;
        x.Notes = i.Notes.Clean(); x.IsActive = i.IsActive;
    }
}
