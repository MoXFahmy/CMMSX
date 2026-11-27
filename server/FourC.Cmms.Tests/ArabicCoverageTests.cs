using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>
/// Finds the server's message texts in the API and Core source (0.29.2, ruling P3): the English texts that reach a person through the
/// translator at the boundary. A text is a message when it is handed straight to one of these:
/// <list type="bullet">
/// <item><c>new DomainException(…)</c> (also <c>NotEnoughStockException</c>, the phone's <c>Rejected</c>, <c>AiProblem(status, …)</c>,
/// <c>AiEndpoints.Problem(status, …)</c>);</item>
/// <item>a validator's <c>.Add(field, …)</c> (on <c>v</c>, <c>rv</c>, <c>rules</c>, <c>missing</c>, <c>check</c>) and <c>.When(condition, field, …)</c>;</item>
/// <item><c>.Required(field, value, label)</c> and <c>.MaxLength(field, value, label, max)</c>: the label goes into "{0} is required.", so it must be
/// an exact key of its own;</item>
/// <item><c>Results.Problem(title: …)</c> and <c>Translator.T(…)</c>.</item>
/// </list>
/// The text may be a literal, either branch of a <c>?:</c>, the first argument of <c>string.Format</c>, or the name of a <c>const string</c> (found
/// in the same file, or by <c>Class.Name</c>, or by a name only one const has). An interpolated string's holes become <c>{0}</c>, <c>{1}</c>… in
/// order, the way ar.json writes them.
/// <para><b>What this does NOT see.</b> Text built elsewhere and passed in a variable (<c>var warning = "…"; T(warning)</c>), returned from a method
/// (<c>Lists.Problem</c>, <c>WorkingHours.Problem</c>), assembled with <c>+</c>, or written in another shape (report column labels, e-mail facts,
/// push titles, export headings). Log templates, AI prompts and SQL are never in these contexts, so they are not scanned. Passing means
/// "every message text handed straight to the translator's boundary has Arabic", not "every word the server sends is Arabic".</para>
/// </summary>
internal static partial class ArabicTextScanner
{
    public sealed record Source(string File, string Text);

    /// <summary>One message text: where it is written, whether it is a label (needs an exact key), and the text with <c>{N}</c> for its holes.</summary>
    public sealed record Hit(string File, int Line, bool Label, string Text);

    /// <summary>A string literal: <see cref="Text"/> is null for raw strings (never messages here).</summary>
    private sealed record Literal(int Start, int Line, string? Text);

    private sealed record Parsed(string File, string Code, Dictionary<int, Literal> Literals, int[] LineStarts);

    private sealed record Const(string File, string? Type, string Name, Literal Value);

    [GeneratedRegex(@"\bnew\s+(?:[A-Za-z_][\w.]*\.)?(?<exc>DomainException|NotEnoughStockException|Rejected|AiProblem)\s*\(|\b(?<recv>v|rv|rules|missing|check)\s*\.\s*Add\s*\(|\.\s*(?<vm>When|Required|MaxLength)\s*\(|\bTranslator\s*\.\s*T\s*\(|\bResults\s*\.\s*Problem\s*\(|\bAiEndpoints\s*\.\s*Problem\s*\(")]
    private static partial Regex Call();

