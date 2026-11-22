using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Shared helpers for the 0.12.0 template, calendar and group tests.</summary>
public static class Pm12
{
    public static async Task<Guid> AssetAsync(HttpClient c, Guid site, string name, Guid? location = null, Guid? category = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name, siteId = site, locationId = location, categoryId = category, status = "Active", criticality = "High", warrantyType = "None" })));

    /// <summary>A work template; <paramref name="extra"/> adds or overrides fields of the body.</summary>
    public static async Task<Guid> TemplateAsync(HttpClient c, string code, string[]? steps = null, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = code, ["name"] = $"Template {code}", ["steps"] = steps ?? ["Check"], ["isActive"] = true };
        foreach (var (k, v) in extra ?? []) body[k] = v;
        return Id(await Ok(await c.PostAsJsonAsync("/api/pm-tasks", body)));
    }

    /// <summary>A monthly PM body from <paramref name="start"/>; <paramref name="extra"/> adds or overrides fields.</summary>
    public static Dictionary<string, object?> Schedule(Guid site, Guid task, DateOnly start, Guid? asset = null, Guid[]? assets = null, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "PM", ["siteId"] = site, ["assetId"] = asset, ["assetIds"] = assets, ["priority"] = "Medium", ["pmTaskId"] = task, ["type"] = "FixedInterval",
            ["basis"] = "Scheduled", ["frequency"] = 1, ["unit"] = "Month", ["workPeriodDays"] = 1, ["wholeMonth"] = false, ["leadDays"] = 0, ["startDate"] = Iso(start),
            ["timeBased"] = false, ["extraTasks"] = Array.Empty<object>(),
        };
        foreach (var (k, v) in extra ?? []) body[k] = v;
        return body;
    }

    public static async Task<JsonElement> SaveScheduleAsync(HttpClient c, Dictionary<string, object?> body) => await Ok(await c.PostAsJsonAsync("/api/pm-schedules", body));

    public static async Task<Guid> MeterAsync(HttpClient c, Guid asset, string code, string unit = "h") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/meters", new { code, name = $"Meter {code}", assetId = asset, unit, isActive = true })));

    /// <summary>Generates every schedule's occurrences up to <paramref name="until"/>; returns the results.</summary>
    public static async Task<JsonElement> GenerateAsync(HttpClient c, DateOnly until, params Guid[] schedules) =>
        (await Ok(await c.PostAsJsonAsync("/api/pm/generate", new { until = Iso(until), scheduleIds = schedules }))).GetProperty("results");
}
