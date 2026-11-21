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

    private static string? Shorten(string? s, int max) => s.Clean() is { } t && t.Length > max ? t[..max] : s.Clean();
}

public static class DowntimeEndpoints
{
    public static void MapDowntimeEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/assets/{id:guid}/back-running", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            if (!await Downtime.EndOpenAsync(db, ctx, id, DateTimeOffset.UtcNow, ct)) throw new DomainException("The asset isn't down.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersExecute).WithTags("Downtime");
    }
}
