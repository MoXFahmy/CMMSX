using FourC.Cmms.Core.Pm;

namespace FourC.Cmms.Tests;

public class PmCalendarTests
{
    private static PmSchedule S(Action<PmSchedule> f) { var s = new PmSchedule { Code = "x", Name = "x" }; f(s); return s; }
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Schedules_are_described_in_arabic_with_the_right_number_forms()
    {
        PmSchedule Every(int n, FrequencyUnit u) => S(x => { x.Type = PmScheduleType.FixedInterval; x.Frequency = n; x.Unit = u; });
        Assert.Equal("كل شهر", PmCalendar.Describe(Every(1, FrequencyUnit.Month), "ar"));
        Assert.Equal("كل شهرين", PmCalendar.Describe(Every(2, FrequencyUnit.Month), "ar"));
        Assert.Equal("كل 3 أشهر", PmCalendar.Describe(Every(3, FrequencyUnit.Month), "ar"));
        Assert.Equal("كل 12 شهرًا", PmCalendar.Describe(Every(12, FrequencyUnit.Month), "ar"));
        Assert.Equal("كل أسبوعين", PmCalendar.Describe(Every(2, FrequencyUnit.Week), "ar"));
        Assert.Equal("كل 10 أيام", PmCalendar.Describe(Every(10, FrequencyUnit.Day), "ar"));
        Assert.Equal("كل سنة بعد الإنجاز", PmCalendar.Describe(S(x => { x.Type = PmScheduleType.FixedInterval; x.Frequency = 1; x.Unit = FrequencyUnit.Year; x.Basis = PmBasis.Actual; }), "ar"));
        Assert.Equal("الإثنين الأول · كل شهر", PmCalendar.Describe(S(x => { x.Type = PmScheduleType.DayOfWeekInMonth; x.WeekOfMonth = 1; x.DayOfWeek = DayOfWeek.Monday; x.Frequency = 1; }), "ar"));
        Assert.Equal("اليوم 15 · كل 3 أشهر", PmCalendar.Describe(S(x => { x.Type = PmScheduleType.DateOfMonth; x.DayOfMonth = 15; x.Frequency = 3; }), "ar"));
        Assert.Equal("كل 500 أو كل 6 أشهر", PmCalendar.Describe(S(x => { x.Type = PmScheduleType.Meter; x.MeterInterval = 500; x.TimeBased = true; x.Frequency = 6; x.Unit = FrequencyUnit.Month; }), "ar"));
        Assert.Equal("عند الخروج عن 10–20", PmCalendar.Describe(S(x => { x.Type = PmScheduleType.Meter; x.MeterTrigger = MeterTrigger.Threshold; x.LowerLimit = 10; x.UpperLimit = 20; }), "ar"));
        // English is unchanged.
        Assert.Equal("Every 3 months", PmCalendar.Describe(Every(3, FrequencyUnit.Month)));
    }

    [Fact]
    public void A_due_date_on_a_non_working_day_moves_to_a_working_day()
    {
        DayOfWeek[] weekend = [DayOfWeek.Friday, DayOfWeek.Saturday];
        // Two days from Thursday 19 November ends on Friday: due on Sunday instead.
        var period = S(x => { x.Type = PmScheduleType.FixedInterval; x.WorkPeriodDays = 2; });
        Assert.Equal(D(2026, 11, 22), PmCalendar.DueDate(period, D(2026, 11, 19), weekend));
        // "Whole month" stays in its month: Friday 31 July becomes Thursday 30 July.
        var month = S(x => { x.Type = PmScheduleType.DateOfMonth; x.WholeMonth = true; });
        Assert.Equal(D(2026, 7, 30), PmCalendar.DueDate(month, D(2026, 7, 1), weekend));
        // No non-working days: as before.
        Assert.Equal(D(2026, 11, 20), PmCalendar.DueDate(period, D(2026, 11, 19), []));
    }

    [Fact]
    public void NextWorkingDay_skips_listed_weekdays()
    {
        var weekend = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday };
        Assert.Equal(D(2026, 11, 23), PmCalendar.NextWorkingDay(D(2026, 11, 21), weekend)); // Saturday → Monday
        Assert.Equal(D(2026, 11, 24), PmCalendar.NextWorkingDay(D(2026, 11, 24), weekend)); // Tuesday stays
        Assert.Equal(D(2026, 11, 21), PmCalendar.NextWorkingDay(D(2026, 11, 21), Enum.GetValues<DayOfWeek>())); // no working day: unchanged
    }

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
    // ---- PM rounds (multi-asset schedules) ----
    private static readonly Guid Site = Guid.NewGuid(), Other = Guid.NewGuid();
    private static RoundAsset R(Guid id, int seq, string? path = null, bool available = true, Guid? site = null) =>
        new(id, seq, site ?? Site, path, path is null ? null : Guid.Parse(path.TrimEnd('/').Split('/')[^1]), available);
    private static string P(params Guid[] ids) => "/" + string.Concat(ids.Select(i => i.ToString("N") + "/"));

    [Fact]
    public void Round_covers_available_assets_of_the_site_in_list_order()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), gone = Guid.NewGuid(), moved = Guid.NewGuid();
        var covered = PmRound.Covered([R(c, 3), R(gone, 4, available: false), R(a, 1), R(moved, 5, site: Other), R(b, 2)], Site);
        Assert.Equal([a, b, c], covered.Select(x => x.Id));
    }

    [Fact]
    public void Round_steps_repeat_per_asset_with_continuous_sequence()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var steps = PmRound.Steps([R(a, 1), R(b, 2)], ["Look", "Listen"]);
        Assert.Equal([1, 2, 3, 4], steps.Select(s => s.Sequence));
        Assert.Equal([a, a, b, b], steps.Select(s => s.AssetId));
        Assert.Equal(["Look", "Listen", "Look", "Listen"], steps.Select(s => s.Description));
    }

    [Fact]
    public void Round_common_location_is_the_deepest_shared_path()
    {
        Guid x = Guid.NewGuid(), y = Guid.NewGuid(), la = Guid.NewGuid(), lb = Guid.NewGuid(), z = Guid.NewGuid();
        Assert.Equal(y, PmRound.CommonLocationId([R(Guid.NewGuid(), 1, P(x, y, la)), R(Guid.NewGuid(), 2, P(x, y, lb))]));
        Assert.Equal(la, PmRound.CommonLocationId([R(Guid.NewGuid(), 1, P(x, y, la)), R(Guid.NewGuid(), 2, P(x, y, la))]));
        Assert.Null(PmRound.CommonLocationId([R(Guid.NewGuid(), 1, P(x)), R(Guid.NewGuid(), 2, P(z))]));
        Assert.Null(PmRound.CommonLocationId([R(Guid.NewGuid(), 1, P(x, y)), R(Guid.NewGuid(), 2)]));
        Assert.Null(PmRound.CommonLocationId([]));
    }
}
