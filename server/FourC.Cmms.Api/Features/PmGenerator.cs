using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Turns PM schedules into work orders (4CSQL "PM Generation" / "Open PM WO").</summary>
public static class PmGenerator
{
    public sealed record Result(Guid ScheduleId, string Code, string Name, DateOnly? DueDate, string? TaskName, string? AssetNumber,
        Guid? WorkOrderId, string? WorkOrderNumber, string? Skipped);

    private static readonly WorkOrderStatus[] Active = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Generates (or, with <paramref name="dryRun"/>, previews) work orders for time-based occurrences up to <paramref name="until"/>;
    /// when <paramref name="until"/> is null each schedule's own lead time is used (automatic generation).
    /// </summary>
    public static async Task<List<Result>> RunAsync(CmmsDbContext db, ITenantContext ctx, DateOnly? until, Guid? siteId, IReadOnlyCollection<Guid>? ids,
        bool dryRun, CancellationToken ct)
    {
        var today = Today;
        var horizon = until ?? today.AddDays(366);
        var q = db.PmSchedules.AsNoTracking().Where(s => s.IsActive && s.NextDueDate != null && s.NextDueDate <= horizon && (s.Type != PmScheduleType.Meter || s.TimeBased));
        if (siteId is not null) q = q.Where(s => s.SiteId == siteId);
        if (ids is { Count: > 0 }) q = q.Where(s => ids.Contains(s.Id));
        var candidates = await q.OrderBy(s => s.NextDueDate).Select(s => s.Id).ToListAsync(ct);
        var results = new List<Result>();
        foreach (var id in candidates)
        {
            try
            {
                var query = db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Asset).Where(s => s.Id == id);
                var s = dryRun ? await query.AsNoTracking().FirstAsync(ct) : await query.FirstAsync(ct);
                results.AddRange(await GenerateForAsync(db, ctx, s, until ?? today.AddDays(Math.Max(0, s.LeadDays)), dryRun, ct));
            }
            catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear(); // another run generated it at the same moment; it will be picked up next time
                results.Add(new Result(id, "", "", null, null, null, null, null, "Skipped: changed by another generation run"));
            }
        }
        if (dryRun)
        {
            // Like 4CSQL's "PM based on actual date" list: show Actual schedules waiting for their last work order.
            var waiting = db.PmSchedules.AsNoTracking().Where(s => s.IsActive && s.Basis == PmBasis.Actual && s.NextDueDate == null && s.LastWorkOrderId != null);
            if (siteId is not null) waiting = waiting.Where(s => s.SiteId == siteId);
            results.AddRange(await waiting.Select(s => new Result(s.Id, s.Code, s.Name, null, s.PmTask!.Name, s.Asset!.AssetNumber, s.LastWorkOrderId,
                db.WorkOrders.Where(w => w.Id == s.LastWorkOrderId).Select(w => w.Number).FirstOrDefault(), "Waiting for the last work order to be completed")).ToListAsync(ct));
        }
        return results;
    }

    private static async Task<List<Result>> GenerateForAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, DateOnly limit, bool dryRun, CancellationToken ct)
    {
        var results = new List<Result>();
        if (s.NextDueDate is not { } first || first > limit) return results;
        if (s.Basis == PmBasis.Actual && await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && Active.Contains(w.Status), ct))
            return [new Result(s.Id, s.Code, s.Name, first, null, s.Asset?.AssetNumber, null, null, "Previous PM work order is still open")];
        var taskIds = s.ExtraTasks.Select(x => x.PmTaskId).Append(s.PmTaskId).Distinct().ToList();
        var tasks = await db.PmTasks.AsNoTracking().Include(t => t.Steps).Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        for (var guard = 0; guard < 120 && s.NextDueDate is { } due && due <= limit; guard++)
        {
            var count = s.GenerationCount + 1;
            var task = tasks.GetValueOrDefault(PmCalendar.TaskFor(s, count)) ?? tasks.GetValueOrDefault(s.PmTaskId);
            WorkOrder? wo = null;
            var exists = await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && w.PmDueDate == due, ct);
            if (!exists && !dryRun && task is not null) wo = await CreateWorkOrderAsync(db, ctx, s, task, due, null, ct);
            results.Add(new Result(s.Id, s.Code, s.Name, due, task?.Name, s.Asset?.AssetNumber, wo?.Id, wo?.Number,
                task is null ? "PM task not found" : exists ? "Already generated" : null));
            s.GenerationCount = count; s.LastDueDate = due; s.LastGeneratedAt = DateTimeOffset.UtcNow; s.LastWorkOrderId = wo?.Id ?? s.LastWorkOrderId;
            s.NextDueDate = s.Basis == PmBasis.Scheduled ? PmCalendar.Next(s, due) : null;
            if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental && s.Basis == PmBasis.Scheduled && s.MeterInterval is { } iv && !dryRun)
                s.NextDueReading = (await db.Meters.Where(m => m.Id == s.MeterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + iv;
            if (!dryRun) await db.SaveChangesAsync(ct);
            if (s.Basis == PmBasis.Actual) break;
        }
        return results;
    }

    public static async Task<WorkOrder> CreateWorkOrderAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, PmTask task, DateOnly? due, string? trigger, CancellationToken ct)
    {
        var assignees = s.AssigneeIds.Count == 0 ? [] : await db.Employees.Where(e => s.AssigneeIds.Contains(e.Id) && e.IsActive).Select(e => e.Id).ToListAsync(ct);
        var description = string.Join("\n\n", new[] { task.Description ?? s.Description, task.SafetyNotes is { Length: > 0 } sn ? $"Safety: {sn}" : null, trigger }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var title = task.Id == s.PmTaskId ? s.Name : $"{s.Name} — {task.Name}";
        var start = due ?? Today;
        var input = new WorkOrderEndpoints.WorkOrderInput(title.Length > 300 ? title[..300] : title, description.Length == 0 ? null : description, WorkType.Preventive, s.Priority,
            s.SiteId, s.LocationId, s.AssetId, s.TradeId ?? task.TradeId, assignees, start, PmCalendar.DueDate(s, start), task.EstimatedHours,
            task.Steps.OrderBy(x => x.Sequence).Select(x => x.Description).ToList());
        return await WorkOrderEndpoints.CreateAsync(db, ctx, input, null, ct,
            w => { w.PmScheduleId = s.Id; w.PmTaskId = task.Id; w.PmDueDate = due; }, $"Generated from PM {s.Code}");
    }

    /// <summary>"Actual" schedules restart from the completion (or cancellation) of their latest work order.</summary>
    public static async Task OnWorkOrderFinishedAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        var s = await db.PmSchedules.FirstOrDefaultAsync(x => x.Id == w.PmScheduleId, ct);
        if (s is null || !s.IsActive || s.Basis != PmBasis.Actual || (s.LastWorkOrderId is { } last && last != w.Id)) return;
        var done = DateOnly.FromDateTime((w.CompletedAt ?? DateTimeOffset.UtcNow).UtcDateTime);
        if (PmCalendar.UsesCalendar(s)) s.NextDueDate = PmCalendar.Next(s, done);
        if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental && s.MeterInterval is { } iv)
            s.NextDueReading = (await db.Meters.Where(m => m.Id == s.MeterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + iv;
    }

    /// <summary>Evaluates meter-based schedules after a reading; returns the work orders it created.</summary>
    public static async Task<List<Result>> OnReadingAsync(CmmsDbContext db, ITenantContext ctx, Meter meter, decimal value, CancellationToken ct)
    {
        var results = new List<Result>();
        var schedules = await db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Asset)
            .Where(s => s.MeterId == meter.Id && s.IsActive && s.Type == PmScheduleType.Meter).ToListAsync(ct);
        foreach (var s in schedules)
        {
            Guid? taskId = null;
            string? why = null;
            if (s.MeterTrigger == MeterTrigger.Threshold)
            {
                if (s.LowerLimit is { } lo && value < lo) { taskId = s.LowerTaskId ?? s.PmTaskId; why = $"Reading {value:0.###} {meter.Unit} is below the lower limit {lo:0.###}."; }
                else if (s.UpperLimit is { } hi && value > hi) { taskId = s.UpperTaskId ?? s.PmTaskId; why = $"Reading {value:0.###} {meter.Unit} is above the upper limit {hi:0.###}."; }
            }
            else if (s.NextDueReading is { } dueAt && value >= dueAt)
            {
                taskId = PmCalendar.TaskFor(s, s.GenerationCount + 1);
                why = $"Meter reached {value:0.###} {meter.Unit} (due at {dueAt:0.###}).";
            }
            if (taskId is null) continue;
            if (await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && Active.Contains(w.Status), ct))
            {
                results.Add(new Result(s.Id, s.Code, s.Name, null, null, s.Asset?.AssetNumber, null, null, "Previous PM work order is still open"));
                continue;
            }
            var task = await db.PmTasks.AsNoTracking().Include(t => t.Steps).FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task is null) continue;
            var wo = await CreateWorkOrderAsync(db, ctx, s, task, null, why, ct);
            s.GenerationCount++; s.LastGeneratedAt = DateTimeOffset.UtcNow; s.LastWorkOrderId = wo.Id;
            if (s.MeterTrigger == MeterTrigger.Incremental)
                s.NextDueReading = s.Basis == PmBasis.Scheduled && s.MeterInterval is { } iv ? value + iv : null;
            if (s.TimeBased && s.Basis == PmBasis.Scheduled) s.NextDueDate = PmCalendar.Next(s, Today); // whichever comes first: restart the clock
            if (s.TimeBased && s.Basis == PmBasis.Actual) s.NextDueDate = null;
            await db.SaveChangesAsync(ct);
            results.Add(new Result(s.Id, s.Code, s.Name, null, task.Name, s.Asset?.AssetNumber, wo.Id, wo.Number, null));
        }
        return results;
    }
}

/// <summary>Background job: periodically generates due PM work orders for every organization that enabled it.</summary>
public sealed class PmSchedulerService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PmSchedulerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Pm:AutoGenerate", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Pm:SchedulerStartDelaySeconds", 60)), stop); } catch (OperationCanceledException) { return; }
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Pm:SchedulerIntervalMinutes", 60)));
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnceAsync(stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "PM scheduler run failed"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        List<Guid> tenantIds;
        await using (var root = new CmmsDbContext(options, new FixedTenantContext(null)))
            tenantIds = (await root.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct)).Where(t => t.Settings.AutoGeneratePm).Select(t => t.Id).ToList();
        foreach (var tenantId in tenantIds)
        {
            await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId, null, "PM scheduler"));
            var created = (await PmGenerator.RunAsync(db, new FixedTenantContext(tenantId, null, "PM scheduler"), null, null, null, false, ct)).Count(r => r.WorkOrderId is not null);
            if (created > 0) log.LogInformation("PM scheduler created {Count} work orders for tenant {Tenant}", created, tenantId);
        }
    }
}
