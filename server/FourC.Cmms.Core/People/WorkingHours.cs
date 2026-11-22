using System.Globalization;

namespace FourC.Cmms.Core.People;

/// <summary>One day of a weekly working pattern: start and end as "HH:mm" (the end after the start, the same day).</summary>
public sealed record WorkingDay(DayOfWeek Day, string Start, string End);

/// <summary>Weekly working patterns of the organization, a site or a person (0.9.0).</summary>
public static class WorkingHours
{
    public static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text ?? "", ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Why a pattern can't be saved, or null when it's fine (null and empty patterns are fine).</summary>
    public static string? Problem(IReadOnlyList<WorkingDay>? pattern)
    {
        if (pattern is null) return null;
        if (pattern.Any(d => !Enum.IsDefined(d.Day) || !TryTime(d.Start, out _) || !TryTime(d.End, out _))) return "Use times like 08:00.";
        if (pattern.GroupBy(d => d.Day).Any(g => g.Count() > 1)) return "Each day can appear once.";
        if (pattern.Any(d => TryTime(d.Start, out var s) && TryTime(d.End, out var e) && e <= s)) return "The end must be after the start.";
        return null;
    }
}
