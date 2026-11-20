using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Tests;

public class ReservationTests
{
    private static DateOnly D(int day) => new DateOnly(2026, 11, 1).AddDays(day);

    private static PlanLine Line(string number, DateOnly? due, decimal reserved, DateOnly? start = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), number, due, start, reserved);

    [Fact]
    public void Still_reserved_never_goes_below_zero_and_ends_when_the_work_order_is_not_open()
    {
        Assert.Equal(2.5m, Reservations.StillReserved(4m, 1.5m, WorkOrderStatus.OnHold));
        Assert.Equal(0m, Reservations.StillReserved(4m, 6m, WorkOrderStatus.Open));
        Assert.Equal(0m, Reservations.StillReserved(4m, 0m, WorkOrderStatus.Completed));
    }

    [Fact]
    public void Stock_goes_to_the_earliest_due_job_first()
    {
        var a = Line("WO-2", due: D(10), reserved: 10);
        var b = Line("WO-1", due: D(1), reserved: 5);
        var s = Reservations.Shortages(10m, [a, b]);
        Assert.Equal(0m, s[b.LineId]);
        Assert.Equal(5m, s[a.LineId]);
    }

    [Fact]
    public void Ties_go_by_planned_start_then_number_and_undated_jobs_come_last()
    {
        var late = Line("WO-1", due: null, reserved: 3);
        var b = Line("WO-3", due: D(2), start: D(1), reserved: 3);
        var a = Line("WO-2", due: D(2), start: D(1), reserved: 3);
        var s = Reservations.Shortages(6m, [late, b, a]);
        Assert.Equal((0m, 0m, 3m), (s[a.LineId], s[b.LineId], s[late.LineId]));
    }

    [Fact]
    public void Negative_on_hand_counts_as_none_and_fractions_are_exact()
    {
        var a = Line("WO-1", due: D(1), reserved: 0.5m);
        Assert.Equal(0.5m, Reservations.Shortages(-3m, [a])[a.LineId]);
        Assert.Equal(0.25m, Reservations.Shortages(0.25m, [a])[a.LineId]);
    }

    [Fact]
    public void Issuing_its_own_parts_never_warns_the_most_urgent_job()
    {
        var b = Line("WO-B", due: D(1), reserved: 5);
        var a = Line("WO-A", due: D(30), reserved: 10);
        Assert.Empty(Reservations.WouldShort(10m, [b, a], 5m, b.WorkOrderId)); // WO-A is 5 short before and after
    }

    [Fact]
    public void Taking_reserved_stock_lists_the_jobs_it_shorts_and_by_how_much()
    {
        var a = Line("WO-A", due: D(5), reserved: 4);
        var c = Assert.Single(Reservations.WouldShort(6m, [a], 3m, null));
        Assert.Equal(("WO-A", 1m), (c.WorkOrderNumber, c.MoreShort));
    }

    [Fact]
    public void Visit_cost_adds_parts_per_asset_and_hours_at_the_average_known_rate()
    {
        var c = FourC.Cmms.Api.Features.PmCost.Visit([(2m, 5m), (1m, 3m)], multiplier: 3, estimatedHours: 2m, rates: [10m, 30m, null]);
        Assert.Equal((39m, 40m, 79m), (c.Parts, c.Labour, c.Total));
    }
}
