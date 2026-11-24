using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FourC.Cmms.Core.Tenancy;

namespace FourC.Cmms.Api.Features;

/// <summary>Which organization settings a save changed, by top-level key, for the activity log (0.25.0).</summary>
public static class SettingsDiff
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Keys kept as they are by the general settings page; they change (and are logged) through their own pages (security, lists, logo).</summary>
    private static readonly HashSet<string> Skipped = ["security", "listLabels", "logoVersion", "logoContentType"];

    /// <summary>{ "settings.timeZone": { from, to }, … } for every changed key, compared as JSON.</summary>
    public static Dictionary<string, object?> Changes(TenantSettings before, TenantSettings after, string? keep = null)
    {
        var a = JsonSerializer.SerializeToNode(before, Json)!.AsObject();
        var b = JsonSerializer.SerializeToNode(after, Json)!.AsObject();
        var changes = new Dictionary<string, object?>();
        foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)).Where(k => keep is null ? !Skipped.Contains(k) : k == keep).Order(StringComparer.Ordinal))
        {
            var from = a[key];
            var to = b[key];
            if (JsonNode.DeepEquals(from, to)) continue;
            changes[$"settings.{key}"] = new { from = from?.DeepClone(), to = to?.DeepClone() };
        }
        return changes;
    }
}
