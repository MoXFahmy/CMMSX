using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Smart checklists (0.13.0): what happens on the server when steps are made and answered.</summary>
public static class Checklists
{
    /// <summary>A change to one step: the answer that fits its type, and its reading text and remarks. <c>IsDone</c> is an older client's tick.</summary>
    public sealed record StepChange(bool? IsDone, StepAnswer? Answer = null, decimal? Value = null, string? Text = null, Guid? MeterId = null, string? Reading = null,
        string? Remarks = null, bool Confirm = false);

    public const string WrongKind = "This step needs a different kind of answer.";
    public const string ChooseMeter = "Choose the meter this reading is for.";

    /// <summary>
    /// Answers a step (web, All OK, phone): checks the answer fits the step, judges a number against its measurement point (range, level,
    /// one reading per step), records a meter step's reading, and stamps who and when. Nothing changes when the answer is refused.
    /// </summary>
    public static async Task<Validator> AnswerAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, TenantSettings settings, WorkOrder w, WorkOrderTask t,
        StepChange c, DateTimeOffset at, CancellationToken ct)
    {
        var v = new Validator();
        var takesValue = t.Type is StepType.Number or StepType.Meter;
        if (c.Answer is not null && t.Type != StepType.Check || c.Value is not null && !takesValue || c.Text is not null && t.Type != StepType.Text
            || c.IsDone is true && t.Type != StepType.Check && c.Value is null && c.Text is null && !StepRules.Answered(t))
        {
            v.Add("answer", WrongKind);
            return v;
        }
        if (c.Text?.Trim() is { Length: > 1000 }) { v.Add("text", "Text can be at most 1000 characters."); return v; }
        // Safety steps first (0.13.0): answering another step needs them (its asset's on a round); clearing an answer is always allowed.
        var answering = c.Answer is not null || c.Value is not null || c.Text is not null || c.IsDone is true;
        if (answering && !t.BeforeStart
            && StepRules.SafetyBlock(await db.WorkOrderTasks.Where(x => x.WorkOrderId == w.Id).ToListAsync(ct), t.AssetId, forStart: false) is { } safety)
        {
            v.Add("tasks", safety);
            return v;
        }
        var changed = false;
        var point = t.Type == StepType.Number && t.MeasurementPointId is { } pointId
            ? await db.MeasurementPoints.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pointId && p.TenantId == db.CurrentTenantId, ct) : null;
        switch (t.Type)
        {
            case StepType.Check:
            {
                // A step ticked before 0.13.0 (done, no answer) is OK until someone changes its answer.
                var answer = c.Answer ?? (c.IsDone is true ? t.Answer ?? StepAnswer.Ok : c.IsDone is false ? null : StepRules.ShownAnswer(t));
                if (answer != t.Answer) { t.Answer = answer; changed = true; }
                break;
            }
            case StepType.Number:
                if (c.Value is { } raw)
                {
                    if (point is not null && MeasureLevels.OutOfRange(point, raw)) { v.Add("value", MeasureLevels.RangeMessage(point)); return v; }
                    var value = Math.Round(raw, Math.Clamp(point?.Decimals ?? t.Decimals ?? 2, 0, 4));
                    if (value != t.Value)
                    {
                        t.Value = value; t.Level = point is null ? null : MeasureLevels.LevelOf(point, value); changed = true;
                        if (point is not null) await WriteReadingAsync(db, ctx, w, t, point, at, ct);
                    }
                }
                else if (c.IsDone is false && t.Value is not null)
                {
                    t.Value = null; t.Level = null; changed = true;
                    if (await db.MeasurementReadings.FirstOrDefaultAsync(r => r.WorkOrderTaskId == t.Id, ct) is { } old) db.MeasurementReadings.Remove(old);
                }
                break;
            case StepType.Meter:
                if (c.Value is { } reading)
                {
                    var meterId = c.MeterId ?? t.MeterId;
                    if (meterId is null) { v.Add("meterId", ChooseMeter); return v; }
                    var asset = t.AssetId ?? w.AssetId;
                    var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == meterId && (asset == null || m.AssetId == asset), ct);
                    if (meter is null) { v.Add("meterId", "Unknown meter."); return v; }
                    if (reading != t.Value || meter.Id != t.MeterId)
                    {
                        var (mv, _) = await PmEndpoints.RecordReadingAsync(db, ctx, mail, meter,
                            new PmEndpoints.ReadingInput(reading, at, $"{w.Number} · #{t.Sequence}", c.Confirm), ct, w.Id);
                        if (!mv.IsValid) return mv;
                        t.MeterId = meter.Id; t.Value = reading; changed = true;
                    }
                }
                else if (c.IsDone is false && t.Value is not null) { t.Value = null; changed = true; } // the meter keeps its reading
                break;
            case StepType.Text:
            {
                var text = c.Text is not null ? c.Text.Clean() : c.IsDone is false ? null : t.TextValue;
                if (text != t.TextValue) { t.TextValue = text; changed = true; }
                break;
            }
        }
        if (c.Reading is not null && c.Reading.Clean() != t.Reading) { t.Reading = c.Reading.Clean(); changed = true; } // an empty string clears it
        if (c.Remarks is not null && c.Remarks.Clean() != t.Remarks) { t.Remarks = c.Remarks.Clean(); changed = true; }
        var done = t.Type == StepType.Check ? t.Answer is not null : StepRules.Answered(t);
        if (done != t.IsDone)
        {
            t.IsDone = done; t.DoneAt = done ? at : null; t.DoneBy = done ? ctx.UserName : null; changed = true;
            if (done) await WorkOrderEndpoints.StepDoneAsync(db, ctx, w.Id, at, ct);
        }
        if (changed) { t.UpdatedAt = at; t.UpdatedById = ctx.UserId; t.UpdatedByName = ctx.UserName; }
        // Follow-up work (0.13.0): once per step, in the same save as the answer.
        if (StepRules.Failed(t) && t.FollowUpWorkOrderId is null && t.FollowUpRequestId is null)
        {
            var (woId, requestId, _) = await RaiseFollowUpAsync(db, ctx, mail, settings, w, t, point, t.Level ?? MeasureLevel.Normal, t.Value, t.AssetId ?? w.AssetId, w.SiteId,
                t.Remarks, ct);
            t.FollowUpWorkOrderId = woId; t.FollowUpRequestId = requestId;
            if (point is not null && await db.MeasurementReadings.FirstOrDefaultAsync(r => r.WorkOrderTaskId == t.Id, ct) is { } reading)
            { reading.FollowUpWorkOrderId = woId; reading.FollowUpRequestId = requestId; }
        }
        return v;
    }

    public const string FromStep = "{0} — from step {1} of {2}";
    public const string FromReading = "{0} — from a reading of {1}";

    public static string LevelWord(MeasureLevel l) => l switch
    {
        MeasureLevel.AlarmHigh => "Alarm high", MeasureLevel.AlertHigh => "Alert high", MeasureLevel.AlertLow => "Alert low", MeasureLevel.AlarmLow => "Alarm low", _ => "Normal",
    };

    private static string Num(decimal d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Raises the follow-up for a failed step or an alert/alarm reading: nothing when the step says None; a work order from the point's template
    /// for the level, else from the step's (or the organization's) template when the kind is a work order; else a request for triage.
    /// </summary>
    internal static async Task<(Guid? WorkOrderId, Guid? RequestId, string? Number)> RaiseFollowUpAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail,
        TenantSettings settings, WorkOrder? source, WorkOrderTask? step, MeasurementPoint? point, MeasureLevel level, decimal? value, Guid? assetId, Guid siteId,
        string? remarks, CancellationToken ct)
    {
        var kind = step is null || step.FollowUp == FollowUpKind.Default ? settings.FollowUpDefault : step.FollowUp;
        if (kind == FollowUpKind.None || kind == FollowUpKind.Default) return (null, null, null);
        var templateId = point is not null ? MeasureLevels.TemplateFor(point, level) : null;
        if (templateId is null && kind == FollowUpKind.WorkOrder) templateId = step?.FollowUpTemplateId ?? settings.FollowUpTemplateId;
        var template = templateId is { } tid ? await db.PmTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid, ct) : null;
        var locationId = assetId is { } aid ? await db.Assets.AllSites().Where(a => a.Id == aid).Select(a => a.LocationId).FirstOrDefaultAsync(ct) : source?.LocationId;
        var unit = point?.Unit ?? step?.Unit;
        var reading = value is { } v ? $"{Num(v)} {unit}".Trim() : null;
        var lines = new List<string?> { step?.Description ?? point?.Name };
        if (step?.Answer == StepAnswer.NotOk) lines.Add(Translator.T(string.Format("Answer: {0}", Translator.T("Not OK"))));
        if (value is { } val && level != MeasureLevel.Normal) lines.Add(Translator.T(string.Format("Value: {0} {1} ({2})", Num(val), unit ?? "", Translator.T(LevelWord(level)))));
        lines.Add(remarks);
        var description = string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        // The main title in the organization's language, whoever answered (0.28.0); the Arabic one is made below.
        string Title(string what) => Translator.T(source is not null && step is not null ? string.Format(FromStep, what, step.Sequence, source.Number)
            : string.Format(FromReading, what, reading ?? ""), settings.DefaultLanguage);
        if (template is not null)
        {
            var title = Title(template.Title is { Length: > 0 } tt ? tt : template.Name);
            // The Arabic title (0.28.0): the template's Arabic title with the same "from step" words in Arabic; none without one.
            var arWhat = template.Title is { Length: > 0 } ? template.TitleAr : template.NameAr;
            var titleAr = arWhat is null ? null : Translator.T(source is not null && step is not null ? string.Format(FromStep, arWhat, step.Sequence, source.Number)
                : string.Format(FromReading, arWhat, reading ?? ""), Languages.Arabic);
            var input = new WorkOrderEndpoints.WorkOrderInput(title.Length > 300 ? title[..300] : title, description, template.WorkType,
                template.Priority ?? source?.Priority ?? WorkPriority.Medium, siteId, locationId, assetId, template.TradeId, null, null, null, template.EstimatedHours, null,
                TemplateId: template.Id, TitleAr: titleAr is { Length: > 300 } ? titleAr[..300] : titleAr ?? "");
            var wo = await WorkOrderEndpoints.CreateAsync(db, ctx, input, null, ct, x => { x.SourceWorkOrderId = source?.Id; x.SourceTaskId = step?.Id; },
                source is null ? "Follow-up of a reading" : $"Follow-up of {source.Number}", mail);
            return (wo.Id, null, wo.Number);
        }
        var requestTitle = Title(step?.Description ?? point?.Name ?? "");
        var request = await WorkRequestEndpoints.CreateFollowUpAsync(db, ctx, mail, new WorkRequestEndpoints.RequestInput(requestTitle.Length > 300 ? requestTitle[..300] : requestTitle,
            description, siteId, locationId, assetId, source?.Priority ?? WorkPriority.Medium, WorkType.Corrective, null, null, null, null),
            r => { r.SourceWorkOrderId = source?.Id; r.SourceTaskId = step?.Id; }, ct);
        return (null, request.Id, request.Number);
    }

    /// <summary>The measurement points steps use, with their range and levels (for the work order page and the phone).</summary>
    internal static async Task<List<PointView>> PointsAsync(CmmsDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.MeasurementPoints.IgnoreQueryFilters().AsNoTracking().Where(p => list.Contains(p.Id) && p.TenantId == db.CurrentTenantId).OrderBy(p => p.Code)
            .Select(p => new PointView(p.Id, p.Code, p.Name, p.Kind, p.Unit, p.Decimals, p.ValidMin, p.ValidMax, p.AlarmLow, p.AlertLow, p.Target, p.AlertHigh, p.AlarmHigh))
            .ToListAsync(ct);
    }

    public sealed record PointView(Guid Id, string Code, string Name, MeasurementKind Kind, string? Unit, int Decimals, decimal? ValidMin, decimal? ValidMax,
        decimal? AlarmLow, decimal? AlertLow, decimal? Target, decimal? AlertHigh, decimal? AlarmHigh);

    /// <summary>The work order and step a follow-up came from, for its page; null when there is none or it can't be seen.</summary>
    internal static async Task<object?> SourceAsync(CmmsDbContext db, Guid? workOrderId, Guid? taskId, CancellationToken ct)
    {
        if (workOrderId is not { } wid) return null;
        var wo = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wid).Select(w => new { w.Id, w.Number, Title = Bilingual.Arabic && w.TitleAr != null ? w.TitleAr : w.Title }).FirstOrDefaultAsync(ct);
        if (wo is null) return null;
        var t = taskId is { } tid ? await db.WorkOrderTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid, ct) : null;
        var unit = t?.Unit ?? (t?.MeasurementPointId is { } pid ? await db.MeasurementPoints.IgnoreQueryFilters().Where(p => p.Id == pid).Select(p => p.Unit).FirstOrDefaultAsync(ct) : null);
        var photos = taskId is { } ptid ? await db.Attachments.AsNoTracking().Where(a => a.WorkOrderTaskId == ptid).OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.FileName, a.ContentType }).ToListAsync(ct) : [];
        return new
        {
            WorkOrderId = wo.Id, wo.Number, wo.Title, StepSequence = t?.Sequence, StepDescription = t is null ? null : Bilingual.Pick(t.Description, t.DescriptionAr), t?.Answer, t?.Value, Unit = unit, t?.Level, t?.TextValue,
            t?.Remarks, Photos = photos,
        };
    }

    /// <summary>A Number step's one reading: written on its first answer, changed with it.</summary>
    private static async Task WriteReadingAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, WorkOrderTask t, MeasurementPoint point, DateTimeOffset at, CancellationToken ct)
    {
        var r = await db.MeasurementReadings.FirstOrDefaultAsync(x => x.WorkOrderTaskId == t.Id, ct);
        if (r is null)
        {
            r = new MeasurementReading { PointId = point.Id, AssetId = t.AssetId ?? w.AssetId, SiteId = w.SiteId, WorkOrderId = w.Id, WorkOrderTaskId = t.Id };
            db.MeasurementReadings.Add(r);
        }
        r.Value = t.Value!.Value; r.Level = t.Level ?? MeasureLevel.Normal; r.ReadAt = at; r.ById = ctx.UserId; r.ByName = ctx.UserName;
    }

    /// <summary>
    /// Finds the meter each new Meter step of the work order reads, before it is saved: its asset's (its own on a round, else the work
    /// order's) active meter with the step's unit, or the asset's only active meter; otherwise the answer names it.
    /// </summary>
    public static async Task ResolveMetersAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        var steps = db.ChangeTracker.Entries<WorkOrderTask>().Where(e => e.State == EntityState.Added && e.Entity.WorkOrderId == w.Id
            && e.Entity.Type == StepType.Meter && e.Entity.MeterId is null).Select(e => e.Entity).ToList();
        if (steps.Count == 0) return;
        var assets = steps.Select(t => t.AssetId ?? w.AssetId).OfType<Guid>().Distinct().ToList();
        if (assets.Count == 0) return;
        var meters = await db.Meters.AllSites().AsNoTracking().Where(m => assets.Contains(m.AssetId) && m.IsActive).Select(m => new { m.Id, m.AssetId, m.Unit }).ToListAsync(ct);
        foreach (var t in steps)
        {
            var mine = meters.Where(m => m.AssetId == (t.AssetId ?? w.AssetId)).ToList();
            var pick = t.Unit is { Length: > 0 } unit ? mine.Where(m => string.Equals(m.Unit, unit, StringComparison.OrdinalIgnoreCase)).ToList() : mine;
            if (pick.Count == 1) t.MeterId = pick[0].Id;
        }
    }
}
