using FourC.Cmms.Core.People;

namespace FourC.Cmms.Tests;

/// <summary>Weekly working patterns (0.9.0), no database.</summary>
public class WorkingHoursTests
{
    [Fact]
    public void Problem_rejects_bad_patterns()
    {
        Assert.Null(WorkingHours.Problem(null));
        Assert.Null(WorkingHours.Problem([new(DayOfWeek.Monday, "08:00", "16:30")]));
        Assert.Equal("Each day can appear once.", WorkingHours.Problem([new(DayOfWeek.Monday, "08:00", "12:00"), new(DayOfWeek.Monday, "13:00", "17:00")]));
        Assert.Equal("The end must be after the start.", WorkingHours.Problem([new(DayOfWeek.Monday, "17:00", "08:00")]));
        Assert.Equal("Use times like 08:00.", WorkingHours.Problem([new(DayOfWeek.Monday, "8am", "16:00")]));
    }

    [Fact]
    public void HoursOn_counts_the_days_span()
    {
        List<WorkingDay> week = [new(DayOfWeek.Monday, "08:00", "16:30")];
        Assert.Equal(8.5m, WorkingHours.HoursOn(week, DayOfWeek.Monday));
        Assert.Equal(0m, WorkingHours.HoursOn(week, DayOfWeek.Friday));
    }

    [Fact]
    public void Organization_defaults_from_the_old_daily_capacity()
    {
        var org = WorkingHours.Organization(new FourC.Cmms.Core.Tenancy.TenantSettings { DailyCapacityHours = 6 });
        Assert.All(Enum.GetValues<DayOfWeek>(), d => Assert.Equal(6m, WorkingHours.HoursOn(org, d)));
        var own = WorkingHours.Organization(new FourC.Cmms.Core.Tenancy.TenantSettings { WorkingHours = [new(DayOfWeek.Sunday, "07:00", "15:00")] });
        Assert.Equal(8m, WorkingHours.HoursOn(own, DayOfWeek.Sunday));
        Assert.Equal(0m, WorkingHours.HoursOn(own, DayOfWeek.Monday));
    }

    [Fact]
    public void A_person_without_hours_uses_their_only_sites_hours()
    {
        var org = new FourC.Cmms.Core.Tenancy.TenantSettings { DailyCapacityHours = 8 };
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var sites = new Dictionary<Guid, List<WorkingDay>?> { [a] = [new(DayOfWeek.Saturday, "09:00", "13:00")], [b] = null };
        Assert.Equal(4m, WorkingHours.HoursOn(WorkingHours.ForEmployee(null, [a], sites, org), DayOfWeek.Saturday));
        Assert.Equal(8m, WorkingHours.HoursOn(WorkingHours.ForEmployee(null, [a, b], sites, org), DayOfWeek.Saturday)); // two sites: the organization's
        Assert.Equal(8m, WorkingHours.HoursOn(WorkingHours.ForEmployee(null, [], sites, org), DayOfWeek.Saturday));
        Assert.Equal(8m, WorkingHours.HoursOn(WorkingHours.ForEmployee(null, [b], sites, org), DayOfWeek.Saturday)); // a site without its own
        List<WorkingDay> mine = [new(DayOfWeek.Saturday, "10:00", "12:00")];
        Assert.Equal(2m, WorkingHours.HoursOn(WorkingHours.ForEmployee(mine, [a], sites, org), DayOfWeek.Saturday));
    }
}
