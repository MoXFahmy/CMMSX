using System.Net.Http.Json;
using FourC.Cmms.Api.Features;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: e-mails and phone notifications name things in each recipient's language.</summary>
public class BilingualMailTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private EmailSenderService Mail => fx.Factory!.Services.GetRequiredService<EmailSenderService>();
    private PushSenderService Push => fx.Factory!.Services.GetRequiredService<PushSenderService>();

    /// <summary>A technician login in <paramref name="language"/>, linked to an employee; its phone registered <paramref name="token"/> when given.</summary>
    private async Task<(Guid Employee, string Email)> TechAsync(Loop.Org o, string language, string? token = null)
    {
        var user = await UserAsync(fx, o.Admin, "Technician");
        await Ok(await user.Client.PutAsJsonAsync("/api/auth/me/language", new { language }));
        var emp = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = $"T{Guid.NewGuid():N}"[..8], fullName = "Tech", userId = user.Id, isContractor = false })));
        if (token is not null) await Ok(await user.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        return (emp, user.Email);
    }

    private static async Task<Guid> ArabicTemplateAsync(HttpClient c) => await Pm12.TemplateAsync(c, "CHK", extra: new()
    {
        ["title"] = "Chiller check-up", ["titleAr"] = "فحص دوري للمبرد",
    });

    [SkippableFact]
    public async Task Email_to_an_arabic_recipient_uses_arabic_names()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var arabic = await TechAsync(o, "ar");
        var english = await TechAsync(o, "en");
        var asset = await AssetAsync(o.Admin, o.Site);
        await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, assetId = asset, type = "Preventive", priority = "Medium", templateId = await ArabicTemplateAsync(o.Admin),
            assigneeIds = new[] { arabic.Employee, english.Employee },
        }));
        await Mail.RunOnceAsync();

        var ar = Assert.Single(fx.Emails.To(arabic.Email));
        Assert.Contains("فحص دوري للمبرد", ar.Subject + ar.Html);
        Assert.Contains(PumpAr, ar.Html);
        var en = Assert.Single(fx.Emails.To(english.Email));
        Assert.Contains("Chiller check-up", en.Subject + en.Html);
        Assert.Contains(Pump, en.Html);
        Assert.DoesNotContain(PumpAr, en.Html);
    }

    [SkippableFact]
    public async Task Push_to_an_arabic_recipient_uses_the_arabic_title()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var token = $"tok-{Guid.NewGuid():N}";
        var arabic = await TechAsync(o, "ar", token);
        await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, type = "Preventive", priority = "Medium", templateId = await ArabicTemplateAsync(o.Admin),
            assigneeIds = new[] { arabic.Employee },
        }));
        await Push.RunOnceAsync(CancellationToken.None);
        var push = Assert.Single(fx.Pushes.To(token));
        Assert.Contains("فحص دوري للمبرد", push.Body);
    }
}
