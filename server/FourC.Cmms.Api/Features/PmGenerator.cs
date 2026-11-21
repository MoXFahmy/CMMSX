using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Tenancy;
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
        bool dryRun, CancellationToken ct, EmailOutbox? mail = null)
    {
        var today = Today;
        var horizon = until ?? today.AddDays(366);
        var q = db.PmSchedules.AsNoTracking().Where(s => s.IsActive && s.NextDueDate != null && s.NextDueDate <= horizon && (s.Type != PmScheduleType.Meter || s.TimeBased));
        if (siteId is not null) q = q.Where(s => s.SiteId == siteId);
        if (ids is { Count: > 0 }) q = q.Where(s => ids.Contains(s.Id));
        var candidates = await q.OrderBy(s => s.NextDueDate).Select(s => s.Id).ToListAsync(ct);
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var results = new List<Result>();
        foreach (var id in candidates)
        {
            try
            {
                var query = db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Asset).Include(s => s.Assets).Where(s => s.Id == id);
                var s = dryRun ? await query.AsNoTracking().FirstAsync(ct) : await query.FirstAsync(ct);
                results.AddRange(await GenerateForAsync(db, ctx, s, until ?? today.AddDays(Math.Max(0, s.LeadDays)), dryRun, settings, ct, mail));
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

    private static async Task<List<Result>> GenerateForAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, DateOnly limit, bool dryRun, TenantSettings settings,
        CancellationToken ct, EmailOutbox? mail)
    {
        var results = new List<Result>();
        if (s.NextDueDate is not { } first || first > limit) return results;
        if (s.Basis == PmBasis.Actual && await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && Active.Contains(w.Status), ct))
            return [new Result(s.Id, s.Code, s.Name, first, null, s.Asset?.AssetNumber, null, null, "Previous PM work order is still open")];
        IReadOnlyList<RoundAsset>? round = null;
        string? roundSkip = null;
        if (s.Assets.Count > 0 && (round = await RoundAssetsAsync(db, s, ct, settings.PmSkipAssetStatuses)).Count == 0)
        {
            // Every available asset is in a skip status (e.g. all under repair): skip like a single asset would, instead of piling up work.
            if ((await RoundAssetsAsync(db, s, ct)).Count == 0) return [new Result(s.Id, s.Code, s.Name, first, null, null, null, null, NoActiveAssets)];
            roundSkip = "every asset on the round has a status that skips PM work";
        }
        var taskIds = s.ExtraTasks.Select(x => x.PmTaskId).Append(s.PmTaskId).Distinct().ToList();
        var tasks = await db.PmTasks.AsNoTracking().Include(t => t.Steps).Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        for (var guard = 0; guard < 120 && s.NextDueDate is { } due && due <= limit; guard++)
        {
            var count = s.GenerationCount + 1;
            var task = tasks.GetValueOrDefault(PmCalendar.TaskFor(s, count)) ?? tasks.GetValueOrDefault(s.PmTaskId);
            WorkOrder? wo = null;
            var exists = await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && w.PmDueDate == due, ct);
            var skip = exists || task is null ? null : roundSkip ?? await SkipReasonAsync(db, s, settings, ct);
            if (skip is not null && due > Today)
            {
                // Generated ahead (lead time, "Generate now"): the rule may no longer apply by the due date, so wait instead of skipping for good.
                results.Add(new Result(s.Id, s.Code, s.Name, due, task?.Name, s.Asset?.AssetNumber, null, null, $"Waiting: {skip}"));
                break;
            }
            if (skip is not null)
            {
                // A skipped occurrence moves the schedule on but is not a generation, so "every Nth" tasks are not lost.
                results.Add(new Result(s.Id, s.Code, s.Name, due, task?.Name, s.Asset?.AssetNumber, null, null, $"Skipped: {skip}"));
                s.LastDueDate = due;
                s.NextDueDate = PmCalendar.Next(s, due);
                if (!dryRun) await db.SaveChangesAsync(ct);
                if (s.Basis == PmBasis.Actual) break;
                continue;
            }
            if (!exists && !dryRun && task is not null) wo = await CreateWorkOrderAsync(db, ctx, s, task, due, null, ct, round, settings.PmNonWorkingDays, mail);
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

    public const string NoActiveAssets = "No active assets in the list";

    /// <summary>Why the organization's PM rules hold back this occurrence, or null to generate it.</summary>
    private static async Task<string?> SkipReasonAsync(CmmsDbContext db, PmSchedule s, TenantSettings settings, CancellationToken ct)
    {
        if (s.Assets.Count == 0 && s.Asset is { } a && settings.PmSkipAssetStatuses.Contains(a.Status))
            return $"the asset is {StatusLabel(a.Status)}";
        if (settings.PmNoStacking && s.Basis == PmBasis.Scheduled && await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && Active.Contains(w.Status), ct))
            return "the previous work order is still open";
        return null;
    }

    private static string StatusLabel(Core.Assets.AssetStatus st) => st switch
    {
        Core.Assets.AssetStatus.UnderRepair => "Under repair",
        Core.Assets.AssetStatus.InStorage => "In storage",
        _ => st.ToString(),
    };

    /// <param name="round">For a round schedule, its covered assets if already loaded; loaded here otherwise.</param>
    /// <param name="nonWorkingDays">The work starts on the first other day; <paramref name="due"/> stays the occurrence's key.</param>
    public static async Task<WorkOrder> CreateWorkOrderAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, PmTask task, DateOnly? due, string? trigger, CancellationToken ct,
        IReadOnlyList<RoundAsset>? round = null, IReadOnlyCollection<DayOfWeek>? nonWorkingDays = null, EmailOutbox? mail = null)
    {
        if (s.Assets.Count > 0) round ??= await RoundAssetsAsync(db, s, ct);
        else round = null;
        var assignees = s.AssigneeIds.Count == 0 ? [] : await db.Employees.Where(e => s.AssigneeIds.Contains(e.Id) && e.IsActive).Select(e => e.Id).ToListAsync(ct);
        var description = string.Join("\n\n", new[] { task.Description ?? s.Description, task.SafetyNotes is { Length: > 0 } sn ? $"Safety: {sn}" : null, trigger }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var title = task.Id == s.PmTaskId ? s.Name : $"{s.Name} — {task.Name}";
        var start = PmCalendar.NextWorkingDay(due ?? Today, nonWorkingDays ?? []);
        var steps = task.Steps.OrderBy(x => x.Sequence).Select(x => x.Description).ToList();
        // A round's work order has no single asset: it lists the covered assets and repeats the steps for each of them.
        var input = new WorkOrderEndpoints.WorkOrderInput(title.Length > 300 ? title[..300] : title, description.Length == 0 ? null : description, WorkType.Preventive, s.Priority,
            s.SiteId, round is null ? s.LocationId : s.LocationId ?? PmRound.CommonLocationId(round), round is null ? s.AssetId : null, s.TradeId ?? task.TradeId, assignees,
            start, PmCalendar.DueDate(s, start, nonWorkingDays ?? []), task.EstimatedHours, round is null ? steps : null);
        // The task's parts come along as planned parts (× covered assets on a round), reserved in the default store for the site.
        var planned = new List<WorkOrderPart>();
        foreach (var tp in await db.PmTaskParts.AsNoTracking().Where(p => p.PmTaskId == task.Id).Select(p => new { p.PartId, p.Quantity }).ToListAsync(ct))
            planned.Add(new WorkOrderPart { PartId = tp.PartId, Quantity = tp.Quantity * (round?.Count ?? 1), StoreId = await Planning.DefaultStoreAsync(db, tp.PartId, s.SiteId, ct) });
        return await WorkOrderEndpoints.CreateAsync(db, ctx, input, null, ct, w =>
        {
            w.PmScheduleId = s.Id; w.PmTaskId = task.Id; w.PmDueDate = due;
            foreach (var p in planned) { p.WorkOrderId = w.Id; db.WorkOrderParts.Add(p); }
            if (round is null) return;
            for (var n = 0; n < round.Count; n++) w.Assets.Add(new WorkOrderAsset { WorkOrderId = w.Id, AssetId = round[n].Id, Sequence = n + 1 });
            foreach (var st in PmRound.Steps(round, steps))
                db.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderId = w.Id, Sequence = st.Sequence, AssetId = st.AssetId, Description = st.Description.Length > 1000 ? st.Description[..1000] : st.Description });
        }, $"Generated from PM {s.Code}", mail, applyTargets: false);
    }

    /// <summary>The assets a round's next work order covers (deleted assets drop out through the query filter).</summary>
    internal static async Task<IReadOnlyList<RoundAsset>> RoundAssetsAsync(CmmsDbContext db, PmSchedule s, CancellationToken ct,
        IReadOnlyCollection<Core.Assets.AssetStatus>? skipStatuses = null)
    {
        var sequence = s.Assets.ToDictionary(a => a.AssetId, a => a.Sequence);
        var ids = sequence.Keys.ToList();
        var rows = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.SiteId, a.Status, a.LocationId, Path = a.Location != null ? a.Location.Path : null }).ToListAsync(ct);
        return PmRound.Covered(rows.Select(a => new RoundAsset(a.Id, sequence[a.Id], a.SiteId, a.Path, a.LocationId,
            a.Status is not (Core.Assets.AssetStatus.Disposed or Core.Assets.AssetStatus.Inactive) && skipStatuses?.Contains(a.Status) != true)), s.SiteId);
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
    public static async Task<List<Result>> OnReadingAsync(CmmsDbContext db, ITenantContext ctx, Meter meter, decimal value, CancellationToken ct, EmailOutbox? mail = null)
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
            var wo = await CreateWorkOrderAsync(db, ctx, s, task, null, why, ct, mail: mail);
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
        {
            tenantIds = (await root.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct)).Where(t => t.Settings.AutoGeneratePm).Select(t => t.Id).ToList();
            // Due dates written without a due time since the last run (an older server during a deploy, an import) get theirs.
            await FourC.Cmms.Core.Services.DueBackfill.RunAsync(root, ct);
            await Housekeeping.TrimAsync(root, DateTimeOffset.UtcNow, ct);
        }
        foreach (var tenantId in tenantIds)
        {
            await using var db = new CmmsDbContext(options, new FixedTenantContext(tenantId, null, "PM scheduler"));
            var created = (await PmGenerator.RunAsync(db, new FixedTenantContext(tenantId, null, "PM scheduler"), null, null, null, false, ct,
                scope.ServiceProvider.GetService<EmailOutbox>())).Count(r => r.WorkOrderId is not null);
            if (created > 0) log.LogInformation("PM scheduler created {Count} work orders for tenant {Tenant}", created, tenantId);
        }
    }
}

