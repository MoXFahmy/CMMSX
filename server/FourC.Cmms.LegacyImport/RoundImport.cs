/// <summary>Pure mapping rules for 4CSQL multi-asset PMs ("rounds") and PM step results. No dependencies: linked into the test project.</summary>
public static class RoundImport
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// A cloud work order belongs to one site, so a round whose assets span sites becomes one schedule per site, coded
    /// "&lt;PM no&gt;-&lt;site&gt;" (sites in order of first appearance). A single-site round keeps its PM number.
    /// </summary>
    public static IReadOnlyList<(string SiteCode, string Code, IReadOnlyList<string> AssetNos)> SplitBySite(string pmNo, IReadOnlyList<(string AssetNo, string SiteCode)> orderedAssets)
    {
        var groups = orderedAssets.GroupBy(a => a.SiteCode.Trim(), Ci).ToList();
        return groups.Select(g => (g.Key, groups.Count == 1 ? pmNo : Trunc($"{pmNo}-{g.Key}", 50), (IReadOnlyList<string>)g.Select(a => a.AssetNo).ToList())).ToList();
    }

    /// <summary>The assets a historical round work order covered: those its steps were recorded for, in round order; the schedule's list when it has no steps.</summary>
    public static IReadOnlyList<string> CoveredAssets(IReadOnlyList<(string AssetNo, int AssetSeqNo)> elements, IReadOnlyList<string> scheduleList)
    {
        var fromSteps = elements.Where(e => !string.IsNullOrWhiteSpace(e.AssetNo)).OrderBy(e => e.AssetSeqNo).Select(e => e.AssetNo.Trim()).Distinct(Ci).ToList();
        return fromSteps.Count > 0 ? fromSteps : scheduleList;
    }

    /// <summary>4CSQL records a finding and a remark per step; the cloud edition has one remarks field.</summary>
    public static string? JoinRemarks(string? findings, string? remarks)
    {
        var parts = new[] { findings, remarks }.Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
