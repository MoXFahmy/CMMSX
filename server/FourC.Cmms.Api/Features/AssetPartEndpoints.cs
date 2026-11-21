using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public sealed record AssetPartInput(Guid PartId, decimal Quantity, string? Note);

/// <summary>Each asset's spare-parts list: the parts it takes and how many.</summary>
public static class AssetPartEndpoints
{
    public static void MapAssetPartEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/assets/{id:guid}/parts").WithTags("Assets");

        g.MapGet("/", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await ListAsync(db, id, ct));
        }).RequirePermission(Permissions.AssetsView);

        // The whole list: lines left out are removed.
        g.MapPut("/", async (Guid id, List<AssetPartInput> input, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            var v = AssetPartRules(input);
            var partIds = input.Select(l => l.PartId).Distinct().ToList();
            if (v.IsValid && await db.Parts.CountAsync(p => partIds.Contains(p.Id), ct) != partIds.Count) v.Add("partId", "Part not found.");
            if (!v.IsValid) return v.Problem();
            Replace(db, await db.AssetParts.Where(x => x.AssetId == id).ToListAsync(ct), id, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ListAsync(db, id, ct));
        }).RequirePermission(Permissions.AssetsEdit);

        // Adds the other asset's lines; a part already listed keeps its quantity and note.
        g.MapPost("/copy-from/{sourceId:guid}", async (Guid id, Guid sourceId, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct) || !await db.Assets.AnyAsync(a => a.Id == sourceId, ct)) return Results.NotFound();
            if (sourceId == id) throw new DomainException("Choose another asset to copy from.");
            var have = await db.AssetParts.Where(x => x.AssetId == id).Select(x => x.PartId).ToListAsync(ct);
            foreach (var line in await db.AssetParts.AsNoTracking().Where(x => x.AssetId == sourceId && !have.Contains(x.PartId)).ToListAsync(ct))
                db.AssetParts.Add(new AssetPart { AssetId = id, PartId = line.PartId, Quantity = line.Quantity, Note = line.Note });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ListAsync(db, id, ct));
        }).RequirePermission(Permissions.AssetsEdit);
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator AssetPartRules(IReadOnlyList<AssetPartInput> lines)
    {
        var v = new Validator()
            .When(lines.GroupBy(l => l.PartId).Any(g => g.Count() > 1), "partId", "List each part once.")
            .When(lines.Any(l => l.Quantity <= 0), "quantity", "Each part needs a quantity above zero.");
        foreach (var l in lines) v.MaxLength("note", l.Note, "Note", 200);
        return v;
    }

    /// <summary>Makes <paramref name="existing"/> (the asset's tracked lines) equal to <paramref name="wanted"/>. Not saved.</summary>
    internal static void Replace(CmmsDbContext db, List<AssetPart> existing, Guid assetId, IReadOnlyList<AssetPartInput> wanted)
    {
        foreach (var gone in existing.Where(e => wanted.All(w => w.PartId != e.PartId))) db.AssetParts.Remove(gone);
        foreach (var w in wanted)
        {
            var line = existing.FirstOrDefault(e => e.PartId == w.PartId);
            if (line is null) { line = new AssetPart { AssetId = assetId, PartId = w.PartId }; db.AssetParts.Add(line); }
            var qty = Math.Round(w.Quantity, 3);
            if (line.Quantity != qty) line.Quantity = qty;
            var note = w.Note.Clean();
            if (line.Note != note) line.Note = note;
        }
    }

    private static async Task<List<AssetPartRow>> ListAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        await db.AssetParts.AsNoTracking().Where(x => x.AssetId == id).OrderBy(x => x.Part!.PartNumber)
            .Select(x => new AssetPartRow(x.PartId, x.Part!.PartNumber, x.Part.Name, x.Part.UnitOfMeasure, x.Quantity, x.Note)).ToListAsync(ct);

    public sealed record AssetPartRow(Guid PartId, string PartNumber, string Name, string UnitOfMeasure, decimal Quantity, string? Note);
}
