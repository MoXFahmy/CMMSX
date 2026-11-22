using System.Linq.Expressions;
using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class WorkOrderEndpoints
{
    public sealed record WorkOrderInput(string Title, string? Description, WorkType Type, WorkPriority Priority, Guid SiteId, Guid? LocationId, Guid? AssetId,
        Guid? TradeId, List<Guid>? AssigneeIds, DateOnly? PlannedStart, DateOnly? DueDate, decimal? EstimatedHours, List<string>? Tasks, string? RowVersion = null,
        string? RequestedBy = null, Guid? CopyPlanFromId = null, DateTimeOffset? DueAt = null, bool? AssetIsDown = null);
    public sealed record StatusInput(WorkOrderStatus Status, HoldReason? HoldReason, string? Note, string? CompletionNotes, string? CauseNotes,
        Guid? FailureCodeId, bool? AssetWasDown, decimal? DowntimeHours, string? PreventionNotes = null, DateTimeOffset? ActualStart = null, DateTimeOffset? ActualEnd = null,
        DateTimeOffset? At = null);
    public sealed record BulkInput(Guid[]? Ids, StatusInput? Status, WorkPriority? Priority, Guid[]? AddAssigneeIds);
    public sealed record TaskInput(string? Description, bool? IsDone, string? Reading = null, string? Remarks = null);
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
        /// <summary>Old name of <see cref="Late"/> (phones and bookmarks).</summary>
        public bool? Overdue { get; set; }
        /// <summary>Past the due time, or past the start-by with nobody having acted on it.</summary>
        public bool? Late { get; set; }
        /// <summary>Past the start-by with nobody having acted on it.</summary>
        public bool? ResponseLate { get; set; }
        /// <summary>Open and due soon (less than a quarter of the target, or 2 hours, left).</summary>
        public bool? DueSoon { get; set; }
        public bool? ActiveOnly { get; set; }
        public DateOnly? CreatedFrom { get; set; }
        public DateOnly? CreatedTo { get; set; }
        public DateOnly? DueFrom { get; set; }
        public DateOnly? DueTo { get; set; }
        /// <summary>"pm", "request" or "direct" (raised without a PM or request); anything else is ignored.</summary>
        public string? Source { get; set; }
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
        List<string> Assignees, DateOnly? DueDate, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool IsOverdue,
        DateTimeOffset? DueAt, DateTimeOffset? StartBy, DateTimeOffset? RespondedAt, SlaState ResponseState, SlaState CompletionState);

    public static void MapWorkOrderEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/work-orders").WithTags("Work orders");

        g.MapGet("/", ([AsParameters] WorkOrderFilter f, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            ListAsync(db, f, ctx, f.Page ?? 1, f.PageSize ?? 25, ct)).RequirePermission(Permissions.WorkOrdersView);

        g.MapGet("/export", ([AsParameters] WorkOrderFilter f, HttpContext http, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "work-orders", (page, size) => ListAsync(db, f, ctx, page, size, ct, ListExport.MaxRows), ct))
            .RequirePermission(Permissions.WorkOrdersView);

        // Counts per status for the list tabs (same filters, ignoring status).
        g.MapGet("/summary", async ([AsParameters] WorkOrderFilter f, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var q = await FilterAsync(db, f, ctx, includeStatus: false, ct);
            var byStatus = await q.GroupBy(w => w.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var overdue = await SlaQueries.LateWork(q, DateTimeOffset.UtcNow).CountAsync(ct);
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
            var roundAssets = await db.WorkOrderAssets.AsNoTracking().Where(a => a.WorkOrderId == id && a.Asset != null).OrderBy(a => a.Sequence)
                .Select(a => new { a.Asset!.Id, a.Asset.AssetNumber, a.Asset.Name }).ToListAsync(ct);
            var labor = await db.WorkOrderLabor.AsNoTracking().Where(l => l.WorkOrderId == id).OrderByDescending(l => l.WorkDate).ThenByDescending(l => l.CreatedAt)
                .Select(l => new { l.Id, l.EmployeeId, EmployeeName = l.Employee!.FullName, l.WorkDate, l.CreatedAt, l.Hours, l.OvertimeHours, l.Rate, l.OvertimeRate, l.Notes,
                    Cost = l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate }).ToListAsync(ct);
            var costs = await db.WorkOrderCosts.AsNoTracking().Where(c => c.WorkOrderId == id).OrderBy(c => c.CreatedAt)
                .Select(c => new { c.Id, c.Description, c.Quantity, c.UnitCost, c.Vendor, c.Reference, Total = c.Quantity * c.UnitCost, c.CreatedAt }).ToListAsync(ct);
            var parts = await db.StockTransactions.AsNoTracking().Where(t => t.WorkOrderId == id && (t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Issue || t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Return))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Type, t.Date, t.PartId, t.Part!.PartNumber, PartName = t.Part.Name, t.Part.UnitOfMeasure, t.StoreId, StoreCode = t.Store!.Code,
                    Quantity = -t.Quantity, t.UnitCost, Total = Math.Round(-t.Quantity * t.UnitCost, 2), t.BatchNumber, t.Notes, t.CreatedByName }).ToListAsync(ct);
            var request = w.WorkRequestId is { } rid ? await db.WorkRequests.AsNoTracking().Where(r => r.Id == rid).Select(r => new { r.Id, r.Number, r.RequesterName, r.RespondBy, r.DecidedAt }).FirstOrDefaultAsync(ct) : null;
            var downtime = w.DowntimeEventId is { } did ? await db.DowntimeEvents.AsNoTracking().Where(e => e.Id == did)
                .Select(e => new { e.Id, e.StartedAt, e.EndedAt, e.Source, e.SourceNumber }).FirstOrDefaultAsync(ct) : null;
            var assetDownSince = w.AssetId is { } aid ? await db.DowntimeEvents.AsNoTracking().Where(e => e.AssetId == aid && e.EndedAt == null)
                .Select(e => (DateTimeOffset?)e.StartedAt).FirstOrDefaultAsync(ct) : null;
            var settings = await SlaQueries.SettingsAsync(db, ct);
            var now = DateTimeOffset.UtcNow;
            var sla = SlaQueries.Work(settings, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt, now);
            // Raised as a request: its response was the request's decision.
            if (request is not null) sla = sla with
            {
                Response = Sla.State(request.RespondBy, request.DecidedAt, now, Sla.For(settings, w.Priority)?.RespondMinutes is { } m ? TimeSpan.FromMinutes(m) : null),
                RespondBy = request.RespondBy, RespondedAt = request.DecidedAt,
            };
            var mine = await perms.GetAsync(UserId(http));
            var transitions = WorkOrderWorkflow.NextStatuses(w.Status)
                .Where(s => mine.Contains(Permissions.WorkOrdersExecute) && (!WorkOrderWorkflow.RequiresClosePermission(w.Status, s) || mine.Contains(Permissions.WorkOrdersClose)));
            var locationNotes = w.Location?.NotesToTechnician;
            // Signatures captured on completion (technician view), for the work order page and the job card.
            var signatures = await db.Attachments.AsNoTracking().Where(a => a.EntityType == "WorkOrder" && a.EntityId == id && a.IsSignature).OrderBy(a => a.CreatedAt)
                .Select(a => new { a.Id, a.SignedBy, a.CreatedAt }).ToListAsync(ct);
            var planned = await Planning.LinesAsync(db, w, ct);
            var labour = Planning.Labour(w.Assignees.Select(a => (a.PlannedHours, a.Employee?.HourlyRate)).ToList(), w.EstimatedHours);
            var plannedParts = planned.Sum(p => p.PlannedCost);
            var pm = w.PmScheduleId is { } pmId ? await db.PmSchedules.AsNoTracking().Where(p => p.Id == pmId).Select(p => new { p.Id, p.Code, p.Name }).FirstOrDefaultAsync(ct) : null;
            return Results.Ok(new
            {
                w.Id, w.Number, w.Title, w.Description, w.Type, w.Priority, w.Status, w.HoldReason,
                w.SiteId, Site = new { w.Site!.Id, w.Site.Name }, w.LocationId, LocationTrail = trail,
                w.AssetId, Asset = w.Asset is null ? null : new { w.Asset.Id, w.Asset.AssetNumber, w.Asset.Name, w.Asset.SerialNumber, w.Asset.Status, w.Asset.Criticality, w.Asset.NotesToTechnician,
                    w.Asset.WarrantyType, w.Asset.WarrantyExpiresOn, UnderWarranty = w.Asset.WarrantyType != FourC.Cmms.Core.Assets.WarrantyType.None && w.Asset.WarrantyExpiresOn >= Today },
                w.RequestedBy, w.PreventionNotes,
                w.TradeId, TradeName = w.Trade?.Name, w.FailureCodeId, FailureCode = w.FailureCode is null ? null : new { w.FailureCode.Code, w.FailureCode.Name },
                Assignees = w.Assignees.Select(a => new { a.EmployeeId, a.Employee!.FullName, a.Employee.Code, a.PlannedHours, a.Employee.HourlyRate }),
                w.PlannedStart, w.DueDate, w.DueAt, w.DueFromTarget, w.StartBy, w.RespondedAt, w.EstimatedHours, w.StartedAt, w.CompletedAt, w.ClosedAt, w.CompletionNotes, w.CauseNotes, w.AssetWasDown, w.DowntimeHours,
                Downtime = downtime, AssetDownSince = assetDownSince,
                LocationNotesToTechnician = locationNotes, Request = request, Pm = pm, w.PmDueDate,
                Assets = roundAssets,
                Tasks = tasks.Select(t => new { t.Id, t.Sequence, t.Description, t.IsDone, t.DoneAt, t.DoneBy, t.AssetId, t.Reading, t.Remarks, t.UpdatedAt, t.UpdatedByName }),
                Labor = labor, Costs = costs, Parts = parts, PlannedParts = planned, Signatures = signatures,
                Planned = new { LaborHours = labour.Hours, LaborCost = labour.Cost, PartsCost = plannedParts, TotalCost = labour.Cost + plannedParts },
                Totals = new
                {
                    LaborHours = labor.Sum(l => l.Hours + l.OvertimeHours), LaborCost = labor.Sum(l => l.Cost), PartsCost = parts.Sum(p => p.Total), OtherCost = costs.Sum(c => c.Total),
                    TotalCost = labor.Sum(l => l.Cost) + parts.Sum(p => p.Total) + costs.Sum(c => c.Total),
                },
                AllowedTransitions = transitions, IsOverdue = sla.Completion == SlaState.Late || sla.Response == SlaState.Late, Sla = sla,
                w.CreatedAt, w.UpdatedAt, w.LegacyRef, RowVersion = Convert.ToBase64String(w.RowVersion),
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/", async (WorkOrderInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, db, ct);
            if (input.CopyPlanFromId is { } from && !await db.WorkOrders.AnyAsync(x => x.Id == from, ct)) v.Add("copyPlanFromId", "The work order to copy was not found.");
            if (!v.IsValid) return v.Problem();
            var w = await CreateAsync(db, ctx, input, requestId: null, ct, mail: mail);
            if (input.AssetIsDown == true && w.AssetId is { } downAsset)
            {
                // "The asset is down": a stop from when the work order was raised, or the asset's open stop.
                w.DowntimeEventId = (await Downtime.StartOrJoinAsync(db, downAsset, w.CreatedAt, w.Title, FourC.Cmms.Core.Assets.DowntimeSource.WorkOrder, w.Number, ct)).Id;
                await db.SaveChangesAsync(ct);
            }
            if (input.CopyPlanFromId is { } source)
            {
                // Duplicate: the plan comes along (planned parts with stores re-picked, planned hours of technicians on both).
                await Planning.CopyPlanAsync(db, source, w, ct);
                await db.SaveChangesAsync(ct);
            }
            return Results.Created($"/api/work-orders/{w.Id}", new { w.Id, w.Number });
        }).RequirePermission(Permissions.WorkOrdersCreate);

        g.MapPut("/{id:guid}", async (Guid id, WorkOrderInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var w = await db.WorkOrders.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            if (w.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled) throw new DomainException("Reopen the work order before editing it.");
            var v = await ValidateAsync(input, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(w).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            var priorityBefore = w.Priority;
            var assetBefore = w.AssetId;
            Apply(w, input);
            if (w.AssetId != assetBefore) await Downtime.FollowAssetAsync(db, w, ct); // its own stop goes with it; any other stays behind
            if (input.AssetIsDown == false) await Downtime.UntickAsync(db, w, ct);
            if (input.AssetIsDown == true && w.DowntimeEventId is null && w.AssetId is { } downAsset)
                w.DowntimeEventId = (await Downtime.StartOrJoinAsync(db, downAsset, DateTimeOffset.UtcNow, w.Title, FourC.Cmms.Core.Assets.DowntimeSource.WorkOrder, w.Number, ct)).Id;
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var raisedAt = w.WorkRequestId is { } rid ? await db.WorkRequests.Where(r => r.Id == rid).Select(r => (DateTimeOffset?)r.CreatedAt).FirstOrDefaultAsync(ct) ?? w.CreatedAt : w.CreatedAt;
            Deadlines.OnUpdate(w, input.DueAt, input.DueDate, priorityBefore, settings, raisedAt);
            var before = w.Assignees.Select(a => a.EmployeeId).ToHashSet();
            SyncAssignees(w, input.AssigneeIds);
            await mail.AssigneesAddedAsync(db, ctx, w, w.Assignees.Select(a => a.EmployeeId).Where(e => !before.Contains(e)).ToList(), ct);
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

        g.MapPost("/{id:guid}/status", async (Guid id, StatusInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms, EmailOutbox mail,
            CancellationToken ct) =>
        {
            var w = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            if (!WorkOrderWorkflow.CanTransition(w.Status, input.Status)) throw new DomainException($"A work order cannot go from {w.Status} to {input.Status}.");
            if (WorkOrderWorkflow.RequiresClosePermission(w.Status, input.Status) && !(await perms.GetAsync(UserId(http))).Contains(Permissions.WorkOrdersClose))
                return Results.Forbid();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var v = await ChangeStatusAsync(db, ctx, mail, settings, w, input, ct);
            if (!v.IsValid) return v.Problem();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { w.Id, w.Status });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // Many work orders at once: one change (status, priority or more technicians), the same rules as one at a time.
        // Rows that can't change are skipped with the reason; the rest are saved together.
        g.MapPost("/bulk", async (BulkInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms, EmailOutbox mail,
            CancellationToken ct) =>
        {
            var ids = Bulk.Ids(input.Ids, "work orders");
            var add = (input.AddAssigneeIds ?? []).Distinct().ToList();
            if ((input.Status is null ? 0 : 1) + (input.Priority is null ? 0 : 1) + (add.Count > 0 ? 1 : 0) != 1) throw new DomainException("Choose one change to make.");
            var mine = await perms.GetAsync(UserId(http));
            if (!mine.Contains(input.Status is null ? Permissions.WorkOrdersEdit : Permissions.WorkOrdersExecute)) return Results.Forbid();
            if (input.Priority is { } wanted && !Enum.IsDefined(wanted)) throw new DomainException("Unknown priority.");
            if (input.Status is { Status: WorkOrderStatus.OnHold, HoldReason: null })
                return new Validator().When(true, "holdReason", "Choose why the work is on hold.").Problem();
            if (add.Count > 0 && await db.Employees.CountAsync(e => add.Contains(e.Id), ct) != add.Count) throw new DomainException("A chosen technician wasn't found.");

            var rows = await db.WorkOrders.Include(x => x.Assignees).Where(w => ids.Contains(w.Id)).OrderBy(w => w.Number).ToListAsync(ct);
            // A priority change recomputes target deadlines from when the work was raised (as on the edit page).
            var requestIds = rows.Where(r => r.WorkRequestId != null).Select(r => r.WorkRequestId!.Value).ToList();
            var raised = await db.WorkRequests.Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.CreatedAt, ct);
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var canClose = mine.Contains(Permissions.WorkOrdersClose);
            var skipped = new List<BulkSkip>();
            var updated = 0;
            foreach (var w in rows)
            {
                string? reason = null;
                if (input.Status is { } st)
                {
                    if (!WorkOrderWorkflow.CanTransition(w.Status, st.Status))
                        reason = $"It can't go from {TableExport.Words(w.Status.ToString()).ToLowerInvariant()} to {TableExport.Words(st.Status.ToString()).ToLowerInvariant()}.";
                    else if (WorkOrderWorkflow.RequiresClosePermission(w.Status, st.Status) && !canClose) reason = "You can't close, cancel or reopen closed work orders.";
                    else if (await ChangeStatusAsync(db, ctx, mail, settings, w, st, ct) is { IsValid: false } v) reason = v.Messages.First();
                }
                else if (w.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled) reason = "Reopen it first.";
                else if (input.Priority is { } priority)
                {
                    var before = w.Priority;
                    w.Priority = priority;
                    Deadlines.OnUpdate(w, w.DueAt, w.DueDate, before, settings,
                        w.WorkRequestId is { } rid && raised.TryGetValue(rid, out var at) ? at : w.CreatedAt);
                }
                else
                {
                    var before = w.Assignees.Select(a => a.EmployeeId).ToHashSet();
                    SyncAssignees(w, [.. before, .. add]);
                    await mail.AssigneesAddedAsync(db, ctx, w, add.Where(e => !before.Contains(e)).ToList(), ct);
                }
                if (reason is null) updated++; else skipped.Add(new BulkSkip(w.Id, w.Number, reason));
            }
            skipped.AddRange(ids.Except(rows.Select(r => r.Id)).Select(missing => new BulkSkip(missing, "", "Not found.")));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new BulkResult(updated, skipped));
        }).RequirePermission(Permissions.WorkOrdersView);

        // "Check Warranty/WO" (4CSQL): warranty status plus open requests and work on an asset, to avoid duplicate jobs.
        api.MapGet("/assets/{assetId:guid}/open-work", async (Guid assetId, CmmsDbContext db, CancellationToken ct) =>
        {
            var a = await db.Assets.AsNoTracking().Where(x => x.Id == assetId).Select(x => new { x.Id, x.Status, x.WarrantyType, x.WarrantyExpiresOn }).FirstOrDefaultAsync(ct);
            if (a is null) return Results.NotFound();
            var orders = await db.WorkOrders.AsNoTracking().Where(w => (w.AssetId == assetId || w.Assets.Any(a => a.AssetId == assetId)) && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold))
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
            var v = new Validator().When(input.Reading is { Length: > 100 }, "reading", "Reading can be at most 100 characters.")
                .When(input.Remarks is { Length: > 2000 }, "remarks", "Remarks can be at most 2000 characters.");
            if (!v.IsValid) return v.Problem();
            if (input.Description.Clean() is { } d) t.Description = d.Length > 1000 ? d[..1000] : d;
            await ApplyTaskChangeAsync(db, ctx, id, t, input.IsDone, input.Reading, input.Remarks, DateTimeOffset.UtcNow, ct);
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
        g.MapPost("/{id:guid}/labor", async (Guid id, LaborInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            await RequireOpenAsync(db, id, ct);
            var (v, l) = await AddLaborAsync(db, ctx, id, input, ct);
            if (!v.IsValid) return v.Problem();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { l!.Id });
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
                .Select(c => new { c.Id, Kind = "comment", Body = (string?)c.Body, From = (WorkOrderStatus?)null, To = (WorkOrderStatus?)null, HoldReason = (HoldReason?)null, By = c.UserName, At = c.CreatedAt }).ToListAsync(ct);
            var changes = await db.WorkOrderStatusChanges.AsNoTracking().Where(c => c.WorkOrderId == id)
                .Select(c => new { c.Id, Kind = "status", Body = c.Note, From = c.FromStatus, To = (WorkOrderStatus?)c.ToStatus, c.HoldReason, By = c.ChangedByName, At = c.ChangedAt }).ToListAsync(ct);
            return comments.Concat(changes).OrderByDescending(x => x.At);
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapPost("/{id:guid}/comments", async (Guid id, CommentInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct)) return Results.NotFound();
            var v = AddComment(db, ctx, id, input.Body, DateTimeOffset.UtcNow);
            if (!v.IsValid) return v.Problem();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).RequirePermission(Permissions.WorkOrdersView);
    }

    // ---------------- helpers ----------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    /// <summary>
    /// Checks and applies a status change to a loaded work order (the move itself and the close permission are the caller's
    /// to check). Returns the problems; nothing changes unless it is valid. The caller saves.
    /// </summary>
    internal static async Task<Validator> ChangeStatusAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, TenantSettings settings, WorkOrder w,
        StatusInput input, CancellationToken ct)
    {
        var v = new Validator().MaxLength("note", input.Note, "Note", 2000)
            .When(input.Status == WorkOrderStatus.OnHold && input.HoldReason is null, "holdReason", "Choose why the work is on hold.")
            .When(input.DowntimeHours < 0, "downtimeHours", "Downtime cannot be negative.");
        if (input.Status == WorkOrderStatus.Completed)
        {
            var notes = input.CompletionNotes.Clean() ?? w.CompletionNotes;
            if (settings.RequireCompletionNotes && notes is null) v.Add("completionNotes", "Describe the work that was done.");
            if (settings.RequireLaborToComplete && !await db.WorkOrderLabor.AnyAsync(l => l.WorkOrderId == w.Id, ct))
                v.Add("labor", "Log the labour before completing the work.");
            if (settings.RequireAllTasksDone && await db.WorkOrderTasks.AnyAsync(t => t.WorkOrderId == w.Id && !t.IsDone, ct))
                v.Add("tasks", "Tick every checklist step before completing the work.");
            if (settings.RequireFailureCodeForBreakdowns && w.Type == WorkType.Breakdown && (input.FailureCodeId ?? w.FailureCodeId) is null)
                v.Add("failureCodeId", "Choose a failure code for breakdown work.");
            if (input.FailureCodeId is { } fc && !await db.FailureCodes.AnyAsync(f => f.Id == fc, ct)) v.Add("failureCodeId", "Unknown failure code.");
            if (input.ActualStart is { } st && input.ActualEnd is { } en && en < st) v.Add("actualEnd", "Actual end cannot be before the actual start.");
            if (input.ActualEnd > DateTimeOffset.UtcNow.AddHours(1)) v.Add("actualEnd", "Actual end cannot be in the future.");
        }
        if (!v.IsValid) return v;

            var now = DateTimeOffset.UtcNow;
        db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange
        {
            WorkOrderId = w.Id, FromStatus = w.Status, ToStatus = input.Status, HoldReason = input.Status == WorkOrderStatus.OnHold ? input.HoldReason : null,
            Note = input.Note.Clean(), ChangedAt = now, ChangedById = ctx.UserId, ChangedByName = ctx.UserName,
        });
        // Leaving Open (start, hold or straight to completed) is the response; the phone's time for offline work.
        if (input.Status != WorkOrderStatus.Open) w.RespondedAt ??= input.At ?? now;
        w.Status = input.Status;
        w.HoldReason = input.Status == WorkOrderStatus.OnHold ? input.HoldReason : null;
        switch (input.Status)
        {
            case WorkOrderStatus.InProgress: w.StartedAt ??= input.ActualStart ?? now; w.CompletedAt = null; w.ClosedAt = null; break;
            case WorkOrderStatus.Completed:
                // Like 4CSQL's "Calculate Dates": default actual start to when work started, else the earliest labour entry.
                var firstLabor = await db.WorkOrderLabor.Where(l => l.WorkOrderId == w.Id).OrderBy(l => l.WorkDate).Select(l => (DateOnly?)l.WorkDate).FirstOrDefaultAsync(ct);
                w.StartedAt = input.ActualStart ?? w.StartedAt ?? (firstLabor is { } fl ? new DateTimeOffset(fl.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : input.ActualEnd ?? now);
                w.CompletedAt = input.ActualEnd ?? now;
                if (w.StartedAt > w.CompletedAt) w.StartedAt = w.CompletedAt; // a derived start never comes after the end
                w.PreventionNotes = input.PreventionNotes.Clean() ?? w.PreventionNotes;
                w.CompletionNotes = input.CompletionNotes.Clean() ?? w.CompletionNotes;
                w.CauseNotes = input.CauseNotes.Clean() ?? w.CauseNotes;
                w.FailureCodeId = input.FailureCodeId ?? w.FailureCodeId;
                w.AssetWasDown = input.AssetWasDown ?? w.AssetWasDown;
                w.DowntimeHours = input.DowntimeHours ?? w.DowntimeHours;
                await Downtime.OnCompletedAsync(db, ctx, w, ct); // a linked stop ends now and fills the downtime fields
                break;
            case WorkOrderStatus.Closed: w.ClosedAt = now; w.CompletedAt ??= now; break;
            case WorkOrderStatus.Open: w.CompletedAt = null; w.ClosedAt = null; break;
        }
        if (input.Status == WorkOrderStatus.Completed) await mail.WorkOrderCompletedAsync(db, ctx, w, ct);
        if (w.PmScheduleId is not null && input.Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            await PmGenerator.OnWorkOrderFinishedAsync(db, w, ct);
        return v;
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static List<T> ParseEnums<T>(string? csv) where T : struct, Enum =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Enum.TryParse<T>(x, true, out var e) ? (T?)e : null).OfType<T>().ToList();

    /// <summary>One page of the work-order list (the list screen and its export share it, so their rows can't differ).</summary>
    internal static async Task<PagedResult<WorkOrderListItem>> ListAsync(CmmsDbContext db, WorkOrderFilter f, ITenantContext ctx, int page, int pageSize,
        CancellationToken ct, int maxPageSize = 500)
    {
        var q = await FilterAsync(db, f, ctx, includeStatus: true, ct);
        var settings = await SlaQueries.SettingsAsync(db, ct);
        var now = DateTimeOffset.UtcNow;
        var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "createdAt").Select(w => new
        {
            w.Id, w.Number, w.Title, w.Type, w.Priority, w.Status, w.HoldReason, w.SiteId, SiteName = w.Site!.Name, w.LocationId, LocationPath = w.Location!.Path,
            w.AssetId, AssetNumber = w.Asset!.AssetNumber, AssetName = w.Asset.Name, TradeName = w.Trade!.Name,
            Assignees = w.Assignees.Select(a => a.Employee!.FullName).ToList(), w.DueDate, w.CreatedAt, w.CompletedAt,
            w.DueAt, w.StartBy, w.RespondedAt, w.DueFromTarget,
        }).ToPagedAsync(page, pageSize, ct, maxPageSize);
        var names = await LocationEndpoints.ResolvePathNamesAsync(db, paged.Items.Select(i => i.LocationPath), ct);
        return new PagedResult<WorkOrderListItem>(paged.Items.Select(w =>
        {
            var sla = SlaQueries.Work(settings, w.Priority, w.Status, w.StartBy, w.RespondedAt, w.DueAt, w.DueFromTarget, w.CompletedAt, now);
            return new WorkOrderListItem(w.Id, w.Number, w.Title, w.Type, w.Priority, w.Status, w.HoldReason,
                w.SiteId, w.SiteName, w.LocationId, w.LocationPath is null ? null : names(w.LocationPath), w.AssetId, w.AssetNumber, w.AssetName, w.TradeName,
                w.Assignees, w.DueDate, w.CreatedAt, w.CompletedAt, sla.Completion == SlaState.Late || sla.Response == SlaState.Late,
                w.DueAt, w.StartBy, w.RespondedAt, sla.Response, sla.Completion);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

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
        if (f.AssetId is not null) q = q.Where(w => w.AssetId == f.AssetId || (w.Asset != null && w.Asset.ParentAssetId == f.AssetId) || w.Assets.Any(a => a.AssetId == f.AssetId));
        if (f.TradeId is not null) q = q.Where(w => w.TradeId == f.TradeId);
        if (f.CreatedFrom is { } cf) { var from = new DateTimeOffset(cf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(w => w.CreatedAt >= from); }
        if (f.CreatedTo is { } ctd && ctd < DateOnly.MaxValue) { var before = new DateTimeOffset(ctd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(w => w.CreatedAt < before); }
        if (f.DueFrom is { } df) q = q.Where(w => w.DueDate >= df);
        if (f.DueTo is { } dt) q = q.Where(w => w.DueDate <= dt);
        q = f.Source?.ToLowerInvariant() switch
        {
            "pm" => q.Where(w => w.PmScheduleId != null),
            "request" => q.Where(w => w.WorkRequestId != null),
            "direct" => q.Where(w => w.PmScheduleId == null && w.WorkRequestId == null),
            _ => q,
        };
        if (f.AssigneeId is not null) q = q.Where(w => w.Assignees.Any(a => a.EmployeeId == f.AssigneeId));
        if (f.Mine == true)
        {
            var me = await db.Employees.Where(e => e.UserId == ctx.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(w => me != null && w.Assignees.Any(a => a.EmployeeId == me));
        }
        if (f.Late == true || f.Overdue == true) q = SlaQueries.LateWork(q, DateTimeOffset.UtcNow);
        if (f.ResponseLate == true) q = SlaQueries.LateResponse(q, DateTimeOffset.UtcNow);
        if (f.DueSoon == true)
        {
            var soon = await SlaQueries.DueSoonIdsAsync(q, await SlaQueries.SettingsAsync(db, ct), DateTimeOffset.UtcNow, ct);
            q = q.Where(w => soon.Contains(w.Id));
        }
        return q;
    }

    /// <summary>The work order's first action (a tick or hours) is its response: the earliest one counts.</summary>
    internal static async Task MarkRespondedAsync(CmmsDbContext db, Guid workOrderId, DateTimeOffset? at, CancellationToken ct)
    {
        var w = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == workOrderId, ct);
        if (w is not null) w.RespondedAt ??= at ?? DateTimeOffset.UtcNow;
    }

    /// <summary>"Start work automatically" setting: an Open work order moves to In progress when work is first recorded on it.</summary>
    private static async Task AutoStartAsync(CmmsDbContext db, ITenantContext ctx, Guid workOrderId, string reason, CancellationToken ct, DateTimeOffset? at = null)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        if (!settings.AutoStartWork) return;
        var now = DateTimeOffset.UtcNow;
        var started = at ?? now; // work recorded offline started when it was done
        // One guarded update, so two steps ticked at the same moment start the work once instead of clashing on the row version.
        var moved = await db.WorkOrders.Where(x => x.Id == workOrderId && x.Status == WorkOrderStatus.Open).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, WorkOrderStatus.InProgress)
            .SetProperty(x => x.StartedAt, x => x.StartedAt ?? started)
            .SetProperty(x => x.RespondedAt, x => x.RespondedAt ?? started)
            .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedById, ctx.UserId), ct);
        // A copy of the work order this request already loaded is now out of date (status, row version): refresh it.
        if (db.ChangeTracker.Entries<WorkOrder>().FirstOrDefault(e => e.Entity.Id == workOrderId) is { } loaded) await loaded.ReloadAsync(ct);
        if (moved == 0) return;
        db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange
        {
            WorkOrderId = workOrderId, FromStatus = WorkOrderStatus.Open, ToStatus = WorkOrderStatus.InProgress, Note = reason, ChangedAt = now,
            ChangedById = ctx.UserId, ChangedByName = ctx.UserName,
        });
    }

    public static async Task<Validator> ValidateAsync(WorkOrderInput i, CmmsDbContext db, CancellationToken ct)
    {
        // The due time's day in the organization's time zone (01:00 in Cairo is still the same local day).
        var dueDay = i.DueAt is { } da ? Sla.LocalDay(da, Sla.Zone((await SlaQueries.SettingsAsync(db, ct)).TimeZone)) : i.DueDate;
        var v = new Validator().Required("title", i.Title, "Title", 300)
            .When(!Enum.IsDefined(i.Type), "type", "Choose a work type.").When(!Enum.IsDefined(i.Priority), "priority", "Choose a priority.")
            .When(i.EstimatedHours < 0, "estimatedHours", "Estimate cannot be negative.")
            .When(i.PlannedStart is { } ps && dueDay is { } dd && dd < ps, "dueDate", "Due date cannot be before the planned start.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(t => t.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        var who = (i.AssigneeIds ?? []).Distinct().ToList();
        if (who.Count > 0 && await db.Employees.CountAsync(e => who.Contains(e.Id), ct) != who.Count) v.Add("assigneeIds", "Unknown person.");
        return v;
    }

    public static async Task<WorkOrder> CreateAsync(CmmsDbContext db, ITenantContext ctx, WorkOrderInput input, Guid? requestId, CancellationToken ct,
        Action<WorkOrder>? configure = null, string? note = null, EmailOutbox? mail = null, bool applyTargets = true, DateTimeOffset? raisedAt = null)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var number = await Numbering.NextAsync(db, "WorkOrder", settings.WorkOrderNumberPrefix, 5,
            n => db.WorkOrders.AllSites().AnyAsync(w => w.Number == n, ct), p => db.WorkOrders.AllSites().Where(w => w.Number.StartsWith(p)).Select(w => w.Number).ToListAsync(ct), ct);
        var w = new WorkOrder { Number = number, Title = input.Title.Trim(), SiteId = input.SiteId, WorkRequestId = requestId };
        // Default the location from the asset when only the asset was chosen.
        if (input.AssetId is not null && input.LocationId is null)
            input = input with { LocationId = await db.Assets.Where(a => a.Id == input.AssetId).Select(a => a.LocationId).FirstOrDefaultAsync(ct) };
        Apply(w, input);
        Deadlines.OnCreate(w, input.DueAt, input.DueDate, settings, raisedAt ?? DateTimeOffset.UtcNow, requestId is not null, applyTargets);
        SyncAssignees(w, input.AssigneeIds);
        configure?.Invoke(w);
        db.WorkOrders.Add(w);
        if (mail is not null) await mail.WorkOrderCreatedAsync(db, ctx, w, ct);
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
        w.AssetId = i.AssetId; w.TradeId = i.TradeId; w.PlannedStart = i.PlannedStart; w.EstimatedHours = i.EstimatedHours; // deadlines: Deadlines
        w.RequestedBy = i.RequestedBy.Clean() is { } rb && rb.Length > 200 ? rb[..200] : i.RequestedBy.Clean();
    }

    private static void SyncAssignees(WorkOrder w, List<Guid>? ids)
    {
        var want = (ids ?? []).Distinct().ToHashSet();
        w.Assignees.RemoveAll(a => !want.Contains(a.EmployeeId));
        foreach (var id in want.Where(id => w.Assignees.All(a => a.EmployeeId != id))) w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = id });
    }

    /// <summary>Logs labour on a work order at the employee's current rates (snapshotted); logging labour starts open work.</summary>
    internal static async Task<(Validator V, WorkOrderLabor? Labor)> AddLaborAsync(CmmsDbContext db, ITenantContext ctx, Guid workOrderId, LaborInput input, CancellationToken ct,
        DateTimeOffset? at = null)
    {
        var (v, emp) = await ValidateLaborAsync(input, db, ct);
        if (!v.IsValid) return (v, null);
        var l = new WorkOrderLabor { WorkOrderId = workOrderId, EmployeeId = emp!.Id, WorkDate = input.WorkDate, Hours = input.Hours, OvertimeHours = input.OvertimeHours,
            Rate = emp.HourlyRate ?? 0, OvertimeRate = emp.OvertimeRate ?? emp.HourlyRate ?? 0, Notes = input.Notes.Clean() };
        db.WorkOrderLabor.Add(l);
        await AutoStartAsync(db, ctx, workOrderId, "Started automatically: labour logged", ct, at);
        await MarkRespondedAsync(db, workOrderId, at, ct);
        return (v, l);
    }

    internal static Validator AddComment(CmmsDbContext db, ITenantContext ctx, Guid workOrderId, string? body, DateTimeOffset at)
    {
        var v = new Validator().Required("body", body, "Comment", 4000);
        if (v.IsValid) db.WorkOrderComments.Add(new WorkOrderComment { WorkOrderId = workOrderId, Body = body!.Trim(), UserId = ctx.UserId, UserName = ctx.UserName, CreatedAt = at });
        return v;
    }

    /// <summary>
    /// Applies a tick, reading or remarks to a checklist step (the office page and the technician app's queued changes),
    /// recording who changed it and when; ticking a step starts open work.
    /// </summary>
    internal static async Task ApplyTaskChangeAsync(CmmsDbContext db, ITenantContext ctx, Guid workOrderId, WorkOrderTask t, bool? isDone, string? reading, string? remarks,
        DateTimeOffset at, CancellationToken ct)
    {
        var changed = false;
        if (reading is not null && reading.Clean() != t.Reading) { t.Reading = reading.Clean(); changed = true; } // an empty string clears it
        if (remarks is not null && remarks.Clean() != t.Remarks) { t.Remarks = remarks.Clean(); changed = true; }
        if (isDone is { } done && done != t.IsDone)
        {
            t.IsDone = done; t.DoneAt = done ? at : null; t.DoneBy = done ? ctx.UserName : null; changed = true;
            if (done)
            {
                await AutoStartAsync(db, ctx, workOrderId, "Started automatically: checklist step done", ct, at);
                await MarkRespondedAsync(db, workOrderId, at, ct);
            }
        }
        if (changed) { t.UpdatedAt = at; t.UpdatedById = ctx.UserId; t.UpdatedByName = ctx.UserName; }
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
