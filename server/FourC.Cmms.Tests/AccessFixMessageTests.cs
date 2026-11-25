using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>0.29.1 (access and privacy fixes): the new refusals read in Arabic too.</summary>
public class AccessFixMessageTests
{
    [Theory]
    [InlineData("Karim Saad can't see this PM's place, so they would not get its e-mail. Choose someone who can.")]
    [InlineData("This contract also covers assets at other sites that you can't see, so it can't be limited to one site.")]
    public void New_messages_have_Arabic(string english) => Assert.NotEqual(english, Translator.T(english, "ar"));

    [Fact]
    public void A_refusal_keeps_the_person_in_the_Arabic_text()
    {
        var arabic = Translator.T("Karim Saad can't see this PM's place, so they would not get its e-mail. Choose someone who can.", "ar");
        Assert.Contains("Karim Saad", arabic);
    }
}
