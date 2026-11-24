using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Settings › Activity log (0.25.0): every change entry of the organization, newest first, with filters, export and "last
/// changed" for settings. The same entries the records' History tabs show.
/// </summary>
public static class ActivityEndpoints
{
    public sealed class ActivityFilter
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? UserId { get; set; }
        public string? Source { get; set; }
        public string? EntityType { get; set; }
        public string? Action { get; set; }
        public Guid? ChangeId { get; set; }
        public string? Search { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    /// <summary>One entry. <c>Changes</c> is the JSON of changed fields ({"Field": {"from": …, "to": …}}), masked values as "••••".</summary>
    public sealed record ActivityRow(Guid Id, DateTimeOffset Timestamp, Guid? UserId, string? UserName, string Source, Guid ChangeId, string EntityType, Guid EntityId,
        string? ParentType, Guid? ParentId, string RecordLabel, string Action, string? Changes);

    public static void MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/activity").WithTags("Activity").RequirePermission(Permissions.SettingsManage);
        g.MapGet("/", ([AsParameters] ActivityFilter f, CmmsDbContext db, CancellationToken ct) => ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 50, ct));
        g.MapGet("/export", ([AsParameters] ActivityFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "activity", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct));

        // Who last changed each settings key ("settings.timeZone", "security.passwordMinLength"…), for the settings pages.
        g.MapGet("/last-changed", async (string entityType, [Microsoft.AspNetCore.Mvc.FromQuery] string[] keys, CmmsDbContext db, CancellationToken ct) =>
        {
            var rows = new List<object>();
            foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(50))
            {
                var quoted = $"\"{key}\"";
                var last = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entityType && a.Changes != null && a.Changes.Contains(quoted))
                    .OrderByDescending(a => a.Timestamp).Select(a => new { a.UserName, a.Timestamp }).FirstOrDefaultAsync(ct);
                if (last is not null) rows.Add(new { Key = key, last.UserName, last.Timestamp });
            }
            return rows;
        });
    }

    /// <summary>Record types whose entries a site-limited person sees only when they can see the record (its own site filter decides).</summary>
    private static readonly string[] SiteRecords =
        ["Site", "Location", "Asset", "WorkRequest", "WorkOrder", "PmSchedule", "Store", "PurchaseOrder", "PurchaseRequest", "Contract", "Meter", "DowntimeEvent"];

    internal static async Task<PagedResult<ActivityRow>> ListAsync(CmmsDbContext db, ActivityFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 200)
    {
        var q = db.AuditEntries.AsNoTracking();
        if (f.From is { } from) { var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(a => a.Timestamp >= start); }
        if (f.To is { } to) { var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(a => a.Timestamp < end); }
        if (f.UserId is { } user) q = q.Where(a => a.UserId == user);
        if (f.Source.Clean() is { } source) q = q.Where(a => a.Source == source);
        if (f.EntityType.Clean() is { } type) q = q.Where(a => a.EntityType == type || a.ParentType == type);
        if (f.Action.Clean() is { } action) q = q.Where(a => a.Action == action);
        if (f.ChangeId is { } change) q = q.Where(a => a.ChangeId == change);
        if (f.Search.Clean() is { } s)
        {
            // A record's number or name finds its entries; so do changed fields and values, the record type and the person.
            var ids = await MatchingRecordsAsync(db, s, ct);
            q = q.Where(a => a.EntityType.Contains(s) || (a.UserName != null && a.UserName.Contains(s)) || (a.Changes != null && a.Changes.Contains(s))
                             || ids.Contains(a.EntityId) || (a.ParentId != null && ids.Contains(a.ParentId.Value)));
        }
        if (db.CurrentSiteIds is not null)
        {
            // Entries of a site's records only when the person can see the record; organization-wide entries (settings, users, parts…) stay.
            q = q.Where(a =>
                (a.ParentType ?? a.EntityType) == "Site" ? db.Sites.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "Location" ? db.Locations.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "Asset" ? db.Assets.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "WorkRequest" ? db.WorkRequests.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "WorkOrder" ? db.WorkOrders.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "PmSchedule" ? db.PmSchedules.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "Store" ? db.Stores.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "PurchaseOrder" ? db.PurchaseOrders.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "PurchaseRequest" ? db.PurchaseRequests.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "Contract" ? db.Contracts.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "Meter" ? db.Meters.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : (a.ParentType ?? a.EntityType) == "DowntimeEvent" ? db.DowntimeEvents.Any(x => x.Id == (a.ParentId ?? a.EntityId))
                : !SiteRecords.Contains(a.ParentType ?? a.EntityType));
        }
        var paged = await q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id).ToPagedAsync(page, pageSize, ct, maxPageSize);
        var labels = await ActivityLabels.ResolveAsync(db, paged.Items.Select(a => (a.ParentType ?? a.EntityType, a.ParentId ?? a.EntityId)), ct);
        return new PagedResult<ActivityRow>(paged.Items.Select(a =>
        {
            var record = labels.GetValueOrDefault((a.ParentType ?? a.EntityType, a.ParentId ?? a.EntityId)) ?? a.ParentType ?? a.EntityType;
            var label = a.ParentType is null ? record : $"{record} › {a.EntityType}";
            return new ActivityRow(a.Id, a.Timestamp, a.UserId, a.UserName, a.Source, a.ChangeId, a.EntityType, a.EntityId, a.ParentType, a.ParentId, label, a.Action, a.Changes);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    /// <summary>Ids of the main records whose number, code or name contains the text (at most 500).</summary>
    internal static async Task<List<Guid>> MatchingRecordsAsync(CmmsDbContext db, string s, CancellationToken ct)
    {
        var ids = new List<Guid>();
        ids.AddRange(await db.WorkOrders.AllSites().Where(x => x.Number.Contains(s) || x.Title.Contains(s)).Select(x => x.Id).Take(100).ToListAsync(ct));
        ids.AddRange(await db.Assets.AllSites().Where(x => x.AssetNumber.Contains(s) || x.Name.Contains(s)).Select(x => x.Id).Take(100).ToListAsync(ct));
        ids.AddRange(await db.Parts.Where(x => x.PartNumber.Contains(s) || x.Name.Contains(s)).Select(x => x.Id).Take(100).ToListAsync(ct));
        ids.AddRange(await db.WorkRequests.AllSites().Where(x => x.Number.Contains(s) || x.Title.Contains(s)).Select(x => x.Id).Take(50).ToListAsync(ct));
        ids.AddRange(await db.PurchaseOrders.AllSites().Where(x => x.Number.Contains(s)).Select(x => x.Id).Take(50).ToListAsync(ct));
        ids.AddRange(await db.Users.Where(x => x.TenantId == db.CurrentTenantId && x.FullName.Contains(s)).Select(x => x.Id).Take(50).ToListAsync(ct));
        ids.AddRange(await db.Employees.Where(x => x.Code.Contains(s) || x.FullName.Contains(s)).Select(x => x.Id).Take(50).ToListAsync(ct));
        return ids;
    }
}

/// <summary>A record's number and name for the activity log and the storage overview ("WO-00012 · Pump swap"), by type and id.</summary>
public static class ActivityLabels
{
    public static async Task<Dictionary<(string Type, Guid Id), string>> ResolveAsync(CmmsDbContext db, IEnumerable<(string Type, Guid Id)> records, CancellationToken ct)
    {
        var byType = records.Distinct().GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());
        var labels = new Dictionary<(string, Guid), string>();
        if (byType.TryGetValue("WorkOrder", out var w))
            foreach (var x in await db.WorkOrders.IgnoreQueryFilters().Where(x => w.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("WorkOrder", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("Asset", out var a))
            foreach (var x in await db.Assets.IgnoreQueryFilters().Where(x => a.Contains(x.Id)).Select(x => new { x.Id, x.AssetNumber, x.Name }).ToListAsync(ct)) labels[("Asset", x.Id)] = $"{x.AssetNumber} · {x.Name}";
        if (byType.TryGetValue("Part", out var p))
            foreach (var x in await db.Parts.IgnoreQueryFilters().Where(x => p.Contains(x.Id)).Select(x => new { x.Id, x.PartNumber, x.Name }).ToListAsync(ct)) labels[("Part", x.Id)] = $"{x.PartNumber} · {x.Name}";
        if (byType.TryGetValue("WorkRequest", out var r))
            foreach (var x in await db.WorkRequests.IgnoreQueryFilters().Where(x => r.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("WorkRequest", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("PurchaseOrder", out var po))
            foreach (var x in await db.PurchaseOrders.IgnoreQueryFilters().Where(x => po.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct)) labels[("PurchaseOrder", x.Id)] = x.Number;
        if (byType.TryGetValue("PurchaseRequest", out var pr))
            foreach (var x in await db.PurchaseRequests.IgnoreQueryFilters().Where(x => pr.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct)) labels[("PurchaseRequest", x.Id)] = x.Number;
        if (byType.TryGetValue("Contract", out var c))
            foreach (var x in await db.Contracts.IgnoreQueryFilters().Where(x => c.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("Contract", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("PmSchedule", out var pm))
            foreach (var x in await db.PmSchedules.IgnoreQueryFilters().Where(x => pm.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("PmSchedule", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("PmTask", out var t))
            foreach (var x in await db.PmTasks.IgnoreQueryFilters().Where(x => t.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("PmTask", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Location", out var l))
            foreach (var x in await db.Locations.IgnoreQueryFilters().Where(x => l.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Location", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Site", out var si))
            foreach (var x in await db.Sites.IgnoreQueryFilters().Where(x => si.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Site", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Store", out var st))
            foreach (var x in await db.Stores.IgnoreQueryFilters().Where(x => st.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Store", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Supplier", out var su))
            foreach (var x in await db.Suppliers.IgnoreQueryFilters().Where(x => su.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Supplier", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Employee", out var e))
            foreach (var x in await db.Employees.IgnoreQueryFilters().Where(x => e.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.FullName }).ToListAsync(ct)) labels[("Employee", x.Id)] = $"{x.Code} · {x.FullName}";
        if (byType.TryGetValue("AppUser", out var u))
            foreach (var x in await db.Users.IgnoreQueryFilters().Where(x => u.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync(ct)) labels[("AppUser", x.Id)] = x.FullName;
        if (byType.TryGetValue("Role", out var ro))
            foreach (var x in await db.Roles.IgnoreQueryFilters().Where(x => ro.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct)) labels[("Role", x.Id)] = x.Name;
        if (byType.ContainsKey("Tenant")) foreach (var id in byType["Tenant"]) labels[("Tenant", id)] = Translator.T("Organization settings");
        return labels;
    }
}
