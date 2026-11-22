using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Helpers for the 0.13.0 smart checklist tests.</summary>
public static class Ck13
{
    /// <summary>A temperature point: valid 0–60 °C, alarm low 5, alert low 10, target 22, alert high 28, alarm high 32; <paramref name="extra"/> overrides.</summary>
    public static Dictionary<string, object?> Point(string code, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["code"] = code, ["name"] = $"Point {code}", ["kind"] = "Temperature", ["unit"] = "°C", ["decimals"] = 1, ["validMin"] = 0, ["validMax"] = 60,
            ["alarmLow"] = 5, ["alertLow"] = 10, ["target"] = 22, ["alertHigh"] = 28, ["alarmHigh"] = 32, ["isActive"] = true,
        };
        foreach (var (k, v) in extra ?? []) body[k] = v;
        return body;
    }

    public static async Task<Guid> PointAsync(HttpClient c, string code, Dictionary<string, object?>? extra = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/measurement-points", Point(code, extra))));

    /// <summary>A work template with typed steps (anonymous objects shaped like <c>StepInput</c>).</summary>
    public static async Task<Guid> TypedTemplateAsync(HttpClient c, string code, object[] steps, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = code, ["name"] = $"Template {code}", ["isActive"] = true, ["stepDetails"] = steps };
        foreach (var (k, v) in extra ?? []) body[k] = v;
        return Id(await Ok(await c.PostAsJsonAsync("/api/pm-tasks", body)));
    }

    public static async Task<JsonElement> ReadingsAsync(HttpClient c, Guid point, Guid? asset = null) =>
        await Ok(await c.GetAsync($"/api/measurement-points/{point}/readings{(asset is { } a ? $"?assetId={a}" : "")}"));
}
