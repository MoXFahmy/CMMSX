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
    public sealed record TaskInput(string Code, string Name, string? Description, Guid? TradeId, decimal? EstimatedHours, string? SafetyNotes, List<string>? Steps, bool IsActive = true, string? RowVersion = null);
    public sealed record ExtraTaskInput(Guid PmTaskId, int EveryNth);
    public sealed record ScheduleInput(string? Code, string Name, string? Description, Guid SiteId, Guid? LocationId, Guid? AssetId, Guid? TradeId, WorkPriority Priority,
        List<Guid>? AssigneeIds, Guid PmTaskId, List<ExtraTaskInput>? ExtraTasks, PmScheduleType Type, PmBasis Basis, int Frequency, FrequencyUnit Unit,
        int? WeekOfMonth, DayOfWeek? DayOfWeek, int? DayOfMonth, int WorkPeriodDays, bool WholeMonth, int LeadDays, DateOnly StartDate, DateOnly? NextDueDate,
        Guid? MeterId, MeterTrigger? MeterTrigger, decimal? MeterInterval, decimal? NextDueReading, decimal? LowerLimit, decimal? UpperLimit,
        Guid? LowerTaskId, Guid? UpperTaskId, bool TimeBased, bool IsActive = true, string? RowVersion = null);
    public sealed record GenerateInput(DateOnly Until, Guid? SiteId, List<Guid>? ScheduleIds);
    public sealed record MeterInput(string Code, string Name, Guid AssetId, string Unit, decimal? MaxDailyIncrement, bool IsActive = true);
    public sealed record ReadingInput(decimal Value, DateTimeOffset? ReadAt, string? Note, bool Confirm = false);

    private static readonly Dictionary<string, Expression<Func<PmSchedule, object?>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = s => s.Code, ["name"] = s => s.Name, ["nextDueDate"] = s => s.NextDueDate, ["asset"] = s => s.Asset!.AssetNumber, ["type"] = s => s.Type,
        ["lastGeneratedAt"] = s => s.LastGeneratedAt,
    };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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
            await db.PmTasks.AsNoTracking().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, ct) is { } x
                ? Results.Ok(new { x.Id, x.Code, x.Name, x.Description, x.TradeId, x.EstimatedHours, x.SafetyNotes, x.IsActive, Steps = x.Steps.OrderBy(s => s.Sequence).Select(s => s.Description), RowVersion = Convert.ToBase64String(x.RowVersion) })
                : Results.NotFound()).RequirePermission(Permissions.PmView);
        t.MapPost("/", async (TaskInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateTaskAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = new PmTask { Code = input.Code.Trim(), Name = input.Name.Trim() };
            ApplyTask(x, input);
            db.PmTasks.Add(x);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pm-tasks/{x.Id}", new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        t.MapPut("/{id:guid}", async (Guid id, TaskInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.PmTasks.Include(y => y.Steps).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            var v = await ValidateTaskAsync(input with { Code = x.Code }, id, db, ct); // task numbers are permanent (as in 4CSQL)
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(x).Property(y => y.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            x.Name = input.Name.Trim();
            db.PmTaskSteps.RemoveRange(x.Steps);
            x.Steps = [];
            ApplyTask(x, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        t.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.PmTasks.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PmSchedules.AnyAsync(s => s.PmTaskId == id || s.LowerTaskId == id || s.UpperTaskId == id || s.ExtraTasks.Any(e => e.PmTaskId == id), ct))
                throw new DomainException("This task is used by PM schedules. Mark it inactive instead.");
            db.PmTasks.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);

        // ---------------- PM schedules ----------------
        var g = api.MapGroup("/pm-schedules").WithTags("PM schedules");
        g.MapGet("/", async (CmmsDbContext db, string? search, Guid? siteId, Guid? assetId, PmScheduleType? type, bool? activeOnly, int? dueWithinDays,
            string? sort, string? dir, int? page, int? pageSize, CancellationToken ct) =>
        {
            var q = db.PmSchedules.AsNoTracking();
            if (search.Clean() is { } s) q = q.Where(x => x.Code.Contains(s) || x.Name.Contains(s) || x.Asset!.AssetNumber.Contains(s));
            if (siteId is not null) q = q.Where(x => x.SiteId == siteId);
            if (assetId is not null) q = q.Where(x => x.AssetId == assetId || (x.Asset != null && x.Asset.ParentAssetId == assetId));
            if (type is not null) q = q.Where(x => x.Type == type);
            if (activeOnly == true) q = q.Where(x => x.IsActive);
            if (dueWithinDays is { } d) { var until = Today.AddDays(d); q = q.Where(x => x.IsActive && x.NextDueDate != null && x.NextDueDate <= until); }
            var paged = await q.SortBy(sort, dir, SortMap, "nextDueDate").Include(x => x.Meter).Include(x => x.ExtraTasks)
                .Select(x => new { S = x, SiteName = x.Site!.Name, AssetNumber = x.Asset!.AssetNumber, AssetName = x.Asset.Name, LocationName = x.Location!.Name, TaskCode = x.PmTask!.Code, TaskName = x.PmTask.Name,
                    MeterUnit = x.Meter!.Unit, MeterReading = x.Meter.CurrentReading,
                    LastWorkOrderNumber = db.WorkOrders.Where(w => w.Id == x.LastWorkOrderId).Select(w => w.Number).FirstOrDefault(),
                    OpenWorkOrders = db.WorkOrders.Count(w => w.PmScheduleId == x.Id && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)) })
                .ToPagedAsync(page ?? 1, pageSize ?? 25, ct);
            var today = Today;
            return new PagedResult<object>(paged.Items.Select(i => (object)new
            {
                i.S.Id, i.S.Code, i.S.Name, i.S.Type, i.S.Basis, i.S.IsActive, i.S.SiteId, i.SiteName, i.S.AssetId, i.AssetNumber, i.AssetName, i.LocationName,
                i.TaskCode, i.TaskName, Schedule = PmCalendar.Describe(i.S), i.S.NextDueDate, i.S.NextDueReading, i.MeterUnit, i.MeterReading, i.S.LastGeneratedAt,
                i.S.LastWorkOrderId, i.LastWorkOrderNumber, i.OpenWorkOrders, IsOverdue = i.S.IsActive && i.S.NextDueDate < today, MultipleTasks = i.S.ExtraTasks.Count,
            }).ToList(), paged.Total, paged.Page, paged.PageSize);
        }).RequirePermission(Permissions.PmView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.PmSchedules.AsNoTracking().Include(x => x.ExtraTasks).Include(x => x.Asset).Include(x => x.Meter).Include(x => x.PmTask).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var recent = await db.WorkOrders.AsNoTracking().Where(w => w.PmScheduleId == id).OrderByDescending(w => w.CreatedAt).Take(10)
                .Select(w => new { w.Id, w.Number, w.Title, w.Status, w.PmDueDate, w.CompletedAt }).ToListAsync(ct);
            return Results.Ok(new
            {
                s.Id, s.Code, s.Name, s.Description, s.SiteId, s.LocationId, s.AssetId, Asset = s.Asset is null ? null : new { s.Asset.Id, s.Asset.AssetNumber, s.Asset.Name },
                s.TradeId, s.Priority, s.AssigneeIds, s.PmTaskId, Task = new { s.PmTask!.Id, s.PmTask.Code, s.PmTask.Name },
                ExtraTasks = s.ExtraTasks.Select(e => new { e.PmTaskId, e.EveryNth }), s.Type, s.Basis, s.Frequency, s.Unit, s.WeekOfMonth, s.DayOfWeek, s.DayOfMonth,
                s.WorkPeriodDays, s.WholeMonth, s.LeadDays, s.StartDate, s.NextDueDate, s.LastDueDate, s.LastGeneratedAt, s.GenerationCount,
                s.MeterId, Meter = s.Meter is null ? null : new { s.Meter.Id, s.Meter.Code, s.Meter.Name, s.Meter.Unit, s.Meter.CurrentReading },
                s.MeterTrigger, s.MeterInterval, s.NextDueReading, s.LowerLimit, s.UpperLimit, s.LowerTaskId, s.UpperTaskId, s.TimeBased, s.IsActive,
                Schedule = PmCalendar.Describe(s), Upcoming = PmCalendar.Forecast(s, Today.AddYears(2), 6), RecentWorkOrders = recent,
                s.LegacyRef, RowVersion = Convert.ToBase64String(s.RowVersion),
            });
        }).RequirePermission(Permissions.PmView);

        // Live preview for the schedule form: describe + next dates for a draft, without saving.
        g.MapPost("/simulate", (ScheduleInput input) =>
        {
            var s = new PmSchedule { Code = "", Name = "" };
            ApplySchedule(s, input);
            s.NextDueDate = input.NextDueDate ?? PmCalendar.FirstOnOrAfter(s, input.StartDate);
            var dates = PmCalendar.Forecast(s, s.NextDueDate.Value.AddYears(3), 6).ToList();
            return Results.Ok(new { Schedule = PmCalendar.Describe(s), Upcoming = dates, DueDates = dates.Select(d => PmCalendar.DueDate(s, d)) });
        }).RequirePermission(Permissions.PmView);

        g.MapPost("/", async (ScheduleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateScheduleAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var code = input.Code.Clean() ?? await Numbering.NextAsync(db, "PmSchedule", "PM-", 5, n => db.PmSchedules.AnyAsync(x => x.Code == n, ct),
                p => db.PmSchedules.Where(x => x.Code.StartsWith(p)).Select(x => x.Code).ToListAsync(ct), ct);
            var s = new PmSchedule { Code = code, Name = input.Name.Trim() };
            ApplySchedule(s, input);
            s.NextDueDate = PmCalendar.UsesCalendar(s) ? input.NextDueDate ?? PmCalendar.FirstOnOrAfter(s, input.StartDate) : null;
            if (s.Type == PmScheduleType.Meter && s.MeterTrigger == MeterTrigger.Incremental && s.NextDueReading is null)
                s.NextDueReading = (await db.Meters.Where(m => m.Id == s.MeterId).Select(m => m.CurrentReading).FirstOrDefaultAsync(ct) ?? 0) + (s.MeterInterval ?? 0);
            db.PmSchedules.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pm-schedules/{s.Id}", new { s.Id, s.Code });
        }).RequirePermission(Permissions.PmManage);

        g.MapPut("/{id:guid}", async (Guid id, ScheduleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var s = await db.PmSchedules.Include(x => x.ExtraTasks).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var v = await ValidateScheduleAsync(input, id, db, ct);
            if (input.Basis != s.Basis && s.GenerationCount > 0) v.Add("basis", "The generation basis cannot change after work orders were generated (as in 4CSQL). Create a new schedule instead.");
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(s).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            s.Name = input.Name.Trim();
            if (input.Code.Clean() is { } c) s.Code = c;
            var wasActive = s.IsActive;
            ApplySchedule(s, input);
            if (PmCalendar.UsesCalendar(s))
                s.NextDueDate = input.NextDueDate ?? (!wasActive && s.IsActive ? PmCalendar.FirstOnOrAfter(s, Today) : s.NextDueDate ?? (s.GenerationCount == 0 ? PmCalendar.FirstOnOrAfter(s, input.StartDate) : null));
            else s.NextDueDate = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { s.Id });
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
        var pm = api.MapGroup("/pm").WithTags("PM generation");
        pm.MapGet("/preview", async (DateOnly until, Guid? siteId, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            await PmGenerator.RunAsync(db, ctx, until, siteId, null, dryRun: true, ct)).RequirePermission(Permissions.PmView);
        pm.MapPost("/generate", async (GenerateInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (input.Until > Today.AddYears(1)) throw new DomainException("Generate at most one year ahead.");
            var results = await PmGenerator.RunAsync(db, ctx, input.Until, input.SiteId, input.ScheduleIds, dryRun: false, ct);
            return Results.Ok(new { created = results.Count(r => r.WorkOrderId is not null), results });
        }).RequirePermission(Permissions.PmGenerate);
        pm.MapGet("/forecast", async (DateOnly from, DateOnly to, Guid? siteId, CmmsDbContext db, CancellationToken ct) =>
        {
            if (to < from || to > from.AddYears(2)) throw new DomainException("Choose a range of up to two years.");
            var q = db.PmSchedules.AsNoTracking().Include(x => x.ExtraTasks).Where(x => x.IsActive && x.NextDueDate != null && x.NextDueDate <= to);
            if (siteId is not null) q = q.Where(x => x.SiteId == siteId);
            var list = await q.Select(x => new { S = x, AssetNumber = x.Asset!.AssetNumber, TaskName = x.PmTask!.Name, SiteName = x.Site!.Name }).ToListAsync(ct);
            var taskNames = await db.PmTasks.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            return list.SelectMany(i => PmCalendar.Forecast(i.S, to, 400).Select((d, k) => new
            {
                Date = d, i.S.Id, i.S.Code, i.S.Name, i.AssetNumber, i.SiteName, i.S.Type,
                TaskName = taskNames.GetValueOrDefault(PmCalendar.TaskFor(i.S, i.S.GenerationCount + 1 + k)) ?? i.TaskName,
                Overdue = d < Today,
            })).Where(x => x.Date >= from || x.Overdue).OrderBy(x => x.Date).Take(5000);
        }).RequirePermission(Permissions.PmView);

        // ---------------- Meters ----------------
        var m = api.MapGroup("/meters").WithTags("Meters");
        m.MapGet("/", async (CmmsDbContext db, Guid? assetId, string? search, CancellationToken ct) =>
        {
            var q = db.Meters.AsNoTracking();
            if (assetId is not null) q = q.Where(x => x.AssetId == assetId);
            if (search.Clean() is { } s) q = q.Where(x => x.Code.Contains(s) || x.Name.Contains(s) || x.Asset!.AssetNumber.Contains(s));
            return await q.OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.AssetId, AssetNumber = x.Asset!.AssetNumber, AssetName = x.Asset.Name, x.Unit,
                x.CurrentReading, x.LastReadAt, x.MaxDailyIncrement, x.IsActive, Schedules = db.PmSchedules.Count(p => p.MeterId == x.Id && p.IsActive) }).ToListAsync(ct);
        }).RequirePermission(Permissions.PmView);
        m.MapPost("/", async (MeterInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateMeterAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var x = new Meter { Code = input.Code.Trim(), Name = input.Name.Trim(), AssetId = input.AssetId, Unit = input.Unit.Trim(), MaxDailyIncrement = input.MaxDailyIncrement, IsActive = input.IsActive };
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
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { x.Id });
        }).RequirePermission(Permissions.PmManage);
        m.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var x = await db.Meters.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (x is null) return Results.NotFound();
            if (await db.PmSchedules.AnyAsync(p => p.MeterId == id, ct)) throw new DomainException("PM schedules use this meter. Remove them first or mark the meter inactive.");
            db.Meters.Remove(x);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);
        m.MapGet("/{id:guid}/readings", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.MeterReadings.AsNoTracking().Where(r => r.MeterId == id).OrderByDescending(r => r.ReadAt).Take(200)
                .Select(r => new { r.Id, r.Value, r.ReadAt, r.Note, r.Source, r.ByName, r.WorkOrderId }).ToListAsync(ct)).RequirePermission(Permissions.PmView);
        m.MapPost("/{id:guid}/readings", async (Guid id, ReadingInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var meter = await db.Meters.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (meter is null) return Results.NotFound();
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
            if (!v.IsValid) return v.Problem();
            db.MeterReadings.Add(new MeterReading { MeterId = id, Value = input.Value, ReadAt = at, Note = input.Note.Clean(), ByName = ctx.UserName });
            if (meter.LastReadAt is null || at >= meter.LastReadAt) { meter.CurrentReading = input.Value; meter.LastReadAt = at; }
            await db.SaveChangesAsync(ct);
            var generated = meter.IsActive ? await PmGenerator.OnReadingAsync(db, ctx, meter, input.Value, ct) : [];
            return Results.Ok(new { ok = true, generated });
        }).RequirePermission(Permissions.MetersRecord);
    }

    private static async Task<Validator> ValidateTaskAsync(TaskInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Task no.", 50).Required("name", i.Name, "Name", 300).MaxLength("safetyNotes", i.SafetyNotes, "Safety notes", 4000)
            .When(i.EstimatedHours < 0, "estimatedHours", "Estimate cannot be negative.").When((i.Steps ?? []).Count > 300, "steps", "At most 300 steps.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.PmTasks.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another task uses this number.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(x => x.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        return v;
    }

    private static void ApplyTask(PmTask x, TaskInput i)
    {
        x.Description = i.Description.Clean(); x.TradeId = i.TradeId; x.EstimatedHours = i.EstimatedHours; x.SafetyNotes = i.SafetyNotes.Clean(); x.IsActive = i.IsActive;
        var seq = 0;
        foreach (var step in (i.Steps ?? []).Select(s => s.Clean()).OfType<string>())
            x.Steps.Add(new PmTaskStep { PmTaskId = x.Id, Sequence = ++seq, Description = step.Length > 2000 ? step[..2000] : step });
    }

    private static async Task<Validator> ValidateScheduleAsync(ScheduleInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
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
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.PmSchedules.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another schedule uses this number.");
        if (!await db.Sites.AnyAsync(s => s.Id == i.SiteId, ct)) v.Add("siteId", "Choose a site.");
        if (i.AssetId is null && i.LocationId is null) v.Add("assetId", "Choose the asset or location to maintain.");
        if (i.LocationId is not null && !await db.Locations.AnyAsync(l => l.Id == i.LocationId && l.SiteId == i.SiteId, ct)) v.Add("locationId", "Location must belong to the selected site.");
        if (i.AssetId is not null && !await db.Assets.AnyAsync(a => a.Id == i.AssetId && a.SiteId == i.SiteId, ct)) v.Add("assetId", "Asset must belong to the selected site.");
        var taskIds = new[] { (Guid?)i.PmTaskId, i.LowerTaskId, i.UpperTaskId }.OfType<Guid>().Concat((i.ExtraTasks ?? []).Select(e => e.PmTaskId)).Distinct().ToList();
        if (await db.PmTasks.CountAsync(x => taskIds.Contains(x.Id), ct) != taskIds.Count) v.Add("pmTaskId", "Choose a valid PM task.");
        if ((i.ExtraTasks ?? []).Any(e => e.EveryNth is < 2 or > 1000)) v.Add("extraTasks", "\"Every Nth\" must be between 2 and 1000.");
        if (i.TradeId is not null && !await db.Trades.AnyAsync(x => x.Id == i.TradeId, ct)) v.Add("tradeId", "Unknown trade.");
        if (i.Type == PmScheduleType.Meter)
        {
            if (i.MeterId is null || !await db.Meters.AnyAsync(x => x.Id == i.MeterId, ct)) v.Add("meterId", "Choose a meter.");
            if (i.MeterTrigger == MeterTrigger.Threshold && i.LowerLimit is null && i.UpperLimit is null) v.Add("lowerLimit", "Set a lower and/or upper limit.");
            if (i.MeterTrigger == MeterTrigger.Threshold && i.LowerLimit >= i.UpperLimit) v.Add("upperLimit", "Upper limit must be above the lower limit.");
            if (i.MeterTrigger != MeterTrigger.Threshold && i.MeterInterval is not > 0) v.Add("meterInterval", "Enter the meter interval (e.g. every 250 hours).");
            if (i.TimeBased && i.MeterTrigger == MeterTrigger.Threshold) v.Add("timeBased", "Time-based generation is only available for incremental meters.");
        }
        return v;
    }

    private static void ApplySchedule(PmSchedule s, ScheduleInput i)
    {
        s.Description = i.Description.Clean(); s.SiteId = i.SiteId; s.LocationId = i.LocationId; s.AssetId = i.AssetId; s.TradeId = i.TradeId; s.Priority = i.Priority;
        s.AssigneeIds = (i.AssigneeIds ?? []).Distinct().ToList(); s.PmTaskId = i.PmTaskId; s.Type = i.Type; s.Basis = i.Basis; s.Frequency = i.Frequency;
        s.Unit = i.Type == PmScheduleType.FixedInterval || i.TimeBased ? i.Unit : FrequencyUnit.Month;
        s.WeekOfMonth = i.Type == PmScheduleType.DayOfWeekInMonth ? i.WeekOfMonth : null; s.DayOfWeek = i.Type == PmScheduleType.DayOfWeekInMonth ? i.DayOfWeek : null;
        s.DayOfMonth = i.Type == PmScheduleType.DateOfMonth ? i.DayOfMonth : null; s.WholeMonth = i.Type == PmScheduleType.DateOfMonth && i.WholeMonth;
        s.WorkPeriodDays = i.WorkPeriodDays; s.LeadDays = i.LeadDays; s.StartDate = i.StartDate; s.IsActive = i.IsActive;
        s.MeterId = i.Type == PmScheduleType.Meter ? i.MeterId : null; s.MeterTrigger = i.Type == PmScheduleType.Meter ? i.MeterTrigger ?? MeterTrigger.Incremental : null;
        s.MeterInterval = i.MeterInterval; s.LowerLimit = i.LowerLimit; s.UpperLimit = i.UpperLimit; s.LowerTaskId = i.LowerTaskId; s.UpperTaskId = i.UpperTaskId;
        s.TimeBased = i.Type == PmScheduleType.Meter && i.TimeBased;
        if (i.NextDueReading is not null) s.NextDueReading = i.NextDueReading;
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
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.Meters.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "Another meter uses this number.");
        if (!await db.Assets.AnyAsync(a => a.Id == i.AssetId, ct)) v.Add("assetId", "Choose the asset this meter is on.");
        return v;
    }
}
