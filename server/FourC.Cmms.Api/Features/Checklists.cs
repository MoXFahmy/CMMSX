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
        var changed = false;
        switch (t.Type)
        {
            case StepType.Check:
            {
                var answer = c.Answer ?? (c.IsDone is true ? t.Answer ?? StepAnswer.Ok : c.IsDone is false ? null : t.Answer);
                if (answer != t.Answer) { t.Answer = answer; changed = true; }
                break;
            }
            case StepType.Number:
                if (c.Value is { } raw)
                {
                    var point = t.MeasurementPointId is { } pid
                        ? await db.MeasurementPoints.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == db.CurrentTenantId, ct) : null;
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
        var done = StepRules.Answered(t);
        if (done != t.IsDone)
        {
            t.IsDone = done; t.DoneAt = done ? at : null; t.DoneBy = done ? ctx.UserName : null; changed = true;
            if (done) await WorkOrderEndpoints.StepDoneAsync(db, ctx, w.Id, at, ct);
        }
        if (changed) { t.UpdatedAt = at; t.UpdatedById = ctx.UserId; t.UpdatedByName = ctx.UserName; }
        return v;
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