/// <summary>Estimated cost of one PM visit.</summary>
public sealed record VisitCost(decimal Parts, decimal Labour, decimal Total);

public static class PmCost
{
    /// <summary>
    /// A visit's estimated cost: the task's parts at current average cost (× covered assets on a round) plus its estimated hours
    /// at the average of the known hourly rates (technicians without a rate are left out).
    /// </summary>
    public static VisitCost Visit(IEnumerable<(decimal Quantity, decimal AverageCost)> parts, int multiplier, decimal? estimatedHours, IEnumerable<decimal?> rates)
    {
        var partsCost = Math.Round(parts.Sum(p => p.Quantity * p.AverageCost) * multiplier, 2);
        var labour = Math.Round((estimatedHours ?? 0) * Planning.AverageRate(rates), 2);
        return new VisitCost(partsCost, labour, partsCost + labour);
    }

    /// <summary>
    /// The visit cost of each schedule and task pair, with a fixed number of queries for the whole list (task parts, task hours,
    /// technicians' rates and round assets are each read once). Schedules need their ExtraTasks and Assets loaded.
    /// </summary>
    public static async Task<Dictionary<(Guid ScheduleId, Guid TaskId), VisitCost>> ForSchedulesAsync(CmmsDbContext db, IReadOnlyCollection<PmSchedule> schedules, CancellationToken ct)
    {
        var pairs = schedules.SelectMany(s => s.ExtraTasks.Select(e => e.PmTaskId).Append(s.PmTaskId).Distinct().Select(t => (S: s, TaskId: t))).ToList();
        var taskIds = pairs.Select(p => p.TaskId).Distinct().ToList();
        var parts = (await db.PmTaskParts.AsNoTracking().Where(p => taskIds.Contains(p.PmTaskId)).Select(p => new { p.PmTaskId, p.Quantity, p.Part!.AverageCost }).ToListAsync(ct))
            .ToLookup(p => p.PmTaskId, p => (p.Quantity, p.AverageCost));
        var hours = await db.PmTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.EstimatedHours, ct);
        var people = schedules.SelectMany(s => s.AssigneeIds).Distinct().ToList();
        var rates = people.Count == 0 ? [] : await db.Employees.AsNoTracking().Where(e => people.Contains(e.Id) && e.IsActive).ToDictionaryAsync(e => e.Id, e => e.HourlyRate, ct);
        var covered = await CoveredCountsAsync(db, schedules.Where(s => s.Assets.Count > 0).ToList(), ct);
        return pairs.ToDictionary(p => (p.S.Id, p.TaskId), p => Visit(parts[p.TaskId], covered.GetValueOrDefault(p.S.Id, 1), hours.GetValueOrDefault(p.TaskId),
            p.S.AssigneeIds.Where(rates.ContainsKey).Select(id => rates[id])));
    }

    /// <summary>How many assets each round's next work order would cover (as generation counts them), from one query.</summary>
    private static async Task<Dictionary<Guid, int>> CoveredCountsAsync(CmmsDbContext db, IReadOnlyList<PmSchedule> rounds, CancellationToken ct)
    {
        if (rounds.Count == 0) return [];
        var ids = rounds.SelectMany(s => s.Assets.Select(a => a.AssetId)).Distinct().ToList();
        var rows = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.SiteId, a.Status, a.LocationId, Path = a.Location != null ? a.Location.Path : null }).ToDictionaryAsync(a => a.Id, ct);
        return rounds.ToDictionary(s => s.Id, s => PmRound.Covered(s.Assets.Where(a => rows.ContainsKey(a.AssetId)).Select(a =>
        {
            var r = rows[a.AssetId];
            return new RoundAsset(r.Id, a.Sequence, r.SiteId, r.Path, r.LocationId, r.Status is not (Core.Assets.AssetStatus.Disposed or Core.Assets.AssetStatus.Inactive));
        }), s.SiteId).Count);
    }
}
