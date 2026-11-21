using System.Net.Http.Json;
using FourC.Cmms.Api.Features.Import;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportExportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static SpreadsheetBook Read(byte[] bytes) => SpreadsheetReader.Read(new MemoryStream(bytes), "book.xlsx");

    private async Task<HttpClient> WithCustomFieldAsync()
    {
        var c = await NewOrganizationAsync(fx);
        await OkAsync(await c.PostAsJsonAsync("/api/custom-fields", new { entityType = "Asset", key = "power", label = "Power (kW)", dataType = "Number", isRequired = false, sortOrder = 1, showInList = false }));
        return c;
    }

    [SkippableFact]
    public async Task Current_data_imports_back_with_no_changes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await WithCustomFieldAsync();
        await RunAsync(c, Book(
            new Sheet("Sites", ["Code", "Name", "City", "Time zone"], ["CAI", "Cairo Plant", "Cairo", "Africa/Cairo"]),
            new Sheet("Locations", ["Site code", "Code", "Name", "Parent code", "Type"], ["CAI", "B1", "Building 1", null, "Building"], ["CAI", "R1", "Room 1", "B1", "Room"]),
            new Sheet("Suppliers", ["Code", "Name", "Currency", "Contractor"], ["SKF", "SKF Distribution", "USD", "No"]),
            new Sheet("Stores", ["Code", "Name", "Site code"], ["ST1", "Main store", "CAI"]),
            new Sheet("Assets", ["Asset number", "Name", "Site code", "Location code", "Category code", "Purchase price", "Commissioned on", "Power (kW)"],
                ["PUMP-01", "Main pump", "CAI", "R1", "MECH", 1250.5m, new DateOnly(2024, 3, 1), 7.5m]),
            new Sheet("People", ["Code", "Full name", "Trades", "Sites", "Hourly rate"], ["E1", "Ahmed Hassan", "MECH, ELEC", "CAI", 12.5m]),
            new Sheet("Parts", ["Part number", "Name", "Unit", "Standard price", "Preferred supplier code"], ["BRG-6205", "Ball bearing", "EA", 6.8m, "SKF"]),
            new Sheet("Stock", ["Part number", "Store code", "Bin", "Re-order level", "Opening quantity"], ["BRG-6205", "ST1", "A-1", 5m, 24m]),
            new Sheet("PM tasks", ["Code", "Name", "Estimated hours"], ["LUB-M", "Monthly lubrication", 1.5m]),
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease bearings"], ["LUB-M", 2m, "Check belts"]),
            new Sheet("PM parts", ["Task code", "Part number", "Quantity"], ["LUB-M", "BRG-6205", 2m]),
            new Sheet("PM schedules", ["Code", "Name", "Task code", "Site code", "Asset number", "Schedule", "Every", "Unit", "Start date", "Assignees"],
                ["PM-LUB-01", "Pump lubrication", "LUB-M", "CAI", "PUMP-01", "Every", 3m, "Month", new DateOnly(2026, 12, 1), "E1"])));

        var current = await (await c.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var check = await CheckAsync(c, current);
        foreach (var sheet in check.GetProperty("sheets").EnumerateArray())
        {
            var name = sheet.GetProperty("sheet").GetString();
            Assert.True(Count(sheet, "new") == 0 && Count(sheet, "changed") == 0 && Count(sheet, "problems") == 0,
                $"{name}: {sheet.GetProperty("rows")}");
        }
        Assert.Equal(15, check.GetProperty("sheets").GetArrayLength());
    }

    [SkippableFact]
    public async Task Text_with_windows_line_breaks_round_trips_unchanged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await OkAsync(await c.PostAsJsonAsync("/api/suppliers", new { code = "SKF", name = "SKF", notes = "Line 1\r\nLine 2", isContractor = false, isActive = true }));
        var current = await (await c.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var suppliers = SheetOf(await CheckAsync(c, current), "Suppliers");
        Assert.Equal((0, 1), (Count(suppliers, "changed"), Count(suppliers, "unchanged")));
    }

    [SkippableFact]
    public async Task The_template_has_every_sheet_and_heading()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await WithCustomFieldAsync();
        var book = Read(await (await c.GetAsync("/api/import/template")).Content.ReadAsByteArrayAsync());
        Assert.Equal(16, book.Sheets.Count);
        Assert.Equal("Help", book.Sheets[^1].Name);
        var assets = book.Sheets.Single(s => s.Name == "Assets");
        Assert.Contains("Power (kW)", assets.Rows[0]);
        Assert.Single(assets.Rows);
    }

    [SkippableFact]
    public async Task The_template_follows_the_persons_language()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/import/template");
        request.Headers.AcceptLanguage.ParseAdd("ar");
        var book = Read(await (await c.SendAsync(request)).Content.ReadAsByteArrayAsync());
        Assert.Equal("المواقع", book.Sheets[0].Name);
        Assert.Equal("الرمز", book.Sheets[0].Rows[0][0]);
    }

    [SkippableFact]
    public async Task Problem_rows_come_back_with_their_reason()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var file = Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null], ["ALX", null, "Alexandria"]));
        var response = await PostAsync(c, "/api/import/problems", file);
        response.EnsureSuccessStatusCode();
        var sites = Read(await response.Content.ReadAsByteArrayAsync()).Sheets.Single(s => s.Name == "Sites");
        Assert.Equal(2, sites.Rows.Count);
        Assert.Equal(["Code", "Name", "City", "Problem"], sites.Rows[0].Cast<string>());
        Assert.Equal("ALX", sites.Rows[1][0]);
        Assert.Equal("Name is required.", sites.Rows[1][3]);
    }
}
