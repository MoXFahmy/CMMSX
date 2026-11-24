using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Background imports (0.26.0): every check and import is a job the worker runs, with progress, history and limits.</summary>
public class ImportJobTests(ManualImportFixture fx) : IClassFixture<ManualImportFixture>
{
    private static readonly string[] Lookup = ["Code", "Name"];
    private static readonly string[] LocationHeadings = ["Site code", "Code", "Name"];

    private ImportWorker Worker => fx.Factory!.Services.GetRequiredService<ImportWorker>();

    private async Task DrainAsync() { while (await Worker.RunNextAsync(default)) { } }

    private static async Task<Guid> QueueAsync(HttpClient c, byte[] file, bool save, string name = "data.xlsx")
    {
        var r = await PostAsync(c, save ? "/api/import/run" : "/api/import/check", file, name);
        Assert.True(r.StatusCode == HttpStatusCode.Accepted, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> JobAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/import/jobs/{id}"));

    private static byte[] Symptoms(int count, string prefix = "SY") =>
        Book(new Sheet("Symptoms", Lookup, Enumerable.Range(1, count).Select(i => new object?[] { $"{prefix}{i}", $"Symptom {i}" }).ToArray()));

    [SkippableFact]
    public async Task Check_and_import_run_as_jobs()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var file = Book(new Sheet("Symptoms", Lookup, ["LEAK", "Leaking"], ["NOISE", "Noisy"]));
        var check = await QueueAsync(org.Admin, file, save: false);
        var queued = await JobAsync(org.Admin, check);
        Assert.Equal(("Queued", 0, false), (queued.GetProperty("status").GetString(), queued.GetProperty("waitingBehind").GetInt32(), queued.GetProperty("save").GetBoolean()));
        Assert.True(await Worker.RunNextAsync(default));
        var done = await JobAsync(org.Admin, check);
        Assert.Equal("Done", done.GetProperty("status").GetString());
        Assert.Equal(2, Count(SheetOf(done.GetProperty("result"), "Symptoms"), "new"));
        Assert.DoesNotContain((await Ok(await org.Admin.GetAsync("/api/symptoms"))).EnumerateArray(), s => s.GetProperty("code").GetString() == "LEAK");

        var run = await QueueAsync(org.Admin, file, save: true);
        await DrainAsync();
        Assert.Equal("Done", (await JobAsync(org.Admin, run)).GetProperty("status").GetString());
        Assert.Contains((await Ok(await org.Admin.GetAsync("/api/symptoms"))).EnumerateArray(), s => s.GetProperty("code").GetString() == "LEAK");
        var history = (await Ok(await org.Admin.GetAsync("/api/import/jobs"))).EnumerateArray().ToList();
        Assert.Equal([run, check], history.Select(Id).Take(2));
        Assert.Equal(("data.xlsx", "Admin"), (history[0].GetProperty("fileName").GetString(), history[0].GetProperty("userName").GetString()));
        Assert.False(history[0].TryGetProperty("result", out var r) && r.ValueKind != JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task Progress_is_reported()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var seen = new List<(string Sheet, int Done, int Total)>();
        using var scope = fx.Factory!.Services.CreateScope();
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(org.Tenant, org.AdminId, "Admin", "Import"));
        var book = SpreadsheetReader.Read(new MemoryStream(Symptoms(250)), "data.xlsx");
        await ImportRun.RunAsync(db, db.Actor, book, save: false, TimeSpan.FromMinutes(5), null, default, (s, d, t) => { seen.Add((s, d, t)); return Task.CompletedTask; });
        Assert.Equal(("Symptoms", 250, 250), seen[^1]);
        Assert.True(seen.Count >= 3, $"{seen.Count} reports");
        Assert.Equal(seen.Select(x => x.Done).Order(), seen.Select(x => x.Done));
    }

    [SkippableFact]
    public async Task Two_claims_take_one_job()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var id = await QueueAsync(org.Admin, Symptoms(2), save: false);
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        await using var a = new CmmsDbContext(options, new FixedTenantContext(null));
        await using var b = new CmmsDbContext(options, new FixedTenantContext(null));
        var claims = await Task.WhenAll(ImportJobs.ClaimNextAsync(a, DateTimeOffset.UtcNow, default), ImportJobs.ClaimNextAsync(b, DateTimeOffset.UtcNow, default));
        Assert.Equal([id], claims.Where(c => c is not null).Select(c => c!.Value));
        await DbAsync(fx, db => db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportJobStatus.Failed)));
    }

    [SkippableFact]
    public async Task Oldest_first_and_one_at_a_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var first = await QueueAsync(org.Admin, Symptoms(2, "A"), save: true);
        var second = await QueueAsync(org.Admin, Symptoms(2, "B"), save: true);
        Assert.Equal(1, (await JobAsync(org.Admin, second)).GetProperty("waitingBehind").GetInt32());
        Assert.True(await Worker.RunNextAsync(default));
        Assert.Equal("Done", (await JobAsync(org.Admin, first)).GetProperty("status").GetString());
        var waiting = await JobAsync(org.Admin, second);
        Assert.Equal(("Queued", 0), (waiting.GetProperty("status").GetString(), waiting.GetProperty("waitingBehind").GetInt32()));
        await DrainAsync();
        Assert.Equal("Done", (await JobAsync(org.Admin, second)).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Stale_jobs_fail_and_save_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var id = await QueueAsync(org.Admin, Symptoms(2), save: true);
        await DbAsync(fx, db => db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, ImportJobStatus.Running).SetProperty(j => j.HeartbeatAt, DateTimeOffset.UtcNow.AddMinutes(-3))));
        var failed = await DbAsync(fx, db => ImportJobs.FailStaleAsync(db, DateTimeOffset.UtcNow, default));
        Assert.True(failed >= 1);
        var job = await JobAsync(org.Admin, id);
        Assert.Equal(("Failed", "The server restarted during this import. Nothing was saved. Try again."), (job.GetProperty("status").GetString(), job.GetProperty("error").GetString()));
    }

    [SkippableFact]
    public async Task Jobs_run_with_the_starters_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true }));
        var limited = await UserAsync(fx, org.Admin, "Administrator", [org.Site]);
        var id = await QueueAsync(limited.Client, Book(new Sheet("Locations", LocationHeadings, ["S2", "OUT", "Outside"], ["S1", "IN", "Inside"])), save: true);
        await DrainAsync();
        var job = await JobAsync(limited.Client, id);
        Assert.Equal("Done", job.GetProperty("status").GetString());
        var sheet = SheetOf(job.GetProperty("result"), "Locations");
        Assert.Equal((1, 1), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.DoesNotContain(await DbAsync(fx, db => db.Locations.IgnoreQueryFilters().Where(l => l.TenantId == org.Tenant).Select(l => l.Code).ToListAsync()), c => c == "OUT");
    }

    [SkippableFact]
    public async Task Limits_are_checked_at_upload()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var big = await PostAsync(org.Admin, "/api/import/check", new byte[25 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Contains("Choose a file of up to 25 MB.", await big.Content.ReadAsStringAsync());
        var csv = Encoding.UTF8.GetBytes("Code,Name\n" + string.Join("\n", Enumerable.Range(1, 20_001).Select(i => $"S{i},Symptom {i}")));
        var tooMany = await PostAsync(org.Admin, "/api/import/check", csv, "Symptoms.csv");
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("Symptoms has 20,001 rows; the limit is 20,000 per sheet. Split it into smaller files.", await tooMany.Content.ReadAsStringAsync());
        // A file far over the limit is refused while it's read, before it is held whole in memory.
        var huge = Encoding.UTF8.GetBytes("Code,Name\n" + string.Join("\n", Enumerable.Range(1, 25_000).Select(i => $"S{i},Symptom {i}")));
        var refused = await PostAsync(org.Admin, "/api/import/check", huge, "Symptoms.csv");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This file has more than 21,000 rows. Split it into smaller files.", await refused.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Problems_workbook_from_a_finished_check()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var id = await QueueAsync(org.Admin, Book(new Sheet("Symptoms", Lookup, ["LEAK", "Leaking"], ["BAD", null])), save: false);
        await DrainAsync();
        var r = await org.Admin.GetAsync($"/api/import/jobs/{id}/problems");
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}");
        var book = SpreadsheetReader.Read(new MemoryStream(await r.Content.ReadAsByteArrayAsync()), "p.xlsx");
        var sheet = book.Sheets.Single(s => s.Name == "Symptoms");
        Assert.Equal("BAD", sheet.Rows[1][0]);
    }

    [SkippableFact]
    public async Task Twenty_thousand_rows_import()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var id = await QueueAsync(org.Admin, Symptoms(20_000), save: true);
        await DrainAsync();
        var job = await JobAsync(org.Admin, id);
        Assert.Equal("Done", job.GetProperty("status").GetString());
        Assert.Equal(20_000, Count(SheetOf(job.GetProperty("result"), "Symptoms"), "new"));
    }
}
