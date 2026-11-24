using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Settings › Security (0.25.0): password rules, one session per person and activity retention.</summary>
public static class SecurityEndpoints
{
    public sealed record SecurityInput(int PasswordMinLength, int PasswordExpiryDays, int PasswordHistory, bool OneSessionPerUser, int? ActivityRetentionMonths);

    public static void MapSecurityEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/settings/security").WithTags("Settings").RequirePermission(Permissions.SettingsManage);
        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            (await db.Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => t.Settings).FirstAsync(ct)).Security);

        g.MapPut("/", async (SecurityInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = new Validator()
                .When(i.PasswordMinLength is < 8 or > 64, "passwordMinLength", "Enter a minimum length from 8 to 64.")
                .When(i.PasswordExpiryDays != 0 && i.PasswordExpiryDays is < 30 or > 730, "passwordExpiryDays", "Enter 0 (never) or 30 to 730 days.")
                .When(i.PasswordHistory is < 0 or > 24, "passwordHistory", "Enter 0 to 24.")
                .When(i.ActivityRetentionMonths is < 12 or > 120, "activityRetentionMonths", "Keep at least 12 months, at most 120, or leave it empty.");
            if (!v.IsValid) return v.Problem();
            var tenant = await db.Tenants.FirstAsync(t => t.Id == db.CurrentTenantId, ct);
            var before = tenant.Settings.Security;
            var after = new SecuritySettings
            {
                PasswordMinLength = i.PasswordMinLength, PasswordExpiryDays = i.PasswordExpiryDays, PasswordHistory = i.PasswordHistory,
                OneSessionPerUser = i.OneSessionPerUser, ActivityRetentionMonths = i.ActivityRetentionMonths,
            };
            // Each changed rule, old and new, in the activity log.
            var changes = new Dictionary<string, object?>();
            void Diff(string key, object? from, object? to) { if (!Equals(from, to)) changes[$"security.{key}"] = new { from, to }; }
            Diff("passwordMinLength", before.PasswordMinLength, after.PasswordMinLength);
            Diff("passwordExpiryDays", before.PasswordExpiryDays, after.PasswordExpiryDays);
            Diff("passwordHistory", before.PasswordHistory, after.PasswordHistory);
            Diff("oneSessionPerUser", before.OneSessionPerUser, after.OneSessionPerUser);
            Diff("activityRetentionMonths", before.ActivityRetentionMonths, after.ActivityRetentionMonths);
            var settings = tenant.Settings;
            settings.Security = after;
            tenant.Settings = settings;
            db.Entry(tenant).Property(t => t.Settings).IsModified = true;
            if (changes.Count > 0) AuditLog.Event(db, "Tenant", tenant.Id, "SettingsChanged", changes);
            await db.SaveChangesAsync(ct);
            return Results.Ok(after);
        });
    }
}
