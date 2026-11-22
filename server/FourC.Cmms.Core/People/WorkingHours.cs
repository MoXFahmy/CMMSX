using System.Globalization;

namespace FourC.Cmms.Core.People;

/// <summary>One day of a weekly working pattern: start and end as "HH:mm" (the end after the start, the same day; "24:00" = midnight at the end).</summary>
public sealed record WorkingDay(DayOfWeek Day, string Start, string End);

/// <summary>Weekly working patterns of the organization, a site or a person (0.9.0).</summary>
public static class WorkingHours
{
    /// <summary>Minutes since midnight for "HH:mm" ("24:00" = 1440), or null when it isn't a time.</summary>
    public static int? Minutes(string? text)
    {
        if (text?.Trim() == "24:00") return 24 * 60;
        return TimeOnly.TryParseExact(text?.Trim() ?? "", ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.Hour * 60 + t.Minute : null;
    }

    /// <summary>Why a pattern can't be saved, or null when it's fine (null and empty patterns are fine).</summary>
    public static string? Problem(IReadOnlyList<WorkingDay>? pattern)
    {
        if (pattern is null) return null;
        if (pattern.Any(d => !Enum.IsDefined(d.Day) || Minutes(d.Start) is null || Minutes(d.End) is null)) return "Use times like 08:00.";
        if (pattern.GroupBy(d => d.Day).Any(g => g.Count() > 1)) return "Each day can appear once.";
        if (pattern.Any(d => Minutes(d.End) <= Minutes(d.Start))) return "The end must be after the start.";
        return null;
    }

    /// <summary>Hours worked on <paramref name="day"/> (0 on a day off).</summary>
    public static decimal HoursOn(IReadOnlyList<WorkingDay> pattern, DayOfWeek day) =>
        pattern.Where(d => d.Day == day).Select(d => Minutes(d.End) is { } e && Minutes(d.Start) is { } s && e > s ? Math.Round((e - s) / 60m, 2) : 0m).FirstOrDefault();

    /// <summary>The organization's pattern; before 0.9.0 (none set) every day from 08:00 for the old "hours per day".</summary>
    public static IReadOnlyList<WorkingDay> Organization(Tenancy.TenantSettings s)
    {
        if (s.WorkingHours is { Count: > 0 } own) return own;
        var minutes = (int)Math.Round(Math.Clamp(s.DailyCapacityHours, 0, 24) * 60);
        var start = minutes <= 16 * 60 ? 8 * 60 : 24 * 60 - minutes;
        string Clock(int m) => m == 24 * 60 ? "24:00" : $"{m / 60:00}:{m % 60:00}";
        return minutes == 0 ? [] : Enum.GetValues<DayOfWeek>().Select(d => new WorkingDay(d, Clock(start), Clock(start + minutes))).ToList();
    }

    /// <summary>A person's pattern: their own; else their site's when they work at exactly one site that has its own; else the organization's.</summary>
    public static IReadOnlyList<WorkingDay> ForEmployee(List<WorkingDay>? own, IReadOnlyList<Guid> siteIds, IReadOnlyDictionary<Guid, List<WorkingDay>?> siteHours,
        Tenancy.TenantSettings org)
    {
        if (own is { Count: > 0 }) return own;
        if (siteIds.Count == 1 && siteHours.GetValueOrDefault(siteIds[0]) is { Count: > 0 } site) return site;
        return Organization(org);
    }
}
