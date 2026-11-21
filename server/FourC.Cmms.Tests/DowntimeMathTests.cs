using FourC.Cmms.Core.Assets;

namespace FourC.Cmms.Tests;

public class DowntimeMathTests
{
    private static readonly DayOfWeek[] AllDays = Enum.GetValues<DayOfWeek>();
    private static readonly DayOfWeek[] SunToThu = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];
    private static readonly (DateOnly, decimal)[] NoTyped = [];
    private static DateTimeOffset U(int d, int h, int m = 11) => new(2026, m, d, h, 0, 0, TimeSpan.Zero);
    private static DateOnly D(int d, int m = 11) => new(2026, m, d);
    private static readonly DateTimeOffset Later = U(30, 0);

    [Fact]
    public void Only_running_days_count()
    {
        // Thursday 19th 20:00 to Sunday 22nd 08:00; the week of the 15th (Sunday) to the 21st (Saturday).
        var r = DowntimeMath.Measure([new StopSpan(U(19, 20), U(22, 8))], NoTyped, D(15), D(21), Later, 16, SunToThu, TimeZoneInfo.Utc);
        Assert.Equal(80m, r.PlannedHours);
        Assert.Equal(4m, r.DownHours);
    }

    [Fact]
    public void Overlapping_stops_count_once()
    {
        var r = DowntimeMath.Measure([new StopSpan(U(18, 10), U(18, 14)), new StopSpan(U(18, 12), U(18, 16))], NoTyped, D(18), D(18), Later, 24, AllDays, TimeZoneInfo.Utc);
        Assert.Equal(24m, r.PlannedHours);
        Assert.Equal(6m, r.DownHours);
    }

    [Fact]
    public void Typed_hours_count_on_their_day_and_are_capped()
    {
        var r = DowntimeMath.Measure([], [(D(19), 30m)], D(15), D(21), Later, 24, AllDays, TimeZoneInfo.Utc);
        Assert.Equal(168m, r.PlannedHours);
        Assert.Equal(24m, r.DownHours);
    }

    [Fact]
    public void Days_split_in_the_organizations_time_zone()
    {
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        // 21:00Z on the 18th to 03:00Z on the 19th is 23:00 to 05:00 in Cairo (UTC+2 in November).
        var r = DowntimeMath.Measure([new StopSpan(U(18, 21), U(19, 3))], NoTyped, D(19), D(19), Later, 24, AllDays, cairo);
        Assert.Equal(5m, r.DownHours);
    }

    [Fact]
    public void An_open_stop_counts_up_to_the_end_of_the_period()
    {
        var r = DowntimeMath.Measure([new StopSpan(U(20, 10), null)], NoTyped, D(20), D(20), U(20, 12), 24, AllDays, TimeZoneInfo.Utc);
        Assert.Equal(2m, r.DownHours);
    }

    [Fact]
    public void A_stop_before_the_period_counts_only_inside_it()
    {
        var r = DowntimeMath.Measure([new StopSpan(U(1, 0), U(16, 6))], NoTyped, D(16), D(16), Later, 24, AllDays, TimeZoneInfo.Utc);
        Assert.Equal(6m, r.DownHours);
    }

    [Fact]
    public void Days_after_now_are_not_planned()
    {
        // A report for the whole month, run on the 20th: only the 1st to the 20th are planned time.
        var r = DowntimeMath.Measure([], NoTyped, D(1), D(30), U(20, 12), 10, AllDays, TimeZoneInfo.Utc);
        Assert.Equal(200m, r.PlannedHours);
        Assert.Equal(0m, r.DownHours);
    }
}
