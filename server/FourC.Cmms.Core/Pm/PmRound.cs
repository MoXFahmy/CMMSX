namespace FourC.Cmms.Core.Pm;

/// <summary>An asset listed on a round schedule, with what generation needs to know about it.</summary>
/// <param name="Available">Active, not disposed and not deleted.</param>
public sealed record RoundAsset(Guid Id, int Sequence, Guid SiteId, string? LocationPath, Guid? LocationId, bool Available);

/// <summary>A checklist step of a round work order: a PM task step for one covered asset.</summary>
public sealed record RoundStep(Guid AssetId, int Sequence, string Description);

/// <summary>Plans the work order of a PM round (legacy multi-asset PM) — pure, no database access.</summary>
public static class PmRound
{
    /// <summary>The listed assets that the round's work order covers: available ones of the schedule's site, in list order.</summary>
    public static IReadOnlyList<RoundAsset> Covered(IEnumerable<RoundAsset> listed, Guid siteId) =>
        listed.Where(a => a.Available && a.SiteId == siteId).OrderBy(a => a.Sequence).ToList();

    /// <summary>Every task step once per covered asset, grouped by asset in round order, numbered continuously from 1.</summary>
    public static IReadOnlyList<RoundStep> Steps(IReadOnlyList<RoundAsset> covered, IReadOnlyList<string> taskSteps) => Steps(covered, _ => taskSteps);

    /// <summary>Each covered asset's typed steps (0.13.0), grouped by asset in round order, numbered continuously from 1.</summary>
    public static IReadOnlyList<(Guid AssetId, int Sequence, PmTaskStep Step)> TypedSteps(IReadOnlyList<RoundAsset> covered, Func<Guid, IReadOnlyList<PmTaskStep>> stepsFor)
    {
        var seq = 0;
        return covered.SelectMany(a => stepsFor(a.Id).Select(st => (a.Id, ++seq, st))).ToList();
    }

    /// <summary>Each covered asset's own steps (its template's, 0.12.0), grouped by asset in round order, numbered continuously from 1.</summary>
    public static IReadOnlyList<RoundStep> Steps(IReadOnlyList<RoundAsset> covered, Func<Guid, IReadOnlyList<string>> stepsFor)
    {
        var seq = 0;
        return covered.SelectMany(a => stepsFor(a.Id).Select(d => new RoundStep(a.Id, ++seq, d))).ToList();
    }

    /// <summary>The deepest location containing every covered asset (from their materialized paths), or null if they share none.</summary>
    public static Guid? CommonLocationId(IReadOnlyList<RoundAsset> covered)
    {
        if (covered.Count == 0 || covered.Any(a => a.LocationPath is null)) return null;
        var paths = covered.Select(a => a.LocationPath!.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var common = 0;
        while (common < paths[0].Length && paths.All(p => p.Length > common && p[common] == paths[0][common])) common++;
        return common == 0 ? null : Guid.Parse(paths[0][common - 1]);
    }
}