    [GeneratedRegex(@"\bconst\s+string\s+(?<name>[A-Za-z_]\w*)\s*=\s*")] private static partial Regex ConstDeclaration();
    [GeneratedRegex(@"\b(?:class|record|struct|interface)\s+(?<name>[A-Za-z_]\w*)")] private static partial Regex TypeDeclaration();
    [GeneratedRegex(@"\G[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*")] private static partial Regex Identifier();
    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)\s*:(?!:)")] private static partial Regex NamedArgument();
    [GeneratedRegex(@"\G\$*(""{3,})")] private static partial Regex RawStart();
    [GeneratedRegex(@"\{\d+\}")] private static partial Regex Hole();

    /// <summary>Every message text of the files, once per place it is written.</summary>
    public static IReadOnlyList<Hit> Scan(IReadOnlyList<Source> sources)
    {
        var parsed = sources.Select(Parse).ToList();
        var consts = parsed.SelectMany(Consts).ToList();
        var hits = new List<Hit>();
        var seen = new HashSet<Hit>();
        foreach (var p in parsed)
        {
            foreach (Match m in Call().Matches(p.Code))
            {
                var kind = m.Groups["exc"].Success ? m.Groups["exc"].Value : m.Groups["recv"].Success ? "Add" : m.Groups["vm"].Success ? m.Groups["vm"].Value
                    : m.Value.Contains("Translator") ? "T" : m.Value.Contains("AiEndpoints") ? "AiEndpoints.Problem" : "Results.Problem";
                var args = Arguments(p.Code, m.Index + m.Length - 1);
                (int Start, int End)? message = null;
                if (kind == "Results.Problem")
                {
                    foreach (var a in args)
                        if (NamedArgument().Match(p.Code[a.Start..a.End]) is { Success: true } n && n.Groups["name"].Value == "title") message = (a.Start + n.Length, a.End);
                }
                else
                {
                    var index = kind switch { "AiProblem" or "AiEndpoints.Problem" or "Add" => 1, "When" or "Required" or "MaxLength" => 2, _ => 0 };
                    if (args.Count > index && !NamedArgument().IsMatch(p.Code[args[index].Start..args[index].End])) message = args[index];
                }
                if (message is not { } span) continue;
                var label = kind is "Required" or "MaxLength";
                foreach (var item in Direct(p.Code, span.Start, span.End))
                {
                    var found = new List<(string File, Literal Value)>();
                    if (item.Literal is { } at) { if (p.Literals.TryGetValue(at, out var lit)) found.Add((p.File, lit)); }
                    else found.AddRange(Resolve(consts, p.File, item.Name!).Select(c => (c.File, c.Value)));
                    foreach (var (file, literal) in found)
                    {
                        if (literal.Text is null || !Hole().Replace(literal.Text, "").Any(char.IsLetter)) continue;
                        var hit = new Hit(file, literal.Line, label, literal.Text);
                        if (seen.Add(hit)) hits.Add(hit);
                    }
                }
            }
        }
        return hits;
    }

    /// <summary>The literals (by start) and names at the top of an argument: through parentheses, <c>?:</c>, a <c>switch</c> expression's arms and <c>string.Format</c>'s first argument.</summary>
    private static List<(int? Literal, string? Name)> Direct(string code, int start, int end)
    {
        var found = new List<(int?, string?)>();
        var i = start;
        while (i < end)
        {
            var c = code[i];
            if (c == '"' || (c is '$' or '@' && i + 1 < end && code[i + 1] is '"' or '$' or '@'))
            {
                found.Add((i, null));
                var j = i;
                while (j < end && code[j] is '$' or '@') j++;
                var raw = RawStart().Match(code, j);
                if (raw.Success)
                {
                    var close = code.IndexOf(raw.Groups[1].Value, j + raw.Length, StringComparison.Ordinal);
                    i = close < 0 ? end : close + raw.Groups[1].Length;
                    continue;
                }
                var quote = code.IndexOf('"', j + 1); // the contents are blanked, so the next quote closes it
                i = quote < 0 ? end : quote + 1;
                continue;
            }
            if (c is '(' or '[' or '{')
            {
                var close = Close(code, i);
                var before = code[start..i].TrimEnd();
                var callee = Regex.Match(before, @"([A-Za-z_][\w.]*)$").Groups[1].Value;
                if (c == '(' && (callee is "" or "return")) found.AddRange(Direct(code, i + 1, close));
                else if (c == '(' && callee is "string.Format" or "String.Format") { var first = Arguments(code, i)[0]; found.AddRange(Direct(code, first.Start, first.End)); }
                else if (c == '{' && callee == "switch") found.AddRange(Direct(code, i + 1, close));
                i = close + 1;
                continue;
            }
            if ((char.IsLetter(c) || c == '_') && (i == start || !(char.IsLetterOrDigit(code[i - 1]) || code[i - 1] is '_' or '.')))
            {
                var m = Identifier().Match(code, i);
                var after = code.AsSpan(m.Index + m.Length).TrimStart();
                if (!after.StartsWith("(") && !after.StartsWith("<")) found.Add((null, m.Value));
                i = m.Index + m.Length;
                continue;
            }
            i++;
        }
        return found;
    }

    /// <summary>The string consts a name may mean: in the same file, or of the named class, or the only one by that name.</summary>
    private static IEnumerable<Const> Resolve(List<Const> consts, string file, string name)
    {
        var parts = name.Split('.');
        var last = parts[^1];
        if (parts.Length > 1) return consts.Where(c => c.Name == last && c.Type == parts[^2]);
        var same = consts.Where(c => c.Name == last && c.File == file).ToList();
        if (same.Count > 0) return same;
        var all = consts.Where(c => c.Name == last).ToList();
        return all.Count == 1 ? all : [];
    }

    private static IEnumerable<Const> Consts(Parsed p)
    {
        // Each type's body, so a const knows the innermost type around it.
        var types = new List<(string Name, int Start, int End)>();
        foreach (Match t in TypeDeclaration().Matches(p.Code))
        {
            var i = t.Index + t.Length;
            while (i < p.Code.Length && p.Code[i] is not ('{' or ';'))
                i = p.Code[i] is '(' or '<' or '[' ? Close(p.Code, i) + 1 : i + 1;
            if (i < p.Code.Length && p.Code[i] == '{') types.Add((t.Groups["name"].Value, i, Close(p.Code, i)));
        }
        foreach (Match m in ConstDeclaration().Matches(p.Code))
        {
            if (!p.Literals.TryGetValue(m.Index + m.Length, out var value)) continue; // const string A = B.C;
            var type = types.Where(t => t.Start < m.Index && m.Index < t.End).OrderByDescending(t => t.Start).Select(t => t.Name).FirstOrDefault();
            yield return new Const(p.File, type, m.Groups["name"].Value, value);
        }
    }

    /// <summary>The arguments of the call whose '(' is at <paramref name="open"/>, as spans of the blanked code.</summary>
    private static List<(int Start, int End)> Arguments(string code, int open)
    {
        var close = Close(code, open);
        var result = new List<(int, int)>();
        int depth = 0, from = open + 1;
        for (var i = open + 1; i < close; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0) { result.Add((from, i)); from = i + 1; }
        }
        result.Add((from, close));
        return result;
    }

    /// <summary>Index of the bracket closing the one at <paramref name="open"/> (strings, characters and comments are already blanked).</summary>
    private static int Close(string t, int open)
    {
        var (opening, closing) = t[open] switch { '(' => ('(', ')'), '[' => ('[', ']'), '<' => ('<', '>'), _ => ('{', '}') };
        if (opening == '<')
        {
            int angle = 0;
            for (var i = open; i < t.Length; i++)
            {
                if (t[i] == '<') angle++;
                else if (t[i] == '>' && --angle == 0) return i;
                else if (t[i] is ';' or '{') return i - 1;
            }
            return t.Length - 1;
        }
        int depth = 0;
        for (var i = open; i < t.Length; i++)
        {
            if (t[i] is '(' or '[' or '{') depth++;
            else if (t[i] is ')' or ']' or '}' && --depth == 0) return i;
        }
        return t.Length - 1;
    }

    /// <summary>
    /// The file with comments and the insides of strings and characters turned into spaces (same length, same lines), and its literals by
    /// where they start (at the <c>$</c> or <c>@</c> of a prefix).
    /// </summary>
    private static Parsed Parse(Source source)
    {
        var src = source.Text;
        var code = src.ToCharArray();
        var literals = new Dictionary<int, Literal>();
        int n = src.Length, i = 0, line = 1;
        void Blank(int from, int to) { for (var k = from; k < to && k < n; k++) if (code[k] != '\n') code[k] = ' '; }
        int Lines(int from, int to) { var count = 0; for (var k = from; k < to && k < n; k++) if (src[k] == '\n') count++; return count; }
        while (i < n)
        {
            var c = src[i];
            if (c == '\n') { line++; i++; continue; }
            if (Starts(src, i, "//")) { var e = src.IndexOf('\n', i); e = e < 0 ? n : e; Blank(i, e); i = e; continue; }
            if (Starts(src, i, "/*")) { var e = src.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2; line += Lines(i, e); Blank(i, e); i = e; continue; }
            if (c is '$' or '"' && (i == 0 || !(char.IsLetterOrDigit(src[i - 1]) || src[i - 1] == '_')) && RawStart().Match(src, i) is { Success: true } raw)
            {
                var quotes = raw.Groups[1].Value;
                var e = src.IndexOf(quotes, i + raw.Length, StringComparison.Ordinal);
                e = e < 0 ? n : e + quotes.Length;
                literals[i] = new Literal(i, line, null);
                Blank(i + raw.Length, e - quotes.Length);
                line += Lines(i, e);
                i = e;
                continue;
            }
            if (StringStart(src, i) is { } q)
            {
                var start = i;
                var startLine = line;
                var text = new StringBuilder();
                var holes = 0;
                i += q.Length;
                while (i < n)
                {
                    var d = src[i];
                    if (q.Verbatim && Starts(src, i, "\"\"")) { text.Append('"'); i += 2; continue; }
                    if (d == '"') { i++; break; }
                    if (!q.Verbatim && d == '\\' && i + 1 < n) { i = Escape(src, i, text); continue; }
                    if (q.Interpolated && Starts(src, i, "{{")) { text.Append('{'); i += 2; continue; }
                    if (q.Interpolated && Starts(src, i, "}}")) { text.Append('}'); i += 2; continue; }
                    if (q.Interpolated && d == '{') { var e = SkipHole(src, i); line += Lines(i, e); i = e; text.Append('{').Append(holes++).Append('}'); continue; }
                    if (d == '\n') line++;
                    text.Append(d);
                    i++;
                }
                literals[start] = new Literal(start, startLine, text.ToString());
                Blank(start + q.Length, i - 1);
                continue;
            }
            if (c == '\'')
            {
                var j = i + 1;
                while (j < n && src[j] != '\'' && src[j] != '\n') j += src[j] == '\\' ? 2 : 1;
                Blank(i + 1, j);
                i = j + 1;
                continue;
            }
            i++;
        }
        var starts = new List<int> { 0 };
        for (var k = 0; k < n; k++) if (src[k] == '\n') starts.Add(k + 1);
        return new Parsed(source.File, new string(code), literals, [.. starts]);
    }

    private static int Escape(string s, int i, StringBuilder text)
    {
        var e = s[i + 1];
        switch (e)
        {
            case 'n': text.Append('\n'); return i + 2;
            case 't': text.Append('\t'); return i + 2;
            case 'r': text.Append('\r'); return i + 2;
            case '0': text.Append('\0'); return i + 2;
            case 'u' when i + 6 <= s.Length && int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                text.Append((char)code); return i + 6;
            default: text.Append(e); return i + 2;
        }
    }

    private static bool Starts(string s, int i, string what) => string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    private static (int Length, bool Verbatim, bool Interpolated)? StringStart(string s, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_') && s[i] != '"') return null;
        foreach (var (prefix, verbatim) in new[] { ("$@\"", true), ("@$\"", true), ("$\"", false), ("@\"", true), ("\"", false) })
            if (Starts(s, i, prefix)) return (prefix.Length, verbatim, prefix.Contains('$'));
        return null;
    }

    /// <summary>Past the <c>{ expression }</c> hole of an interpolated string starting at <paramref name="i"/> (its own strings may hold quotes and braces).</summary>
    private static int SkipHole(string s, int i)
    {
        int depth = 0;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"')
            {
                for (i++; i < s.Length && s[i] != '"'; i++) if (s[i] == '\\') i++;
                continue;
            }
            if (c == '\'')
            {
                for (i++; i < s.Length && s[i] != '\''; i++) if (s[i] == '\\') i++;
                continue;
            }
            if (c is '{' or '(' or '[') depth++;
            else if (c is '}' or ')' or ']' && --depth == 0 && c == '}') return i + 1;
        }
        return i;
    }
}

