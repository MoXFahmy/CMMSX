using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FourC.Cmms.Api.Features;

/// <summary>Signed-in devices: everyone sees and signs out their own; administrators (Manage users) anyone's.</summary>
public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/auth/me/devices").WithTags("Devices").RequireAuthorization();

        me.MapGet("/", async (HttpContext http, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            await ListAsync(db, ctx.UserId!.Value, await Sessions.CurrentAsync(http), ct));

        me.MapPost("/{id:guid}/sign-out", async (Guid id, CmmsDbContext db, ITenantContext ctx, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!await db.UserSessions.AnyAsync(s => s.Id == id && s.UserId == ctx.UserId, ct)) return Results.NotFound();
            await Sessions.RevokeAsync(db, cache, db.UserSessions.Where(s => s.Id == id), ct);
            return Results.Ok(new { ok = true });
        });

        me.MapPost("/sign-out-others", async (HttpContext http, CmmsDbContext db, ITenantContext ctx, IMemoryCache cache, CancellationToken ct) =>
        {
            var current = await Sessions.CurrentAsync(http);
            await Sessions.RevokeAsync(db, cache, db.UserSessions.Where(s => s.UserId == ctx.UserId && s.Id != current), ct);
            // Also sign-ins from before 0.4.0 that have not been used since (they have no device session yet).
            await db.Users.Where(u => u.Id == ctx.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.SignedOutBefore, DateTimeOffset.UtcNow), ct);
            return Results.Ok(new { ok = true });
        });

        var users = api.MapGroup("/users/{userId:guid}/devices").WithTags("Devices").RequirePermission(Permissions.UsersManage);

        users.MapGet("/", async (Guid userId, CmmsDbContext db, CancellationToken ct) =>
            await db.Users.AnyAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, ct) ? Results.Ok(await ListAsync(db, userId, null, ct)) : Results.NotFound());

        users.MapPost("/{id:guid}/sign-out", async (Guid userId, Guid id, CmmsDbContext db, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!await db.UserSessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct)) return Results.NotFound();
            await Sessions.RevokeAsync(db, cache, db.UserSessions.Where(s => s.Id == id), ct);
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>Devices still signed in: not signed out, and not past when their sign-in runs out.</summary>
    private static async Task<List<object>> ListAsync(CmmsDbContext db, Guid userId, Guid? current, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return (await db.UserSessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now))
                .OrderByDescending(s => s.LastSeenAt).ToListAsync(ct))
            .Select(s => (object)new { s.Id, s.DeviceName, s.CreatedAt, s.LastSeenAt, Current = s.Id == current }).ToList();
    }
}
