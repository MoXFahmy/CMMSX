using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features.Import;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: Arabic columns beside the main texts in the Excel import, the empty template and the current-data download.</summary>
public class BilingualImportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static async Task<(Guid Id, string Number)> NumberedAssetAsync(HttpClient c, Guid site, string? nameAr = null)
    {
        var id = await AssetAsync(c, site, nameAr: nameAr);
        return (id, S(await Ok(await c.GetAsync($"/api/assets/{id}")), "assetNumber")!);
    }

    [SkippableFact]
    public async Task Import_fills_arabic_columns()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var (asset, number) = await NumberedAssetAsync(o.Admin, o.Site);
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", ["Check coupling"]);
        await RunAsync(o.Admin, Book(
            new Sheet("Assets", ["Asset number", "Name (Arabic)", "Notes to technician (Arabic)"], [number, "مضخة جديدة", "افصل أولاً"]),
            new Sheet("Work templates", ["Code", "Name (Arabic)", "Title (Arabic)", "Safety notes (Arabic)"], ["CHK", "فحص المبرد", "فحص دوري", "اقفل المضخة"]),
            new Sheet("PM steps", ["Task code", "Step", "Description", "Description (Arabic)"], ["CHK", 1, "Check coupling", "افحص الوصلة"])));
        var a = await Ok(await o.Admin.GetAsync($"/api/assets/{asset}"));
        Assert.Equal(("مضخة جديدة", "افصل أولاً"), (S(a, "nameAr"), S(a, "notesToTechnicianAr")));
        Assert.Equal(Pump, S(a, "name"));
        var t = await Ok(await o.Admin.GetAsync($"/api/pm-tasks/{task}"));
        Assert.Equal(("فحص المبرد", "فحص دوري", "اقفل المضخة"), (S(t, "nameAr"), S(t, "titleAr"), S(t, "safetyNotesAr")));
        Assert.Equal("افحص الوصلة", S(t.GetProperty("stepDetails").EnumerateArray().Single(), "descriptionAr"));
    }

    [SkippableFact]
    public async Task Arabic_headings_are_read_too()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var (asset, number) = await NumberedAssetAsync(o.Admin, o.Site);
        await RunAsync(o.Admin, Book(new Sheet("الأصول", ["رقم الأصل", "الاسم (عربي)"], [number, "مضخة جديدة"])));
        Assert.Equal("مضخة جديدة", S(await Ok(await o.Admin.GetAsync($"/api/assets/{asset}")), "nameAr"));
    }

    [SkippableFact]
    public async Task Blank_arabic_cell_keeps_the_current_text()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var (asset, number) = await NumberedAssetAsync(o.Admin, o.Site, PumpAr);
        await RunAsync(o.Admin, Book(new Sheet("Assets", ["Asset number", "Name", "Name (Arabic)"], [number, "Chiller pump 2B", null])));
        var a = await Ok(await o.Admin.GetAsync($"/api/assets/{asset}"));
        Assert.Equal(("Chiller pump 2B", PumpAr), (S(a, "name"), S(a, "nameAr")));
        await RunAsync(o.Admin, Book(new Sheet("Assets", ["Asset number", "Name"], [number, "Chiller pump 2C"])));
        Assert.Equal(PumpAr, S(await Ok(await o.Admin.GetAsync($"/api/assets/{asset}")), "nameAr"));
    }

    [SkippableFact]
    public async Task Current_data_download_includes_arabic_columns()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var (asset, number) = await NumberedAssetAsync(o.Admin, o.Site, PumpAr);
        var bytes = await (await SendAsync(o.Admin, HttpMethod.Get, "/api/import/current", "ar")).Content.ReadAsByteArrayAsync();
        var book = SpreadsheetReader.Read(new MemoryStream(bytes), "current.xlsx");
        var sheet = book.Sheets.Single(x => x.Name is "Assets" or "الأصول");
        var heads = sheet.Rows[0].Select(x => x?.ToString()).ToList();
        var nameAr = heads.FindIndex(h => h is "Name (Arabic)" or "الاسم (عربي)");
        Assert.True(nameAr >= 0, string.Join(" | ", heads));
        var row = sheet.Rows.Skip(1).Single(r => r[0]?.ToString() == number);
        Assert.Equal(PumpAr, row[nameAr]?.ToString());
        // Re-importing the download unchanged writes nothing, and the main name stays the main name (Review Focus 1).
        var check = SheetOf(await CheckAsync(o.Admin, bytes), "Assets");
        Assert.Equal((0, 0), (Count(check, "changed"), Count(check, "problems")));
        await RunAsync(o.Admin, bytes);
        Assert.Equal(Pump, S(await Ok(await o.Admin.GetAsync($"/api/assets/{asset}")), "name"));
    }

    [SkippableFact]
    public async Task Too_long_arabic_cell_is_a_problem_row()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var (_, number) = await NumberedAssetAsync(o.Admin, o.Site);
        var result = await CheckAsync(o.Admin, Book(new Sheet("Assets", ["Asset number", "Name (Arabic)"], [number, new string('م', 301)])));
        var sheet = SheetOf(result, "Assets");
        Assert.Equal(1, Count(sheet, "problems"));
        Assert.Contains(ProblemsOf(sheet), p => p.Contains("Name (Arabic)"));
    }
}
