using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Purchase requests (0.16.0): anyone allowed asks for parts or services; one person approves or rejects ("do we need this?");
/// buyers turn approved lines into draft orders. The requester always sees their own; buyers and approvers see their sites'.
/// </summary>
public static class PurchaseRequestEndpoints
{
    public sealed record PrLineInput(Guid? Id, Guid? PartId, string? Description, string? UnitOfMeasure, decimal Quantity, DateOnly? NeededBy, Guid? WorkOrderId,
        Guid? AssetId, Guid? StoreId, decimal? EstimatedUnitPrice, string? Reason);
    public sealed record PrInput(Guid SiteId, DateOnly? NeededBy, string? Notes, List<Guid>? RecipientIds, List<PrLineInput>? Lines, string? RowVersion = null);
    public sealed record CommentInput(string? Body);
    public sealed record OrderRequestLine(Guid RequestLineId, Guid? SupplierId, decimal? Quantity, decimal? UnitPrice);
    public sealed record OrderFromRequestsInput(List<OrderRequestLine>? Lines);

    public sealed class PrFilter
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public Guid? SiteId { get; set; }
        public bool? Mine { get; set; }
        public bool? WaitingForMe { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record PrListItem(Guid Id, string Number, PurchaseRequestStatus Status, string SiteName, string? RequestedByName, DateOnly? NeededBy, int Lines,
        decimal? EstimatedTotal, DateTimeOffset? SubmittedAt, bool IsLate);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static readonly PurchaseRequestStatus[] Done = [PurchaseRequestStatus.Ordered, PurchaseRequestStatus.Closed, PurchaseRequestStatus.Cancelled];

    /// <summary>Buyers and approvers see every request at their sites; anyone else only their own.</summary>
    internal static bool SeesAll(IReadOnlySet<string> mine) =>
        mine.Contains(Permissions.PurchasingView) || mine.Contains(Permissions.PurchasingCreate) || mine.Contains(Permissions.PurchasingApproveRequests);

    internal static bool MayUse(IReadOnlySet<string> mine) => SeesAll(mine) || mine.Contains(Permissions.PurchasingRequest);

    internal static IQueryable<PurchaseRequest> Visible(CmmsDbContext db, Guid me, IReadOnlySet<string> mine) =>
        SeesAll(mine) ? db.PurchaseRequests : db.PurchaseRequests.Where(r => r.RequestedById == me);

    /// <summary>Who may approve or reject: "Approve purchase requests" at the request's site (the site filter), never the requester.</summary>
    internal static bool CanDecide(PurchaseRequest r, Guid me, IReadOnlySet<string> mine) =>
        r.Status == PurchaseRequestStatus.Submitted && mine.Contains(Permissions.PurchasingApproveRequests) && r.RequestedById != me;

    private static bool Editable(PurchaseRequestStatus s) => s is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Rejected;

    internal static async Task<PagedResult<PrListItem>> ListAsync(CmmsDbContext db, Guid me, IReadOnlySet<string> mine, PrFilter f, int page, int pageSize,
        CancellationToken ct, int maxPageSize = 500)
    {
        var q = Visible(db, me, mine).AsNoTracking();
        if (f.Search.Clean() is { } t) q = q.Where(r => r.Number.Contains(t) || r.RequestedByName!.Contains(t) || r.Notes!.Contains(t) || r.Lines.Any(l => l.Description.Contains(t)));
        var statuses = (f.Status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Enum.TryParse<PurchaseRequestStatus>(x, true, out var e) ? e : (PurchaseRequestStatus?)null).OfType<PurchaseRequestStatus>().ToList();
        if (statuses.Count > 0) q = q.Where(r => statuses.Contains(r.Status));
        if (f.SiteId is not null) q = q.Where(r => r.SiteId == f.SiteId);
        if (f.Mine == true) q = q.Where(r => r.RequestedById == me);
        if (f.WaitingForMe == true)
            q = mine.Contains(Permissions.PurchasingApproveRequests) ? q.Where(r => r.Status == PurchaseRequestStatus.Submitted && r.RequestedById != me) : q.Where(r => false);
        var map = new Dictionary<string, System.Linq.Expressions.Expression<Func<PurchaseRequest, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = r => r.Number, ["neededBy"] = r => r.NeededBy, ["status"] = r => r.Status, ["requestedBy"] = r => r.RequestedByName, ["submittedAt"] = r => r.SubmittedAt,
            ["createdAt"] = r => r.CreatedAt,
        };
        var paged = await q.SortBy(f.Sort ?? "createdAt", f.Dir ?? (f.Sort is null ? "desc" : f.Dir), map, "createdAt")
            .Select(r => new
            {
                r.Id, r.Number, r.Status, SiteName = r.Site!.Name, r.RequestedByName, r.NeededBy, Lines = r.Lines.Count, r.SubmittedAt,
                Estimate = r.Lines.Where(l => l.EstimatedUnitPrice != null).Sum(l => (decimal?)(l.EstimatedUnitPrice * l.Quantity)),
                FirstNeeded = r.Lines.Min(l => l.NeededBy),
            })
            .ToPagedAsync(page, pageSize, ct, maxPageSize);
        return new PagedResult<PrListItem>(paged.Items.Select(r =>
        {
            var needed = r.NeededBy ?? r.FirstNeeded;
            return new PrListItem(r.Id, r.Number, r.Status, r.SiteName, r.RequestedByName, needed, r.Lines, r.Estimate is { } e ? Math.Round(e, 2) : null, r.SubmittedAt,
                needed < Today && !Done.Contains(r.Status));
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    public static void MapPurchaseRequestEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/purchase-requests").WithTags("Purchase requests").RequireAuthorization();

        g.MapGet("/", async ([AsParameters] PrFilter f, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(UserId(http));
            if (!MayUse(mine)) return Results.Forbid();
            return Results.Ok(await ListAsync(db, UserId(http), mine, f, f.Page ?? 1, f.PageSize ?? 25, ct));
        });

        g.MapGet("/export", async ([AsParameters] PrFilter f, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(UserId(http));
            if (!MayUse(mine)) return Results.Forbid();
            return await ListExport.FileAsync(http, db, "purchase-requests", (page, size) => ListAsync(db, UserId(http), mine, f, page, size, ct, ListExport.MaxRows), ct);
        });

        g.MapGet("/{id:guid}", async (Guid id, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var me = UserId(http);
            var mine = await perms.GetAsync(me);
            if (!MayUse(mine)) return Results.Forbid();
            var r = await Visible(db, me, mine).AsNoTracking().Include(x => x.Site).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            return Results.Ok(await DetailAsync(db, r, me, mine, ct));
        });

        g.MapPost("/", async (PrInput i, HttpContext http, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var v = await ValidateAsync(db, i, ct);
            if (!v.IsValid) return v.Problem();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var (sequence, prefix) = Numbering.For(settings, await db.Sites.AllSites().AsNoTracking().FirstOrDefaultAsync(s => s.Id == i.SiteId, ct), NumberKind.PurchaseRequest);
            var number = await Numbering.NextAsync(db, sequence, prefix, 5,
                n => db.PurchaseRequests.AllSites().AnyAsync(r => r.Number == n, ct),
                p => db.PurchaseRequests.AllSites().Where(r => r.Number.StartsWith(p)).Select(r => r.Number).ToListAsync(ct), ct);
            var r = new PurchaseRequest { Number = number, SiteId = i.SiteId, RequestedById = UserId(http), RequestedByName = ctx.UserName };
            await ApplyAsync(db, r, i, ct);
            db.PurchaseRequests.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/purchase-requests/{r.Id}", new { r.Id, r.Number });
        }).RequirePermission(Permissions.PurchasingRequest);

        g.MapPut("/{id:guid}", async (Guid id, PrInput i, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var me = UserId(http);
            var r = await Visible(db, me, await perms.GetAsync(me)).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (!Editable(r.Status)) throw new DomainException("This request can't be changed any more.");
            if (r.RequestedById != me) throw new DomainException("Only the person who raised this request can change it.");
            var v = await ValidateAsync(db, i, ct);
            if (!v.IsValid) return v.Problem();
            if (i.RowVersion is not null) db.Entry(r).Property(x => x.RowVersion).OriginalValue = i.RowVersion.ToVersion();
            await ApplyAsync(db, r, i, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        }).RequirePermission(Permissions.PurchasingRequest);

        g.MapDelete("/{id:guid}", async (Guid id, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var me = UserId(http);
            var r = await Visible(db, me, await perms.GetAsync(me)).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != PurchaseRequestStatus.Draft) throw new DomainException("Only draft requests can be deleted. Cancel it instead.");
            if (r.RequestedById != me) throw new DomainException("Only the person who raised this request can delete it.");
            db.PurchaseRequests.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PurchasingRequest);

        g.MapPost("/{id:guid}/submit", async (Guid id, HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms, EmailOutbox mail, CancellationToken ct) =>
        {
            var me = UserId(http);
            var r = await Visible(db, me, await perms.GetAsync(me)).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (!Editable(r.Status)) throw new DomainException("This request has already been submitted.");
            if (r.RequestedById != me) throw new DomainException("Only the person who raised this request can submit it.");
            RequireReady(r);
            r.Status = PurchaseRequestStatus.Submitted; r.SubmittedAt = DateTimeOffset.UtcNow;
            r.DecidedAt = null; r.DecidedById = null; r.DecidedByName = null;
            await mail.PrSubmittedAsync(db, ctx, r, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.Status });
        }).RequirePermission(Permissions.PurchasingRequest);

        g.MapPost("/{id:guid}/approve", (Guid id, HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms, EmailOutbox mail, CancellationToken ct) =>
            DecideAsync(id, true, null, http, db, ctx, perms, mail, ct)).RequirePermission(Permissions.PurchasingApproveRequests);

        g.MapPost("/{id:guid}/reject", (Guid id, PurchasingEndpoints.ReasonInput i, HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms,
            EmailOutbox mail, CancellationToken ct) => DecideAsync(id, false, i.Reason, http, db, ctx, perms, mail, ct)).RequirePermission(Permissions.PurchasingApproveRequests);

        g.MapPost("/{id:guid}/cancel", async (Guid id, PurchasingEndpoints.ReasonInput i, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
        {
            var me = UserId(http);
            var mine = await perms.GetAsync(me);
            var r = await Visible(db, me, mine).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (!(r.RequestedById == me || mine.Contains(Permissions.PurchasingApproveRequests))) return Results.Forbid();
            if (r.Status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.Submitted or PurchaseRequestStatus.Approved or PurchaseRequestStatus.Rejected))
                throw new DomainException("This request can't be cancelled any more.");
            if (await OrderedLineIdsAsync(db, r.Lines.Select(l => l.Id).ToList(), ct) is { Count: > 0 })
                throw new DomainException("Some lines are already on orders. Cancel those orders first, or close the request.");
            if (i.Reason.Clean() is not { } reason) throw new DomainException("Give a reason.");
            r.Status = PurchaseRequestStatus.Cancelled; r.CancelledAt = DateTimeOffset.UtcNow; r.CancelReason = reason;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.Status });
        });

        // Approved lines become draft orders grouped by supplier and site, as the Re-order tab does.
        g.MapPost("/order", async (OrderFromRequestsInput i, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var wanted = (i.Lines ?? []).DistinctBy(l => l.RequestLineId).ToList();
            if (wanted.Count == 0) throw new DomainException("Select at least one line.");
            var ids = wanted.Select(l => l.RequestLineId).ToList();
            var lines = await db.PurchaseRequestLines.Include(l => l.Part).Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
            var requestIds = lines.Values.Select(l => l.PurchaseRequestId).Distinct().ToList();
            var requests = await db.PurchaseRequests.Include(r => r.Lines).Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            var onOrder = await db.PurchaseOrderLines.AllSites().Where(l => l.PurchaseRequestLineId != null && ids.Contains(l.PurchaseRequestLineId.Value))
                .Select(l => new { RequestLineId = l.PurchaseRequestLineId!.Value, Order = db.PurchaseOrders.AllSites().Where(o => o.Id == l.PurchaseOrderId).Select(o => new { o.Number, o.Status }).First() })
                .ToListAsync(ct);
            var live = onOrder.Where(x => x.Order.Status != PurchaseOrderStatus.Cancelled).ToDictionary(x => x.RequestLineId, x => x.Order.Number);
            var resolved = wanted.Select(w =>
            {
                if (!lines.TryGetValue(w.RequestLineId, out var line) || !requests.TryGetValue(line.PurchaseRequestId, out var r)) throw new DomainException("Unknown request line.");
                if (r.Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartlyOrdered)) throw new DomainException($"{r.Number} isn't approved.");
                if (live.TryGetValue(line.Id, out var po)) throw new DomainException($"{r.Number} line {line.LineNumber} is already on {po}.");
                var supplier = w.SupplierId ?? line.Part?.PreferredSupplierId ?? throw new DomainException($"Choose a supplier for {r.Number} line {line.LineNumber}.");
                var qty = w.Quantity ?? line.Quantity;
                if (qty <= 0) throw new DomainException($"{r.Number} line {line.LineNumber}: quantity must be more than 0.");
                var price = w.UnitPrice ?? line.EstimatedUnitPrice ?? line.Part?.LastPurchasePrice ?? line.Part?.AverageCost ?? 0m;
                if (price < 0) throw new DomainException($"{r.Number} line {line.LineNumber}: the price can't be below 0.");
                return (Line: line, Request: r, SupplierId: supplier, Quantity: qty, Price: price);
            }).ToList();
            var supplierIds = resolved.Select(x => x.SupplierId).Distinct().ToList();
            var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id) && s.IsActive).ToDictionaryAsync(s => s.Id, ct);
            if (suppliers.Count != supplierIds.Count) throw new DomainException("Choose an active supplier.");
            var created = await db.InTransactionAsync(async tk =>
            {
                var list = new List<object>();
                var ordersOf = new Dictionary<Guid, List<string>>();
                foreach (var grp in resolved.GroupBy(x => (x.SupplierId, x.Request.SiteId)))
                {
                    var sup = suppliers[grp.Key.SupplierId];
                    var numbers = grp.Select(x => x.Request.Number).Distinct().OrderBy(n => n).ToList();
                    var who = string.Join(", ", grp.Select(x => x.Request.RequestedByName).OfType<string>().Distinct());
                    var input = new PurchasingEndpoints.PoInput(grp.Key.SiteId, sup.Id, Today, null, who.Length > 0 ? who[..Math.Min(who.Length, 200)] : null, sup.Currency, null,
                        sup.PaymentTerms, null, null, null, null, 0, $"From {string.Join(", ", numbers)}",
                        grp.OrderBy(x => x.Request.Number).ThenBy(x => x.Line.LineNumber).Select(x => new PurchasingEndpoints.LineInput(null, x.Line.PartId, x.Line.Description,
                            x.Line.UnitOfMeasure, x.Quantity, x.Price, 0, x.Line.StoreId, x.Line.WorkOrderId, null, x.Line.Id)).ToList());
                    var o = await PurchasingEndpoints.CreateDraftAsync(db, ctx, input, tk);
                    foreach (var x in grp) (ordersOf.TryGetValue(x.Request.Id, out var l) ? l : ordersOf[x.Request.Id] = []).Add(o.Number);
                    list.Add(new { o.Id, o.Number, Supplier = sup.Name, Lines = o.Lines.Count });
                }
                await PurchaseRequestSync.SyncAsync(db, requestIds, tk);
                foreach (var (rid, orders) in ordersOf) await mail.PrOrderedAsync(db, ctx, requests[rid], orders.Distinct().ToList(), tk);
                await db.SaveChangesAsync(tk);
                return list;
            }, ct);
            return Results.Ok(created);
        }).RequirePermission(Permissions.PurchasingCreate);

        // The buyer decides the rest won't be ordered.
        g.MapPost("/{id:guid}/close", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var r = await db.PurchaseRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartlyOrdered)) throw new DomainException("Only approved requests can be closed.");
            r.Status = PurchaseRequestStatus.Closed; r.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.Status });
        }).RequirePermission(Permissions.PurchasingCreate);

        g.MapPost("/{id:guid}/comments", async (Guid id, CommentInput i, HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            var me = UserId(http);
            var mine = await perms.GetAsync(me);
            if (!MayUse(mine)) return Results.Forbid();
            var r = await Visible(db, me, mine).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (i.Body.Clean() is not { } body) throw new DomainException("Write a comment.");
            if (body.Length > 4000) throw new DomainException("A comment can be at most 4,000 characters.");
            var c = new PurchaseRequestComment { TenantId = r.TenantId, PurchaseRequestId = r.Id, Body = body, UserId = me, UserName = ctx.UserName, CreatedAt = DateTimeOffset.UtcNow };
            db.PurchaseRequestComments.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        });
    }

    private static async Task<IResult> DecideAsync(Guid id, bool approve, string? reason, HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms,
        EmailOutbox mail, CancellationToken ct)
    {
        var me = UserId(http);
        var mine = await perms.GetAsync(me);
        var r = await Visible(db, me, mine).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        if (r.Status != PurchaseRequestStatus.Submitted) throw new DomainException("This request isn't waiting for approval.");
        if (r.RequestedById == me) throw new DomainException("You can't approve your own request.");
        var why = reason.Clean();
        if (!approve && why is null) throw new DomainException("Give a reason.");
        r.Status = approve ? PurchaseRequestStatus.Approved : PurchaseRequestStatus.Rejected;
        r.DecidedAt = DateTimeOffset.UtcNow; r.DecidedById = me; r.DecidedByName = ctx.UserName; r.RejectReason = approve ? null : why;
        await mail.PrDecidedAsync(db, ctx, r, approve, why, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { r.Id, r.Status });
    }

    /// <summary>Request lines already on an order that isn't cancelled.</summary>
    internal static async Task<HashSet<Guid>> OrderedLineIdsAsync(CmmsDbContext db, IReadOnlyCollection<Guid> lineIds, CancellationToken ct) =>
        (await db.PurchaseOrderLines.AllSites().Where(l => l.PurchaseRequestLineId != null && lineIds.Contains(l.PurchaseRequestLineId.Value)
                && db.PurchaseOrders.AllSites().Any(o => o.Id == l.PurchaseOrderId && o.Status != PurchaseOrderStatus.Cancelled))
            .Select(l => l.PurchaseRequestLineId!.Value).ToListAsync(ct)).ToHashSet();

    private static void RequireReady(PurchaseRequest r)
    {
        if (r.Lines.Count == 0) throw new DomainException("Add at least one line.");
        foreach (var l in r.Lines.OrderBy(x => x.LineNumber))
        {
            if (l.Quantity <= 0) throw new DomainException($"Line {l.LineNumber}: quantity must be more than 0.");
            if (l.PartId is not null && l.StoreId is null) throw new DomainException($"Line {l.LineNumber}: choose the store this part should arrive in.");
        }
    }

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, PrInput i, CancellationToken ct)
    {
        var v = new Validator().MaxLength("notes", i.Notes, "Notes", 4000);
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId && s.IsActive, ct)) v.Add("siteId", "Choose a site.");
        var lines = i.Lines ?? [];
        if (lines.Count > 200) v.Add("lines", "A request can have at most 200 lines.");
        var partIds = lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var storeIds = lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).Distinct().ToList();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SiteId, ct);
        var woIds = lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).Distinct().ToList();
        var wos = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => new { w.SiteId, w.Status }, ct);
        var assetIds = lines.Where(l => l.AssetId is not null).Select(l => l.AssetId!.Value).Distinct().ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.SiteId, ct);
        for (var k = 0; k < lines.Count; k++)
        {
            var l = lines[k];
            var n = k + 1;
            if (l.PartId is null && string.IsNullOrWhiteSpace(l.Description)) v.Add($"lines[{k}].description", $"Line {n}: enter a description or choose a part.");
            if (l.PartId is { } p && !parts.Contains(p)) v.Add($"lines[{k}].partId", $"Line {n}: unknown part.");
            if (l.Quantity < 0) v.Add($"lines[{k}].quantity", $"Line {n}: quantity must be more than 0.");
            if (l.EstimatedUnitPrice < 0) v.Add($"lines[{k}].estimatedUnitPrice", $"Line {n}: the price can't be below 0.");
            if ((l.Description?.Length ?? 0) > 500) v.Add($"lines[{k}].description", $"Line {n}: the description can be at most 500 characters.");
            if (l.StoreId is { } s && (!stores.TryGetValue(s, out var storeSite) || storeSite != i.SiteId)) v.Add($"lines[{k}].storeId", $"Line {n}: choose one of this site's stores.");
            if (l.WorkOrderId is { } w)
            {
                if (!wos.TryGetValue(w, out var wo) || wo.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled) v.Add($"lines[{k}].workOrderId", $"Line {n}: the work order is unknown or closed.");
                else if (wo.SiteId != i.SiteId) v.Add($"lines[{k}].workOrderId", $"Line {n}: the work order is at another site.");
            }
            if (l.AssetId is { } a && (!assets.TryGetValue(a, out var assetSite) || assetSite != i.SiteId)) v.Add($"lines[{k}].assetId", $"Line {n}: the asset is at another site.");
        }
        var who = (i.RecipientIds ?? []).Distinct().ToList();
        if (who.Count > 0 && await db.Users.CountAsync(u => who.Contains(u.Id) && u.TenantId == db.CurrentTenantId && u.IsActive, ct) != who.Count)
            v.Add("recipientIds", "Choose active users.");
        return v;
    }

    private static async Task ApplyAsync(CmmsDbContext db, PurchaseRequest r, PrInput i, CancellationToken ct)
    {
        r.SiteId = i.SiteId; r.NeededBy = i.NeededBy; r.Notes = i.Notes.Clean(); r.RecipientIds = (i.RecipientIds ?? []).Distinct().ToList();
        var lines = i.Lines ?? [];
        var partIds = lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var woIds = lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).ToList();
        var woAssets = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.AssetId, ct);
        foreach (var gone in r.Lines.Where(l => lines.All(x => x.Id != l.Id)).ToList()) r.Lines.Remove(gone);
        var n = 1;
        foreach (var x in lines)
        {
            var line = x.Id is { } lid ? r.Lines.FirstOrDefault(l => l.Id == lid) : null;
            var part = x.PartId is { } pid ? parts.GetValueOrDefault(pid) : null;
            if (line is null)
            {
                line = new PurchaseRequestLine { Description = "" };
                r.Lines.Add(line);
                if (db.Entry(r).State != EntityState.Detached && db.Entry(r).State != EntityState.Added) db.Entry(line).State = EntityState.Added; // preset Guid id: a new row
            }
            line.LineNumber = n++; line.PartId = x.PartId; line.Description = x.Description.Clean() ?? part?.Name ?? "";
            line.UnitOfMeasure = x.UnitOfMeasure.Clean() ?? part?.UnitOfMeasure; line.Quantity = x.Quantity; line.NeededBy = x.NeededBy;
            line.WorkOrderId = x.WorkOrderId;
            line.AssetId = x.WorkOrderId is { } w && woAssets.GetValueOrDefault(w) is { } wa ? wa : x.AssetId;
            line.StoreId = x.StoreId; line.EstimatedUnitPrice = x.EstimatedUnitPrice; line.Reason = x.Reason.Clean();
        }
    }

    private static async Task<object> DetailAsync(CmmsDbContext db, PurchaseRequest r, Guid me, IReadOnlySet<string> mine, CancellationToken ct)
    {
        var partIds = r.Lines.Where(l => l.PartId is not null).Select(l => l.PartId!.Value).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Id, p.PartNumber, p.Name, p.PreferredSupplierId }, ct);
        var storeIds = r.Lines.Where(l => l.StoreId is not null).Select(l => l.StoreId!.Value).ToList();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => new { s.Id, s.Code, s.Name }, ct);
        var woIds = r.Lines.Where(l => l.WorkOrderId is not null).Select(l => l.WorkOrderId!.Value).ToList();
        var wos = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => new { w.Id, w.Number, w.Title }, ct);
        var assetIds = r.Lines.Where(l => l.AssetId is not null).Select(l => l.AssetId!.Value).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => new { a.Id, a.AssetNumber, a.Name }, ct);
        var lineIds = r.Lines.Select(l => l.Id).ToList();
        var orderLines = await db.PurchaseOrderLines.AllSites().AsNoTracking()
            .Where(l => l.PurchaseRequestLineId != null && lineIds.Contains(l.PurchaseRequestLineId.Value))
            .Select(l => new
            {
                RequestLineId = l.PurchaseRequestLineId!.Value, l.Id, l.PurchaseOrderId, l.Quantity, l.QuantityReceived,
                Order = db.PurchaseOrders.AllSites().Where(o => o.Id == l.PurchaseOrderId).Select(o => new { o.Number, o.Status }).FirstOrDefault(),
            }).ToListAsync(ct);
        var live = orderLines.Where(o => o.Order is not null && o.Order.Status != PurchaseOrderStatus.Cancelled).ToLookup(o => o.RequestLineId);
        var comments = await db.PurchaseRequestComments.AsNoTracking().Where(c => c.PurchaseRequestId == r.Id).OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Body, c.UserId, c.UserName, c.CreatedAt }).ToListAsync(ct);
        var anyOrdered = live.Count > 0;
        var orderable = r.Status is PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartlyOrdered;
        return new
        {
            r.Id, r.Number, r.Status, r.SiteId, SiteName = r.Site?.Name, r.RequestedById, r.RequestedByName, r.NeededBy, r.Notes, r.RecipientIds,
            r.SubmittedAt, r.DecidedAt, r.DecidedByName, r.RejectReason, r.CancelledAt, r.CancelReason, r.ClosedAt, r.CreatedAt,
            Lines = r.Lines.OrderBy(l => l.LineNumber).Select(l =>
            {
                var o = live[l.Id].FirstOrDefault();
                return new
                {
                    l.Id, l.LineNumber, l.PartId, Part = l.PartId is { } p ? parts.GetValueOrDefault(p) : null, l.Description, l.UnitOfMeasure, l.Quantity, l.NeededBy,
                    WorkOrder = l.WorkOrderId is { } w ? wos.GetValueOrDefault(w) : null, Asset = l.AssetId is { } a ? assets.GetValueOrDefault(a) : null,
                    Store = l.StoreId is { } s ? stores.GetValueOrDefault(s) : null, l.EstimatedUnitPrice, l.Reason,
                    Order = o is null ? null : new { Id = o.PurchaseOrderId, o.Order!.Number, LineId = o.Id, o.Order.Status },
                    Ordered = o?.Quantity ?? 0m, Received = o?.QuantityReceived ?? 0m,
                };
            }),
            Comments = comments,
            Actions = new
            {
                Edit = Editable(r.Status) && r.RequestedById == me && mine.Contains(Permissions.PurchasingRequest),
                Submit = Editable(r.Status) && r.RequestedById == me && mine.Contains(Permissions.PurchasingRequest),
                Approve = CanDecide(r, me, mine),
                Reject = CanDecide(r, me, mine),
                Cancel = r.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Submitted or PurchaseRequestStatus.Approved or PurchaseRequestStatus.Rejected
                    && !anyOrdered && (r.RequestedById == me || mine.Contains(Permissions.PurchasingApproveRequests)),
                Delete = r.Status == PurchaseRequestStatus.Draft && r.RequestedById == me,
                Order = orderable && mine.Contains(Permissions.PurchasingCreate) && r.Lines.Any(l => !live.Contains(l.Id)),
                Close = orderable && mine.Contains(Permissions.PurchasingCreate),
                Comment = true,
            },
            RowVersion = Convert.ToBase64String(r.RowVersion),
        };
    }
}

