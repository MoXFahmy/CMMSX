using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Sign-ups never confirmed are removed after 7 days (0.29.0), with every row their organization had; nothing else is touched.</summary>
public class PendingSignUpTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string BadLink = "This link has expired or was already used.";

    /// <summary>Makes an organization a sign-up pending since <paramref name="since"/>; with a token, its link still works.</summary>
    private Task MarkPendingAsync(Guid tenantId, DateTimeOffset since, string? token = null) => DbAsync(fx, db =>
        db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.SignUpPendingSince, (DateTimeOffset?)since)
            .SetProperty(t => t.SignUpTokenHash, token == null ? (string?)null : SignUpConfirmation.Hash(token))
            .SetProperty(t => t.SignUpTokenExpires, token == null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddHours(24))));

    private Task<int> PurgeAsync(DateTimeOffset now) => DbAsync(fx, db => PendingSignUps.PurgeAsync(db, now, CancellationToken.None));

    /// <summary>
    /// How many rows carry <paramref name="tenantId"/> in each table, read past every filter. The tables come from the model itself
    /// (every entity with a TenantId), so a table added later is checked without changing this test.
    /// </summary>
    private Task<Dictionary<string, int>> RowsOfAsync(Guid tenantId) => DbAsync(fx, async db =>
    {
        var counts = new Dictionary<string, int>();
        foreach (var type in db.Model.GetEntityTypes().Where(t => t.BaseType is null && t.GetTableName() is not null && t.FindProperty("TenantId") is not null))
            counts[type.Name] = await (Task<int>)CountOf.MakeGenericMethod(type.ClrType).Invoke(null, [db, tenantId])!;
        return counts;
    });

    private static readonly MethodInfo CountOf = typeof(PendingSignUpTests).GetMethod(nameof(CountRows), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static Task<int> CountRows<T>(CmmsDbContext db, Guid tenantId) where T : class =>
        db.Set<T>().IgnoreQueryFilters().CountAsync(e => EF.Property<Guid>(e, "TenantId") == tenantId);

    private Task<bool> TenantExistsAsync(Guid tenantId) => DbAsync(fx, db => db.Tenants.AnyAsync(t => t.Id == tenantId));

    private static async Task<string> ProblemTitle(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    [SkippableFact]
    public async Task Unconfirmed_sign_ups_older_than_7_days_are_removed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        // Trees of its own (a room in the hall, a pump in a line, a cost centre under another): the removal has to manage rows that point at rows of the same table.
        var room = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/locations",
            new { siteId = org.Site, parentId = org.Location, code = "L2", name = "Room", type = "Room", criticality = "Medium", isActive = true })));
        var line = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets",
            new { name = "Line", siteId = org.Site, locationId = room, status = "Active", criticality = "High", warrantyType = "None" })));
        await Ok(await org.Admin.PostAsJsonAsync("/api/assets",
            new { name = "Pump", siteId = org.Site, locationId = room, parentAssetId = line, status = "Active", criticality = "High", warrantyType = "None" }));
        var prod = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/cost-centres", new { code = "PROD", name = "Production", isActive = true })));
        await Ok(await org.Admin.PostAsJsonAsync("/api/cost-centres", new { code = "PACK", name = "Packing", parentId = prod, isActive = true }));
        var token = SignUpConfirmation.NewToken();
        var now = DateTimeOffset.UtcNow;
        await MarkPendingAsync(org.Tenant, now.AddDays(-8), token);
        var before = await RowsOfAsync(org.Tenant);
        Assert.True(before.Count > 50, $"only {before.Count} tenant tables found");
        Assert.True(before.Count(kv => kv.Value > 0) >= 8, string.Join(", ", before.Where(kv => kv.Value > 0).Select(kv => kv.Key)));

        Assert.Equal(1, await PurgeAsync(now));

        var left = (await RowsOfAsync(org.Tenant)).Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}: {kv.Value}").ToList();
        Assert.True(left.Count == 0, "rows left: " + string.Join(", ", left));
        Assert.False(await TenantExistsAsync(org.Tenant));
        // Its old link now fails like any used or expired one.
        var r = await fx.Client().PostAsJsonAsync("/api/auth/confirm-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(BadLink, await ProblemTitle(r));
        // And the address is free to sign up again.
        var again = fx.Client();
        await Ok(await again.PostAsJsonAsync("/api/auth/register", new { organizationName = "Loop Co", fullName = "Admin", email = org.AdminEmail, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/auth/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Recent_and_confirmed_organizations_stay()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var now = DateTimeOffset.UtcNow;
        var recent = await OrgAsync(fx);
        await MarkPendingAsync(recent.Tenant, now.AddDays(-6), SignUpConfirmation.NewToken());
        // Exactly 7 days is not yet "older than 7 days".
        var edge = await OrgAsync(fx);
        await MarkPendingAsync(edge.Tenant, now - PendingSignUps.Keep, SignUpConfirmation.NewToken());
        var confirmed = await OrgAsync(fx);
        // An organization made without signing up (CMX staff, an import): never pending.
        var direct = Guid.CreateVersion7();
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var opts = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            await using var db = new CmmsDbContext(opts, new FixedTenantContext(direct, source: "Web"));
            var (tenant, _) = await TenantProvisioning.CreateAsync(db, hasher, direct, "Direct Co", "Admin", Addr("direct"), "Passw0rd!x");
            Assert.Null(tenant.SignUpPendingSince);
        }
        Guid[] tenants = [recent.Tenant, edge.Tenant, confirmed.Tenant, direct];
        var before = new Dictionary<Guid, Dictionary<string, int>>();
        foreach (var t in tenants) before[t] = await RowsOfAsync(t);
        var states = await DbAsync(fx, db => db.Tenants.AsNoTracking().Where(t => tenants.Contains(t.Id))
            .Select(t => new { t.Id, t.SignUpPendingSince, t.SignUpTokenHash, t.SignUpTokenExpires }).ToListAsync());

        Assert.Equal(0, await PurgeAsync(now));

        foreach (var t in tenants)
        {
            Assert.True(await TenantExistsAsync(t));
            Assert.Equal(before[t], await RowsOfAsync(t));
        }
        var after = await DbAsync(fx, db => db.Tenants.AsNoTracking().Where(t => tenants.Contains(t.Id))
            .Select(t => new { t.Id, t.SignUpPendingSince, t.SignUpTokenHash, t.SignUpTokenExpires }).ToListAsync());
        Assert.Equal(states.OrderBy(s => s.Id), after.OrderBy(s => s.Id));
        // The edge one is due for any later purge (a later now): no longer pending, so the other tests' counts stay their own.
        await DbAsync(fx, db => db.Tenants.Where(t => t.Id == edge.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.SignUpPendingSince, (DateTimeOffset?)null)));
    }

    [SkippableFact]
    public async Task Signing_in_while_the_link_is_not_asked_for_releases_the_organization()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        // This server doesn't ask for the link (confirmation off): organizations left pending can be signed in to and used.
        var now = DateTimeOffset.UtcNow;
        var used = await OrgAsync(fx);
        await MarkPendingAsync(used.Tenant, now.AddDays(-8), SignUpConfirmation.NewToken());
        var renewed = await OrgAsync(fx);
        await MarkPendingAsync(renewed.Tenant, now.AddDays(-8), SignUpConfirmation.NewToken());
        var idle = await OrgAsync(fx);
        await MarkPendingAsync(idle.Tenant, now.AddDays(-8), SignUpConfirmation.NewToken());
        var logs = new CapturedLogs();
        fx.Factory!.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);

        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email = used.AdminEmail, password = "Passw0rd!x", rememberMe = false }));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        // Signing in with a new password releases it too.
        await Ok(await fx.Client().PostAsJsonAsync("/api/auth/renew-password",
            new { email = renewed.AdminEmail, password = "Passw0rd!x", newPassword = "Renewed0!pass", rememberMe = false }));

        var states = await DbAsync(fx, db => db.Tenants.AsNoTracking().Where(t => t.Id == used.Tenant || t.Id == renewed.Tenant || t.Id == idle.Tenant)
            .Select(t => new { t.Id, t.SignUpPendingSince, t.SignUpTokenHash, t.SignUpTokenExpires }).ToListAsync());
        foreach (var t in states.Where(s => s.Id != idle.Tenant))
        {
            Assert.Null(t.SignUpPendingSince);
            Assert.Null(t.SignUpTokenHash);
            Assert.Null(t.SignUpTokenExpires);
        }
        Assert.NotNull(states.Single(s => s.Id == idle.Tenant).SignUpPendingSince);
        var text = logs.Text;
        Assert.Contains($"PendingSignUpReleased {used.Tenant}", text);
        Assert.Contains($"PendingSignUpReleased {renewed.Tenant}", text);
        Assert.DoesNotContain(used.AdminEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(renewed.AdminEmail, text, StringComparison.OrdinalIgnoreCase);

        // The ones in use stay; the one nobody signed in to is still removed.
        var usedRows = await RowsOfAsync(used.Tenant);
        Assert.Equal(1, await PurgeAsync(now));
        Assert.True(await TenantExistsAsync(used.Tenant));
        Assert.True(await TenantExistsAsync(renewed.Tenant));
        Assert.Equal(usedRows, await RowsOfAsync(used.Tenant));
        Assert.False(await TenantExistsAsync(idle.Tenant));
    }

    [SkippableFact]
    public async Task Removal_logs_no_personal_data()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        var email = Addr("zubaida");
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Pendleton Widgets", fullName = "Zubaida Quarrington", email, password = "Passw0rd!x" }));
        var tenant = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        await MarkPendingAsync(tenant, DateTimeOffset.UtcNow.AddDays(-8), SignUpConfirmation.NewToken());
        var logs = new CapturedLogs();
        fx.Factory!.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);

        // Through the daily job, as the server runs it.
        await fx.Factory.Services.GetRequiredService<PlatformJobs>().RunDailyAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(await TenantExistsAsync(tenant));
        var text = logs.Text;
        Assert.Contains("PendingSignUpRemoved", text);
        Assert.Contains(tenant.ToString(), text);
        Assert.DoesNotContain("Zubaida", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quarrington", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pendleton", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email.Split('@')[0], text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Weekly_export_is_due_on_Sunday_from_two_UTC_once()
    {
        var sunday = new DateOnly(2026, 11, 29);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);
        DateTimeOffset At(DateOnly d, int h, int m = 0, int offsetHours = 0) => new(d.ToDateTime(new TimeOnly(h, m)), TimeSpan.FromHours(offsetHours));
        Assert.False(PlatformJobs.ExportDue(At(sunday, 1, 59), null));
        Assert.True(PlatformJobs.ExportDue(At(sunday, 2), null));
        Assert.True(PlatformJobs.ExportDue(At(sunday, 23, 59), sunday.AddDays(-7)));
        Assert.False(PlatformJobs.ExportDue(At(sunday, 3), sunday));
        Assert.False(PlatformJobs.ExportDue(At(sunday.AddDays(1), 3), null));
        Assert.False(PlatformJobs.ExportDue(At(sunday.AddDays(-1), 23), null));
        // 04:00 in Cairo (UTC+3 here) is 01:00 UTC: not yet; 06:00 there is 03:00 UTC.
        Assert.False(PlatformJobs.ExportDue(At(sunday, 4, 0, 3), null));
        Assert.True(PlatformJobs.ExportDue(At(sunday, 6, 0, 3), null));
        // 01:00 on Monday at UTC+3 is still Sunday 22:00 UTC.
        Assert.True(PlatformJobs.ExportDue(At(sunday.AddDays(1), 1, 0, 3), null));
    }

    /// <summary>Everything the app logs while attached: each line's text, its values and any exception.</summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public string Text => string.Join('\n', _lines);
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(CapturedLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}")) : "";
                owner._lines.Enqueue($"{category} {logLevel} {formatter(state, exception)} {values} {exception}");
            }
        }
    }
}
