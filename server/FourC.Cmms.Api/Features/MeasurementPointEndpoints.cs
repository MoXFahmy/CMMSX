using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Pm;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Measurement points (0.13.0): what is measured, its valid range and levels, shared by work templates' Number steps; readings per
/// point and asset, entered on checklist steps or directly.
/// </summary>
public static class MeasurementPointEndpoints
{
    public sealed record PointInput(string Code, string Name, MeasurementKind Kind = MeasurementKind.Other, string? Unit = null, int Decimals = 1,
        decimal? ValidMin = null, decimal? ValidMax = null, decimal? AlarmLow = null, decimal? AlertLow = null, decimal? Target = null, decimal? AlertHigh = null,
        decimal? AlarmHigh = null, Guid? AlarmLowTemplateId = null, Guid? AlertLowTemplateId = null, Guid? AlertHighTemplateId = null, Guid? AlarmHighTemplateId = null,
        List<Guid>? AssetIds = null, bool IsActive = true, string? RowVersion = null);
    public sealed record DirectReadingInput(Guid? AssetId, decimal Value, DateTimeOffset? ReadAt = null, string? Note = null);

    public const string InUse = "In use by {0} work template(s).";
    public const string CodeTaken = "Another point uses this code.";
    public const string Unknown = "Unknown measurement point.";
    public const string Inactive = "This point isn't active.";

    public static void MapMeasurementPointEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/measurement-points").WithTags("Measurement points");

