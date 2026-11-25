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
}
