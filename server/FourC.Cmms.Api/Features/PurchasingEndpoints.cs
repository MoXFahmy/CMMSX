using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
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
        string? TaxNumber, string? PaymentTerms, string? Currency, bool IsContractor, string? Notes, bool IsActive = true,
        // Companies (0.17.0): roles and lists; a role or list left out (null) stays as it is — the import has none of them.
        bool? IsSupplier = null, bool? IsManufacturer = null, List<SupplierContactInput>? Contacts = null, List<SupplierAddressInput>? Addresses = null,
        List<SupplierRateInput>? Rates = null);
    public sealed record SupplierContactInput(Guid? Id, string Name, string? Role, string? Phone, string? Email);
    public sealed record SupplierAddressInput(Guid? Id, string? Label, string Address, string? City, string? Country);
    public sealed record SupplierRateInput(Guid CategoryId, decimal Rate);
    public sealed record LineInput(Guid? Id, Guid? PartId, string? Description, string? UnitOfMeasure, decimal Quantity, decimal UnitPrice, decimal DiscountPercent,
        Guid? StoreId, Guid? WorkOrderId, string? AccountCode, Guid? PurchaseRequestLineId = null,
        // The pack (0.18.0): pieces per ordered unit and the supplier's part number; left out (older pages) keeps the line's, 1 on a new line.
        decimal? PackSize = null, string? SupplierPartNumber = null,
        // A repair line (0.24.0): a unit under repair going to the supplier; left out (older pages) keeps the line's.
        Guid? UnitId = null);
    public sealed record PoInput(Guid? SiteId, Guid? SupplierId, DateOnly? OrderDate, DateOnly? ExpectedDate, string? RequestedBy, string? Currency, decimal? ExchangeRate,
        string? PaymentTerms, string? ShippingMethod, string? DeliverTo, string? SupplierQuoteRef, decimal? TaxPercent, decimal ShippingCost, string? Notes,
        List<LineInput> Lines, Guid? OrderTypeId = null, string? RowVersion = null);
    public sealed record ReasonInput(string? Reason);
    /// <param name="Serials">A serial-tracked part's serial numbers, one per piece received (quantity × pack size, 0.24.0).</param>
    public sealed record ReceiveLine(Guid LineId, decimal Quantity, Guid? StoreId, string? BatchNumber, DateOnly? ExpiryDate, List<string>? Serials = null);
    public sealed record ReceiveInput(DateOnly? Date, string? Reference, List<ReceiveLine> Lines);
    public sealed record ProposalLine(Guid PartId, Guid StoreId, decimal Quantity, decimal? UnitPrice, Guid? SupplierId);
    public sealed record FromProposalInput(List<ProposalLine> Lines);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static readonly PurchaseOrderStatus[] Receivable = [PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];
    /// <summary>Orders on which a repair line still holds its unit (0.24.0).</summary>
    private static readonly PurchaseOrderStatus[] OpenRepair =
        [PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];

    public sealed class PoFilter
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public Guid? SupplierId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? OrderTypeId { get; set; }
        public bool? Overdue { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record PoListItem(Guid Id, string Number, PurchaseOrderStatus Status, DateOnly OrderDate, DateOnly? ExpectedDate, Guid SupplierId, string SupplierName,
        string SiteName, string Currency, string? RequestedBy, int Lines, decimal Total, decimal ReceivedPercent, bool IsOverdue, string? OrderTypeName = null);

    /// <summary>One page of the purchase-order list (the list screen and its export share it).</summary>
    internal static async Task<PagedResult<PoListItem>> ListAsync(CmmsDbContext db, PoFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 500)
    {
            var q = db.PurchaseOrders.AsNoTracking();
        if (f.Search.Clean() is { } t) q = q.Where(o => o.Number.Contains(t) || o.Supplier!.Name.Contains(t) || o.SupplierQuoteRef!.Contains(t) || o.RequestedBy!.Contains(t));
        var statuses = (f.Status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<PurchaseOrderStatus>(x, true, out var e) ? e : (PurchaseOrderStatus?)null)
            .Where(x => x is not null).Select(x => x!.Value).ToList();
        if (statuses.Count > 0) q = q.Where(o => statuses.Contains(o.Status));
        if (f.SupplierId is not null) q = q.Where(o => o.SupplierId == f.SupplierId);
        if (f.SiteId is not null) q = q.Where(o => o.SiteId == f.SiteId);
        if (f.OrderTypeId is not null) q = q.Where(o => o.OrderTypeId == f.OrderTypeId);
        if (f.From is not null) q = q.Where(o => o.OrderDate >= f.From);
        if (f.To is not null) q = q.Where(o => o.OrderDate <= f.To);
        if (f.Overdue == true) q = q.Where(o => Receivable.Contains(o.Status) && o.ExpectedDate < Today);
        var map = new Dictionary<string, System.Linq.Expressions.Expression<Func<PurchaseOrder, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = o => o.Number, ["orderDate"] = o => o.OrderDate, ["expectedDate"] = o => o.ExpectedDate, ["supplier"] = o => o.Supplier!.Name, ["status"] = o => o.Status,
        };
        var paged = await q.SortBy(f.Sort ?? "orderDate", f.Dir ?? (f.Sort is null ? "desc" : f.Dir), map, "orderDate")
            .Select(o => new { o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, o.SupplierId, SupplierName = o.Supplier!.Name, SiteName = o.Site!.Name, o.Currency, o.TaxPercent, o.ShippingCost, o.RequestedBy,
                OrderTypeName = db.OrderTypes.Where(t => t.Id == o.OrderTypeId).Select(t => t.Name).FirstOrDefault() })
            .ToPagedAsync(page, pageSize, ct, maxPageSize);
        var ids = paged.Items.Select(o => o.Id).ToList();
        var lines = (await db.PurchaseOrderLines.AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId)).ToListAsync(ct)).ToLookup(l => l.PurchaseOrderId);
        return new PagedResult<PoListItem>(paged.Items.Select(o =>
        {
            var ls = lines[o.Id].ToList();
            var po = new PurchaseOrder { Number = o.Number, TaxPercent = o.TaxPercent, ShippingCost = o.ShippingCost, Lines = ls };
            var ordered = ls.Sum(l => l.Quantity);
            return new PoListItem(o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, o.SupplierId, o.SupplierName, o.SiteName, o.Currency, o.RequestedBy, ls.Count,
                PurchaseOrderMath.Total(po), ordered == 0 ? 0 : Math.Round(ls.Sum(l => Math.Min(l.QuantityReceived, l.Quantity)) / ordered * 100, 0),
                Receivable.Contains(o.Status) && o.ExpectedDate < Today, o.OrderTypeName);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    /// <summary>Receives order lines (the web's Receive and the phone's, 0.11.0); the caller holds the transaction.</summary>
    internal static async Task<int> ReceiveCoreAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, Guid id, ReceiveInput i, DateOnly date, CancellationToken tk)
    {
        var o = await LoadAsync(db, id, tk);
        Require(o, Receivable);
        // The lines carry no row version, so the order's does the job: two receipts of one order at once both read the same outstanding quantity, and
        // the second one's save finds the order changed ("changed by someone else") instead of receiving past the line (0.11.0-3, 0.29.1).
        db.Entry(o).Property(x => x.UpdatedAt).IsModified = true;
        var count = 0;
        var forWork = new List<(Guid WorkOrderId, string Line)>();
        foreach (var r in i.Lines.Where(x => x.Quantity != 0))
        {
            var line = o.Lines.FirstOrDefault(l => l.Id == r.LineId) ?? throw new DomainException("Unknown order line.");
            if (r.Quantity < 0) throw new DomainException($"Line {line.LineNumber}: quantity must be positive.");
            var outstanding = line.Quantity - line.QuantityReceived;
            if (r.Quantity > outstanding) throw new DomainException($"Line {line.LineNumber}: only {outstanding:0.###} outstanding.");
            // A repair line brings one unit back: all of it or nothing, so its cost isn't split (0.26.1).
            if (line.UnitId is not null && r.Quantity != outstanding) throw new DomainException($"Line {line.LineNumber}: {UnitRules.RepairWhole}");
            var unitCost = Math.Round(line.NetUnitPrice * o.ExchangeRate, 4);
            var store = r.StoreId ?? line.StoreId;
            if (line.UnitId is { } repairedId)
            {
                // A repair line (0.24.0): the unit comes back into a store with the line's cost in the organization's currency.
                var back = store is { } backStore ? await db.Stores.FirstOrDefaultAsync(x => x.Id == backStore, tk) : null;
                if (back is null) throw new DomainException($"Line {line.LineNumber}: Choose the store the unit comes back to.");
                var repaired = await db.Assets.AllSites().FirstAsync(a => a.Id == repairedId, tk);
                await SerialUnits.BackFromRepairAsync(db, ctx, repaired, back, Math.Round(line.NetUnitPrice * o.ExchangeRate * r.Quantity, 2), $"Repaired on {o.Number}",
                    DateTimeOffset.UtcNow, tk, o.Id, line.Id);
            }
            if (line.PartId is { } partId && store is { } storeId)
            {
                // Packs become the part's own units (0.18.0): 1 box of 100 books 100 pieces, each at the box's cost ÷ 100.
                var pieces = r.Quantity * line.PackSize;
                var pieceCost = PackMath.CostPerPiece(line.NetUnitPrice, o.ExchangeRate, line.PackSize);
                var part = await db.Parts.FirstAsync(p => p.Id == partId, tk);
                if (part.IsSerialTracked)
                {
                    // Serial units (0.24.0): one per piece, each with its serial; even ordered for a job they wait in the store until fitted on it.
                    var serials = await SerialUnitEndpoints.ReceiptSerialsAsync(db, part, pieces, r.Serials, tk, $"Line {line.LineNumber}: ");
                    var unitStore = await db.Stores.FirstOrDefaultAsync(x => x.Id == storeId, tk) ?? throw new DomainException($"Line {line.LineNumber}: choose the store receiving this part.");
                    await SerialUnits.ReceiveAsync(db, ctx, part, unitStore, serials, date, pieceCost, new StockPost(StockTransactionType.Receipt, partId, storeId, pieces, date, pieceCost,
                        SupplierId: o.SupplierId, PurchaseOrderId: o.Id, PurchaseOrderLineId: line.Id, Reference: i.Reference.Clean() ?? o.Number), tk);
                }
                else
                {
                    var rx = await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Receipt, partId, storeId, pieces, date, pieceCost,
                        SupplierId: o.SupplierId, PurchaseOrderId: o.Id, PurchaseOrderLineId: line.Id, Reference: i.Reference.Clean() ?? o.Number,
                        BatchNumber: r.BatchNumber.Clean(), ExpiryDate: r.ExpiryDate), tk);
                    if (line.WorkOrderId is { } wo) // ordered for a job: receive and issue straight to it
                        await StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Issue, partId, storeId, pieces, date, WorkOrderId: wo,
                            BatchNumber: r.BatchNumber.Clean(), Notes: $"Received on {o.Number}", RelatedTransactionId: rx.Id), tk);
                }
            }
            else if (line.PartId is not null)
                throw new DomainException($"Line {line.LineNumber}: choose the store receiving this part.");
            else if (line.WorkOrderId is { } wo)
                db.WorkOrderCosts.Add(new WorkOrderCost { TenantId = o.TenantId, WorkOrderId = wo, Description = line.Description, Quantity = r.Quantity, UnitCost = unitCost,
                    Vendor = o.Supplier?.Name, Reference = o.Number });
            line.QuantityReceived += r.Quantity;
            if (line.PartId is { } paidPart) await RecordLastPaidAsync(db, o, line, paidPart, date, tk);
            if (line.WorkOrderId is { } forWo) forWork.Add((forWo, $"{line.Description}: {r.Quantity:0.###} {line.UnitOfMeasure}".Trim()));
            count++;
        }
        if (count == 0) throw new DomainException("Enter the quantity received on at least one line.");
        o.Status = o.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        await mail.PartsReceivedAsync(db, ctx, o, forWork, tk);
        await PurchaseRequestSync.ReceivedAsync(db, ctx, mail, o, i.Lines.Where(x => x.Quantity > 0).Select(x => (x.LineId, x.Quantity)).ToList(), tk);
        await db.SaveChangesAsync(tk);
        return count;
    }

    public static void MapPurchasingEndpoints(this RouteGroupBuilder api)
    {
        // ---------------- Suppliers ----------------
        var s = api.MapGroup("/suppliers").WithTags("Suppliers");
        s.MapGet("/", async (CmmsDbContext db, string? search, bool? activeOnly, bool? contractors, string? role, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.Suppliers.AsNoTracking();
            if (search.Clean() is { } t) q = q.Where(x => x.Code.Contains(t) || x.Name.Contains(t) || x.ContactName!.Contains(t) || x.Email!.Contains(t) || x.City!.Contains(t));
            if (activeOnly == true) q = q.Where(x => x.IsActive);
            q = WithRole(q, contractors == true ? "contractor" : role);
            var since = new DateOnly(Today.Year, 1, 1);
            return await q.OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone, x.City, x.Country, x.Currency, x.PaymentTerms, x.IsSupplier, x.IsContractor, x.IsManufacturer, x.IsActive,
                OpenOrders = db.PurchaseOrders.Count(o => o.SupplierId == x.Id && o.Status >= PurchaseOrderStatus.Submitted && o.Status <= PurchaseOrderStatus.PartiallyReceived),
                ReceivedYtd = db.StockTransactions.Where(r => r.SupplierId == x.Id && r.Type == StockTransactionType.Receipt && r.Date >= since).Sum(r => (decimal?)(r.Quantity * r.UnitCost)) ?? 0,
            }).ToPagedAsync(page ?? 1, pageSize ?? 50, ct);
        }).RequirePermission(Permissions.PurchasingView);
        // Pickers (0.17.0): active companies with the role asked for; orders and requests ask for suppliers (the default).
        s.MapGet("/lookup", async (CmmsDbContext db, string? role, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            // Contacts and contractor rates only for people who work with them (work orders, people, contracts, purchasing).
            var mine = await perms.GetAsync(UserId(http));
            var details = new[] { Permissions.WorkOrdersView, Permissions.PeopleView, Permissions.ContractsView, Permissions.PurchasingView, Permissions.SuppliersManage }.Any(mine.Contains);
            var rows = await WithRole(db.Suppliers.AsNoTracking().Where(x => x.IsActive), role ?? "supplier").OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.Currency, x.PaymentTerms,
                Contacts = x.Contacts.OrderBy(c => c.Sort).Select(c => new { c.Id, c.Name, c.Role }).ToList(),
                // Rates are kept while a company is not a contractor, but not shown (0.29.1).
                Rates = x.CategoryRates.Where(r => x.IsContractor).Select(r => new { CategoryId = r.ContractorCategoryId, r.Rate }).ToList(),
            }).ToListAsync(ct);
            return Results.Ok(details ? rows : rows.Select(x => x with { Contacts = [], Rates = [] }).ToList());
        }).RequireAuthorization();
        s.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.Suppliers.AsNoTracking().Where(x => x.Id == id).Select(x => new
            {
                x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone, x.Address, x.City, x.Country, x.TaxNumber, x.PaymentTerms, x.Currency,
                x.IsSupplier, x.IsContractor, x.IsManufacturer, x.Notes, x.IsActive, x.CreatedAt, x.UpdatedAt,
                Contacts = x.Contacts.OrderBy(c => c.Sort).Select(c => new { c.Id, c.Name, c.Role, c.Phone, c.Email }).ToList(),
                Addresses = x.Addresses.OrderBy(a => a.Sort).Select(a => new { a.Id, a.Label, a.Address, a.City, a.Country }).ToList(),
                Rates = x.CategoryRates.Where(r => x.IsContractor).Select(r => new
                {
                    CategoryId = r.ContractorCategoryId, r.Rate,
                    CategoryName = db.ContractorCategories.Where(c => c.Id == r.ContractorCategoryId).Select(c => c.Name).FirstOrDefault(),
                }).ToList(),
            }).FirstOrDefaultAsync(ct) is { } x ? Results.Ok(x) : Results.NotFound())
            .RequirePermission(Permissions.PurchasingView);
        s.MapPost("/", async (SupplierInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateSupplierAsync(i, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = CreateSupplier(db, i);
            // Two people adding one code at the same moment: the second is told, not shown a server error (0.18.0-13).
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (Unique.Violated(e)) { return Results.Problem(title: Translator.T("Another supplier uses this code."), statusCode: 409); }
            return Results.Created($"/api/suppliers/{x.Id}", new { x.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        s.MapPut("/{id:guid}", async (Guid id, SupplierInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Suppliers.Include(y => y.Contacts).Include(y => y.Addresses).Include(y => y.CategoryRates).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var v = await ValidateSupplierAsync(i, id, db, ct);
            if (!v.IsValid) return v.Problem();
            if (await CompanyContacts.RefusalAsync(db, x, i.Contacts, ct) is { } named) throw new DomainException(named);
            var known = x.Contacts.Select(c => c.Id).Concat(x.Addresses.Select(a => a.Id)).Concat(x.CategoryRates.Select(r => r.Id)).ToHashSet();
            UpdateSupplier(x, i);
            MarkNewChildren(db, x, known);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (Unique.Violated(e)) { return Results.Problem(title: Translator.T("Another supplier uses this code."), statusCode: 409); }
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        s.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Suppliers.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PurchaseOrders.AllSites().AnyAsync(o => o.SupplierId == id, ct) || await db.StockTransactions.AllSites().AnyAsync(t => t.SupplierId == id, ct))
                throw new DomainException("This supplier has purchasing history. Mark it inactive instead.");
            if (await db.WorkOrderContractors.IgnoreQueryFilters([CmmsDbContext.SiteFilter]).AnyAsync(l => l.SupplierId == id, ct)
                || await db.Contracts.AllSites().AnyAsync(c => c.SupplierId == id, ct) || await db.Employees.AnyAsync(e => e.CompanyId == id, ct))
                throw new DomainException("This company is used on work orders, contracts or employees. Mark it inactive instead.");
            var unpreferred = await db.Parts.Where(p => p.PreferredSupplierId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.PreferredSupplierId, (Guid?)null), ct);
            if (unpreferred > 0) FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "Supplier", id, "Deleted", $"No longer the preferred supplier of {FourC.Cmms.Core.Auditing.AuditLog.Count(unpreferred, "part", "parts")}");
            db.SupplierParts.RemoveRange(await db.SupplierParts.Where(r => r.SupplierId == id).ToListAsync(ct)); // its catalogue rows go with it (0.18.0)
            db.Suppliers.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SuppliersManage);

        // ---------------- Purchase orders ----------------
        var g = api.MapGroup("/purchase-orders").WithTags("Purchase orders");
        g.MapGet("/", ([AsParameters] PoFilter f, CmmsDbContext db, CancellationToken ct) =>
            ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 25, ct)).RequirePermission(Permissions.PurchasingView);

        g.MapGet("/export", ([AsParameters] PoFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "purchase-orders", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct))
            .RequirePermission(Permissions.PurchasingView);

        g.MapGet("/{id:guid}", async (Guid id, HttpContext http, CmmsDbContext db, PermissionService perms, AccessService access, CancellationToken ct) =>
        {
            var o = await db.PurchaseOrders.AsNoTracking().Include(x => x.Supplier).Include(x => x.Site).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return Results.NotFound();
            var partIds = o.Lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
            var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.PartNumber, p.Name, p.IsShelfItem, p.UnitOfMeasure, p.IsSerialTracked }, ct);
            var storeIds = o.Lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).ToList();
            var stores = await db.Stores.AsNoTracking().Where(x => storeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
            var woIds = o.Lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).ToList();
            var wos = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Number, ct);
            var orderType = o.OrderTypeId is { } otid ? await db.OrderTypes.AsNoTracking().Where(t => t.Id == otid).Select(t => new { t.Id, t.Code, t.Name }).FirstOrDefaultAsync(ct) : null;
            var requestLineIds = o.Lines.Where(l => l.PurchaseRequestLineId is not null).Select(l => l.PurchaseRequestLineId!.Value).ToList();
            var requests = await db.PurchaseRequestLines.AllSites().AsNoTracking().Where(l => requestLineIds.Contains(l.Id))
                .Select(l => new { l.Id, RequestId = l.PurchaseRequestId, Number = db.PurchaseRequests.AllSites().Where(r => r.Id == l.PurchaseRequestId).Select(r => r.Number).FirstOrDefault() })
                .ToDictionaryAsync(l => l.Id, ct);
            var receipts = await db.StockTransactions.AsNoTracking().Where(t => t.PurchaseOrderId == id && t.Type == StockTransactionType.Receipt).OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Date, t.PurchaseOrderLineId, t.Part!.PartNumber, StoreCode = t.Store!.Code, t.Quantity, t.UnitCost, t.Reference, t.BatchNumber, t.CreatedByName }).ToListAsync(ct);
            var mine = await perms.GetAsync(UserId(http));
            var limit = await db.Users.Where(u => u.Id == UserId(http)).Select(u => u.PurchaseApprovalLimit).FirstOrDefaultAsync(ct);
            var totalBase = PurchaseOrderMath.TotalInBase(o);
            var canApproveAmount = limit is null || totalBase <= limit;
            var hasReceipts = o.Lines.Any(l => l.QuantityReceived > 0);
            // Approval levels (0.16.0): the panel, and who may sign; approving a draft isn't offered when levels would apply.
            var approval = await PurchaseApprovals.PanelAsync(db, perms, access, o, UserId(http), ct);
            var byLevels = approval.Waiting;
            var iCanSign = approval.ICanSign;
            var repairIds = o.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).ToList();
            var repairUnits = await db.Assets.AllSites().AsNoTracking().Where(a => repairIds.Contains(a.Id)).Select(a => new { a.Id, a.SerialNumber, a.AssetNumber }).ToDictionaryAsync(a => a.Id, ct);
            var draftNeedsLevels = o.Status == PurchaseOrderStatus.Draft && (await PurchaseApprovals.LevelsForAsync(db, o, ct)).Count > 0;
            return Results.Ok(new
            {
                o.Id, o.Number, o.Status, o.SiteId, SiteName = o.Site!.Name, o.SupplierId, Supplier = new { o.Supplier!.Id, o.Supplier.Code, o.Supplier.Name, o.Supplier.ContactName, o.Supplier.Email, o.Supplier.Phone, o.Supplier.Address, o.Supplier.City, o.Supplier.Country, o.Supplier.TaxNumber },
                o.OrderDate, o.ExpectedDate, o.RequestedBy, o.Currency, o.ExchangeRate, o.PaymentTerms, o.ShippingMethod, o.DeliverTo, o.SupplierQuoteRef, o.TaxPercent, o.ShippingCost, o.Notes,
                o.SubmittedAt, o.ApprovedAt, o.ApprovedByName, o.CancelledAt, o.CancelReason, o.CreatedAt, o.LegacyRef, o.OrderTypeId, OrderType = orderType,
                Lines = o.Lines.OrderBy(l => l.LineNumber).Select(l => new
                {
                    l.Id, l.LineNumber, l.PartId, PartNumber = l.PartId is { } p ? parts.GetValueOrDefault(p)?.PartNumber : null, IsShelfItem = l.PartId is { } p2 && (parts.GetValueOrDefault(p2)?.IsShelfItem ?? false), PartUnit = l.PartId is { } p3 ? parts.GetValueOrDefault(p3)?.UnitOfMeasure : null,
                    IsSerialTracked = l.PartId is { } p4 && (parts.GetValueOrDefault(p4)?.IsSerialTracked ?? false),
                    l.Description, l.UnitOfMeasure, l.Quantity, l.UnitPrice, l.DiscountPercent, l.NetUnitPrice, l.LineTotal, l.QuantityReceived, Outstanding = Math.Max(l.Quantity - l.QuantityReceived, 0), l.PackSize, l.SupplierPartNumber,
                    l.StoreId, StoreCode = l.StoreId is { } sid ? stores.GetValueOrDefault(sid) : null, l.WorkOrderId, WorkOrderNumber = l.WorkOrderId is { } wid ? wos.GetValueOrDefault(wid) : null, l.AccountCode,
                    l.PurchaseRequestLineId, l.UnitId, UnitSerial = l.UnitId is { } u1 ? repairUnits.GetValueOrDefault(u1)?.SerialNumber : null,
                    UnitAssetNumber = l.UnitId is { } u2 ? repairUnits.GetValueOrDefault(u2)?.AssetNumber : null,
                    Request = l.PurchaseRequestLineId is { } rl && requests.GetValueOrDefault(rl) is { } rq ? new { Id = rq.RequestId, rq.Number } : null,
                }),
                Approval = approval.View,
                Totals = new { Subtotal = PurchaseOrderMath.Subtotal(o), Tax = PurchaseOrderMath.Tax(o), o.ShippingCost, Total = PurchaseOrderMath.Total(o), TotalInBase = totalBase },
                Receipts = receipts,
                IsOverdue = Receivable.Contains(o.Status) && o.ExpectedDate < Today,
                Actions = new
                {
                    Edit = o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate),
                    Submit = o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate),
                    Approve = byLevels ? iCanSign
                        : o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted && !draftNeedsLevels && mine.Contains(Permissions.PurchasingApprove) && canApproveAmount,
                    ApprovalLimitExceeded = !byLevels && !draftNeedsLevels && o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted
                        && mine.Contains(Permissions.PurchasingApprove) && !canApproveAmount,
                    Reject = byLevels ? iCanSign : o.Status == PurchaseOrderStatus.Submitted && mine.Contains(Permissions.PurchasingApprove),
                    RestartApproval = byLevels && mine.Contains(Permissions.PurchasingApprove),
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
            // Linked request lines: the order and the request that follows it are saved together, the request under the version read when the
            // attempt starts (0.16.0-5). Validating and claiming are part of every attempt: a retry after a dropped connection starts again
            // from what is stored, with the claim marked again and the version checked (0.29.1).
            try
            {
                return await Atomic.RunAsync<IResult>(db, async tk =>
                {
                    db.ChangeTracker.Clear();
                    var claims = new List<PurchaseRequest>();
                    var v = await ValidatePoAsync(i, db, tk, null, null, claims);
                    if (!v.IsValid) return v.Problem();
                    RequestLinks.Claim(db, claims);
                    var made = await CreateDraftAsync(db, ctx, i, tk);
                    if (claims.Count > 0) { await PurchaseRequestSync.SyncAsync(db, claims.Select(r => r.Id).ToList(), tk); await db.SaveChangesAsync(tk); }
                    return Results.Created($"/api/purchase-orders/{made.Id}", new { made.Id, made.Number });
                }, ct);
            }
            catch (DbUpdateConcurrencyException e) when (RequestLinks.IsLost(e)) { return RequestLinks.LostResult(); }
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPut("/{id:guid}", async (Guid id, PoInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            // Everything from loading the order to the last save is one attempt: a retry after a dropped connection starts again from what is stored (0.29.1).
            try
            {
                return await Atomic.RunAsync<IResult>(db, async tk =>
                {
                    db.ChangeTracker.Clear();
                    var o = await db.PurchaseOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, tk);
                    if (o is null) return Results.NotFound();
                    if (o.Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be edited. Return it to draft first.");
                    var claims = new List<PurchaseRequest>();
                    var v = await ValidatePoAsync(i, db, tk, o.SupplierId, o.Id, claims);
                    if (!v.IsValid) return v.Problem();
                    if (i.RowVersion is not null) db.Entry(o).Property(x => x.RowVersion).OriginalValue = i.RowVersion.ToVersion();
                    var before = o.Lines.Where(l => l.PurchaseRequestLineId is not null).Select(l => l.PurchaseRequestLineId!.Value).ToList();
                    await ApplyPoAsync(db, o, i, tk);
                    RequestLinks.Claim(db, claims);
                    await db.SaveChangesAsync(tk);
                    if (before.Count > 0) { await PurchaseRequestSync.SyncLinesAsync(db, before, tk); await db.SaveChangesAsync(tk); }
                    if (claims.Count > 0) { await PurchaseRequestSync.SyncAsync(db, claims.Select(r => r.Id).ToList(), tk); await db.SaveChangesAsync(tk); }
                    return Results.Ok(new { o.Id });
                }, ct);
            }
            catch (DbUpdateConcurrencyException e) when (RequestLinks.IsLost(e)) { return RequestLinks.LostResult(); }
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            return await Atomic.RunAsync<IResult>(db, async tk =>
            {
                db.ChangeTracker.Clear(); // a retry starts again from what is stored (0.29.1)
                var o = await db.PurchaseOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, tk);
                if (o is null) return Results.NotFound();
                if (o.Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be deleted. Cancel it instead.");
                var requested = o.Lines.Where(l => l.PurchaseRequestLineId is not null).Select(l => l.PurchaseRequestLineId!.Value).ToList();
                db.PurchaseOrders.Remove(o);
                await db.SaveChangesAsync(tk);
                if (requested.Count > 0) { await PurchaseRequestSync.SyncLinesAsync(db, requested, tk); await db.SaveChangesAsync(tk); } // their requests: back to order
                return Results.NoContent();
            }, ct);
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPost("/{id:guid}/submit", async (Guid id, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, PermissionService perms, AccessService access, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.Draft);
            await RequireReadyAsync(db, o, ct);
            o.Status = PurchaseOrderStatus.Submitted; o.SubmittedAt = DateTimeOffset.UtcNow; o.SubmittedById = ctx.UserId;
            // Approval levels (0.16.0): the levels this order needs are copied onto it and their first signers told; else today's rule.
            if (!await PurchaseApprovals.StartAsync(db, ctx, perms, access, mail, o, ct)) await mail.PoSubmittedAsync(db, ctx, o, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, PermissionService perms, AccessService access,
            CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            // Signed by levels: the locked signing step decides, whatever this read saw (0.16.1).
            if (await PurchaseApprovals.ByLevelsAsync(db, o, ct))
            {
                await PurchaseApprovals.SignAsync(db, ctx, perms, access, mail, id, approve: true, null, ct);
                return Results.Ok(new { o.Id, (await db.PurchaseOrders.AsNoTracking().FirstAsync(x => x.Id == id, ct)).Status });
            }
            Require(o, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted);
            await RequireReadyAsync(db, o, ct);
            if (o.Status == PurchaseOrderStatus.Draft && (await PurchaseApprovals.LevelsForAsync(db, o, ct)).Count > 0)
                throw new DomainException("Submit the order for approval first.");
            var limit = await db.Users.Where(u => u.Id == UserId(http)).Select(u => u.PurchaseApprovalLimit).FirstOrDefaultAsync(ct);
            var total = PurchaseOrderMath.TotalInBase(o);
            if (limit is not null && total > limit) throw new DomainException($"This order ({total:N2}) is above your approval limit of {limit:N2}.");
            o.Status = PurchaseOrderStatus.Approved; o.SubmittedAt ??= DateTimeOffset.UtcNow;
            o.ApprovedAt = DateTimeOffset.UtcNow; o.ApprovedById = ctx.UserId; o.ApprovedByName = ctx.UserName;
            await mail.PoDecidedAsync(db, ctx, o, approved: true, null, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        g.MapPost("/{id:guid}/reject", async (Guid id, ReasonInput i, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, PermissionService perms, AccessService access,
            CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            if (await PurchaseApprovals.ByLevelsAsync(db, o, ct))
            {
                await PurchaseApprovals.SignAsync(db, ctx, perms, access, mail, id, approve: false, i.Reason, ct);
                return Results.Ok(new { o.Id, Status = PurchaseOrderStatus.Draft });
            }
            Require(o, PurchaseOrderStatus.Submitted);
            if (i.Reason.Clean() is not { } reason) return new Validator().Required("reason", i.Reason, "Reason").Problem();
            o.Status = PurchaseOrderStatus.Draft; o.SubmittedAt = null;
            o.Notes = $"{o.Notes}{(o.Notes is null ? "" : "\n")}Returned by {ctx.UserName} on {Today:yyyy-MM-dd}: {reason}";
            await mail.PoDecidedAsync(db, ctx, o, approved: false, reason, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        // Approval levels (0.16.0): a new round from today's levels, for when an approver left or the levels changed.
        g.MapPost("/{id:guid}/restart-approval", async (Guid id, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, PermissionService perms, AccessService access,
            CancellationToken ct) =>
        {
            await PurchaseApprovals.RestartAsync(db, ctx, perms, access, mail, id, ct);
            return Results.Ok(new { Id = id });
        }).RequirePermission(Permissions.PurchasingApprove);

        g.MapPost("/{id:guid}/cancel", async (Guid id, ReasonInput i, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(UserId(http));
            return await Atomic.RunAsync<IResult>(db, async tk =>
            {
                db.ChangeTracker.Clear(); // a retry starts again from what is stored (0.29.1)
                var o = await LoadAsync(db, id, tk);
                Require(o, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved);
                if (!mine.Contains(Permissions.PurchasingApprove) && !(o.Status == PurchaseOrderStatus.Draft && mine.Contains(Permissions.PurchasingCreate))) return Results.Forbid();
                if (o.Lines.Any(l => l.QuantityReceived > 0)) throw new DomainException("Goods have been received on this order. Close it instead.");
                if (i.Reason.Clean() is not { } reason) return new Validator().Required("reason", i.Reason, "Reason").Problem();
                o.Status = PurchaseOrderStatus.Cancelled; o.CancelledAt = DateTimeOffset.UtcNow; o.CancelReason = reason;
                var requested = o.Lines.Where(l => l.PurchaseRequestLineId is not null).Select(l => l.PurchaseRequestLineId!.Value).ToList();
                await db.SaveChangesAsync(tk);
                if (requested.Count > 0) { await PurchaseRequestSync.SyncLinesAsync(db, requested, tk); await db.SaveChangesAsync(tk); } // their requests: back to order
                return Results.Ok(new { o.Id, o.Status });
            }, ct);
        }).RequirePermission(Permissions.PurchasingView);

        g.MapPost("/{id:guid}/close", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            Require(o, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received);
            o.Status = PurchaseOrderStatus.Closed;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { o.Id, o.Status });
        }).RequirePermission(Permissions.PurchasingApprove);

        // "Send to supplier": e-mails an approved order, with every line, to the supplier's address.
        g.MapPost("/{id:guid}/send", async (Guid id, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var o = await LoadAsync(db, id, ct);
            if (o.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed))
                throw new DomainException("Only approved orders can be sent.");
            if (await mail.PoToSupplierAsync(db, ctx, o, ct) is { } error) throw new DomainException(error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { queued = true, to = o.Supplier?.Email });
        }).RequirePermission(Permissions.PurchasingCreate);

        // Goods receipt: stock lines post to the ledger at the net price in base currency; work-order lines become direct costs.
        g.MapPost("/{id:guid}/receive", async (Guid id, ReceiveInput i, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var date = i.Date ?? Today;
            if (date > Today) throw new DomainException("The receipt date cannot be in the future.");
            var received = await db.InTransactionAsync(tk => ReceiveCoreAsync(db, ctx, mail, id, i, date, tk), ct);
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
                return (Part: part, Store: store, SupplierId: supplier, l.Quantity, l.UnitPrice);
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
                    // The proposal's quantities are pieces: the catalogue turns them into the supplier's whole packs at its price (0.18.0).
                    var currency = sup.Currency.Clean()?.ToUpperInvariant() ?? await CatalogueEndpoints.BaseCurrencyAsync(db, tk);
                    var lines = new List<(LineInput Line, int? Lead)>();
                    foreach (var r in grp)
                    {
                        // The proposal page has no price box: its price per piece only stands in when the catalogue has no price (0.18.0).
                        var f = await CatalogueEndpoints.FillAsync(db, r.Part, sup.Id, currency, r.Quantity, null, tk, fallbackPerPiece: r.UnitPrice);
                        lines.Add((new LineInput(null, r.Part.Id, r.Part.Name, f.UnitOfMeasure, f.Quantity, f.UnitPrice, f.DiscountPercent, r.Store.Id, null, null,
                            PackSize: f.PackSize, SupplierPartNumber: f.SupplierPartNumber), f.LeadTimeDays));
                    }
                    var input = new PoInput(grp.Key.SiteId, sup.Id, Today, CatalogueEndpoints.ExpectedFrom(Today, lines.Select(l => l.Lead)), ctx.UserName, currency, null,
                        sup.PaymentTerms, null, null, null, null, 0, "Created from the purchase proposal.", lines.Select(l => l.Line).ToList(),
                        await DefaultTypeAsync(db, "PARTS", tk));
                    var o = await CreateDraftAsync(db, ctx, input, tk);
                    list.Add(new { o.Id, o.Number, Supplier = sup.Name, Lines = o.Lines.Count });
                }
                return list;
            }, ct);
            return Results.Ok(created);
        }).RequirePermission(Permissions.PurchasingCreate);
    }

    /// <summary>The organization's active order type with this code (0.16.0), for orders CMX makes itself.</summary>
    internal static async Task<Guid?> DefaultTypeAsync(CmmsDbContext db, string code, CancellationToken ct) =>
        await db.OrderTypes.Where(t => t.Code == code && t.IsActive).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    internal static async Task<PurchaseOrder> CreateDraftAsync(CmmsDbContext db, ITenantContext ctx, PoInput i, CancellationToken ct)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var (sequence, prefix) = Numbering.For(settings, await db.Sites.AllSites().AsNoTracking().FirstOrDefaultAsync(s => s.Id == i.SiteId, ct), NumberKind.PurchaseOrder);
        var number = await Numbering.NextAsync(db, sequence, prefix, 5,
            n => db.PurchaseOrders.AllSites().AnyAsync(o => o.Number == n, ct), p => db.PurchaseOrders.AllSites().Where(o => o.Number.StartsWith(p)).Select(o => o.Number).ToListAsync(ct), ct);
        var o = new PurchaseOrder { Number = number, SiteId = i.SiteId!.Value, SupplierId = i.SupplierId!.Value, Currency = settings.Currency, TaxPercent = settings.DefaultTaxPercent, RequestedBy = ctx.UserName };
        await ApplyPoAsync(db, o, i with { TaxPercent = i.TaxPercent ?? settings.DefaultTaxPercent, Currency = i.Currency.Clean() ?? settings.Currency, RequestedBy = i.RequestedBy.Clean() ?? ctx.UserName }, ct);
        db.PurchaseOrders.Add(o);
        await db.SaveChangesAsync(ct);
        return o;
    }

    private static async Task ApplyPoAsync(CmmsDbContext db, PurchaseOrder o, PoInput i, CancellationToken ct)
    {
        o.SiteId = i.SiteId!.Value; o.SupplierId = i.SupplierId!.Value; o.OrderDate = i.OrderDate ?? Today; o.ExpectedDate = i.ExpectedDate; o.RequestedBy = i.RequestedBy.Clean();
        o.Currency = i.Currency.Clean()?.ToUpperInvariant() ?? o.Currency; o.ExchangeRate = i.ExchangeRate is > 0 ? i.ExchangeRate.Value : 1;
        o.PaymentTerms = i.PaymentTerms.Clean(); o.ShippingMethod = i.ShippingMethod.Clean(); o.DeliverTo = i.DeliverTo.Clean(); o.SupplierQuoteRef = i.SupplierQuoteRef.Clean();
        o.TaxPercent = i.TaxPercent ?? o.TaxPercent; o.ShippingCost = i.ShippingCost; o.Notes = i.Notes.Clean(); o.OrderTypeId = i.OrderTypeId;
        var partIds = i.Lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var unitIds = i.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).ToList();
        var units = await db.Assets.AllSites().AsNoTracking().Where(a => unitIds.Contains(a.Id))
            .Select(a => new { a.Id, a.SerialNumber, PartName = db.Parts.Where(p => p.Id == a.PartId).Select(p => p.Name).FirstOrDefault() }).ToDictionaryAsync(a => a.Id, ct);
        foreach (var gone in o.Lines.Where(l => i.Lines.All(x => x.Id != l.Id)).ToList()) o.Lines.Remove(gone);
        var n = 1;
        foreach (var x in i.Lines)
        {
            var line = x.Id is { } lid ? o.Lines.FirstOrDefault(l => l.Id == lid) : null;
            var part = x.PartId is { } pid ? parts[pid] : null;
            if (line is null)
            {
                line = new PurchaseOrderLine { Description = "" };
                o.Lines.Add(line);
                // A new line on a saved order: its id is preset, so EF would take it for an existing row (as 0.12.1 found for PM task parts).
                if (db.Entry(o).State is not (EntityState.Added or EntityState.Detached)) db.Entry(line).State = EntityState.Added;
            }
            line.LineNumber = n++; line.PartId = x.PartId; line.Description = x.Description.Clean() ?? part?.Name ?? "";
            line.UnitOfMeasure = x.UnitOfMeasure.Clean() ?? part?.UnitOfMeasure; line.Quantity = x.Quantity; line.UnitPrice = x.UnitPrice; line.DiscountPercent = x.DiscountPercent;
            line.StoreId = x.StoreId; line.WorkOrderId = x.WorkOrderId; line.AccountCode = x.AccountCode.Clean();
            if (x.PurchaseRequestLineId is not null) line.PurchaseRequestLineId = x.PurchaseRequestLineId; // edits keep the line's request (0.16.0)
            if (x.PackSize is { } pack) line.PackSize = pack;
            if (x.SupplierPartNumber is not null || x.PackSize is not null) line.SupplierPartNumber = x.SupplierPartNumber.Clean();
            if (x.UnitId is { } unitId)
            {
                // A repair line is a service line for one unit: no part, quantity 1, "Repair of ‹part› ‹serial›" unless described.
                var unit = units[unitId];
                line.UnitId = unitId; line.PartId = null; line.Quantity = 1; line.PackSize = 1;
                line.Description = x.Description.Clean() ?? $"Repair of {unit.PartName} {unit.SerialNumber}".Trim();
            }
        }
    }

    private static async Task<Validator> ValidatePoAsync(PoInput i, CmmsDbContext db, CancellationToken ct, Guid? currentSupplierId, Guid? orderId = null, List<PurchaseRequest>? claims = null)
    {
        var v = new Validator().MaxLength("supplierQuoteRef", i.SupplierQuoteRef, "Quote ref.", 100).MaxLength("notes", i.Notes, "Notes", 4000)
            .When(i.ShippingCost < 0, "shippingCost", "Shipping cannot be negative.").When(i.TaxPercent is < 0 or > 100, "taxPercent", "Tax must be between 0 and 100%.")
            .When(i.ExchangeRate is <= 0, "exchangeRate", "Exchange rate must be positive.").When(i.ExpectedDate < i.OrderDate, "expectedDate", "Expected date is before the order date.");
        if (i.SiteId is null || !await db.Sites.AnyAsync(x => x.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.SupplierId is null || !await db.Suppliers.AnyAsync(x => x.Id == i.SupplierId && x.IsActive, ct)) v.Add("supplierId", "Choose an active supplier.");
        // A newly chosen company must sell to us (0.17.0); an order keeps a supplier that later lost the role.
        else if (i.SupplierId != currentSupplierId && !await db.Suppliers.AnyAsync(x => x.Id == i.SupplierId && x.IsSupplier, ct)) v.Add("supplierId", "Choose a company with the Supplier role.");
        if (i.OrderTypeId is { } type && !await db.OrderTypes.AnyAsync(t => t.Id == type && t.IsActive, ct)) v.Add("orderTypeId", "Choose an active order type.");
        var lines = i.Lines ?? [];
        for (var k = 0; k < lines.Count; k++)
        {
            var l = lines[k];
            if (l.PartId is null && l.UnitId is null && string.IsNullOrWhiteSpace(l.Description)) v.Add($"lines[{k}].description", "Enter a description or choose a part.");
            if (l.Quantity <= 0) v.Add($"lines[{k}].quantity", "Quantity must be positive.");
            if (l.UnitPrice < 0) v.Add($"lines[{k}].unitPrice", "Price cannot be negative.");
            if (l.DiscountPercent is < 0 or > 100) v.Add($"lines[{k}].discountPercent", "Discount must be between 0 and 100%.");
            if (l.PartId is not null && l.StoreId is null) v.Add($"lines[{k}].storeId", "Choose the receiving store.");
            if (l.PackSize is <= 0) v.Add($"lines[{k}].packSize", "Pieces per pack must be more than 0.");
        }
        var partIds = lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).Distinct().ToList();
        if (partIds.Count > 0 && await db.Parts.CountAsync(p => partIds.Contains(p.Id), ct) != partIds.Count) v.Add("lines", "Unknown part.");
        // Repair lines (0.24.0): a unit under repair, on one open repair line at a time.
        var unitIds = lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).ToList();
        if (unitIds.Count != unitIds.Distinct().Count()) v.Add("lines", "List each unit once.");
        foreach (var unitId in unitIds.Distinct())
        {
            if (!await db.Assets.AllSites().AnyAsync(a => a.Id == unitId && a.PartId != null && a.Status == AssetStatus.UnderRepair, ct))
            { v.Add("lines", "Only units under repair can go on a repair line."); continue; }
            var other = await db.PurchaseOrderLines.Where(l => l.UnitId == unitId && l.PurchaseOrderId != orderId && l.QuantityReceived < l.Quantity)
                .Join(db.PurchaseOrders.AllSites().Where(o => OpenRepair.Contains(o.Status)), l => l.PurchaseOrderId, o => o.Id, (l, o) => o.Number).FirstOrDefaultAsync(ct);
            if (other is not null) v.Add("lines", $"This unit is already on an open repair line ({other}).");
        }
        var storeIds = lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).Distinct().ToList();
        if (storeIds.Count > 0 && await db.Stores.CountAsync(s => storeIds.Contains(s.Id), ct) != storeIds.Count) v.Add("lines", "Unknown store.");
        var woIds = lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).Distinct().ToList();
        if (woIds.Count > 0 && await db.WorkOrders.CountAsync(w => woIds.Contains(w.Id) && w.Status != WorkOrderStatus.Closed && w.Status != WorkOrderStatus.Cancelled, ct) != woIds.Count)
            v.Add("lines", "A work order on this order is unknown or closed.");
        // Links to purchase-request lines (0.16.0-4): visible, approved, and not already on another live order.
        await RequestLinks.ValidateAsync(db, v, lines, orderId, claims ?? [], ct);
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

    /// <summary>
    /// The order supplier's catalogue row remembers what this receipt paid (0.18.0): the gross price and discount per pack, in the order's currency,
    /// converted through pieces to the row's own pack. The catalogue price itself never changes.
    /// </summary>
    private static async Task RecordLastPaidAsync(CmmsDbContext db, PurchaseOrder o, PurchaseOrderLine line, Guid partId, DateOnly date, CancellationToken ct)
    {
        var row = await db.SupplierParts.FirstOrDefaultAsync(r => r.PartId == partId && r.SupplierId == o.SupplierId, ct);
        if (row is null) return;
        row.LastPaidPrice = PackMath.PriceForPack(line.UnitPrice, line.PackSize, row.PackSize);
        row.LastPaidDiscount = line.DiscountPercent; row.LastPaidCurrency = o.Currency.ToUpperInvariant(); row.LastPaidOn = date;
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator SupplierRules(SupplierInput i)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("email", i.Email, "Email", 200)
            .When(i.Email.Clean() is { } e && !e.Contains('@'), "email", "Enter a valid email address.").When(i.Currency.Clean() is { Length: not 3 }, "currency", "Use a 3-letter currency code.")
            .When(i.IsSupplier == false && !i.IsContractor && i.IsManufacturer != true, "roles", "Choose at least one role.");
        var contacts = i.Contacts ?? [];
        for (var k = 0; k < contacts.Count; k++)
        {
            if (string.IsNullOrWhiteSpace(contacts[k].Name)) v.Add($"contacts[{k}].name", $"Contact {k + 1}: enter a name.");
            if (contacts[k].Email.Clean() is { } ce && !ce.Contains('@')) v.Add($"contacts[{k}].email", $"Contact {k + 1}: enter a valid email address.");
        }
        var addresses = i.Addresses ?? [];
        for (var k = 0; k < addresses.Count; k++)
            if (string.IsNullOrWhiteSpace(addresses[k].Address)) v.Add($"addresses[{k}].address", $"Address {k + 1}: enter the address.");
        var rates = i.Rates ?? [];
        if (rates.Any(r => r.Rate < 0)) v.Add("rates", "Rates cannot be negative.");
        if (rates.Select(r => r.CategoryId).Distinct().Count() != rates.Count) v.Add("rates", "Each category can have one rate.");
        return v;
    }

    internal static Supplier CreateSupplier(CmmsDbContext db, SupplierInput i)
    {
        var x = new Supplier { Code = i.Code.Trim(), Name = i.Name.Trim() };
        ApplySupplier(x, i);
        db.Suppliers.Add(x);
        return x;
    }

    internal static void UpdateSupplier(Supplier x, SupplierInput i)
    {
        x.Code = i.Code.Trim(); x.Name = i.Name.Trim();
        ApplySupplier(x, i);
    }

    private static async Task<Validator> ValidateSupplierAsync(SupplierInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = SupplierRules(i);
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.Suppliers.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another supplier uses this code.");
        var cats = (i.Rates ?? []).Select(r => r.CategoryId).Distinct().ToList();
        if (cats.Count > 0 && await db.ContractorCategories.CountAsync(c => cats.Contains(c.Id), ct) != cats.Count) v.Add("rates", "Unknown contractor category.");
        return v;
    }

    internal static void ApplySupplier(Supplier x, SupplierInput i)
    {
        x.ContactName = i.ContactName.Clean(); x.Email = i.Email.Clean(); x.Phone = i.Phone.Clean(); x.Address = i.Address.Clean(); x.City = i.City.Clean(); x.Country = i.Country.Clean();
        x.TaxNumber = i.TaxNumber.Clean(); x.PaymentTerms = i.PaymentTerms.Clean(); x.Currency = i.Currency.Clean()?.ToUpperInvariant();
        var wasContractor = x.IsContractor;
        x.IsContractor = i.IsContractor;
        x.Notes = i.Notes.Clean(); x.IsActive = i.IsActive;
        if (i.IsSupplier is { } supplier) x.IsSupplier = supplier;
        if (i.IsManufacturer is { } maker) x.IsManufacturer = maker;
        if (i.Contacts is { } contacts)
        {
            x.Contacts.RemoveAll(c => !contacts.Any(n => n.Id == c.Id));
            for (var k = 0; k < contacts.Count; k++)
            {
                var n = contacts[k];
                var c = x.Contacts.FirstOrDefault(y => n.Id is not null && y.Id == n.Id);
                if (c is null) { c = new SupplierContact { SupplierId = x.Id, Name = "" }; x.Contacts.Add(c); }
                c.Name = n.Name.Trim(); c.Role = n.Role.Clean(); c.Phone = n.Phone.Clean(); c.Email = n.Email.Clean(); c.Sort = k;
            }
        }
        if (i.Addresses is { } addresses)
        {
            x.Addresses.RemoveAll(a => !addresses.Any(n => n.Id == a.Id));
            for (var k = 0; k < addresses.Count; k++)
            {
                var n = addresses[k];
                var a = x.Addresses.FirstOrDefault(y => n.Id is not null && y.Id == n.Id);
                if (a is null) { a = new SupplierAddress { SupplierId = x.Id, Address = "" }; x.Addresses.Add(a); }
                a.Label = n.Label.Clean(); a.Address = n.Address.Trim(); a.City = n.City.Clean(); a.Country = n.Country.Clean(); a.Sort = k;
            }
        }
        // Rates are kept while the company is not a contractor (hidden), and come back when it is ticked again; a form that could not see them
        // sends none, which must not wipe what is kept (0.17.0-2, 0.29.1). Only a company that was and is a contractor can clear its rates.
        if (i.Rates is { } rates && i.IsContractor && (wasContractor || rates.Count > 0))
        {
            x.CategoryRates.RemoveAll(r => !rates.Any(n => n.CategoryId == r.ContractorCategoryId));
            foreach (var n in rates)
            {
                var r = x.CategoryRates.FirstOrDefault(y => y.ContractorCategoryId == n.CategoryId);
                if (r is null) { r = new SupplierCategoryRate { SupplierId = x.Id, ContractorCategoryId = n.CategoryId }; x.CategoryRates.Add(r); }
                r.Rate = n.Rate;
            }
        }
    }

    /// <summary>Children added to a tracked company carry their own ids, so EF must be told they are new (the 0.12.1 lesson).</summary>
    private static void MarkNewChildren(CmmsDbContext db, Supplier x, HashSet<Guid> known)
    {
        foreach (var c in x.Contacts.Where(c => !known.Contains(c.Id))) db.Entry(c).State = EntityState.Added;
        foreach (var a in x.Addresses.Where(a => !known.Contains(a.Id))) db.Entry(a).State = EntityState.Added;
        foreach (var r in x.CategoryRates.Where(r => !known.Contains(r.Id))) db.Entry(r).State = EntityState.Added;
    }

    /// <summary>Companies with a role: "supplier", "contractor" or "manufacturer"; anything else is every company.</summary>
    internal static IQueryable<Supplier> WithRole(IQueryable<Supplier> q, string? role) => role?.ToLowerInvariant() switch
    {
        "supplier" => q.Where(x => x.IsSupplier),
        "contractor" => q.Where(x => x.IsContractor),
        "manufacturer" => q.Where(x => x.IsManufacturer),
        _ => q,
    };
}
