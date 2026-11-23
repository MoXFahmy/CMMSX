using FourC.Cmms.Core.Contracts;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: the date rules of service contracts — periods, status, deadlines, renewals and expected work.</summary>
public class ContractTermsTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void Period_end_clamps_to_month_end()
    {
        Assert.Equal(D("2027-12-31"), ContractTerms.PeriodEnd(D("2027-01-01"), 12));
        Assert.Equal(D("2027-02-28"), ContractTerms.PeriodEnd(D("2027-01-31"), 1));
        Assert.Equal(D("2028-02-29"), ContractTerms.PeriodEnd(D("2028-01-31"), 1));
        Assert.Equal(D("2027-02-28"), ContractTerms.PeriodEnd(D("2027-01-30"), 1));
        Assert.Equal(D("2027-04-14"), ContractTerms.PeriodEnd(D("2027-03-15"), 1));
        Assert.Equal(D("2028-02-29"), ContractTerms.PeriodEnd(D("2027-08-31"), 6));
    }

    [Fact]
    public void Status_follows_the_dates_and_termination()
    {
        var today = D("2027-06-01");
        Assert.Equal(ContractStatus.Upcoming, ContractTerms.StatusOn(today, D("2027-07-01"), D("2028-06-30"), false, null));
        Assert.Equal(ContractStatus.Active, ContractTerms.StatusOn(today, D("2027-01-01"), D("2027-12-31"), false, null));
        Assert.Equal(ContractStatus.Expired, ContractTerms.StatusOn(today, D("2026-06-01"), D("2027-05-31"), false, null));
        Assert.Equal(ContractStatus.Active, ContractTerms.StatusOn(today, D("2026-06-01"), D("2027-05-31"), true, null));
        Assert.Equal(ContractStatus.Terminated, ContractTerms.StatusOn(today, D("2027-01-01"), D("2027-12-31"), false, D("2027-06-01")));
        Assert.Equal(ContractStatus.Active, ContractTerms.StatusOn(today, D("2027-01-01"), D("2027-12-31"), false, D("2027-09-01")));
    }

    [Fact]
    public void Deadline_and_attention_window()
    {
        Assert.Equal(D("2027-12-01"), ContractTerms.Deadline(true, D("2027-12-31"), 30, null));
        Assert.Equal(D("2027-12-31"), ContractTerms.Deadline(false, D("2027-12-31"), 30, null));
        Assert.Null(ContractTerms.Deadline(true, D("2027-12-31"), 30, D("2027-09-01")));
        var deadline = D("2027-12-01");
        Assert.True(ContractTerms.NeedsAttention(D("2027-10-02"), deadline, 60));
        Assert.True(ContractTerms.NeedsAttention(D("2027-12-01"), deadline, 60));
        Assert.False(ContractTerms.NeedsAttention(D("2027-10-01"), deadline, 60));
        Assert.False(ContractTerms.NeedsAttention(D("2027-12-02"), deadline, 60));
        Assert.False(ContractTerms.NeedsAttention(D("2027-11-01"), null, 60));
    }

    [Fact]
    public void Renewals_catch_up_to_today()
    {
        Assert.Equal([(D("2026-03-01"), D("2026-03-31")), (D("2026-04-01"), D("2026-04-30")), (D("2026-05-01"), D("2026-05-31")), (D("2026-06-01"), D("2026-06-30"))],
            ContractTerms.Renewals(D("2026-02-28"), 1, D("2026-06-15")));
        Assert.Empty(ContractTerms.Renewals(D("2027-12-31"), 12, D("2027-06-01")));
        Assert.Empty(ContractTerms.Renewals(D("2026-02-28"), 0, D("2026-06-15")));
    }

    [Fact]
    public void Expected_count_scales_to_the_period()
    {
        Assert.Equal(12.0m, ContractTerms.ExpectedIn(12, ExpectedPer.Year, D("2027-01-01"), D("2027-12-31")));
        Assert.Equal(3.0m, ContractTerms.ExpectedIn(1, ExpectedPer.Month, D("2027-01-01"), D("2027-03-31")));
        Assert.Equal(8.0m, ContractTerms.ExpectedIn(2, ExpectedPer.Quarter, D("2027-01-01"), D("2027-12-31")));
    }
}
