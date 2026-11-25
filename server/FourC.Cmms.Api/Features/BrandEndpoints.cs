using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Brands and their models (0.21.0), picked on assets. An asset keeps the names as its manufacturer and model text, so renaming
/// here renames everywhere; renaming onto another name merges the two after the person agrees.
/// </summary>
public static class BrandEndpoints
{
    public sealed record BrandInput(string Name, bool IsActive = true, bool Merge = false);

    public static void MapBrandEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/brands", async (CmmsDbContext db, CancellationToken ct) =>
            (await db.Brands.AsNoTracking().Include(b => b.Models).OrderBy(b => b.Name).ToListAsync(ct)).Select(b => new
            {
                b.Id, b.Name, b.IsActive,
                Models = b.Models.Where(m => !m.IsDeleted).OrderBy(m => m.Name).Select(m => new { m.Id, m.Name, m.IsActive }),
            })).RequirePermission(Permissions.AssetsView).WithTags("Brands");

        var g = api.MapGroup("/brands").WithTags("Brands");
        g.MapPost("/", async (BrandInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var name = NameOf(i.Name);
            if (await db.Brands.AnyAsync(b => b.Name == name, ct)) throw new DomainException("This brand already exists.");
            var brand = new Brand { Name = name, IsActive = i.IsActive };
            db.Brands.Add(brand);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/brands/{brand.Id}", new { brand.Id });
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapPut("/{id:guid}", async (Guid id, BrandInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var name = NameOf(i.Name);
            // The whole change is one attempt (load, rename or merge, assets, log): a retry after a dropped connection starts again from what is stored (0.29.1).
            return await Atomic.RunAsync<IResult>(db, async tk =>
            {
                db.ChangeTracker.Clear();
                var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, tk);
                if (brand is null) return Results.NotFound();
                var other = await db.Brands.FirstOrDefaultAsync(b => b.Id != id && b.Name == name, tk);
                if (other is not null && !i.Merge) return Results.Conflict(new { MergeWith = other.Id, Message = Translator.T("Another brand has this name. Merge the two?") });
                if (other is not null)
                {
                    await MergeBrandAsync(db, brand, other, tk);
                    return Results.Ok(new { other.Id });
                }
                var renamed = brand.Name != name;
                brand.Name = name; brand.IsActive = i.IsActive;
                await db.SaveChangesAsync(tk);
                var followed = await db.Assets.AllSites().Where(a => a.BrandId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Manufacturer, name), tk);
                if (renamed && followed > 0)
                {
                    FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "Brand", id, "Updated", $"Brand renamed to {name}: {FourC.Cmms.Core.Auditing.AuditLog.Count(followed, "asset", "assets")}");
                    await db.SaveChangesAsync(tk);
                }
                return Results.Ok(new { brand.Id });
            }, ct);
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var brand = await db.Brands.Include(b => b.Models).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (brand is null) return Results.NotFound();
            if (await db.Assets.AllSites().AnyAsync(a => a.BrandId == id, ct)) throw new DomainException("This brand is in use. Make it inactive instead.");
            db.BrandModels.RemoveRange(brand.Models);
            db.Brands.Remove(brand);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AssetsEdit);

        g.MapPost("/{id:guid}/models", async (Guid id, BrandInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Brands.AnyAsync(b => b.Id == id, ct)) return Results.NotFound();
            var name = NameOf(i.Name);
            if (await db.BrandModels.AnyAsync(m => m.BrandId == id && m.Name == name, ct)) throw new DomainException("This brand already has this model.");
            var model = new BrandModel { BrandId = id, Name = name, IsActive = i.IsActive };
            db.BrandModels.Add(model);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/brand-models/{model.Id}", new { model.Id });
        }).RequirePermission(Permissions.AssetsEdit);

        var m = api.MapGroup("/brand-models").WithTags("Brands");
        m.MapPut("/{id:guid}", async (Guid id, BrandInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var name = NameOf(i.Name);
            return await Atomic.RunAsync<IResult>(db, async tk => // one attempt from the load on, as for brands (0.29.1)
            {
                db.ChangeTracker.Clear();
                var model = await db.BrandModels.FirstOrDefaultAsync(x => x.Id == id, tk);
                if (model is null) return Results.NotFound();
                var other = await db.BrandModels.FirstOrDefaultAsync(x => x.Id != id && x.BrandId == model.BrandId && x.Name == name, tk);
                if (other is not null && !i.Merge) return Results.Conflict(new { MergeWith = other.Id, Message = Translator.T("Another model of this brand has this name. Merge the two?") });
                if (other is not null)
                {
                    var moved = await db.Assets.AllSites().Where(a => a.ModelId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, other.Id).SetProperty(a => a.Model, other.Name), tk);
                    db.BrandModels.Remove(model);
                    FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "BrandModel", other.Id, "Updated", $"Model merged into {other.Name}: {FourC.Cmms.Core.Auditing.AuditLog.Count(moved, "asset", "assets")}");
                    await db.SaveChangesAsync(tk);
                    return Results.Ok(new { other.Id });
                }
                var modelRenamed = model.Name != name;
                model.Name = name; model.IsActive = i.IsActive;
                await db.SaveChangesAsync(tk);
                var followers = await db.Assets.AllSites().Where(a => a.ModelId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Model, name), tk);
                if (modelRenamed && followers > 0)
                {
                    FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "BrandModel", id, "Updated", $"Model renamed to {name}: {FourC.Cmms.Core.Auditing.AuditLog.Count(followers, "asset", "assets")}");
                    await db.SaveChangesAsync(tk);
                }
                return Results.Ok(new { model.Id });
            }, ct);
        }).RequirePermission(Permissions.AssetsEdit);

        m.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var model = await db.BrandModels.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (model is null) return Results.NotFound();
            if (await db.Assets.AllSites().AnyAsync(a => a.ModelId == id, ct)) throw new DomainException("This model is in use. Make it inactive instead.");
            db.BrandModels.Remove(model);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AssetsEdit);
    }

    private static string NameOf(string? name) =>
        name.Clean() is { } n ? n.Length <= 200 ? n : throw new DomainException("A name can be at most 200 characters.") : throw new DomainException("Enter a name.");

    /// <summary>
    /// <paramref name="from"/> goes into <paramref name="into"/>: its assets and models move (a model with the same name joins the
    /// existing one), assets take the kept names, and the merged brand is removed.
    /// </summary>
    private static async Task MergeBrandAsync(CmmsDbContext db, Brand from, Brand into, CancellationToken ct)
    {
        var fromModels = await db.BrandModels.Where(x => x.BrandId == from.Id).ToListAsync(ct);
        var intoModels = await db.BrandModels.Where(x => x.BrandId == into.Id).ToListAsync(ct);
        foreach (var model in fromModels)
        {
            if (intoModels.FirstOrDefault(x => string.Equals(x.Name, model.Name, StringComparison.OrdinalIgnoreCase)) is { } same)
            {
                await db.Assets.AllSites().Where(a => a.ModelId == model.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, same.Id).SetProperty(a => a.Model, same.Name), ct);
                db.BrandModels.Remove(model);
            }
            else model.BrandId = into.Id;
        }
        var moved = await db.Assets.AllSites().Where(a => a.BrandId == from.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.BrandId, into.Id), ct);
        db.Brands.Remove(from);
        FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "Brand", into.Id, "Updated", $"Brand merged into {into.Name}: {FourC.Cmms.Core.Auditing.AuditLog.Count(moved, "asset", "assets")}");
        await db.SaveChangesAsync(ct);
        await db.Assets.AllSites().Where(a => a.BrandId == into.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Manufacturer, into.Name), ct);
    }

    /// <summary>
    /// The page picked a brand and model: the asset takes their ids and their names as text. An empty pick clears the text only
    /// when the asset had that brand or model linked; text the lists couldn't link (a model with no maker, a model not in the
    /// list yet) stays as the page sent it, since the page can't show it.
    /// </summary>
    internal static async Task SetAsync(CmmsDbContext db, Asset asset, Guid? brandId, Guid? modelId, CancellationToken ct)
    {
        var (hadBrand, hadModel) = (asset.BrandId, asset.ModelId);
        asset.BrandId = brandId;
        asset.ModelId = brandId is null ? null : modelId;
        if (brandId is { } b) asset.Manufacturer = await db.Brands.Where(x => x.Id == b).Select(x => x.Name).FirstAsync(ct);
        else if (hadBrand is not null) { asset.Manufacturer = null; asset.Model = null; }
        if (asset.ModelId is { } mm) asset.Model = await db.BrandModels.Where(x => x.Id == mm).Select(x => x.Name).FirstAsync(ct);
        else if (hadModel is not null) asset.Model = null;
    }

    /// <summary>What's wrong with a picked brand and model; an inactive one may stay when it's already the asset's.</summary>
    internal static async Task CheckAsync(CmmsDbContext db, Validator v, Guid? brandId, Guid? modelId, Guid? currentBrand, Guid? currentModel, CancellationToken ct)
    {
        if (modelId is not null && brandId is null) { v.Add("modelId", "Choose a brand for this model."); return; }
        if (brandId is { } b && !await db.Brands.AnyAsync(x => x.Id == b && (x.IsActive || x.Id == currentBrand), ct)) v.Add("brandId", "Choose an active brand.");
        if (modelId is { } m)
        {
            var model = await db.BrandModels.Where(x => x.Id == m).Select(x => new { x.BrandId, x.IsActive }).FirstOrDefaultAsync(ct);
            if (model is null || model.BrandId != brandId) v.Add("modelId", "Choose a model of the chosen brand.");
            else if (!model.IsActive && m != currentModel) v.Add("modelId", "Choose an active model.");
        }
    }
}
