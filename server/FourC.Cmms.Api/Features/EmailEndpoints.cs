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
                // The Settings › Notifications grid: who each work and request e-mail can go to, and who it goes to by default.
                Grid = EmailEvents.All.Where(EmailEvents.Grid.ContainsKey).Select(k => new { Key = k, EmailEvents.Grid[k].Applicable, EmailEvents.Grid[k].Defaults }),
            });
        }).RequireAuthorization();

        g.MapPost("/test", async (CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            if (await mail.TestAsync(db, ctx, ct) is { } error) throw new DomainException(error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { queued = true });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapGet("/log", async (CmmsDbContext db, ITenantContext ctx, int? take, CancellationToken ct) =>
        {
            var q = db.OutboundEmails.AsNoTracking();
            if (db.CurrentSiteIds is not null)
            {
                // People limited to some sites see e-mails about records they can see, and their own test e-mails.
                var mine = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
                q = q.Where(e => (e.EntityType == "WorkOrder" && db.WorkOrders.Any(w => w.Id == e.EntityId))
                    || (e.EntityType == "WorkRequest" && db.WorkRequests.Any(r => r.Id == e.EntityId))
                    || (e.EntityType == "PurchaseOrder" && db.PurchaseOrders.Any(p => p.Id == e.EntityId))
                    || (e.EntityType == null && e.ToAddress == mine));
            }
            return await q.OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(e => new { e.Id, e.CreatedAt, e.SentAt, e.EventKey, e.ToAddress, e.Subject, e.Status, e.Attempts, e.LastError }).ToListAsync(ct);
        }).RequirePermission(Permissions.SettingsManage);

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
