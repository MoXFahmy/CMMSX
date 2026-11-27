using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>0.29.2 (Arabic polish): server texts that had no Arabic (0.17.0-10, 0.18.0-7) and dates inside sentences in the reader's months (0.19.0-4).</summary>
public class ArabicPolishTests
{
    [Theory]
    [InlineData("Person")]
    [InlineData("Supplier's part number")]
    [InlineData("Pack")]
    [InlineData("Action taken")]
    [InlineData("Prevention")]
    [InlineData("Bin")]
    [InlineData("Planned after its due date ({0}).")]
    [InlineData("As of {0}")]
    [InlineData("As of {0} · at average cost")]
    [InlineData("As of {0} · values in base currency")]
    [InlineData("Enter a description or choose a part.")]
    [InlineData("Cost centre {0} not found.")]
    [InlineData("Line {0}: {1}")]
    [InlineData("{0} has an invalid value.")]
    [InlineData("{0} {1} not found.")]
    [InlineData("Select between 1 and {0} {1}.")]
    [InlineData("work orders")]
    [InlineData("assets")]
    [InlineData("parts")]
    [InlineData("PM schedules")]
    [InlineData("Save the file as .xlsx (Excel workbook) or .csv first.")]
    [InlineData("This file can't be read as an Excel workbook.")]
    [InlineData("Unknown list: {0}.")]
    [InlineData("Unknown value: {0}.")]
    [InlineData("Could not generate a unique {0} number. Check the numbering settings.")]
    public void New_texts_have_arabic(string english) => Assert.NotEqual(english, Translator.T(english, "ar"));

    [Theory]
    [InlineData("Select between 1 and 500 work orders.")]
    [InlineData("Select between 1 and 500 assets.")]
    [InlineData("Select between 1 and 500 parts.")]
    [InlineData("Select between 1 and 500 PM schedules.")]
    [InlineData("Cost centre CC1 not found.")]
    public void Composed_texts_are_arabic_all_through(string english) => Assert.DoesNotMatch("[a-z]{3,}", Translator.T(english, "ar"));

    [Fact]
    public void Dates_take_a_format_and_the_readers_months()
    {
        var day = new DateOnly(2026, 11, 27);
        using (Translator.Use("en"))
        {
            Assert.Equal("27 Nov", Translator.Date(day, "d MMM"));
            Assert.Equal("27 Nov 2026", Translator.Date(day));
        }
        using (Translator.Use("ar"))
        {
            var shortDate = Translator.Date(day, "d MMM");
            Assert.Contains("نوفمبر", shortDate);
            Assert.StartsWith("27", shortDate);
            Assert.DoesNotContain("2026", shortDate);
            Assert.Contains("نوفمبر 2026", Translator.Date(day));
        }
    }
}