        g.MapGet("/", async (Guid? assetId, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = db.MeasurementPoints.AsNoTracking();
            if (assetId is { } a) q = q.Where(p => db.MeasurementPointAssets.Any(x => x.PointId == p.Id && x.AssetId == a) || db.MeasurementReadings.Any(r => r.PointId == p.Id && r.AssetId == a));
            return await q.OrderBy(p => p.Code).Select(p => new
            {
                p.Id, p.Code, p.Name, p.Kind, p.Unit, p.Decimals, p.ValidMin, p.ValidMax, p.AlarmLow, p.AlertLow, p.Target, p.AlertHigh, p.AlarmHigh, p.IsActive,
                AssetCount = db.MeasurementPointAssets.Count(x => x.PointId == p.Id), ReadingCount = db.MeasurementReadings.Count(r => r.PointId == p.Id),
            }).ToListAsync(ct);
        }).RequirePermission(Permissions.PmView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.MeasurementPoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var assets = await (from x in db.MeasurementPointAssets where x.PointId == id join a in db.Assets on x.AssetId equals a.Id orderby a.AssetNumber
                                select new { a.Id, a.AssetNumber, Name = Bilingual.Arabic && a.NameAr != null ? a.NameAr : a.Name, a.SiteId }).ToListAsync(ct);
            var ar = Bilingual.Arabic; // template names in the reader's language (0.29.2)
            var usedBy = await db.PmTasks.AsNoTracking().Where(t => t.Steps.Any(s => s.MeasurementPointId == id)).OrderBy(t => t.Code)
                .Select(t => new { t.Id, t.Code, Name = ar && t.NameAr != null ? t.NameAr : t.Name }).ToListAsync(ct);
            return Results.Ok(new
            {
                p.Id, p.Code, p.Name, p.Kind, p.Unit, p.Decimals, p.ValidMin, p.ValidMax, p.AlarmLow, p.AlertLow, p.Target, p.AlertHigh, p.AlarmHigh,
                p.AlarmLowTemplateId, p.AlertLowTemplateId, p.AlertHighTemplateId, p.AlarmHighTemplateId, p.IsActive, Assets = assets, UsedBy = usedBy,
                RowVersion = Convert.ToBase64String(p.RowVersion),
            });
        }).RequirePermission(Permissions.PmView);

        g.MapPost("/", async (PointInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = new MeasurementPoint { Code = input.Code?.Trim() ?? "", Name = input.Name?.Trim() ?? "" };
            Apply(p, input);
            var v = await ValidateAsync(p, input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            foreach (var a in (input.AssetIds ?? []).Distinct()) p.Assets.Add(new MeasurementPointAsset { PointId = p.Id, AssetId = a });
            db.MeasurementPoints.Add(p);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/measurement-points/{p.Id}", new { p.Id });
        }).RequirePermission(Permissions.PmManage);

        g.MapPut("/{id:guid}", async (Guid id, PointInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.MeasurementPoints.Include(x => x.Assets).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            if (input.RowVersion is not null) db.Entry(p).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            p.Code = input.Code?.Trim() ?? ""; p.Name = input.Name?.Trim() ?? "";
            Apply(p, input);
            var v = await ValidateAsync(p, input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.AssetIds is not null)
            {
                // Assets the editor can't see (another site or branch) stay on the point.
                var links = await db.MeasurementPointAssets.IgnoreQueryFilters().Where(x => x.PointId == id).ToListAsync(ct);
                var linked = links.Select(x => x.AssetId).ToList();
                var visible = (await db.Assets.Where(a => linked.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
                var wanted = input.AssetIds.Distinct().ToHashSet();
                foreach (var x in links.Where(x => visible.Contains(x.AssetId) && !wanted.Contains(x.AssetId))) db.MeasurementPointAssets.Remove(x);
                foreach (var a in wanted.Where(a => !linked.Contains(a))) db.MeasurementPointAssets.Add(new MeasurementPointAsset { PointId = id, AssetId = a });
                db.Entry(p).Property(x => x.Name).IsModified = true; // a list-only change still checks the row version
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { p.Id });
        }).RequirePermission(Permissions.PmManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.MeasurementPoints.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var used = await db.PmTasks.CountAsync(t => t.Steps.Any(s => s.MeasurementPointId == id), ct);
            if (used > 0) throw new DomainException(string.Format(InUse, used));
            db.MeasurementPoints.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PmManage);

        // An asset's measurements (0.13.0): the points listing it or read on it, each with its readings in the period (oldest first) for the chart.
        api.MapGet("/assets/{assetId:guid}/measurements", async (Guid assetId, DateOnly? from, DateOnly? to, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == assetId, ct)) return Results.NotFound();
            var start = new DateTimeOffset((from ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset((to ?? DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var points = await db.MeasurementPoints.AsNoTracking()
                .Where(p => db.MeasurementPointAssets.Any(x => x.PointId == p.Id && x.AssetId == assetId) || db.MeasurementReadings.Any(r => r.PointId == p.Id && r.AssetId == assetId))
                .OrderBy(p => p.Code).ToListAsync(ct);
            // The newest 200 of each point, so a point read every hour doesn't push a monthly one off its chart.
            var readings = new Dictionary<Guid, object>();
            foreach (var point in points)
                readings[point.Id] = (await db.MeasurementReadings.AsNoTracking().Where(r => r.PointId == point.Id && r.AssetId == assetId && r.ReadAt >= start && r.ReadAt < end)
                    .OrderByDescending(r => r.ReadAt).Take(200)
                    .Select(r => new { r.Id, r.ReadAt, r.Value, r.Level, r.WorkOrderId, r.FollowUpWorkOrderId, r.FollowUpRequestId, r.ByName, r.Note }).ToListAsync(ct))
                    .OrderBy(r => r.ReadAt).ToList();
            return Results.Ok(points.Select(p => new
            {
                p.Id, p.Code, p.Name, p.Kind, p.Unit, p.Decimals, p.ValidMin, p.ValidMax, p.AlarmLow, p.AlertLow, p.Target, p.AlertHigh, p.AlarmHigh, p.IsActive,
                Readings = readings[p.Id],
            }));
        }).WithTags("Measurement points").RequirePermission(Permissions.AssetsView);

        g.MapGet("/{id:guid}/readings", async (Guid id, Guid? assetId, DateOnly? from, DateOnly? to, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.MeasurementPoints.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
            var q = db.MeasurementReadings.AsNoTracking().Where(r => r.PointId == id);
            if (assetId is { } a) q = q.Where(r => r.AssetId == a);
            if (from is { } f) q = q.Where(r => r.ReadAt >= new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            if (to is { } t) q = q.Where(r => r.ReadAt < new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            return Results.Ok(await q.OrderByDescending(r => r.ReadAt).Take(1000).Select(r => new
            {
                r.Id, r.Value, r.ReadAt, r.Level, r.AssetId, AssetNumber = db.Assets.Where(x => x.Id == r.AssetId).Select(x => x.AssetNumber).FirstOrDefault(),
                r.WorkOrderId, WorkOrderNumber = db.WorkOrders.Where(w => w.Id == r.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
                r.FollowUpWorkOrderId, FollowUpNumber = db.WorkOrders.Where(w => w.Id == r.FollowUpWorkOrderId).Select(w => w.Number).FirstOrDefault()
                    ?? db.WorkRequests.Where(w => w.Id == r.FollowUpRequestId).Select(w => w.Number).FirstOrDefault(),
                r.FollowUpRequestId, r.Note, r.ByName,
            }).ToListAsync(ct));
        }).RequirePermission(Permissions.PmView);

        g.MapPost("/{id:guid}/readings", async (Guid id, DirectReadingInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var p = await db.MeasurementPoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            if (input.AssetId is null) throw new DomainException("Choose the asset.");
            var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.AssetId, ct);
            if (asset is null) return Results.NotFound();
            if (!p.IsActive) throw new DomainException(Inactive);
            var at = input.ReadAt ?? DateTimeOffset.UtcNow;
            if (at > DateTimeOffset.UtcNow.AddMinutes(5)) throw new DomainException("Reading time cannot be in the future.");
            if (MeasureLevels.OutOfRange(p, input.Value)) throw new DomainException(MeasureLevels.RangeMessage(p));
            var value = Math.Round(input.Value, Math.Clamp(p.Decimals, 0, 4));
            var reading = new MeasurementReading
            {
                PointId = id, AssetId = asset.Id, SiteId = asset.SiteId, Value = value, ReadAt = at, Level = MeasureLevels.LevelOf(p, value),
                Note = input.Note.Clean() is { } n ? n.Length > 1000 ? n[..1000] : n : null, ById = ctx.UserId, ByName = ctx.UserName,
            };
            db.MeasurementReadings.Add(reading);
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            // The reading and its follow-up are saved together or not at all.
            await db.InTransactionAsync(async tk =>
            {
                if (reading.Level != MeasureLevel.Normal)
                {
                    var (woId, requestId, _) = await Checklists.RaiseFollowUpAsync(db, ctx, mail, settings, null, null, p, reading.Level, value, asset.Id, asset.SiteId, reading.Note, tk);
                    reading.FollowUpWorkOrderId = woId; reading.FollowUpRequestId = requestId;
                }
                return await db.SaveChangesAsync(tk);
            }, ct);
            return Results.Ok(new { reading.Id, reading.Level, reading.FollowUpWorkOrderId, reading.FollowUpRequestId });
        }).RequirePermission(Permissions.MetersRecord);
    }

    internal static void Apply(MeasurementPoint p, PointInput i)
    {
        p.Kind = i.Kind; p.Unit = i.Unit.Clean(); p.Decimals = i.Decimals; p.IsActive = i.IsActive;
        p.ValidMin = i.ValidMin; p.ValidMax = i.ValidMax; p.AlarmLow = i.AlarmLow; p.AlertLow = i.AlertLow; p.Target = i.Target; p.AlertHigh = i.AlertHigh; p.AlarmHigh = i.AlarmHigh;
        p.AlarmLowTemplateId = i.AlarmLowTemplateId; p.AlertLowTemplateId = i.AlertLowTemplateId; p.AlertHighTemplateId = i.AlertHighTemplateId; p.AlarmHighTemplateId = i.AlarmHighTemplateId;
    }

    /// <summary>The checks that need no database (shared with the import); <paramref name="p"/> already has the input applied.</summary>
    internal static Validator PointRules(MeasurementPoint p, PointInput i)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("unit", i.Unit, "Unit", 20)
            .When(!Enum.IsDefined(i.Kind), "kind", "Choose what is measured.")
            .When(i.Decimals is < 0 or > 4, "decimals", "Decimals can be 0 to 4.");
        foreach (var (field, message) in MeasureLevels.Problems(p)) v.Add(field, message);
        return v;
    }

    private static async Task<Validator> ValidateAsync(MeasurementPoint p, PointInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = PointRules(p, i);
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.MeasurementPoints.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", CodeTaken);
        var templates = new[] { ("alarmLowTemplateId", i.AlarmLowTemplateId), ("alertLowTemplateId", i.AlertLowTemplateId), ("alertHighTemplateId", i.AlertHighTemplateId),
            ("alarmHighTemplateId", i.AlarmHighTemplateId) };
        foreach (var (field, tid) in templates)
            if (tid is { } t && !await db.PmTasks.AnyAsync(x => x.Id == t, ct)) v.Add(field, "Unknown work template.");
        var assets = (i.AssetIds ?? []).Distinct().ToList();
        if (assets.Count > 0)
        {
            // New assets must be ones the editor sees; ones already on the point may be hidden from them.
            var kept = id is { } pid ? await db.MeasurementPointAssets.IgnoreQueryFilters().Where(x => x.PointId == pid).Select(x => x.AssetId).ToListAsync(ct) : [];
            var check = assets.Where(a => !kept.Contains(a)).ToList();
            if (check.Count > 0 && await db.Assets.CountAsync(a => check.Contains(a.Id), ct) != check.Count) v.Add("assetIds", "Unknown asset.");
        }
        return v;
    }
}
