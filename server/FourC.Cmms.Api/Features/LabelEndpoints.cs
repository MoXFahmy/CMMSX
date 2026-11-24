using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// QR labels: what each label shows and the link its QR code holds (<c>{App:PublicUrl}/q/{type}/{id}</c>). Scanned in the
/// technician view the link opens the record from the phone's copy; with any phone camera it opens the page.
/// </summary>
public static class LabelEndpoints
{
    public sealed record LabelInput(string? Type, List<Guid>? Ids, bool IncludeChildren = false);
    /// <summary>One label. <c>Site</c> (0.23.0) is the site's name for assets and locations; parts have none.</summary>
    public sealed record Label(Guid Id, string Number, string Name, string? Detail, string Url, string? Site = null);

    public const int MaxLabels = 500;

    public static void MapLabelEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/labels", async (LabelInput input, CmmsDbContext db, ITenantContext ctx, PermissionService perms, IConfiguration config, HttpContext http,
            CancellationToken ct) =>
        {
            var ids = (input.Ids ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.Problem(title: Translator.T("Choose what to print labels for."), statusCode: 400);
            if (ids.Count > MaxLabels) return Results.Problem(title: Translator.T($"Print at most {MaxLabels} labels at a time."), statusCode: 400);
            var type = input.Type?.ToLowerInvariant();
            var needed = type switch { "asset" => Permissions.AssetsView, "location" => Permissions.LocationsView, "part" => Permissions.InventoryView, _ => null };
            if (needed is null) return Results.Problem(title: Translator.T("Labels are for assets, locations or parts."), statusCode: 400);
            if (!(await perms.GetAsync(ctx.UserId!.Value)).Contains(needed)) return Results.Forbid();

            var baseUrl = (config["App:PublicUrl"] is { Length: > 0 } u ? u : $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
            string Link(Guid id) => $"{baseUrl}/q/{type}/{id}";
            var names = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
            string? PathText(string? path) => path is null ? null : string.Join(" › ", LocationEndpoints.PathIds(path).Select(id => names.GetValueOrDefault(id)).Where(n => n is not null));

            List<Label> labels;
            switch (type)
            {
                case "asset":
                    labels = (await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id))
                            .Select(a => new { a.Id, a.AssetNumber, a.Name, Path = a.Location != null ? a.Location.Path : null, Site = a.Site!.Name }).ToListAsync(ct))
                        .Select(a => new Label(a.Id, a.AssetNumber, a.Name, PathText(a.Path), Link(a.Id), a.Site)).ToList();
                    break;
                case "location":
                {
                    var chosen = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.Path }).ToListAsync(ct);
                    var q = db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id));
                    if (input.IncludeChildren)
                    {
                        // Everything below a chosen location: its materialized path starts with the chosen one's.
                        var prefixes = chosen.Select(c => c.Path).ToList();
                        var below = (await db.Locations.AsNoTracking().Select(l => new { l.Id, l.Path }).ToListAsync(ct))
                            .Where(l => prefixes.Any(p => l.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase))).Select(l => l.Id).ToList();
                        if (below.Count > MaxLabels) return Results.Problem(title: Translator.T($"That is {below.Count} locations; print at most {MaxLabels} labels at a time."), statusCode: 400);
                        q = db.Locations.AsNoTracking().Where(l => below.Contains(l.Id));
                    }
                    labels = (await q.Select(l => new { l.Id, l.Code, l.Name, l.Path, l.ParentId, Site = l.Site!.Name }).ToListAsync(ct))
                        .Select(l => new Label(l.Id, l.Code, l.Name, l.ParentId is null ? null : PathText(l.Path) is { } p && p.LastIndexOf(" › ", StringComparison.Ordinal) is var i && i > 0 ? p[..i] : null, Link(l.Id), l.Site))
                        .ToList();
                    break;
                }
                default:
                    labels = (await db.Parts.AsNoTracking().Where(p => ids.Contains(p.Id))
                            .Select(p => new { p.Id, p.PartNumber, p.Name, p.Manufacturer, Group = p.Group != null ? p.Group.Name : null }).ToListAsync(ct))
                        .Select(p => new Label(p.Id, p.PartNumber, p.Name, p.Manufacturer ?? p.Group, Link(p.Id))).ToList();
                    break;
            }
            return Results.Ok(labels.OrderBy(l => l.Number, StringComparer.OrdinalIgnoreCase));
        }).RequireAuthorization().WithTags("Labels");
    }
}
