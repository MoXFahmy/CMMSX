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

public class EmailAddressTests
{
    [Theory]
    [InlineData("ann@example.com", true)]
    [InlineData("a.b+c@sub.example.eg", true)]
    [InlineData("ann@example", false)]
    [InlineData("ann example.com", false)]
    [InlineData("Ann <ann@example.com>", false)]
    [InlineData("ann@example.com\r\nBcc: x@y.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_plain_addresses_are_used(string? address, bool ok) => Assert.Equal(ok, EmailText.IsAddress(address));
}
