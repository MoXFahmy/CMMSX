using System.Linq.Expressions;
using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class WorkRequestEndpoints
{
    public sealed record RequestInput(string Title, string? Description, Guid SiteId, Guid? LocationId, Guid? AssetId, WorkPriority Priority, WorkType Type,
        string? RequesterName, string? RequesterEmail, string? RequesterPhone, DateOnly? RequiredBy, bool AssetIsDown = false);
    public sealed record ApproveInput(WorkType? Type, WorkPriority? Priority, Guid? TradeId, List<Guid>? AssigneeIds, DateOnly? DueDate, string? Note, DateTimeOffset? DueAt = null);
    public sealed record DecisionInput(string? Note);

    private static readonly Dictionary<string, Expression<Func<WorkRequest, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = r => r.Number, ["title"] = r => r.Title, ["status"] = r => r.Status, ["priority"] = r => r.Priority, ["createdAt"] = r => r.CreatedAt,
        ["requiredBy"] = r => r.RequiredBy, ["site"] = r => r.Site!.Name,
    };

    public sealed class RequestFilter
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? AssetId { get; set; }
        public bool? Mine { get; set; }
        /// <summary>Still waiting for a decision after the response or completion deadline.</summary>
        public bool? Late { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record WorkRequestListItem(Guid Id, string Number, string Title, WorkRequestStatus Status, WorkPriority Priority, WorkType Type, Guid SiteId,
        string SiteName, string? LocationName, Guid? AssetId, string? AssetNumber, string? AssetName, string? RequesterName, DateOnly? RequiredBy,
        DateTimeOffset CreatedAt, Guid? WorkOrderId, string? WorkOrderNumber,
        DateTimeOffset? RespondBy, DateTimeOffset? DueAt, SlaState ResponseState, SlaState CompletionState);

    /// <summary>One page of the requests this user may see (the list screen and its export share it).</summary>
    internal static async Task<PagedResult<WorkRequestListItem>> ListAsync(CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx,
        RequestFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 500)
    {
        var q = await VisibleAsync(db, http, perms, ctx);
        if (f.Mine == true) q = q.Where(r => r.CreatedById == ctx.UserId);
        if (f.Search.Clean() is { } s) q = q.Where(r => r.Number.Contains(s) || r.Title.Contains(s) || r.RequesterName!.Contains(s));
        var statuses = (f.Status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<WorkRequestStatus>(x, true, out var e) ? (WorkRequestStatus?)e : null).OfType<WorkRequestStatus>().ToList();
        if (statuses.Count > 0) q = q.Where(r => statuses.Contains(r.Status));
        if (f.SiteId is not null) q = q.Where(r => r.SiteId == f.SiteId);
        if (f.AssetId is not null) q = q.Where(r => r.AssetId == f.AssetId);
        var now = DateTimeOffset.UtcNow;
        if (f.Late == true) q = SlaQueries.LateRequests(q, now);
        var settings = await SlaQueries.SettingsAsync(db, ct);
        var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "createdAt").Select(r => new
        {
            r.Id, r.Number, r.Title, r.Status, r.Priority, r.Type, r.SiteId, SiteName = r.Site!.Name, LocationName = r.Location!.Name,
            r.AssetId, AssetNumber = r.Asset!.AssetNumber, AssetName = r.Asset.Name, r.RequesterName, r.RequiredBy, r.CreatedAt, r.WorkOrderId,
            r.RespondBy, r.DueAt, r.DueFromTarget, r.DecidedAt,
            Work = db.WorkOrders.Where(w => w.Id == r.WorkOrderId)
                .Select(w => new { w.Number, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt }).FirstOrDefault(),
        }).ToPagedAsync(page, pageSize, ct, maxPageSize);
        return new PagedResult<WorkRequestListItem>(paged.Items.Select(r =>
        {
            var work = r.Work is { } w ? SlaQueries.Work(settings, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt, now).Completion : (SlaState?)null;
            var sla = SlaQueries.Request(settings, r.Priority, r.Status, r.RespondBy, r.DecidedAt, r.DueAt, r.DueFromTarget, work, now);
            return new WorkRequestListItem(r.Id, r.Number, r.Title, r.Status, r.Priority, r.Type, r.SiteId, r.SiteName, r.LocationName, r.AssetId, r.AssetNumber, r.AssetName,
                r.RequesterName, r.RequiredBy, r.CreatedAt, r.WorkOrderId, r.Work?.Number, r.RespondBy,
                // Approved: the work order's deadline, the one its state is about.
                r.Status == WorkRequestStatus.Approved && r.Work is { } wo ? wo.DueAt : r.DueAt, sla.Response, sla.Completion);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    public static void MapWorkRequestEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/work-requests").WithTags("Work requests");

        g.MapGet("/", ([AsParameters] RequestFilter f, CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
            ListAsync(db, http, perms, ctx, f, f.Page ?? 1, f.PageSize ?? 25, ct)).RequireAuthorization();

        g.MapGet("/export", ([AsParameters] RequestFilter f, CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "work-requests", (page, size) => ListAsync(db, http, perms, ctx, f, page, size, ct, ListExport.MaxRows), ct))
            .RequireAuthorization();

        g.MapGet("/summary", async (CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
            await (await VisibleAsync(db, http, perms, ctx)).GroupBy(r => r.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct))
            .RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
        {
            var r = await (await VisibleAsync(db, http, perms, ctx)).Include(x => x.Site).Include(x => x.Location).Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var users = await db.Users.Where(u => u.Id == r.CreatedById || u.Id == r.DecidedById).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            var wo = r.WorkOrderId is { } wid ? await db.WorkOrders.Where(w => w.Id == wid).Select(w => new { w.Id, w.Number, w.Status }).FirstOrDefaultAsync(ct) : null;
            var settings = await SlaQueries.SettingsAsync(db, ct);
            var now = DateTimeOffset.UtcNow;
            var work = r.WorkOrderId is { } wid2 ? await db.WorkOrders.Where(w => w.Id == wid2)
                .Select(w => new { w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt }).FirstOrDefaultAsync(ct) : null;
            var sla = SlaQueries.Request(settings, r.Priority, r.Status, r.RespondBy, r.DecidedAt, r.DueAt, r.DueFromTarget,
                work is null ? null : SlaQueries.Work(settings, work.Priority, work.Status, work.StartBy, work.RespondedAt, work.DueAt, work.DueFromTarget, work.CompletedAt, now).Completion, now);
            return Results.Ok(new
            {
                r.Id, r.Number, r.Title, r.Description, r.Status, r.Priority, r.Type, r.SiteId, Site = new { r.Site!.Id, r.Site.Name }, r.LocationId, LocationName = r.Location?.Name,
                r.AssetId, Asset = r.Asset is null ? null : new { r.Asset.Id, r.Asset.AssetNumber, r.Asset.Name }, r.RequesterName, r.RequesterEmail, r.RequesterPhone,
                r.RequiredBy, r.RespondBy, DueAt = r.Status == WorkRequestStatus.Approved && work is not null ? work.DueAt : r.DueAt, r.DueFromTarget, Sla = sla,
                r.DecisionNote, r.DecidedAt, DecidedBy = r.DecidedById is { } d ? users.GetValueOrDefault(d) : null, WorkOrder = wo, r.DowntimeEventId,
                r.CreatedAt, CreatedBy = r.CreatedById is { } c ? users.GetValueOrDefault(c) : null, CanCancel = r.Status == WorkRequestStatus.New && r.CreatedById == ctx.UserId,
            });
        }).RequireAuthorization();

        g.MapPost("/", async (RequestInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var (v, r) = await CreateAsync(db, ctx, mail, input, ct);
            if (!v.IsValid) return v.Problem();
            return Results.Created($"/api/work-requests/{r!.Id}", new { r.Id, r.Number });
        }).RequirePermission(Permissions.RequestsCreate);

        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != WorkRequestStatus.New) throw new DomainException("Only new requests can be approved.");
            var priority = input.Priority ?? r.Priority;
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            // The clock started when the requester asked: the deadline counts from the request, even with a new priority.
            var (dueAt, fromTarget) = Deadlines.OnApprove(r, priority, input.DueAt, input.DueDate, settings);
            var woInput = new WorkOrderEndpoints.WorkOrderInput(r.Title, r.Description, input.Type ?? r.Type, priority, r.SiteId, r.LocationId, r.AssetId,
                input.TradeId, input.AssigneeIds, null, null, null, null, DueAt: dueAt);
            var v = await WorkOrderEndpoints.ValidateAsync(woInput, db, ct);
            if (!v.IsValid) return v.Problem();
            var wo = await db.InTransactionAsync(async token =>
            {
                // The request's stop (if it said the asset is down) carries over: completing the work order ends it.
                var created = await WorkOrderEndpoints.CreateAsync(db, ctx, woInput, r.Id, token, w => { w.DueFromTarget = fromTarget; w.DowntimeEventId = r.DowntimeEventId; },
                    mail: mail, raisedAt: r.CreatedAt);
                r.Status = WorkRequestStatus.Approved; r.WorkOrderId = created.Id; r.DecisionNote = input.Note.Clean(); r.DecidedAt = DateTimeOffset.UtcNow; r.DecidedById = ctx.UserId;
                await mail.RequestDecidedAsync(db, ctx, r, created.Number, token);
                await db.SaveChangesAsync(token);
                return created;
            }, ct);
            return Results.Ok(new { workOrderId = wo.Id, wo.Number });
        }).RequirePermission(Permissions.RequestsManage);

        g.MapPost("/{id:guid}/reject", async (Guid id, DecisionInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != WorkRequestStatus.New) throw new DomainException("Only new requests can be rejected.");
            var v = new Validator().Required("note", input.Note, "Reason", 2000);
            if (!v.IsValid) return v.Problem();
            r.Status = WorkRequestStatus.Rejected; r.DecisionNote = input.Note!.Trim(); r.DecidedAt = DateTimeOffset.UtcNow; r.DecidedById = ctx.UserId;
            await mail.RequestDecidedAsync(db, ctx, r, null, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        }).RequirePermission(Permissions.RequestsManage);

        g.MapPost("/{id:guid}/cancel", async (Guid id, DecisionInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var manager = (await perms.GetAsync(UserId(http))).Contains(Permissions.RequestsManage);
            if (r.CreatedById != ctx.UserId && !manager) return Results.Forbid();
            if (r.Status != WorkRequestStatus.New) throw new DomainException("Only new requests can be cancelled.");
            r.Status = WorkRequestStatus.Cancelled; r.DecisionNote = input.Note.Clean(); r.DecidedAt = DateTimeOffset.UtcNow; r.DecidedById = ctx.UserId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        }).RequireAuthorization();
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>People with requests.view see every request; everyone else only sees requests they submitted.</summary>
    private static async Task<IQueryable<WorkRequest>> VisibleAsync(CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx)
    {
        var p = await perms.GetAsync(UserId(http));
        var q = db.WorkRequests.AsNoTracking();
        return p.Contains(Permissions.RequestsView) || p.Contains(Permissions.RequestsManage) ? q : q.Where(r => r.CreatedById == ctx.UserId);
    }

    /// <summary>Raises a work request (numbered, requester defaulted from the signed-in user, reviewers e-mailed).</summary>
    /// <param name="raisedAt">When it was raised, if not now (a request made offline on a phone): its deadlines count from then.</param>
    internal static async Task<(Validator V, WorkRequest? Request)> CreateAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, RequestInput input, CancellationToken ct,
        DateTimeOffset? raisedAt = null)
    {
        var v = await ValidateAsync(input, db, ct);
        if (!v.IsValid) return (v, null);

        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var number = await Numbering.NextAsync(db, "WorkRequest", settings.WorkRequestNumberPrefix, 5,
            n => db.WorkRequests.AnyAsync(r => r.Number == n, ct), p => db.WorkRequests.Where(r => r.Number.StartsWith(p)).Select(r => r.Number).ToListAsync(ct), ct);
        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.UserId, ct);
        var r = new WorkRequest
        {
            Number = number, Title = input.Title.Trim(), Description = input.Description.Clean(), SiteId = input.SiteId, LocationId = input.LocationId, AssetId = input.AssetId,
            Priority = input.Priority, Type = input.Type, RequiredBy = input.RequiredBy,
            RequesterName = input.RequesterName.Clean() ?? me?.FullName, RequesterEmail = input.RequesterEmail.Clean() ?? me?.Email, RequesterPhone = input.RequesterPhone.Clean() ?? me?.Phone,
        };
        if (r.AssetId is not null && r.LocationId is null) r.LocationId = await db.Assets.Where(a => a.Id == r.AssetId).Select(a => a.LocationId).FirstOrDefaultAsync(ct);
        r.CreatedAt = raisedAt ?? DateTimeOffset.UtcNow;
        Deadlines.OnCreate(r, settings, r.CreatedAt);
        // "The asset is down": the stop starts when the request was raised, or joins the asset's open stop.
        if (input.AssetIsDown && r.AssetId is { } downAsset)
            r.DowntimeEventId = (await Downtime.StartOrJoinAsync(db, downAsset, r.CreatedAt, r.Title, FourC.Cmms.Core.Assets.DowntimeSource.Request, r.Number, ct)).Id;
        db.WorkRequests.Add(r);
        await mail.RequestSubmittedAsync(db, ctx, r, ct);
        await db.SaveChangesAsync(ct);
        return (v, r);
    }

    private static async Task<Validator> ValidateAsync(RequestInput i, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("title", i.Title, "What needs to be done", 300).MaxLength("description", i.Description, "Details", 8000)
            .MaxLength("requesterName", i.RequesterName, "Name", 200).MaxLength("requesterPhone", i.RequesterPhone, "Phone", 50)
            .When(!Enum.IsDefined(i.Priority), "priority", "Choose a priority.").When(!Enum.IsDefined(i.Type), "type", "Choose a type.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        return v;
    }
}
