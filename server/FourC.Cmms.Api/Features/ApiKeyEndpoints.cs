using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Settings › API keys (0.26.0): keys for service accounts, shown once, revocable. Manage users.</summary>
public static class ApiKeyEndpoints
{
    public sealed record ApiKeyInput(Guid ServiceAccountId, string Name, bool ReadOnly, DateTimeOffset? ExpiresAt);

    public static void MapApiKeyEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/api-keys").WithTags("API keys").RequirePermission(Permissions.UsersManage);

        g.MapGet("/", async (CmmsDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var keys = await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
            var ids = keys.Select(k => k.ServiceAccountId).Distinct().ToList();
            var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.TenantId == db.CurrentTenantId).ToListAsync(ct);
            // Like people: a site-limited manager sees the keys of accounts inside their own access only.
            var editor = Access.Of(http) ?? UserAccess.Everything;
            var inside = new HashSet<Guid>();
            foreach (var u in users) if (await UserAccessRules.OutsideAsync(db, editor, u, ct) is null) inside.Add(u.Id);
            keys = keys.Where(k => inside.Contains(k.ServiceAccountId)).ToList();
            var accounts = users.ToDictionary(u => u.Id, u => new { u.FullName, u.IsActive });
            return keys.Select(k => new
            {
                k.Id, k.Name, k.ServiceAccountId, ServiceAccountName = accounts.GetValueOrDefault(k.ServiceAccountId)?.FullName, k.Prefix, k.ReadOnly, k.CreatedAt,
                k.CreatedByName, k.ExpiresAt, k.LastUsedAt, k.RevokedAt,
                Status = k.RevokedAt is not null ? "Revoked" : k.ExpiresAt <= now ? "Expired"
                    : accounts.GetValueOrDefault(k.ServiceAccountId)?.IsActive != true ? "AccountInactive" : "Active",
            });
        });

        g.MapPost("/", async (ApiKeyInput input, CmmsDbContext db, ITenantContext ctx, HttpContext http, CancellationToken ct) =>
        {
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == input.ServiceAccountId && u.TenantId == db.CurrentTenantId && u.IsServiceAccount, ct);
            var v = new Validator().Required("name", input.Name, "Name", 100)
                .When(account is null, "serviceAccountId", "Choose a service account.")
                .When(input.ExpiresAt is { } e && e <= DateTimeOffset.UtcNow, "expiresAt", "Choose an expiry date in the future.");
            if (!v.IsValid) return v.Problem();
            if (await UserAccessRules.OutsideAsync(db, Access.Of(http) ?? UserAccess.Everything, account!, ct) is { } outside) throw new DomainException(outside);
            var (key, prefix, hash) = ApiKeyAuth.Make();
            var row = new ApiKey
            {
                ServiceAccountId = account!.Id, Name = input.Name.Trim(), Prefix = prefix, Hash = hash, ReadOnly = input.ReadOnly, ExpiresAt = input.ExpiresAt,
                CreatedAt = DateTimeOffset.UtcNow, CreatedById = ctx.UserId, CreatedByName = ctx.UserName,
            };
            db.ApiKeys.Add(row);
            await db.SaveChangesAsync(ct);
            // The only time the key is ever shown.
            return Results.Created($"/api/api-keys/{row.Id}", new { row.Id, key, prefix });
        });

        g.MapPost("/{id:guid}/revoke", async (Guid id, CmmsDbContext db, ITenantContext ctx, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (await db.Users.FirstOrDefaultAsync(u => u.Id == row.ServiceAccountId && u.TenantId == db.CurrentTenantId, ct) is { } account
                && await UserAccessRules.OutsideAsync(db, Access.Of(http) ?? UserAccess.Everything, account, ct) is { } outside) throw new DomainException(outside);
            if (row.RevokedAt is null) { row.RevokedAt = DateTimeOffset.UtcNow; row.RevokedById = ctx.UserId; await db.SaveChangesAsync(ct); }
            return Results.Ok(new { row.Id });
        });
    }

    /// <summary>Revokes every key of a service account that is deleted.</summary>
    internal static async Task RevokeAllAsync(CmmsDbContext db, Guid accountId, Guid? by, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var k in await db.ApiKeys.Where(k => k.ServiceAccountId == accountId && k.RevokedAt == null).ToListAsync(ct)) { k.RevokedAt = now; k.RevokedById = by; }
        await db.SaveChangesAsync(ct);
    }
}
