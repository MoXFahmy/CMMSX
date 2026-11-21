using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

public class SessionRenewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_30_day_sign_in_used_after_a_day_starts_its_30_days_again() =>
        Assert.True(Sessions.RenewCookie(Now.AddDays(-2), Now.AddDays(28), Now));

    [Fact]
    public void A_30_day_sign_in_isnt_reissued_on_every_request() =>
        Assert.False(Sessions.RenewCookie(Now.AddHours(-3), Now.AddDays(30).AddHours(-3), Now));

    [Fact]
    public void A_12_hour_sign_in_is_left_to_the_normal_sliding() =>
        Assert.False(Sessions.RenewCookie(Now.AddHours(-7), Now.AddHours(5), Now));
}
