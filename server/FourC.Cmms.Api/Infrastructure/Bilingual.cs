using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// 0.28.0: a record's main text or its Arabic twin, by the reader's language (the request's, or the one set with
/// <see cref="Translator.Use"/>). Arabic readers see the Arabic text when there is one; everyone else sees the main text.
/// In database queries write the same choice inline — <c>var ar = Bilingual.Arabic;</c> before the query, then
/// <c>ar &amp;&amp; x.NameAr != null ? x.NameAr : x.Name</c> — so it runs as one SQL CASE (blank Arabic texts are stored as null).
/// </summary>
public static class Bilingual
{
    /// <summary>The reader reads Arabic.</summary>
    public static bool Arabic => Translator.CurrentLanguage == Languages.Arabic;

    /// <summary>The text to show: the Arabic text for Arabic readers when it isn't blank, else the main text.</summary>
    public static string Pick(string main, string? ar) => Arabic && !string.IsNullOrWhiteSpace(ar) ? ar : main;

    /// <summary><see cref="Pick"/> for texts that may be missing (notes).</summary>
    public static string? PickNote(string? main, string? ar) => Arabic && !string.IsNullOrWhiteSpace(ar) ? ar : main;

    /// <summary>The text not shown (for searching both): the main text when the Arabic one is shown, else the Arabic text (or null).</summary>
    public static string? Other(string main, string? ar) =>
        string.IsNullOrWhiteSpace(ar) ? null : Arabic ? main : ar;

    /// <summary>
    /// A text that must never be hidden (safety notes, 0.29.2): the reader's own language when it has text, else the other language;
    /// a blank text (empty or only spaces) counts as missing. <paramref name="arabic"/> names the reader when it isn't the request's
    /// (an e-mail written for its recipient). In database queries write the same choice inline.
    /// </summary>
    public static string? Either(string? main, string? ar, bool? arabic = null)
    {
        var (first, second) = arabic ?? Arabic ? (ar, main) : (main, ar);
        return !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;
    }
}
