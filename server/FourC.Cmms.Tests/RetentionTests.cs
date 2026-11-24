using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>How long the activity log is kept, and organization settings changes listed by key (0.25.0).</summary>
public class RetentionTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Retention_deletes_only_older_entries()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = await OrgAsync(fx, emailOn: false);
        await Ok(await PasswordRuleTests.SetSecurity(org.Admin, retention: 12));
        var now = DateTimeOffset.UtcNow;
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        AuditEntry Row(Guid tenant, int monthsAgo) => new()
        {
            TenantId = tenant, EntityType = "Asset", EntityId = Guid.NewGuid(), Action = "Updated", Timestamp = now.AddMonths(-monthsAgo), ChangeId = Guid.NewGuid(),
        };
        var old = Row(org.Tenant, 13);
        var recent = Row(org.Tenant, 11);
        var othersOld = Row(other.Tenant, 13);
        db.AuditEntries.AddRange(old, recent, othersOld);
        await db.SaveChangesAsync();

        await Housekeeping.TrimActivityAsync(db, now);

        var left = await db.AuditEntries.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == old.Id || a.Id == recent.Id || a.Id == othersOld.Id).Select(a => a.Id).ToListAsync();
        Assert.DoesNotContain(old.Id, left);
        Assert.Contains(recent.Id, left);
        Assert.Contains(othersOld.Id, left);
        // The organization's own recent changes (its sign-up, the retention setting) stay.
        Assert.True(await db.AuditEntries.IgnoreQueryFilters().AnyAsync(a => a.TenantId == org.Tenant && a.Action == "SettingsChanged"));
    }

    [SkippableFact]
    public async Task Settings_changes_list_each_key()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        string? zoneBefore = null, languageBefore = null;
        await TestSettings.SetSettingsAsync(org.Admin, s =>
        {
            zoneBefore = s["timeZone"]?.GetValue<string>();
            languageBefore = s["defaultLanguage"]?.GetValue<string>();
            s["timeZone"] = zoneBefore == "Africa/Cairo" ? "Europe/London" : "Africa/Cairo";
            s["defaultLanguage"] = languageBefore == "ar" ? "en" : "ar";
        });
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var entry = Assert.Single(await db.AuditEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == org.Tenant && a.EntityType == "Tenant" && a.Action == "SettingsChanged").ToListAsync());
        using var changes = JsonDocument.Parse(entry.Changes!);
        var keys = changes.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["settings.defaultLanguage", "settings.timeZone"], keys.Order().ToList());
        var zone = changes.RootElement.GetProperty("settings.timeZone");
        Assert.Equal(zoneBefore, zone.GetProperty("from").GetString());
        Assert.NotEqual(zoneBefore, zone.GetProperty("to").GetString());
        Assert.Equal("Admin", entry.UserName);
    }
}
