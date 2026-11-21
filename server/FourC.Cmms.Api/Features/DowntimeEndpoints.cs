using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Starting, joining and ending asset stops; shared by requests, work orders and the asset's Downtime tab.</summary>
internal static class Downtime
{
    /// <summary>The asset's open stop, or a new one starting at <paramref name="at"/> (one open stop per asset). Not saved.</summary>
    public static async Task<DowntimeEvent> StartOrJoinAsync(CmmsDbContext db, Guid assetId, DateTimeOffset at, string? reason, DowntimeSource source,
        string? sourceNumber, CancellationToken ct)
    {
        var open = db.DowntimeEvents.Local.FirstOrDefault(e => e.AssetId == assetId && e.EndedAt == null && !e.IsDeleted)
            ?? await db.DowntimeEvents.FirstOrDefaultAsync(e => e.AssetId == assetId && e.EndedAt == null, ct);
        if (open is not null) return open;
        var stop = new DowntimeEvent { AssetId = assetId, StartedAt = at, Reason = Shorten(reason, 500), Source = source, SourceNumber = sourceNumber };
        db.DowntimeEvents.Add(stop);
        return stop;
    }

    /// <summary>Ends the asset's open stop ("Back running"); false when the asset isn't down. Not saved.</summary>
    public static async Task<bool> EndOpenAsync(CmmsDbContext db, ITenantContext ctx, Guid assetId, DateTimeOffset at, CancellationToken ct)
    {
        var open = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.AssetId == assetId && e.EndedAt == null, ct);
        if (open is null) return false;
        open.EndedAt = at < open.StartedAt ? open.StartedAt : at;
        open.EndedById = ctx.UserId;
        return true;
    }

    /// <summary>
    /// Takes the work order off its stop. A stop ticked by mistake (still open, started by this work order, nothing else on it)
    /// is deleted; any other stop stays as it is. Not saved.
    /// </summary>
    public static async Task UntickAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        if (w.DowntimeEventId is not { } id) return;
        w.DowntimeEventId = null;
        var stop = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (stop is null || stop.EndedAt is not null || stop.Source != DowntimeSource.WorkOrder || stop.SourceNumber != w.Number) return;
        var shared = await db.WorkRequests.AnyAsync(r => r.DowntimeEventId == id, ct)
            || await db.WorkOrders.AnyAsync(o => o.DowntimeEventId == id && o.Id != w.Id, ct);
        if (!shared) db.DowntimeEvents.Remove(stop);
    }

    /// <summary>
    /// After a work order's asset changed: the stop this work order started (still open, nothing else on it) goes with it and keeps its
    /// start; if the new asset is already down, the work order joins that stop and its own is removed. Any other stop stays where it is
    /// and the work order lets go of it. Not saved.
    /// </summary>
    public static async Task FollowAssetAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        if (w.DowntimeEventId is not { } id) return;
        var stop = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        var own = stop is { EndedAt: null, Source: DowntimeSource.WorkOrder } && stop.SourceNumber == w.Number
                  && !await db.WorkRequests.AnyAsync(r => r.DowntimeEventId == id, ct)
                  && !await db.WorkOrders.AnyAsync(o => o.DowntimeEventId == id && o.Id != w.Id, ct);
        if (!own || w.AssetId is not { } asset) { w.DowntimeEventId = null; return; }
        var there = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.AssetId == asset && e.EndedAt == null && e.Id != id, ct);
        if (there is null) { stop!.AssetId = asset; return; }
        w.DowntimeEventId = there.Id;
        db.DowntimeEvents.Remove(stop!);
    }

    /// <summary>On completion: ends the work order's open stop at the completion time and fills its downtime from the stop. Not saved.</summary>
    public static async Task OnCompletedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)
    {
        if (w.DowntimeEventId is not { } id) return;
        var stop = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (stop is null) return;
        if (stop.EndedAt is null)
        {
            var end = w.CompletedAt ?? DateTimeOffset.UtcNow;
            stop.EndedAt = end < stop.StartedAt ? stop.StartedAt : end;
            stop.EndedById = ctx.UserId;
        }
        w.AssetWasDown = true;
        w.DowntimeHours = Hours(stop.StartedAt, stop.EndedAt.Value);
    }

    public static decimal Hours(DateTimeOffset start, DateTimeOffset end) => Math.Round((decimal)(end - start).TotalHours, 2);

    public static TimeZoneInfo Zone(string? id) => TimeZoneInfo.TryFindSystemTimeZoneById(id ?? "UTC", out var tz) ? tz : TimeZoneInfo.Utc;

    public static DateOnly LocalDay(DateTimeOffset at, TimeZoneInfo tz) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, tz).DateTime);

    /// <summary>What availability is measured from, for some assets over local days [from, to].</summary>
    public sealed record History(List<StopSpan> Stops, List<(DateOnly Day, decimal Hours)> Typed, List<(DateOnly Day, decimal Hours)> OldBreakdowns);

    /// <summary>
    /// The stops touching the period, plus work completed without a stop but with "Asset was down" ticked (data from before 0.8.0, or typed by
    /// hand): its typed hours count on its completion day. Such work done while the asset had a stop is about that stop, so it adds neither
    /// hours nor a breakdown of its own (no minute counted twice).
    /// </summary>
    public static async Task<Dictionary<Guid, History>> LoadAsync(CmmsDbContext db, IReadOnlyCollection<Guid> assetIds, DateOnly from, DateOnly to, TimeZoneInfo tz,
        CancellationToken ct)
    {
        var start = DowntimeMath.Midnight(from, tz);
        var end = DowntimeMath.Midnight(to.AddDays(1), tz);
        var ids = assetIds.Distinct().ToList();
        var stops = await db.DowntimeEvents.AsNoTracking().Where(e => ids.Contains(e.AssetId) && e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
            .Select(e => new { e.AssetId, e.StartedAt, e.EndedAt }).ToListAsync(ct);
        var done = await db.WorkOrders.AsNoTracking()
            .Where(w => w.AssetId != null && ids.Contains(w.AssetId.Value) && w.AssetWasDown && w.DowntimeEventId == null && w.CompletedAt >= start && w.CompletedAt < end)
            .Select(w => new { AssetId = w.AssetId!.Value, CompletedAt = w.CompletedAt!.Value, Hours = w.DowntimeHours ?? 0, w.CreatedAt, w.StartedAt }).ToListAsync(ct);
        // The work's own time: from when it was raised (or started, if earlier) until it was completed.
        var work = done.Select(w => (w.AssetId, w.CompletedAt, w.Hours, From: w.StartedAt is { } st && st < w.CreatedAt ? st : w.CreatedAt)).ToList();
        var earliest = work.Count == 0 ? start : work.Min(w => w.From);
        var now = DateTimeOffset.UtcNow;
        var around = work.Count == 0 ? [] : await db.DowntimeEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AssetId) && e.StartedAt < end && (e.EndedAt == null || e.EndedAt > earliest))
            .Select(e => new { e.AssetId, e.StartedAt, e.EndedAt }).ToListAsync(ct);
        var typed = work.Where(w => !around.Any(e => e.AssetId == w.AssetId && e.StartedAt < w.CompletedAt && (e.EndedAt ?? now) > w.From)).ToList();
        return ids.ToDictionary(id => id, id => new History(
            stops.Where(s => s.AssetId == id).Select(s => new StopSpan(s.StartedAt, s.EndedAt)).ToList(),
            typed.Where(t => t.AssetId == id && t.Hours > 0).Select(t => (LocalDay(t.CompletedAt, tz), t.Hours)).ToList(),
            typed.Where(t => t.AssetId == id).Select(t => (LocalDay(t.CompletedAt, tz), t.Hours)).ToList()));
    }

    /// <summary>The asset's open stop start for each asset that is down now.</summary>
    public static async Task<Dictionary<Guid, DateTimeOffset>> DownSinceAsync(CmmsDbContext db, IReadOnlyCollection<Guid> assetIds, CancellationToken ct)
    {
        var ids = assetIds.Distinct().ToList();
        return await db.DowntimeEvents.AsNoTracking().Where(e => e.EndedAt == null && ids.Contains(e.AssetId))
            .ToDictionaryAsync(e => e.AssetId, e => e.StartedAt, ct);
    }

    private static string? Shorten(string? s, int max) => s.Clean() is { } t && t.Length > max ? t[..max] : s.Clean();
}

