using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class LanguageTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<HttpClient> RegisterAsync()
    {
        var c = fx.Client();
        var r = await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Lugha Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" });
        r.EnsureSuccessStatusCode();
        return c;
    }

    private static async Task<JsonElement> MeAsync(HttpClient c) => (await (await c.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>());

    [SkippableFact]
    public async Task A_person_uses_the_organizations_language_until_they_choose_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await RegisterAsync();
        var me = await MeAsync(c);
        Assert.Equal("en", me.GetProperty("user").GetProperty("language").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("user").GetProperty("languageChoice").ValueKind);

        await TestSettings.SetSettingsAsync(c, s => s["defaultLanguage"] = "ar");
        Assert.Equal("ar", (await MeAsync(c)).GetProperty("user").GetProperty("language").GetString());

        (await c.PutAsJsonAsync("/api/auth/me/language", new { language = "en" })).EnsureSuccessStatusCode();
        var chosen = (await MeAsync(c)).GetProperty("user");
        Assert.Equal(("en", "en"), (chosen.GetProperty("language").GetString(), chosen.GetProperty("languageChoice").GetString()));

        (await c.PutAsJsonAsync("/api/auth/me/language", new { language = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal("ar", (await MeAsync(c)).GetProperty("user").GetProperty("language").GetString()); // back to the organization's
    }

    [SkippableFact]
    public async Task Renamed_terms_have_their_own_arabic_names()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await RegisterAsync();
        var arabic = (await MeAsync(c)).GetProperty("tenant").GetProperty("settings").GetProperty("terminologyArabic");
        Assert.Equal(("الموقع", "الأصول"), (arabic.GetProperty("site").GetString(), arabic.GetProperty("assets").GetString()));
        await TestSettings.SetSettingsAsync(c, s => { s["terminologyArabic"]!["asset"] = "المعدة"; s["terminologyArabic"]!["assets"] = "المعدات"; });
        var saved = (await MeAsync(c)).GetProperty("tenant").GetProperty("settings").GetProperty("terminologyArabic");
        Assert.Equal("المعدات", saved.GetProperty("assets").GetString());
    }

    [SkippableFact]
    public async Task An_organization_set_up_in_arabic_speaks_arabic()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");
        (await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "مصنع", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" })).EnsureSuccessStatusCode();
        var me = await MeAsync(c);
        Assert.Equal("ar", me.GetProperty("tenant").GetProperty("settings").GetProperty("defaultLanguage").GetString());
        Assert.Equal("ar", me.GetProperty("user").GetProperty("language").GetString());
    }

    [SkippableFact]
    public async Task Signing_in_in_another_language_keeps_it_as_the_persons_choice()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var email = $"{Guid.NewGuid():N}@test.local";
        (await fx.Client().PostAsJsonAsync("/api/auth/register", new { organizationName = "Lugha Co", fullName = "Admin", email, password = "Passw0rd!x" })).EnsureSuccessStatusCode();

        var arabic = fx.Client();
        arabic.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");
        (await arabic.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x" })).EnsureSuccessStatusCode();
        var user = (await MeAsync(arabic)).GetProperty("user");
        Assert.Equal(("ar", "ar"), (user.GetProperty("language").GetString(), user.GetProperty("languageChoice").GetString()));

        // Signing in again in the organization's language doesn't undo a choice already made.
        var english = fx.Client();
        (await english.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x" })).EnsureSuccessStatusCode();
        Assert.Equal("ar", (await MeAsync(english)).GetProperty("user").GetProperty("languageChoice").GetString());
    }

    [SkippableFact]
    public async Task Only_the_languages_cmx_has_can_be_chosen()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/auth/me/language", new { language = "fr" })).StatusCode);
        var bad = await TestSettings.TrySetSettingsAsync(c, s => s["defaultLanguage"] = "xx");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
