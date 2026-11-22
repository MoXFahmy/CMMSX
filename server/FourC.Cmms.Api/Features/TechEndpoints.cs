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
        Results.Problem(title: Translator.T("Your account isn't linked to an employee. Ask an administrator to link it under People."), statusCode: 404);

    public static void MapTechEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tech").WithTags("Technician app");

        // My open jobs. Answers 304 when the phone already has this version (it asks every 15 minutes).
        g.MapGet("/bundle", async (HttpContext http, CmmsDbContext db, ITenantContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            var me = await EmployeeAsync(db, ctx, ct);
            if (me is null) return NotLinked();
            // Mine: assigned to me, or given to a team I'm in (0.9.0).
            var myTeams = await TeamEndpoints.TeamsOfAsync(db, me.EmployeeId, ct);
            var jobs = await db.WorkOrders.AsNoTracking().Where(w => Open.Contains(w.Status)
                    && (w.Assignees.Any(a => a.EmployeeId == me.EmployeeId) || (w.TeamId != null && myTeams.Contains(w.TeamId.Value))))
                .OrderBy(w => w.DueDate == null).ThenBy(w => w.DueDate).ThenBy(w => w.Number).Take(MaxJobs)
                .Select(w => new
                {
                    w.Id, w.Number, w.Title, w.Description, w.Type, w.Priority, w.Status, w.HoldReason, w.DueDate, w.DueAt, w.PlannedStart, w.SiteId,
                    TeamName = w.Team == null ? null : w.Team.Name,
                    Asset = w.Asset == null ? null : new { w.Asset.Id, w.Asset.AssetNumber, w.Asset.Name, w.Asset.NotesToTechnician,
                        DownSince = db.DowntimeEvents.Where(e => e.AssetId == w.AssetId && e.EndedAt == null).Select(e => (DateTimeOffset?)e.StartedAt).FirstOrDefault() },
                    // The job's own stop: completing it ends the stop and fills the downtime from it.
                    Downtime = db.DowntimeEvents.Where(e => e.Id == w.DowntimeEventId).Select(e => new { e.StartedAt, e.EndedAt }).FirstOrDefault(),
                    w.LocationId, LocationPath = w.Location == null ? null : w.Location.Path, LocationNotes = w.Location == null ? null : w.Location.NotesToTechnician,
                    w.AccessWindows, w.OnlyWhenStopped,
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
            // Anyone's hours satisfy "log labour before completing" (as on the server); the phone lists only mine.
            var withLabor = (await db.WorkOrderLabor.AsNoTracking().Where(l => ids.Contains(l.WorkOrderId)).Select(l => l.WorkOrderId).Distinct().ToListAsync(ct)).ToHashSet();
            var labor = (await db.WorkOrderLabor.AsNoTracking().Where(l => ids.Contains(l.WorkOrderId) && l.EmployeeId == me.EmployeeId).OrderByDescending(l => l.WorkDate)
                .Select(l => new { l.WorkOrderId, l.WorkDate, l.Hours, l.OvertimeHours, l.Notes }).ToListAsync(ct)).ToLookup(l => l.WorkOrderId);
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            // Planned parts with what is already issued (net of returns) per part and store, and each site's default store (0.11.0).
            var planned = (await db.WorkOrderParts.AsNoTracking().Where(p => ids.Contains(p.WorkOrderId)).OrderBy(p => p.CreatedAt)
                .Select(p => new { p.WorkOrderId, p.Id, p.PartId, p.StoreId, p.Quantity }).ToListAsync(ct)).ToLookup(p => p.WorkOrderId);
            var issued = (await db.StockTransactions.AsNoTracking()
                    .Where(t => t.WorkOrderId != null && ids.Contains(t.WorkOrderId.Value) && (t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Issue || t.Type == FourC.Cmms.Core.Inventory.StockTransactionType.Return))
                    .GroupBy(t => new { t.WorkOrderId, t.PartId, t.StoreId }).Select(g => new { g.Key.WorkOrderId, g.Key.PartId, g.Key.StoreId, Net = -g.Sum(t => t.Quantity) }).ToListAsync(ct))
                .ToDictionary(x => (x.WorkOrderId!.Value, x.PartId, x.StoreId), x => x.Net);
            var siteIds = jobs.Select(j => j.SiteId).Distinct().ToList();
            var defaultStores = (await db.Sites.AllSites().AsNoTracking().Where(s => siteIds.Contains(s.Id)).Select(s => new { s.Id, s.Settings }).ToListAsync(ct))
                .ToDictionary(s => s.Id, s => s.Settings.DefaultStoreId);
            var body = new
            {
                me.EmployeeId, ctx.UserId,
                Jobs = jobs.Select(j => new
                {
                    j.Id, j.Number, j.Title, j.Description, j.Type, j.Priority, j.Status, j.HoldReason, j.DueDate, j.DueAt, j.PlannedStart, j.SiteId, j.TeamName, j.Asset, j.Downtime,
                    j.AccessWindows, j.OnlyWhenStopped,
                    Location = j.LocationId is null ? null : new { Id = j.LocationId, Path = PathText(j.LocationPath, names), NotesToTechnician = j.LocationNotes },
                    RoundAssets = round[j.Id].Select(a => new { a.Id, a.AssetNumber, a.Name }),
                    Tasks = tasks[j.Id].Select(t => new { t.Id, t.Sequence, t.Description, t.IsDone, t.Reading, t.Remarks, t.AssetId, t.UpdatedAt, t.UpdatedByName }),
                    PlannedParts = parts[j.Id].Select(p => new { p.PartNumber, p.PartName, p.UnitOfMeasure, p.Quantity, p.StoreCode }),
                    Files = files[j.Id].Select(f => new { f.Id, f.FileName, f.ContentType, f.IsSignature, f.SignedBy }),
                    Comments = comments[j.Id].Take(CommentsPerJob).Select(c => new { c.Body, c.UserName, c.CreatedAt }),
                    MyLabor = labor[j.Id].Select(l => new { l.WorkDate, l.Hours, l.OvertimeHours, l.Notes }),
                    HasLabor = withLabor.Contains(j.Id),
                    PlannedLines = planned[j.Id].Select(p => new { p.Id, p.PartId, p.StoreId, p.Quantity,
                        Issued = p.StoreId is { } st ? issued.GetValueOrDefault((j.Id, p.PartId, st)) : 0m }),
                    DefaultStoreId = defaultStores.GetValueOrDefault(j.SiteId),
                }),
                FailureCodes = await db.FailureCodes.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Code).Select(f => new { f.Id, f.Code, f.Name }).ToListAsync(ct),
                CompletionRules = new
                {
                    Notes = settings.RequireCompletionNotes, Labour = settings.RequireLaborToComplete, AllSteps = settings.RequireAllTasksDone,
                    FailureCodeOnBreakdown = settings.RequireFailureCodeForBreakdowns,
                    HoldNote = (settings.CommentRequiredFor ?? []).Contains(CommentActions.Hold),
                },
            };
            return Versioned(http, JsonSerializer.SerializeToUtf8Bytes(body, json.Value.SerializerOptions));
        }).RequirePermission(Permissions.WorkOrdersView);

        // Parts and stock of my sites' stores, cancel reasons, PM task lists, open counts and (to receive) open purchase orders,
        // for working offline (0.11.0). Answers 304 when the phone already has this version.
        g.MapGet("/stock", async (HttpContext http, CmmsDbContext db, ITenantContext ctx, PermissionService perms, IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            var me = await EmployeeAsync(db, ctx, ct);
            if (me is null) return NotLinked();
            var sites = me.SiteIds;
            var stores = await db.Stores.AsNoTracking().Where(s => s.IsActive && (sites.Count == 0 || sites.Contains(s.SiteId))).OrderBy(s => s.Code)
                .Select(s => new { s.Id, s.Code, s.Name, s.SiteId }).ToListAsync(ct);
            var storeIds = stores.Select(s => s.Id).ToList();
            var levels = (await db.StockLevels.AsNoTracking().Where(l => storeIds.Contains(l.StoreId) && l.Part!.IsActive)
                .Select(l => new { l.PartId, l.StoreId, l.QuantityOnHand, l.Bin }).ToListAsync(ct)).ToLookup(l => l.PartId);
            var reserved = (await db.StockReservations.AsNoTracking().Where(r => storeIds.Contains(r.StoreId)).ToListAsync(ct))
                .GroupBy(r => (r.PartId, r.StoreId)).ToDictionary(g => g.Key, g => g.Sum(r => r.Reserved));
            var partIds = levels.Select(l => l.Key).ToList();
            var photos = (await db.Attachments.AsNoTracking().Where(a => a.EntityType == "Part" && partIds.Contains(a.EntityId) && a.ContentType.StartsWith("image/"))
                    .Select(a => new { a.EntityId, a.Id, a.IsPrimaryImage, a.CreatedAt }).ToListAsync(ct))
                .GroupBy(a => a.EntityId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.IsPrimaryImage).ThenBy(a => a.CreatedAt).First().Id);
            var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).OrderBy(p => p.PartNumber)
                .Select(p => new { p.Id, p.PartNumber, p.Name, p.UnitOfMeasure, p.Barcode, p.IsShelfItem, p.AllowNegativeStock }).ToListAsync(ct);
            var allowed = await perms.GetAsync(ctx.UserId!.Value);
            var body = new
            {
                Stores = stores,
                Parts = parts.Select(p => new
                {
                    p.Id, p.PartNumber, p.Name, p.UnitOfMeasure, p.Barcode, p.IsShelfItem, p.AllowNegativeStock, PhotoId = photos.TryGetValue(p.Id, out var ph) ? ph : (Guid?)null,
                    Levels = levels[p.Id].Select(l => new { l.StoreId, OnHand = l.QuantityOnHand, Reserved = reserved.GetValueOrDefault((p.Id, l.StoreId)), l.Bin }),
                }),
                CancelReasons = await db.CancelReasons.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync(ct),
                PmTasks = await db.PmTasks.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Code)
                    .Select(t => new { t.Id, t.Code, t.Name, Steps = t.Steps.OrderBy(s => s.Sequence).Select(s => s.Description) }).ToListAsync(ct),
                Counts = await db.StockCounts.AsNoTracking().Where(c => c.Status == FourC.Cmms.Core.Inventory.StockCountStatus.Open && storeIds.Contains(c.StoreId))
                    .OrderBy(c => c.CreatedAt).Select(c => new { c.Id, c.Name, c.StoreId }).ToListAsync(ct),
                PurchaseOrders = allowed.Contains(Permissions.PurchasingReceive)
                    ? await db.PurchaseOrders.AsNoTracking()
                        .Where(o => (o.Status == FourC.Cmms.Core.Purchasing.PurchaseOrderStatus.Approved || o.Status == FourC.Cmms.Core.Purchasing.PurchaseOrderStatus.PartiallyReceived)
                            && (sites.Count == 0 || sites.Contains(o.SiteId)))
                        .OrderBy(o => o.Number).Select(o => new
                        {
                            o.Id, o.Number, Supplier = o.Supplier!.Name,
                            Lines = o.Lines.Where(l => l.PartId != null && l.QuantityReceived < l.Quantity).OrderBy(l => l.LineNumber)
                                .Select(l => new { l.Id, l.LineNumber, l.PartId, l.Description, l.UnitOfMeasure, Outstanding = l.Quantity - l.QuantityReceived, l.StoreId }),
                        }).ToListAsync(ct)
                    : null,
            };
            return Versioned(http, JsonSerializer.SerializeToUtf8Bytes(body, json.Value.SerializerOptions));
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
            return Versioned(http, JsonSerializer.SerializeToUtf8Bytes(body, json.Value.SerializerOptions));
        }).RequirePermission(Permissions.AssetsView);
    }

    /// <summary>The JSON with an ETag of its content, or 304 when the phone sent that ETag (its copy is current).</summary>
    private static IResult Versioned(HttpContext http, byte[] bytes)
    {
        var etag = $"\"{Convert.ToBase64String(SHA256.HashData(bytes))[..22].Replace('+', '-').Replace('/', '_')}\"";
        http.Response.Headers.ETag = etag;
        if (http.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(bytes, "application/json");
    }

    private static async Task<Dictionary<Guid, string>> LocationNamesAsync(CmmsDbContext db, List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    /// <summary>"Plant › Hall 2 › Line 3" from a materialized path of location ids.</summary>
    private static string? PathText(string? path, IReadOnlyDictionary<Guid, string> names) =>
        path is null ? null : string.Join(" › ", LocationEndpoints.PathIds(path).Select(id => names.GetValueOrDefault(id)).Where(n => n is not null));
}
