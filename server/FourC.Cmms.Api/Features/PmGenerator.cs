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
        Guid? WorkOrderId, string? WorkOrderNumber, string? Skipped, Guid? GroupId = null, string? GroupNumber = null, int Children = 0)
    {
        /// <summary>Work orders this result made: its own, or its group's (0.12.0).</summary>
        public int Made => WorkOrderId is not null ? 1 : Children;
    }

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
        var q = db.PmSchedules.AsNoTracking().Where(s => s.IsActive && s.NextDueDate != null && s.NextDueDate <= horizon
            && (s.Type != PmScheduleType.Meter || s.TimeBased || s.Calendars.Any()));
        if (siteId is not null) q = q.Where(s => s.SiteId == siteId);
        if (ids is { Count: > 0 }) q = q.Where(s => ids.Contains(s.Id));
        var candidates = await q.OrderBy(s => s.NextDueDate).Select(s => s.Id).ToListAsync(ct);
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var results = new List<Result>();
        foreach (var id in candidates)
        {
            try
            {
                var query = db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Asset).Include(s => s.Assets).Include(s => s.Calendars).Include(s => s.ExtraMeters)
                    .AsSplitQuery().Where(s => s.Id == id);
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
        var rules = Rules(s, settings);
        if (s.Assets.Count > 0 && (round = await RoundAssetsAsync(db, s, ct, rules.Skip)).Count == 0)
        {
            // Every available asset is in a skip status (e.g. all under repair): skip like a single asset would, instead of piling up work.
            if ((await RoundAssetsAsync(db, s, ct)).Count == 0) return [new Result(s.Id, s.Code, s.Name, first, null, null, null, null, NoActiveAssets)];
            roundSkip = "every asset on the round has a status that skips PM work";
        }
        var taskIds = s.ExtraTasks.Select(x => x.PmTaskId).Append(s.PmTaskId).Distinct().ToList();
        var tasks = await db.PmTasks.AsNoTracking().Include(t => t.Steps).Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var calendars = await PmEndpoints.CalendarsAsync(db, s.Calendars.Select(c => c.ScheduleCalendarId), ct);
        // Spread blocks come from the whole list (assets in a skip status included), so a skipped asset doesn't shift the others' parts.
        var listed = PmTriggers.Spreads(s) && round is not null ? await RoundAssetsAsync(db, s, ct) : null;
        DateOnly? last = null;
        for (var guard = 0; guard < 120 && s.NextDueDate is { } due && due <= limit; guard++)
        {
            last = due;
            var count = s.GenerationCount + 1;
            var task = tasks.GetValueOrDefault(PmCalendar.TaskFor(s, count)) ?? tasks.GetValueOrDefault(s.PmTaskId);
            // Even spread (0.12.0): this occurrence covers one block of the assets; "every Nth" counts periods.
            var spread = PmTriggers.Spreads(s) && round is not null;
            if (spread && s.CycleStart is null) { s.CycleStart = due; s.SpreadIndex = 0; }
            var part = round;
            if (spread)
            {
                var (from, size) = PmTriggers.Spread(listed!.Count, s.SpreadParts!.Value, Math.Min(s.SpreadIndex, s.SpreadParts.Value - 1));
                var available = round!.Select(a => a.Id).ToHashSet();
                part = listed.Skip(from).Take(size).Where(a => available.Contains(a.Id)).ToList();
            }
            WorkOrder? wo = null;
            var exists = await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && w.PmDueDate == due, ct)
                || await db.WorkOrderGroups.AnyAsync(g => g.PmScheduleId == s.Id && g.PmDueDate == due, ct);
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
                if (spread) AdvanceSpread(s, countPeriod: false);
                else s.NextDueDate = PmTriggers.Next(s, due, calendars);
                if (!dryRun) await db.SaveChangesAsync(ct);
                if (s.Basis == PmBasis.Actual) break;
                continue;
            }
            WorkOrderGroup? group = null;
            if (!exists && !dryRun && task is not null)
            {
                // A group PM (0.12.0): a group with a work order per asset, instead of one round work order.
                if (part is { Count: 0 }) { }
                else if (s.MultiAssetMode == MultiAssetMode.Group && part is { Count: > 0 }) group = await WorkGroups.CreateGroupAsync(db, ctx, s, task, due, part, rules.NonWorking, ct, mail);
                else wo = await CreateWorkOrderAsync(db, ctx, s, task, due, null, ct, part, rules.NonWorking, mail);
            }
            results.Add(new Result(s.Id, s.Code, s.Name, due, task?.Name, s.Asset?.AssetNumber, wo?.Id, wo?.Number,
                task is null ? "PM task not found" : exists ? "Already generated" : part is { Count: 0 } ? "No active assets in this part" : null, group?.Id, group?.Number,
                group is null ? 0 : part!.Count));
            s.LastDueDate = due; s.LastGeneratedAt = DateTimeOffset.UtcNow; s.LastWorkOrderId = wo?.Id ?? s.LastWorkOrderId;
            if (spread) AdvanceSpread(s, countPeriod: true);
            else
            {
                s.GenerationCount = count;
                s.NextDueDate = s.Basis == PmBasis.Scheduled ? PmTriggers.Next(s, due, calendars) : null;
            }
            if (s.Basis == PmBasis.Scheduled && !dryRun) await RestartMetersAsync(db, s, null, 0, ct); // whichever comes first: every meter starts again
            if (!dryRun) await db.SaveChangesAsync(ct);
            if (s.Basis == PmBasis.Actual) break;
        }
        // Its calendars have no more dates (0.12.0): say so instead of going quiet.
        if (last is { } lastDue && s.NextDueDate is null && s.Basis == PmBasis.Scheduled && (s.Type == PmScheduleType.Calendar || calendars.Count > 0))
            results.Add(new Result(s.Id, s.Code, s.Name, null, null, s.Asset?.AssetNumber, null, null, string.Format(NoNextDate, lastDue.ToString("yyyy-MM-dd"))));
        return results;
    }

    /// <summary>
    /// Even spread: moves to the next part of the period, or to the first part of the next period (counted as a generation for
    /// "every Nth" when <paramref name="countPeriod"/>).
    /// </summary>
    private static void AdvanceSpread(PmSchedule s, bool countPeriod)
    {
        var parts = s.SpreadParts!.Value;
        var cycle = s.CycleStart!.Value;
        var next = PmCalendar.Next(s, cycle);
        if (s.SpreadIndex + 1 < parts)
        {
            s.SpreadIndex++;
            s.NextDueDate = PmTriggers.SpreadDate(cycle, next, parts, s.SpreadIndex);
            return;
        }
        if (countPeriod) s.GenerationCount++;
        s.CycleStart = next; s.SpreadIndex = 0; s.NextDueDate = next;
    }

    public const string NoNextDate = "No next date: its calendars have no dates after {0}.";
    public const string AlreadyGenerated = "This occurrence was already generated as {0}.";
    public const string NotAnOccurrence = "This date isn't one of the PM's next occurrences.";
    public const string SpreadInOrder = "Generate a spread PM's occurrences in order.";

    /// <summary>
    /// Generates one of the PM's next occurrences now (0.12.0). The next one goes through normal generation (the PM moves on); a
    /// later one is made out of order and is found as "Already generated" when its turn comes.
    /// </summary>
    public static async Task<Result> GenerateOccurrenceAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, DateOnly date, CancellationToken ct, EmailOutbox? mail = null)
    {
        var existing = await db.WorkOrders.Where(w => w.PmScheduleId == s.Id && w.PmDueDate == date && w.GroupId == null).Select(w => w.Number).FirstOrDefaultAsync(ct)
            ?? await db.WorkOrderGroups.Where(g => g.PmScheduleId == s.Id && g.PmDueDate == date).Select(g => g.Number).FirstOrDefaultAsync(ct);
        if (existing is not null) throw new DomainException(string.Format(AlreadyGenerated, existing));
        var calendars = await PmEndpoints.CalendarsAsync(db, s.Calendars.Select(c => c.ScheduleCalendarId), ct);
        var dates = PmTriggers.Forecast(s, date, 500, calendars).ToList();
        var k = dates.IndexOf(date);
        if (k < 0) throw new DomainException(NotAnOccurrence);
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        if (k == 0)
        {
            var made = await GenerateForAsync(db, ctx, s, date, false, settings, ct, mail);
            return made.FirstOrDefault() ?? new Result(s.Id, s.Code, s.Name, date, null, s.Asset?.AssetNumber, null, null, "Nothing to generate");
        }
        if (PmTriggers.Spreads(s)) throw new DomainException(SpreadInOrder);
        var rules = Rules(s, settings);
        var taskId = PmCalendar.TaskFor(s, s.GenerationCount + 1 + k);
        var task = await db.PmTasks.AsNoTracking().Include(t => t.Steps).FirstAsync(t => t.Id == taskId, ct);
        IReadOnlyList<RoundAsset>? round = null;
        if (s.Assets.Count > 0 && (round = await RoundAssetsAsync(db, s, ct, rules.Skip)).Count == 0) throw new DomainException(NoActiveAssets);
        if (s.MultiAssetMode == MultiAssetMode.Group && round is { Count: > 0 })
        {
            var group = await WorkGroups.CreateGroupAsync(db, ctx, s, task, date, round, rules.NonWorking, ct, mail);
            return new Result(s.Id, s.Code, s.Name, date, task.Name, null, null, null, null, group.Id, group.Number, round.Count);
        }
        var wo = await CreateWorkOrderAsync(db, ctx, s, task, date, null, ct, round, rules.NonWorking, mail);
        return new Result(s.Id, s.Code, s.Name, date, task.Name, s.Asset?.AssetNumber, wo.Id, wo.Number, null);
    }

    /// <summary>
    /// Restarts every incremental meter trigger of <paramref name="s"/> (its own meter and its extra meters, 0.12.0) at the meter's
    /// current reading plus the interval; <paramref name="readMeter"/> uses <paramref name="value"/>, the reading just taken.
    /// </summary>
    private static async Task RestartMetersAsync(CmmsDbContext db, PmSchedule s, Guid? readMeter, decimal value, CancellationToken ct)
    {
        async Task<decimal> CurrentAsync(Guid meterId) => meterId == readMeter ? value
            : await db.Meters.Where(m => m.Id == meterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0;
        if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental && s.MeterId is { } own && s.MeterInterval is { } iv)
            s.NextDueReading = await CurrentAsync(own) + iv;
        foreach (var m in s.ExtraMeters) m.NextDueReading = await CurrentAsync(m.MeterId) + m.Interval;
    }

    public const string NoActiveAssets = "No active assets in the list";

    /// <summary>The PM rules that apply to <paramref name="s"/>: its own (0.12.0) where it has them, else the organization's.</summary>
    internal static (IReadOnlyCollection<Core.Assets.AssetStatus> Skip, IReadOnlyCollection<DayOfWeek> NonWorking, bool NoStacking) Rules(PmSchedule s, TenantSettings org) =>
        (s.SkipStatusesOverride ?? org.PmSkipAssetStatuses, s.NonWorkingDaysOverride ?? org.PmNonWorkingDays, s.NoStackingOverride ?? org.PmNoStacking);

    /// <summary>Why the organization's PM rules hold back this occurrence, or null to generate it.</summary>
    private static async Task<string?> SkipReasonAsync(CmmsDbContext db, PmSchedule s, TenantSettings settings, CancellationToken ct)
    {
        var rules = Rules(s, settings);
        if (s.Assets.Count == 0 && s.Asset is { } a && rules.Skip.Contains(a.Status))
            return $"the asset is {StatusLabel(a.Status)}";
        if (rules.NoStacking && s.Basis == PmBasis.Scheduled && await db.WorkOrders.AnyAsync(w => w.PmScheduleId == s.Id && Active.Contains(w.Status), ct))
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
        // Told about the work (0.12.0): the template's and the PM's recipients who are still active users.
        var wanted = task.RecipientIds.Concat(s.RecipientIds).Distinct().ToList();
        var recipients = wanted.Count == 0 ? [] : await db.Users.Where(u => wanted.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId).Select(u => u.Id).ToListAsync(ct);
        var description = string.Join("\n\n", new[] { task.Description ?? s.Description, task.SafetyNotes is { Length: > 0 } sn ? $"Safety: {sn}" : null, trigger }.Where(x => !string.IsNullOrWhiteSpace(x)));
        // The template's own title wins (0.12.0); otherwise the PM's name, with the template's for an every-Nth one.
        var title = task.Title is { Length: > 0 } tt ? tt : task.Id == s.PmTaskId ? s.Name : $"{s.Name} — {task.Name}";
        var start = PmCalendar.NextWorkingDay(due ?? Today, nonWorkingDays ?? []);
        var steps = task.Steps.OrderBy(x => x.Sequence).ToList();
        // A round's assets with their own template (0.12.0) use it on routine visits; an every-Nth visit uses the Nth template for all.
        var own = round is null || task.Id != s.PmTaskId ? [] : s.Assets.Where(a => a.PmTaskId is not null && round.Any(r => r.Id == a.AssetId))
            .ToDictionary(a => a.AssetId, a => a.PmTaskId!.Value);
        var ownIds = own.Values.Distinct().ToList();
        var ownTemplates = ownIds.Count == 0 ? [] : await db.PmTasks.AsNoTracking().Include(t => t.Steps).Include(t => t.Parts).Where(t => ownIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        IReadOnlyList<PmTaskStep> StepsFor(Guid assetId) => own.TryGetValue(assetId, out var tid) && ownTemplates.TryGetValue(tid, out var ot)
            ? ot.Steps.OrderBy(x => x.Sequence).ToList() : steps;
        // A round's work order has no single asset: it lists the covered assets and repeats the steps for each of them.
        var input = new WorkOrderEndpoints.WorkOrderInput(title.Length > 300 ? title[..300] : title, description.Length == 0 ? null : description, task.WorkType, task.Priority ?? s.Priority,
            s.SiteId, round is null ? s.LocationId : s.LocationId ?? PmRound.CommonLocationId(round), round is null ? s.AssetId : null, s.TradeId ?? task.TradeId, assignees,
            start, PmCalendar.DueDate(s, start, nonWorkingDays ?? []), task.EstimatedHours, null, TeamId: s.TeamId, CostCentreId: s.CostCentreId);
        // The template's parts come along as planned parts (once per covered asset on a round, from that asset's own template where
        // it has one), reserved in the default store for the site.
        var taskParts = await db.PmTaskParts.AsNoTracking().Where(p => p.PmTaskId == task.Id).Select(p => new { p.PartId, p.Quantity }).ToListAsync(ct);
        var needs = new Dictionary<Guid, decimal>();
        foreach (var assetId in round?.Select(r => (Guid?)r.Id) ?? [null])
        {
            var lines = assetId is { } aid && own.TryGetValue(aid, out var tid) && ownTemplates.TryGetValue(tid, out var ot)
                ? ot.Parts.Select(p => new { p.PartId, p.Quantity }).ToList() : taskParts;
            foreach (var l in lines) needs[l.PartId] = needs.GetValueOrDefault(l.PartId) + l.Quantity;
        }
        var planned = new List<WorkOrderPart>();
        foreach (var (partId, qty) in needs)
            planned.Add(new WorkOrderPart { PartId = partId, Quantity = qty, StoreId = await Planning.DefaultStoreAsync(db, partId, s.SiteId, ct) });
        return await WorkOrderEndpoints.CreateAsync(db, ctx, input, null, ct, w =>
        {
            w.PmScheduleId = s.Id; w.PmTaskId = task.Id; w.PmDueDate = due; w.OnlyWhenStopped = s.OnlyWhenStopped; w.RecipientIds = recipients; w.ContractId = s.ContractId;
            foreach (var p in planned) { p.WorkOrderId = w.Id; db.WorkOrderParts.Add(p); }
            if (round is null) return;
            for (var n = 0; n < round.Count; n++) w.Assets.Add(new WorkOrderAsset { WorkOrderId = w.Id, AssetId = round[n].Id, Sequence = n + 1 });
            foreach (var st in PmRound.TypedSteps(round, StepsFor)) db.WorkOrderTasks.Add(StepCopy.ToWorkOrder(st.Step, w.Id, st.Sequence, st.AssetId));
        }, $"Generated from PM {s.Code}", mail, applyTargets: false, typedSteps: round is null ? steps : []);
    }

    /// <summary>The assets a round's next work order covers (deleted assets drop out through the organization filter; site limits don't apply).</summary>
    internal static async Task<IReadOnlyList<RoundAsset>> RoundAssetsAsync(CmmsDbContext db, PmSchedule s, CancellationToken ct,
        IReadOnlyCollection<Core.Assets.AssetStatus>? skipStatuses = null)
    {
        var sequence = s.Assets.ToDictionary(a => a.AssetId, a => a.Sequence);
        var ids = sequence.Keys.ToList();
        var rows = await db.Assets.AllSites().AsNoTracking().Where(a => ids.Contains(a.Id)) // every asset on the round, also those in branches the person can't see
            .Select(a => new { a.Id, a.SiteId, a.Status, a.LocationId, Path = a.Location != null ? a.Location.Path : null }).ToListAsync(ct);
        return PmRound.Covered(rows.Select(a => new RoundAsset(a.Id, sequence[a.Id], a.SiteId, a.Path, a.LocationId,
            a.Status is not (Core.Assets.AssetStatus.Disposed or Core.Assets.AssetStatus.Inactive) && skipStatuses?.Contains(a.Status) != true)), s.SiteId);
    }

    /// <summary>"Actual" schedules restart from the completion (or cancellation) of their latest work order.</summary>
    public static async Task OnWorkOrderFinishedAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        var s = await db.PmSchedules.Include(x => x.ExtraMeters).FirstOrDefaultAsync(x => x.Id == w.PmScheduleId, ct);
        if (s is null || !s.IsActive || s.Basis != PmBasis.Actual || (s.LastWorkOrderId is { } last && last != w.Id)) return;
        var done = DateOnly.FromDateTime((w.CompletedAt ?? DateTimeOffset.UtcNow).UtcDateTime);
        if (PmCalendar.UsesCalendar(s)) s.NextDueDate = PmCalendar.Next(s, done); // calendars need the Scheduled basis
        await RestartMetersAsync(db, s, null, 0, ct);
    }

    /// <summary>Evaluates meter-based schedules after a reading; returns the work orders it created.</summary>
    public static async Task<List<Result>> OnReadingAsync(CmmsDbContext db, ITenantContext ctx, Meter meter, decimal value, CancellationToken ct, EmailOutbox? mail = null)
    {
        var results = new List<Result>();
        // PMs on this meter: as their own meter, or as an extra meter trigger (0.12.0).
        var schedules = await db.PmSchedules.Include(s => s.ExtraTasks).Include(s => s.Asset).Include(s => s.ExtraMeters).Include(s => s.Calendars).AsSplitQuery()
            .Where(s => s.IsActive && ((s.MeterId == meter.Id && s.Type == PmScheduleType.Meter) || s.ExtraMeters.Any(m => m.MeterId == meter.Id))).ToListAsync(ct);
        foreach (var s in schedules)
        {
            Guid? taskId = null;
            string? why = null;
            var extraRow = s.ExtraMeters.FirstOrDefault(m => m.MeterId == meter.Id);
            if (!(s.MeterId == meter.Id && s.Type == PmScheduleType.Meter))
            {
                if (extraRow?.NextDueReading is { } extraDue && value >= extraDue)
                {
                    taskId = PmCalendar.TaskFor(s, s.GenerationCount + 1);
                    why = $"Meter reached {value:0.###} {meter.Unit} (due at {extraDue:0.###}).";
                }
            }
            else if (s.MeterTrigger == MeterTrigger.Threshold)
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
            if (s.Basis == PmBasis.Scheduled)
            {
                // Whichever comes first: every meter starts again, the PM's own clock restarts today and its calendars keep their dates.
                await RestartMetersAsync(db, s, meter.Id, value, ct);
                var calendars = await PmEndpoints.CalendarsAsync(db, s.Calendars.Select(c => c.ScheduleCalendarId), ct);
                if (PmTriggers.UsesDates(s, calendars.Count)) s.NextDueDate = PmTriggers.Next(s, Today, calendars);
            }
            else
            {
                if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental) s.NextDueReading = null;
                foreach (var m in s.ExtraMeters) m.NextDueReading = null;
                if (s.TimeBased) s.NextDueDate = null;
            }
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
                scope.ServiceProvider.GetService<EmailOutbox>())).Sum(r => r.Made);
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
        var rows = await db.Assets.AllSites().AsNoTracking().Where(a => ids.Contains(a.Id)) // every asset on the round, also those in branches the person can't see
            .Select(a => new { a.Id, a.SiteId, a.Status, a.LocationId, Path = a.Location != null ? a.Location.Path : null }).ToDictionaryAsync(a => a.Id, ct);
        return rounds.ToDictionary(s => s.Id, s => PmRound.Covered(s.Assets.Where(a => rows.ContainsKey(a.AssetId)).Select(a =>
        {
            var r = rows[a.AssetId];
            return new RoundAsset(r.Id, a.Sequence, r.SiteId, r.Path, r.LocationId, r.Status is not (Core.Assets.AssetStatus.Disposed or Core.Assets.AssetStatus.Inactive));
        }), s.SiteId).Count);
    }
}
