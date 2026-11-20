/// <summary>Pure rules for 4CSQL PM parts lists (<c>checklistInv</c>). No dependencies: linked into the test project.</summary>
public static class PmPartsRules
{
    /// <summary>
    /// Cleans legacy rows into (task code, part number, quantity): keys trimmed, rows without a task, a part or a quantity above
    /// zero dropped, and repeats of the same task and part (ignoring case) summed under the first spelling seen.
    /// </summary>
    public static IReadOnlyList<(string TaskCode, string PartNo, decimal Quantity)> Lines(IEnumerable<(string? TaskCode, string? PartNo, double? Qty)> rows)
    {
        var result = new List<(string TaskCode, string PartNo, decimal Quantity)>();
        var index = new Dictionary<(string, string), int>();
        foreach (var (task, part, qty) in rows)
        {
            var t = task?.Trim();
            var p = part?.Trim();
            if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(p) || qty is not > 0) continue;
            var q = Math.Round((decimal)qty.Value, 3);
            var key = (t.ToUpperInvariant(), p.ToUpperInvariant());
            if (index.TryGetValue(key, out var i)) result[i] = (result[i].TaskCode, result[i].PartNo, result[i].Quantity + q);
            else { index[key] = result.Count; result.Add((t, p, q)); }
        }
        return result;
    }
}
