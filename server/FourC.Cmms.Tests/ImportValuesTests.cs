using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Core.Assets;

namespace FourC.Cmms.Tests;

public class ImportValuesTests
{
    private static ColumnDef Col(ValueKind kind, Type? choices = null) => new("x", "Thing", "الشيء", kind, Choices: choices);

    private static object? Read(object? cell, ColumnDef col)
    {
        Assert.True(ImportValues.TryRead(cell, col, out var value, out var problem), problem);
        return value;
    }

    private static string Problem(object? cell, ColumnDef col)
    {
        Assert.False(ImportValues.TryRead(cell, col, out _, out var problem));
        return problem!;
    }

    [Fact]
    public void Numeric_cells_read_as_plain_text_where_text_is_expected()
    {
        var text = Col(ValueKind.Text);
        Assert.Equal("123", Read(123m, text));
        Assert.Equal("00123", Read("00123", text));
        Assert.Equal("12.5", Read(12.50m, text));
        Assert.Equal("PUMP-01", Read("  PUMP-01 ", text));
        Assert.Null(Read("   ", text));
        Assert.Null(Read(null, text));
    }

    [Fact]
    public void Dates_read_from_cells_and_text()
    {
        var date = Col(ValueKind.Date);
        Assert.Equal(new DateOnly(2026, 11, 21), Read(new DateOnly(2026, 11, 21), date));
        Assert.Equal(new DateOnly(2026, 11, 21), Read("2026-11-21", date));
        Assert.Equal("Thing must be a date (yyyy-mm-dd).", Problem("21/11/2026", date));
        Assert.Equal(7.5m, Read("7.5", Col(ValueKind.Number)));
        Assert.Equal("Thing must be a number.", Problem("seven", Col(ValueKind.Number)));
        // A comma is never a thousands separator or a decimal mark here: "12,5" must not become 125.
        Assert.Equal("Thing must be a number, with . for decimals and no thousands separators.", Problem("12,5", Col(ValueKind.Number)));
        Assert.Equal("Thing must be a number, with . for decimals and no thousands separators.", Problem("1,250", Col(ValueKind.Number)));
        Assert.Equal(3, Read(3m, Col(ValueKind.Integer)));
        Assert.Equal("Thing must be a whole number.", Problem(2.5m, Col(ValueKind.Integer)));
    }

    [Fact]
    public void Choices_accept_english_and_arabic_words()
    {
        var status = Col(ValueKind.Choice, typeof(AssetStatus));
        Assert.Equal(AssetStatus.Active, Read("active", status));
        Assert.Equal(AssetStatus.Active, Read("Active", status));
        Assert.Equal(AssetStatus.Active, Read("نشط", status));
        Assert.Equal(AssetStatus.UnderRepair, Read("UnderRepair", status));
        Assert.Equal(AssetStatus.UnderRepair, Read("under repair", status));
        var problem = Problem("broken", status);
        Assert.StartsWith("Thing must be one of: Active, Inactive, In storage, Under repair, Disposed", problem);
    }

    [Fact]
    public void Yes_no_and_lists()
    {
        var yes = Col(ValueKind.YesNo);
        Assert.Equal(true, Read("نعم", yes));
        Assert.Equal(false, Read(0m, yes));
        Assert.Equal(true, Read(true, yes));
        Assert.Equal(false, Read("no", yes));
        Assert.Equal("Thing must be Yes or No.", Problem("maybe", yes));
        Assert.Equal(new List<string> { "ELEC", "MECH" }, Read(" ELEC , MECH ,", Col(ValueKind.CodeList)));
    }

    [Fact]
    public void A_cut_off_export_never_splits_a_task_list()
    {
        Dictionary<string, object?> Row(string task, int step) => new() { ["taskCode"] = task, ["step"] = step };
        List<Dictionary<string, object?>> rows = [Row("A", 1), Row("A", 2), Row("A", 3), Row("B", 1), Row("B", 2), Row("B", 3)];
        var (kept, cut) = ImportWorkbook.WholeTasks(rows, 4);
        Assert.True(cut);
        Assert.Equal(["A", "A", "A"], kept.Select(r => (string)r["taskCode"]!));
        var (all, notCut) = ImportWorkbook.WholeTasks(rows, 6);
        Assert.False(notCut);
        Assert.Equal(6, all.Count);
    }

    [Fact]
    public void Sheets_are_found_by_english_or_arabic_name()
    {
        Assert.Equal(24, ImportSheets.All.Count); // 0.21.0 added brands and models
        Assert.Equal("AssetParts", ImportSheets.Find("قطع الأصول")?.Key);
        Assert.Equal("Assets", ImportSheets.Find(" assets ")?.Key);
        Assert.Equal("Assets", ImportSheets.Find("الأصول")?.Key);
        Assert.Equal("PmSchedules", ImportSheets.Find("PM  Schedules")?.Key);
        Assert.Equal("Assets", ImportSheets.Find("assets-november")?.Key);
        Assert.Null(ImportSheets.Find("Notes"));
        Assert.Equal("siteCode", ImportSheets.Find("Locations")!.FindColumn(" SITE code ")?.Key);
    }
}
