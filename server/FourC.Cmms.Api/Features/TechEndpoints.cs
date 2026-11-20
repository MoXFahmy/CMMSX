using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// The technician view's server side: what a phone keeps (my jobs; the assets, locations and meters of my sites) and,
/// in <c>TechActions</c>, the queued changes it sends back.
/// </summary>
public static partial class TechEndpoints
{
    private static readonly WorkOrderStatus[] Open = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold];
    private const int MaxJobs = 300;
    private const int CommentsPerJob = 20;

    private sealed record Me(Guid EmployeeId, List<Guid> SiteIds);

    private static async Task<Me?> EmployeeAsync(CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        await db.Employees.AsNoTracking().Where(e => e.UserId == ctx.UserId && e.IsActive).Select(e => new Me(e.Id, e.SiteIds)).FirstOrDefaultAsync(ct);

    private static IResult NotLinked() =>
        Results.Problem(title: "Your account isn't linked to an employee. Ask an administrator to link it under People.", statusCode: 404);

    public static void MapTechEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tech").WithTags("Technician app");

        g.MapGet("/bundle", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = await EmployeeAsync(db, ctx, ct);
            if (me is null) return NotLinked();
            var jobs = await db.WorkOrders.AsNoTracking().Where(w => Open.Contains(w.Status) && w.Assignees.Any(a => a.EmployeeId == me.EmployeeId))
                .OrderBy(w => w.DueDate == null).ThenBy(w => w.DueDate).ThenBy(w => w.Number).Take(MaxJobs)
                .Select(w => new
                {
                    w.Id, w.Number, w.Title, w.Description, w.Type, w.Priority, w.Status, w.HoldReason, w.DueDate, w.PlannedStart, w.SiteId,
                    Asset = w.Asset == null ? null : new { w.Asset.Id, w.Asset.AssetNumber, w.Asset.Name, w.Asset.NotesToTechnician },
                    w.LocationId, LocationPath = w.Location == null ? null : w.Location.Path, LocationNotes = w.Location == null ? null : w.Location.NotesToTechnician,
                }).ToListAsync(ct);
            var ids = jobs.Select(j => j.Id).ToList();
            var names = await LocationNamesAsync(db, jobs.Where(j => j.LocationPath != null).SelectMany(j => LocationEndpoints.PathIds(j.LocationPath!)).Distinct().ToList(), ct);
            var tasks = (await db.WorkOrderTasks.AsNoTracking().Where(t => ids.Contains(t.WorkOrderId)).OrderBy(t => t.Sequence)
                .Select(t => new { t.WorkOrderId, t.Id, t.Sequence, t.Description, t.IsDone, t.Reading, t.Remarks, t.AssetId, t.UpdatedAt, t.UpdatedByName }).ToListAsync(ct))
                .ToLookup(t => t.WorkOrderId);
            var round = (await db.WorkOrderAssets.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId) && a.Asset != null).OrderBy(a => a.Sequence)
                .Select(a => new { a.WorkOrderId, a.Asset!.Id, a.Asset.AssetNumber, a.Asset.Name }).ToListAsync(ct)).ToLookup(a => a.WorkOrderId);
            var parts = (await db.WorkOrderParts.AsNoTracking().Where(p => ids.Contains(p.WorkOrderId)).OrderBy(p => p.Part!.PartNumber)
                .Select(p => new { p.WorkOrderId, p.Part!.PartNumber, PartName = p.Part.Name, p.Part.UnitOfMeasure, p.Quantity, StoreCode = p.Store != null ? p.Store.Code : null })
                .ToListAsync(ct)).ToLookup(p => p.WorkOrderId);
            var files = (await db.Attachments.AsNoTracking().Where(a => a.EntityType == "WorkOrder" && ids.Contains(a.EntityId)).OrderBy(a => a.CreatedAt)
                .Select(a => new { a.EntityId, a.Id, a.FileName, a.ContentType, a.IsSignature, a.SignedBy }).ToListAsync(ct)).ToLookup(a => a.EntityId);
            var comments = (await db.WorkOrderComments.AsNoTracking().Where(c => ids.Contains(c.WorkOrderId)).OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.WorkOrderId, c.Body, c.UserName, c.CreatedAt }).ToListAsync(ct)).ToLookup(c => c.WorkOrderId);
            var labor = (await db.WorkOrderLabor.AsNoTracking().Where(l => ids.Contains(l.WorkOrderId) && l.EmployeeId == me.EmployeeId).OrderByDescending(l => l.WorkDate)
                .Select(l => new { l.WorkOrderId, l.WorkDate, l.Hours, l.OvertimeHours, l.Notes }).ToListAsync(ct)).ToLookup(l => l.WorkOrderId);
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            return Results.Ok(new
            {
                me.EmployeeId, ctx.UserId, GeneratedAt = DateTimeOffset.UtcNow,
                Jobs = jobs.Select(j => new
                {
                    j.Id, j.Number, j.Title, j.Description, j.Type, j.Priority, j.Status, j.HoldReason, j.DueDate, j.PlannedStart, j.SiteId, j.Asset,
                    Location = j.LocationId is null ? null : new { Id = j.LocationId, Path = PathText(j.LocationPath, names), NotesToTechnician = j.LocationNotes },
                    RoundAssets = round[j.Id].Select(a => new { a.Id, a.AssetNumber, a.Name }),
                    Tasks = tasks[j.Id].Select(t => new { t.Id, t.Sequence, t.Description, t.IsDone, t.Reading, t.Remarks, t.AssetId, t.UpdatedAt, t.UpdatedByName }),
                    PlannedParts = parts[j.Id].Select(p => new { p.PartNumber, p.PartName, p.UnitOfMeasure, p.Quantity, p.StoreCode }),
                    Files = files[j.Id].Select(f => new { f.Id, f.FileName, f.ContentType, f.IsSignature, f.SignedBy }),
                    Comments = comments[j.Id].Take(CommentsPerJob).Select(c => new { c.Body, c.UserName, c.CreatedAt }),
                    MyLabor = labor[j.Id].Select(l => new { l.WorkDate, l.Hours, l.OvertimeHours, l.Notes }),
                }),
                FailureCodes = await db.FailureCodes.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Code).Select(f => new { f.Id, f.Code, f.Name }).ToListAsync(ct),
                CompletionRules = new
                {
                    Notes = settings.RequireCompletionNotes, Labour = settings.RequireLaborToComplete, AllSteps = settings.RequireAllTasksDone,
                    FailureCodeOnBreakdown = settings.RequireFailureCodeForBreakdowns,
                },
            });
        }).RequirePermission(Permissions.WorkOrdersView);

        // The places of my sites, for scanning offline. Answers 304 when the phone already has this version.
        g.MapGet("/places", async (HttpContext http, CmmsDbContext db, ITenantContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            var me = await EmployeeAsync(db, ctx, ct);
            if (me is null) return NotLinked();
            var sites = me.SiteIds;
            var assets = await db.Assets.AsNoTracking().Where(a => a.Status != AssetStatus.Disposed && (sites.Count == 0 || sites.Contains(a.SiteId)))
                .OrderBy(a => a.AssetNumber).Select(a => new { a.Id, a.AssetNumber, a.Name, a.Barcode, a.SiteId, a.LocationId, a.NotesToTechnician }).ToListAsync(ct);
            var locs = await db.Locations.AsNoTracking().Where(l => sites.Count == 0 || sites.Contains(l.SiteId))
                .Select(l => new { l.Id, l.Name, l.Path, l.SiteId, l.NotesToTechnician }).ToListAsync(ct);
            var names = locs.ToDictionary(l => l.Id, l => l.Name);
            var meters = await db.Meters.AsNoTracking().Where(m => m.IsActive && m.Asset!.Status != AssetStatus.Disposed && (sites.Count == 0 || sites.Contains(m.Asset.SiteId)))
                .Select(m => new { m.Id, m.AssetId, m.Name, m.Unit, m.CurrentReading, m.LastReadAt }).ToListAsync(ct);
            var body = new
            {
                Assets = assets,
                Locations = locs.OrderBy(l => l.Path).Select(l => new { l.Id, l.Name, Path = PathText(l.Path, names), l.SiteId, l.NotesToTechnician }),
                Meters = meters,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, json.Value.SerializerOptions);
            var etag = $"\"{Convert.ToBase64String(SHA256.HashData(bytes))[..22].Replace('+', '-').Replace('/', '_')}\"";
            http.Response.Headers.ETag = etag;
            if (http.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Bytes(bytes, "application/json");
        }).RequirePermission(Permissions.AssetsView);
    }

    private static async Task<Dictionary<Guid, string>> LocationNamesAsync(CmmsDbContext db, List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    /// <summary>"Plant › Hall 2 › Line 3" from a materialized path of location ids.</summary>
    private static string? PathText(string? path, IReadOnlyDictionary<Guid, string> names) =>
        path is null ? null : string.Join(" › ", LocationEndpoints.PathIds(path).Select(id => names.GetValueOrDefault(id)).Where(n => n is not null));
}
