using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Puts the server's messages (errors, validation, report texts, e-mails) into the person's language. The English text is
/// the key; Localization/ar.json maps it to Arabic, with {0}, {1}… standing for the values inside a message, so "Batch B1
/// expired on 2026-11-20…" keeps its batch and date. A value that is itself a known phrase (a field name, a status) is
/// translated too. Text the catalog doesn't know stays in English.
/// </summary>
public static partial class Translator
{
    private static readonly Catalog Arabic = Catalog.Load("ar");

    /// <summary>The language of the current request (from the app's Accept-Language), else English.</summary>
    public static string CurrentLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";

    /// <summary>Speaks <paramref name="language"/> until disposed (an e-mail built for its recipient, whatever the request's language).</summary>
    public static IDisposable Use(string language)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == "ar" ? "ar" : "en");
        return new Restore(before);
    }

    private sealed class Restore(CultureInfo before) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = before;
    }

    /// <summary>
    /// Dates in the current language: Arabic month names with the digits 0-9, else "21 Nov 2026". A date inside a sentence is formatted
    /// here before the sentence goes to <see cref="T"/> (an interpolated <c>{day:d MMM}</c> would always be English).
    /// </summary>
    public static string Date(DateOnly d, string format = "d MMM yyyy") => d.ToString(format, CurrentLanguage == "ar" ? CultureInfo.GetCultureInfo("ar-EG") : CultureInfo.InvariantCulture);

    public static string T(string? english, string? language = null)
    {
        if (string.IsNullOrEmpty(english)) return english ?? "";
        return (language ?? CurrentLanguage) == "ar" ? Arabic.Translate(english, 0) : english;
    }

    private sealed partial class Catalog(Dictionary<string, string> exact, List<(Regex Pattern, string Arabic)> patterns)
    {
        public static Catalog Load(string language)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"FourC.Cmms.Api.Localization.{language}.json")
                ?? throw new InvalidOperationException($"Missing translations for {language}.");
            var all = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            var exact = all.Where(kv => !Placeholder().IsMatch(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            // Templates with the most fixed text first, so "{0} is required." doesn't swallow longer ones.
            var patterns = all.Where(kv => Placeholder().IsMatch(kv.Key))
                .OrderByDescending(kv => Placeholder().Replace(kv.Key, "").Length)
                .Select(kv => (new Regex("^" + Placeholder().Replace(Regex.Escape(kv.Key).Replace(@"\{", "{"), m => $"(?<p{m.Groups[1].Value}>.+?)") + "$",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchLimit), kv.Value)).ToList();
            return new Catalog(exact, patterns);
        }

        public string Translate(string text, int depth)
        {
            if (exact.TryGetValue(text, out var hit)) return hit;
            if (depth > 2) return text;
            foreach (var (pattern, arabic) in patterns)
            {
                Match m;
                try { m = pattern.Match(text); }
                catch (RegexMatchTimeoutException) { continue; } // a very long value: try the next template rather than wait
                if (!m.Success) continue;
                return Placeholder().Replace(arabic, p => m.Groups["p" + p.Groups[1].Value] is { Success: true } g ? Translate(g.Value, depth + 1) : p.Value);
            }
            // Several messages joined into one ("Choose a site. Enter the hours worked."): each sentence on its own.
            var sentences = Sentence().Split(text);
            return sentences.Length > 1 && depth == 0 ? string.Join(" ", sentences.Select(s => Translate(s, depth + 1))) : text;
        }

        /// <summary>Templates hold one to three wildcards; on a long typed-in value one could take a while to rule out.</summary>
        private static readonly TimeSpan MatchLimit = TimeSpan.FromMilliseconds(50);

        [GeneratedRegex(@"\{(\d)\}")]
        private static partial Regex Placeholder();

        [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
        private static partial Regex Sentence();
    }
}
