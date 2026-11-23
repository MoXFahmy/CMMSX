namespace FourC.Cmms.Core.Contracts;

/// <summary>The date rules of service contracts (0.17.0): periods, status, deadlines, renewals and the expected number of work orders.</summary>
public static class ContractTerms
{
    /// <summary>Most renewals one pass adds (a guard against a bad renewal length; 1200 monthly periods is a century).</summary>
    public const int MaxRenewals = 1200;

    /// <summary>
    /// The last day of a period of <paramref name="months"/> months starting on <paramref name="start"/>: start + N months − 1 day. When adding
    /// the months lands in a shorter month (31 January + 1 month), the period ends on that month's last day (28/29 February).
    /// </summary>
    public static DateOnly PeriodEnd(DateOnly start, int months)
    {
        var next = start.AddMonths(months);
        return next.Day != start.Day ? next : next.AddDays(-1);
    }

    /// <summary>Where a contract stands on <paramref name="today"/>. A contract that renews itself and has passed its end is still active (its renewal is due).</summary>
    public static ContractStatus StatusOn(DateOnly today, DateOnly start, DateOnly end, bool autoRenew, DateOnly? terminatedOn)
    {
        if (terminatedOn <= today) return ContractStatus.Terminated;
        if (start > today) return ContractStatus.Upcoming;
        if (end < today) return autoRenew && terminatedOn is null ? ContractStatus.Active : ContractStatus.Expired;
        return ContractStatus.Active;
    }

    /// <summary>
    /// The date that needs attention: the last day to stop an automatic renewal (end − notice days), or the end of a contract that doesn't renew.
    /// None once a termination date is set.
    /// </summary>
    public static DateOnly? Deadline(bool autoRenew, DateOnly end, int noticeDays, DateOnly? terminatedOn) =>
        terminatedOn is not null ? null : autoRenew ? end.AddDays(-noticeDays) : end;

    /// <summary>In the alert window: from <paramref name="alertDays"/> before the deadline up to and including it.</summary>
    public static bool NeedsAttention(DateOnly today, DateOnly? deadline, int alertDays) =>
        deadline is { } d && today >= d.AddDays(-alertDays) && today <= d;

    /// <summary>The periods an automatic renewal adds after <paramref name="end"/>, each starting the day after the last, until one ends on or after <paramref name="today"/>.</summary>
    public static List<(DateOnly Start, DateOnly End)> Renewals(DateOnly end, int months, DateOnly today)
    {
        var list = new List<(DateOnly, DateOnly)>();
        if (months < 1) return list;
        while (end < today && list.Count < MaxRenewals)
        {
            var start = end.AddDays(1);
            end = PeriodEnd(start, months);
            list.Add((start, end));
        }
        return list;
    }

    /// <summary>The expected number of work orders from <paramref name="from"/> to <paramref name="to"/>, for <paramref name="count"/> per year, quarter or month (1 decimal).</summary>
    public static decimal ExpectedIn(int count, ExpectedPer per, DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        var days = to.DayNumber - from.DayNumber + 1;
        var unit = per switch { ExpectedPer.Month => 30.4375m, ExpectedPer.Quarter => 91.3125m, _ => 365.25m };
        return Math.Round(count * days / unit, 1, MidpointRounding.AwayFromZero);
    }
}
