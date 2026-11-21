using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Assets;

public enum DowntimeSource { Request = 1, WorkOrder = 2, Manual = 3 }

/// <summary>One real stop of an asset: from when it went down until it ran again (no end while still down).</summary>
public class DowntimeEvent : TenantEntity
{
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Reason { get; set; }
    public DowntimeSource Source { get; set; }
    /// <summary>The number of the request or work order that started the stop, if any.</summary>
    public string? SourceNumber { get; set; }
    public Guid? EndedById { get; set; }
}

/// <summary>A stop for measuring; no end means the asset is still down.</summary>
public sealed record StopSpan(DateTimeOffset Start, DateTimeOffset? End);

public sealed record DowntimeMeasure(decimal PlannedHours, decimal DownHours);

/// <summary>
/// Planned and stopped hours of one asset over a period, counted by local day in the organization's time zone:
/// only the asset's running days count, each for its hours a day, and a day's downtime never exceeds them.
/// </summary>
public static class DowntimeMath
{
    public static DowntimeMeasure Measure(IEnumerable<StopSpan> stops, IEnumerable<(DateOnly Day, decimal Hours)> typedHours,
        DateOnly from, DateOnly to, DateTimeOffset now, decimal hoursPerDay, IReadOnlyCollection<DayOfWeek> runsOn, TimeZoneInfo tz)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var last = to < today ? to : today;
        if (last < from || hoursPerDay <= 0) return new(0, 0);

        var windowStart = Midnight(from, tz);
        var windowEnd = Min(Midnight(last.AddDays(1), tz), now);
        var clipped = stops
            .Select(s => (Start: Max(s.Start, windowStart), End: Min(s.End ?? now, windowEnd)))
            .Where(s => s.End > s.Start)
            .OrderBy(s => s.Start)
            .ToList();

        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var s in clipped)
        {
            if (merged.Count > 0 && s.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Max(merged[^1].End, s.End));
            else merged.Add(s);
        }

        var perDay = new Dictionary<DateOnly, decimal>();
        foreach (var (start, end) in merged)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, tz).DateTime);
            var cursor = start;
            while (cursor < end)
            {
                var next = Min(Midnight(day.AddDays(1), tz), end);
                perDay[day] = perDay.GetValueOrDefault(day) + (decimal)(next - cursor).TotalHours;
                cursor = next;
                day = day.AddDays(1);
            }
        }
        foreach (var (day, hours) in typedHours)
            if (day >= from && day <= last && hours > 0) perDay[day] = perDay.GetValueOrDefault(day) + hours;

        decimal planned = 0, down = 0;
        for (var d = from; d <= last; d = d.AddDays(1))
        {
            if (!runsOn.Contains(d.DayOfWeek)) continue;
            planned += hoursPerDay;
            down += Math.Min(hoursPerDay, perDay.GetValueOrDefault(d));
        }
        return new(Math.Round(planned, 2), Math.Round(down, 2));
    }

    /// <summary>The start of a local day as an instant.</summary>
    public static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo tz)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
