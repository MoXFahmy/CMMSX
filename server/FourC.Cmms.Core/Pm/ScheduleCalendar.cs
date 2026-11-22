using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Pm;

public enum CalendarKind { Interval = 1, Dates = 2 }

/// <summary>A named set of PM dates shared by many PMs (0.12.0): every N units from an anchor date, or chosen dates.</summary>
public class ScheduleCalendar : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public CalendarKind Kind { get; set; } = CalendarKind.Interval;
    public int Frequency { get; set; } = 1;
    public FrequencyUnit Unit { get; set; } = FrequencyUnit.Month;
    public DateOnly? AnchorDate { get; set; }
    public List<DateOnly> Dates { get; set; } = [];
    public bool IsActive { get; set; } = true;
}
