using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Planning;

/// <summary>Whether an availability type takes hours away (holiday, training) or adds them (an extra shift).</summary>
public enum AvailabilityKind { Away, Extra }

/// <summary>A kind of time away or extra time (0.19.0), with the colour the planning board tints a day with.</summary>
public class AvailabilityType : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public AvailabilityKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Time away or extra time (0.19.0) for one person or a whole team (whoever is a member when it is read), over a date range,
/// on chosen weekdays (none = every day), for some hours a day or (no hours) the whole working day.
/// </summary>
public class AvailabilityEntry : TenantEntity
{
    public Guid TypeId { get; set; }
    public AvailabilityType? Type { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? TeamId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    public decimal? Hours { get; set; }
    public string? Notes { get; set; }
}

/// <summary>The one rule for a person's hours on a day (0.19.0): used by the board, its warnings, the timesheet and the phone.</summary>
public static class Availability
{
    /// <summary>Whether an entry from <paramref name="from"/> to <paramref name="to"/> on <paramref name="days"/> (none = every day) covers <paramref name="day"/>.</summary>
    public static bool Covers(DateOnly from, DateOnly to, IReadOnlyCollection<DayOfWeek> days, DateOnly day) =>
        day >= from && day <= to && (days.Count == 0 || days.Contains(day.DayOfWeek));

    /// <summary>
    /// Hours available: the pattern's hours less time away (an entry without hours takes the whole pattern day; never below 0),
    /// plus extra time.
    /// </summary>
    public static decimal Available(decimal patternHours, IEnumerable<(AvailabilityKind Kind, decimal? Hours)> slots)
    {
        var list = slots.ToList();
        var away = list.Where(s => s.Kind == AvailabilityKind.Away).Sum(s => s.Hours ?? patternHours);
        var extra = list.Where(s => s.Kind == AvailabilityKind.Extra).Sum(s => s.Hours ?? 0);
        return Math.Round(Math.Max(0, patternHours - away) + extra, 2);
    }
}
