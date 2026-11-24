using System.Text.Json;
using FourC.Cmms.Core.Auditing;
using System.Linq.Expressions;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class PmEndpoints
{
    /// <param name="Parts">The parts each generated work order needs; null on an update keeps the task's parts.</param>
    public sealed record TaskInput(string Code, string Name, string? Description, Guid? TradeId, decimal? EstimatedHours, string? SafetyNotes, List<string>? Steps, bool IsActive = true,
        string? RowVersion = null, List<TaskPartInput>? Parts = null, string? Title = null, WorkType? WorkType = null, WorkPriority? Priority = null,
        List<Guid>? RecipientIds = null, decimal? ExpectedStopHours = null, List<StepInput>? StepDetails = null);
    /// <summary>A typed template step (0.13.0); <c>TaskInput.StepDetails</c> wins over the plain <c>Steps</c> texts.</summary>
    public sealed record StepInput(string Description, StepType Type = StepType.Check, string? Unit = null, int? Decimals = null, Guid? MeasurementPointId = null,
        bool PhotoRequired = false, bool BeforeStart = false, FollowUpKind FollowUp = FollowUpKind.Default, Guid? FollowUpTemplateId = null);
    public sealed record TaskPartInput(Guid PartId, decimal Quantity);
    public const string NeedsTemplate = "Choose a work template for this follow-up.";
    public const string UsedForFollowUps = "This template is used for follow-up work. Mark it inactive instead.";
    public sealed record ExtraTaskInput(Guid PmTaskId, int EveryNth);
    public sealed record ScheduleInput(string? Code, string Name, string? Description, Guid SiteId, Guid? LocationId, Guid? AssetId, Guid? TradeId, WorkPriority Priority,
        List<Guid>? AssigneeIds, Guid PmTaskId, List<ExtraTaskInput>? ExtraTasks, PmScheduleType Type, PmBasis Basis, int Frequency, FrequencyUnit Unit,
        int? WeekOfMonth, DayOfWeek? DayOfWeek, int? DayOfMonth, int WorkPeriodDays, bool WholeMonth, int LeadDays, DateOnly StartDate, DateOnly? NextDueDate,
        Guid? MeterId, MeterTrigger? MeterTrigger, decimal? MeterInterval, decimal? NextDueReading, decimal? LowerLimit, decimal? UpperLimit,
        Guid? LowerTaskId, Guid? UpperTaskId, bool TimeBased, bool IsActive = true, string? RowVersion = null, List<Guid>? AssetIds = null, Guid? TeamId = null,
        bool? ManagesTeam = null, bool? OnlyWhenStopped = null, List<Guid>? RecipientIds = null,
        bool? ManagesRules = null, List<Core.Assets.AssetStatus>? SkipStatusesOverride = null, List<DayOfWeek>? NonWorkingDaysOverride = null, bool? NoStackingOverride = null,
        List<AssetTemplateInput>? AssetTemplates = null, List<Guid>? CalendarIds = null, List<ExtraMeterInput>? ExtraMeters = null,
        MultiAssetMode? MultiAssetMode = null, GroupPlan? GroupPlan = null, int? SpreadParts = null, Guid? ContractId = null, bool? ManagesContract = null,
        Guid? CostCentreId = null, bool? ManagesCostCentre = null, Guid? AssetGroupId = null, bool? ManagesAssetGroup = null);
    /// <summary>A meter trigger on top of the PM's own (0.12.0), whichever comes first.</summary>
    public sealed record ExtraMeterInput(Guid MeterId, decimal Interval, decimal? NextDueReading = null);
    /// <summary>A multi-asset PM's asset with its own template (empty = the PM's).</summary>
    public sealed record AssetTemplateInput(Guid AssetId, Guid? PmTaskId);
    public sealed record GenerateInput(DateOnly Until, Guid? SiteId, List<Guid>? ScheduleIds);
    public sealed record OccurrenceInput(DateOnly Date);
    public const int Occurrences = 24;
    /// <param name="ManagesSource">The page sets the meter it follows (0.12.0); otherwise the link is left as it is.</param>
    public sealed record MeterInput(string Code, string Name, Guid AssetId, string Unit, decimal? MaxDailyIncrement, bool IsActive = true, Guid? SourceMeterId = null,
        bool? ManagesSource = null);
    public sealed record ReadingInput(decimal Value, DateTimeOffset? ReadAt, string? Note, bool Confirm = false);

    private static readonly Dictionary<string, Expression<Func<PmSchedule, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = s => s.Code, ["name"] = s => s.Name, ["nextDueDate"] = s => s.NextDueDate, ["asset"] = s => s.Asset!.AssetNumber, ["type"] = s => s.Type,
        ["lastGeneratedAt"] = s => s.LastGeneratedAt,
    };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public sealed class PmScheduleFilter
    {
        public string? Search { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? AssetId { get; set; }
        public PmScheduleType? Type { get; set; }
        public bool? ActiveOnly { get; set; }
        public int? DueWithinDays { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record PmScheduleListItem(Guid Id, string Code, string Name, PmScheduleType Type, PmBasis Basis, bool IsActive, Guid SiteId, string SiteName,
        Guid? AssetId, string? AssetNumber, string? AssetName, string? LocationName, string? TaskCode, string? TaskName, string Schedule, DateOnly? NextDueDate,
        decimal? NextDueReading, string? MeterUnit, decimal? MeterReading, DateTimeOffset? LastGeneratedAt, Guid? LastWorkOrderId, string? LastWorkOrderNumber,
        int OpenWorkOrders, bool IsOverdue, int MultipleTasks, int AssetCount, bool OnlyWhenStopped = false);

    /// <summary>One page of the PM schedule list (the list screen and its export share it).</summary>
    internal static async Task<PagedResult<PmScheduleListItem>> ListAsync(CmmsDbContext db, PmScheduleFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 500)
    {
            var q = db.PmSchedules.AsNoTracking();
        if (f.Search.Clean() is { } s) q = q.Where(x => x.Code.Contains(s) || x.Name.Contains(s) || x.Asset!.AssetNumber.Contains(s));
        if (f.SiteId is not null) q = q.Where(x => x.SiteId == f.SiteId);
        if (f.AssetId is not null) q = q.Where(x => x.AssetId == f.AssetId || (x.Asset != null && x.Asset.ParentAssetId == f.AssetId) || x.Assets.Any(a => a.AssetId == f.AssetId)
            || (x.AssetGroupId != null && db.AssetGroupMembers.Any(m => m.AssetGroupId == x.AssetGroupId && m.AssetId == f.AssetId))); // and PMs on its groups (0.21.0)
        if (f.Type is not null) q = q.Where(x => x.Type == f.Type);
        if (f.ActiveOnly == true) q = q.Where(x => x.IsActive);
        if (f.DueWithinDays is { } d) { var until = Today.AddDays(d); q = q.Where(x => x.IsActive && x.NextDueDate != null && x.NextDueDate <= until); }
        var paged = await q.SortBy(f.Sort, f.Dir, SortMap, "nextDueDate").Include(x => x.Meter).Include(x => x.ExtraTasks)
            .Select(x => new { S = x, SiteName = x.Site!.Name, AssetNumber = x.Asset!.AssetNumber, AssetName = x.Asset.Name, LocationName = x.Location!.Name, TaskCode = x.PmTask!.Code, TaskName = x.PmTask.Name,
                MeterUnit = x.Meter!.Unit, MeterReading = x.Meter.CurrentReading, AssetCount = x.Assets.Count,
                LastWorkOrderNumber = db.WorkOrders.Where(w => w.Id == x.LastWorkOrderId).Select(w => w.Number).FirstOrDefault(),
                OpenWorkOrders = db.WorkOrders.Count(w => w.PmScheduleId == x.Id && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)) })
            .ToPagedAsync(page, pageSize, ct, maxPageSize);
        var today = Today;
        return new PagedResult<PmScheduleListItem>(paged.Items.Select(i => new PmScheduleListItem(
            i.S.Id, i.S.Code, i.S.Name, i.S.Type, i.S.Basis, i.S.IsActive, i.S.SiteId, i.SiteName, i.S.AssetId, i.AssetNumber, i.AssetName, i.LocationName,
            i.TaskCode, i.TaskName, PmCalendar.Describe(i.S, Translator.CurrentLanguage), i.S.NextDueDate, i.S.NextDueReading, i.MeterUnit, i.MeterReading, i.S.LastGeneratedAt,
            i.S.LastWorkOrderId, i.LastWorkOrderNumber, i.OpenWorkOrders, i.S.IsActive && i.S.NextDueDate < today, i.S.ExtraTasks.Count, i.AssetCount, i.S.OnlyWhenStopped)).ToList(),
            paged.Total, paged.Page, paged.PageSize);
    }

    public static void MapPmEndpoints(this RouteGroupBuilder api)
    {
        // ---------------- PM tasks ----------------
        var t = api.MapGroup("/pm-tasks").WithTags("PM tasks");
        t.MapGet("/", async (CmmsDbContext db, string? search, CancellationToken ct) =>
        {
            var q = db.PmTasks.AsNoTracking();
            if (search.Clean() is { } s) q = q.Where(x => x.Code.Contains(s) || x.Name.Contains(s));
            var used = await db.PmSchedules.GroupBy(x => x.PmTaskId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            var list = await q.OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.TradeId, x.EstimatedHours, x.IsActive, StepCount = x.Steps.Count }).ToListAsync(ct);
            return list.Select(x => new { x.Id, x.Code, x.Name, x.TradeId, x.EstimatedHours, x.IsActive, x.StepCount, ScheduleCount = used.GetValueOrDefault(x.Id) });
        }).RequirePermission(Permissions.PmView);
        t.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.PmTasks.AsNoTracking().Include(y => y.Steps).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var parts = await db.PmTaskParts.AsNoTracking().Where(p => p.PmTaskId == id).OrderBy(p => p.Part!.PartNumber)
                .Select(p => new { p.PartId, p.Part!.PartNumber, PartName = p.Part.Name, p.Part.UnitOfMeasure, p.Quantity, UnitCost = p.Part.AverageCost }).ToListAsync(ct);
            return Results.Ok(new
            {
                x.Id, x.Code, x.Name, x.Description, x.TradeId, x.EstimatedHours, x.SafetyNotes, x.IsActive, Steps = x.Steps.OrderBy(s => s.Sequence).Select(s => s.Description),
                StepDetails = x.Steps.OrderBy(s => s.Sequence).Select(s => new StepInput(s.Description, s.Type, s.Unit, s.Decimals, s.MeasurementPointId, s.PhotoRequired,
                    s.BeforeStart, s.FollowUp, s.FollowUpTemplateId)),
                Parts = parts, PartsCost = Math.Round(parts.Sum(p => p.Quantity * p.UnitCost), 2), RowVersion = Convert.ToBase64String(x.RowVersion),
                x.Title, x.WorkType, x.Priority, x.RecipientIds, x.ExpectedStopHours, UsedBy = await UsedByAsync(db, id, ct),
            });
        }).RequirePermission(Permissions.PmView);
        t.MapPost("/", async (TaskInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateTaskAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = CreateTask(db, input);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pm-tasks/{x.Id}", new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        t.MapPut("/{id:guid}", async (Guid id, TaskInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.PmTasks.Include(y => y.Steps).Include(y => y.Parts).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var v = await ValidateTaskAsync(input with { Code = x.Code }, id, db, ct); // task numbers are permanent (as in 4CSQL)
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(x).Property(y => y.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            UpdateTask(db, x, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        t.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.PmTasks.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PmSchedules.AllSites().AnyAsync(s => s.PmTaskId == id || s.LowerTaskId == id || s.UpperTaskId == id || s.ExtraTasks.Any(e => e.PmTaskId == id)
                    || s.Assets.Any(a => a.PmTaskId == id), ct))
                throw new DomainException("This task is used by PM schedules. Mark it inactive instead.");
            // A checklist on an asset (still there) or a category, in any site (0.12.0).
            if (await db.AssetChecklists.IgnoreQueryFilters().AnyAsync(c => c.TenantId == db.CurrentTenantId && !c.IsDeleted && c.PmTaskId == id
                    && (c.AssetId == null || db.Assets.IgnoreQueryFilters().Any(a => a.Id == c.AssetId && !a.IsDeleted)), ct))
                throw new DomainException(UsedAsChecklist);
            // A measurement point's level, any template step's follow-up, or the organization's default (0.13.0).
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(tn => tn.Id == db.CurrentTenantId, ct)).Settings;
            if (settings.FollowUpTemplateId == id
                || await db.MeasurementPoints.AnyAsync(p => p.AlarmLowTemplateId == id || p.AlertLowTemplateId == id || p.AlertHighTemplateId == id || p.AlarmHighTemplateId == id, ct)
                || await db.PmTasks.AnyAsync(o => o.Id != id && o.Steps.Any(st => st.FollowUpTemplateId == id), ct))
                throw new DomainException(UsedForFollowUps);
            db.PmTasks.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);

        // ---------------- PM schedules ----------------
        var g = api.MapGroup("/pm-schedules").WithTags("PM schedules");
        g.MapGet("/", ([AsParameters] PmScheduleFilter f, CmmsDbContext db, CancellationToken ct) =>
            ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 25, ct)).RequirePermission(Permissions.PmView);

        g.MapGet("/export", ([AsParameters] PmScheduleFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "pm-schedules", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct))
            .RequirePermission(Permissions.PmView);

        // Pause or resume many schedules. Resuming a calendar schedule starts it again from today, as the edit form does.
        g.MapPost("/bulk", async (BulkActiveInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var ids = Bulk.Ids(input.Ids, "PM schedules");
            var rows = await db.PmSchedules.Include(s => s.Calendars).Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            var calendars = (await CalendarsAsync(db, rows.SelectMany(r => r.Calendars.Select(c => c.ScheduleCalendarId)), ct)).ToDictionary(c => c.Id);
            foreach (var s in rows) SetActive(s, input.Active, s.Calendars.Where(c => calendars.ContainsKey(c.ScheduleCalendarId)).Select(c => calendars[c.ScheduleCalendarId]).ToList());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new BulkResult(rows.Count, ids.Except(rows.Select(r => r.Id)).Select(id => new BulkSkip(id, "", "Not found.")).ToList()));
        }).RequirePermission(Permissions.PmManage);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.PmSchedules.AsNoTracking().Include(x => x.ExtraTasks).Include(x => x.Asset).Include(x => x.Meter).Include(x => x.PmTask)
                .Include(x => x.Assets).ThenInclude(a => a.Asset).Include(x => x.Calendars).Include(x => x.ExtraMeters).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var calendars = await CalendarsAsync(db, s.Calendars.Select(c => c.ScheduleCalendarId), ct);
            var extraIds = s.ExtraMeters.Select(m => m.MeterId).ToList();
            var extraMeters = await db.Meters.AsNoTracking().Where(m => extraIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            var recent = await db.WorkOrders.AsNoTracking().Where(w => w.PmScheduleId == id).OrderByDescending(w => w.CreatedAt).Take(10)
                .Select(w => new { w.Id, w.Number, w.Title, w.Status, w.PmDueDate, w.CompletedAt }).ToListAsync(ct);
            var visitCost = (await PmCost.ForSchedulesAsync(db, [s], ct))[(s.Id, s.PmTaskId)];
            return Results.Ok(new
            {
                s.Id, s.Code, s.Name, s.Description, s.SiteId, s.LocationId, s.AssetId, Asset = s.Asset is null ? null : new { s.Asset.Id, s.Asset.AssetNumber, s.Asset.Name },
                Assets = s.Assets.Where(a => a.Asset is not null).OrderBy(a => a.Sequence).Select(a => new { a.Asset!.Id, a.Asset.AssetNumber, a.Asset.Name, a.PmTaskId }),
                s.SkipStatusesOverride, s.NonWorkingDaysOverride, s.NoStackingOverride, s.MultiAssetMode, s.GroupPlan, s.SpreadParts, s.SpreadIndex, s.CycleStart,
                s.TradeId, s.TeamId, s.Priority, s.AssigneeIds, s.RecipientIds, s.PmTaskId, Task = new { s.PmTask!.Id, s.PmTask.Code, s.PmTask.Name }, s.OnlyWhenStopped, s.ContractId, s.CostCentreId, s.AssetGroupId,
                GroupAssets = await GroupAssetsAsync(db, s, ct),
                ExtraTasks = s.ExtraTasks.Select(e => new { e.PmTaskId, e.EveryNth }), s.Type, s.Basis, s.Frequency, s.Unit, s.WeekOfMonth, s.DayOfWeek, s.DayOfMonth,
                s.WorkPeriodDays, s.WholeMonth, s.LeadDays, s.StartDate, s.NextDueDate, s.LastDueDate, s.LastGeneratedAt, s.GenerationCount,
                s.MeterId, Meter = s.Meter is null ? null : new { s.Meter.Id, s.Meter.Code, s.Meter.Name, s.Meter.Unit, s.Meter.CurrentReading },
                s.MeterTrigger, s.MeterInterval, s.NextDueReading, s.LowerLimit, s.UpperLimit, s.LowerTaskId, s.UpperTaskId, s.TimeBased, s.IsActive,
                Schedule = PmCalendar.Describe(s, Translator.CurrentLanguage), Upcoming = PmTriggers.Forecast(s, Today.AddYears(2), 6, calendars), RecentWorkOrders = recent, VisitCost = visitCost,
                CalendarIds = s.Calendars.Select(c => c.ScheduleCalendarId),
                Calendars = calendars.OrderBy(c => c.Code).Select(c => new { c.Id, c.Code, c.Name }),
                ExtraMeters = s.ExtraMeters.Where(m => extraMeters.ContainsKey(m.MeterId)).Select(m => new
                {
                    m.MeterId, extraMeters[m.MeterId].Code, extraMeters[m.MeterId].Name, extraMeters[m.MeterId].Unit, extraMeters[m.MeterId].CurrentReading, m.Interval, m.NextDueReading,
                }),
                s.LegacyRef, RowVersion = Convert.ToBase64String(s.RowVersion),
            });
        }).RequirePermission(Permissions.PmView);

        // Live preview for the schedule form: describe + next dates for a draft, without saving.
        g.MapPost("/simulate", async (ScheduleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = new PmSchedule { Code = "", Name = "" };
            ApplySchedule(s, input);
            var calendars = await CalendarsAsync(db, input.CalendarIds ?? [], ct);
            s.NextDueDate = input.NextDueDate ?? PmTriggers.First(s, input.StartDate, calendars);
            var dates = s.NextDueDate is { } first ? PmTriggers.Forecast(s, first.AddYears(3), 6, calendars).ToList() : [];
            return Results.Ok(new { Schedule = PmCalendar.Describe(s, Translator.CurrentLanguage), Upcoming = dates, DueDates = dates.Select(d => PmCalendar.DueDate(s, d)) });
        }).RequirePermission(Permissions.PmView);

        g.MapPost("/", async (ScheduleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateScheduleAsync(input, null, db, ct);
            if (input.ManagesContract == true && input.ContractId is { } contract && await ContractEndpoints.ChoiceProblemAsync(db, contract, input.SiteId, ct) is { } problem)
                v.Add("contractId", problem);
            if (!v.IsValid) return v.Problem();
            var code = input.Code.Clean() ?? await Numbering.NextAsync(db, "PmSchedule", "PM-", 5, n => db.PmSchedules.AllSites().AnyAsync(x => x.Code == n, ct),
                p => db.PmSchedules.AllSites().Where(x => x.Code.StartsWith(p)).Select(x => x.Code).ToListAsync(ct), ct);
            var s = CreateSchedule(db, input, code, await CalendarsAsync(db, input.CalendarIds ?? [], ct));
            foreach (var m in s.ExtraMeters.Where(m => m.NextDueReading is null))
                m.NextDueReading = (await db.Meters.Where(x => x.Id == m.MeterId).Select(x => x.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + m.Interval;
            // "Only while stopped" comes from the asset unless the form says otherwise.
            if (input.OnlyWhenStopped is null && s.AssetId is { } stopAsset)
                s.OnlyWhenStopped = await db.Assets.AllSites().Where(a => a.Id == stopAsset).Select(a => a.OnlyWhenStopped).FirstOrDefaultAsync(ct);
            if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental && s.NextDueReading is null)
                s.NextDueReading = (await db.Meters.Where(m => m.Id == s.MeterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + (s.MeterInterval ?? 0);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pm-schedules/{s.Id}", new { s.Id, s.Code, Warnings = await WarningsAsync(db, s, ct) });
        }).RequirePermission(Permissions.PmManage);

        g.MapPut("/{id:guid}", async (Guid id, ScheduleInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var s = await db.PmSchedules.Include(x => x.ExtraTasks).Include(x => x.Assets).Include(x => x.Calendars).Include(x => x.ExtraMeters).AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var before = s.Assets.OrderBy(a => a.Sequence).Select(a => a.AssetId).ToList();
            // The round's assets that still exist, and those of them the editor can't see (another branch): only these may be kept
            // past the site limits; any other asset must be one the editor sees.
            var live = before.Count == 0 ? [] : (await db.Assets.AllSites().Where(a => before.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
            var seen = before.Count == 0 ? [] : (await db.Assets.Where(a => before.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
            var hidden = before.Where(a => live.Contains(a) && !seen.Contains(a)).ToList();
            string? coversHidden = null;
            // A client that sends no asset list (e.g. one that predates rounds) leaves a round's list as it is, unless it makes
            // the schedule a single-asset one. Assets deleted since drop out.
            if (input.AssetIds is null && input.AssetId is null && before.Count > 0)
                input = input with { AssetIds = before.Where(live.Contains).ToList() };
            else if (hidden.Count > 0)
            {
                // A round's assets the editor can't see stay on it, after the ones they sent; they can't turn it into something else.
                if (input.AssetIds is { Count: > 0 } sent && input.AssetId is null && input.Type != PmScheduleType.Meter)
                    input = input with { AssetIds = [.. sent, .. hidden.Where(a => !sent.Contains(a))] };
                else coversHidden = "This round also covers assets in parts of the site you can't see.";
            }
            // Pages from before teams (0.9.0) don't send the team: an edit from them keeps it where it still fits; a team they do
            // send is a choice.
            if (input.ManagesTeam != true && input.TeamId is null && s.TeamId is { } keep)
                input = input with { TeamId = await db.Teams.AllSites().AnyAsync(t => t.Id == keep && (t.SiteId == null || t.SiteId == input.SiteId), ct) ? keep : null };
            // Pages from before asset groups (0.21.0) don't send the group: an edit from them keeps it.
            if (input.ManagesAssetGroup != true && input.AssetGroupId is null && s.AssetGroupId is { } keepGroup) input = input with { AssetGroupId = keepGroup };
            var v = await ValidateScheduleAsync(input, id, db, ct, hidden);
            if (coversHidden is not null) v.Add("assetIds", coversHidden);
            if (input.ManagesContract == true && input.ContractId is { } contract && contract != s.ContractId
                && await ContractEndpoints.ChoiceProblemAsync(db, contract, input.SiteId, ct) is { } problem) v.Add("contractId", problem);
            v.Merge(ScheduleChangeRules(s, input));
            // Work generated out of order (0.12.0) doesn't count as a generation, but it fixes the mode too.
            if (input.MultiAssetMode is { } mode && mode != s.MultiAssetMode && s.GenerationCount == 0
                && (await db.WorkOrders.AnyAsync(w => w.PmScheduleId == id, ct) || await db.WorkOrderGroups.AnyAsync(g => g.PmScheduleId == id, ct)))
                v.Add("multiAssetMode", ModeLocked);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(s).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            UpdateSchedule(s, input, await CalendarsAsync(db, input.CalendarIds ?? s.Calendars.Select(c => c.ScheduleCalendarId), ct));
            foreach (var m in s.ExtraMeters.Where(m => m.NextDueReading is null))
                m.NextDueReading = (await db.Meters.Where(x => x.Id == m.MeterId).Select(x => x.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + m.Interval;
            var after = s.Assets.OrderBy(a => a.Sequence).Select(a => a.AssetId).ToList();
            if (!before.SequenceEqual(after))
            {
                // The list lives in its own table, so a list-only change would leave the schedule row untouched: touch it so
                // its RowVersion is checked (stale edits get 409), and record the change in the audit trail.
                db.Entry(s).Property(x => x.Name).IsModified = true;
                var all = before.Concat(after).Distinct().ToList();
                var numbers = await db.Assets.IgnoreQueryFilters().Where(a => all.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.AssetNumber, ct);
                db.AuditEntries.Add(new AuditEntry
                {
                    EntityType = nameof(PmSchedule), EntityId = s.Id, Action = "Updated", UserId = ctx.UserId, UserName = ctx.UserName, Timestamp = DateTimeOffset.UtcNow,
                    Changes = JsonSerializer.Serialize(new Dictionary<string, object> { ["Assets"] = new { from = before.Select(numbers.GetValueOrDefault), to = after.Select(numbers.GetValueOrDefault) } },
                        JsonSerializerOptions.Web),
                });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { s.Id, Warnings = await WarningsAsync(db, s, ct) });
        }).RequirePermission(Permissions.PmManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.PmSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            if (await db.WorkOrders.AnyAsync(w => w.PmScheduleId == id, ct)) throw new DomainException("This schedule has generated work orders. Deactivate it instead to keep the history.");
            db.PmSchedules.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);

        // ---------------- Generation & forecast ----------------
        // The next 24 occurrences (0.12.0): dates, or projected meter readings with an estimated date.
        g.MapGet("/{id:guid}/occurrences", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var sch = await db.PmSchedules.AsNoTracking().Include(x => x.ExtraTasks).Include(x => x.Calendars).Include(x => x.Assets).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (sch is null) return Results.NotFound();
            var calendars = await CalendarsAsync(db, sch.Calendars.Select(c => c.ScheduleCalendarId), ct);
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var nonWorking = PmGenerator.Rules(sch, settings).NonWorking;
            var names = await db.PmTasks.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            var dates = PmTriggers.Forecast(sch, Today.AddYears(30), Occurrences, calendars).ToList();
            var made = await db.WorkOrders.AsNoTracking().Where(w => w.PmScheduleId == id && w.GroupId == null && w.PmDueDate != null && dates.Contains(w.PmDueDate.Value))
                .Select(w => new { Date = w.PmDueDate!.Value, w.Id, w.Number, w.Status }).ToListAsync(ct);
            var groups = await db.WorkOrderGroups.AsNoTracking().Where(g => g.PmScheduleId == id && g.PmDueDate != null && dates.Contains(g.PmDueDate.Value))
                .Select(g => new { Date = g.PmDueDate!.Value, g.Id, g.Number }).ToListAsync(ct);
            var list = dates.Select((d, k) =>
            {
                var start = PmCalendar.NextWorkingDay(d, nonWorking);
                var wo = made.FirstOrDefault(x => x.Date == d);
                var grp = groups.FirstOrDefault(x => x.Date == d);
                return new
                {
                    Date = d, DueDate = PmCalendar.DueDate(sch, start, nonWorking), TemplateName = names.GetValueOrDefault(PmCalendar.TaskFor(sch, sch.GenerationCount + 1 + PmTriggers.PeriodOffset(sch, k))),
                    Status = wo is not null || grp is not null ? "Generated" : "Planned", WorkOrderId = wo?.Id, WorkOrderNumber = wo?.Number, WorkOrderStatus = wo?.Status,
                    GroupId = grp?.Id, GroupNumber = grp?.Number,
                };
            }).ToList();
            return Results.Ok(new { Dates = list, Readings = await ProjectedReadingsAsync(db, sch, ct) });
        }).RequirePermission(Permissions.PmView);

        // "Generate this one now" for any of the next occurrences (0.12.0).
        g.MapPost("/{id:guid}/occurrences/generate", async (Guid id, OccurrenceInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var sch = await db.PmSchedules.Include(x => x.ExtraTasks).Include(x => x.Asset).Include(x => x.Assets).Include(x => x.Calendars).Include(x => x.ExtraMeters)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (sch is null) return Results.NotFound();
            var result = await PmGenerator.GenerateOccurrenceAsync(db, ctx, sch, input.Date, ct, mail);
            return Results.Ok(result);
        }).RequirePermission(Permissions.PmGenerate);

        // "Make a copy for this PM" (0.12.0): later edits to the shared template no longer reach this PM.
        g.MapPost("/{id:guid}/own-template", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var sch = await db.PmSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (sch is null) return Results.NotFound();
            var src = await db.PmTasks.AsNoTracking().Include(x => x.Steps).Include(x => x.Parts).FirstAsync(x => x.Id == sch.PmTaskId, ct);
            var stem = $"{src.Code}-{sch.Code}";
            stem = stem.Length > 46 ? stem[..46] : stem;
            var code = stem;
            for (var n = 2; await db.PmTasks.AnyAsync(x => x.Code == code, ct); n++) code = $"{stem}-{n}";
            var name = $"{src.Name} ({sch.Code})";
            var copy = new PmTask
            {
                Code = code, Name = name.Length > 300 ? name[..300] : name, Description = src.Description, TradeId = src.TradeId, EstimatedHours = src.EstimatedHours,
                SafetyNotes = src.SafetyNotes, IsActive = true, Title = src.Title, WorkType = src.WorkType, Priority = src.Priority, RecipientIds = [.. src.RecipientIds],
                ExpectedStopHours = src.ExpectedStopHours,
            };
            foreach (var st in src.Steps.OrderBy(x => x.Sequence)) copy.Steps.Add(StepCopy.Copy(st, copy.Id));
            foreach (var pt in src.Parts) copy.Parts.Add(new PmTaskPart { PmTaskId = copy.Id, PartId = pt.PartId, Quantity = pt.Quantity });
            db.PmTasks.Add(copy);
            sch.PmTaskId = copy.Id;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { PmTaskId = copy.Id, copy.Code });
        }).RequirePermission(Permissions.PmManage);

        var pm = api.MapGroup("/pm").WithTags("PM generation");
        pm.MapGet("/preview", async (DateOnly until, Guid? siteId, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            await PmGenerator.RunAsync(db, ctx, until, siteId, null, dryRun: true, ct)).RequirePermission(Permissions.PmView);
        pm.MapPost("/generate", async (GenerateInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            if (input.Until > Today.AddYears(1)) throw new DomainException("Generate at most one year ahead.");
            var results = await PmGenerator.RunAsync(db, ctx, input.Until, input.SiteId, input.ScheduleIds, dryRun: false, ct, mail);
            return Results.Ok(new { created = results.Sum(r => r.Made), results });
        }).RequirePermission(Permissions.PmGenerate);
        pm.MapGet("/forecast", async (DateOnly from, DateOnly to, Guid? siteId, CmmsDbContext db, CancellationToken ct) =>
        {
            if (to < from || to > from.AddYears(2)) throw new DomainException("Choose a range of up to two years.");
            return await ForecastAsync(db, from, to, siteId, ct);
        }).RequirePermission(Permissions.PmView);

        // ---------------- Meters ----------------
        var m = api.MapGroup("/meters").WithTags("Meters");
        m.MapGet("/", async (CmmsDbContext db, Guid? assetId, string? search, CancellationToken ct) =>
        {
            var q = db.Meters.AsNoTracking();
            if (assetId is not null) q = q.Where(x => x.AssetId == assetId);
            if (search.Clean() is { } s) q = q.Where(x => x.Code.Contains(s) || x.Name.Contains(s) || x.Asset!.AssetNumber.Contains(s));
            return await q.OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.AssetId, AssetNumber = x.Asset!.AssetNumber, AssetName = x.Asset.Name, x.Unit,
                x.CurrentReading, x.LastReadAt, x.MaxDailyIncrement, x.IsActive, Schedules = db.PmSchedules.Count(p => p.MeterId == x.Id && p.IsActive),
                x.SourceMeterId, SourceCode = db.Meters.IgnoreQueryFilters().Where(m => m.Id == x.SourceMeterId).Select(m => m.Code).FirstOrDefault(),
                Followers = db.Meters.Count(m => m.SourceMeterId == x.Id) }).ToListAsync(ct);
        }).RequirePermission(Permissions.PmView);
        m.MapPost("/", async (MeterInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateMeterAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = new Meter { Code = input.Code.Trim(), Name = input.Name.Trim(), AssetId = input.AssetId, Unit = input.Unit.Trim(), MaxDailyIncrement = input.MaxDailyIncrement, IsActive = input.IsActive };
            if (input.ManagesSource == true) x.SourceMeterId = input.SourceMeterId;
            db.Meters.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/meters/{x.Id}", new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        m.MapPut("/{id:guid}", async (Guid id, MeterInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Meters.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var v = await ValidateMeterAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            x.Code = input.Code.Trim(); x.Name = input.Name.Trim(); x.AssetId = input.AssetId; x.Unit = input.Unit.Trim(); x.MaxDailyIncrement = input.MaxDailyIncrement; x.IsActive = input.IsActive;
            if (input.ManagesSource == true) x.SourceMeterId = input.SourceMeterId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        m.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Meters.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PmSchedules.AllSites().AnyAsync(p => p.MeterId == id || p.ExtraMeters.Any(m => m.MeterId == id), ct))
                throw new DomainException("PM schedules use this meter. Remove them first or mark the meter inactive.");
            if (await db.Meters.AllSites().AnyAsync(m => m.SourceMeterId == id, ct)) throw new DomainException("Other meters follow this one. Unlink them first.");
            db.Meters.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);
        m.MapGet("/{id:guid}/readings", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.MeterReadings.AsNoTracking().Where(r => r.MeterId == id).OrderByDescending(r => r.ReadAt).Take(200)
                .Select(r => new { r.Id, r.Value, r.ReadAt, r.Note, r.Source, r.ByName, r.WorkOrderId }).ToListAsync(ct)).RequirePermission(Permissions.PmView);
        m.MapPost("/{id:guid}/readings", async (Guid id, ReadingInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (meter is null) return Results.NotFound();
            var (v, generated) = await RecordReadingAsync(db, ctx, mail, meter, input, ct);
            return v.IsValid ? Results.Ok(new { ok = true, generated }) : v.Problem();
        }).RequirePermission(Permissions.MetersRecord);
    }

    /// <summary>Records a meter reading (with the spike guard unless confirmed) and runs the meter's PM schedules.</summary>
    /// <param name="workOrderId">The work order the reading was taken on (a checklist's Meter step, 0.13.0).</param>
    internal static async Task<(Validator V, List<PmGenerator.Result> Generated)> RecordReadingAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, Meter meter,
        ReadingInput input, CancellationToken ct, Guid? workOrderId = null)
    {
        {
            var id = meter.Id;
            var at = input.ReadAt ?? DateTimeOffset.UtcNow;
            var v = new Validator().When(at > DateTimeOffset.UtcNow.AddMinutes(10), "readAt", "A reading cannot be in the future.").MaxLength("note", input.Note, "Note", 1000);
            // Spike guard (legacy "Max Daily Increment"): ask for confirmation instead of silently accepting a probable typo.
            if (!input.Confirm && meter.CurrentReading is { } last && meter.LastReadAt is { } lastAt)
            {
                if (input.Value < last && at >= lastAt) v.Add("value", $"This is lower than the last reading ({last:0.###} {meter.Unit}). Confirm if the meter was reset or replaced.");
                var days = Math.Max(1m, (decimal)(at - lastAt).TotalDays);
                if (meter.MaxDailyIncrement is { } max && input.Value - last > max * days)
                    v.Add("value", $"An increase of {input.Value - last:0.###} {meter.Unit} in {days:0.#} day(s) exceeds the expected maximum ({max:0.###}/day). Confirm to save it anyway.");
            }
            if (!v.IsValid) return (v, []);
            var previous = meter.CurrentReading;
            var latest = meter.LastReadAt is null || at >= meter.LastReadAt;
            db.MeterReadings.Add(new MeterReading { MeterId = id, Value = input.Value, ReadAt = at, Note = input.Note.Clean(), ByName = ctx.UserName, WorkOrderId = workOrderId });
            if (latest) { meter.CurrentReading = input.Value; meter.LastReadAt = at; }
            await db.SaveChangesAsync(ct);
            var generated = meter.IsActive ? await PmGenerator.OnReadingAsync(db, ctx, meter, input.Value, ct, mail) : [];
            // Meters that follow this one (0.12.0) get the same increase; not a reset or a reading older than the latest.
            if (latest && previous is { } prev && input.Value > prev)
                await PropagateAsync(db, ctx, mail, meter, input.Value - prev, at, [id], generated, ct);
            return (v, generated);
        }
    }

    /// <summary>Adds <paramref name="increase"/> to every meter following <paramref name="source"/>, then to theirs, running their PMs.</summary>
    private static async Task PropagateAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, Meter source, decimal increase, DateTimeOffset at, HashSet<Guid> seen,
        List<PmGenerator.Result> generated, CancellationToken ct)
    {
        // Followers in any site or branch: the link was made by someone who could see both.
        var followers = await db.Meters.AllSites().Where(m => m.SourceMeterId == source.Id && m.IsActive).ToListAsync(ct);
        foreach (var f in followers.Where(f => seen.Add(f.Id)))
        {
            var value = (f.CurrentReading ?? 0) + increase;
            var latest = f.LastReadAt is null || at >= f.LastReadAt;
            db.MeterReadings.Add(new MeterReading { MeterId = f.Id, Value = value, ReadAt = at, Note = $"From {source.Code}", Source = ReadingSource.Propagated, ByName = ctx.UserName });
            if (latest) { f.CurrentReading = value; f.LastReadAt = at; }
            await db.SaveChangesAsync(ct);
            generated.AddRange(await PmGenerator.OnReadingAsync(db, ctx, f, value, ct, mail));
            if (latest) await PropagateAsync(db, ctx, mail, f, increase, at, seen, generated, ct);
        }
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator PmTaskRules(TaskInput i)
    {
        var parts = i.Parts ?? [];
        return new Validator().Required("code", i.Code, "Task no.", 50).Required("name", i.Name, "Name", 300).MaxLength("safetyNotes", i.SafetyNotes, "Safety notes", 4000)
            .MaxLength("title", i.Title, "Title", 300)
            .When(i.WorkType is { } wt && !Enum.IsDefined(wt), "workType", "Choose a work type.").When(i.Priority is { } pr && !Enum.IsDefined(pr), "priority", "Choose a priority.")
            .When(i.ExpectedStopHours is < 0 or > 10_000, "expectedStopHours", "Enter between 0 and 10,000 hours.")
            .When((i.RecipientIds ?? []).Distinct().Count() > 20, "recipientIds", "At most 20 recipients.")
            .When(i.EstimatedHours < 0, "estimatedHours", "Estimate cannot be negative.").When((i.Steps ?? []).Count > 300, "steps", "At most 300 steps.")
            .When(parts.Count > 200, "parts", "At most 200 parts.")
            .When(parts.Any(p => Math.Round(p.Quantity, 3) is <= 0 or > 1_000_000_000m), "parts", "Each part needs a quantity above zero.")
            .When(parts.GroupBy(p => p.PartId).Any(g => g.Count() > 1), "parts", "List each part once.")
            .When((i.StepDetails ?? []).Count > 300, "steps", "At most 300 steps.")
            .When((i.StepDetails ?? []).Any(st => !Enum.IsDefined(st.Type) || !Enum.IsDefined(st.FollowUp)), "steps", "Choose what each step asks for.")
            .When((i.StepDetails ?? []).Any(st => st.Unit is { Length: > 20 }), "steps", "Unit can be at most 20 characters.")
            .When((i.StepDetails ?? []).Any(st => st.Decimals is < 0 or > 4), "steps", "Decimals can be 0 to 4.")
            .When((i.StepDetails ?? []).Any(st => st.MeasurementPointId is not null && st.Type != StepType.Number), "steps", "Only number steps can use a measurement point.")
            .When((i.StepDetails ?? []).Any(st => st.FollowUp == FollowUpKind.WorkOrder && st.FollowUpTemplateId is null), "steps", NeedsTemplate);
    }

    internal static PmTask CreateTask(CmmsDbContext db, TaskInput i)
    {
        var x = new PmTask { Code = i.Code.Trim(), Name = i.Name.Trim() };
        ApplyTask(x, i);
        SetParts(x, i.Parts);
        db.PmTasks.Add(x);
        return x;
    }

    /// <summary>Updates a task loaded with its steps and parts: the steps are replaced by the input's, the parts brought in line (null keeps them).</summary>
    internal static void UpdateTask(CmmsDbContext db, PmTask x, TaskInput i)
    {
        x.Name = i.Name.Trim();
        var before = x.Steps.ToList();
        db.PmTaskSteps.RemoveRange(x.Steps);
        x.Steps = [];
        ApplyTask(x, i, before);
        // The new steps already have ids, so EF would take them for existing rows and update nothing (a 409): they are new.
        foreach (var st in x.Steps) db.Entry(st).State = EntityState.Added;
        if (i.Parts is not null) UpdateParts(db, x, i.Parts);
    }

    private static async Task<Validator> ValidateTaskAsync(TaskInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = PmTaskRules(i);
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.PmTasks.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another task uses this number.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(x => x.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        var parts = i.Parts ?? [];
        var ids = parts.Select(p => p.PartId).Distinct().ToList();
        if (ids.Count > 0 && await db.Parts.CountAsync(p => ids.Contains(p.Id), ct) != ids.Count) v.Add("parts", "Unknown part.");
        await CheckRecipientsAsync(db, v, i.RecipientIds, ct);
        var points = (i.StepDetails ?? []).Select(st => st.MeasurementPointId).OfType<Guid>().Distinct().ToList();
        if (points.Count > 0 && await db.MeasurementPoints.CountAsync(p => points.Contains(p.Id), ct) != points.Count) v.Add("steps", MeasurementPointEndpoints.Unknown);
        var followUps = (i.StepDetails ?? []).Select(st => st.FollowUpTemplateId).OfType<Guid>().Distinct().ToList();
        if (followUps.Count > 0 && await db.PmTasks.CountAsync(t => followUps.Contains(t.Id), ct) != followUps.Count) v.Add("steps", "Unknown work template.");
        return v;
    }

    /// <summary>Recipients must be users of this organization.</summary>
    internal static async Task CheckRecipientsAsync(CmmsDbContext db, Validator v, List<Guid>? recipientIds, CancellationToken ct)
    {
        var who = (recipientIds ?? []).Distinct().ToList();
        if (who.Count > 0 && await db.Users.CountAsync(u => who.Contains(u.Id) && u.TenantId == db.CurrentTenantId, ct) != who.Count) v.Add("recipientIds", "Unknown user.");
    }

    /// <summary>Where a template is used: the PMs (and how), the asset checklists, and its open work orders.</summary>
    private static async Task<object> UsedByAsync(CmmsDbContext db, Guid id, CancellationToken ct)
    {
        var rows = await db.PmSchedules.AsNoTracking().Where(s => s.PmTaskId == id || s.LowerTaskId == id || s.UpperTaskId == id || s.ExtraTasks.Any(e => e.PmTaskId == id)
                || s.Assets.Any(a => a.PmTaskId == id))
            .OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name, s.PmTaskId, s.LowerTaskId, s.UpperTaskId, Nth = s.ExtraTasks.Any(e => e.PmTaskId == id) }).ToListAsync(ct);
        var schedules = rows.Select(s => new
        {
            s.Id, s.Code, s.Name,
            Role = s.PmTaskId == id ? "routine" : s.Nth ? "everyNth" : s.LowerTaskId == id ? "lower" : s.UpperTaskId == id ? "upper" : "asset",
        });
        var checklists = await db.AssetChecklists.AsNoTracking().Where(c => c.PmTaskId == id)
            .Select(c => new
            {
                c.Id, c.AssetId, c.AssetCategoryId,
                Name = c.AssetId != null ? db.Assets.Where(a => a.Id == c.AssetId).Select(a => a.AssetNumber + " · " + a.Name).FirstOrDefault()
                    : db.AssetCategories.Where(k => k.Id == c.AssetCategoryId).Select(k => k.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        var open = await db.WorkOrders.CountAsync(w => w.PmTaskId == id && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold), ct);
        return new { Schedules = schedules, Checklists = checklists, OpenWorkOrders = open };
    }

    private static void SetParts(PmTask x, List<TaskPartInput>? parts)
    {
        foreach (var p in parts ?? []) x.Parts.Add(new PmTaskPart { PmTaskId = x.Id, PartId = p.PartId, Quantity = Math.Round(p.Quantity, 3) });
    }

    /// <summary>
    /// Brings a saved task's parts in line with the list: changes quantities in place, removes parts no longer listed and adds
    /// new ones. (Removing every line and adding the same parts again collides with the one-line-per-part rule.)
    /// </summary>
    private static void UpdateParts(CmmsDbContext db, PmTask x, List<TaskPartInput> parts)
    {
        var wanted = parts.ToDictionary(p => p.PartId, p => Math.Round(p.Quantity, 3));
        foreach (var line in x.Parts.ToList())
        {
            if (wanted.Remove(line.PartId, out var qty)) line.Quantity = qty;
            else { x.Parts.Remove(line); db.PmTaskParts.Remove(line); }
        }
        foreach (var (partId, qty) in wanted)
        {
            // A new line already has its id, so EF would take it for an existing row and update nothing (a 409): it is new.
            var line = new PmTaskPart { PmTaskId = x.Id, PartId = partId, Quantity = qty };
            x.Parts.Add(line);
            db.Entry(line).State = EntityState.Added;
        }
    }

    /// <param name="before">The task's steps before an update: plain step texts (an older page, the import) keep the definition of the step with the same text.</param>
    internal static void ApplyTask(PmTask x, TaskInput i, IReadOnlyList<PmTaskStep>? before = null)
    {
        x.Description = i.Description.Clean(); x.TradeId = i.TradeId; x.EstimatedHours = i.EstimatedHours; x.SafetyNotes = i.SafetyNotes.Clean(); x.IsActive = i.IsActive;
        // Template fields (0.12.0): a type or recipient list left out keeps what is there.
        x.Title = i.Title.Clean(); x.Priority = i.Priority; x.ExpectedStopHours = i.ExpectedStopHours is { } esh ? Math.Round(esh, 2) : null;
        if (i.WorkType is { } wt) x.WorkType = wt;
        if (i.RecipientIds is not null) x.RecipientIds = i.RecipientIds.Distinct().ToList();
        var details = i.StepDetails?.Where(st => st.Description.Clean() is not null).ToList()
            ?? StepCopy.Match((i.Steps ?? []).Select(s => s.Clean()).OfType<string>(), before ?? [])
                .Select(st => new StepInput(st.Description, st.Type, st.Unit, st.Decimals, st.MeasurementPointId, st.PhotoRequired, st.BeforeStart, st.FollowUp, st.FollowUpTemplateId)).ToList();
        var seq = 0;
        foreach (var st in details)
        {
            var text = st.Description.Clean()!;
            var point = st.Type == StepType.Number ? st.MeasurementPointId : null; // a point gives the unit and decimals
            x.Steps.Add(new PmTaskStep
            {
                PmTaskId = x.Id, Sequence = ++seq, Description = text.Length > 2000 ? text[..2000] : text, Type = st.Type,
                Unit = point is null ? st.Unit.Clean() : null, Decimals = point is null ? st.Decimals : null, MeasurementPointId = point, PhotoRequired = st.PhotoRequired, BeforeStart = st.BeforeStart,
                FollowUp = st.FollowUp, FollowUpTemplateId = st.FollowUp == FollowUpKind.None ? null : st.FollowUpTemplateId,
            });
        }
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator PmScheduleRules(ScheduleInput i, Guid? id)
    {
        var v = new Validator().Required("name", i.Name, "Name", 300).MaxLength("code", i.Code, "PM no.", 50)
            .When(!Enum.IsDefined(i.Type), "type", "Choose a schedule type.")
            .When(i.Frequency is < 1 or > 1000, "frequency", "Frequency must be between 1 and 1000.")
            .When(i.WorkPeriodDays is < 1 or > 366, "workPeriodDays", "Work period must be between 1 and 366 days.")
            .When(i.LeadDays is < 0 or > 90, "leadDays", "Lead time must be between 0 and 90 days.")
            .When(i.Type is PmScheduleType.DayOfWeekInMonth or PmScheduleType.DateOfMonth && i.Basis == PmBasis.Actual, "basis", "Fixed day/date schedules are generated by scheduled date only.")
            .When(i.Type == PmScheduleType.DayOfWeekInMonth && (i.WeekOfMonth is not (>= 1 and <= 5) || i.DayOfWeek is null), "weekOfMonth", "Choose the week and the day.")
            .When(i.Type == PmScheduleType.DateOfMonth && i.DayOfMonth is not (>= 1 and <= 31), "dayOfMonth", "Choose a day between 1 and 31.")
            .When(id is null && i.Type != PmScheduleType.Meter && i.StartDate < Today.AddDays(-366), "startDate", "Start date is more than a year in the past.");
        var round = i.AssetIds ?? [];
        if (i.AssetId is null && i.LocationId is null && round.Count == 0 && i.AssetGroupId is null) v.Add("assetId", "Choose the asset or location to maintain.");
        // Shared calendars and extra meters (0.12.0).
        var calendars = (i.CalendarIds ?? []).Distinct().Count();
        if (i.Type == PmScheduleType.Calendar && calendars == 0) v.Add("calendarIds", "Choose at least one calendar.");
        else if (calendars > 10) v.Add("calendarIds", "At most 10 calendars.");
        else if ((calendars > 0 || i.Type == PmScheduleType.Calendar) && i.Basis == PmBasis.Actual) v.Add("calendarIds", "Shared calendars need the Scheduled basis.");
        var extra = i.ExtraMeters ?? [];
        if (extra.Count > 5) v.Add("extraMeters", "At most 5 extra meters.");
        else if (extra.Any(m => m.Interval <= 0)) v.Add("extraMeters", "Each extra meter needs an interval above zero.");
        else if (extra.GroupBy(m => m.MeterId).Any(g => g.Count() > 1) || extra.Any(m => i.Type == PmScheduleType.Meter && m.MeterId == i.MeterId))
            v.Add("extraMeters", "List each meter once.");
        else if (extra.Count > 0 && (round.Count > 0 || i.AssetGroupId is not null)) v.Add("extraMeters", "Extra meters need a single asset.");
        if (i.SpreadParts is >= 2 && (i.Type != PmScheduleType.FixedInterval || i.Basis != PmBasis.Scheduled || calendars > 0 || extra.Count > 0))
            v.Add("spreadParts", "Spread needs a fixed interval, the Scheduled basis, and no calendars or extra meters.");
        else if (i.SpreadParts is >= 2 && (round.Count < 2 || i.SpreadParts > round.Count)) v.Add("spreadParts", $"Spread over 2 to {Math.Max(2, round.Count)} parts.");
        else if (i.SpreadParts is >= 2 && i.SpreadParts > ShortestPeriodDays(i.Frequency, i.Unit))
            v.Add("spreadParts", string.Format(SpreadDays, ShortestPeriodDays(i.Frequency, i.Unit)));
        if (i.MultiAssetMode == MultiAssetMode.Group && round.Count < 2 && i.AssetGroupId is null) v.Add("multiAssetMode", "Choose several assets for a group.");
        else if (i.MultiAssetMode == MultiAssetMode.Group && i.Basis == PmBasis.Actual) v.Add("multiAssetMode", "Groups need the Scheduled basis.");
        if ((i.ExtraTasks ?? []).Any(e => e.EveryNth is < 2 or > 1000)) v.Add("extraTasks", "\"Every Nth\" must be between 2 and 1000.");
        if (i.Type == PmScheduleType.Meter)
        {
            if (i.MeterTrigger == MeterTrigger.Threshold && i.LowerLimit is null && i.UpperLimit is null) v.Add("lowerLimit", "Set a lower and/or upper limit.");
            if (i.MeterTrigger == MeterTrigger.Threshold && i.LowerLimit >= i.UpperLimit) v.Add("upperLimit", "Upper limit must be above the lower limit.");
            if (i.MeterTrigger != MeterTrigger.Threshold && i.MeterInterval is not > 0) v.Add("meterInterval", "Enter the meter interval (e.g. every 250 hours).");
            if (i.TimeBased && i.MeterTrigger == MeterTrigger.Threshold) v.Add("timeBased", "Time-based generation is only available for incremental meters.");
        }
        return v;
    }

    /// <summary>What can't change on a schedule that has run: its generation basis (as in 4CSQL).</summary>
    internal static Validator ScheduleChangeRules(PmSchedule s, ScheduleInput i) => new Validator()
        .When(i.Basis != s.Basis && s.GenerationCount > 0, "basis", "The generation basis cannot change after work orders were generated (as in 4CSQL). Create a new schedule instead.")
        .When(i.MultiAssetMode is { } mode && mode != s.MultiAssetMode && s.GenerationCount > 0, "multiAssetMode", ModeLocked);

    public const string UsedAsChecklist = "This template is a checklist on assets or categories. Remove it there or mark it inactive instead.";
    public const string SpreadDays = "Spread over at most {0} parts: one a day.";

    /// <summary>The fewest days a period of this length can have (a month: 28), so every spread part gets a day of its own.</summary>
    internal static int ShortestPeriodDays(int frequency, FrequencyUnit unit)
    {
        var n = Math.Max(1, frequency);
        return unit switch { FrequencyUnit.Day => n, FrequencyUnit.Week => 7 * n, FrequencyUnit.Month => 28 * n, _ => 365 * n };
    }

    public const string ModeLocked = "The multi-asset mode can't change after the PM has generated work.";

    /// <summary>A new schedule with its first due date (meter schedules: see the POST for their first reading).</summary>
    /// <param name="calendars">The shared calendars the input names (0.12.0); none when left out.</param>
    internal static PmSchedule CreateSchedule(CmmsDbContext db, ScheduleInput i, string code, IReadOnlyList<ScheduleCalendar>? calendars = null)
    {
        var s = new PmSchedule { Code = code, Name = i.Name.Trim() };
        ApplySchedule(s, i);
        calendars ??= [];
        s.NextDueDate = PmTriggers.UsesDates(s, calendars.Count) ? i.NextDueDate ?? PmTriggers.First(s, i.StartDate, calendars) : null;
        db.PmSchedules.Add(s);
        return s;
    }

    /// <summary>The shared calendars with these ids (organization-wide).</summary>
    internal static async Task<List<ScheduleCalendar>> CalendarsAsync(CmmsDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : await db.ScheduleCalendars.AsNoTracking().Where(c => list.Contains(c.Id)).ToListAsync(ct);
    }

    /// <summary>Updates a schedule (loaded with its extra tasks and assets) and works out its next due date as the screen does.</summary>
    internal static void UpdateSchedule(PmSchedule s, ScheduleInput i, IReadOnlyList<ScheduleCalendar>? calendars = null)
    {
        s.Name = i.Name.Trim();
        if (i.Code.Clean() is { } c) s.Code = c;
        var wasActive = s.IsActive;
        var loadedNext = s.NextDueDate;
        var calendarsBefore = s.Calendars.Select(x => x.ScheduleCalendarId).ToHashSet();
        ApplySchedule(s, i);
        // The import doesn't read calendars: a PM whose dates come only from them keeps its next date there.
        if (calendars is null && s.Type == PmScheduleType.Calendar) return;
        calendars ??= [];
        // Calendars linked or unlinked (0.12.0), the next date not typed over: it follows the new set, as when a calendar is edited.
        var calendarsChanged = i.CalendarIds is not null && !calendarsBefore.SetEquals(s.Calendars.Select(x => x.ScheduleCalendarId));
        if (PmTriggers.UsesDates(s, calendars.Count) && calendarsChanged && wasActive && s.IsActive && s.Basis == PmBasis.Scheduled
            && (i.NextDueDate is null || i.NextDueDate == loadedNext))
            s.NextDueDate = PmTriggers.Retrigger(s, calendars, Today);
        else if (PmTriggers.UsesDates(s, calendars.Count))
            s.NextDueDate = i.NextDueDate ?? (!wasActive && s.IsActive ? PmTriggers.First(s, Today, calendars)
                : s.NextDueDate ?? (s.GenerationCount == 0 ? PmTriggers.First(s, i.StartDate, calendars) : null));
        else s.NextDueDate = null;
    }

    /// <param name="keptHidden">Assets already on the round that the editor can't see (another branch): checked past the site limits.
    /// Every other asset must be one the editor sees.</param>
    private static async Task<Validator> ValidateScheduleAsync(ScheduleInput i, Guid? id, CmmsDbContext db, CancellationToken ct, IReadOnlyCollection<Guid>? keptHidden = null)
    {
        var v = PmScheduleRules(i, id);
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.PmSchedules, x => x.Code == i.Code.Trim() && x.Id != id,
                "Another schedule uses this number.", "This number is already used by a schedule in a site you can't see.", ct) is { } scheduleClash)
            v.Add("code", scheduleClash);
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        Access.RequireLocation(db, v, i.LocationId);
        var currentTeam = id is { } sid ? await db.PmSchedules.Where(x => x.Id == sid).Select(x => x.TeamId).FirstOrDefaultAsync(ct) : null;
        await TeamEndpoints.CheckForWorkAsync(db, v, i.TeamId, i.SiteId, currentTeam, ct);
        if (i.AssetGroupId is { } groupId)
        {
            // A PM on an asset group (0.21.0) covers its members at the PM's site; it can't also name assets.
            var group = await db.AssetGroups.Where(x => x.Id == groupId).Select(x => new { x.SiteId }).FirstOrDefaultAsync(ct);
            if (group is null) v.Add("assetGroupId", "Choose an asset group.");
            else if (group.SiteId is { } groupSite && groupSite != i.SiteId) v.Add("assetGroupId", "Choose a group of this PM's site or a shared group.");
            if (i.Type == PmScheduleType.Meter) v.Add("assetGroupId", "Meter schedules cannot cover several assets.");
            if ((i.AssetTemplates?.Count ?? 0) > 0 || i.SpreadParts is >= 2) v.Add("assetGroupId", "Per-asset templates and spread aren't available for a PM on an asset group.");
        }
        var round = i.AssetIds ?? [];
        if (round.Count > 100) v.Add("assetIds", "List at most 100 assets.");
        else if (round.Count > 0)
        {
            if (round.Distinct().Count() != round.Count) v.Add("assetIds", "An asset is listed twice.");
            else
            {
                // Assets the editor names must be ones they see; those already on the round in a hidden branch are checked past the limits.
                var kept = round.Where(a => keptHidden?.Contains(a) == true).ToList();
                var named = round.Where(a => keptHidden?.Contains(a) != true).ToList();
                var elsewhere = await db.Assets.Where(a => named.Contains(a.Id) && a.SiteId != i.SiteId).OrderBy(a => a.AssetNumber).Select(a => a.AssetNumber).ToListAsync(ct);
                if (elsewhere.Count > 0) v.Add("assetIds", $"Assets must belong to the selected site: {string.Join(", ", elsewhere)}.");
                else if (await db.Assets.CountAsync(a => named.Contains(a.Id), ct) + await db.Assets.AllSites().CountAsync(a => kept.Contains(a.Id), ct) != round.Count)
                    v.Add("assetIds", "Some listed assets no longer exist. Remove them from the list.");
            }
            if (i.Type == PmScheduleType.Meter) v.Add("assetIds", "Meter schedules cannot cover several assets.");
            if (i.AssetId is not null) v.Add("assetId", "Choose either one asset or several assets.");
        }
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        var taskIds = new[] { (Guid?)i.PmTaskId, i.LowerTaskId, i.UpperTaskId }.OfType<Guid>().Concat((i.ExtraTasks ?? []).Select(e => e.PmTaskId))
            .Concat((i.AssetTemplates ?? []).Select(a => a.PmTaskId).OfType<Guid>()).Distinct().ToList();
        if ((i.AssetTemplates ?? []).Any(a => !round.Contains(a.AssetId))) v.Add("assetTemplates", "Give templates only to assets on the list.");
        if (await db.PmTasks.CountAsync(x => taskIds.Contains(x.Id), ct) != taskIds.Count) v.Add("pmTaskId", "Choose a valid PM task.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(x => x.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        if (i.Type == PmScheduleType.Meter && (i.MeterId is null || !await db.Meters.AnyAsync(x => x.Id == i.MeterId, ct))) v.Add("meterId", "Choose a meter.");
        var calIds = (i.CalendarIds ?? []).Distinct().ToList();
        if (calIds.Count > 0 && await db.ScheduleCalendars.CountAsync(c => calIds.Contains(c.Id), ct) != calIds.Count) v.Add("calendarIds", "Unknown calendar.");
        var meterIds = (i.ExtraMeters ?? []).Select(m => m.MeterId).Distinct().ToList();
        if (meterIds.Count > 0 && await db.Meters.CountAsync(m => meterIds.Contains(m.Id), ct) != meterIds.Count) v.Add("extraMeters", "Unknown meter.");
        if ((i.NonWorkingDaysOverride ?? []).Distinct().Count() >= 7) v.Add("nonWorkingDaysOverride", "Leave at least one working day.");
        if ((i.RecipientIds ?? []).Distinct().Count() > 20) v.Add("recipientIds", "At most 20 recipients.");
        else await CheckRecipientsAsync(db, v, i.RecipientIds, ct);
        return v;
    }

    /// <summary>
    /// A meter PM's next <see cref="Occurrences"/> due readings, each with an estimated date from the meter's use over the last 90
    /// days (none with too little history).
    /// </summary>
    private static async Task<object> ProjectedReadingsAsync(CmmsDbContext db, PmSchedule s, CancellationToken ct)
    {
        if (s.Type != PmScheduleType.Meter || s.MeterTrigger != MeterTrigger.Incremental || s.NextDueReading is not { } next || s.MeterInterval is not > 0 || s.MeterId is not { } meterId)
            return Array.Empty<object>();
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var readings = await db.MeterReadings.AsNoTracking().Where(r => r.MeterId == meterId && r.ReadAt >= since).OrderBy(r => r.ReadAt)
            .Select(r => new { r.Value, r.ReadAt }).ToListAsync(ct);
        var current = await db.Meters.AsNoTracking().Where(m => m.Id == meterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0;
        decimal? perDay = null;
        if (readings.Count >= 2 && (readings[^1].ReadAt - readings[0].ReadAt).TotalDays is var days && days >= 1 && readings[^1].Value > readings[0].Value)
            perDay = (readings[^1].Value - readings[0].Value) / (decimal)days;
        return Enumerable.Range(0, Occurrences).Select(k =>
        {
            var reading = next + k * s.MeterInterval.Value;
            DateOnly? estimated = perDay is { } rate ? Today.AddDays((int)Math.Round(Math.Max(0, reading - current) / rate)) : null;
            return new { Reading = reading, EstimatedDate = estimated };
        }).ToList();
    }

    public const string NoStepsWarning = "These assets get no checklist steps: {0}.";

    /// <summary>What to point out after saving (0.12.0): a multi-asset PM's assets whose template has no steps.</summary>
    /// <summary>The members a PM on an asset group would cover today (0.21.0), in round order; empty for other PMs.</summary>
    private static async Task<object[]> GroupAssetsAsync(CmmsDbContext db, PmSchedule s, CancellationToken ct)
    {
        if (s.AssetGroupId is null) return [];
        var ids = (await PmGenerator.RoundAssetsAsync(db, s, ct)).Select(a => a.Id).ToList();
        var names = await db.Assets.AllSites().AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => new { a.Id, a.AssetNumber, a.Name }, ct);
        return ids.Where(names.ContainsKey).Select(id => (object)names[id]).ToArray();
    }

    internal static async Task<List<string>> WarningsAsync(CmmsDbContext db, PmSchedule s, CancellationToken ct)
    {
        if (s.Assets.Count == 0) return [];
        var templateIds = s.Assets.Select(a => a.PmTaskId ?? s.PmTaskId).Distinct().ToList();
        var stepless = (await db.PmTasks.AsNoTracking().Where(t => templateIds.Contains(t.Id) && !t.Steps.Any()).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var assets = s.Assets.Where(a => stepless.Contains(a.PmTaskId ?? s.PmTaskId)).OrderBy(a => a.Sequence).Select(a => a.AssetId).ToList();
        if (assets.Count == 0) return [];
        var numbers = await db.Assets.AllSites().AsNoTracking().Where(a => assets.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.AssetNumber, ct);
        return [Translator.T(string.Format(NoStepsWarning, string.Join(", ", assets.Select(a => numbers.GetValueOrDefault(a) ?? "?"))))];
    }

    /// <summary>Pauses or resumes a schedule; resuming a calendar schedule moves its next due date to the first one from today.</summary>
    internal static void SetActive(PmSchedule s, bool active, IReadOnlyList<ScheduleCalendar>? calendars = null)
    {
        calendars ??= [];
        if (active && !s.IsActive && PmTriggers.UsesDates(s, calendars.Count)) s.NextDueDate = PmTriggers.First(s, Today, calendars);
        s.IsActive = active;
    }

    internal static void ApplySchedule(PmSchedule s, ScheduleInput i)
    {
        s.Description = i.Description.Clean(); s.SiteId = i.SiteId; s.LocationId = i.LocationId; s.AssetId = i.AssetId; s.TradeId = i.TradeId; s.TeamId = i.TeamId; s.Priority = i.Priority;
        if (i.OnlyWhenStopped is { } stopped) s.OnlyWhenStopped = stopped; // left out (older pages, the import): unchanged
        if (i.ManagesContract == true) s.ContractId = i.ContractId; // the contract (0.17.0), passed to the work it creates
        // A cost centre for its work (0.20.0); none = each work order's asset's or location's. Pages from before leave it as it is.
        if (i.ManagesCostCentre == true || s.CreatedAt == default) s.CostCentreId = i.CostCentreId;
        if (i.RecipientIds is not null) s.RecipientIds = i.RecipientIds.Distinct().ToList(); // left out: unchanged
        if (i.SpreadParts is { } spread && (spread >= 2 ? spread : (int?)null) != s.SpreadParts)
        {
            // Even spread (0.12.0): 0 or 1 = off; a change starts a new period at the next date.
            s.SpreadParts = spread >= 2 ? spread : null; s.SpreadIndex = 0; s.CycleStart = null;
        }
        if (i.MultiAssetMode is { } multi) s.MultiAssetMode = multi;
        if (i.GroupPlan is { } plan) s.GroupPlan = plan;
        if (i.ManagesRules == true)
        {
            // Null = the organization's rule (0.12.0); pages and the import that don't manage rules leave them as they are.
            s.SkipStatusesOverride = i.SkipStatusesOverride?.Distinct().ToList(); s.NonWorkingDaysOverride = i.NonWorkingDaysOverride?.Distinct().ToList();
            s.NoStackingOverride = i.NoStackingOverride;
        }
        s.AssigneeIds = (i.AssigneeIds ?? []).Distinct().ToList(); s.PmTaskId = i.PmTaskId; s.Type = i.Type; s.Basis = i.Basis; s.Frequency = i.Frequency;
        s.Unit = i.Type == PmScheduleType.FixedInterval || i.TimeBased ? i.Unit : FrequencyUnit.Month;
        s.WeekOfMonth = i.Type == PmScheduleType.DayOfWeekInMonth ? i.WeekOfMonth : null; s.DayOfWeek = i.Type == PmScheduleType.DayOfWeekInMonth ? i.DayOfWeek : null;
        s.DayOfMonth = i.Type == PmScheduleType.DateOfMonth ? i.DayOfMonth : null; s.WholeMonth = i.Type == PmScheduleType.DateOfMonth && i.WholeMonth;
        s.WorkPeriodDays = i.WorkPeriodDays; s.LeadDays = i.LeadDays; s.StartDate = i.StartDate; s.IsActive = i.IsActive;
        s.MeterId = i.Type == PmScheduleType.Meter ? i.MeterId : null; s.MeterTrigger = i.Type == PmScheduleType.Meter ? i.MeterTrigger ?? MeterTrigger.Incremental : null;
        s.MeterInterval = i.MeterInterval; s.LowerLimit = i.LowerLimit; s.UpperLimit = i.UpperLimit; s.LowerTaskId = i.LowerTaskId; s.UpperTaskId = i.UpperTaskId;
        s.TimeBased = i.Type == PmScheduleType.Meter && i.TimeBased;
        if (i.NextDueReading is not null) s.NextDueReading = i.NextDueReading;
        // An asset group (0.21.0) replaces the asset and the list: each generation takes its members.
        s.AssetGroupId = i.AssetGroupId;
        if (s.AssetGroupId is not null) s.AssetId = null;
        var listed = s.AssetGroupId is null ? (i.AssetIds ?? []).Distinct().ToList() : [];
        s.Assets.RemoveAll(a => !listed.Contains(a.AssetId));
        for (var n = 0; n < listed.Count; n++)
        {
            var existing = s.Assets.FirstOrDefault(a => a.AssetId == listed[n]);
            if (existing is null) s.Assets.Add(new PmScheduleAsset { PmScheduleId = s.Id, AssetId = listed[n], Sequence = n + 1 });
            else existing.Sequence = n + 1;
        }
        if (i.AssetTemplates is not null)
        {
            // Each asset's own template (0.12.0); assets not named use the PM's. Left out: unchanged.
            var own = i.AssetTemplates.GroupBy(a => a.AssetId).ToDictionary(g => g.Key, g => g.First().PmTaskId);
            foreach (var a in s.Assets) a.PmTaskId = own.GetValueOrDefault(a.AssetId) is { } tid && tid != i.PmTaskId ? tid : null;
        }
        if (i.CalendarIds is not null)
        {
            var cals = i.CalendarIds.Distinct().ToList();
            s.Calendars.RemoveAll(c => !cals.Contains(c.ScheduleCalendarId));
            foreach (var c in cals.Where(c => s.Calendars.All(x => x.ScheduleCalendarId != c))) s.Calendars.Add(new PmScheduleCalendar { PmScheduleId = s.Id, ScheduleCalendarId = c });
        }
        if (i.ExtraMeters is not null)
        {
            var meters = i.ExtraMeters.GroupBy(m => m.MeterId).Select(g => g.First()).ToList();
            s.ExtraMeters.RemoveAll(m => meters.All(x => x.MeterId != m.MeterId));
            foreach (var m in meters)
            {
                var row = s.ExtraMeters.FirstOrDefault(x => x.MeterId == m.MeterId);
                if (row is null) s.ExtraMeters.Add(row = new PmScheduleMeter { PmScheduleId = s.Id, MeterId = m.MeterId });
                row.Interval = Math.Round(m.Interval, 3);
                if (m.NextDueReading is not null) row.NextDueReading = m.NextDueReading;
            }
        }
        var want = (i.ExtraTasks ?? []).GroupBy(e => e.PmTaskId).Select(e => e.First()).Where(e => e.PmTaskId != i.PmTaskId).ToList();
        s.ExtraTasks.RemoveAll(e => want.All(w => w.PmTaskId != e.PmTaskId));
        foreach (var w in want)
        {
            var existing = s.ExtraTasks.FirstOrDefault(e => e.PmTaskId == w.PmTaskId);
            if (existing is null) s.ExtraTasks.Add(new PmScheduleTask { PmScheduleId = s.Id, PmTaskId = w.PmTaskId, EveryNth = w.EveryNth });
            else existing.EveryNth = w.EveryNth;
        }
    }

    private static async Task<Validator> ValidateMeterAsync(MeterInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Meter no.", 50).Required("name", i.Name, "Name", 200).Required("unit", i.Unit, "Unit", 30)
            .When(i.MaxDailyIncrement <= 0, "maxDailyIncrement", "Must be positive.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await Unique.ClashAsync(db.Meters, x => x.Code == i.Code.Trim() && x.Id != id,
                "Another meter uses this number.", "This number is already used by a meter in a site you can't see.", ct) is { } meterClash)
            v.Add("code", meterClash);
        if (!await db.Assets.AnyAsync(a => a.Id == i.AssetId, ct)) v.Add("assetId", "Choose the asset this meter is on.");
        if (i.ManagesSource == true && i.SourceMeterId is { } source)
        {
            // Meter propagation (0.12.0): same unit, and no loop (following the chain up from the source never reaches this meter).
            var src = await db.Meters.AsNoTracking().Where(m => m.Id == source).Select(m => new { m.Unit }).FirstOrDefaultAsync(ct);
            if (src is null) v.Add("sourceMeterId", "Unknown meter.");
            else if (!string.Equals(src.Unit.Trim(), i.Unit?.Trim(), StringComparison.OrdinalIgnoreCase)) v.Add("sourceMeterId", "A meter can only follow a meter with the same unit.");
            else
            {
                Guid? cur = source;
                for (var n = 0; cur is { } c && n < 100; n++)
                {
                    if (c == id) { v.Add("sourceMeterId", "Meters can't follow each other in a loop."); break; }
                    cur = await db.Meters.IgnoreQueryFilters().Where(m => m.Id == c && m.TenantId == db.CurrentTenantId).Select(m => m.SourceMeterId).FirstOrDefaultAsync(ct);
                }
            }
        }
        return v;
    }

    /// <summary>A future PM occurrence (not generated yet): the PM page's forecast and the planning board.</summary>
    public sealed class ForecastRow
    {
        public DateOnly Date { get; init; }
        public Guid Id { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public Guid? AssetId { get; init; }
        public string? AssetNumber { get; init; }
        public Guid SiteId { get; init; }
        public required string SiteName { get; init; }
        public PmScheduleType Type { get; init; }
        public string? TaskName { get; init; }
        public decimal? Cost { get; init; }
        public bool Overdue { get; init; }
        public bool IsNext { get; init; }
        /// <summary>The people the work order will go to (the schedule's technicians).</summary>
        public List<Guid> AssigneeIds { get; init; } = [];
    }

    /// <param name="perSchedule">At most this many occurrences per PM; <paramref name="total"/>: at most this many rows, earliest first (0.14.0: the annual plan asks for all).</param>
    internal static async Task<List<ForecastRow>> ForecastAsync(CmmsDbContext db, DateOnly from, DateOnly to, Guid? siteId, CancellationToken ct, int perSchedule = 400, int total = 5000)
    {
        var q = db.PmSchedules.AsNoTracking().Include(x => x.ExtraTasks).Include(x => x.Assets).Where(x => x.IsActive && x.NextDueDate != null && x.NextDueDate <= to);
        if (siteId is not null) q = q.Where(x => x.SiteId == siteId);
        var list = await q.Select(x => new { S = x, AssetNumber = x.Asset!.AssetNumber, TaskName = x.PmTask!.Name, SiteName = x.Site!.Name }).ToListAsync(ct);
        var taskNames = await db.PmTasks.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        // Estimated cost per visit, worked out once per schedule and task (not per forecast row).
        var costs = (await PmCost.ForSchedulesAsync(db, list.Select(i => i.S).ToList(), ct)).ToDictionary(x => x.Key, x => x.Value.Total);
        // Each PM's shared calendars (0.12.0), read once for the whole list.
        var ids = list.Select(i => i.S.Id).ToList();
        var links = (await db.PmScheduleCalendars.AsNoTracking().Where(x => ids.Contains(x.PmScheduleId)).ToListAsync(ct)).ToLookup(x => x.PmScheduleId, x => x.ScheduleCalendarId);
        var calendars = (await CalendarsAsync(db, links.SelectMany(x => x), ct)).ToDictionary(c => c.Id);
        IReadOnlyList<ScheduleCalendar> CalendarsOf(Guid id) => links[id].Where(calendars.ContainsKey).Select(c => calendars[c]).ToList();
        return list.SelectMany(i => PmTriggers.Forecast(i.S, to, perSchedule, CalendarsOf(i.S.Id)).Select((d, k) => new ForecastRow
        {
            Date = d, Id = i.S.Id, Code = i.S.Code, Name = i.S.Name, AssetId = i.S.AssetId, AssetNumber = i.AssetNumber, SiteId = i.S.SiteId, SiteName = i.SiteName, Type = i.S.Type,
            TaskName = taskNames.GetValueOrDefault(PmCalendar.TaskFor(i.S, i.S.GenerationCount + 1 + PmTriggers.PeriodOffset(i.S, k))) ?? i.TaskName,
            Cost = costs.GetValueOrDefault((i.S.Id, PmCalendar.TaskFor(i.S, i.S.GenerationCount + 1 + PmTriggers.PeriodOffset(i.S, k)))),
            Overdue = d < Today,
            // The schedule's next occurrence: generating it creates no other work order.
            IsNext = k == 0,
            AssigneeIds = i.S.AssigneeIds,
        })).Where(x => x.Date >= from || x.Overdue).OrderBy(x => x.Date).Take(total).ToList();
    }
}
