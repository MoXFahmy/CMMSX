using FourC.Cmms.Core.Tenancy;

namespace FourC.Cmms.Core.Work;

/// <summary>A priority's targets: respond within / complete within, in minutes (empty = no target).</summary>
public sealed class SlaTarget
{
    public WorkPriority Priority { get; set; }
    public int? RespondMinutes { get; set; }
    public int? CompleteMinutes { get; set; }
}

public enum SlaState { None, OnTrack, DueSoon, Late, Met, Missed }

/// <summary>
/// Deadlines from targets. The clock counts every hour (no working-hours calendar) and does not pause on hold. A date-only
/// deadline means the end of that day in the organization's time zone.
/// </summary>
public static class Sla
{
    /// <summary>Left on a hand-set deadline below which it is "due soon".</summary>
    public static readonly TimeSpan DueSoonWithoutTarget = TimeSpan.FromHours(2);

    public static TimeZoneInfo Zone(string? id) =>
        id is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : TimeZoneInfo.Utc;

    /// <summary>
    /// The last second of <paramref name="day"/> in <paramref name="zone"/>. When clocks go back and 23:59:59 happens twice, the
    /// later one; when they jump forward over the end of the day, the last second before the jump.
    /// </summary>
    public static DateTimeOffset EndOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
        for (var i = 0; i < 24 * 60 && zone.IsInvalidTime(local); i++) local = local.AddMinutes(-1); // skipped: step back out of the gap
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly LocalDay(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    public static SlaTarget? For(TenantSettings s, WorkPriority p) => s.SlaTargets.FirstOrDefault(t => t.Priority == p);

    public static DateTimeOffset? RespondBy(TenantSettings s, WorkPriority p, DateTimeOffset raisedAt) =>
        For(s, p)?.RespondMinutes is { } m ? raisedAt.AddMinutes(m) : null;

    public static DateTimeOffset? CompleteBy(TenantSettings s, WorkPriority p, DateTimeOffset raisedAt) =>
        For(s, p)?.CompleteMinutes is { } m ? raisedAt.AddMinutes(m) : null;

    /// <summary>
    /// Where a deadline stands. Once done: met or missed. Before: late when past, due soon when less than a quarter of
    /// <paramref name="window"/> is left (or <see cref="DueSoonWithoutTarget"/> when there is no window), else on track.
    /// </summary>
    public static SlaState State(DateTimeOffset? deadline, DateTimeOffset? doneAt, DateTimeOffset now, TimeSpan? window)
    {
        if (deadline is not { } d) return SlaState.None;
        if (doneAt is { } done) return done <= d ? SlaState.Met : SlaState.Missed;
        if (now > d) return SlaState.Late;
        var soon = window is { } w ? w / 4 : DueSoonWithoutTarget;
        return d - now < soon ? SlaState.DueSoon : SlaState.OnTrack;
    }
}
