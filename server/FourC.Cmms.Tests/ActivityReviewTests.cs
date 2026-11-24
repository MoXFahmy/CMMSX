using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.25.0 final review: who may read whose history, site limits, retention in batches, lockout noise and one change per tick.</summary>
public class ActivityReviewTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private Task<List<AuditEntry>> EntriesAsync(Func<IQueryable<AuditEntry>, IQueryable<AuditEntry>> which) =>
        DbAsync(fx, db => which(db.AuditEntries.IgnoreQueryFilters().AsNoTracking()).ToListAsync());

    [SkippableFact]
    public async Task History_of_users_roles_and_settings_needs_their_permission()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, min: 10));
        var tech = await UserAsync(fx, org.Admin, "Technician");
        var role = Id((await Ok(await org.Admin.GetAsync("/api/roles"))).EnumerateArray().First());
        foreach (var (type, id) in new[] { ("AppUser", org.AdminId), ("Tenant", org.Tenant), ("Role", role) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.GetAsync($"/api/audit?entityType={type}&entityId={id}")).StatusCode);
            await Ok(await org.Admin.GetAsync($"/api/audit?entityType={type}&entityId={id}"));
        }
    }

    [SkippableFact]
    public async Task Site_limited_manager_cannot_anonymise_outside_their_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var away = await UserAsync(fx, org.Admin, "Administrator", name: "On Leave");
        await DbAsync(fx, db => db.Users.IgnoreQueryFilters().Where(u => u.Id == away.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        var limited = await UserAsync(fx, org.Admin, "Administrator", [org.Site], name: "Site Manager");
        var code = away.Id.ToString("N")[..6];
        Assert.False((await limited.Client.GetAsync($"/api/anonymise/preview?userId={away.Id}")).IsSuccessStatusCode);
        Assert.False((await limited.Client.PostAsJsonAsync("/api/anonymise", new { userId = away.Id, confirm = code })).IsSuccessStatusCode);
        Assert.Equal("On Leave", await DbAsync(fx, db => db.Users.IgnoreQueryFilters().Where(u => u.Id == away.Id).Select(u => u.FullName).SingleAsync()));
    }

    [SkippableFact]
    public async Task Site_limited_admins_miss_other_sites_stock_and_teams()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var siteB = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var storeA = await Phone.StoreAsync(org.Admin, org.Site, "SA");
        var storeB = await Phone.StoreAsync(org.Admin, siteB, "SB");
        var partA = await Phone.PartAsync(org.Admin, storeA, "PA");
        var partB = await Phone.PartAsync(org.Admin, storeB, "PB");
        var teamA = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/teams", new { code = "TA", name = "Team A", siteId = org.Site, memberIds = Array.Empty<Guid>() })));
        var teamB = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/teams", new { code = "TB", name = "Team B", siteId = siteB, memberIds = Array.Empty<Guid>() })));
        var admin = await UserAsync(fx, org.Admin, "Administrator", [org.Site]);
        var rows = await ActivityLogTests.RowsAsync(admin.Client);
        var levelB = await DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().Where(l => l.StoreId == storeB).Select(l => l.Id).SingleAsync());
        var levelA = await DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().Where(l => l.StoreId == storeA).Select(l => l.Id).SingleAsync());
        Assert.Contains(rows, r => r.GetProperty("entityId").GetGuid() == teamA);
        Assert.DoesNotContain(rows, r => r.GetProperty("entityId").GetGuid() == teamB);
        Assert.Contains(rows, r => r.GetProperty("entityId").GetGuid() == levelA);
        Assert.DoesNotContain(rows, r => r.GetProperty("entityId").GetGuid() == levelB);
        Assert.DoesNotContain(rows, r => r.GetProperty("entityId").GetGuid() == storeB);
        // Parts are organization-wide: both show.
        Assert.Contains(rows, r => r.GetProperty("entityId").GetGuid() == partA);
        Assert.Contains(rows, r => r.GetProperty("entityId").GetGuid() == partB);
    }

    [SkippableFact]
    public async Task Retention_deletes_in_batches()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, retention: 12));
        var now = DateTimeOffset.UtcNow;
        var ids = await DbAsync(fx, async db =>
        {
            var rows = Enumerable.Range(0, 5).Select(i => new AuditEntry
            {
                TenantId = org.Tenant, EntityType = "Asset", EntityId = Guid.NewGuid(), Action = "Updated", Timestamp = now.AddMonths(-13).AddMinutes(i), ChangeId = Guid.NewGuid(),
            }).ToList();
            db.AuditEntries.AddRange(rows);
            await db.SaveChangesAsync();
            return rows.Select(r => r.Id).ToList();
        });
        var deleted = await DbAsync(fx, db => Housekeeping.TrimActivityAsync(db, now, default, batchSize: 2));
        Assert.True(deleted >= 5, $"{deleted} deleted");
        Assert.Empty(await EntriesAsync(q => q.Where(a => ids.Contains(a.Id))));
    }

    [SkippableFact]
    public async Task Locked_out_is_recorded_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var c = fx.Client();
        for (var i = 0; i < 12; i++) await c.PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Wrong-one-9", rememberMe = false });
        var events = await EntriesAsync(q => q.Where(a => a.EntityType == "AppUser" && a.EntityId == org.AdminId));
        Assert.Single(events, e => e.Action == "LockedOut");
        Assert.Equal(7, events.Count(e => e.Action == "SignInFailed"));
    }

    [SkippableFact]
    public async Task History_keeps_the_records_own_entries_beyond_its_lines()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Round", siteId = org.Site, type = "Preventive", priority = "Low" })));
        // A big round: 250 later changes to its checklist steps.
        await DbAsync(fx, async db =>
        {
            var later = DateTimeOffset.UtcNow.AddMinutes(1);
            db.AuditEntries.AddRange(Enumerable.Range(0, 250).Select(i => new AuditEntry
            {
                TenantId = org.Tenant, EntityType = "WorkOrderTask", EntityId = Guid.NewGuid(), ParentType = "WorkOrder", ParentId = wo, Action = "Updated",
                Timestamp = later.AddSeconds(i), ChangeId = Guid.NewGuid(), Changes = "{\"IsDone\":{\"from\":false,\"to\":true}}",
            }));
            return await db.SaveChangesAsync();
        });
        var history = (await Ok(await org.Admin.GetAsync($"/api/audit?entityType=WorkOrder&entityId={wo}"))).EnumerateArray().ToList();
        Assert.Contains(history, h => h.GetProperty("entityType").GetString() == "WorkOrder" && h.GetProperty("action").GetString() == "Created");
    }

    [SkippableFact]
    public async Task Ticking_a_step_that_starts_the_work_writes_one_entry_for_the_step()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await TestSettings.SetSettingsAsync(org.Admin, s => s["autoStartWork"] = true);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low", tasks = new[] { "Isolate" } })));
        var step = Id((await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("tasks")[0]);
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}/tasks/{step}", new { isDone = true }));
        Assert.Equal("InProgress", (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("status").GetString());
        var updates = await EntriesAsync(q => q.Where(a => a.EntityId == step && a.Action == "Updated"));
        Assert.Single(updates);
        Assert.Contains("UpdatedByName", updates[0].Changes);
    }

    [SkippableFact]
    public async Task Renewing_with_one_session_on_does_not_ask_about_the_same_browser()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, expiry: 30, oneSession: true));
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == org.AdminId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordChangedAt, DateTimeOffset.UtcNow.AddDays(-31)));
            ((MemoryCache)scope.ServiceProvider.GetRequiredService<IMemoryCache>()).Compact(1.0);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await org.Admin.GetAsync("/api/auth/me")).StatusCode);
        var login = await org.Admin.PostAsJsonAsync("/api/auth/login", new { email = org.AdminEmail, password = "Passw0rd!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        var renew = await org.Admin.PostAsJsonAsync("/api/auth/renew-password", new { email = org.AdminEmail, password = "Passw0rd!x", newPassword = "Renewed0ne!x", rememberMe = false });
        Assert.True(renew.IsSuccessStatusCode, $"{(int)renew.StatusCode}: {await renew.Content.ReadAsStringAsync()}");
        await Ok(await org.Admin.GetAsync("/api/auth/me"));
    }

    [SkippableFact]
    public async Task Organization_name_list_wording_and_logo_are_logged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var current = await Ok(await org.Admin.GetAsync("/api/settings"));
        await Ok(await org.Admin.PutAsJsonAsync("/api/settings", new { name = "Renamed Co", settings = current.GetProperty("settings") }));
        await Ok(await ListLabelTests.PutListsAsync(org.Admin, new { priority = new { Urgent = new { name = "Emergency", color = "red", order = 0 } } }));
        using (var form = new MultipartFormDataContent())
        {
            var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(png, "file", "logo.png");
            Assert.True((await org.Admin.PostAsync("/api/settings/logo", form)).IsSuccessStatusCode);
        }
        Assert.True((await org.Admin.DeleteAsync("/api/settings/logo")).IsSuccessStatusCode);
        var entries = await EntriesAsync(q => q.Where(a => a.TenantId == org.Tenant && a.EntityType == "Tenant" && a.Action == "SettingsChanged"));
        var name = Assert.Single(entries, e => e.Changes!.StartsWith("{\"name\":"));
        Assert.Contains("Renamed Co", name.Changes);
        Assert.Contains(entries, e => e.Changes!.Contains("settings.listLabels"));
        Assert.Equal(2, entries.Count(e => e.Changes!.Contains("settings.logo")));
        // The general page's save doesn't repeat the lists, the logo or the security rules.
        foreach (var key in new[] { "settings.listLabels", "settings.logo", "settings.security" }) Assert.DoesNotContain(key, name.Changes!);
    }
}

