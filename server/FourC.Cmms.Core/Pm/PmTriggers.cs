namespace FourC.Cmms.Core.Pm;

/// <summary>
/// A PM's next date from all its date triggers (0.12.0): its own rule (as before) and its shared calendars, whichever comes
/// first. Pure date arithmetic, no database access.
/// </summary>
public static class PmTriggers
{
    /// <summary>The calendar's first date after <paramref name="after"/>; null when it has none (inactive, no anchor, dates all past).</summary>
    public static DateOnly? CalendarNext(ScheduleCalendar c, DateOnly after)
    {
        if (!c.IsActive) return null;
        if (c.Kind == CalendarKind.Dates) return c.Dates.Where(d => d > after).Order().Select(d => (DateOnly?)d).FirstOrDefault();
        if (c.AnchorDate is not { } anchor) return null;
        if (anchor > after) return anchor;
        var freq = Math.Max(1, c.Frequency);
        if (c.Unit is FrequencyUnit.Day or FrequencyUnit.Week)
        {
            var step = c.Unit == FrequencyUnit.Day ? freq : 7 * freq;
            var k = (after.DayNumber - anchor.DayNumber) / step + 1;
            return anchor.AddDays(k * step);
        }
        // Months and years from the anchor each time, so a 31st stays the month's last day instead of drifting.
        var months = c.Unit == FrequencyUnit.Month ? freq : 12 * freq;
        var guess = Math.Max(1, ((after.Year - anchor.Year) * 12 + after.Month - anchor.Month) / months);
        for (var n = guess; n < guess + 3; n++)
        {
            var d = anchor.AddMonths(n * months);
            if (d > after) return d;
        }
        return anchor.AddMonths((guess + 3) * months);
    }

    /// <summary>The PM's next date after <paramref name="after"/>: the earliest of its own rule (unless its type is Calendar) and its calendars.</summary>
    public static DateOnly? Next(PmSchedule s, DateOnly after, IReadOnlyList<ScheduleCalendar> calendars)
    {
        DateOnly? best = OwnRule(s) ? PmCalendar.Next(s, after) : null;
        foreach (var c in calendars)
            if (CalendarNext(c, after) is { } d && (best is null || d < best)) best = d;
        return best;
    }

    /// <summary>The PM's first date on or after <paramref name="start"/>.</summary>
    public static DateOnly? First(PmSchedule s, DateOnly start, IReadOnlyList<ScheduleCalendar> calendars)
    {
        DateOnly? best = OwnRule(s) ? PmCalendar.FirstOnOrAfter(s, start) : null;
        foreach (var c in calendars)
            if (CalendarNext(c, start.AddDays(-1)) is { } d && (best is null || d < best)) best = d;
        return best;
    }

    /// <summary>Even spread: the k-th of <paramref name="parts"/> blocks of <paramref name="assetCount"/> assets in list order, as equal as possible.</summary>
    public static (int Start, int Count) Spread(int assetCount, int parts, int k)
    {
        var size = assetCount / parts;
        var extra = assetCount % parts;
        return (k * size + Math.Min(k, extra), size + (k < extra ? 1 : 0));
    }

    /// <summary>Even spread: the start of the k-th of <paramref name="parts"/> occurrences in the period from <paramref name="cycleStart"/> to <paramref name="nextCycle"/>.</summary>
    public static DateOnly SpreadDate(DateOnly cycleStart, DateOnly nextCycle, int parts, int k) =>
        cycleStart.AddDays((int)((long)k * (nextCycle.DayNumber - cycleStart.DayNumber) / parts));

    /// <summary>The PM spreads its assets over each period (0.12.0).</summary>
    public static bool Spreads(PmSchedule s) => s.SpreadParts is >= 2 && s.Assets.Count > 0;

    /// <summary>Upcoming target dates from the PM's next date (Actual-basis PMs only know their next one).</summary>
    public static IEnumerable<DateOnly> Forecast(PmSchedule s, DateOnly to, int max, IReadOnlyList<ScheduleCalendar> calendars)
    {
        if (s.NextDueDate is not { } d || !UsesDates(s, calendars.Count)) yield break;
        if (Spreads(s))
        {
            // Each period's parts in turn, from where the PM is now.
            var parts = s.SpreadParts!.Value;
            var cycle = s.CycleStart ?? d;
            var k = s.CycleStart is null ? 0 : s.SpreadIndex;
            for (var i = 0; i < max; i++)
            {
                var next = PmCalendar.Next(s, cycle);
                var date = SpreadDate(cycle, next, parts, k);
                if (date > to) yield break;
                yield return date;
                if (++k == parts) { k = 0; cycle = next; }
            }
            yield break;
        }
        for (var i = 0; i < max && d <= to; i++)
        {
            yield return d;
            if (s.Basis == PmBasis.Actual || Next(s, d, calendars) is not { } next) yield break;
            d = next;
        }
    }

    /// <summary>The PM generates by date: its own rule or any calendar.</summary>
    public static bool UsesDates(PmSchedule s, int calendarCount) => OwnRule(s) || calendarCount > 0;

    /// <summary>The PM has a date rule of its own (not only calendars, and not a meter without "or time").</summary>
    public static bool OwnRule(PmSchedule s) => s.Type != PmScheduleType.Calendar && PmCalendar.UsesCalendar(s);
}
