using System.Text;
using System.Text.RegularExpressions;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Tests;

/// <summary>
/// One statement that lifts the site filter (<c>AllSites()</c>, <c>IgnoreQueryFilters(</c>) and still leans on it. A lift applies to the WHOLE query,
/// so a visibility test or a second set inside the same query silently loses the limits too (0.9.1 filter leaks; 0.26.1-3, where
/// <c>db.Assets.Any(...)</c> next to <c>db.Assets.AllSites()</c> could never say "hidden"). The scan reads one C# statement at a time (split on
/// <c>;</c> outside strings and comments) and reports a statement when:
/// <list type="number">
/// <item>it lifts a filter and also names a site-visibility helper (<c>SiteAccess.VisibleAsync</c>, <c>Access.Covers</c>, <c>CurrentSiteIds</c>,
/// <c>CurrentBranchPaths</c>, <c>.Visible(</c>); or</item>
/// <item>a query that lifts a filter holds another <c>db.&lt;Set&gt;</c> inside it: in the arguments of its own method chain
/// (<c>db.A.AllSites().Where(x =&gt; db.B.Any(...))</c>) or inside the same <c>(from ... select ...)</c> query. A second set that lifts the filter itself
/// is explicit and not reported, and two separate queries in one statement (<c>a.AllSites().AnyAsync() || b.AllSites().AnyAsync()</c>) are not nested.</item>
/// </list>
/// Rule 2 is what catches the 0.26.1-3 statement: the helper list alone never matched it.
/// <para><b>What this guard does NOT prove.</b> It is a text heuristic, not a data-flow check. Known blind spots:
/// (a) it recognises a set only as <c>db.</c>, <c>scoped.</c> or <c>Db.</c> followed by the set's name, so a context held in another variable name
/// (<c>ctx.Assets</c>, <c>c.Context.Assets</c>) is invisible; (b) it splits on <c>;</c>, so a lambda with a block body is cut into pieces and a lift in
/// one piece is not joined to a set used in another; (c) a lifted query held in a variable and used in a LATER statement
/// (<c>var q = db.A.AllSites(); ... q.Where(x => db.B.Any(...))</c>) is never joined; (d) it only looks inside the API and Core projects' source
/// files, not at generated or runtime-built queries, and not at raw SQL; (e) an entry in the allow-list vouches for the statement as it was when
/// someone read it. Passing means "no statement of the shapes above is unreviewed", not "no query leaks".</para>
/// </summary>
internal static partial class FilterLiftScanner
{
    public sealed record Hit(string File, int Line, string Statement);

    [GeneratedRegex(@"\bAllSites\(\)|\bIgnoreQueryFilters\(")] private static partial Regex Lift();
    [GeneratedRegex(@"SiteAccess\.VisibleAsync|Access\.Covers|\bCurrentSiteIds\b|\bCurrentBranchPaths\b|\.Visible\(")] private static partial Regex Helper();
    [GeneratedRegex(@"\b(?:db|scoped|Db)\.([A-Z]\w*)")] private static partial Regex SetRef();

