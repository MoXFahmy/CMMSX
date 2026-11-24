using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Imports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.26.1: API key and import fixes left by 0.26.0's review.</summary>
public class ImportFixTests(ManualImportFixture fx) : IClassFixture<ManualImportFixture>
{
    private static readonly string[] Lookup = ["Code", "Name"];

    /// <summary>A storage whose deletes fail (a network error, say).</summary>
    private sealed class FailingDeletes : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new IOException("The storage can't be reached.");
    }

    private Task<Guid> OldJobAsync(Guid tenant, string key) => DbAsync(fx, async db =>
    {
        var j = new ImportJob { TenantId = tenant, FileName = "old.xlsx", StorageKey = key, Status = ImportJobStatus.Done, CreatedAt = DateTimeOffset.UtcNow.AddDays(-40), FinishedAt = DateTimeOffset.UtcNow.AddDays(-40) };
        db.ImportJobs.Add(j);
        await db.SaveChangesAsync();
        return j.Id;
    });

    [SkippableFact]
    public async Task Done_is_saved_with_the_import()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var job = await OldJobAsync(org.Tenant, "unused");
        await DbAsync(fx, db => db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == job)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportJobStatus.Running).SetProperty(j => j.FinishedAt, (DateTimeOffset?)null).SetProperty(j => j.Save, true)));
        using var scope = fx.Factory!.Services.CreateScope();
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(org.Tenant, org.AdminId, "Admin", "Import"));
        var book = SpreadsheetReader.Read(new MemoryStream(Book(new Sheet("Symptoms", Lookup, ["LEAK", "Leaking"]))), "data.xlsx");
        ImportJobStatus? inside = null;
        var result = await ImportRun.RunAsync(db, db.Actor, book, save: true, TimeSpan.FromMinutes(5), null, default, beforeCommit: async (r, tk) =>
        {
            await ImportWorker.MarkDoneAsync(db, job, r, new JsonSerializerOptions(JsonSerializerDefaults.Web), tk);
            inside = await db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == job).Select(j => (ImportJobStatus?)j.Status).FirstAsync(tk);
        });
        Assert.True(result.Saved);
        Assert.Equal(ImportJobStatus.Done, inside);
        var after = await DbAsync(fx, d => d.ImportJobs.IgnoreQueryFilters().AsNoTracking().FirstAsync(j => j.Id == job));
        Assert.Equal(ImportJobStatus.Done, after.Status);
        Assert.NotNull(after.FinishedAt);
        Assert.Contains("\"saved\":true", after.ResultJson);
    }

    [SkippableFact]
    public async Task A_check_marks_nothing_inside()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        using var scope = fx.Factory!.Services.CreateScope();
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(org.Tenant, org.AdminId, "Admin", "Import"));
        var book = SpreadsheetReader.Read(new MemoryStream(Book(new Sheet("Symptoms", Lookup, ["LEAK", "Leaking"]))), "data.xlsx");
        var called = false;
        await ImportRun.RunAsync(db, db.Actor, book, save: false, TimeSpan.FromMinutes(5), null, default, beforeCommit: (_, _) => { called = true; return Task.CompletedTask; });
        Assert.False(called);
    }

    [SkippableFact]
    public async Task Files_stay_listed_when_the_delete_fails()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var job = await OldJobAsync(org.Tenant, $"{org.Tenant}/imports/old.xlsx");
        await DbAsync(fx, db => Housekeeping.TrimImportFilesAsync(db, new FailingDeletes(), DateTimeOffset.UtcNow));
        Assert.NotNull(await DbAsync(fx, db => db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == job).Select(j => j.StorageKey).FirstAsync()));
    }

    [SkippableFact]
    public async Task Service_accounts_arent_recipients_or_managers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await ContractTests.WorldAsync(fx);
        var sa = await ServiceAccountTests.ServiceAccountAsync(w.O.Admin, "SAP", "Viewer");
        Assert.Equal(HttpStatusCode.BadRequest, (await w.O.Admin.PostAsJsonAsync("/api/contracts",
            ContractTests.Body(w.C, ContractTests.Today, ContractTests.Today.AddDays(30), manager: sa))).StatusCode);
        var template = await Pm12.TemplateAsync(w.O.Admin, "SA");
        var asset = await Pm12.AssetAsync(w.O.Admin, w.O.Site, "Lift");
        Assert.Equal(HttpStatusCode.BadRequest, (await w.O.Admin.PostAsJsonAsync("/api/pm-schedules",
            Pm12.Schedule(w.O.Site, template, ContractTests.Today, asset, extra: new() { ["recipientIds"] = new[] { sa } }))).StatusCode);
    }

    [SkippableFact]
    public async Task Keys_cannot_read_devices()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Assert.True(ApiKeyRules.Blocked("GET", "/api/users/x/devices"));
        Assert.False(ApiKeyRules.Blocked("GET", "/api/users"));
        var org = await OrgAsync(fx, emailOn: false);
        var sa = await ServiceAccountTests.ServiceAccountAsync(org.Admin, "Gateway", "Administrator");
        var (_, key) = await ApiKeyTests.KeyAsync(org.Admin, sa);
        var c = fx.Client();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        var r = await c.GetAsync($"/api/users/{org.AdminId}/devices");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("API keys can't do this.", await r.Content.ReadAsStringAsync());
    }
}

/// <summary>Background imports whose file reads fail with a very long message.</summary>
public sealed class LongErrorImportFixture : ApiFixture
{
    public static readonly string Message = new('x', 1500);

    protected override void Configure(IWebHostBuilder b)
    {
        b.UseSetting("Import:RunInBackground", "false");
        b.ConfigureTestServices(s => s.AddSingleton<IFileStorage>(sp => new FailingReads(ActivatorUtilities.CreateInstance<LocalFileStorage>(sp))));
    }

    private sealed class FailingReads(IFileStorage inner) : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) => inner.SaveAsync(key, content, ct);
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => throw new DomainException(Message);
        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
    }
}

public class ImportErrorTests(LongErrorImportFixture fx) : IClassFixture<LongErrorImportFixture>
{
    [SkippableFact]
    public async Task Long_errors_are_cut()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var r = await PostAsync(org.Admin, "/api/import/check", Book(new Sheet("Symptoms", ["Code", "Name"], ["LEAK", "Leaking"])));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var worker = fx.Factory!.Services.GetRequiredService<ImportWorker>();
        while (await worker.RunNextAsync(default)) { }
        var job = await Ok(await org.Admin.GetAsync($"/api/import/jobs/{id}"));
        Assert.Equal("Failed", job.GetProperty("status").GetString());
        Assert.Equal(1000, job.GetProperty("error").GetString()!.Length);
    }
}
