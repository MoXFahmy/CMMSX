using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Core.Pm;

public enum PmScheduleType { FixedInterval = 1, DayOfWeekInMonth = 2, DateOfMonth = 3, Meter = 4 }
/// <summary>Scheduled: next = last target date + interval. Actual: next = last work order completion + interval (4CSQL "Schedule"/"Actual").</summary>
public enum PmBasis { Scheduled = 1, Actual = 2 }
public enum FrequencyUnit { Day = 1, Week = 2, Month = 3, Year = 4 }
public enum MeterTrigger { Incremental = 1, Threshold = 2 }
public enum ReadingSource { Manual = 1, WorkOrder = 2, Import = 3 }

/// <summary>Reusable PM instructions (legacy pmchecklist + checklistelements).</summary>
public class PmTask : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? TradeId { get; set; }
    public decimal? EstimatedHours { get; set; }
    public string? SafetyNotes { get; set; }
    public bool IsActive { get; set; } = true;
    public List<PmTaskStep> Steps { get; set; } = [];
}

public class PmTaskStep : Entity
{
    public Guid PmTaskId { get; set; }
    public int Sequence { get; set; }
    public required string Description { get; set; }
}

/// <summary>When and where a PM task is performed (legacy pmschedule / pmmeters).</summary>
public class PmSchedule : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid? TradeId { get; set; }
    public WorkPriority Priority { get; set; } = WorkPriority.Medium;
    public List<Guid> AssigneeIds { get; set; } = [];
    public Guid PmTaskId { get; set; }
    public PmTask? PmTask { get; set; }
    public List<PmScheduleTask> ExtraTasks { get; set; } = [];

    public PmScheduleType Type { get; set; } = PmScheduleType.FixedInterval;
    public PmBasis Basis { get; set; } = PmBasis.Scheduled;
    public int Frequency { get; set; } = 1;
    public FrequencyUnit Unit { get; set; } = FrequencyUnit.Month;
    /// <summary>1–4, or 5 for the last week of the month (DayOfWeekInMonth).</summary>
    public int? WeekOfMonth { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
    /// <summary>1–31; clamped to the month's last day (DateOfMonth).</summary>
    public int? DayOfMonth { get; set; }
    /// <summary>Days allowed to complete the work: due date = target start + (period − 1).</summary>
    public int WorkPeriodDays { get; set; } = 1;
    /// <summary>DateOfMonth only: the work period is the whole month (legacy "Monthly").</summary>
    public bool WholeMonth { get; set; }
    /// <summary>Generate the work order this many days before the target start.</summary>
    public int LeadDays { get; set; } = 7;

    public DateOnly StartDate { get; set; }
    /// <summary>Target start of the next occurrence; null while an "Actual" schedule waits for its open work order.</summary>
    public DateOnly? NextDueDate { get; set; }
    public DateOnly? LastDueDate { get; set; }
    public DateTimeOffset? LastGeneratedAt { get; set; }
    public Guid? LastWorkOrderId { get; set; }
    public int GenerationCount { get; set; }

    public Guid? MeterId { get; set; }
    public Meter? Meter { get; set; }
    public MeterTrigger? MeterTrigger { get; set; }
    public decimal? MeterInterval { get; set; }
    public decimal? NextDueReading { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }
    public Guid? LowerTaskId { get; set; }
    public Guid? UpperTaskId { get; set; }
    /// <summary>Meter schedules: also generate on the time interval, whichever comes first.</summary>
    public bool TimeBased { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Multiple PM: on every Nth generation use this task instead of the routine one (e.g. monthly instead of weekly).</summary>
public class PmScheduleTask
{
    public Guid PmScheduleId { get; set; }
    public Guid PmTaskId { get; set; }
    public int EveryNth { get; set; }
}

public class Meter : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public required string Unit { get; set; }
    public decimal? CurrentReading { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
    /// <summary>Readings rising faster than this per day are flagged as probable typos (legacy max_daily_reading).</summary>
    public decimal? MaxDailyIncrement { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MeterReading : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid MeterId { get; set; }
    public decimal Value { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    public string? Note { get; set; }
    public ReadingSource Source { get; set; } = ReadingSource.Manual;
    public Guid? WorkOrderId { get; set; }
    public string? ByName { get; set; }
    public string? LegacyRef { get; set; }
}

/// <summary>Pure date arithmetic for PM schedules — no database access, fully unit-testable.</summary>
public static class PmCalendar
{
    public static DateOnly AddInterval(DateOnly d, int frequency, FrequencyUnit unit) => unit switch
    {
        FrequencyUnit.Day => d.AddDays(frequency),
        FrequencyUnit.Week => d.AddDays(7 * frequency),
        FrequencyUnit.Month => d.AddMonths(frequency),
        _ => d.AddYears(frequency),
    };

    /// <summary>The n-th (1–4) or last (5) given weekday of a month.</summary>
    public static DateOnly NthWeekday(int year, int month, int week, DayOfWeek day)
    {
        if (week >= 5)
        {
            var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            return last.AddDays(-(((int)last.DayOfWeek - (int)day + 7) % 7));
        }
        var first = new DateOnly(year, month, 1);
        return first.AddDays(((int)day - (int)first.DayOfWeek + 7) % 7 + 7 * (Math.Clamp(week, 1, 4) - 1));
    }

    public static DateOnly ClampedDay(int year, int month, int day) => new(year, month, Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(year, month)));

    /// <summary>First occurrence on or after <paramref name="start"/>.</summary>
    public static DateOnly FirstOnOrAfter(PmSchedule s, DateOnly start)
    {
        switch (s.Type)
        {
            case PmScheduleType.DayOfWeekInMonth:
                for (var m = 0; m < 24; m++)
                {
                    var month = new DateOnly(start.Year, start.Month, 1).AddMonths(m);
                    var d = NthWeekday(month.Year, month.Month, s.WeekOfMonth ?? 1, s.DayOfWeek ?? System.DayOfWeek.Monday);
                    if (d >= start) return d;
                }
                return start;
            case PmScheduleType.DateOfMonth:
                var same = ClampedDay(start.Year, start.Month, s.DayOfMonth ?? start.Day);
                if (same >= start) return same;
                var next = new DateOnly(start.Year, start.Month, 1).AddMonths(1);
                return ClampedDay(next.Year, next.Month, s.DayOfMonth ?? start.Day);
            default:
                return start;
        }
    }

    /// <summary>The occurrence after <paramref name="from"/> (a previous target date, or a completion date for Actual schedules).</summary>
    public static DateOnly Next(PmSchedule s, DateOnly from)
    {
        var freq = Math.Max(1, s.Frequency);
        switch (s.Type)
        {
            case PmScheduleType.DayOfWeekInMonth:
            {
                var m = new DateOnly(from.Year, from.Month, 1).AddMonths(freq);
                return NthWeekday(m.Year, m.Month, s.WeekOfMonth ?? 1, s.DayOfWeek ?? System.DayOfWeek.Monday);
            }
            case PmScheduleType.DateOfMonth:
            {
                var m = new DateOnly(from.Year, from.Month, 1).AddMonths(freq);
                return ClampedDay(m.Year, m.Month, s.DayOfMonth ?? from.Day);
            }
            default:
                return AddInterval(from, freq, s.Unit);
        }
    }

    public static DateOnly DueDate(PmSchedule s, DateOnly targetStart) =>
        s.Type == PmScheduleType.DateOfMonth && s.WholeMonth ? targetStart.AddMonths(1).AddDays(-1) : targetStart.AddDays(Math.Max(1, s.WorkPeriodDays) - 1);

    /// <summary>Upcoming target dates from NextDueDate. Actual-basis schedules only know their next date.</summary>
    public static IEnumerable<DateOnly> Forecast(PmSchedule s, DateOnly to, int max = 60)
    {
        if (s.NextDueDate is not { } d || !UsesCalendar(s)) yield break;
        for (var i = 0; i < max && d <= to; i++)
        {
            yield return d;
            if (s.Basis == PmBasis.Actual) yield break;
            d = Next(s, d);
        }
    }

    public static bool UsesCalendar(PmSchedule s) => s.Type != PmScheduleType.Meter || s.TimeBased;

    /// <summary>Multiple PM: the task with the largest "every Nth" that divides the generation count wins.</summary>
    public static Guid TaskFor(PmSchedule s, int generationNumber) =>
        s.ExtraTasks.Where(t => t.EveryNth > 1 && generationNumber % t.EveryNth == 0).OrderByDescending(t => t.EveryNth).Select(t => (Guid?)t.PmTaskId).FirstOrDefault() ?? s.PmTaskId;

    public static string Describe(PmSchedule s)
    {
        string Every(int n, string unit) => n == 1 ? $"Every {unit}" : $"Every {n} {unit}s";
        var weeks = new[] { "", "1st", "2nd", "3rd", "4th", "last" };
        return s.Type switch
        {
            PmScheduleType.FixedInterval => Every(s.Frequency, s.Unit.ToString().ToLowerInvariant()) + (s.Basis == PmBasis.Actual ? " after completion" : ""),
            PmScheduleType.DayOfWeekInMonth => $"{weeks[Math.Clamp(s.WeekOfMonth ?? 1, 1, 5)]} {s.DayOfWeek} · {Every(s.Frequency, "month").ToLowerInvariant()}",
            PmScheduleType.DateOfMonth => $"Day {s.DayOfMonth} · {Every(s.Frequency, "month").ToLowerInvariant()}",
            _ => s.MeterTrigger == Pm.MeterTrigger.Threshold
                ? (s.LowerLimit, s.UpperLimit) switch
                {
                    ({ } lo, { } hi) => $"When outside {lo:0.###}–{hi:0.###}",
                    ({ } lo, null) => $"When below {lo:0.###}",
                    (null, { } hi) => $"When above {hi:0.###}",
                    _ => "Threshold",
                }
                : $"Every {s.MeterInterval:0.###}" + (s.TimeBased ? $" or {Every(s.Frequency, s.Unit.ToString().ToLowerInvariant()).ToLowerInvariant()}" : ""),
        };
    }
}
