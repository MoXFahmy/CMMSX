using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

public class SenderNameTests
{
    [Fact]
    public void Mail_is_sent_as_the_organization_via_cmx() =>
        Assert.Equal("Mail Co via CMX", EmailText.SenderName("Mail Co", "CMX"));

    [Fact]
    public void An_organization_named_like_the_app_is_not_repeated() =>
        Assert.Equal("CMX", EmailText.SenderName(" cmx ", "CMX"));

    [Fact]
    public void Without_an_organization_name_the_app_name_is_used() =>
        Assert.Equal("CMX", EmailText.SenderName(null, "CMX"));
}
