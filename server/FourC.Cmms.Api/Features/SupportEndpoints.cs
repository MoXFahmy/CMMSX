using System.Text.RegularExpressions;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AssetCategoryEndpoints
{
    public sealed record CategoryInput(string Code, string Name, string? Description, string? Color, bool IsActive = true);

    public static void MapAssetCategoryEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/asset-categories").WithTags("Asset categories");

        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Assets.Where(a => a.CategoryId != null).GroupBy(a => a.CategoryId!.Value).Select(x => new { x.Key, C = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            var cats = await db.AssetCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            return cats.Select(c => new { c.Id, c.Code, c.Name, c.Description, c.Color, c.IsActive, AssetCount = counts.GetValueOrDefault(c.Id) });
        }).RequirePermission(Permissions.AssetsView);

        g.MapPost("/", async (CategoryInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var c = CreateCategory(db, input);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/asset-categories/{c.Id}", new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, CategoryInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.AssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            UpdateCategory(c, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.AssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await db.Assets.AllSites().AnyAsync(a => a.CategoryId == id, ct)) throw new DomainException("This category is used by assets. Re-categorize them first or mark the category inactive.");
            db.AssetCategories.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator CategoryRules(CategoryInput i) => new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("color", i.Color, "Color", 20);

    internal static AssetCategory CreateCategory(CmmsDbContext db, CategoryInput i)
    {
        var c = new AssetCategory { Code = i.Code.Trim(), Name = i.Name.Trim() };
        UpdateCategory(c, i);
        db.AssetCategories.Add(c);
        return c;
    }

    internal static void UpdateCategory(AssetCategory c, CategoryInput i)
    {
        c.Code = i.Code.Trim(); c.Name = i.Name.Trim(); c.Description = i.Description.Clean(); c.Color = i.Color.Clean(); c.IsActive = i.IsActive;
    }

    private static async Task<Validator> ValidateAsync(CategoryInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = CategoryRules(i);
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.AssetCategories.AnyAsync(c => c.Code == i.Code.Trim() && c.Id != id, ct)) v.Add("code", "Another category uses this code.");
        return v;
    }
}

public static partial class CustomFieldEndpoints
{
    public sealed record FieldInput(string EntityType, string? Key, string Label, CustomFieldType DataType, List<string>? Options, bool IsRequired,
        int SortOrder, List<Guid>? CategoryIds, bool ShowInList);

    public static void MapCustomFieldEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/custom-fields").WithTags("Custom fields");

        g.MapGet("/", async (string? entityType, CmmsDbContext db, CancellationToken ct) =>
            await db.CustomFields.AsNoTracking().Where(c => entityType == null || c.EntityType == entityType).OrderBy(c => c.SortOrder).ThenBy(c => c.Label)
                .Select(c => new { c.Id, c.EntityType, c.Key, c.Label, c.DataType, c.Options, c.IsRequired, c.SortOrder, c.CategoryIds, c.ShowInList })
                .ToListAsync(ct)).RequireAuthorization();

        g.MapPost("/", async (FieldInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var key = string.IsNullOrWhiteSpace(input.Key) ? KeyFrom(input.Label ?? "") : input.Key.Trim();
            var v = await ValidateAsync(input with { Key = key }, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var f = new CustomFieldDefinition { EntityType = input.EntityType, Key = key, Label = input.Label!.Trim() };
            Apply(f, input);
            db.CustomFields.Add(f);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/custom-fields/{f.Id}", new { f.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, FieldInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            var v = await ValidateAsync(input with { Key = f.Key, EntityType = f.EntityType }, id, db, ct);
            if (!v.IsValid) return v.Problem();
            f.Label = input.Label.Trim();
            Apply(f, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { f.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            db.CustomFields.Remove(f);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static void Apply(CustomFieldDefinition f, FieldInput i)
    {
        f.DataType = i.DataType; f.IsRequired = i.IsRequired; f.SortOrder = i.SortOrder; f.ShowInList = i.ShowInList;
        f.Options = (i.Options ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
        f.CategoryIds = (i.CategoryIds ?? []).Distinct().ToList();
    }

    private static async Task<Validator> ValidateAsync(FieldInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("label", i.Label, "Label", 200).Required("key", i.Key, "Key", 100)
            .When(i.EntityType is not "Asset" and not "Location", "entityType", "Unsupported entity type.")
            .When(!Enum.IsDefined(i.DataType), "dataType", "Choose a valid field type.")
            .When(i.DataType == CustomFieldType.Select && (i.Options ?? []).All(string.IsNullOrWhiteSpace), "options", "Add at least one option.");
        if (!string.IsNullOrWhiteSpace(i.Key) && !KeyPattern().IsMatch(i.Key)) v.Add("key", "Key may contain only letters, digits and underscores.");
        if (!string.IsNullOrWhiteSpace(i.Key) && await db.CustomFields.AnyAsync(c => c.EntityType == i.EntityType && c.Key == i.Key && c.Id != id, ct))
            v.Add("key", "A field with this key already exists.");
        return v;
    }

    public static string KeyFrom(string label)
    {
        var k = NonWord().Replace(label.Trim(), "_").Trim('_');
        if (k.Length == 0) k = "field";
        if (char.IsDigit(k[0])) k = "f_" + k;
        return k.Length > 60 ? k[..60] : k;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")] private static partial Regex KeyPattern();
    [GeneratedRegex("[^A-Za-z0-9]+")] private static partial Regex NonWord();
}

public static class AttachmentEndpoints
{
    private const long MaxSize = 25 * 1024 * 1024;
    private static readonly Dictionary<string, (string View, string Edit)> EntityPermissions = new()
    {
        ["Asset"] = (Permissions.AssetsView, Permissions.AssetsEdit),
        ["Location"] = (Permissions.LocationsView, Permissions.LocationsManage),
        ["Site"] = (Permissions.LocationsView, Permissions.LocationsManage),
        ["WorkOrder"] = (Permissions.WorkOrdersView, Permissions.WorkOrdersExecute),
        ["WorkRequest"] = (Permissions.RequestsCreate, Permissions.RequestsCreate),
        ["PmTask"] = (Permissions.PmView, Permissions.PmManage),
        ["Part"] = (Permissions.InventoryView, Permissions.InventoryManage),
        ["Supplier"] = (Permissions.PurchasingView, Permissions.SuppliersManage),
        ["PurchaseOrder"] = (Permissions.PurchasingView, Permissions.PurchasingCreate),
    };

    // Files are served from the app's own origin, so a file the browser runs as a page (SVG, HTML, any XML) would run
    // its script as the signed-in user. Only these types open in place; everything else is downloaded.
    private static readonly HashSet<string> ShownInPlace = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp", "image/avif", "application/pdf",
    };
    // No script and nothing loaded from anywhere (inline styles only), plus a sandbox. Chrome shows an error page
    // instead of a sandboxed PDF, so PDFs shown in place go without the sandbox: with nosniff the browser only ever
    // reads them as a PDF.
    private const string NoScript = "default-src 'none'; style-src 'unsafe-inline'";
    private const string NoScriptSandboxed = NoScript + "; sandbox";
    private static readonly FileExtensionContentTypeProvider TypesByExtension = new();

    private static string MediaType(string? contentType) =>
        Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var t) && t.MediaType.HasValue
            ? t.MediaType.Value!.ToLowerInvariant() : "application/octet-stream";

    /// <summary>The stored type comes from the file's extension, not from the browser; failing that, the browser's
    /// type is kept only when it is safe to show in place.</summary>
    /// <summary>Stores an uploaded file and adds its attachment record (not yet saved). The type follows the file's extension.</summary>
    internal static async Task<Attachment> SaveAttachmentAsync(CmmsDbContext db, IFileStorage storage, string entityType, Guid entityId, IFormFile file, CancellationToken ct)
    {
        var fileName = Path.GetFileName(file.FileName);
        var att = new Attachment
        {
            EntityType = entityType, EntityId = entityId, FileName = fileName, ContentType = ContentTypeOf(fileName, file.ContentType), Size = file.Length, StorageKey = "",
        };
        att.StorageKey = $"{db.CurrentTenantId:N}/{entityType.ToLowerInvariant()}/{entityId:N}/{att.Id:N}";
        await using (var s = file.OpenReadStream()) await storage.SaveAsync(att.StorageKey, s, ct);
        db.Attachments.Add(att);
        return att;
    }

    internal static string ContentTypeOf(string fileName, string? claimed)
    {
        if (TypesByExtension.TryGetContentType(fileName, out var type)) return type;
        var media = MediaType(claimed);
        return ShownInPlace.Contains(media) ? media : "application/octet-stream";
    }

    public static void MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/attachments").WithTags("Attachments").RequireAuthorization();

        g.MapGet("/", async (string entityType, Guid entityId, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await AllowedAsync(http, perms, entityType, edit: false)) return Results.Forbid();
            if (!await SiteAccess.VisibleAsync(db, entityType, entityId, ct)) return Results.NotFound();
            var rows = await db.Attachments.AsNoTracking().Where(a => a.EntityType == entityType && a.EntityId == entityId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
            var userIds = rows.Select(r => r.CreatedById).OfType<Guid>().Distinct().ToList();
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            return Results.Ok(rows.Select(a => new { a.Id, a.FileName, a.ContentType, a.Size, a.Description, a.IsPrimaryImage, a.IsSignature, a.SignedBy, a.CreatedAt, CreatedBy = a.CreatedById is { } c ? users.GetValueOrDefault(c) : null, a.WorkOrderTaskId }));
        });

        g.MapPost("/", async (HttpRequest req, CmmsDbContext db, IFileStorage storage, PermissionService perms, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) throw new DomainException("Upload must be multipart/form-data.");
            var form = await req.ReadFormAsync(ct);
            var entityType = form["entityType"].ToString();
            if (!Guid.TryParse(form["entityId"], out var entityId)) throw new DomainException("Missing entity id.");
            if (!await AllowedAsync(req.HttpContext, perms, entityType, edit: true)) return Results.Forbid();
            var exists = entityType switch
            {
                "Asset" => await db.Assets.AnyAsync(a => a.Id == entityId, ct),
                "Location" => await db.Locations.AnyAsync(a => a.Id == entityId, ct),
                "Site" => await db.Sites.AnyAsync(a => a.Id == entityId, ct),
                "WorkOrder" => await db.WorkOrders.AnyAsync(a => a.Id == entityId, ct),
                "WorkRequest" => await db.WorkRequests.AnyAsync(a => a.Id == entityId, ct),
                "PmTask" => await db.PmTasks.AnyAsync(a => a.Id == entityId, ct),
                "Part" => await db.Parts.AnyAsync(a => a.Id == entityId, ct),
                "Supplier" => await db.Suppliers.AnyAsync(a => a.Id == entityId, ct),
                "PurchaseOrder" => await db.PurchaseOrders.AnyAsync(a => a.Id == entityId, ct),
                _ => false,
            };
            if (!exists) return Results.NotFound();
            // A work order photo taken on one of its checklist steps (0.13.0).
            Guid? stepId = null;
            if (Guid.TryParse(form["stepId"], out var sid))
            {
                if (entityType != "WorkOrder" || !await db.WorkOrderTasks.AnyAsync(t => t.Id == sid && t.WorkOrderId == entityId, ct)) throw new DomainException("This checklist step was removed.");
                stepId = sid;
            }
            // Sizes are checked before anything is stored, so a too-large third file doesn't leave the first two behind.
            if (form.Files.FirstOrDefault(f => f.Length > MaxSize) is { } tooLarge) throw new DomainException($"{tooLarge.FileName} is larger than 25 MB.");
            var saved = new List<Attachment>();
            foreach (var file in form.Files.Where(f => f.Length > 0))
            {
                var att = await SaveAttachmentAsync(db, storage, entityType, entityId, file, ct);
                att.WorkOrderTaskId = stepId;
                saved.Add(att);
            }
            await Housekeeping.SaveOrRemoveFilesAsync(storage, saved.Select(a => a.StorageKey).ToList(), () => db.SaveChangesAsync(ct), ct);
            return Results.Ok(saved.Select(a => new { a.Id, a.FileName }));
        }).DisableAntiforgery();

        g.MapGet("/{id:guid}/content", async (Guid id, bool? download, CmmsDbContext db, IFileStorage storage, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null || !await SiteAccess.VisibleAsync(db, a.EntityType, a.EntityId, ct)) return Results.NotFound();
            if (!await AllowedAsync(http, perms, a.EntityType, edit: false)) return Results.Forbid();
            var stream = await storage.OpenReadAsync(a.StorageKey, ct);
            if (stream is null) return Results.NotFound();
            // Decided from the stored type at every request, so files uploaded before types were checked are covered too.
            var type = MediaType(a.ContentType);
            var inline = download != true && ShownInPlace.Contains(type);
            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = inline && type == "application/pdf" ? NoScript : NoScriptSandboxed;
            if (inline)
            {
                // Shown in the browser, but saved under its own name (the Android app downloads PDFs it cannot show).
                var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline");
                disposition.SetHttpFileName(a.FileName);
                http.Response.Headers.ContentDisposition = disposition.ToString();
            }
            return Results.File(stream, a.ContentType, inline ? null : a.FileName, enableRangeProcessing: true);
        });

        g.MapPut("/{id:guid}/primary", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null || !await SiteAccess.VisibleAsync(db, a.EntityType, a.EntityId, ct)) return Results.NotFound();
            if (!await AllowedAsync(http, perms, a.EntityType, edit: true)) return Results.Forbid();
            if (!a.ContentType.StartsWith("image/")) throw new DomainException("Only images can be the primary photo.");
            await db.Attachments.Where(x => x.EntityType == a.EntityType && x.EntityId == a.EntityId && x.Id != id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPrimaryImage, false), ct);
            a.IsPrimaryImage = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, IFileStorage storage, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null || !await SiteAccess.VisibleAsync(db, a.EntityType, a.EntityId, ct)) return Results.NotFound();
            if (!await AllowedAsync(http, perms, a.EntityType, edit: true)) return Results.Forbid();
            db.Attachments.Remove(a);
            await db.SaveChangesAsync(ct);
            await storage.DeleteAsync(a.StorageKey, ct);
            return Results.NoContent();
        });
    }

    private static async Task<bool> AllowedAsync(HttpContext http, PermissionService perms, string entityType, bool edit)
    {
        if (!EntityPermissions.TryGetValue(entityType, out var p)) return false;
        var userId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        return (await perms.GetAsync(userId)).Contains(edit ? p.Edit : p.View);
    }
}

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (string entityType, Guid entityId, CmmsDbContext db, CancellationToken ct) =>
            !await SiteAccess.VisibleAsync(db, entityType, entityId, ct) ? Results.NotFound()
            : Results.Ok(await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.Timestamp).Take(200)
                .Select(a => new { a.Id, a.Action, a.Changes, a.UserName, a.Timestamp }).ToListAsync(ct)))
            .WithTags("Audit").RequireAuthorization();
    }
}

