using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Working with a group (0.12.0): its page, and actions that apply to its work orders.</summary>
public static partial class WorkGroups
{
    public sealed record AssignInput(List<Guid> EmployeeIds, List<Guid>? ChildIds = null);
    public sealed record ReportInput(string? CompletionNotes, string? CauseNotes, string? PreventionNotes, Guid? FailureCodeId, List<Guid>? ChildIds = null, Guid? RemedyId = null);
    public sealed record GroupStatusInput(WorkOrderStatus Status, HoldReason? HoldReason = null, string? Note = null, string? CompletionNotes = null, string? CauseNotes = null,
        Guid? FailureCodeId = null, string? PreventionNotes = null, Guid? CancelReasonId = null, List<Guid>? ChildIds = null, Guid? RemedyId = null);
    public sealed record DueInput(DateOnly? DueDate, DateTimeOffset? DueAt);
    public sealed record CommentInput(string Body);

    public const string DueFromGroup = "The due date comes from group {0}.";
    public const string LeftAlone = "Left alone (finished or hours logged): {0}.";
    private static readonly WorkOrderStatus[] Finished = [WorkOrderStatus.Completed, WorkOrderStatus.Closed, WorkOrderStatus.Cancelled];

    /// <summary>A group's status from its work orders (it has none of its own).</summary>
    public static string Status(IEnumerable<WorkOrderStatus> children)
    {
        var list = children.ToList();
        if (list.Count == 0 || list.All(s => s == WorkOrderStatus.Open)) return "Open";
        if (list.All(s => s == WorkOrderStatus.Cancelled)) return "Cancelled";
        if (list.All(s => s is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)) return "Closed";
        if (list.All(s => Finished.Contains(s))) return "Completed";
        return "InProgress";
    }

    /// <summary>A group's comments, newest first (shown on each of its work orders).</summary>
    internal static async Task<List<object>> CommentsAsync(CmmsDbContext db, Guid groupId, CancellationToken ct) =>
        (await db.WorkOrderGroupComments.IgnoreQueryFilters().AsNoTracking().Where(c => c.GroupId == groupId && c.TenantId == db.CurrentTenantId)
            .OrderByDescending(c => c.CreatedAt).Select(c => new { c.Id, c.Body, c.UserName, c.CreatedAt }).ToListAsync(ct)).Cast<object>().ToList();

    public static void MapWorkGroupEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/work-groups").WithTags("Work groups");