/// <summary>Every site-limited record type the activity log records is checked against the person's sites (0.25.0); no database needed.</summary>
public class ActivitySiteMapTests
{
    /// <summary>A person limited to one site (and, for the branch model, to a branch of the location tree).</summary>
    private sealed class Limited(bool branches) : ITenantContext
    {
        public Guid? TenantId { get; } = Guid.NewGuid();
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Limited";
        public IReadOnlyList<Guid>? SiteIds { get; } = [Guid.NewGuid()];
        public IReadOnlyList<string>? BranchPaths => branches ? ["/x/"] : null;
    }

    private static List<Type> SiteLimitedTypes(bool branches)
    {
        using var db = new CmmsDbContext(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none").Options, new Limited(branches));
        return db.Model.GetEntityTypes().Where(t => t.GetDeclaredQueryFilters().Any(f => f.Key != null && f.Key != CmmsDbContext.TenantFilter)).Select(t => t.ClrType).ToList();
    }

    [Fact]
    public void Every_site_limited_record_type_is_checked()
    {
        var limited = SiteLimitedTypes(false).Concat(SiteLimitedTypes(true)).Distinct().ToList();
        Assert.Contains(typeof(FourC.Cmms.Core.Work.WorkOrder), limited);
        var missing = limited
            .Where(t => typeof(TenantEntity).IsAssignableFrom(t) || AuditScope.IsRecorded(t))
            .Where(t => !ActivitySites.Checks(t.Name, AuditScope.ParentTypeOf(t)))
            .Select(t => $"{t.Name} is limited to sites but the activity log doesn't check it").ToList();
        Assert.Empty(missing);
    }
}