/// <summary>Records reached by type and id (attachments, the audit trail) answer "not found" outside the person's sites.</summary>
public static class SiteAccess
{
    public static async Task<bool> VisibleAsync(CmmsDbContext db, string entityType, Guid id, CancellationToken ct)
    {
        if (db.CurrentSiteIds is null) return true; // no limit: as before (the audit trail of a deleted record stays readable)
        return entityType switch
        {
            "Site" => await db.Sites.AnyAsync(x => x.Id == id, ct),
            "Location" => await db.Locations.AnyAsync(x => x.Id == id, ct),
            "Asset" => await db.Assets.AnyAsync(x => x.Id == id, ct),
            "WorkRequest" => await db.WorkRequests.AnyAsync(x => x.Id == id, ct),
            "WorkOrder" => await db.WorkOrders.AnyAsync(x => x.Id == id, ct),
            "PmSchedule" => await db.PmSchedules.AnyAsync(x => x.Id == id, ct),
            "Store" => await db.Stores.AnyAsync(x => x.Id == id, ct),
            "PurchaseOrder" => await db.PurchaseOrders.AnyAsync(x => x.Id == id, ct),
            "Team" => await db.Teams.AnyAsync(x => x.Id == id, ct),
            "Meter" => await db.Meters.AnyAsync(x => x.Id == id, ct),
            "DowntimeEvent" => await db.DowntimeEvents.AnyAsync(x => x.Id == id, ct),
            // Rows of a site's records: visible when their record is (their own site filters say so).
            "WorkOrderLabor" => await db.WorkOrderLabor.AnyAsync(x => x.Id == id, ct),
            "WorkOrderPart" => await db.WorkOrderParts.AnyAsync(x => x.Id == id, ct),
            "WorkOrderCost" => await db.WorkOrderCosts.AnyAsync(x => x.Id == id, ct),
            "WorkOrderTask" => await db.WorkOrderTasks.AnyAsync(x => x.Id == id, ct),
            "WorkOrderComment" => await db.WorkOrderComments.AnyAsync(x => x.Id == id, ct),
            "StockLevel" => await db.StockLevels.AnyAsync(x => x.Id == id, ct),
            "StockTransaction" => await db.StockTransactions.AnyAsync(x => x.Id == id, ct),
            "AssetPart" => await db.AssetParts.AnyAsync(x => x.Id == id, ct),
            "AssetTransfer" => await db.AssetTransfers.AnyAsync(x => x.Id == id, ct),
            "PurchaseOrderLine" => await db.PurchaseOrderLines.AnyAsync(x => x.Id == id, ct),
            // Shared by every site.
            "Part" or "PartGroup" or "Supplier" or "PmTask" or "Employee" or "Trade" or "FailureCode" or "AssetCategory" or "CustomFieldDefinition" => true,
            _ => false, // anything else: not shown to people with limited access
        };
    }
}
