using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>Maintains the materialized Path/Depth of the location tree.</summary>
public static class LocationTree
{
    public const int MaxDepth = 12;

    /// <summary>Sets Path/Depth for a new or re-parented location (parent must already be loaded or queryable).</summary>
    public static async Task AssignPathAsync(CmmsDbContext db, Location loc, CancellationToken ct = default)
    {
        if (loc.ParentId is null)
        {
            loc.Path = "/" + Location.Segment(loc.Id);
            loc.Depth = 0;
            return;
        }
        var parent = Tracked(db, loc.ParentId.Value)
                     ?? await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loc.ParentId, ct)
                     ?? throw new DomainException("Parent location not found.");
        if (parent.SiteId != loc.SiteId) throw new DomainException("Parent location belongs to a different site.");
        if (parent.Path.Contains(Location.Segment(loc.Id))) throw new DomainException("A location cannot be moved under itself or one of its children.");
        if (parent.Depth + 1 > MaxDepth) throw new DomainException($"Location tree cannot be deeper than {MaxDepth} levels.");
        loc.Path = parent.Path + Location.Segment(loc.Id);
        loc.Depth = parent.Depth + 1;
    }

    /// <summary>
    /// A location this context already holds (a parent added a moment ago and not saved yet). Looked up without scanning
    /// every tracked record for changes first: an import adding thousands of locations would otherwise slow down with the
    /// square of their number.
    /// </summary>
    private static Location? Tracked(CmmsDbContext db, Guid id)
    {
        var auto = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try { return db.Locations.Local.FirstOrDefault(l => l.Id == id); }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = auto; }
    }

    /// <summary>After moving a location, rewrite the paths (and site) of all its descendants in one statement.</summary>
    public static Task<int> RewriteDescendantsAsync(CmmsDbContext db, string oldPath, string newPath, int depthDelta, Guid siteId, CancellationToken ct = default) =>
        db.Locations.Where(l => l.Path.StartsWith(oldPath) && l.Path != oldPath)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Path, l => newPath + l.Path.Substring(oldPath.Length))
                .SetProperty(l => l.Depth, l => l.Depth + depthDelta)
                .SetProperty(l => l.SiteId, siteId), ct);
}
