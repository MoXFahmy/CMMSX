using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>E-mail status, the test e-mail, the organization's e-mail log and each user's opt-outs.</summary>
public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/email").WithTags("E-mail");

        g.MapGet("/status", async (CmmsDbContext db, EmailOutbox mail, Microsoft.Extensions.Options.IOptions<EmailOptions> options, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
            return Results.Ok(new
            {
                Available = mail.Available, Allowed = mail.AllowedFor(tenant), tenant.Slug, From = mail.Available ? options.Value.From : null,
                Enabled = tenant.Settings.EmailEnabled, Events = EmailEvents.All,
            });
        }).RequireAuthorization();

        g.MapPost("/test", async (CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            if (await mail.TestAsync(db, ctx, ct) is { } error) throw new DomainException(error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { queued = true });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapGet("/log", async (CmmsDbContext db, int? take, CancellationToken ct) =>
            await db.OutboundEmails.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(e => new { e.Id, e.CreatedAt, e.SentAt, e.EventKey, e.ToAddress, e.Subject, e.Status, e.Attempts, e.LastError }).ToListAsync(ct))
            .RequirePermission(Permissions.SettingsManage);

        var me = api.MapGroup("/auth/me/email-opt-outs").WithTags("E-mail").RequireAuthorization();
        me.MapGet("/", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.EmailOptOuts).FirstOrDefaultAsync(ct) ?? []);
        me.MapPut("/", async (List<string> optOuts, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ctx.UserId, ct);
            if (user is null) return Results.NotFound();
            user.EmailOptOuts = [.. (optOuts ?? []).Where(EmailEvents.All.Contains).Distinct()];
            await db.SaveChangesAsync(ct);
            return Results.Ok(user.EmailOptOuts);
        });
    }
}
