using System.Globalization;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Api.Features.Import;

/// <summary>Turns a cell into the value its column expects, or says what's wrong with it (in the request's language).</summary>
public static class ImportValues
{
    private static readonly string[] Yes = ["yes", "true", "1", "y", "نعم"];
    private static readonly string[] No = ["no", "false", "0", "n", "لا"];

    /// <summary>A blank cell reads as null with no problem. Otherwise the value's type follows the column's kind.</summary>
    public static bool TryRead(object? cell, ColumnDef col, out object? value, out string? problem)
    {
        value = null; problem = null;
        if (cell is null || cell is string s0 && string.IsNullOrWhiteSpace(s0)) return true;
        var text = cell switch
        {
            string s => s.Trim(),
            decimal m => Plain(m),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            _ => Convert.ToString(cell, CultureInfo.InvariantCulture)?.Trim() ?? "",
        };
        switch (col.Kind)
        {
            case ValueKind.Text:
                value = text.Replace("\r\n", "\n");
                return true;
            case ValueKind.Number:
                if (cell is decimal num) { value = num; return true; }
                // No thousands separators: in a comma-decimal file "12,5" would otherwise read as 125.
                if (cell is string && !text.Contains(',') && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) { value = parsed; return true; }
                problem = Translator.T(text.Contains(',') ? $"{col.Heading} must be a number, with . for decimals and no thousands separators." : $"{col.Heading} must be a number.");
                return false;
            case ValueKind.Integer:
                if ((cell is decimal whole && whole == decimal.Truncate(whole) && whole is >= int.MinValue and <= int.MaxValue))
                {
                    value = (int)(decimal)cell; return true;
                }
                if (cell is string && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; }
                problem = Translator.T($"{col.Heading} must be a whole number.");
                return false;
            case ValueKind.Date:
                if (cell is DateOnly date) { value = date; return true; }
                if (cell is string && DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyy-M-d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) { value = d2; return true; }
                problem = Translator.T($"{col.Heading} must be a date (yyyy-mm-dd).");
                return false;
            case ValueKind.YesNo:
                if (cell is bool flag) { value = flag; return true; }
                var low = text.ToLowerInvariant();
                if (Yes.Contains(low)) { value = true; return true; }
                if (No.Contains(low)) { value = false; return true; }
                problem = Translator.T($"{col.Heading} must be Yes or No.");
                return false;
            case ValueKind.Choice:
                var type = col.Choices ?? throw new InvalidOperationException($"Column {col.Key} has no choices.");
                var wanted = ImportSheets.Normal(text);
                foreach (Enum option in Enum.GetValues(type))
                {
                    var words = new[] { option.ToString(), ImportSheets.ChoiceWord(option), Translator.T(ImportSheets.ChoiceWord(option), "ar") };
                    if (words.Any(w => ImportSheets.Normal(w) == wanted)) { value = option; return true; }
                }
                var allowed = string.Join(", ", Enum.GetValues(type).Cast<Enum>().Select(ImportSheets.ChoiceText));
                problem = Translator.T($"{col.Heading} must be one of: {allowed}.");
                return false;
            case ValueKind.CodeList:
                value = text.Split([',', '،', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                return true;
            default:
                throw new InvalidOperationException($"Unknown kind {col.Kind}.");
        }
    }

    /// <summary>How a value shows in the preview's "old → new": dates yyyy-mm-dd, plain numbers, words in the request's language.</summary>
    public static string? Show(object? value) => value switch
    {
        null => null,
        string s => s,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => Plain(m),
        double f => Plain((decimal)f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => Translator.T(b ? "Yes" : "No"),
        Enum e => ImportSheets.ChoiceText(e),
        IEnumerable<string> list => string.Join(", ", list),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>12.50 → "12.5", 123.0 → "123".</summary>
    public static string Plain(decimal m) => m.ToString("0.############", CultureInfo.InvariantCulture);
}
