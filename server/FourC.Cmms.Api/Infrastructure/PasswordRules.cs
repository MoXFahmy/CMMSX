using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// The organization's password rules (0.25.0): minimum length, "not one of the last N" and expiry. Checked wherever a password
/// is set (an administrator's, change password, renewing an expired one) and at sign-in.
/// </summary>
public static class PasswordRules
{
    public const string Expired = "Your password has expired. Choose a new one.";
    public const string RulesChanged = "Your organization's password rules changed. Choose a new password.";
    private const string Recent = "You used this password recently. Choose another one.";
    /// <summary>How many hashes are kept per person (the largest history an organization can ask for).</summary>
    public const int Kept = 24;

    public static async Task<SecuritySettings> SettingsAsync(CmmsDbContext db, Guid tenantId, CancellationToken ct) =>
        (await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Settings).FirstOrDefaultAsync(ct))?.Security ?? new SecuritySettings();

    /// <summary>What's wrong with <paramref name="password"/> as <paramref name="user"/>'s new password, by their organization's rules.</summary>
    public static async Task<IReadOnlyList<string>> ProblemsAsync(CmmsDbContext db, IPasswordHasher<AppUser> hasher, AppUser user, string password, CancellationToken ct)
    {
        var rules = await SettingsAsync(db, user.TenantId, ct);
        var problems = new List<string>();
        if (password.Length < rules.PasswordMinLength) problems.Add(string.Format(Translator.T("Use at least {0} characters."), rules.PasswordMinLength));
        // A new password is never the current one, and with a history rule not one of the last N either.
        var last = rules.PasswordHistory > 0
            ? await db.PasswordHistories.IgnoreQueryFilters().Where(h => h.UserId == user.Id).OrderByDescending(h => h.SetAt)
                .Take(rules.PasswordHistory).Select(h => h.Hash).ToListAsync(ct)
            : [];
        if (user.PasswordHash is { } current) last.Add(current);
        if (last.Any(h => hasher.VerifyHashedPassword(user, h, password) != PasswordVerificationResult.Failed)) problems.Add(Translator.T(Recent));
        return problems;
    }

    /// <summary>A password was just set: it joins the history (the last <see cref="Kept"/> stay) and the expiry clock restarts. The caller saves.</summary>
    public static async Task RecordAsync(CmmsDbContext db, AppUser user, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        user.PasswordChangedAt = now;
        if (user.PasswordHash is not { } hash) return;
        db.PasswordHistories.Add(new PasswordHistory { TenantId = user.TenantId, UserId = user.Id, Hash = hash, SetAt = now });
        var old = await db.PasswordHistories.IgnoreQueryFilters().Where(h => h.UserId == user.Id).OrderByDescending(h => h.SetAt).Skip(Kept - 1).ToListAsync(ct);
        db.PasswordHistories.RemoveRange(old);
    }

    /// <summary>Whether the password has outlived the organization's expiry.</summary>
    public static bool IsExpired(AppUser user, SecuritySettings rules, DateTimeOffset now) =>
        rules.PasswordExpiryDays > 0 && user.PasswordChangedAt is { } set && set.AddDays(rules.PasswordExpiryDays) <= now;
}
