using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>Builds import workbooks for tests and sends them to the import endpoints.</summary>
internal static class ImportFiles
{
    public sealed record Sheet(string Name, string[] Headings, params object?[][] Rows);

    public static byte[] Book(params Sheet[] sheets) => TableExport.Xlsx(sheets
        .Select(s => new ExportSheet(s.Name, s.Headings.Select((h, i) => new ExportColumn($"c{i}", h)).ToList(), s.Rows)).ToList());

    public static Task<HttpResponseMessage> PostAsync(HttpClient c, string path, byte[] file, string fileName = "data.xlsx")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", fileName);
        return c.PostAsync(path, form);
    }

    public static Task<JsonElement> CheckAsync(HttpClient c, byte[] file, string fileName = "data.xlsx") => JobResultAsync(c, "/api/import/check", file, fileName);

    public static Task<JsonElement> RunAsync(HttpClient c, byte[] file, string fileName = "data.xlsx") => JobResultAsync(c, "/api/import/run", file, fileName);

    /// <summary>Uploads to a background import (0.26.0) and follows the job until it finishes; its result.</summary>
    public static async Task<JsonElement> JobResultAsync(HttpClient c, string path, byte[] file, string fileName = "data.xlsx")
    {
        var id = (await OkAsync(await PostAsync(c, path, file, fileName))).GetProperty("id").GetGuid();
        return (await FinishedAsync(c, id)).GetProperty("result");
    }

    /// <summary>The job once it is done (fails the test if it failed or took over two minutes).</summary>
    public static async Task<JsonElement> FinishedAsync(HttpClient c, Guid id)
    {
        var until = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < until)
        {
            var job = await OkAsync(await c.GetAsync($"/api/import/jobs/{id}"));
            var status = job.GetProperty("status").GetString();
            if (status == "Done") return job;
            Assert.True(status != "Failed", $"The import failed: {job.GetProperty("error")}");
            await Task.Delay(100);
        }
        throw new TimeoutException("The import didn't finish in two minutes.");
    }

    public static async Task<JsonElement> OkAsync(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The result of one sheet, by its key ("Sites", "Locations"…).</summary>
    public static JsonElement SheetOf(JsonElement result, string key) =>
        result.GetProperty("sheets").EnumerateArray().First(s => s.GetProperty("sheet").GetString() == key);

    public static int Count(JsonElement sheet, string what) => sheet.GetProperty(what).GetInt32();

    public static IEnumerable<string> ProblemsOf(JsonElement sheet) =>
        sheet.GetProperty("rows").EnumerateArray().SelectMany(r => r.GetProperty("problems").EnumerateArray().Select(p => p.GetString()!));

    public static async Task<HttpClient> NewOrganizationAsync(ApiFixture fx, string name = "Import Co")
    {
        var c = fx.Client();
        var r = await c.PostAsJsonAsync("/api/auth/register", new { organizationName = name, fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" });
        r.EnsureSuccessStatusCode();
        return c;
    }

    public static readonly string[] SiteHeadings = ["Code", "Name", "City"];
    public static readonly string[] LocationHeadings = ["Site code", "Code", "Name", "Parent code"];
}
