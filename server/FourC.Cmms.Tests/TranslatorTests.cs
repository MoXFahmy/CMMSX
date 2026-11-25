using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

public class TranslatorTests
{
    [Fact]
    public void A_message_with_values_keeps_them_in_arabic() =>
        Assert.Equal("انتهت صلاحية التشغيلة B1 في 2026-11-20 ولا يمكن صرفها.", Translator.T("Batch B1 expired on 2026-11-20 and cannot be issued.", "ar"));

    [Fact]
    public void A_field_name_inside_a_message_is_translated_too() =>
        Assert.Equal("اسم المؤسسة مطلوب.", Translator.T("Organization name is required.", "ar"));

    [Fact]
    public void A_status_inside_a_message_is_translated_too() =>
        Assert.Equal("لا يمكن أن ينتقل أمر العمل من مكتمل إلى قيد التنفيذ.", Translator.T("A work order cannot go from Completed to InProgress.", "ar"));

    [Fact]
    public void Unknown_text_and_english_stay_as_they_are()
    {
        Assert.Equal("Pump P-101", Translator.T("Pump P-101", "ar"));
        Assert.Equal("Organization name is required.", Translator.T("Organization name is required.", "en"));
    }
}

public class ArabicAnswersTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<HttpClient> RegisterAsync()
    {
        var c = fx.Client();
        (await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Lugha Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }))
            .EnsureSuccessStatusCode();
        return c;
    }

    private static HttpRequestMessage Arabic(HttpMethod method, string url, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.AcceptLanguage.ParseAdd("ar");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [SkippableFact]
    public async Task Errors_come_back_in_the_language_the_app_asked_for()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await RegisterAsync();
        // A validation error, in Arabic and in English.
        var ar = await c.SendAsync(Arabic(HttpMethod.Post, "/api/sites", new { code = "", name = "", isActive = true }));
        Assert.Equal(HttpStatusCode.BadRequest, ar.StatusCode);
        var arBody = await ar.Content.ReadAsStringAsync();
        Assert.Contains("مطلوب", arBody);
        Assert.Contains("توجد أخطاء في البيانات المدخلة.", arBody);
        var en = await c.PostAsJsonAsync("/api/sites", new { code = "", name = "", isActive = true });
        Assert.Contains("is required.", await en.Content.ReadAsStringAsync());

        // A rule broken on the server (an error, not a field).
        var backwards = await c.SendAsync(Arabic(HttpMethod.Get, $"/api/reports/wo-throughput?from={Loop.Iso(Loop.Today)}&to={Loop.Iso(Loop.Today.AddDays(-60))}"));
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Contains("تاريخ البدء بعد تاريخ الانتهاء.", await backwards.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Reports_have_arabic_names_columns_and_months()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await RegisterAsync();
        var list = await (await c.SendAsync(Arabic(HttpMethod.Get, "/api/reports"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.EnumerateArray(), r => r.GetProperty("title").GetString() == "الأعمال المتراكمة");
        var report = await (await c.SendAsync(Arabic(HttpMethod.Get, "/api/reports/wo-throughput?from=2026-10-01&to=2026-11-30"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("إنتاجية أوامر العمل", report.GetProperty("title").GetString());
        Assert.Contains(report.GetProperty("columns").EnumerateArray(), col => col.GetProperty("label").GetString() == "الشهر");
        Assert.Contains(report.GetProperty("rows").EnumerateArray(), row => row.GetProperty("month").GetString()!.Contains("نوفمبر"));
    }
}
