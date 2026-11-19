using FourC.Cmms.Api.Infrastructure;
using MailKit.Security;

namespace FourC.Cmms.Tests;

public class EmailOptionsTests
{
    [Theory]
    [InlineData(true, 587, SecureSocketOptions.StartTls)]
    [InlineData(true, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(false, 1025, SecureSocketOptions.None)]
    public void Smtp_with_tls_never_falls_back_to_plain_text(bool tls, int port, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpEmailSender.Security(new EmailOptions.SmtpOptions { UseTls = tls, Port = port }));

    [Theory]
    [InlineData("", true, "plant", false)]               // sign-up open: nobody until the operator says so
    [InlineData("", false, "plant", true)]               // sign-up closed: every organization on the server
    [InlineData("plant, depot", true, "Depot", true)]
    [InlineData("plant", true, "other", false)]
    [InlineData("*", true, "anyone", true)]
    public void Organizations_need_the_operators_ok_while_sign_up_is_open(string allowed, bool openSignUp, string slug, bool expected) =>
        Assert.Equal(expected, new EmailOptions { AllowedOrganizations = allowed }.AllowsOrganization(slug, openSignUp));
}
