using FourC.Cmms.Core.Pm;

namespace FourC.Cmms.Tests;

public class PmCalendarTests
{
    private static PmSchedule S(Action<PmSchedule> f) { var s = new PmSchedule { Code = "x", Name = "x" }; f(s); return s; }
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Fixed_interval_adds_calendar_units()
    {
        var weekly = S(s => { s.Unit = FrequencyUnit.Week; s.Frequency = 2; });
        Assert.Equal(D(2026, 1, 15), PmCalendar.Next(weekly, D(2026, 1, 1)));
        var monthly = S(s => { s.Unit = FrequencyUnit.Month; s.Frequency = 1; });
        Assert.Equal(D(2026, 2, 28), PmCalendar.Next(monthly, D(2026, 1, 31))); // month-end clamps
        var days = S(s => { s.Unit = FrequencyUnit.Day; s.Frequency = 28; });   // legacy 4CSQL "month" = 28 days
        Assert.Equal(D(2026, 1, 29), PmCalendar.Next(days, D(2026, 1, 1)));
    }

    [Theory]
    [InlineData(2, DayOfWeek.Wednesday, 2026, 3, 11)] // 2nd Wednesday of March 2026
    [InlineData(1, DayOfWeek.Sunday, 2026, 3, 1)]
    [InlineData(5, DayOfWeek.Friday, 2026, 3, 27)]    // last Friday
    [InlineData(5, DayOfWeek.Tuesday, 2026, 3, 31)]
    public void Nth_weekday_of_month(int week, DayOfWeek day, int y, int m, int expected) =>
        Assert.Equal(D(y, m, expected), PmCalendar.NthWeekday(y, m, week, day));

    [Fact]
    public void Day_of_week_in_month_schedule_steps_by_months()
    {
        var s = S(x => { x.Type = PmScheduleType.DayOfWeekInMonth; x.WeekOfMonth = 2; x.DayOfWeek = DayOfWeek.Wednesday; x.Frequency = 3; });
        var first = PmCalendar.FirstOnOrAfter(s, D(2026, 3, 12)); // 2nd Wed of March already passed
        Assert.Equal(D(2026, 4, 8), first);
        Assert.Equal(D(2026, 7, 8), PmCalendar.Next(s, first));
    }

    [Fact]
    public void Date_of_month_clamps_and_supports_whole_month_period()
    {
        var s = S(x => { x.Type = PmScheduleType.DateOfMonth; x.DayOfMonth = 31; x.Frequency = 1; x.WholeMonth = true; });
        Assert.Equal(D(2026, 2, 28), PmCalendar.Next(s, D(2026, 1, 31)));
        Assert.Equal(D(2028, 2, 29), PmCalendar.Next(s, D(2028, 1, 31))); // leap year
        Assert.Equal(D(2026, 2, 14), PmCalendar.DueDate(S(x => { x.Type = PmScheduleType.DateOfMonth; x.WholeMonth = true; }), D(2026, 1, 15)));
        Assert.Equal(D(2026, 1, 17), PmCalendar.DueDate(S(x => x.WorkPeriodDays = 3), D(2026, 1, 15)));
    }

    [Fact]
    public void Multiple_pm_uses_heavier_task_on_every_nth_generation()
    {
        // Guide example: weekly W001, monthly M001 every 4th generation.
        var weekly = Guid.NewGuid(); var monthly = Guid.NewGuid(); var annual = Guid.NewGuid();
        var s = S(x => { x.PmTaskId = weekly; x.ExtraTasks = [new() { PmTaskId = monthly, EveryNth = 4 }, new() { PmTaskId = annual, EveryNth = 52 }]; });
        var seq = Enumerable.Range(1, 8).Select(n => PmCalendar.TaskFor(s, n)).ToList();
        Assert.Equal([weekly, weekly, weekly, monthly, weekly, weekly, weekly, monthly], seq);
        Assert.Equal(annual, PmCalendar.TaskFor(s, 52));
    }

    [Fact]
    public void Forecast_stops_after_one_occurrence_for_actual_basis()
    {
        var sched = S(x => { x.Unit = FrequencyUnit.Week; x.NextDueDate = D(2026, 1, 1); });
        Assert.Equal(5, PmCalendar.Forecast(sched, D(2026, 1, 31)).Count());
        sched.Basis = PmBasis.Actual;
        Assert.Single(PmCalendar.Forecast(sched, D(2026, 12, 31)));
    }
}
