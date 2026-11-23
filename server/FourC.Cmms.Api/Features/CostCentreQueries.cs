using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>The cost centre in force (0.20.0): an asset's own, else up its parent assets, else its location's up the location tree.</summary>
public static class CostCentreQueries
{
    public static async Task<Guid?> InForceAsync(CmmsDbContext db, Guid? assetId, Guid? locationId, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        for (var a = assetId; a is { } id && seen.Add(id);)
        {
            var asset = await db.Assets.AllSites().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.CostCentreId, x.ParentAssetId, x.LocationId }).FirstOrDefaultAsync(ct);
            if (asset is null) break;
            if (asset.CostCentreId is { } own) return own;
            locationId ??= asset.LocationId;
            a = asset.ParentAssetId;
        }
        for (var l = locationId; l is { } id && seen.Add(id);)
        {
            var location = await db.Locations.AllSites().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.CostCentreId, x.ParentId }).FirstOrDefaultAsync(ct);
            if (location is null) break;
            if (location.CostCentreId is { } own) return own;
            l = location.ParentId;
        }
        return null;
    }

    /// <summary>Why a cost centre can't be chosen, or null when it can: it must exist for the user's sites and be active (unless kept unchanged).</summary>
    public static async Task<string?> ChoiceProblemAsync(CmmsDbContext db, Guid? costCentreId, Guid? current, CancellationToken ct)
    {
        if (costCentreId is not { } id || id == current) return null;
        return await db.CostCentres.AnyAsync(c => c.Id == id && c.IsActive, ct) ? null : "Choose an active cost centre.";
    }
}
