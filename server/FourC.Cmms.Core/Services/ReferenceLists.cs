using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>
/// Keeps the brand, model and unit lists (0.21.0) in step with the texts records carry: it adds what's missing and links assets
/// to their brand and model, with the asset's text set to the list's name. Run at startup (this builds the starting lists on
/// upgrade), inside every import, and at the end of a 4CSQL migration run. Running it again changes nothing.
/// </summary>
public static class ReferenceLists
{
    public static readonly (string Symbol, string Name, string NameAr)[] StarterUnits =
    [
        ("pcs", "Pieces", "قطعة"), ("m", "Metres", "متر"), ("L", "Litres", "لتر"), ("kg", "Kilograms", "كيلوغرام"),
        ("h", "Hours", "ساعة"), ("°C", "Degrees Celsius", "درجة مئوية"), ("bar", "Bar", "بار"), ("kWh", "Kilowatt-hours", "كيلوواط ساعة"),
    ];

    private const string CaseSensitive = "Latin1_General_100_CS_AS";
    private const string Binary = "Latin1_General_100_BIN2";

    /// <summary>Spaces trimmed and inner runs made one, case kept.</summary>
    public static string Tidy(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Key(string s) => Tidy(s).ToLowerInvariant();

    public static async Task ReconcileAllAsync(CmmsDbContext db, CancellationToken ct = default)
    {
        foreach (var tenant in await db.Tenants.IgnoreQueryFilters().Select(t => t.Id).ToListAsync(ct))
            await ReconcileAsync(db, tenant, ct);
    }

    /// <summary>One organization: the brands, models and units added.</summary>
    public static async Task<(int Brands, int Models, int Units)> ReconcileAsync(CmmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var assets = db.Assets.IgnoreQueryFilters().Where(a => a.TenantId == tenantId && !a.IsDeleted);

        // Brands: one per manufacturer text (case and spaces don't matter; the first spelling met names it).
        var brands = (await db.Brands.IgnoreQueryFilters().Where(b => b.TenantId == tenantId && !b.IsDeleted).Select(b => new { b.Id, b.Name }).ToListAsync(ct))
            .GroupBy(b => Key(b.Name)).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Name));
        // Only assets not linked yet (linked ones already have their brand), so a run with nothing to do is a few queries.
        var makers = await assets.Where(a => a.BrandId == null && a.Manufacturer != null && a.Manufacturer != "").Select(a => a.Manufacturer!).Distinct().ToListAsync(ct);
        var newBrands = 0;
        foreach (var maker in makers.OrderBy(m => m, StringComparer.Ordinal))
        {
            var key = Key(maker);
            if (key.Length == 0 || brands.ContainsKey(key)) continue;
            var b = new Brand { TenantId = tenantId, Name = Tidy(maker) };
            db.Brands.Add(b);
            brands[key] = (b.Id, b.Name);
            newBrands++;
        }

        // Models: one per (brand, model text); model text without a manufacturer stays as text.
        var models = (await db.BrandModels.IgnoreQueryFilters().Where(m => m.TenantId == tenantId && !m.IsDeleted).Select(m => new { m.Id, m.BrandId, m.Name }).ToListAsync(ct))
            .GroupBy(m => (m.BrandId, Key(m.Name))).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Name));
        var pairs = await assets.Where(a => a.ModelId == null && a.Manufacturer != null && a.Manufacturer != "" && a.Model != null && a.Model != "")
            .Select(a => new { Maker = a.Manufacturer!, Model = a.Model! }).Distinct().ToListAsync(ct);
        var newModels = 0;
        foreach (var p in pairs.OrderBy(p => p.Model, StringComparer.Ordinal))
        {
            if (!brands.TryGetValue(Key(p.Maker), out var brand) || Key(p.Model).Length == 0) continue;
            var key = (brand.Id, Key(p.Model));
            if (models.ContainsKey(key)) continue;
            var m = new BrandModel { TenantId = tenantId, BrandId = brand.Id, Name = Tidy(p.Model) };
            db.BrandModels.Add(m);
            models[key] = (m.Id, m.Name);
            newModels++;
        }

        // Units: one per unit text on parts, measurement points and template steps (spaces trimmed, case kept).
        var symbols = (await db.Units.IgnoreQueryFilters().Where(u => u.TenantId == tenantId && !u.IsDeleted).Select(u => u.Symbol).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var texts = new List<string>();
        texts.AddRange(await db.Parts.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .Select(p => EF.Functions.Collate(p.UnitOfMeasure, CaseSensitive)).Distinct().ToListAsync(ct));
        texts.AddRange(await db.MeasurementPoints.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && !p.IsDeleted && p.Unit != null)
            .Select(p => EF.Functions.Collate(p.Unit!, CaseSensitive)).Distinct().ToListAsync(ct));
        texts.AddRange(await (from s in db.PmTaskSteps.IgnoreQueryFilters()
                              join t in db.PmTasks.IgnoreQueryFilters() on s.PmTaskId equals t.Id
                              where t.TenantId == tenantId && !t.IsDeleted && s.Unit != null
                              select EF.Functions.Collate(s.Unit!, CaseSensitive)).Distinct().ToListAsync(ct));
        var newUnits = 0;
        foreach (var text in texts.Select(Tidy).Where(t => t.Length is > 0 and <= 30).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal))
        {
            if (!symbols.Add(text)) continue;
            db.Units.Add(new Unit { TenantId = tenantId, Symbol = text, Name = text });
            newUnits++;
        }

        if (newBrands + newModels + newUnits > 0) await db.SaveChangesAsync(ct);

        // Link: every manufacturer spelling to its brand, then every model text to its model; the text becomes the list's name.
        foreach (var maker in makers)
        {
            if (!brands.TryGetValue(Key(maker), out var brand)) continue;
            await assets.Where(a => a.BrandId == null && a.Manufacturer == maker).ExecuteUpdateAsync(s => s.SetProperty(a => a.BrandId, brand.Id), ct);
        }
        await assets.Where(a => a.BrandId != null && db.Brands.IgnoreQueryFilters().Any(b => b.Id == a.BrandId && EF.Functions.Collate(b.Name, Binary) != a.Manufacturer))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Manufacturer, a => db.Brands.IgnoreQueryFilters().Where(b => b.Id == a.BrandId).Select(b => b.Name).First()), ct);
        foreach (var p in pairs)
        {
            if (!brands.TryGetValue(Key(p.Maker), out var brand) || !models.TryGetValue((brand.Id, Key(p.Model)), out var model)) continue;
            await assets.Where(a => a.ModelId == null && a.BrandId == brand.Id && a.Model == p.Model).ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, model.Id), ct);
        }
        await assets.Where(a => a.ModelId != null && db.BrandModels.IgnoreQueryFilters().Any(m => m.Id == a.ModelId && EF.Functions.Collate(m.Name, Binary) != a.Model))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Model, a => db.BrandModels.IgnoreQueryFilters().Where(m => m.Id == a.ModelId).Select(m => m.Name).First()), ct);

        return (newBrands, newModels, newUnits);
    }
}
