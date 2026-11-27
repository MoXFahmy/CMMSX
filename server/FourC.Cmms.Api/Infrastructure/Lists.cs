using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// The four fixed lists an organization may reword (0.21.0): their names in a language, reading a name back (import), and the
/// checks on saved wording. The meaning of each value is the enum; only what people read changes.
/// </summary>
public static class Lists
{
    public static readonly string[] Palette = ["gray", "red", "pink", "grape", "violet", "indigo", "blue", "cyan", "teal", "green", "lime", "yellow", "orange"];

    private static readonly (Type Type, string Key, string Word)[] Known =
    [
        (typeof(WorkType), "workType", "work type"), (typeof(WorkPriority), "priority", "priority"),
        (typeof(AssetStatus), "assetStatus", "asset status"), (typeof(Criticality), "criticality", "criticality level"),
    ];

    /// <summary>The settings key of a list ("priority"), or null for other choices.</summary>
    public static string? Key(Type type) => Known.FirstOrDefault(k => k.Type == type).Key;

    private static ListLabel? Label(TenantSettings? s, Enum value) =>
        Key(value.GetType()) is { } key && s?.ListLabels is { } all && all.TryGetValue(key, out var list) && list.TryGetValue(value.ToString(), out var label) ? label : null;

    /// <summary>The built-in words: "Under repair", or its Arabic.</summary>
    public static string BuiltIn(Enum value, string? language = null) => Translator.T(TableExport.Words(value.ToString()), language);

    /// <summary>What people read for a value: the organization's name in the language, else the built-in words.</summary>
    public static string Name(TenantSettings? s, Enum value, string? language = null)
    {
        var lang = language ?? Translator.CurrentLanguage;
        var label = Label(s, value);
        var own = lang == "ar" ? label?.NameAr : label?.Name;
        return string.IsNullOrWhiteSpace(own) ? BuiltIn(value, lang) : own.Trim();
    }

    /// <summary>Any name a value goes by: built-in English or Arabic, or the organization's in either language.</summary>
    private static IEnumerable<string> Names(TenantSettings? s, Enum value)
    {
        var label = Label(s, value);
        return new[] { value.ToString(), TableExport.Words(value.ToString()), BuiltIn(value, "en"), BuiltIn(value, "ar"), label?.Name, label?.NameAr }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Normal(x!));
    }

    /// <summary>The value a cell names (case and spaces don't matter), or null.</summary>
    public static Enum? Parse(TenantSettings? s, Type type, string text)
    {
        var wanted = Normal(text);
        return Enum.GetValues(type).Cast<Enum>().FirstOrDefault(v => Names(s, v).Contains(wanted));
    }

    /// <summary>
    /// What's wrong with the shape of a payload before it is cleaned (0.29.1), or null: a list sent as nothing, a value sent as nothing, or two
    /// lists (or two values of one list) that differ only in capital letters.
    /// </summary>
    public static string? Shape(Dictionary<string, Dictionary<string, ListLabel>>? labels)
    {
        if (labels is null) return null;
        if (labels.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) return "A list or a value is given twice. Capital letters don't make them different.";
        foreach (var (key, list) in labels)
        {
            if (list is null || list.Values.Any(l => l is null)) return "Fill in every list and every value: none can be empty.";
            if (list.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) return "A list or a value is given twice. Capital letters don't make them different.";
        }
        return null;
    }

    /// <summary>What's wrong with an organization's wording, or null.</summary>
    public static string? Problem(Dictionary<string, Dictionary<string, ListLabel>> labels)
    {
        foreach (var (key, list) in labels)
        {
            var known = Known.FirstOrDefault(k => k.Key == key);
            if (known.Type is null) return $"Unknown list: {key}.";
            var values = Enum.GetValues(known.Type).Cast<Enum>().ToList();
            foreach (var name in list.Keys) if (!values.Any(v => v.ToString() == name)) return $"Unknown value: {name}.";
            foreach (var label in list.Values)
            {
                if ((label.Name?.Trim().Length ?? 0) > 50 || (label.NameAr?.Trim().Length ?? 0) > 50) return "A name can be at most 50 characters.";
                if (!string.IsNullOrWhiteSpace(label.Color) && !Palette.Contains(label.Color)) return "Choose a colour from the list.";
            }
            if (values.All(v => list.TryGetValue(v.ToString(), out var l) && l.Hidden)) return $"Leave at least one {known.Word} showing.";
            var probe = new TenantSettings { ListLabels = new() { [key] = list } };
            foreach (var v in values)
            {
                if (!list.TryGetValue(v.ToString(), out var own)) continue;
                var mine = new[] { own.Name, own.NameAr }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Normal(x!)).ToList();
                foreach (var other in values.Where(o => !o.Equals(v)))
                    if (Names(probe, other).Intersect(mine).Any()) return $"This name is already used by {Shown(probe, other)}.";
            }
        }
        return null;
    }

    /// <summary>
    /// The value a name clashes with, as the reader knows it (0.29.2): an Arabic reader by its Arabic name (the organization's, else the
    /// built-in one), an English reader by the built-in words as before.
    /// </summary>
    private static string Shown(TenantSettings probe, Enum value) => Translator.CurrentLanguage == "ar" ? Name(probe, value, "ar") : BuiltIn(value, "en");

    /// <summary>Trimmed names under the exact list and value names (any case is accepted); rows that say nothing are dropped.</summary>
    public static Dictionary<string, Dictionary<string, ListLabel>> Clean(Dictionary<string, Dictionary<string, ListLabel>>? labels) =>
        (labels ?? []).ToDictionary(l => Known.FirstOrDefault(k => string.Equals(k.Key, l.Key, StringComparison.OrdinalIgnoreCase)).Key ?? l.Key, l => l.Value
            .Select(v => (Key: ValueName(l.Key, v.Key), Label: new ListLabel
            {
                Name = string.IsNullOrWhiteSpace(v.Value.Name) ? null : v.Value.Name.Trim(), NameAr = string.IsNullOrWhiteSpace(v.Value.NameAr) ? null : v.Value.NameAr.Trim(),
                Color = string.IsNullOrWhiteSpace(v.Value.Color) ? null : v.Value.Color.Trim(), Order = v.Value.Order, Hidden = v.Value.Hidden,
            }))
            .Where(v => v.Label.Name is not null || v.Label.NameAr is not null || v.Label.Color is not null || v.Label.Order is not null || v.Label.Hidden)
            .ToDictionary(v => v.Key, v => v.Label));

    private static string ValueName(string list, string value)
    {
        var type = Known.FirstOrDefault(k => string.Equals(k.Key, list, StringComparison.OrdinalIgnoreCase)).Type;
        return type is not null && Enum.GetNames(type).FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)) is { } exact ? exact : value;
    }

    private static string Normal(string s) => string.Join(' ', s.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
