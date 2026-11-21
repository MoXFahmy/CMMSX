using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static byte[] SitesAndLocations() => Book(
        new Sheet("Sites", SiteHeadings, ["CAI", "Cairo Plant", "Cairo"]),
        new Sheet("Locations", LocationHeadings, ["CAI", "B1", "Building 1", null], ["CAI", "R1", "Room 1", "B1"]));

    private static async Task<JsonElement> Sites(HttpClient c) => await OkAsync(await c.GetAsync("/api/sites"));

    [SkippableFact]
    public async Task A_check_saves_nothing_and_an_import_saves()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);

        var check = await CheckAsync(c, SitesAndLocations());
        Assert.False(check.GetProperty("saved").GetBoolean());
        Assert.Equal(1, Count(SheetOf(check, "Sites"), "new"));
        Assert.Equal(2, Count(SheetOf(check, "Locations"), "new"));
        Assert.Equal(0, (await Sites(c)).GetArrayLength());

        var run = await RunAsync(c, SitesAndLocations());
        Assert.True(run.GetProperty("saved").GetBoolean());
        var site = (await Sites(c)).EnumerateArray().Single();
        var locations = (await OkAsync(await c.GetAsync($"/api/locations?siteId={site.GetProperty("id")}"))).GetProperty("items").EnumerateArray().ToList();
        var b1 = locations.Single(l => l.GetProperty("code").GetString() == "B1");
        var r1 = locations.Single(l => l.GetProperty("code").GetString() == "R1");
        Assert.Equal(b1.GetProperty("id").GetGuid(), r1.GetProperty("parentId").GetGuid());
        Assert.Equal("Building 1 › Room 1", r1.GetProperty("pathName").GetString());
    }

    [SkippableFact]
    public async Task A_known_code_updates_and_a_blank_keeps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, SitesAndLocations());

        var result = await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["cai", "Cairo Main", null])));
        var sites = SheetOf(result, "Sites");
        Assert.Equal(1, Count(sites, "changed"));
        var change = sites.GetProperty("rows")[0].GetProperty("changes").EnumerateArray().Single();
        Assert.Equal(("Name", "Cairo Plant", "Cairo Main"), (change.GetProperty("column").GetString(), change.GetProperty("old").GetString(), change.GetProperty("new").GetString()));
        var site = (await Sites(c)).EnumerateArray().Single();
        Assert.Equal(("CAI", "Cairo Main", "Cairo"), (site.GetProperty("code").GetString(), site.GetProperty("name").GetString(), site.GetProperty("city").GetString()));
    }

    [SkippableFact]
    public async Task Moving_a_location_keeps_the_paths_below_it_right()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]),
            new Sheet("Locations", LocationHeadings, ["CAI", "B1", "Building 1", null], ["CAI", "F1", "Floor 1", "B1"])));

        // One file: a new area, the building moved under it, its floor (unchanged) and a new room on that floor.
        await RunAsync(c, Book(new Sheet("Locations", LocationHeadings,
            ["CAI", "X", "East area", null], ["CAI", "B1", "Building 1", "X"], ["CAI", "F1", "Floor 1", "B1"], ["CAI", "R9", "Room 9", "F1"])));
        var site = (await Sites(c))[0].GetProperty("id");
        var locations = (await OkAsync(await c.GetAsync($"/api/locations?siteId={site}"))).GetProperty("items").EnumerateArray().ToList();
        string PathOf(string code) => locations.Single(l => l.GetProperty("code").GetString() == code).GetProperty("pathName").GetString()!;
        Assert.Equal("East area › Building 1 › Floor 1", PathOf("F1"));
        Assert.Equal("East area › Building 1 › Floor 1 › Room 9", PathOf("R9"));
    }

    [SkippableFact]
    public async Task Codes_and_headings_match_ignoring_case_and_spaces()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, SitesAndLocations());
        var result = await RunAsync(c, Book(new Sheet("  locations ", [" site CODE ", "CODE", "name"], ["cai", "b1", "Main building"])));
        var sheet = SheetOf(result, "Locations");
        Assert.Equal(1, Count(sheet, "changed"));
        Assert.Equal(0, Count(sheet, "problems"));
        Assert.Empty(sheet.GetProperty("ignoredColumns").EnumerateArray());
    }

    [SkippableFact]
    public async Task Blank_rows_are_skipped()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await CheckAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null], [null, null, null], ["ALX", "Alexandria", null], [null, "", null])));
        var sheet = SheetOf(result, "Sites");
        Assert.Equal(2, Count(sheet, "new"));
        Assert.Equal(0, Count(sheet, "problems"));
        Assert.Equal([2, 4], sheet.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("row").GetInt32()));
    }

    [SkippableFact]
    public async Task A_key_twice_in_a_sheet_is_a_problem()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var sheet = SheetOf(await CheckAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null], ["CAI", "Cairo again", null]))), "Sites");
        Assert.Equal((1, 1), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.Contains("CAI appears twice in this sheet (row 2).", ProblemsOf(sheet));
    }

    [SkippableFact]
    public async Task Rows_depending_on_a_problem_row_are_problems()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await CheckAsync(c, Book(
            new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]),
            new Sheet("Locations", LocationHeadings, ["CAI", "B9", null, null], ["CAI", "R9", "Room 9", "B9"])));
        var sheet = SheetOf(result, "Locations");
        Assert.Equal(2, Count(sheet, "problems"));
        Assert.Contains("Name is required.", ProblemsOf(sheet));
        Assert.Contains("Location B9 not found.", ProblemsOf(sheet));
    }

    [SkippableFact]
    public async Task An_unknown_site_on_a_store_is_a_problem()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        string[] headings = ["Code", "Name", "Site code"];
        var bad = SheetOf(await CheckAsync(c, Book(new Sheet("Stores", headings, ["ST1", "Main store", "XX"]))), "Stores");
        Assert.Contains("Site XX not found.", ProblemsOf(bad));
        var good = SheetOf(await CheckAsync(c, Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]), new Sheet("Stores", headings, ["ST1", "Main store", "CAI"]))), "Stores");
        Assert.Equal(1, Count(good, "new"));
    }

    [SkippableFact]
    public async Task Unchanged_rows_are_not_written()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var file = Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", "Cairo"]));
        await RunAsync(c, file);
        var id = (await Sites(c))[0].GetProperty("id").GetGuid();
        var before = await UpdatedAtAsync(id);
        var again = SheetOf(await RunAsync(c, file), "Sites");
        Assert.Equal((0, 1), (Count(again, "changed"), Count(again, "unchanged")));
        Assert.Equal(before, await UpdatedAtAsync(id));
    }

    private async Task<DateTimeOffset?> UpdatedAtAsync(Guid siteId)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        return await db.Sites.IgnoreQueryFilters().Where(s => s.Id == siteId).Select(s => s.UpdatedAt).SingleAsync();
    }

    [SkippableFact]
    public async Task A_stopped_run_saves_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var me = await OkAsync(await c.GetAsync("/api/auth/me"));
        var tenantId = me.GetProperty("tenant").GetProperty("id").GetGuid();
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        var ctx = new FixedTenantContext(tenantId);
        await using var db = new CmmsDbContext(options, ctx);
        var book = SpreadsheetReader.Read(new MemoryStream(SitesAndLocations()), "data.xlsx");

        var result = await ImportRun.RunAsync(db, ctx, book, save: true, budget: TimeSpan.Zero, CancellationToken.None);
        Assert.False(result.Saved);
        Assert.Equal("This file is too big to import in one go. Split it into smaller files.", result.Stopped);
        Assert.Equal(0, (await Sites(c)).GetArrayLength());
    }

    [SkippableFact]
    public async Task A_run_that_runs_out_of_time_after_its_last_row_saves_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var tenantId = (await OkAsync(await c.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        var ctx = new FixedTenantContext(tenantId);
        await using var db = new CmmsDbContext(options, ctx);
        var book = SpreadsheetReader.Read(new MemoryStream(Book(new Sheet("Sites", SiteHeadings, ["CAI", "Cairo", null]))), "data.xlsx");

        // In time for the start, the sheet and its one row; out of time from then on (finishing, saving, committing).
        var calls = 0;
        TimeSpan Clock() => ++calls > 3 ? TimeSpan.FromSeconds(100) : TimeSpan.Zero;
        var result = await ImportRun.RunAsync(db, ctx, book, save: true, budget: TimeSpan.FromSeconds(45), Clock, CancellationToken.None);

        Assert.False(result.Saved);
        Assert.NotNull(result.Stopped);
        Assert.Equal(0, (await Sites(c)).GetArrayLength());
    }

    [SkippableFact]
    public async Task Only_administrators_can_import()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = await NewOrganizationAsync(fx);
        var roles = await OkAsync(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Technician").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await OkAsync(await admin.PostAsJsonAsync("/api/users", new { fullName = "Tech", email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u" }));
        var tech = fx.Client();
        await OkAsync(await tech.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(tech, "/api/import/check", SitesAndLocations())).StatusCode);
    }

    [SkippableFact]
    public async Task Limits_are_enforced()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var many = Enumerable.Range(1, 5001).Select(i => new object?[] { $"S{i}", $"Site {i}", null }).ToArray();
        var tooMany = await PostAsync(c, "/api/import/check", Book(new Sheet("Sites", SiteHeadings, many)));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("the limit is 5,000 per sheet", await tooMany.Content.ReadAsStringAsync());

        var big = await PostAsync(c, "/api/import/check", new byte[11 * 1024 * 1024]);
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Contains("Choose a file of up to 10 MB.", await big.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Arabic_headings_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book(
            new Sheet("المواقع", ["الرمز", "الاسم", "المدينة"], ["CAI", "مصنع القاهرة", "القاهرة"]),
            new Sheet("الأماكن", ["رمز الموقع", "الرمز", "الاسم", "رمز المكان الأعلى"], ["CAI", "B1", "المبنى 1", null])));
        Assert.Equal(1, Count(SheetOf(result, "Sites"), "new"));
        Assert.Equal(1, Count(SheetOf(result, "Locations"), "new"));
        Assert.Equal("مصنع القاهرة", (await Sites(c))[0].GetProperty("name").GetString());
    }
}
