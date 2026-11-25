using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: picking a record's main or Arabic text by the reader's language.</summary>
public class BilingualTests
{
    [Fact]
    public void Pick_takes_arabic_for_arabic_readers()
    {
        using (Translator.Use("ar")) Assert.Equal("مضخة", Bilingual.Pick("Pump", "مضخة"));
    }

    [Fact]
    public void Pick_takes_main_for_english_readers()
    {
        using (Translator.Use("en")) Assert.Equal("Pump", Bilingual.Pick("Pump", "مضخة"));
    }

    [Fact]
    public void Pick_falls_back_to_main_when_arabic_is_blank()
    {
        using (Translator.Use("ar"))
        {
            Assert.Equal("Pump", Bilingual.Pick("Pump", null));
            Assert.Equal("Pump", Bilingual.Pick("Pump", "  "));
        }
    }

    [Fact]
    public void PickNote_handles_missing_main()
    {
        using (Translator.Use("ar"))
        {
            Assert.Null(Bilingual.PickNote(null, null));
            Assert.Equal("افصل أولاً", Bilingual.PickNote("Isolate first", "افصل أولاً"));
        }
    }

    [Fact]
    public void Other_is_the_text_not_shown()
    {
        using (Translator.Use("ar"))
        {
            Assert.Equal("Pump", Bilingual.Other("Pump", "مضخة"));
            Assert.Null(Bilingual.Other("Pump", null));
        }
        using (Translator.Use("en")) Assert.Equal("مضخة", Bilingual.Other("Pump", "مضخة"));
    }

    [Fact]
    public void Arabic_is_true_only_inside_an_arabic_scope()
    {
        using (Translator.Use("ar")) Assert.True(Bilingual.Arabic);
        using (Translator.Use("en")) Assert.False(Bilingual.Arabic);
    }
}
