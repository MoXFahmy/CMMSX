using System.Text.RegularExpressions;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
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
            var c = new AssetCategory { Code = input.Code.Trim(), Name = input.Name.Trim(), Description = input.Description.Clean(), Color = input.Color.Clean(), IsActive = input.IsActive };
            db.AssetCategories.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/asset-categories/{c.Id}", new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, CategoryInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.AssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            c.Code = input.Code.Trim(); c.Name = input.Name.Trim(); c.Description = input.Description.Clean(); c.Color = input.Color.Clean(); c.IsActive = input.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.AssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await db.Assets.AnyAsync(a => a.CategoryId == id, ct)) throw new DomainException("This category is used by assets. Re-categorize them first or mark the category inactive.");
            db.AssetCategories.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Validator> ValidateAsync(CategoryInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).MaxLength("color", i.Color, "Color", 20);
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
            var f = new CustomFieldDefinition { EntityType = input.EntityType, Key = key, Label = input.Label.Trim() };
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
    };

    public static void MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/attachments").WithTags("Attachments").RequireAuthorization();

        g.MapGet("/", async (string entityType, Guid entityId, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await AllowedAsync(http, perms, entityType, edit: false)) return Results.Forbid();
            var rows = await db.Attachments.AsNoTracking().Where(a => a.EntityType == entityType && a.EntityId == entityId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
            var userIds = rows.Select(r => r.CreatedById).OfType<Guid>().Distinct().ToList();
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            return Results.Ok(rows.Select(a => new { a.Id, a.FileName, a.ContentType, a.Size, a.Description, a.IsPrimaryImage, a.CreatedAt, CreatedBy = a.CreatedById is { } c ? users.GetValueOrDefault(c) : null }));
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
                _ => false,
            };
            if (!exists) return Results.NotFound();
            var saved = new List<object>();
            foreach (var file in form.Files)
            {
                if (file.Length == 0) continue;
                if (file.Length > MaxSize) throw new DomainException($"{file.FileName} is larger than 25 MB.");
                var att = new Attachment
                {
                    EntityType = entityType, EntityId = entityId, FileName = Path.GetFileName(file.FileName), ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    Size = file.Length, StorageKey = "",
                };
                att.StorageKey = $"{db.CurrentTenantId:N}/{entityType.ToLowerInvariant()}/{entityId:N}/{att.Id:N}";
                await using (var s = file.OpenReadStream()) await storage.SaveAsync(att.StorageKey, s, ct);
                db.Attachments.Add(att);
                saved.Add(new { att.Id, att.FileName });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(saved);
        }).DisableAntiforgery();

        g.MapGet("/{id:guid}/content", async (Guid id, bool? download, CmmsDbContext db, IFileStorage storage, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            if (!await AllowedAsync(http, perms, a.EntityType, edit: false)) return Results.Forbid();
            var stream = await storage.OpenReadAsync(a.StorageKey, ct);
            if (stream is null) return Results.NotFound();
            var inline = download != true && (a.ContentType.StartsWith("image/") || a.ContentType == "application/pdf");
            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(stream, a.ContentType, inline ? null : a.FileName, enableRangeProcessing: true);
        });

        g.MapPut("/{id:guid}/primary", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
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
            if (a is null) return Results.NotFound();
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
            await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.Timestamp).Take(200)
                .Select(a => new { a.Id, a.Action, a.Changes, a.UserName, a.Timestamp }).ToListAsync(ct))
            .WithTags("Audit").RequireAuthorization();
    }
}
