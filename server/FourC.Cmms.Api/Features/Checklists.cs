using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Smart checklists (0.13.0): what happens on the server when steps are made and answered.</summary>
public static class Checklists
{
    /// <summary>
    /// Finds the meter each new Meter step of the work order reads, before it is saved: its asset's (its own on a round, else the work
    /// order's) active meter with the step's unit, or the asset's only active meter; otherwise the answer names it.
    /// </summary>
    public static async Task ResolveMetersAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        var steps = db.ChangeTracker.Entries<WorkOrderTask>().Where(e => e.State == EntityState.Added && e.Entity.WorkOrderId == w.Id
            && e.Entity.Type == StepType.Meter && e.Entity.MeterId is null).Select(e => e.Entity).ToList();
        if (steps.Count == 0) return;
        var assets = steps.Select(t => t.AssetId ?? w.AssetId).OfType<Guid>().Distinct().ToList();
        if (assets.Count == 0) return;
        var meters = await db.Meters.AllSites().AsNoTracking().Where(m => assets.Contains(m.AssetId) && m.IsActive).Select(m => new { m.Id, m.AssetId, m.Unit }).ToListAsync(ct);
        foreach (var t in steps)
        {
            var mine = meters.Where(m => m.AssetId == (t.AssetId ?? w.AssetId)).ToList();
            var pick = t.Unit is { Length: > 0 } unit ? mine.Where(m => string.Equals(m.Unit, unit, StringComparison.OrdinalIgnoreCase)).ToList() : mine;
            if (pick.Count == 1) t.MeterId = pick[0].Id;
        }
    }
}