        g.MapGet("/", async (string? search, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = db.WorkOrderGroups.AsNoTracking();
            if (search.Clean() is { } s) q = q.Where(x => x.Number.Contains(s) || x.Title.Contains(s));
            var rows = await q.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.Number).Take(500)
                .Select(x => new
                {
                    x.Id, x.Number, x.Title, x.SiteId, SiteName = x.Site!.Name, x.PmScheduleId, PmCode = db.PmSchedules.Where(p => p.Id == x.PmScheduleId).Select(p => p.Code).FirstOrDefault(),
                    x.DueDate, x.PlannedStart, Statuses = db.WorkOrders.Where(w => w.GroupId == x.Id).Select(w => w.Status).ToList(),
                }).ToListAsync(ct);
            return rows.Select(x => new
            {
                x.Id, x.Number, x.Title, x.SiteId, x.SiteName, x.PmScheduleId, x.PmCode, x.DueDate, x.PlannedStart, Status = Status(x.Statuses),
                Total = x.Statuses.Count, Done = x.Statuses.Count(st => Finished.Contains(st)),
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var grp = await db.WorkOrderGroups.AsNoTracking().Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (grp is null) return Results.NotFound();
            // Its work orders the person may see (branch limits apply to them as anywhere).
            var children = await db.WorkOrders.AsNoTracking().Where(w => w.GroupId == id).OrderBy(w => w.Number)
                .Select(w => new
                {
                    w.Id, w.Number, Title = Bilingual.Arabic && w.TitleAr != null ? w.TitleAr : w.Title, w.Status, w.AssetId, AssetNumber = w.Asset!.AssetNumber, AssetName = Bilingual.Arabic && w.Asset.NameAr != null ? w.Asset.NameAr : w.Asset.Name, w.DueDate, w.CompletedAt,
                    Assignees = w.Assignees.Select(a => new { a.EmployeeId, a.Employee!.FullName }).ToList(),
                    StepsDone = db.WorkOrderTasks.Count(t => t.WorkOrderId == w.Id && t.IsDone), StepsTotal = db.WorkOrderTasks.Count(t => t.WorkOrderId == w.Id),
                    LaborHours = db.WorkOrderLabor.Where(l => l.WorkOrderId == w.Id).Sum(l => (decimal?)(l.Hours + l.OvertimeHours)) ?? 0,
                    LaborCost = db.WorkOrderLabor.Where(l => l.WorkOrderId == w.Id && l.Employee!.CompanyId == null).Sum(l => (decimal?)(l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate)) ?? 0,
                    // Contractor lines plus labour of company-linked employees (0.17.0).
                    ContractorCost = (db.WorkOrderContractors.Where(l => l.WorkOrderId == w.Id).Sum(l => (decimal?)(l.HoursDone * l.Rate)) ?? 0)
                        + (db.WorkOrderLabor.Where(l => l.WorkOrderId == w.Id && l.Employee!.CompanyId != null).Sum(l => (decimal?)(l.Hours * l.Rate + l.OvertimeHours * l.OvertimeRate)) ?? 0),
                    PartsCost = db.StockTransactions.Where(t => t.WorkOrderId == w.Id && (t.Type == Core.Inventory.StockTransactionType.Issue || t.Type == Core.Inventory.StockTransactionType.Return))
                        .Sum(t => (decimal?)(-t.Quantity * t.UnitCost)) ?? 0,
                    OtherCost = db.WorkOrderCosts.Where(c => c.WorkOrderId == w.Id).Sum(c => (decimal?)(c.Quantity * c.UnitCost)) ?? 0,
                }).ToListAsync(ct);
            var pm = grp.PmScheduleId is { } pmId ? await db.PmSchedules.AsNoTracking().Where(p => p.Id == pmId).Select(p => new { p.Id, p.Code, p.Name }).FirstOrDefaultAsync(ct) : null;
            return Results.Ok(new
            {
                grp.Id, grp.Number, grp.Title, grp.SiteId, SiteName = grp.Site!.Name, grp.PmDueDate, grp.PlannedStart, grp.DueDate, grp.DueAt, grp.CreatedAt, Pm = pm,
                Status = Status(children.Select(c => c.Status)),
                Children = children.Select(c => new
                {
                    c.Id, c.Number, c.Title, c.Status, c.AssetId, c.AssetNumber, c.AssetName, c.DueDate, c.CompletedAt, c.Assignees, c.StepsDone, c.StepsTotal, c.LaborHours,
                    Cost = Math.Round(c.LaborCost + c.PartsCost + c.OtherCost + c.ContractorCost, 2), ContractorCost = Math.Round(c.ContractorCost, 2),
                }),
                Totals = new
                {
                    LaborHours = children.Sum(c => c.LaborHours), LaborCost = Math.Round(children.Sum(c => c.LaborCost), 2), PartsCost = Math.Round(children.Sum(c => c.PartsCost), 2),
                    OtherCost = Math.Round(children.Sum(c => c.OtherCost), 2), ContractorCost = Math.Round(children.Sum(c => c.ContractorCost), 2),
                    TotalCost = Math.Round(children.Sum(c => c.LaborCost + c.PartsCost + c.OtherCost + c.ContractorCost), 2),
                },
                Comments = await CommentsAsync(db, id, ct),
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        // Technicians and contractors on every work order (or chosen ones) in one step; finished work or work with hours stays as it is.
        g.MapPost("/{id:guid}/assign", async (Guid id, AssignInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            if (!await db.WorkOrderGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var add = (input.EmployeeIds ?? []).Distinct().ToList();
            if (add.Count == 0) throw new DomainException("Choose who to assign.");
            if (await db.Employees.CountAsync(e => add.Contains(e.Id), ct) != add.Count) throw new DomainException("A chosen technician wasn't found.");
            var rows = await ChildrenAsync(db, id, input.ChildIds, ct, assignees: true);
            var worked = (await db.WorkOrderLabor.Where(l => rows.Select(r => r.Id).Contains(l.WorkOrderId)).Select(l => l.WorkOrderId).Distinct().ToListAsync(ct)).ToHashSet();
            var left = new List<string>();
            var assigned = 0;
            foreach (var w in rows)
            {
                if (Finished.Contains(w.Status) || worked.Contains(w.Id)) { left.Add(w.Number); continue; }
                var before = w.Assignees.Select(a => a.EmployeeId).ToHashSet();
                foreach (var e in add.Where(e => !before.Contains(e))) w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = e });
                await mail.AssigneesAddedAsync(db, ctx, w, add.Where(e => !before.Contains(e)).ToList(), ct);
                assigned++;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Assigned = assigned, LeftAlone = left, Message = left.Count == 0 ? null : Translator.T(string.Format(LeftAlone, string.Join(", ", left))) });
        }).RequirePermission(Permissions.WorkOrdersEdit);

        // One report written to every unfinished work order; what a work order already has is kept.
        g.MapPost("/{id:guid}/report", async (Guid id, ReportInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.WorkOrderGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            if (await Codes.CheckAsync(db.FailureCodes, input.FailureCodeId, null, Codes.UnknownCause, ct) is { } badCause) throw new DomainException(badCause);
            if (await Codes.CheckAsync(db.Remedies, input.RemedyId, null, Codes.UnknownRemedy, ct) is { } badRemedy) throw new DomainException(badRemedy);
            var v = new Validator().MaxLength("completionNotes", input.CompletionNotes, "Action taken", 4000).MaxLength("causeNotes", input.CauseNotes, "Cause", 4000)
                .MaxLength("preventionNotes", input.PreventionNotes, "Prevention", 4000);
            if (!v.IsValid) return v.Problem();
            var updated = 0;
            foreach (var w in (await ChildrenAsync(db, id, input.ChildIds, ct)).Where(w => !Finished.Contains(w.Status)))
            {
                if (string.IsNullOrWhiteSpace(w.CompletionNotes) && input.CompletionNotes.Clean() is { } cn) w.CompletionNotes = cn;
                if (string.IsNullOrWhiteSpace(w.CauseNotes) && input.CauseNotes.Clean() is { } cause) w.CauseNotes = cause;
                if (string.IsNullOrWhiteSpace(w.PreventionNotes) && input.PreventionNotes.Clean() is { } pn) w.PreventionNotes = pn;
                if (w.FailureCodeId is null && input.FailureCodeId is not null) w.FailureCodeId = input.FailureCodeId;
                if (w.RemedyId is null && input.RemedyId is not null) w.RemedyId = input.RemedyId;
                updated++;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Updated = updated });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // Every work order's normal status change, with its rules; those that refuse say why.
        g.MapPost("/{id:guid}/status", async (Guid id, GroupStatusInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, PermissionService perms, EmailOutbox mail,
            CancellationToken ct) =>
        {
            if (!await db.WorkOrderGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var canClose = (await perms.GetAsync(ctx.UserId!.Value)).Contains(Permissions.WorkOrdersClose);
            var change = new WorkOrderEndpoints.StatusInput(input.Status, input.HoldReason, input.Note, input.CompletionNotes, input.CauseNotes, input.FailureCodeId, null, null,
                input.PreventionNotes, CancelReasonId: input.CancelReasonId, RemedyId: input.RemedyId);
            var refused = new List<object>();
            var changed = 0;
            foreach (var w in await ChildrenAsync(db, id, input.ChildIds, ct))
            {
                string? reason = null;
                if (w.Status == input.Status) continue;
                if (!WorkOrderWorkflow.CanTransition(w.Status, input.Status))
                    reason = $"It can't go from {TableExport.Words(w.Status.ToString()).ToLowerInvariant()} to {TableExport.Words(input.Status.ToString()).ToLowerInvariant()}.";
                else if (WorkOrderWorkflow.RequiresClosePermission(w.Status, input.Status) && !canClose) reason = "You can't close, cancel or reopen closed work orders.";
                else if (input.Status == WorkOrderStatus.OnHold && input.HoldReason is null) reason = "Choose why the work is on hold.";
                else if (await WorkOrderEndpoints.ChangeStatusAsync(db, ctx, mail, settings, w, change, ct) is { IsValid: false } v) reason = v.Messages.First();
                if (reason is null) changed++; else refused.Add(new { w.Id, w.Number, Reason = Translator.T(reason) });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Changed = changed, Refused = refused });
        }).RequirePermission(Permissions.WorkOrdersExecute);

        // The group's due date, passed to every unfinished work order.
        g.MapPut("/{id:guid}/due", async (Guid id, DueInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var grp = await db.WorkOrderGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (grp is null) return Results.NotFound();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var day = input.DueAt is { } at ? Sla.LocalDay(at, Sla.Zone(settings.TimeZone)) : input.DueDate;
            grp.DueDate = day; grp.DueAt = input.DueAt;
            // Every unfinished work order of the group, also those in branches the person can't see: the group's date is theirs.
            foreach (var w in await db.WorkOrders.AllSites().Where(w => w.GroupId == id && !Finished.Contains(w.Status)).ToListAsync(ct))
                Deadlines.OnUpdate(w, input.DueAt, input.DueDate, w.Priority, settings, w.CreatedAt);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { grp.Id, grp.DueDate });
        }).RequirePermission(Permissions.WorkOrdersEdit);

        g.MapPost("/{id:guid}/comments", async (Guid id, CommentInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.WorkOrderGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var v = new Validator().Required("body", input.Body, "Comment", 4000);
            if (!v.IsValid) return v.Problem();
            db.WorkOrderGroupComments.Add(new WorkOrderGroupComment { GroupId = id, Body = input.Body.Trim(), UserId = ctx.UserId, UserName = ctx.UserName });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).RequirePermission(Permissions.WorkOrdersExecute);
    }

    /// <summary>The group's work orders the person may see, all or the chosen ones, in number order (tracked).</summary>
    private static async Task<List<WorkOrder>> ChildrenAsync(CmmsDbContext db, Guid groupId, List<Guid>? only, CancellationToken ct, bool assignees = false)
    {
        var q = db.WorkOrders.Where(w => w.GroupId == groupId);
        if (only is { Count: > 0 }) q = q.Where(w => only.Contains(w.Id));
        if (assignees) q = q.Include(w => w.Assignees);
        return await q.OrderBy(w => w.Number).ToListAsync(ct);
    }
}
