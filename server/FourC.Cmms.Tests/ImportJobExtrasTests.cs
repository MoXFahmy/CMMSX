using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Imports;
using FourC.Cmms.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Background imports (0.26.0): numbers kept outside the run, the e-mail after a long run, and old files removed.</summary>
public class ImportJobExtrasTests(ManualImportFixture fx) : IClassFixture<ManualImportFixture>
{
    private static readonly string[] AssetHeadings = ["Asset number", "Name", "Site code"];
    private static readonly string[] Lookup = ["Code", "Name"];

    private ImportWorker Worker => fx.Factory!.Services.GetRequiredService<ImportWorker>();
    private async Task DrainAsync() { while (await Worker.RunNextAsync(default)) { } }

    private static async Task<Guid> QueueAsync(HttpClient c, byte[] file, bool save)
    {
        var r = await PostAsync(c, save ? "/api/import/run" : "/api/import/check", file);
        Assert.True(r.StatusCode == HttpStatusCode.Accepted, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<long?> NextAssetNumberAsync(Guid tenant) => DbAsync(fx, db =>
        db.Database.SqlQuery<long?>($"SELECT NextValue AS Value FROM NumberSequences WHERE TenantId = {tenant} AND Name = 'Asset'").FirstOrDefaultAsync());

    private Task BackdateAsync(Guid id, TimeSpan by) => DbAsync(fx, db =>
        db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.CreatedAt, DateTimeOffset.UtcNow - by)));

    [SkippableFact]
    public async Task A_check_takes_no_numbers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "First", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" }));
        var before = await NextAssetNumberAsync(org.Tenant);
        var file = Book(new Sheet("Assets", AssetHeadings, [null, "Pump 1", "S1"], [null, "Pump 2", "S1"], [null, "Pump 3", "S1"]));
        await QueueAsync(org.Admin, file, save: false);
        await DrainAsync();
        Assert.Equal(before, await NextAssetNumberAsync(org.Tenant));
        await QueueAsync(org.Admin, file, save: true);
        await DrainAsync();
        Assert.Equal(before + 3, await NextAssetNumberAsync(org.Tenant));
    }

    [SkippableFact]
    public async Task Numbers_are_kept_even_if_the_import_rolls_back()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        using var scope = fx.Factory!.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        await using var db = new CmmsDbContext(options, new FixedTenantContext(org.Tenant, org.AdminId, "Admin", "Import"));
        long first;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var c = new ImportContext(db, db.Actor, new TenantSettings(), DateOnly.FromDateTime(DateTime.UtcNow)) { Saving = true };
            first = await c.TakeNumbersAsync("Asset", 3, default);
            await tx.RollbackAsync();
        }
        Assert.Equal(first + 3, await NextAssetNumberAsync(org.Tenant));
    }

    [SkippableFact]
    public async Task Long_runs_email_the_starter()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx);
        var file = Book(new Sheet("Symptoms", Lookup, ["LEAK", "Leaking"]));
        var quick = await QueueAsync(org.Admin, file, save: false);
        await DrainAsync();
        Assert.Empty(await RowsAsync(fx, org.AdminEmail));
        var slow = await QueueAsync(org.Admin, file, save: true);
        await BackdateAsync(slow, TimeSpan.FromMinutes(2));
        await DrainAsync();
        var mail = Assert.Single(await RowsAsync(fx, org.AdminEmail));
        Assert.Contains("data.xlsx", mail.Subject + mail.TextBody);
        Assert.Contains("Symptoms", mail.TextBody);
        Assert.NotEqual(quick, slow);
    }

    [SkippableFact]
    public async Task Key_imports_send_no_email()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx);
        var account = await ServiceAccountTests.ServiceAccountAsync(org.Admin, "ERP", "Administrator");
        var key = ApiKeyTests.KeyClient(fx, (await ApiKeyTests.KeyAsync(org.Admin, account)).Key);
        var id = await QueueAsync(key, Book(new Sheet("Assets", AssetHeadings, ["K-1", "Keyed pump", "S1"])), save: true);
        await BackdateAsync(id, TimeSpan.FromMinutes(2));
        await DrainAsync();
        Assert.Empty(await RowsAsync(fx, ServiceAccountTests.EmailOf(account)));
        Assert.DoesNotContain(await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().Where(e => e.TenantId == org.Tenant).Select(e => e.ToAddress).ToListAsync()), a => a.EndsWith(".invalid"));
        var asset = await DbAsync(fx, db => db.Assets.IgnoreQueryFilters().SingleAsync(a => a.TenantId == org.Tenant && a.AssetNumber == "K-1"));
        var created = await DbAsync(fx, db => db.AuditEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.EntityId == asset.Id && a.Action == "Created"));
        Assert.Equal(("Import", "ERP"), (created.Source, created.UserName));
    }

    [SkippableFact]
    public async Task Old_files_are_removed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await DrainAsync();
        var org = await OrgAsync(fx, emailOn: false);
        var old = await QueueAsync(org.Admin, Book(new Sheet("Symptoms", Lookup, ["A", "A"], ["B", null])), save: false);
        var recent = await QueueAsync(org.Admin, Book(new Sheet("Symptoms", Lookup, ["C", "C"], ["D", null])), save: false);
        await DrainAsync();
        await DbAsync(fx, async db =>
        {
            await db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == old).ExecuteUpdateAsync(s => s.SetProperty(j => j.FinishedAt, DateTimeOffset.UtcNow.AddDays(-31)));
            return await db.ImportJobs.IgnoreQueryFilters().Where(j => j.Id == recent).ExecuteUpdateAsync(s => s.SetProperty(j => j.FinishedAt, DateTimeOffset.UtcNow.AddDays(-29)));
        });
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            Assert.True(await Housekeeping.TrimImportFilesAsync(db, scope.ServiceProvider.GetRequiredService<IFileStorage>(), DateTimeOffset.UtcNow, default) >= 1);
        }
        var gone = await org.Admin.GetAsync($"/api/import/jobs/{old}/problems");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Contains("This file is no longer kept.", await gone.Content.ReadAsStringAsync());
        Assert.True((await org.Admin.GetAsync($"/api/import/jobs/{recent}/problems")).IsSuccessStatusCode);
    }
}
