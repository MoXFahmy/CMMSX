using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Lines CMX writes itself and stores in English (0.29.2, spec decision 6): activity summaries, work order history notes, PM generation
/// results, stock movement notes, a request's automatic approval. They stay stored exactly as written (searches and old rows rely on it)
/// and are put into the reader's language where they leave the server for a screen or an export, so rows written before 0.29.2 show in
/// Arabic too. The Arabic is in Localization/ar.json under the English form.
/// <para>Only these forms are translated. A note column also holds what people typed: that text never matches a form and is shown as
/// written, even when it happens to be a phrase the catalogue knows. Inside a form the values (a name, a number, a code, a date, a person's
/// own words) are shown as written too, except where the value is itself one of these lines (a PM skip reason).</para>
/// </summary>
public static partial class StoredText
{
    /// <summary>
    /// Every line, exactly as the code writes it, <c>{0}</c>… for the values. A count written by <c>AuditLog.Count</c> ("1 asset" /
    /// "14 assets") is spelt out in each form, singular and plural, so a count needs no catch-all "{0} assets" entry that would rewrite
    /// names ending in "assets" in other messages.
    /// </summary>
    public static IReadOnlyList<string> Forms { get; } =
    [
        // Activity log summaries of changes made in bulk (AuditLog.Summary, 0.25.0).
        "Brand renamed to {0}: {1} asset", "Brand renamed to {0}: {1} assets", "Brand merged into {0}: {1} asset", "Brand merged into {0}: {1} assets",
        "Model renamed to {0}: {1} asset", "Model renamed to {0}: {1} assets", "Model merged into {0}: {1} asset", "Model merged into {0}: {1} assets",
        "Moved to another site: {0} asset followed", "Moved to another site: {0} assets followed",
        "Unit renamed to {0}: {1} part and {2} point or step", "Unit renamed to {0}: {1} part and {2} points and steps",
        "Unit renamed to {0}: {1} parts and {2} point or step", "Unit renamed to {0}: {1} parts and {2} points and steps",
        "Cost centre set on {0} work order and {1} issue", "Cost centre set on {0} work order and {1} issues",
        "Cost centre set on {0} work orders and {1} issue", "Cost centre set on {0} work orders and {1} issues",
        "No longer the preferred supplier of {0} part", "No longer the preferred supplier of {0} parts",
        "{0} request unlinked", "{0} requests unlinked",
        // Work order history notes (status changes).
        "Created from work request", "Created on the phone", "Started automatically: labour logged", "Started automatically: checklist step done",
        "Generated from PM {0}", "Generated from PM {0} (group {1})", "Follow-up of a reading", "Follow-up of {0}", "Refused by {0}", "Refused by {0}: {1}",
        // A request's decision note when nobody decided.
        "Approved automatically (type: {0})",
        // PM generation results and their reasons.
        "Skipped: {0}", "Waiting: {0}", "the asset is {0}", "the previous work order is still open", "every asset on the round has a status that skips PM work",
        "changed by another generation run", "Waiting for the last work order to be completed", "Previous PM work order is still open", "PM task not found",
        "Already generated", "No active assets in this part", "Nothing to generate", "No active assets in the list", "The asset group is inactive.",
        "No next date: its calendars have no dates after {0}.",
        // Stock movement notes.
        "From the phone", "Stock take {0}", "Stock take {0}: {1}", "Stock count {0}", "Received on {0}", "Opening balance (import)", "Unit found",
        "Unit {0} fitted to {1}", "Unit {0} removed", "Unit {0} sent for repair", "Unit {0} back from repair", "Unit {0} scrapped",
    ];

    /// <summary>Forms whose value is itself a line CMX wrote (a PM skip reason, an asset status), translated in turn.</summary>
    private static readonly HashSet<string> ValuesTranslated = ["Skipped: {0}", "Waiting: {0}", "the asset is {0}"];

    private sealed record Form(Regex Pattern, string Arabic, bool TranslateValues);

    /// <summary>The forms with the most fixed text first, so "Refused by {0}" doesn't take "Refused by {0}: {1}"'s lines.</summary>
    private static readonly Form[] Compiled = Forms
        .OrderByDescending(f => Hole().Replace(f, "").Length)
        .Select(f => new Form(
            new Regex("^" + Hole().Replace(Regex.Escape(f).Replace(@"\{", "{"), h => $"(?<p{h.Groups[1].Value}>.+?)") + "$",
                RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
            Translator.T(f, "ar"), // the form is its own key: this is its Arabic, {0}… kept
            ValuesTranslated.Contains(f)))
        .ToArray();

    /// <summary>A stored line in the reader's language (or <paramref name="language"/>); anything that isn't one of <see cref="Forms"/> as it is.</summary>
    [return: NotNullIfNotNull(nameof(english))]
    public static string? Show(string? english, string? language = null) =>
        string.IsNullOrEmpty(english) || (language ?? Translator.CurrentLanguage) != "ar" ? english : Arabic(english, 0) ?? english;

    private static string? Arabic(string text, int depth)
    {
        foreach (var form in Compiled)
        {
            Match m;
            try { m = form.Pattern.Match(text); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success) continue;
            return Hole().Replace(form.Arabic, h =>
            {
                var value = m.Groups["p" + h.Groups[1].Value].Value;
                return form.TranslateValues && depth < 2 ? Arabic(value, depth + 1) ?? Translator.T(value, "ar") : value;
            });
        }
        return null;
    }

    /// <summary>Arabic stays readable in an exported cell; characters that matter in HTML are still escaped.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Arabic) };

    /// <summary>
    /// An activity entry's changes JSON for the reader: a lone <c>{"Summary":{"to":"…"}}</c> (AuditLog.Summary) gets its line in the reader's
    /// language; field changes and everything else come back exactly as stored.
    /// </summary>
    [return: NotNullIfNotNull(nameof(changesJson))]
    public static string? Changes(string? changesJson, string? language = null)
    {
        if (string.IsNullOrEmpty(changesJson) || (language ?? Translator.CurrentLanguage) != "ar" || !changesJson.Contains("\"Summary\"", StringComparison.Ordinal))
            return changesJson;
        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return changesJson;
            var entries = doc.RootElement.EnumerateObject().ToList();
            if (entries.Count != 1 || entries[0].Name != "Summary" || entries[0].Value.ValueKind != JsonValueKind.Object) return changesJson;
            var fields = entries[0].Value.EnumerateObject().ToList();
            if (fields.Count != 1 || fields[0].Name != "to" || fields[0].Value.ValueKind != JsonValueKind.String) return changesJson;
            var english = fields[0].Value.GetString()!;
            var shown = Show(english, "ar");
            return shown == english ? changesJson
                : JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> { ["Summary"] = new() { ["to"] = shown } }, Json);
        }
        catch (JsonException) { return changesJson; }
    }

    [GeneratedRegex(@"\{(\d)\}")]
    private static partial Regex Hole();
}
