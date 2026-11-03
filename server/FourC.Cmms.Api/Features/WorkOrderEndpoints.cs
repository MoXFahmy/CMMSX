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

public static class WorkOrderEndpoints
{
    public sealed record WorkOrderInput(string Title, string? Description, WorkType Type, WorkPriority Priority, Guid SiteId, Guid? LocationId, Guid? AssetId,
        Guid? TradeId, List<Guid>? AssigneeIds, DateOnly? PlannedStart, DateOnly? DueDate, decimal? EstimatedHours, List<string>? Tasks, string? RowVersion = null,
        string? RequestedBy = null);
    public sealed record StatusInput(WorkOrderStatus Status, HoldReason? HoldReason, string? Note, string? CompletionNotes, string? CauseNotes,
        Guid? FailureCodeId, bool? AssetWasDown, decimal? DowntimeHours, string? PreventionNotes = null, DateTimeOffset? ActualStart = null, DateTimeOffset? ActualEnd = null);
    public sealed record TaskInput(string? Description, bool? IsDone);
    public sealed record LaborInput(Guid EmployeeId, DateOnly WorkDate, decimal Hours, decimal OvertimeHours, string? Notes);
    public sealed record CostInput(string Description, decimal Quantity, decimal UnitCost, string? Vendor, string? Reference = null);
    public sealed record CommentInput(string Body);

    public sealed class WorkOrderFilter
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? Type { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? LocationId { get; set; }
        public Guid? AssetId { get; set; }
        public Guid? TradeId { get; set; }
        public Guid? AssigneeId { get; set; }
        public bool? Mine { get; set; }
        public bool? Overdue { get; set; }
        public bool? ActiveOnly { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    private static readonly Dictionary<string, Expression<Func<WorkOrder, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = w => w.Number, ["title"] = w => w.Title, ["status"] = w => w.Status, ["priority"] = w => w.Priority, ["type"] = w => w.Type,
        ["dueDate"] = w => w.DueDate, ["createdAt"] = w => w.CreatedAt, ["site"] = w => w.Site!.Name, ["asset"] = w => w.Asset!.AssetNumber,
        ["completedAt"] = w => w.CompletedAt,
    };

    public sealed record WorkOrderListItem(Guid Id, string Number, string Title, WorkType Type, WorkPriority Priority, WorkOrderStatus Status, HoldReason? HoldReason,
        Guid SiteId, string SiteName, Guid? LocationId, string? LocationPath, Guid? AssetId, string? AssetNumber, string? AssetName, string? TradeName,
        List<string> Assignees, DateOnly? DueDate, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool IsOverdue);

    public static void MapWorkOrderEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/work-orders").WithTags("Work orders");

        g.MapGet("/", async ([AsParameters] WorkOrderFilter f, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var q = await FilterAsync(db, f, ctx, includeStatus: true, ct);
            var today = Today;
            var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "createdAt").Select(w => new
            {
                w.Id, w.Number, w.Title, w.Type, w.Priority, w.Status, w.HoldReason, w.SiteId, SiteName = w.Site!.Name, w.LocationId, LocationPath = w.Location!.Path,
                w.AssetId, AssetNumber = w.Asset!.AssetNumber, AssetName = w.Asset.Name, TradeName = w.Trade!.Name,
                Assignees = w.Assignees.Select(a => a.Employee!.FullName).ToList(), w.DueDate, w.CreatedAt, w.CompletedAt,
            }).ToPagedAsync(f.Page ?? 1, f.PageSize ?? 25, ct);
            var names = await LocationEndpoints.ResolvePathNamesAsync(db, paged.Items.Select(i => i.LocationPath), ct);
            return new PagedResult<WorkOrderListItem>(paged.Items.Select(w => new WorkOrderListItem(w.Id, w.Number, w.Title, w.Type, w.Priority, w.Status, w.HoldReason,
                w.SiteId, w.SiteName, w.LocationId, w.LocationPath is null ? null : names(w.LocationPath), w.AssetId, w.AssetNumber, w.AssetName, w.TradeName,
                w.Assignees, w.DueDate, w.CreatedAt, w.CompletedAt, w.DueDate < today && WorkOrderWorkflow.IsActive(w.Status))).ToList(), paged.Total, paged.Page, paged.PageSize);
        }).RequirePermission(Permissions.WorkOrdersView);

