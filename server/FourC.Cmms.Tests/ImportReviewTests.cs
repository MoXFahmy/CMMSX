using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Core.Common;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.26.0 final review: progress per sheet, who sees which imports and keys, and the memory guards.</summary>
public class ImportReviewTests(ManualImportFixture fx) : IClassFixture<ManualImportFixture>
{
    private static readonly string[] Lookup = ["Code", "Name"];
    private ImportWorker Worker => fx.Factory!.Services.GetRequiredService<ImportWorker>();
    private async Task DrainAsync() { while (await Worker.RunNextAsync(default)) { } }

    private static async Task<Guid> QueueAsync(HttpClient c, byte[] file, bool save)
    {
        var r = await PostAsync(c, save ? "/api/import/run" : "/api/import/check", file);
        Assert.True(r.StatusCode == HttpStatusCode.Accepted, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [SkippableFact]
    public async Task Progress_counts_each_sheet()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var id = await QueueAsync(org.Admin, Book(new Sheet("Symptoms", Lookup, ["A", "A"], ["B", "B"], ["C", "C"]), new Sheet("Causes", Lookup, ["X", "X"])), save: false);
        await DrainAsync();
        var job = await Ok(await org.Admin.GetAsync($"/api/import/jobs/{id}"));
        Assert.Equal(("Causes", 1, 1), (job.GetProperty("sheet").GetString(), job.GetProperty("rowsDone").GetInt32(), job.GetProperty("rowsTotal").GetInt32()));
    }

    [SkippableFact]
    public async Task Site_limited_admins_see_only_their_own_imports()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var theirs = await QueueAsync(org.Admin, Book(new Sheet("Symptoms", Lookup, ["A", "A"])), save: false);
        var limited = await UserAsync(fx, org.Admin, "Administrator", [org.Site]);
        var mine = await QueueAsync(limited.Client, Book(new Sheet("Symptoms", Lookup, ["B", "B"])), save: false);
        await DrainAsync();
        var listed = (await Ok(await limited.Client.GetAsync("/api/import/jobs"))).EnumerateArray().Select(Id).ToList();
        Assert.Contains(mine, listed);
        Assert.DoesNotContain(theirs, listed);
        Assert.Equal(HttpStatusCode.NotFound, (await limited.Client.GetAsync($"/api/import/jobs/{theirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await limited.Client.GetAsync($"/api/import/jobs/{theirs}/problems")).StatusCode);
        // Someone who sees every site sees every import.
        Assert.Contains(mine, (await Ok(await org.Admin.GetAsync("/api/import/jobs"))).EnumerateArray().Select(Id));
    }

    [SkippableFact]
    public async Task Site_limited_managers_see_and_revoke_only_keys_inside_their_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var everywhere = await ServiceAccountTests.ServiceAccountAsync(org.Admin, "Everywhere", "Viewer");
        var (key, _) = await ApiKeyTests.KeyAsync(org.Admin, everywhere);
        var limited = await UserAsync(fx, org.Admin, "Administrator", [org.Site]);
        Assert.DoesNotContain((await Ok(await limited.Client.GetAsync("/api/api-keys"))).EnumerateArray(), k => Id(k) == key);
        Assert.False((await limited.Client.PostAsync($"/api/api-keys/{key}/revoke", null)).IsSuccessStatusCode);
        Assert.Equal("Active", (await Ok(await org.Admin.GetAsync("/api/api-keys"))).EnumerateArray().Single(k => Id(k) == key).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task A_file_holds_20000_rows_in_all()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var rows = (int n, string p) => Enumerable.Range(1, n).Select(i => new object?[] { $"{p}{i}", $"{p} {i}" }).ToArray();
        var r = await PostAsync(org.Admin, "/api/import/check", Book(new Sheet("Symptoms", Lookup, rows(20_000, "S")), new Sheet("Causes", Lookup, rows(1, "C"))));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("This file has 20,001 rows; the limit is 20,000 in one file. Split it into smaller files.", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public void The_reader_stops_past_its_row_cap()
    {
        var csv = Encoding.UTF8.GetBytes("Code,Name\n" + string.Join("\n", Enumerable.Range(1, 200).Select(i => $"S{i},Symptom {i}")));
        var e = Assert.Throws<DomainException>(() => SpreadsheetReader.Read(new MemoryStream(csv), "Symptoms.csv", maxRows: 50));
        Assert.Contains("more than 50 rows", e.Message);
        Assert.Equal(201, SpreadsheetReader.Read(new MemoryStream(csv), "Symptoms.csv").Sheets[0].Rows.Count);
    }
}
