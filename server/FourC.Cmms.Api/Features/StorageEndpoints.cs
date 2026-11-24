using System.Reflection;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Settings › Storage (0.25.0): the space files take, by kind and by record, and the largest files. CMX has no storage limit.</summary>
public static class StorageEndpoints
{
    public const int PageSize = 100;

    /// <summary>Images, Videos, Documents, Signatures or Other, from the file's type.</summary>
    public static string KindOf(string contentType, bool signature)
    {
        if (signature) return "Signatures";
        var t = contentType.ToLowerInvariant();
        if (t.StartsWith("image/")) return "Images";
        if (t.StartsWith("video/")) return "Videos";
        if (t.StartsWith("text/") || t == "application/pdf" || t.Contains("word") || t.Contains("excel") || t.Contains("spreadsheet")
            || t.Contains("presentation") || t.Contains("powerpoint") || t.Contains("opendocument") || t == "application/rtf") return "Documents";
        return "Other";
    }

    private static readonly string[] Kinds = ["Images", "Videos", "Documents", "Signatures", "Other"];

    public static void MapStorageEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/storage").WithTags("Settings").RequirePermission(Permissions.SettingsManage);

        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var groups = await db.Attachments.AsNoTracking().GroupBy(a => new { a.EntityType, a.ContentType, a.IsSignature })
                .Select(x => new { x.Key.EntityType, x.Key.ContentType, x.Key.IsSignature, Bytes = x.Sum(a => a.Size), Files = x.Count() }).ToListAsync(ct);
            var byKind = Kinds.Select(k => new { kind = k, rows = groups.Where(x => KindOf(x.ContentType, x.IsSignature) == k).ToList() })
                .Select(k => new { k.kind, bytes = k.rows.Sum(x => x.Bytes), files = k.rows.Sum(x => x.Files) }).ToList();
            var byRecord = groups.GroupBy(x => x.EntityType).Select(x => new { entityType = x.Key, bytes = x.Sum(r => r.Bytes), files = x.Sum(r => r.Files) })
                .OrderByDescending(x => x.bytes).ToList();
            var deleted = 0L;
            foreach (var type in byRecord.Select(r => r.entityType))
                deleted += await DeletedBytesAsync(db, type, ct);
            return Results.Ok(new { totalBytes = groups.Sum(x => x.Bytes), files = groups.Sum(x => x.Files), byKind, byRecord, deletedBytes = deleted });
        });

        g.MapGet("/files", async (string? search, int? page, CmmsDbContext db, CancellationToken ct) =>
        {
            var q = Visible(db, db.Attachments.AsNoTracking());
            if (search.Clean() is { } s)
            {
                // The file's name, its record's number or name, or who uploaded it.
                var records = await ActivityEndpoints.MatchingRecordsAsync(db, s, ct);
                var people = await db.Users.Where(u => u.TenantId == db.CurrentTenantId && u.FullName.Contains(s)).Select(u => u.Id).ToListAsync(ct);
                q = q.Where(a => a.FileName.Contains(s) || records.Contains(a.EntityId) || (a.CreatedById != null && people.Contains(a.CreatedById.Value)));
            }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(a => a.Size).ThenBy(a => a.FileName).Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize)
                .Select(a => new { a.Id, a.FileName, a.ContentType, a.Size, a.EntityType, a.EntityId, a.IsSignature, a.CreatedById, a.CreatedAt }).ToListAsync(ct);
            var labels = await ActivityLabels.ResolveAsync(db, rows.Select(a => (a.EntityType, a.EntityId)), ct);
            var uploaders = rows.Where(a => a.CreatedById != null).Select(a => a.CreatedById!.Value).Distinct().ToList();
            var names = await db.Users.Where(u => uploaders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            return Results.Ok(new
            {
                total,
                items = rows.Select(a => new
                {
                    a.Id, a.FileName, a.ContentType, a.Size, a.EntityType, a.EntityId, kind = KindOf(a.ContentType, a.IsSignature),
                    recordLabel = labels.GetValueOrDefault((a.EntityType, a.EntityId)),
                    uploadedBy = a.CreatedById is { } c ? names.GetValueOrDefault(c) : null, uploadedAt = a.CreatedAt,
                }),
            });
        });
    }

    /// <summary>Records with files that belong to a site: their files are listed only when the person can see the record.</summary>
    private static readonly string[] SiteRecords = ["Site", "Location", "Asset", "WorkRequest", "WorkOrder", "PurchaseOrder", "PurchaseRequest", "Contract"];

    /// <summary>A site-limited person's list leaves out files of records they can't see (0.26.1); the totals stay organization-wide.</summary>
    internal static IQueryable<Core.Files.Attachment> Visible(CmmsDbContext db, IQueryable<Core.Files.Attachment> q) => db.CurrentSiteIds is null ? q : q.Where(a =>
        !SiteRecords.Contains(a.EntityType)
        || (a.EntityType == "Site" && db.Sites.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "Location" && db.Locations.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "Asset" && db.Assets.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "WorkRequest" && db.WorkRequests.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "WorkOrder" && db.WorkOrders.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "PurchaseOrder" && db.PurchaseOrders.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "PurchaseRequest" && db.PurchaseRequests.Any(x => x.Id == a.EntityId))
        || (a.EntityType == "Contract" && db.Contracts.Any(x => x.Id == a.EntityId)));

    /// <summary>Space taken by files of a record type's deleted records (CMX keeps a deleted record and its files).</summary>
    private static Task<long> DeletedBytesAsync(CmmsDbContext db, string entityType, CancellationToken ct)
    {
        var type = db.Model.GetEntityTypes().Select(t => t.ClrType).FirstOrDefault(t => t.Name == entityType && typeof(ISoftDeletable).IsAssignableFrom(t) && typeof(Entity).IsAssignableFrom(t));
        if (type is null) return Task.FromResult(0L);
        return (Task<long>)typeof(StorageEndpoints).GetMethod(nameof(DeletedBytesOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type)
            .Invoke(null, [db, entityType, ct])!;
    }

    private static async Task<long> DeletedBytesOfAsync<T>(CmmsDbContext db, string entityType, CancellationToken ct) where T : Entity, ISoftDeletable
    {
        var tenant = db.CurrentTenantId;
        var deleted = db.Set<T>().IgnoreQueryFilters().Where(x => x.IsDeleted && EF.Property<Guid>(x, "TenantId") == tenant).Select(x => x.Id);
        return await db.Attachments.Where(a => a.EntityType == entityType && deleted.Contains(a.EntityId)).SumAsync(a => (long?)a.Size, ct) ?? 0;
    }
}