        // Counts per status for the list tabs (same filters, ignoring status).
        g.MapGet("/summary", async ([AsParameters] WorkOrderFilter f, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var q = await FilterAsync(db, f, ctx, includeStatus: false, ct);
            var today = Today;
            var byStatus = await q.GroupBy(w => w.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var overdue = await q.CountAsync(w => w.DueDate < today && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold), ct);
            return new { byStatus, overdue };
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.AsNoTracking().Include(x => x.Site).Include(x => x.Location).Include(x => x.Asset).Include(x => x.Trade).Include(x => x.FailureCode)
                .Include(x => x.Assignees).ThenInclude(a => a.Employee).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            var trail = new List<object>();
            if (w.Location is not null)
            {
                var ids = LocationEndpoints.PathIds(w.Location.Path).ToList();
                trail.AddRange((await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.Name, l.Depth, l.NotesToTechnician }).ToListAsync(ct))
                    .OrderBy(l => l.Depth).Select(l => new { l.Id, l.Name }));
            }
            var tasks = await db.WorkOrderTasks.AsNoTracking().Where(t => t.WorkOrderId == id).OrderBy(t => t.Sequence).ToListAsync(ct);
            var labor = await db.WorkOrderLabor.AsNoTracking().Where(l => l.WorkOrderId == id).OrderByDescending(l => l.WorkDate).ThenByDescending(l => l.CreatedAt)
                .Select(l => new { l.Id, l.EmployeeId, EmployeeName = l.Employee!.FullName, l.WorkDate, l.CreatedAt, l.Hours, l.OvertimeHours, l.Rate, l.OvertimeRate, l.Notes,
                    Cost = l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate }).ToListAsync(ct);
            var costs = await db.WorkOrderCosts.AsNoTracking().Where(c => c.WorkOrderId == id).OrderBy(c => c.CreatedAt)
                .Select(c => new { c.Id, c.Description, c.Quantity, c.UnitCost, c.Vendor, c.Reference, Total = c.Quantity * c.UnitCost, c.CreatedAt }).ToListAsync(ct);
            var request = w.WorkRequestId is { } rid ? await db.WorkRequests.AsNoTracking().Where(r => r.Id == rid).Select(r => new { r.Id, r.Number, r.RequesterName }).FirstOrDefaultAsync(ct) : null;
            var mine = await perms.GetAsync(UserId(http));
            var transitions = WorkOrderWorkflow.NextStatuses(w.Status)
                .Where(s => mine.Contains(Permissions.WorkOrdersExecute) && (!WorkOrderWorkflow.RequiresClosePermission(w.Status, s) || mine.Contains(Permissions.WorkOrdersClose)));
            var locationNotes = w.Location?.NotesToTechnician;
            var pm = w.PmScheduleId is { } pmId ? await db.PmSchedules.AsNoTracking().Where(p => p.Id == pmId).Select(p => new { p.Id, p.Code, p.Name }).FirstOrDefaultAsync(ct) : null;
            return Results.Ok(new
            {
                w.Id, w.Number, w.Title, w.Description, w.Type, w.Priority, w.Status, w.HoldReason,
                w.SiteId, Site = new { w.Site!.Id, w.Site.Name }, w.LocationId, LocationTrail = trail,
                w.AssetId, Asset = w.Asset is null ? null : new { w.Asset.Id, w.Asset.AssetNumber, w.Asset.Name, w.Asset.Status, w.Asset.Criticality, w.Asset.NotesToTechnician,
                    w.Asset.WarrantyType, w.Asset.WarrantyExpiresOn, UnderWarranty = w.Asset.WarrantyType != FourC.Cmms.Core.Assets.WarrantyType.None && w.Asset.WarrantyExpiresOn >= Today },
                w.RequestedBy, w.PreventionNotes,
                w.TradeId, TradeName = w.Trade?.Name, w.FailureCodeId, FailureCode = w.FailureCode is null ? null : new { w.FailureCode.Code, w.FailureCode.Name },
                Assignees = w.Assignees.Select(a => new { a.EmployeeId, a.Employee!.FullName, a.Employee.Code }),
                w.PlannedStart, w.DueDate, w.EstimatedHours, w.StartedAt, w.CompletedAt, w.ClosedAt, w.CompletionNotes, w.CauseNotes, w.AssetWasDown, w.DowntimeHours,
                LocationNotesToTechnician = locationNotes, Request = request, Pm = pm, w.PmDueDate,
                Tasks = tasks.Select(t => new { t.Id, t.Sequence, t.Description, t.IsDone, t.DoneAt, t.DoneBy }),
                Labor = labor, Costs = costs,
                Totals = new
                {
                    LaborHours = labor.Sum(l => l.Hours + l.OvertimeHours), LaborCost = labor.Sum(l => l.Cost), OtherCost = costs.Sum(c => c.Total),
                    TotalCost = labor.Sum(l => l.Cost) + costs.Sum(c => c.Total),
                },
                AllowedTransitions = transitions, IsOverdue = w.DueDate < Today && WorkOrderWorkflow.IsActive(w.Status),
                w.CreatedAt, w.UpdatedAt, w.LegacyRef, RowVersion = Convert.ToBase64String(w.RowVersion),
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/", async (WorkOrderInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, db, ct);
            if (!v.IsValid) return v.Problem();
            var w = await CreateAsync(db, ctx, input, requestId: null, ct);
            return Results.Created($"/api/work-orders/{w.Id}", new { w.Id, w.Number });
        }).RequirePermission(Permissions.WorkOrdersCreate);