/// <summary>
/// Guard (0.29.2): every server message text has Arabic in Localization/ar.json, all through: an Arabic result that still holds English words
/// (a catch-all template or one known sentence translated around new English) does not count. A text without full Arabic fails the build
/// unless it is listed in <c>arabic-coverage-allowlist.txt</c> with a reason (a line CMX stores in English and translates when shown, Task 4;
/// text that becomes part of a record people edit). See <see cref="ArabicTextScanner"/> for what is scanned and what is not.
/// </summary>
public class ArabicCoverageTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION"))) dir = dir.Parent;
        Assert.True(dir is not null, "VERSION not found above the test's folder");
        return dir!.FullName;
    }

    private static IEnumerable<ArabicTextScanner.Source> Sources()
    {
        var server = Path.Combine(Root(), "server");
        foreach (var project in new[] { "FourC.Cmms.Api", "FourC.Cmms.Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(server, project), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(server, file).Replace('\\', '/');
                if (relative.Contains("/Migrations/") || relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                yield return new ArabicTextScanner.Source(relative, File.ReadAllText(file));
            }
    }

    private static readonly Lazy<IReadOnlyList<ArabicTextScanner.Hit>> All = new(() => ArabicTextScanner.Scan(Sources().ToList()));

    /// <summary>The Arabic catalogue the server ships (the embedded ar.json): English key to Arabic.</summary>
    private static readonly Lazy<Dictionary<string, string>> Catalogue = new(() =>
    {
        using var stream = typeof(Translator).Assembly.GetManifestResourceStream("FourC.Cmms.Api.Localization.ar.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    /// <summary>The text with a sample value in each hole, as the translator would see it at run time ("X" is too short to count as English left over).</summary>
    internal static string Sample(string text) => Regex.Replace(text, @"\{\d+\}", "X");

    /// <summary>
    /// Latin words the Arabic catalogue keeps on purpose: the product's name, report abbreviations, file types, a date format, a content type
    /// and an example time zone. Taken from the ar.json values as they are (<see cref="Kept_latin_words_come_from_the_catalogue"/> checks
    /// each one is still used there); anything else in Latin letters in an Arabic result is English left over.
    /// </summary>
    internal static readonly string[] KeptLatin =
        ["multipart/form-data", "yyyy-mm-dd", "Africa/Cairo", "4CSQL", "CMX", "MTBF", "MTTR", "JPEG", "PNG", "WebP", "WebM", "MOV", "PDF", "Excel", "Word", "xlsx", "csv"];

    /// <summary>The runs of three or more Latin letters left in an Arabic result, apart from <see cref="KeptLatin"/>.</summary>
    internal static List<string> EnglishLeft(string arabic)
    {
        foreach (var kept in KeptLatin) arabic = Regex.Replace(arabic, $"(?<![A-Za-z]){Regex.Escape(kept)}(?![A-Za-z])", " ");
        return Regex.Matches(arabic, "[A-Za-z]{3,}").Select(m => m.Value).ToList();
    }

    /// <summary>What an Arabic reader gets: a label's own entry (it is put into "{0} is required.", so it needs an exact key), else the translated sample; null when nothing changes.</summary>
    internal static string? ArabicOf(ArabicTextScanner.Hit h)
    {
        if (h.Label) return Catalogue.Value.TryGetValue(h.Text, out var own) ? own : null;
        var sample = Sample(h.Text);
        var arabic = Translator.T(sample, "ar");
        return arabic == sample ? null : arabic;
    }

    /// <summary>Arabic all through: translated, and no English words left in the result.</summary>
    internal static bool HasArabic(ArabicTextScanner.Hit h) => ArabicOf(h) is { } arabic && EnglishLeft(arabic).Count == 0;

    internal static string Show(ArabicTextScanner.Hit h) =>
        $"{h.File}:{h.Line} \"{h.Text}\"" + (ArabicOf(h) is { } partly ? $"  (English left in the Arabic: {string.Join(", ", EnglishLeft(partly))})" : "");

    private sealed record Entry(string File, string Fingerprint, string Reason, int Number);

    internal const int FingerprintLength = 40;

    /// <summary>The first 40 characters of the text (holes as {0}, whitespace squashed): what an allow-list entry repeats.</summary>
    internal static string Fingerprint(string text)
    {
        var one = Regex.Replace(text, @"\s+", " ").Trim();
        return one.Length <= FingerprintLength ? one : one[..FingerprintLength];
    }

    /// <summary>A line is <c>path-from-server/File.cs | first 40 characters of the text | the reason</c>; blank lines and lines starting with # are notes.</summary>
    private static List<Entry> AllowList()
    {
        var path = Path.Combine(Root(), "server", "FourC.Cmms.Tests", "arabic-coverage-allowlist.txt");
        var entries = new List<Entry>();
        var lines = File.ReadAllLines(path);
        for (var n = 0; n < lines.Length; n++)
        {
            var text = lines[n].Trim();
            if (text.Length == 0 || text[0] == '#') continue;
            var parts = text.Split(" | ", 3, StringSplitOptions.TrimEntries);
            Assert.True(parts.Length == 3 && parts[0].EndsWith(".cs", StringComparison.Ordinal) && parts[1].Length > 0 && parts[2].Length > 0,
                $"allow-list line {n + 1} must be 'File.cs | first 40 characters of the text | reason': {text}");
            entries.Add(new Entry(parts[0], parts[1], parts[2], n + 1));
        }
        return entries;
    }

    /// <summary>Texts without Arabic matched to entries (same file, same fingerprint; one entry covers that text wherever it is in the file). Left over on either side is the result.</summary>
    private static (List<ArabicTextScanner.Hit> Unlisted, List<Entry> Stale) Pair(IEnumerable<ArabicTextScanner.Hit> missing, List<Entry> entries)
    {
        var used = new HashSet<Entry>();
        var unlisted = new List<ArabicTextScanner.Hit>();
        foreach (var hit in missing)
        {
            var entry = entries.FirstOrDefault(e => e.File == hit.File && e.Fingerprint == Fingerprint(hit.Text));
            if (entry is null) unlisted.Add(hit); else used.Add(entry);
        }
        return (unlisted, entries.Where(e => !used.Contains(e)).ToList());
    }

    private static (List<ArabicTextScanner.Hit> Unlisted, List<Entry> Stale) MatchSource() => Pair(All.Value.Where(h => !HasArabic(h)), AllowList());

    [Fact]
    public void Every_message_has_arabic()
    {
        var (unlisted, _) = MatchSource();
        Assert.True(unlisted.Count == 0,
            $"{unlisted.Count} server text(s) have no Arabic. Add each to server/FourC.Cmms.Api/Localization/ar.json (key = the exact English, {{0}} for a value; a label needs its own key), "
            + "or list it in arabic-coverage-allowlist.txt with a reason:\n" + string.Join("\n", unlisted.Select(Show)));
    }

    [Fact]
    public void Scan_reaches_the_known_messages()
    {
        // A scan that silently found nothing would pass everything: it must see the messages of every kind it claims to.
        output.WriteLine($"{All.Value.Count} texts checked: {All.Value.Count(h => h.Label)} labels, {All.Value.Count(h => !h.Label)} messages, "
            + $"{All.Value.Count(h => h.Text.Contains("{0}"))} with values, in {All.Value.Select(h => h.File).Distinct().Count()} files.");
        Assert.True(All.Value.Count > 800, $"only {All.Value.Count} texts found");
        Assert.Contains(All.Value, h => h.Label && h.Text == "Supplier's part number");
        Assert.Contains(All.Value, h => !h.Label && h.Text == "One or more validation errors occurred.");
        Assert.Contains(All.Value, h => h.Text == "Choose a range of up to six weeks.");                 // new DomainException("…")
        Assert.Contains(All.Value, h => h.Text == "Not enough stock: {0} has {1} {2} in {3}.");           // interpolated, in Core
        Assert.Contains(All.Value, h => h.Text == "This point isn't active.");                           // a const passed by name
    }

    [Fact]
    public void Allow_list_entries_still_match()
    {
        var (_, stale) = MatchSource();
        Assert.True(stale.Count == 0,
            "These allow-list entries match no text without Arabic any more (the text moved, changed, or now has Arabic). Remove or update them:\n"
            + string.Join("\n", stale.Select(e => $"line {e.Number}: {e.File} | {e.Fingerprint}")));
    }

    [Fact]
    public void Allow_list_entries_give_a_real_reason()
    {
        var files = Sources().Select(s => s.File).ToHashSet();
        foreach (var e in AllowList())
        {
            Assert.True(e.Reason.Length >= 15 && e.Reason.Contains(' '), $"allow-list line {e.Number} ({e.File}) needs a one-line reason");
            Assert.True(files.Contains(e.File), $"allow-list line {e.Number} names a file that is not scanned: {e.File}");
        }
    }

    [Fact]
    public void Scanner_finds_a_new_message_and_the_guard_reports_it()
    {
        var hit = Assert.Single(ArabicTextScanner.Scan([new("New.cs", "if (x) throw new DomainException(\"Brand new text.\");")]));
        Assert.Equal(new ArabicTextScanner.Hit("New.cs", 1, false, "Brand new text."), hit);
        Assert.False(HasArabic(hit));
        var (unlisted, stale) = Pair([hit], []);
        Assert.Equal([hit], unlisted);
        Assert.Empty(stale);
        // Listed, it passes; the entry is used, not stale.
        var listed = Pair([hit], [new Entry("New.cs", Fingerprint(hit.Text), "a reason for the test", 1)]);
        Assert.Empty(listed.Unlisted);
        Assert.Empty(listed.Stale);
    }

    /// <summary>Review fix: a catch-all template or a known sentence wrapped around new English is not Arabic.</summary>
    [Fact]
    public void Partly_english_arabic_is_reported()
    {
        const string planted = """
            if (a) throw new DomainException($"Widget {code} not found.");
            if (b) v.Add("lines", $"Line {n}: Something new happened.");
            if (c) throw new DomainException("Choose an active person. Then ring the bell twice.");
            """;
        var hits = ArabicTextScanner.Scan([new("Planted.cs", planted)]);
        Assert.Equal(3, hits.Count);
        foreach (var hit in hits)
        {
            // The translator does change each one (a catch-all template, or the known first sentence), but English is left in it.
            Assert.NotNull(ArabicOf(hit));
            Assert.NotEmpty(EnglishLeft(ArabicOf(hit)!));
            Assert.False(HasArabic(hit));
        }
        Assert.Equal(hits, Pair(hits, []).Unlisted);
        // Whole Arabic, and Arabic that keeps a deliberate Latin word, still pass.
        Assert.True(HasArabic(new("A.cs", 1, false, "Site {0} not found.")));
        Assert.True(HasArabic(new("A.cs", 1, false, "Upload must be multipart/form-data.")));
        Assert.True(HasArabic(new("A.cs", 1, true, "Supplier's part number")));
    }

    [Fact]
    public void Kept_latin_words_come_from_the_catalogue()
    {
        // The exceptions stay a short list of words ar.json really keeps; a word dropped from every Arabic text must leave the list too.
        foreach (var kept in KeptLatin)
            Assert.True(Catalogue.Value.Values.Any(v => Regex.IsMatch(v, $"(?<![A-Za-z]){Regex.Escape(kept)}(?![A-Za-z])")), $"{kept} is in no Arabic text any more");
        // And the catalogue's own Arabic holds no other English.
        var leftovers = Catalogue.Value.Where(kv => EnglishLeft(kv.Value).Count > 0).Select(kv => $"\"{kv.Key}\": {string.Join(", ", EnglishLeft(kv.Value))}").ToList();
        Assert.True(leftovers.Count == 0, "These Arabic texts hold English words:\n" + string.Join("\n", leftovers));
    }

    [Fact]
    public void Scanner_reads_message_contexts_and_their_texts()
    {
        const string source = """
            public static class Demo
            {
                private const string Gone = "It is gone now.";
                public const string Format = "Only {0} left.";
                void Run(Validator v, ILogger log, List<string> list)
                {
                    // throw new DomainException("In a comment.");
                    log.LogWarning("Mail {Id} failed to send.", id);
                    list.Add("Not a validator add.");
                    var s = "throw new DomainException(\"Inside a string.\")";
                    v.Add("name", $"{name} is broken now.");
                    v.Required("code", i.Code, "Code label").MaxLength("notes", i.Notes, "Notes label", 100);
                    v.When(i.X < 0, "x", ok ? "Yes it is." : "No it isn't.");
                    if (a) return Results.Problem(title: Translator.T("Title text."), statusCode: 400);
                    if (b) return Results.Problem(statusCode: 409, title: "Bare title.");
                    if (c) throw new FourC.Cmms.Core.Common.DomainException(Gone);
                    if (d) throw new DomainException(string.Format(Format, 3));
                    if (e) throw new AiProblem(409, Other.Named);
                    var t = Translator.T(kind switch { 1 => "One kind.", _ => "Other kind." });
                }
            }
            public static class Other { public const string Named = "From another class."; }
            """;
        var hits = ArabicTextScanner.Scan([new("Demo.cs", source)]);
        Assert.Equal(new[]
        {
            ("{0} is broken now.", false, 11), ("Code label", true, 12), ("Notes label", true, 12), ("Yes it is.", false, 13), ("No it isn't.", false, 13),
            ("Title text.", false, 14), ("Bare title.", false, 15), ("It is gone now.", false, 3), ("Only {0} left.", false, 4), ("From another class.", false, 22),
            ("One kind.", false, 19), ("Other kind.", false, 19),
        }.OrderBy(x => x.Item1, StringComparer.Ordinal), hits.Select(h => (h.Text, h.Label, h.Line)).OrderBy(x => x.Item1, StringComparer.Ordinal));
    }
}
