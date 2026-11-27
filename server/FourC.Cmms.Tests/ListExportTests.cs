using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace FourC.Cmms.Tests;

public class ListExportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";

    private async Task<(HttpClient admin, Guid site)> OrgAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Export Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        return (c, site);
    }

    private async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var email = Addr(role.Replace(' ', '-').ToLowerInvariant());
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u" }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return c;
    }

    private static async Task<string> NewWorkOrderAsync(HttpClient c, Guid site, string title, string priority) =>
        (await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, type = "Corrective", priority, siteId = site }))).GetProperty("number").GetString()!;

    private static async Task<string> SheetAsync(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync()));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> CsvAsync(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
        return Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
    }

    [SkippableFact]
    public async Task Work_order_export_uses_filters_columns_and_titles()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var high = await NewWorkOrderAsync(admin, site, "Fix the chiller", "High");
        var low = await NewWorkOrderAsync(admin, site, "Paint the fence", "Low");
        var r = await admin.GetAsync("/api/work-orders/export?format=xlsx&priority=High&c=number&t=No&c=priority&t=Pri&c=title&t=Job");
        var sheet = await SheetAsync(r);
        Assert.Contains("<t>Pri</t>", sheet);
        Assert.Contains(high, sheet);
        Assert.Contains("Fix the chiller", sheet);
        Assert.DoesNotContain(low, sheet);
        Assert.Contains("filename=work-orders-", r.Content.Headers.ContentDisposition?.ToString());
    }

    [SkippableFact]
    public async Task Csv_export_of_work_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await NewWorkOrderAsync(admin, site, "Grease the bearings", "Medium");
        var csv = await CsvAsync(await admin.GetAsync("/api/work-orders/export?format=csv&c=number&t=Number&c=title&t=Title&c=status&t=Status"));
        Assert.StartsWith("Number,Title,Status\r\n", csv);
        Assert.Contains(",Grease the bearings,Open\r\n", csv);
    }

    [SkippableFact]
    public async Task Request_export_shows_a_requester_only_their_own()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        var requester = await UserAsync(admin, "Requester");
        await Ok(await admin.PostAsJsonAsync("/api/work-requests", new { title = "Admin's leak", siteId = site, priority = "High", type = "Corrective" }));
        await Ok(await requester.PostAsJsonAsync("/api/work-requests", new { title = "My broken light", siteId = site, priority = "Low", type = "Corrective" }));
        var csv = await CsvAsync(await requester.GetAsync("/api/work-requests/export?format=csv&c=title&t=Title"));
        Assert.Contains("My broken light", csv);
        Assert.DoesNotContain("Admin's leak", csv);
    }

    [SkippableFact]
    public async Task Export_rejects_an_unknown_column()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await NewWorkOrderAsync(admin, site, "Anything", "Low");
        var r = await admin.GetAsync("/api/work-orders/export?c=nope");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("nope", await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Export_needs_the_list_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _) = await OrgAsync();
        var requester = await UserAsync(admin, "Requester");
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync("/api/work-orders/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync("/api/purchase-orders/export")).StatusCode);
    }

    [SkippableTheory]
    [InlineData("work-orders")]
    [InlineData("work-requests")]
    [InlineData("pm-schedules")]
    [InlineData("parts")]
    [InlineData("purchase-orders")]
    [InlineData("assets")]
    public async Task Every_list_exports(string list)
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await NewWorkOrderAsync(admin, site, "Row", "Low");
        var csv = await CsvAsync(await admin.GetAsync($"/api/{list}/export?format=csv"));
        Assert.False(string.IsNullOrWhiteSpace(csv.Split("\r\n")[0]));
        var sheet = await SheetAsync(await admin.GetAsync($"/api/{list}/export"));
        Assert.Contains("<sheetData>", sheet);
    }

    [SkippableFact]
    public async Task Asset_export_reads_custom_fields()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, site) = await OrgAsync();
        await Ok(await admin.PostAsJsonAsync("/api/custom-fields", new { entityType = "Asset", key = "colour", label = "Colour", dataType = "Text", isRequired = false, sortOrder = 1, showInList = true }));
        await Ok(await admin.PostAsJsonAsync("/api/assets", new { assetNumber = "P-77", name = "Pump", siteId = site, status = "Active", criticality = "High", warrantyType = "None",
            customFields = new Dictionary<string, string?> { ["colour"] = "Ocean blue" } }));
        var csv = await CsvAsync(await admin.GetAsync("/api/assets/export?format=csv&search=P-77&c=assetNumber&t=No&c=customFields.colour&t=Colour&c=status&t=Status"));
        Assert.Equal("No,Colour,Status\r\nP-77,Ocean blue,Active\r\n", csv);
    }

    [Fact]
    public void Export_caps_rows_and_says_so()
    {
        var items = Enumerable.Range(1, 3).Select(i => (object)new { Number = $"WO-{i}" }).ToList();
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["format"] = "csv", ["c"] = "number", ["t"] = "No" });
        var full = ListExport.Build(query, "work-orders", new PagedResult<object>(items, 3, 1, ListExport.MaxRows), TimeZoneInfo.Utc);
        Assert.False(full.Truncated);
        var cut = ListExport.Build(query, "work-orders", new PagedResult<object>(items, ListExport.MaxRows + 1, 1, ListExport.MaxRows), TimeZoneInfo.Utc);
        Assert.True(cut.Truncated);
        Assert.Equal(10_000, ListExport.MaxRows);
        Assert.Equal("text/csv", cut.ContentType);
        Assert.EndsWith(".csv", cut.FileName);
    }

    [Fact]
    public void Titles_fall_back_to_keys_when_they_do_not_match()
    {
        var items = new List<object> { new { Number = "WO-1", Title = "T" } };
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["format"] = "csv", ["c"] = new(["number", "title"]), ["t"] = "Only one" });
        var file = ListExport.Build(query, "work-orders", new PagedResult<object>(items, 1, 1, ListExport.MaxRows), TimeZoneInfo.Utc);
        Assert.StartsWith("number,title\r\n", Encoding.UTF8.GetString(file.Bytes).TrimStart('﻿'));
    }

    [SkippableFact]
    public async Task Contract_status_follows_ui_culture()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await ContractTests.WorldAsync(fx);
        await ContractTests.ContractAsync(w.O.Admin, ContractTests.Body(w.C, ContractTests.Today.AddDays(-10), ContractTests.Today.AddDays(355)));
        const string url = "/api/contracts/export?format=csv&c=status&t=Status";
        var arabic = Translator.T("Active", "ar");
        Assert.NotEqual("Active", arabic);
        // The link carries the app's language (a download link cannot send a header); that outranks the browser's own.
        Assert.Contains($"\r\n{arabic}\r\n", await CsvAsync(await w.O.Admin.GetAsync(url + "&ui-culture=ar")));
        var req = new HttpRequestMessage(HttpMethod.Get, url + "&ui-culture=en");
        req.Headers.AcceptLanguage.ParseAdd("ar");
        var english = await CsvAsync(await w.O.Admin.SendAsync(req));
        Assert.Contains("\r\nActive\r\n", english);
        Assert.DoesNotContain(arabic, english);
    }
}
