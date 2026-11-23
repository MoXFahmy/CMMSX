using FourC.Cmms.Core.Planning;

namespace FourC.Cmms.Tests;

/// <summary>0.19.0: the one rule for a person's hours on a day — the working pattern, less time away, plus extra time.</summary>
public class AvailabilityMathTests
{
    [Fact]
    public void Whole_day_away_leaves_nothing() => Assert.Equal(0m, Availability.Available(8, [(AvailabilityKind.Away, null)]));

    [Fact]
    public void Part_day_away_and_overlaps_never_go_below_zero()
    {
        Assert.Equal(5m, Availability.Available(8, [(AvailabilityKind.Away, 3m)]));
        Assert.Equal(0m, Availability.Available(8, [(AvailabilityKind.Away, 3m), (AvailabilityKind.Away, 6m)]));
    }

    [Fact]
    public void Extra_time_adds_even_on_a_day_off()
    {
        Assert.Equal(6m, Availability.Available(0, [(AvailabilityKind.Extra, 6m)]));
        Assert.Equal(2m, Availability.Available(8, [(AvailabilityKind.Away, null), (AvailabilityKind.Extra, 2m)]));
    }

    [Fact]
    public void Repeating_entry_covers_only_its_weekdays_in_range()
    {
        var from = new DateOnly(2026, 12, 1);
        var to = new DateOnly(2026, 12, 31);
        Assert.True(Availability.Covers(from, to, [DayOfWeek.Sunday], new DateOnly(2026, 12, 6)));
        Assert.False(Availability.Covers(from, to, [DayOfWeek.Sunday], new DateOnly(2026, 12, 7)));
        Assert.False(Availability.Covers(from, to, [], new DateOnly(2027, 1, 1)));
        Assert.True(Availability.Covers(from, to, [], new DateOnly(2026, 12, 15)));
    }
}
