namespace FourC.Cmms.Core.People;

/// <summary>One day of a weekly working pattern: start and end as "HH:mm" (the end after the start, the same day).</summary>
public sealed record WorkingDay(DayOfWeek Day, string Start, string End);