/// <summary>Keeps purchase requests in step with the orders made from them (0.16.0).</summary>
internal static class PurchaseRequestSync
{
    /// <summary>Recomputes each request's status from how many of its lines are on live orders. The caller saves.</summary>
    public static async Task SyncAsync(CmmsDbContext db, IReadOnlyCollection<Guid> requestIds, CancellationToken ct)
    {
        if (requestIds.Count == 0) return;
        var requests = await db.PurchaseRequests.AllSites().Include(r => r.Lines).Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
        var lineIds = requests.SelectMany(r => r.Lines).Select(l => l.Id).ToList();
        var ordered = await PurchaseRequestEndpoints.OrderedLineIdsAsync(db, lineIds, ct);
        foreach (var r in requests)
            r.Status = PurchaseRequestRules.StatusAfterOrdering(r.Status, r.Lines.Count, r.Lines.Count(l => ordered.Contains(l.Id)));
    }

    /// <summary>After order lines made from requests are deleted or their order cancelled: their requests follow.</summary>
    public static async Task SyncLinesAsync(CmmsDbContext db, IReadOnlyCollection<Guid> requestLineIds, CancellationToken ct) =>
        await SyncAsync(db, await db.PurchaseRequestLines.AllSites().Where(l => requestLineIds.Contains(l.Id)).Select(l => l.PurchaseRequestId).Distinct().ToListAsync(ct), ct);

