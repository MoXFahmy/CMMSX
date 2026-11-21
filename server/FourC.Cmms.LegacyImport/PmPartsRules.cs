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
            if (!Usable(task, part, qty)) continue;
            var t = task!.Trim();
            var p = part!.Trim();
            var q = Math.Round((decimal)qty!.Value, 3);
            var key = (t.ToUpperInvariant(), p.ToUpperInvariant());
            if (index.TryGetValue(key, out var i)) result[i] = (result[i].TaskCode, result[i].PartNo, result[i].Quantity + q);
            else { index[key] = result.Count; result.Add((t, p, q)); }
        }
        return result;
    }

    /// <summary>Rows dropped for having no task, no part or no quantity above zero (repeats that were added together aren't counted).</summary>
    public static int Unusable(IEnumerable<(string? TaskCode, string? PartNo, double? Qty)> rows) => rows.Count(r => !Usable(r.TaskCode, r.PartNo, r.Qty));

    private static bool Usable(string? task, string? part, double? qty) =>
        !string.IsNullOrWhiteSpace(task) && !string.IsNullOrWhiteSpace(part) && qty is > 0;
}
