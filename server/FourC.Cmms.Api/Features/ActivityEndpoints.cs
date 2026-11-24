using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
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
        // The person filter's choices: the organization's people and service accounts, for anyone who may open the log (0.26.1).
        g.MapGet("/people", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.TenantId == db.CurrentTenantId).OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.IsServiceAccount, u.IsActive }).ToListAsync(ct));
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

    internal static async Task<PagedResult<ActivityRow>> ListAsync(CmmsDbContext db, ActivityFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 200)
    {
        var q = db.AuditEntries.AsNoTracking();
        if (f.From is not null || f.To is not null)
        {
            // The organization's days, in its time zone (0.26.1).
            var zone = Sla.Zone((await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone);
            if (f.From is { } from) { var start = DayStart(from, zone); q = q.Where(a => a.Timestamp >= start); }
            if (f.To is { } to) { var end = DayStart(to.AddDays(1), zone); q = q.Where(a => a.Timestamp < end); }
        }
        if (f.UserId is { } user) q = q.Where(a => a.UserId == user);
        if (f.Source.Clean() is { } source) q = q.Where(a => a.Source == source);
        if (f.EntityType.Clean() is { } type) q = q.Where(a => a.EntityType == type || a.ParentType == type);
        if (f.Action.Clean() is { } action) q = q.Where(a => a.Action == action);
        if (f.ChangeId is { } change) q = q.Where(a => a.ChangeId == change);
        if (f.Search.Clean() is { } s)
        {
            // A record's number or name finds its entries; so do changed fields and values, the record type and the person.
            var ids = await MatchingRecordsAsync(db, s, ct);
            // Changed values are stored JSON-escaped (Arabic as \uXXXX): both spellings are searched (0.26.1).
            var escaped = JsonSerializer.Serialize(s)[1..^1];
            q = q.Where(a => a.EntityType.Contains(s) || (a.UserName != null && a.UserName.Contains(s)) || (a.Changes != null && (a.Changes.Contains(s) || a.Changes.Contains(escaped)))
                             || ids.Contains(a.EntityId) || (a.ParentId != null && ids.Contains(a.ParentId.Value)));
        }
        // Entries of a site's records only when the person can see the record; organization-wide entries (settings, users, parts…) stay.
        if (db.CurrentSiteIds is not null) q = q.Where(ActivitySites.Visible(db));
        var paged = await q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id).ToPagedAsync(page, pageSize, ct, maxPageSize);
        var labels = await ActivityLabels.ResolveAsync(db, paged.Items.Select(a => (a.ParentType ?? a.EntityType, a.ParentId ?? a.EntityId)), ct);
        return new PagedResult<ActivityRow>(paged.Items.Select(a =>
        {
            var record = labels.GetValueOrDefault((a.ParentType ?? a.EntityType, a.ParentId ?? a.EntityId)) ?? a.ParentType ?? a.EntityType;
            var label = a.ParentType is null ? record : $"{record} › {a.EntityType}";
            return new ActivityRow(a.Id, a.Timestamp, a.UserId, a.UserName, a.Source, a.ChangeId, a.EntityType, a.EntityId, a.ParentType, a.ParentId, label, a.Action, a.Changes);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    /// <summary>The first moment of <paramref name="day"/> in <paramref name="zone"/>.</summary>
    internal static DateTimeOffset DayStart(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
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
        // Deleted records keep their names here, so the filters are lifted; the organization is checked by hand (0.26.1).
        var tenant = db.CurrentTenantId;
        var labels = new Dictionary<(string, Guid), string>();
        if (byType.TryGetValue("WorkOrder", out var w))
            foreach (var x in await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenant && w.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("WorkOrder", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("Asset", out var a))
            foreach (var x in await db.Assets.IgnoreQueryFilters().Where(x => x.TenantId == tenant && a.Contains(x.Id)).Select(x => new { x.Id, x.AssetNumber, x.Name }).ToListAsync(ct)) labels[("Asset", x.Id)] = $"{x.AssetNumber} · {x.Name}";
        if (byType.TryGetValue("Part", out var p))
            foreach (var x in await db.Parts.IgnoreQueryFilters().Where(x => x.TenantId == tenant && p.Contains(x.Id)).Select(x => new { x.Id, x.PartNumber, x.Name }).ToListAsync(ct)) labels[("Part", x.Id)] = $"{x.PartNumber} · {x.Name}";
        if (byType.TryGetValue("WorkRequest", out var r))
            foreach (var x in await db.WorkRequests.IgnoreQueryFilters().Where(x => x.TenantId == tenant && r.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("WorkRequest", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("PurchaseOrder", out var po))
            foreach (var x in await db.PurchaseOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenant && po.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct)) labels[("PurchaseOrder", x.Id)] = x.Number;
        if (byType.TryGetValue("PurchaseRequest", out var pr))
            foreach (var x in await db.PurchaseRequests.IgnoreQueryFilters().Where(x => x.TenantId == tenant && pr.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct)) labels[("PurchaseRequest", x.Id)] = x.Number;
        if (byType.TryGetValue("Contract", out var c))
            foreach (var x in await db.Contracts.IgnoreQueryFilters().Where(x => x.TenantId == tenant && c.Contains(x.Id)).Select(x => new { x.Id, x.Number, x.Title }).ToListAsync(ct)) labels[("Contract", x.Id)] = $"{x.Number} · {x.Title}";
        if (byType.TryGetValue("PmSchedule", out var pm))
            foreach (var x in await db.PmSchedules.IgnoreQueryFilters().Where(x => x.TenantId == tenant && pm.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("PmSchedule", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("PmTask", out var t))
            foreach (var x in await db.PmTasks.IgnoreQueryFilters().Where(x => x.TenantId == tenant && t.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("PmTask", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Location", out var l))
            foreach (var x in await db.Locations.IgnoreQueryFilters().Where(x => x.TenantId == tenant && l.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Location", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Site", out var si))
            foreach (var x in await db.Sites.IgnoreQueryFilters().Where(x => x.TenantId == tenant && si.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Site", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Store", out var st))
            foreach (var x in await db.Stores.IgnoreQueryFilters().Where(x => x.TenantId == tenant && st.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Store", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Supplier", out var su))
            foreach (var x in await db.Suppliers.IgnoreQueryFilters().Where(x => x.TenantId == tenant && su.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)) labels[("Supplier", x.Id)] = $"{x.Code} · {x.Name}";
        if (byType.TryGetValue("Employee", out var e))
            foreach (var x in await db.Employees.IgnoreQueryFilters().Where(x => x.TenantId == tenant && e.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.FullName }).ToListAsync(ct)) labels[("Employee", x.Id)] = $"{x.Code} · {x.FullName}";
        if (byType.TryGetValue("AppUser", out var u))
            foreach (var x in await db.Users.IgnoreQueryFilters().Where(x => x.TenantId == tenant && u.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync(ct)) labels[("AppUser", x.Id)] = x.FullName;
        if (byType.TryGetValue("Role", out var ro))
            foreach (var x in await db.Roles.IgnoreQueryFilters().Where(x => x.TenantId == tenant && ro.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct)) labels[("Role", x.Id)] = x.Name;
        if (byType.ContainsKey("Tenant")) foreach (var id in byType["Tenant"]) labels[("Tenant", id)] = Translator.T("Organization settings");
        return labels;
    }
}

/// <summary>
/// Which entries of the activity log a site-limited person sees (0.25.0): a line's entry when they can see its record (a work
/// order's labour → the work order), a record's own entry when they can see the record, and organization-wide entries (parts,
/// users, settings…). Each record's own site filter decides. A test fails when a site-limited type is in neither list.
/// </summary>
public static class ActivitySites
{
    /// <summary>Site-limited records whose lines' entries follow them.</summary>
    public static readonly HashSet<string> Parents =
        ["Site", "Location", "Asset", "WorkRequest", "WorkOrder", "WorkOrderGroup", "PmSchedule", "Store", "PurchaseOrder", "PurchaseRequest", "Contract",
         "StockCount", "AssetGroup", "Meter", "DowntimeEvent"];

    /// <summary>Site-limited rows checked by themselves: records of their own, and lines whose record is organization-wide (a part's stock level in one store).</summary>
    public static readonly HashSet<string> Own =
        [.. Parents, "Team", "PrintLayout", "CostCentre", "StockLevel", "MeasurementReading", "MeasurementPointAsset", "AssetChecklist"];

    public static bool Checks(string type, string? parentType) => (parentType is not null && Parents.Contains(parentType)) || Own.Contains(type);

    /// <summary>a => (its record is site-limited and visible) or (it isn't a site-limited line and its own row is visible or organization-wide).</summary>
    public static Expression<Func<AuditEntry, bool>> Visible(CmmsDbContext db)
    {
        var types = db.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(Entity).IsAssignableFrom(t))
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
        var a = Expression.Parameter(typeof(AuditEntry), "a");
        Expression? byParent = null, byOwn = null;
        foreach (var name in Parents.Where(types.ContainsKey))
            byParent = Or(byParent, Body(Make(nameof(ByParent), types[name], db, name), a));
        foreach (var name in Own.Where(types.ContainsKey))
            byOwn = Or(byOwn, Body(Make(nameof(ByOwn), types[name], db, name), a));
        var parents = Parents.ToList();
        var own = Own.ToList();
        Expression<Func<AuditEntry, bool>> notALine = x => x.ParentType == null || !parents.Contains(x.ParentType);
        Expression<Func<AuditEntry, bool>> notOwn = x => !own.Contains(x.EntityType);
        var rest = Expression.AndAlso(Body(notALine, a), Expression.OrElse(byOwn ?? Expression.Constant(false), Body(notOwn, a)));
        return Expression.Lambda<Func<AuditEntry, bool>>(byParent is null ? rest : Expression.OrElse(byParent, rest), a);
    }

    private static Expression<Func<AuditEntry, bool>> ByParent<T>(CmmsDbContext db, string name) where T : Entity =>
        a => a.ParentType == name && db.Set<T>().Any(x => x.Id == a.ParentId);

    private static Expression<Func<AuditEntry, bool>> ByOwn<T>(CmmsDbContext db, string name) where T : Entity =>
        a => a.EntityType == name && db.Set<T>().Any(x => x.Id == a.EntityId);

    private static Expression<Func<AuditEntry, bool>> Make(string method, Type type, CmmsDbContext db, string name) =>
        (Expression<Func<AuditEntry, bool>>)typeof(ActivitySites).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [db, name])!;

    private static Expression Or(Expression? left, Expression right) => left is null ? right : Expression.OrElse(left, right);

    /// <summary>The lambda's body with its parameter replaced by <paramref name="a"/>.</summary>
    private static Expression Body(Expression<Func<AuditEntry, bool>> lambda, ParameterExpression a) => new Swap(lambda.Parameters[0], a).Visit(lambda.Body);

    private sealed class Swap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
