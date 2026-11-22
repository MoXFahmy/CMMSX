using FourC.Cmms.Core.Pm;

namespace FourC.Cmms.Tests;

/// <summary>0.12.0 rules that need no database.</summary>
public class PmPureTests
{
    [Fact]
    public void Round_steps_per_asset_template()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var covered = new[] { new RoundAsset(a, 1, Guid.Empty, null, null, true), new RoundAsset(b, 2, Guid.Empty, null, null, true) };
        var steps = PmRound.Steps(covered, id => id == b ? ["Special", "Extra"] : ["Routine"]);
        Assert.Equal([new RoundStep(a, 1, "Routine"), new RoundStep(b, 2, "Special"), new RoundStep(b, 3, "Extra")], steps);
    }

    private static ScheduleCalendar Cal(CalendarKind kind, DateOnly? anchor = null, int every = 1, FrequencyUnit unit = FrequencyUnit.Month, DateOnly[]? dates = null, bool active = true) =>
        new() { Code = "C", Name = "C", Kind = kind, AnchorDate = anchor, Frequency = every, Unit = unit, Dates = [.. dates ?? []], IsActive = active };

    private static PmSchedule Monthly(PmScheduleType type = PmScheduleType.FixedInterval) =>
        new() { Code = "P", Name = "P", Type = type, Frequency = 1, Unit = FrequencyUnit.Month, StartDate = new(2026, 1, 1) };

    [Fact]
    public void Interval_calendar_from_an_anchor()
    {
        var c = Cal(CalendarKind.Interval, new(2026, 1, 15), every: 2);
        Assert.Equal(new DateOnly(2026, 1, 15), PmTriggers.CalendarNext(c, new(2026, 1, 1)));
        Assert.Equal(new DateOnly(2026, 5, 15), PmTriggers.CalendarNext(c, new(2026, 3, 15)));
        var weekly = Cal(CalendarKind.Interval, new(2020, 1, 6), every: 1, unit: FrequencyUnit.Week);
        Assert.Equal(new DateOnly(2026, 11, 23), PmTriggers.CalendarNext(weekly, new(2026, 11, 17)));
    }

    [Fact]
    public void Dates_calendar_skips_past_dates()
    {
        var c = Cal(CalendarKind.Dates, dates: [new(2026, 12, 20), new(2026, 6, 1), new(2027, 1, 10)]);
        Assert.Equal(new DateOnly(2026, 12, 20), PmTriggers.CalendarNext(c, new(2026, 6, 1)));
        Assert.Null(PmTriggers.CalendarNext(c, new(2027, 1, 10)));
    }

    [Fact]
    public void Earliest_of_own_rule_and_calendar()
    {
        var c = Cal(CalendarKind.Dates, dates: [new(2026, 3, 20)]);
        Assert.Equal(new DateOnly(2026, 3, 20), PmTriggers.Next(Monthly(), new(2026, 3, 1), [c]));
        Assert.Equal(new DateOnly(2026, 4, 25), PmTriggers.Next(Monthly(), new(2026, 3, 25), [c])); // a month after the last occurrence
        Assert.Equal(new DateOnly(2026, 3, 2), PmTriggers.First(Monthly(), new(2026, 3, 2), [c])); // an interval starts on its start date
    }

    [Fact]
    public void Calendar_type_uses_calendars_only()
    {
        var c = Cal(CalendarKind.Dates, dates: [new(2026, 7, 4)]);
        Assert.Equal(new DateOnly(2026, 7, 4), PmTriggers.Next(Monthly(PmScheduleType.Calendar), new(2026, 3, 1), [c]));
        Assert.Equal(new DateOnly(2026, 7, 4), PmTriggers.First(Monthly(PmScheduleType.Calendar), new(2026, 7, 4), [c]));
    }

    [Fact]
    public void Empty_or_inactive_calendars_give_no_date()
    {
        Assert.Null(PmTriggers.Next(Monthly(PmScheduleType.Calendar), new(2026, 3, 1), []));
        Assert.Null(PmTriggers.Next(Monthly(PmScheduleType.Calendar), new(2026, 3, 1), [Cal(CalendarKind.Dates, dates: [new(2026, 4, 1)], active: false)]));
        Assert.Null(PmTriggers.CalendarNext(Cal(CalendarKind.Interval), new(2026, 3, 1))); // no anchor
    }

    [Fact]
    public void Group_status_from_children()
    {
        Assert.Equal("Open", FourC.Cmms.Api.Features.WorkGroups.Status([]));
        Assert.Equal("Open", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Open, Core.Work.WorkOrderStatus.Open]));
        Assert.Equal("InProgress", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Open, Core.Work.WorkOrderStatus.OnHold]));
        Assert.Equal("Completed", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Completed, Core.Work.WorkOrderStatus.Cancelled]));
        Assert.Equal("Closed", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Closed, Core.Work.WorkOrderStatus.Cancelled]));
        Assert.Equal("Cancelled", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Cancelled]));
        Assert.Equal("Completed", FourC.Cmms.Api.Features.WorkGroups.Status([Core.Work.WorkOrderStatus.Closed, Core.Work.WorkOrderStatus.Completed]));
    }
}
