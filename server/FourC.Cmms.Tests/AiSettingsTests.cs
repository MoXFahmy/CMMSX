using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The AI assistant's switch, the organization's monthly limit under the server's ceiling, and the status (0.15.0).</summary>
public class AiSettingsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>Saves the organization's settings with <paramref name="change"/> applied to what is stored.</summary>
    public static async Task<HttpResponseMessage> PutSettingsAsync(HttpClient c, Action<JsonObject> change)
    {
        var current = JsonNode.Parse((await Ok(await c.GetAsync("/api/settings"))).GetRawText())!;
        var settings = current["settings"]!.AsObject();
        change(settings);
        return await c.PutAsJsonAsync("/api/settings", new { name = current["name"]!.GetValue<string>(), settings });
    }

    public static async Task<JsonElement> StatusAsync(HttpClient c) => await Ok(await c.GetAsync("/api/ai/status"));

    [SkippableFact]
    public async Task Status_reports_ceiling_limit_and_use()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var s = await StatusAsync(org.Admin);
        Assert.True(s.GetProperty("configured").GetBoolean());
        Assert.False(s.GetProperty("enabled").GetBoolean());
        Assert.Equal(300, s.GetProperty("ceiling").GetInt32());
        Assert.Equal(300, s.GetProperty("limit").GetInt32());
        Assert.Equal(0, s.GetProperty("used").GetInt32());
        var zone = (await Ok(await org.Admin.GetAsync("/api/settings"))).GetProperty("settings").GetProperty("timeZone").GetString()!;
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone));
        var next = new DateOnly(local.Year, local.Month, 1).AddMonths(1);
        Assert.Equal(Iso(next), s.GetProperty("renewsOn").GetString());
    }

    [SkippableFact]
    public async Task Limit_above_ceiling_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("The limit can be at most 300 a month.", await Bad(await PutSettingsAsync(org.Admin, s => s["aiMonthlyLimit"] = 301)));
        Assert.Contains("The limit can't be below 0.", await Bad(await PutSettingsAsync(org.Admin, s => s["aiMonthlyLimit"] = -1)));
        await Ok(await PutSettingsAsync(org.Admin, s => s["aiMonthlyLimit"] = 50));
        Assert.Equal(50, (await StatusAsync(org.Admin)).GetProperty("limit").GetInt32());
    }

    [SkippableFact]
    public async Task Switch_cannot_go_on_without_a_key()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        try
        {
            fx.Ai.Configured = false;
            Assert.Contains("AI isn't set up on this server.", await Bad(await PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true)));
            Assert.False((await StatusAsync(org.Admin)).GetProperty("configured").GetBoolean());
        }
        finally { fx.Ai.Configured = true; }
        await Ok(await PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true));
        Assert.True((await StatusAsync(org.Admin)).GetProperty("enabled").GetBoolean());
    }

    [SkippableFact]
    public async Task Settings_save_keeps_working_when_the_key_is_removed_later()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true));
        try
        {
            fx.Ai.Configured = false;
            await Ok(await PutSettingsAsync(org.Admin, s => s["dailyCapacityHours"] = 7));
        }
        finally { fx.Ai.Configured = true; }
    }
}