        g.MapPut("/{id:guid}", async (Guid id, WorkOrderInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            if (w.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled) throw new DomainException("Reopen the work order before editing it.");
            var v = await ValidateAsync(input, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(w).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            Apply(w, input);
            SyncAssignees(w, input.AssigneeIds);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { w.Id });
        }).RequirePermission(Permissions.WorkOrdersEdit);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            if (await db.WorkOrderLabor.AnyAsync(l => l.WorkOrderId == id, ct)) throw new DomainException("Work with logged labour cannot be deleted. Cancel it instead.");
            db.WorkOrders.Remove(w);
            await db.WorkRequests.Where(r => r.WorkOrderId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.WorkOrderId, (Guid?)null).SetProperty(r => r.Status, WorkRequestStatus.New), ct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersDelete);

        g.MapPost("/{id:guid}/status", async (Guid id, StatusInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            if (!WorkOrderWorkflow.CanTransition(w.Status, input.Status)) throw new DomainException($"A work order cannot go from {w.Status} to {input.Status}.");
            if (WorkOrderWorkflow.RequiresClosePermission(w.Status, input.Status) && !(await perms.GetAsync(UserId(http))).Contains(Permissions.WorkOrdersClose))
                return Results.Forbid();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var v = new Validator().MaxLength("note", input.Note, "Note", 2000)
                .When(input.Status == WorkOrderStatus.OnHold && input.HoldReason is null, "holdReason", "Choose why the work is on hold.")
                .When(input.DowntimeHours < 0, "downtimeHours", "Downtime cannot be negative.");
            if (input.Status == WorkOrderStatus.Completed)
            {
                var notes = input.CompletionNotes.Clean() ?? w.CompletionNotes;
                if (settings.RequireCompletionNotes && notes is null) v.Add("completionNotes", "Describe the work that was done.");
                if (input.FailureCodeId is { } fc && !await db.FailureCodes.AnyAsync(f => f.Id == fc, ct)) v.Add("failureCodeId", "Unknown failure code.");
                if (input.ActualStart is { } st && input.ActualEnd is { } en && en < st) v.Add("actualEnd", "Actual end cannot be before the actual start.");
                if (input.ActualEnd > DateTimeOffset.UtcNow.AddHours(1)) v.Add("actualEnd", "Actual end cannot be in the future.");
            }
            if (!v.IsValid) return v.Problem();

            var now = DateTimeOffset.UtcNow;
            db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange
            {
                WorkOrderId = id, FromStatus = w.Status, ToStatus = input.Status, HoldReason = input.Status == WorkOrderStatus.OnHold ? input.HoldReason : null,
                Note = input.Note.Clean(), ChangedAt = now, ChangedById = ctx.UserId, ChangedByName = ctx.UserName,
            });
            w.Status = input.Status;
            w.HoldReason = input.Status == WorkOrderStatus.OnHold ? input.HoldReason : null;
            switch (input.Status)
            {
                case WorkOrderStatus.InProgress: w.StartedAt ??= now; w.CompletedAt = null; w.ClosedAt = null; break;
                case WorkOrderStatus.Completed:
                    // Like 4CSQL's "Calculate Dates": default actual start to when work started, else the earliest labour entry.
                    var firstLabor = await db.WorkOrderLabor.Where(l => l.WorkOrderId == id).OrderBy(l => l.WorkDate).Select(l => (DateOnly?)l.WorkDate).FirstOrDefaultAsync(ct);
                    w.StartedAt = input.ActualStart ?? w.StartedAt ?? (firstLabor is { } fl ? new DateTimeOffset(fl.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : now);
                    w.CompletedAt = input.ActualEnd ?? now;
                    w.PreventionNotes = input.PreventionNotes.Clean() ?? w.PreventionNotes;
                    w.CompletionNotes = input.CompletionNotes.Clean() ?? w.CompletionNotes;
                    w.CauseNotes = input.CauseNotes.Clean() ?? w.CauseNotes;
                    w.FailureCodeId = input.FailureCodeId ?? w.FailureCodeId;
                    w.AssetWasDown = input.AssetWasDown ?? w.AssetWasDown;
                    w.DowntimeHours = input.DowntimeHours ?? w.DowntimeHours;
                    break;
                case WorkOrderStatus.Closed: w.ClosedAt = now; w.CompletedAt ??= now; break;
                case WorkOrderStatus.Open: w.CompletedAt = null; w.ClosedAt = null; break;
            }
            if (w.PmScheduleId is not null && input.Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
                await PmGenerator.OnWorkOrderFinishedAsync(db, w, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { w.Id, w.Status });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // "Check Warranty/WO" (4CSQL): warranty status plus open requests and work on an asset, to avoid duplicate jobs.
        api.MapGet("/assets/{assetId:guid}/open-work", async (Guid assetId, CmmsDbContext db, CancellationToken ct) =>
        {
            var a = await db.Assets.AsNoTracking().Where(x => x.Id == assetId).Select(x => new { x.Id, x.Status, x.WarrantyType, x.WarrantyExpiresOn }).FirstOrDefaultAsync(ct);
            if (a is null) return Results.NotFound();
            var orders = await db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold))
                .OrderByDescending(w => w.CreatedAt).Take(10).Select(w => new { w.Id, w.Number, w.Title, w.Status, w.Priority }).ToListAsync(ct);
            var requests = await db.WorkRequests.AsNoTracking().Where(r => r.AssetId == assetId && r.Status == WorkRequestStatus.New)
                .OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new { r.Id, r.Number, r.Title }).ToListAsync(ct);
            return Results.Ok(new
            {
                a.Status, a.WarrantyType, a.WarrantyExpiresOn,
                UnderWarranty = a.WarrantyType != FourC.Cmms.Core.Assets.WarrantyType.None && a.WarrantyExpiresOn >= Today, OpenWorkOrders = orders, OpenRequests = requests,
            });
        }).WithTags("Work orders").RequireAuthorization();

        // ---- Tasks ----
        g.MapPost("/{id:guid}/tasks", async (Guid id, TaskInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var v = new Validator().Required("description", input.Description, "Task", 1000);
            if (!v.IsValid) return v.Problem();
            var seq = (await db.WorkOrderTasks.Where(t => t.WorkOrderId == id).MaxAsync(t => (int?)t.Sequence, ct) ?? 0) + 1;
            var task = new WorkOrderTask { WorkOrderId = id, Sequence = seq, Description = input.Description!.Trim() };
            db.WorkOrderTasks.Add(task);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { task.Id });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        g.MapPut("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, TaskInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var t = await db.WorkOrderTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.WorkOrderId == id, ct);
            if (t is null) return Results.NotFound();
            if (input.Description.Clean() is { } d) t.Description = d.Length > 1000 ? d[..1000] : d;
            if (input.IsDone is { } done && done != t.IsDone)
            {
                t.IsDone = done; t.DoneAt = done ? DateTimeOffset.UtcNow : null; t.DoneBy = done ? ctx.UserName : null;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id, t.IsDone });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        g.MapDelete("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            await db.WorkOrderTasks.Where(x => x.Id == taskId && x.WorkOrderId == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // ---- Labour ----
        g.MapPost("/{id:guid}/labor", async (Guid id, LaborInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var (v, emp) = await ValidateLaborAsync(input, db, ct);
            if (!v.IsValid) return v.Problem();
            var l = new WorkOrderLabor { WorkOrderId = id, EmployeeId = emp!.Id, WorkDate = input.WorkDate, Hours = input.Hours, OvertimeHours = input.OvertimeHours,
                Rate = emp.HourlyRate ?? 0, OvertimeRate = emp.OvertimeRate ?? emp.HourlyRate ?? 0, Notes = input.Notes.Clean() };
            db.WorkOrderLabor.Add(l);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { l.Id });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        g.MapPut("/{id:guid}/labor/{laborId:guid}", async (Guid id, Guid laborId, LaborInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var l = await db.WorkOrderLabor.FirstOrDefaultAsync(x => x.Id == laborId && x.WorkOrderId == id, ct);
            if (l is null) return Results.NotFound();
            var (v, emp) = await ValidateLaborAsync(input, db, ct);
            if (!v.IsValid) return v.Problem();
            if (l.EmployeeId != emp!.Id) { l.EmployeeId = emp.Id; l.Rate = emp.HourlyRate ?? 0; l.OvertimeRate = emp.OvertimeRate ?? emp.HourlyRate ?? 0; }
            l.WorkDate = input.WorkDate; l.Hours = input.Hours; l.OvertimeHours = input.OvertimeHours; l.Notes = input.Notes.Clean();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { l.Id });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        g.MapDelete("/{id:guid}/labor/{laborId:guid}", async (Guid id, Guid laborId, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            await db.WorkOrderLabor.Where(x => x.Id == laborId && x.WorkOrderId == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // ---- Other costs ----
        g.MapPost("/{id:guid}/costs", async (Guid id, CostInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var v = ValidateCost(input);
            if (!v.IsValid) return v.Problem();
            var c = new WorkOrderCost { WorkOrderId = id, Description = input.Description.Trim(), Quantity = input.Quantity, UnitCost = input.UnitCost, Vendor = input.Vendor.Clean(), Reference = input.Reference.Clean() };
            db.WorkOrderCosts.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        g.MapDelete("/{id:guid}/costs/{costId:guid}", async (Guid id, Guid costId, CmmsDbContext db, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            await db.WorkOrderCosts.Where(x => x.Id == costId && x.WorkOrderId == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // ---- Activity: comments + status changes ----
        g.MapGet("/{id:guid}/activity", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var comments = await db.WorkOrderComments.AsNoTracking().Where(c => c.WorkOrderId == id)
                .Select(c => new { c.Id, Kind = "comment", c.Body, From = (WorkOrderStatus?)null, To = (WorkOrderStatus?)null, HoldReason = (HoldReason?)null, By = c.UserName, At = c.CreatedAt }).ToListAsync(ct);
            var changes = await db.WorkOrderStatusChanges.AsNoTracking().Where(c => c.WorkOrderId == id)
                .Select(c => new { c.Id, Kind = "status", Body = c.Note, From = c.FromStatus, To = (WorkOrderStatus?)c.ToStatus, c.HoldReason, By = c.ChangedByName, At = c.ChangedAt }).ToListAsync(ct);
            return comments.Concat(changes).OrderByDescending(x => x.At);
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/{id:guid}/comments", async (Guid id, CommentInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct)) return Results.NotFound();
            var v = new Validator().Required("body", input.Body, "Comment", 4000);
            if (!v.IsValid) return v.Problem();
            db.WorkOrderComments.Add(new WorkOrderComment { WorkOrderId = id, Body = input.Body.Trim(), UserId = ctx.UserId, UserName = ctx.UserName, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).RequirePermission(Permissions.WorkOrdersView);
    }

    // ---------------- helpers ----------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static List<T> ParseEnums<T>(string? csv) where T : struct, Enum =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Enum.TryParse<T>(x, true, out var e) ? (T?)e : null).OfType<T>().ToList();

    private static async Task<IQueryable<WorkOrder>> FilterAsync(CmmsDbContext db, WorkOrderFilter f, ITenantContext ctx, bool includeStatus, CancellationToken ct)
    {
        var q = db.WorkOrders.AsNoTracking();
        if (f.Search.Clean() is { } s) q = q.Where(w => w.Number.Contains(s) || w.Title.Contains(s) || w.Asset!.AssetNumber.Contains(s) || w.Asset.Name.Contains(s));
        if (includeStatus && ParseEnums<WorkOrderStatus>(f.Status) is { Count: > 0 } st) q = q.Where(w => st.Contains(w.Status));
        if (f.ActiveOnly == true) q = q.Where(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold);
        if (ParseEnums<WorkPriority>(f.Priority) is { Count: > 0 } pr) q = q.Where(w => pr.Contains(w.Priority));
        if (ParseEnums<WorkType>(f.Type) is { Count: > 0 } ty) q = q.Where(w => ty.Contains(w.Type));
        if (f.SiteId is not null) q = q.Where(w => w.SiteId == f.SiteId);
        if (f.LocationId is not null)
        {
            var path = await db.Locations.Where(l => l.Id == f.LocationId).Select(l => l.Path).FirstOrDefaultAsync(ct) ?? "/none/";
            q = q.Where(w => w.LocationId != null && db.Locations.Any(l => l.Id == w.LocationId && l.Path.StartsWith(path)));
        }
        if (f.AssetId is not null) q = q.Where(w => w.AssetId == f.AssetId || (w.Asset != null && w.Asset.ParentAssetId == f.AssetId));
        if (f.TradeId is not null) q = q.Where(w => w.TradeId == f.TradeId);
        if (f.AssigneeId is not null) q = q.Where(w => w.Assignees.Any(a => a.EmployeeId == f.AssigneeId));
        if (f.Mine == true)
        {
            var me = await db.Employees.Where(e => e.UserId == ctx.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(w => me != null && w.Assignees.Any(a => a.EmployeeId == me));
        }
        if (f.Overdue == true)
        {
            var today = Today;
            q = q.Where(w => w.DueDate < today && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold));
        }
        return q;
    }

    public static async Task<Validator> ValidateAsync(WorkOrderInput i, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("title", i.Title, "Title", 300)
            .When(!Enum.IsDefined(i.Type), "type", "Choose a work type.").When(!Enum.IsDefined(i.Priority), "priority", "Choose a priority.")
            .When(i.EstimatedHours < 0, "estimatedHours", "Estimate cannot be negative.")
            .When(i.PlannedStart is not null && i.DueDate is not null && i.DueDate < i.PlannedStart, "dueDate", "Due date cannot be before the planned start.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(t => t.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        var who = (i.AssigneeIds ?? []).Distinct().ToList();
        if (who.Count > 0 && await db.Employees.CountAsync(e => who.Contains(e.Id), ct) != who.Count) v.Add("assigneeIds", "Unknown person.");
        return v;
    }

    public static async Task<WorkOrder> CreateAsync(CmmsDbContext db, ITenantContext ctx, WorkOrderInput input, Guid? requestId, CancellationToken ct,
        Action<WorkOrder>? configure = null, string? note = null)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var number = await Numbering.NextAsync(db, "WorkOrder", settings.WorkOrderNumberPrefix, 5,
            n => db.WorkOrders.AnyAsync(w => w.Number == n, ct), p => db.WorkOrders.Where(w => w.Number.StartsWith(p)).Select(w => w.Number).ToListAsync(ct), ct);
        var w = new WorkOrder { Number = number, Title = input.Title.Trim(), SiteId = input.SiteId, WorkRequestId = requestId };
        // Default the location from the asset when only the asset was chosen.
        if (input.AssetId is not null && input.LocationId is null)
            input = input with { LocationId = await db.Assets.Where(a => a.Id == input.AssetId).Select(a => a.LocationId).FirstOrDefaultAsync(ct) };
        Apply(w, input);
        SyncAssignees(w, input.AssigneeIds);
        configure?.Invoke(w);
        db.WorkOrders.Add(w);
        var seq = 0;
        foreach (var t in (input.Tasks ?? []).Select(x => x.Clean()).OfType<string>())
            db.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderId = w.Id, Sequence = ++seq, Description = t.Length > 1000 ? t[..1000] : t });
        db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange { WorkOrderId = w.Id, ToStatus = WorkOrderStatus.Open, ChangedAt = DateTimeOffset.UtcNow,
            ChangedById = ctx.UserId, ChangedByName = ctx.UserName, Note = note ?? (requestId is null ? null : "Created from work request") });
        await db.SaveChangesAsync(ct);
        return w;
    }

    private static void Apply(WorkOrder w, WorkOrderInput i)
    {
        w.Title = i.Title.Trim(); w.Description = i.Description.Clean(); w.Type = i.Type; w.Priority = i.Priority; w.SiteId = i.SiteId; w.LocationId = i.LocationId;
        w.AssetId = i.AssetId; w.TradeId = i.TradeId; w.PlannedStart = i.PlannedStart; w.DueDate = i.DueDate; w.EstimatedHours = i.EstimatedHours;
        w.RequestedBy = i.RequestedBy.Clean() is { } rb && rb.Length > 200 ? rb[..200] : i.RequestedBy.Clean();
    }

    private static void SyncAssignees(WorkOrder w, List<Guid>? ids)
    {
        var want = (ids ?? []).Distinct().ToHashSet();
        w.Assignees.RemoveAll(a => !want.Contains(a.EmployeeId));
        foreach (var id in want.Where(id => w.Assignees.All(a => a.EmployeeId != id))) w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = id });
    }

    private static async Task RequireOpenAsync(CmmsDbContext db, Guid id, CancellationToken ct)
    {
        var status = await db.WorkOrders.Where(w => w.Id == id).Select(w => (WorkOrderStatus?)w.Status).FirstOrDefaultAsync(ct)
                     ?? throw new BadHttpRequestException("Work order not found.", 404);
        if (status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled) throw new DomainException("This work order is closed. Reopen it to make changes.");
    }

    private static async Task<(Validator, FourC.Cmms.Core.People.Employee?)> ValidateLaborAsync(LaborInput i, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().When(i.Hours < 0 || i.OvertimeHours < 0, "hours", "Hours cannot be negative.")
            .When(i.Hours + i.OvertimeHours <= 0, "hours", "Enter the hours worked.").When(i.Hours + i.OvertimeHours > 24, "hours", "More than 24 hours in one entry.")
            .When(i.WorkDate > Today.AddDays(1), "workDate", "Work date cannot be in the future.").MaxLength("notes", i.Notes, "Notes", 2000);
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == i.EmployeeId, ct);
        if (emp is null) v.Add("employeeId", "Choose who did the work.");
        return (v, emp);
    }

    private static Validator ValidateCost(CostInput i) => new Validator().Required("description", i.Description, "Description", 500)
        .MaxLength("vendor", i.Vendor, "Vendor", 200).MaxLength("reference", i.Reference, "Reference", 100).When(i.Quantity <= 0, "quantity", "Quantity must be positive.").When(i.UnitCost < 0, "unitCost", "Cost cannot be negative.");
}
