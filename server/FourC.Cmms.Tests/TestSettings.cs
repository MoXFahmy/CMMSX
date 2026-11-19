using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace FourC.Cmms.Tests;

/// <summary>Changes the signed-in organization's settings the way the Settings page does: read them, edit, save.</summary>
public static class TestSettings
{
    public static async Task SetSettingsAsync(HttpClient c, Action<JsonObject> change)
    {
        var current = JsonNode.Parse(await c.GetStringAsync("/api/settings"))!.AsObject();
        var settings = current["settings"]!.AsObject();
        change(settings);
        var r = await c.PutAsJsonAsync("/api/settings", new { name = current["name"]!.GetValue<string>(), settings });
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>Like <see cref="SetSettingsAsync"/> but returns the response, for testing rejected settings.</summary>
    public static async Task<HttpResponseMessage> TrySetSettingsAsync(HttpClient c, Action<JsonObject> change)
    {
        var current = JsonNode.Parse(await c.GetStringAsync("/api/settings"))!.AsObject();
        var settings = current["settings"]!.AsObject();
        change(settings);
        return await c.PutAsJsonAsync("/api/settings", new { name = current["name"]!.GetValue<string>(), settings });
    }
}
