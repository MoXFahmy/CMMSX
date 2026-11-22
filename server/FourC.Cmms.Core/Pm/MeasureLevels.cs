namespace FourC.Cmms.Core.Pm;

/// <summary>Where a value falls against a measurement point's range and levels (0.13.0); no database.</summary>
public static class MeasureLevels
{
    public const string RangeOrder = "The valid range's minimum must be below its maximum.";
    public const string LevelOrder = "Levels must go up: alarm low, alert low, target, alert high, alarm high.";
    public const string LevelRange = "Levels must be inside the valid range.";
    public const string OutsideRange = "Enter a value from {0} to {1} {2}.";
    public const string AtMost = "Enter a value of at most {0} {1}.";
    public const string AtLeast = "Enter a value of at least {0} {1}.";

    /// <summary>Alarm wins over alert; a level at its threshold counts (≥ high, ≤ low); unset levels are skipped.</summary>
    public static MeasureLevel LevelOf(MeasurementPoint p, decimal value)
    {
        if (p.AlarmHigh is { } ah && value >= ah) return MeasureLevel.AlarmHigh;
        if (p.AlertHigh is { } eh && value >= eh) return MeasureLevel.AlertHigh;
        if (p.AlarmLow is { } al && value <= al) return MeasureLevel.AlarmLow;
        if (p.AlertLow is { } el && value <= el) return MeasureLevel.AlertLow;
        return MeasureLevel.Normal;
    }

    /// <summary>A value that can't be right at all (typed 850 for 85.0).</summary>
    public static bool OutOfRange(MeasurementPoint p, decimal value) => value < (p.ValidMin ?? decimal.MinValue) || value > (p.ValidMax ?? decimal.MaxValue);

    /// <summary>The message for <see cref="OutOfRange"/> (English; the caller translates it).</summary>
    public static string RangeMessage(MeasurementPoint p) => (p.ValidMin, p.ValidMax) switch
    {
        ({ } min, { } max) => string.Format(OutsideRange, Show(min), Show(max), p.Unit ?? ""),
        (null, { } max) => string.Format(AtMost, Show(max), p.Unit ?? ""),
        ({ } min, null) => string.Format(AtLeast, Show(min), p.Unit ?? ""),
        _ => "",
    } is var m && m.Length > 0 ? m.TrimEnd(' ', '.') + "." : "";

    private static string Show(decimal d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A point's range and levels that don't fit together, as (field, message).</summary>
    public static IReadOnlyList<(string Field, string Message)> Problems(MeasurementPoint p)
    {
        var list = new List<(string, string)>();
        if (p.ValidMin is { } min && p.ValidMax is { } max && min >= max) list.Add(("validMax", RangeOrder));
        var levels = new (string Field, decimal? Value)[] { ("alarmLow", p.AlarmLow), ("alertLow", p.AlertLow), ("target", p.Target), ("alertHigh", p.AlertHigh), ("alarmHigh", p.AlarmHigh) };
        decimal? previous = null;
        foreach (var (field, value) in levels)
        {
            if (value is not { } v) continue;
            if (previous is { } prev && v < prev) { list.Add((field, LevelOrder)); break; }
            previous = v;
        }
        foreach (var (field, value) in levels)
            if (value is { } v && OutOfRange(p, v)) { list.Add((field, LevelRange)); break; }
        return list;
    }

    /// <summary>The work template a reading at this level raises its follow-up from, if the point names one.</summary>
    public static Guid? TemplateFor(MeasurementPoint p, MeasureLevel level) => level switch
    {
        MeasureLevel.AlarmHigh => p.AlarmHighTemplateId,
        MeasureLevel.AlertHigh => p.AlertHighTemplateId,
        MeasureLevel.AlertLow => p.AlertLowTemplateId,
        MeasureLevel.AlarmLow => p.AlarmLowTemplateId,
        _ => null,
    };
}
