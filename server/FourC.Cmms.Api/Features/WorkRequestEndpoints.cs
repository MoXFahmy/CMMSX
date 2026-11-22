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
        string? RequesterName, string? RequesterEmail, string? RequesterPhone, DateOnly? RequiredBy, bool AssetIsDown = false,
        Guid? RequestTypeId = null, List<Guid>? ExtraRecipientIds = null, List<AccessWindow>? AccessWindows = null);
    public sealed record ApproveInput(WorkType? Type, WorkPriority? Priority, Guid? TradeId, List<Guid>? AssigneeIds, DateOnly? DueDate, string? Note, DateTimeOffset? DueAt = null,
        Guid? TeamId = null);

    /// <summary>At most this many extra recipients on one request.</summary>
    public const int MaxExtraRecipients = 10;
    public sealed record DecisionInput(string? Note);
    public sealed record CommentInput(string? Body);

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
        public Guid? RequestTypeId { get; set; }
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
        DateTimeOffset? RespondBy, DateTimeOffset? DueAt, SlaState ResponseState, SlaState CompletionState, string? RequestTypeName = null);

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
        if (f.RequestTypeId is not null) q = q.Where(r => r.RequestTypeId == f.RequestTypeId);
        var now = DateTimeOffset.UtcNow;
        if (f.Late == true) q = SlaQueries.LateRequests(q, now);
        var settings = await SlaQueries.SettingsAsync(db, ct);
        var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "createdAt").Select(r => new
        {
            r.Id, r.Number, r.Title, r.Status, r.Priority, r.Type, r.SiteId, SiteName = r.Site!.Name, LocationName = r.Location!.Name,
            r.AssetId, AssetNumber = r.Asset!.AssetNumber, AssetName = r.Asset.Name, r.RequesterName, r.RequiredBy, r.CreatedAt, r.WorkOrderId,
            r.RespondBy, r.DueAt, r.DueFromTarget, r.DecidedAt, RequestTypeName = r.RequestType!.Name,
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
                r.Status is WorkRequestStatus.Approved or WorkRequestStatus.Closed && r.Work is { } wo ? wo.DueAt : r.DueAt, sla.Response, sla.Completion, r.RequestTypeName);
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
            var r = await (await VisibleAsync(db, http, perms, ctx)).Include(x => x.Site).Include(x => x.Location).Include(x => x.Asset).Include(x => x.RequestType)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var users = await db.Users.Where(u => u.Id == r.CreatedById || u.Id == r.DecidedById).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            var canAnswer = r.AwaitingFeedbackSince != null && await RequestFeedbackService.IsRequesterAsync(db, r, ctx.UserId, ct);
            var feedback = await db.RequestFeedback.AsNoTracking().Where(f => f.WorkRequestId == r.Id).OrderBy(f => f.AnsweredAt)
                .Select(f => new { f.Id, f.Accepted, f.Rating, f.Comment, f.ByName, f.AnsweredAt, f.ViaLink }).ToListAsync(ct);
            var extra = r.ExtraRecipientIds.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => r.ExtraRecipientIds.Contains(u.Id))
                .OrderBy(u => u.FullName).Select(u => new { u.Id, Name = u.FullName }).ToListAsync(ct);
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
                r.RequiredBy, r.RespondBy, DueAt = r.Status is WorkRequestStatus.Approved or WorkRequestStatus.Closed && work is not null ? work.DueAt : r.DueAt, r.DueFromTarget, Sla = sla,
                r.DecisionNote, r.DecidedAt, DecidedBy = r.DecidedById is { } d ? users.GetValueOrDefault(d) : null, WorkOrder = wo, r.DowntimeEventId,
                r.CreatedAt, CreatedBy = r.CreatedById is { } c ? users.GetValueOrDefault(c) : null, CanCancel = r.Status == WorkRequestStatus.New && r.CreatedById == ctx.UserId,
                r.RequestTypeId, RequestType = r.RequestType is null ? null : new { r.RequestType.Id, r.RequestType.Name, r.RequestType.DefaultTradeId },
                ExtraRecipients = extra, r.AccessWindows, r.OnlyWhenStopped,
                AwaitingFeedback = r.AwaitingFeedbackSince != null, r.AwaitingFeedbackSince, CanAnswer = canAnswer,
                Feedback = feedback,
            });
        }).RequireAuthorization();

        // People who can see a site, for the request form's "keep informed" picker (names only); only for a site the asker sees.
        g.MapGet("/people", async (Guid siteId, CmmsDbContext db, AccessService access, CancellationToken ct) =>
        {
            if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct)) return Results.Ok(Array.Empty<object>());
            var users = await db.Users.AsNoTracking().Where(u => u.IsActive && u.TenantId == db.CurrentTenantId).OrderBy(u => u.FullName)
                .Select(u => new { u.Id, Name = u.FullName }).Take(1000).ToListAsync(ct);
            var seen = new List<object>();
            foreach (var u in users)
                if (Access.CoversSite(await access.GetAsync(u.Id, ct), siteId)) seen.Add(u);
            return Results.Ok(seen);
        }).RequirePermission(Permissions.RequestsCreate);

        g.MapPost("/", async (RequestInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, AccessService access, CancellationToken ct) =>
        {
            var (v, r) = await CreateAsync(db, ctx, mail, access, input, ct);
            if (!v.IsValid) return v.Problem();
            return Results.Created($"/api/work-requests/{r!.Id}", new { r.Id, r.Number });
        }).RequirePermission(Permissions.RequestsCreate);

        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != WorkRequestStatus.New) throw new DomainException("Only new requests can be approved.");
            var (v, wo) = await AtomicallyAsync(db, token => ApproveCoreAsync(db, ctx, mail, r, input, null, token), ct);
            if (!v.IsValid) return v.Problem();
            return Results.Ok(new { workOrderId = wo!.Id, wo.Number });
        }).RequirePermission(Permissions.RequestsManage);

        g.MapPost("/{id:guid}/reject", async (Guid id, DecisionInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != WorkRequestStatus.New) throw new DomainException("Only new requests can be rejected.");
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var v = (settings.CommentRequiredFor ?? []).Contains(CommentActions.Reject)
                ? new Validator().Required("note", input.Note, "Reason", 2000) : new Validator().MaxLength("note", input.Note, "Reason", 2000);
            if (!v.IsValid) return v.Problem();
            r.Status = WorkRequestStatus.Rejected; r.DecisionNote = input.Note.Clean(); r.DecidedAt = DateTimeOffset.UtcNow; r.DecidedById = ctx.UserId;
            await mail.RequestDecidedAsync(db, ctx, r, null, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        }).RequirePermission(Permissions.RequestsManage);

        // ---- The thread between the requester and maintenance ----
        g.MapGet("/{id:guid}/comments", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await (await VisibleAsync(db, http, perms, ctx)).AnyAsync(r => r.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await CommentsAsync(db, id, ct));
        }).RequireAuthorization();

        g.MapPost("/{id:guid}/comments", async (Guid id, CommentInput input, CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx, EmailOutbox mail,
            CancellationToken ct) =>
        {
            var r = await (await VisibleAsync(db, http, perms, ctx)).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var v = new Validator().Required("body", input.Body, "Comment", 4000);
            if (!v.IsValid) return v.Problem();
            var c = new WorkRequestComment
            {
                WorkRequestId = r.Id, Body = input.Body!.Trim(), UserId = ctx.UserId, UserName = ctx.UserName,
                ByRequester = await RequestFeedbackService.IsRequesterAsync(db, r, ctx.UserId, ct),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.WorkRequestComments.Add(c);
            await mail.RequestCommentAsync(db, ctx, r, c, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequireAuthorization();

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

    /// <summary>A request's thread, oldest first (the request page, and read-only on its work order).</summary>
    internal static async Task<List<RequestCommentView>> CommentsAsync(CmmsDbContext db, Guid requestId, CancellationToken ct) =>
        await db.WorkRequestComments.AsNoTracking().Where(c => c.WorkRequestId == requestId).OrderBy(c => c.CreatedAt)
            .Select(c => new RequestCommentView(c.Id, c.Body, c.UserName, c.ByRequester, c.CreatedAt)).ToListAsync(ct);

    public sealed record RequestCommentView(Guid Id, string Body, string? UserName, bool ByRequester, DateTimeOffset CreatedAt);

    /// <summary>Runs <paramref name="work"/> in a transaction, or in the caller's when one is already open (a phone change).</summary>
    private static Task<T> AtomicallyAsync<T>(CmmsDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        db.Database.CurrentTransaction is null ? db.InTransactionAsync(work, ct) : work(ct);

    /// <summary>
    /// Approves a new request into a work order (the caller holds the transaction). The work order takes the request's place, deadline,
    /// stop, access windows and "only while stopped". <paramref name="autoNote"/>: approved automatically by its type (decided by nobody).
    /// </summary>
    internal static async Task<(Validator V, WorkOrder? Wo)> ApproveCoreAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, WorkRequest r, ApproveInput input,
        string? autoNote, CancellationToken ct)
    {
        var priority = input.Priority ?? r.Priority;
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        // The clock started when the requester asked: the deadline counts from the request, even with a new priority.
        var (dueAt, fromTarget) = Deadlines.OnApprove(r, priority, input.DueAt, input.DueDate, settings);
        var woInput = new WorkOrderEndpoints.WorkOrderInput(r.Title, r.Description, input.Type ?? r.Type, priority, r.SiteId, r.LocationId, r.AssetId,
            input.TradeId, input.AssigneeIds, null, null, null, null, DueAt: dueAt, TeamId: input.TeamId);
        var v = await WorkOrderEndpoints.ValidateAsync(woInput, db, ct);
        if (!v.IsValid) return (v, null);
        // The request's stop (if it said the asset is down) carries over: completing the work order ends it.
        var created = await WorkOrderEndpoints.CreateAsync(db, ctx, woInput, r.Id, ct, w =>
        {
            w.DueFromTarget = fromTarget; w.DowntimeEventId = r.DowntimeEventId;
            w.AccessWindows = [.. r.AccessWindows]; w.OnlyWhenStopped = r.OnlyWhenStopped;
        }, mail: mail, raisedAt: r.CreatedAt);
        r.Status = WorkRequestStatus.Approved; r.WorkOrderId = created.Id; r.DecidedAt = DateTimeOffset.UtcNow;
        r.DecisionNote = autoNote ?? input.Note.Clean(); r.DecidedById = autoNote is null ? ctx.UserId : null;
        // Approved automatically: nobody decided, so the requester (who raised it) still hears it was approved.
        await mail.RequestDecidedAsync(db, autoNote is null ? ctx : new FixedTenantContext(ctx.TenantId, null, ctx.UserName), r, created.Number, ct);
        await db.SaveChangesAsync(ct);
        return (v, created);
    }

    /// <summary>People with requests.view see every request; everyone else only sees requests they submitted.</summary>
    private static async Task<IQueryable<WorkRequest>> VisibleAsync(CmmsDbContext db, HttpContext http, PermissionService perms, ITenantContext ctx)
    {
        var p = await perms.GetAsync(UserId(http));
        var q = db.WorkRequests.AsNoTracking();
        return p.Contains(Permissions.RequestsView) || p.Contains(Permissions.RequestsManage) ? q : q.Where(r => r.CreatedById == ctx.UserId);
    }

    /// <summary>Raises a work request (numbered, requester defaulted from the signed-in user, reviewers e-mailed).</summary>
    /// <param name="raisedAt">When it was raised, if not now (a request made offline on a phone): its deadlines count from then.</param>
    internal static async Task<(Validator V, WorkRequest? Request)> CreateAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, AccessService access, RequestInput input,
        CancellationToken ct, DateTimeOffset? raisedAt = null)
    {
        var v = await ValidateAsync(input, db, access, ct);
        if (!v.IsValid) return (v, null);
        return await AtomicallyAsync(db, token => CreateCoreAsync(db, ctx, mail, input, v, raisedAt, token), ct);
    }

    private static async Task<(Validator V, WorkRequest? Request)> CreateCoreAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, RequestInput input, Validator v,
        DateTimeOffset? raisedAt, CancellationToken ct)
    {

        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var (sequence, prefix) = Numbering.For(settings, await db.Sites.AllSites().AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.SiteId, ct), NumberKind.WorkRequest);
        var number = await Numbering.NextAsync(db, sequence, prefix, 5,
            n => db.WorkRequests.AllSites().AnyAsync(r => r.Number == n, ct), p => db.WorkRequests.AllSites().Where(r => r.Number.StartsWith(p)).Select(r => r.Number).ToListAsync(ct), ct);
        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.UserId, ct);
        var r = new WorkRequest
        {
            Number = number, Title = input.Title.Trim(), Description = input.Description.Clean(), SiteId = input.SiteId, LocationId = input.LocationId, AssetId = input.AssetId,
            Priority = input.Priority, Type = input.Type, RequiredBy = input.RequiredBy,
            RequesterName = input.RequesterName.Clean() ?? me?.FullName, RequesterEmail = input.RequesterEmail.Clean() ?? me?.Email, RequesterPhone = input.RequesterPhone.Clean() ?? me?.Phone,
            RequestTypeId = input.RequestTypeId, ExtraRecipientIds = (input.ExtraRecipientIds ?? []).Distinct().ToList(), AccessWindows = AccessWindows.Clean(input.AccessWindows),
        };
        if (r.AssetId is { } assetId)
        {
            var asset = await db.Assets.Where(a => a.Id == assetId).Select(a => new { a.LocationId, a.OnlyWhenStopped }).FirstOrDefaultAsync(ct);
            r.LocationId ??= asset?.LocationId;
            r.OnlyWhenStopped = asset?.OnlyWhenStopped == true;
        }
        r.CreatedAt = raisedAt ?? DateTimeOffset.UtcNow;
        Deadlines.OnCreate(r, settings, r.CreatedAt);
        // "The asset is down": the stop starts when the request was raised, or joins the asset's open stop.
        if (input.AssetIsDown && r.AssetId is { } downAsset)
            r.DowntimeEventId = (await Downtime.StartOrJoinAsync(db, downAsset, r.CreatedAt, r.Title, FourC.Cmms.Core.Assets.DowntimeSource.Request, r.Number, ct)).Id;
        db.WorkRequests.Add(r);
        await mail.RequestSubmittedAsync(db, ctx, r, ct);
        await db.SaveChangesAsync(ct);
        if (r.RequestTypeId is { } typeId && await db.RequestTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct) is { AutoApprove: true } type)
            await AutoApproveAsync(db, ctx, mail, r, type, ct);
        return (v, r);
    }

    /// <summary>A request of an auto-approve type becomes a work order at once: the type's trade, and its team when that team can work at the site.</summary>
    private static async Task AutoApproveAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, WorkRequest r, RequestType type, CancellationToken ct)
    {
        var team = type.TeamId;
        if (team is not null)
        {
            var check = new Validator();
            await TeamEndpoints.CheckForWorkAsync(db, check, team, r.SiteId, null, ct);
            if (!check.IsValid) team = null;
        }
        var input = new ApproveInput(null, null, type.DefaultTradeId, null, null, null, TeamId: team);
        // If the work order can't be made (a trade removed since), the request simply waits for someone to approve it.
        await ApproveCoreAsync(db, ctx, mail, r, input, $"Approved automatically (type: {type.Name})", ct);
    }

    private static async Task<Validator> ValidateAsync(RequestInput i, CmmsDbContext db, AccessService access, CancellationToken ct)
    {
        var v = new Validator().Required("title", i.Title, "What needs to be done", 300).MaxLength("description", i.Description, "Details", 8000)
            .MaxLength("requesterName", i.RequesterName, "Name", 200).MaxLength("requesterPhone", i.RequesterPhone, "Phone", 50)
            .When(!Enum.IsDefined(i.Priority), "priority", "Choose a priority.").When(!Enum.IsDefined(i.Type), "type", "Choose a type.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        Access.RequireLocation(db, v, i.LocationId, i.AssetId);
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        if (i.RequestTypeId is { } typeId && !await db.RequestTypes.AnyAsync(t => t.Id == typeId && t.IsActive, ct)) v.Add("requestTypeId", "Choose a request type.");
        if (AccessWindows.Problem(i.AccessWindows) is { } windows) v.Add("accessWindows", windows);
        var extra = (i.ExtraRecipientIds ?? []).Distinct().ToList();
        if (extra.Count > MaxExtraRecipients) v.Add("extraRecipientIds", $"Choose at most {MaxExtraRecipients} people.");
        else if (extra.Count > 0)
        {
            // Everyone kept informed must be able to see the place (else the e-mail would never reach them).
            var locationId = i.LocationId ?? (i.AssetId is { } a ? await db.Assets.Where(x => x.Id == a).Select(x => x.LocationId).FirstOrDefaultAsync(ct) : null);
            var path = locationId is { } l ? await db.Locations.AllSites().Where(x => x.Id == l).Select(x => x.Path).FirstOrDefaultAsync(ct) : null;
            var active = await db.Users.Where(u => extra.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId).Select(u => u.Id).ToListAsync(ct);
            var ok = active.Count == extra.Count;
            foreach (var id in active) ok &= Access.Covers(await access.GetAsync(id, ct), i.SiteId, path);
            if (!ok) v.Add("extraRecipientIds", "Choose people who can see this place.");
        }
        return v;
    }
}