    /// <summary>Order lines made from requests were received: each requester hears about their lines.</summary>
    public static async Task ReceivedAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, PurchaseOrder o, IReadOnlyCollection<(Guid LineId, decimal Quantity)> received,
        CancellationToken ct)
    {
        var byRequestLine = received.Select(x => (Line: o.Lines.FirstOrDefault(l => l.Id == x.LineId), x.Quantity))
            .Where(x => x.Line?.PurchaseRequestLineId is not null).ToList();
        if (byRequestLine.Count == 0) return;
        var ids = byRequestLine.Select(x => x.Line!.PurchaseRequestLineId!.Value).ToList();
        var requestOf = await db.PurchaseRequestLines.AllSites().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.PurchaseRequestId, ct);
        var requestIds = requestOf.Values.Distinct().ToList();
        var requests = await db.PurchaseRequests.AllSites().AsNoTracking().Include(r => r.Lines).Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
        foreach (var r in requests)
        {
            var lines = byRequestLine.Where(x => requestOf.GetValueOrDefault(x.Line!.PurchaseRequestLineId!.Value) == r.Id)
                .Select(x => $"{x.Line!.Description}: {x.Quantity:0.###} {x.Line.UnitOfMeasure}".Trim()).ToList();
            await mail.PrReceivedAsync(db, ctx, r, lines, ct);
        }
    }
}