public static class DowntimeEndpoints
{
    public sealed record StopInput(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Reason);

    public static void MapDowntimeEndpoints(this RouteGroupBuilder api)
    {
        // The asset's Downtime tab: down since, running time, availability over the last 30 days and the stops, newest first.
        api.MapGet("/assets/{id:guid}/downtime", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var asset = await db.Assets.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.RunsHoursPerDay, a.RunsOn }).FirstOrDefaultAsync(ct);
            if (asset is null) return Results.NotFound();
            var tz = Downtime.Zone((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone);
            var now = DateTimeOffset.UtcNow;
            var today = Downtime.LocalDay(now, tz);
            var from = today.AddDays(-29);
            var h = (await Downtime.LoadAsync(db, [id], from, today, tz, ct))[id];
            var m = DowntimeMath.Measure(h.Stops, h.Typed, from, today, now, asset.RunsHoursPerDay, asset.RunsOn, tz);

            var stops = await db.DowntimeEvents.AsNoTracking().Where(e => e.AssetId == id).OrderByDescending(e => e.StartedAt).Take(500)
                .Select(e => new { e.Id, e.StartedAt, e.EndedAt, e.Reason, e.Source, e.SourceNumber }).ToListAsync(ct);
            var stopIds = stops.Select(s => (Guid?)s.Id).ToList();
            var requests = await db.WorkRequests.AsNoTracking().Where(r => stopIds.Contains(r.DowntimeEventId))
                .Select(r => new { StopId = r.DowntimeEventId!.Value, Kind = "request", r.Id, r.Number }).ToListAsync(ct);
            var orders = await db.WorkOrders.AsNoTracking().Where(w => stopIds.Contains(w.DowntimeEventId))
                .Select(w => new { StopId = w.DowntimeEventId!.Value, Kind = "workOrder", w.Id, w.Number }).ToListAsync(ct);
            var links = requests.Concat(orders).ToLookup(l => l.StopId);
            return Results.Ok(new
            {
                DownSince = stops.FirstOrDefault(s => s.EndedAt == null)?.StartedAt,
                asset.RunsHoursPerDay, asset.RunsOn,
                Availability30 = m.PlannedHours > 0 ? Math.Round((m.PlannedHours - m.DownHours) / m.PlannedHours * 100, 1) : (decimal?)null,
                PlannedHours30 = m.PlannedHours, DownHours30 = m.DownHours,
                Stops = stops.Select(s => new
                {
                    s.Id, s.StartedAt, s.EndedAt, Hours = Downtime.Hours(s.StartedAt, s.EndedAt ?? now), s.Reason, s.Source, s.SourceNumber,
                    Links = links[s.Id].Select(l => new { l.Kind, l.Id, l.Number }),
                }),
            });
        }).RequirePermission(Permissions.AssetsView).WithTags("Downtime");

        // A stop logged by hand (one that never became a request or work order, or one entered afterwards).
        api.MapPost("/assets/{id:guid}/downtime", async (Guid id, StopInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            var v = await CheckAsync(db, id, null, input, ct);
            if (!v.IsValid) return v.Problem();
            var stop = new DowntimeEvent { AssetId = id, StartedAt = input.StartedAt, EndedAt = input.EndedAt, Reason = input.Reason.Clean(), Source = DowntimeSource.Manual };
            db.DowntimeEvents.Add(stop);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/downtime/{stop.Id}", new { stop.Id });
        }).RequirePermission(Permissions.AssetsEdit).WithTags("Downtime");

        // A stop that wasn't one (logged on the wrong asset, a request ticked down by mistake): removed, and the requests and work orders on it
        // let go of it. Completed work on it no longer says the asset was down.
        api.MapDelete("/downtime/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var stop = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (stop is null) return Results.NotFound();
            foreach (var r in await db.WorkRequests.Where(r => r.DowntimeEventId == id).ToListAsync(ct)) r.DowntimeEventId = null;
            foreach (var w in await db.WorkOrders.Where(w => w.DowntimeEventId == id).ToListAsync(ct))
            {
                w.DowntimeEventId = null;
                if (w.CompletedAt is not null) { w.AssetWasDown = false; w.DowntimeHours = null; }
            }
            db.DowntimeEvents.Remove(stop);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AssetsEdit).WithTags("Downtime");

        // Correcting any stop's start, end or reason; the change history keeps each edit.
        api.MapPut("/downtime/{id:guid}", async (Guid id, StopInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var stop = await db.DowntimeEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (stop is null) return Results.NotFound();
            var v = await CheckAsync(db, stop.AssetId, id, input, ct);
            if (!v.IsValid) return v.Problem();
            if (stop.EndedAt is null && input.EndedAt is not null) stop.EndedById = ctx.UserId;
            stop.StartedAt = input.StartedAt; stop.EndedAt = input.EndedAt; stop.Reason = input.Reason.Clean();
            // Completed work on this stop shows its new length.
            if (stop.EndedAt is { } end)
                foreach (var w in await db.WorkOrders.Where(w => w.DowntimeEventId == id && w.CompletedAt != null).ToListAsync(ct))
                    w.DowntimeHours = Downtime.Hours(stop.StartedAt, end);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { stop.Id });
        }).RequirePermission(Permissions.AssetsEdit).WithTags("Downtime");

        api.MapPost("/assets/{id:guid}/back-running", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            if (!await Downtime.EndOpenAsync(db, ctx, id, DateTimeOffset.UtcNow, ct)) throw new DomainException("The asset isn't down.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersExecute).WithTags("Downtime");
    }

    private static async Task<Validator> CheckAsync(CmmsDbContext db, Guid assetId, Guid? stopId, StopInput i, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (i.StartedAt == default) { var missing = new Validator(); missing.Add("startedAt", "Choose when the asset stopped."); return missing; }
        var v = new Validator().MaxLength("reason", i.Reason, "Reason", 500)
            .When(i.StartedAt > now.AddMinutes(1), "startedAt", "The start can't be in the future.")
            .When(i.EndedAt is { } e && e <= i.StartedAt, "endedAt", "The end must be after the start.")
            .When(i.EndedAt > now.AddMinutes(1), "endedAt", "The end can't be in the future.");
        if (!v.IsValid) return v;
        var others = db.DowntimeEvents.Where(e => e.AssetId == assetId && e.Id != stopId);
        if (i.EndedAt is null && await others.AnyAsync(e => e.EndedAt == null, ct)) v.Add("endedAt", "The asset is already down.");
        else if (i.EndedAt is { } end
                     ? await others.AnyAsync(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > i.StartedAt), ct)
                     : await others.AnyAsync(e => e.EndedAt == null || e.EndedAt > i.StartedAt, ct))
            v.Add("startedAt", "This stop overlaps another stop of this asset.");
        return v;
    }
}