    /// <summary>The names of the context's sets (a <c>db.CurrentTenantId</c> is not one).</summary>
    private static readonly HashSet<string> Sets = typeof(CmmsDbContext).GetProperties()
        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)).Select(p => p.Name).ToHashSet();

    public static IReadOnlyList<Hit> Scan(string source, string fileName)
    {
        var hits = new List<Hit>();
        foreach (var (line, text) in Statements(source))
        {
            if (!Lift().IsMatch(text)) continue;
            if (Helper().IsMatch(text) || NestsAnotherSet(text)) hits.Add(new Hit(fileName, line, Squash(text)));
        }
        return hits;
    }

    private static string Squash(string text)
    {
        var one = Regex.Replace(text, @"\s+", " ");
        return one.Length <= 160 ? one : one[..160] + "…";
    }

    /// <summary>Rule 2: a lifted query that holds a reference to another set (or the same set again) inside itself.</summary>
    private static bool NestsAnotherSet(string t)
    {
        foreach (Match lift in Lift().Matches(t))
        {
            var own = SetRef().Matches(t[..lift.Index]).LastOrDefault();
            var inner = new List<Match>();
            // (a) the arguments of the chain that follows the lift: .Where(x => db.B.Any(...)).Select(...)
            var pos = lift.Index + lift.Length;
            if (lift.Value.EndsWith('(')) pos = Close(t, pos - 1) + 1; // IgnoreQueryFilters( ... )
            while (true)
            {
                var next = SkipSpace(t, pos);
                if (next >= t.Length || t[next] != '.') break;
                pos = next + 1;
                while (pos < t.Length && (char.IsLetterOrDigit(t[pos]) || t[pos] is '_')) pos++;
                var open = SkipSpace(t, pos);
                if (open < t.Length && t[open] == '<') { open = SkipSpace(t, Angle(t, open) + 1); pos = open; }
                if (open < t.Length && t[open] == '(')
                {
                    var end = Close(t, open);
                    var args = t[(open + 1)..end];
                    inner.AddRange(SetRef().Matches(args).Select(m => Shift(m, open + 1, t)));
                    pos = end + 1;
                }
            }
            // (b) the same (from ... select ...) query
            var group = EnclosingGroup(t, lift.Index);
            if (group is { } g && t[(g.Start + 1)..].TrimStart().StartsWith("from ", StringComparison.Ordinal))
                inner.AddRange(SetRef().Matches(t[(g.Start + 1)..g.End]).Select(m => Shift(m, g.Start + 1, t)));
            foreach (var m in inner.DistinctBy(x => x.Index))
            {
                if (own is not null && m.Index == own.Index) continue;
                if (!Sets.Contains(m.Groups[1].Value)) continue;
                // A set that lifts the filter itself is explicit about it.
                var rest = t[m.Index..Math.Min(t.Length, m.Index + 140)];
                var stop = rest[3..].IndexOf("db.", StringComparison.Ordinal);
                if (Lift().IsMatch(stop < 0 ? rest : rest[..(stop + 3)])) continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>A match found inside a slice, as a match of the whole text.</summary>
    private static Match Shift(Match m, int offset, string whole) => SetRef().Match(whole, offset + m.Index);

    private static int SkipSpace(string t, int i) { while (i < t.Length && char.IsWhiteSpace(t[i])) i++; return i; }

    /// <summary>Index of the bracket closing the one at <paramref name="open"/> (strings are already blanked).</summary>
    private static int Close(string t, int open)
    {
        int depth = 0;
        for (var i = open; i < t.Length; i++)
        {
            if (t[i] is '(' or '[' or '{') depth++;
            else if (t[i] is ')' or ']' or '}' && --depth == 0) return i;
        }
        return t.Length - 1;
    }

    private static int Angle(string t, int open)
    {
        int depth = 0;
        for (var i = open; i < t.Length; i++)
        {
            if (t[i] == '<') depth++;
            else if (t[i] == '>' && --depth == 0) return i;
        }
        return open;
    }

    /// <summary>The innermost parentheses holding <paramref name="index"/>.</summary>
    private static (int Start, int End)? EnclosingGroup(string t, int index)
    {
        int depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            if (t[i] is ')' or ']' or '}') depth++;
            else if (t[i] is '(' or '[' or '{')
            {
                if (depth == 0) return t[i] == '(' ? (i, Close(t, i)) : null;
                depth--;
            }
        }
        return null;
    }

    /// <summary>The statements of a C# file: comments dropped, string and character contents blanked, split on <c>;</c>; each with its first line.</summary>
    public static IReadOnlyList<(int Line, string Text)> Statements(string src)
    {
        var result = new List<(int, string)>();
        var buf = new StringBuilder();
        int line = 1, start = 1, i = 0, n = src.Length;
        void Flush()
        {
            var text = buf.ToString().Trim();
            if (text.Length > 0) result.Add((start, text));
            buf.Clear();
        }
        while (i < n)
        {
            var c = src[i];
            if (buf.Length == 0 && char.IsWhiteSpace(c)) { if (c == '\n') line++; i++; continue; }
            if (buf.Length == 0) start = line;
            if (Starts(src, i, "//")) { while (i < n && src[i] != '\n') i++; continue; }
            if (Starts(src, i, "/*"))
            {
                var end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                line += src[i..end].Count(ch => ch == '\n');
                i = end; buf.Append(' '); continue;
            }
            var raw = RawStart().Match(src, i);
            if (raw.Success)
            {
                var quotes = raw.Groups[1].Value;
                var end = src.IndexOf(quotes, i + raw.Length, StringComparison.Ordinal);
                end = end < 0 ? n : end + quotes.Length;
                line += src[i..end].Count(ch => ch == '\n');
                i = end; buf.Append("\"\""); continue;
            }
            var quote = StringStart(src, i);
            if (quote is { } q)
            {
                i += q.Length;
                while (i < n)
                {
                    var d = src[i];
                    if (d == '\n') line++;
                    if (q.Verbatim && Starts(src, i, "\"\"")) { i += 2; continue; }
                    if (!q.Verbatim && d == '\\') { i += 2; continue; }
                    if (q.Interpolated && d == '{' && !Starts(src, i, "{{")) { i = SkipHole(src, i, ref line); continue; }
                    if (q.Interpolated && Starts(src, i, "{{")) { i += 2; continue; }
                    i++;
                    if (d == '"') break;
                }
                buf.Append("\"\""); continue;
            }
            if (c == '\'')
            {
                var j = i + 1;
                while (j < n && src[j] != '\'') j += src[j] == '\\' ? 2 : 1;
                i = j + 1; buf.Append("''"); continue;
            }
            if (c == ';') { Flush(); i++; continue; }
            if (c == '\n') line++;
            buf.Append(c); i++;
        }
        Flush();
        return result;
    }

    private static bool Starts(string s, int i, string what) => string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    [GeneratedRegex("\\G\\$*(\"{3,})")] private static partial Regex RawStart();

    private static (int Length, bool Verbatim, bool Interpolated)? StringStart(string s, int i)
    {
        foreach (var (prefix, verbatim) in new[] { ("$@\"", true), ("@$\"", true), ("$\"", false), ("@\"", true), ("\"", false) })
            if (Starts(s, i, prefix)) return (prefix.Length, verbatim, prefix.Contains('$'));
        return null;
    }

    /// <summary>Past the <c>{ expression }</c> hole of an interpolated string starting at <paramref name="i"/> (its own strings may hold quotes and braces).</summary>
    private static int SkipHole(string s, int i, ref int line)
    {
        int depth = 0;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\n') line++;
            if (c == '"')
            {
                for (i++; i < s.Length && s[i] != '"'; i++) if (s[i] == '\\') i++;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i + 1;
        }
        return i;
    }
}

/// <summary>
/// Guard (0.29.1): no query in the API or Core lifts the site filter and still depends on it, unless it is listed in
/// <c>guard-filter-lift-allowlist.txt</c> with a reason. A new entry needs a reviewer's agreement that the lift is deliberate and nothing
/// in the same query is meant to stay limited.
/// </summary>
public class GuardFilterLiftTests
{
    /// <summary>The 0.26.1-3 statement as it stood before the fix: <c>AllSites()</c> also lifted the limits from the <c>db.Assets.Any</c> that was meant to say "hidden".</summary>
    private const string OldContractPattern = """
        if (i.SiteId is { } contractSite)
        {
            var kept = existing is null ? [] : await (from ca in db.ContractAssets.AsNoTracking() join a in db.Assets.AllSites().AsNoTracking() on ca.AssetId equals a.Id
                                                      where ca.ContractId == existing.Id && !ids.Contains(a.Id) && !db.Assets.Any(x => x.Id == a.Id)
                                                      select new { a.Id, a.AssetNumber, a.SiteId }).ToListAsync(ct);
            foreach (var a in named.Concat(kept).Where(a => a.SiteId != contractSite))
                v.Add("assets", $"Asset {a.AssetNumber} is at another site than the contract.");
        }
        """;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION"))) dir = dir.Parent;
        Assert.True(dir is not null, "VERSION not found above the test's folder");
        return dir!.FullName;
    }

    private static IEnumerable<(string Relative, string Path)> Sources()
    {
        var server = Path.Combine(Root(), "server");
        foreach (var project in new[] { "FourC.Cmms.Api", "FourC.Cmms.Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(server, project), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(server, file).Replace('\\', '/');
                if (relative.Contains("/Migrations/") || relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                yield return (relative, file);
            }
    }

    private sealed record Entry(string File, int Line, string Fingerprint, string Reason, int Number);

    /// <summary>A line is <c>relative/path.cs:123 | the statement's first characters | the reason</c>; blank lines and lines starting with # are notes.</summary>
    private static List<Entry> AllowList()
    {
        var path = Path.Combine(Root(), "server", "FourC.Cmms.Tests", "guard-filter-lift-allowlist.txt");
        var entries = new List<Entry>();
        var lines = File.ReadAllLines(path);
        for (var n = 0; n < lines.Length; n++)
        {
            var text = lines[n].Trim();
            if (text.Length == 0 || text[0] == '#') continue;
            var parts = text.Split(" | ", 3, StringSplitOptions.TrimEntries);
            var head = parts.Length == 3 ? Regex.Match(parts[0], @"^(?<file>\S+\.cs):(?<line>\d+)$") : Match.Empty;
            Assert.True(head.Success && parts[1].Length > 0 && parts[2].Length > 0, $"allow-list line {n + 1} must be 'file.cs:line | start of the statement | reason': {text}");
            entries.Add(new Entry(head.Groups["file"].Value, int.Parse(head.Groups["line"].Value), parts[1], parts[2], n + 1));
        }
        return entries;
    }

    /// <summary>How far a statement may move before its entry no longer finds it: line numbers drift as the file is edited.</summary>
    private const int Drift = 10;

    /// <summary>How many characters of the statement an entry must repeat (whitespace squashed): a different statement at the same place is not the listed one.</summary>
    internal const int FingerprintLength = 40;

    internal static string Fingerprint(string statement) => statement.Length <= FingerprintLength ? statement : statement[..FingerprintLength];

    private static bool Fits(Entry e, FilterLiftScanner.Hit hit) =>
        e.File == hit.File && Math.Abs(e.Line - hit.Line) <= Drift && hit.Statement.StartsWith(e.Fingerprint, StringComparison.Ordinal);

    /// <summary>Pairs each hit with the nearest unused entry that fits it (same file, within <see cref="Drift"/> lines, same start of statement); what is left over on either side is the result.</summary>
    private static (List<FilterLiftScanner.Hit> Unlisted, List<Entry> Stale) Pair(IEnumerable<FilterLiftScanner.Hit> hits, List<Entry> entries)
    {
        var unused = entries.ToList();
        var unlisted = new List<FilterLiftScanner.Hit>();
        foreach (var hit in hits)
        {
            var entry = unused.Where(e => Fits(e, hit)).OrderBy(e => Math.Abs(e.Line - hit.Line)).FirstOrDefault();
            if (entry is null) unlisted.Add(hit); else unused.Remove(entry);
        }
        return (unlisted, unused);
    }

    private static (List<FilterLiftScanner.Hit> Unlisted, List<Entry> Stale) MatchSource() =>
        Pair(Sources().SelectMany(s => FilterLiftScanner.Scan(File.ReadAllText(s.Path), s.Relative)), AllowList());

    [Fact]
    public void A_different_statement_at_the_same_place_does_not_take_over_an_entry()
    {
        var listed = new FilterLiftScanner.Hit("A.cs", 100, "var n = await db.WorkOrderParts.AllSites().AnyAsync(l => db.WorkOrders.Any(w => w.Id == l.Id));");
        var entry = new Entry("A.cs", 100, Fingerprint(listed.Statement), "deliberate", 1);
        // the same statement a few lines down still matches (code above it moved)
        Assert.Empty(Pair([listed with { Line = 107 }], [entry]).Unlisted);
        // a new, different lift at the very same line is unlisted, and the old entry is left stale
        var other = listed with { Statement = "var rows = await db.Assets.AllSites().Where(a => db.Parts.Any(p => p.Id == a.PartId)).ToListAsync(ct);" };
        var (unlisted, stale) = Pair([other], [entry]);
        Assert.Single(unlisted);
        Assert.Single(stale);
        // too far away is no match either
        Assert.Single(Pair([listed with { Line = 140 }], [entry]).Unlisted);
    }

    [Fact]
    public void Scanner_finds_the_old_contract_pattern()
    {
        var hits = FilterLiftScanner.Scan(OldContractPattern, "ContractEndpoints.cs");
        var hit = Assert.Single(hits);
        Assert.Contains("db.Assets.AllSites()", hit.Statement);
        Assert.Equal(1, hit.Line); // the statement starts at the "if" that holds it
    }

    [Fact]
    public void Scanner_reads_one_statement_at_a_time_and_ignores_text_and_comments()
    {
        // The visibility helper in a different statement, a string, or a comment is not a lift that leans on it.
        const string safe = """
            var visible = await SiteAccess.VisibleAsync(db, "Asset", id, ct);
            var names = await db.Assets.AllSites().Where(a => ids.Contains(a.Id)).Select(a => a.Name).ToListAsync(ct);
            var text = "db.Assets.AllSites(); Access.Covers(x); db.Parts.Any()"; // db.Assets.AllSites().Where(a => db.Parts.Any())
            /* db.Assets.AllSites().Where(a => db.Parts.Any(p => p.Id == a.PartId)) */
            var number = await db.Parts.AllSites().AnyAsync(p => p.Id == id, ct) || await db.Assets.AllSites().AnyAsync(a => a.Id == id, ct);
            var both = await db.Assets.AllSites().Where(a => db.Parts.AllSites().Any(p => p.Id == a.PartId)).ToListAsync(ct);
            """;
        Assert.Empty(FilterLiftScanner.Scan(safe, "Safe.cs"));
    }

    [Fact]
    public void Scanner_finds_a_lift_next_to_a_visibility_helper_or_a_second_set()
    {
        Assert.Single(FilterLiftScanner.Scan("if (db.CurrentSiteIds is not null) Hide(await db.Sites.AllSites().Select(s => s.Code).ToListAsync(ct));", "A.cs"));
        Assert.Single(FilterLiftScanner.Scan("var ok = await db.Assets.AllSites().AnyAsync(a => a.Id == id && Access.Covers(access, a.SiteId, null), ct);", "B.cs"));
        Assert.Single(FilterLiftScanner.Scan("var n = await db.WorkOrderParts.AllSites().CountAsync(l => l.PartId == id && db.WorkOrders.Any(w => w.Id == l.WorkOrderId), ct);", "C.cs"));
        // an interpolated string with quotes inside does not hide what follows it
        Assert.Single(FilterLiftScanner.Scan("var n = $\"x {(a ? \"p\" : \"q\")}\"; var m = await db.WorkOrderParts.AllSites().AnyAsync(l => db.WorkOrders.Any(w => w.Id == l.WorkOrderId), ct);", "D.cs"));
    }

    [Fact]
    public void Source_tree_has_no_unlisted_filter_lifts()
    {
        var (unlisted, _) = MatchSource();
        Assert.True(unlisted.Count == 0,
            "A query lifts the site filter and also depends on it (or on another set), so the lift reaches that part too. Fix it with two queries, or list it in guard-filter-lift-allowlist.txt with a reason:\n"
            + string.Join("\n", unlisted.Select(h => $"{h.File}:{h.Line}  {h.Statement}")));
    }

    [Fact]
    public void Allow_list_entries_still_exist()
    {
        var (_, stale) = MatchSource();
        Assert.True(stale.Count == 0,
            "These allow-list entries match no statement any more (code moved or was fixed). Remove or update them:\n"
            + string.Join("\n", stale.Select(e => $"line {e.Number}: {e.File}:{e.Line}  {e.Reason}")));
    }

    [Fact]
    public void Allow_list_entries_give_a_real_reason()
    {
        foreach (var e in AllowList())
        {
            Assert.True(e.Reason.Length >= 25 && e.Reason.Contains(' '), $"allow-list line {e.Number} ({e.File}:{e.Line}) needs a one-line reason");
            Assert.True(Sources().Any(s => s.Relative == e.File), $"allow-list line {e.Number} names a file that is not scanned: {e.File}");
        }
    }
}
