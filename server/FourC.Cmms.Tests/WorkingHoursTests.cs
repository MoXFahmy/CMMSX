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
}
