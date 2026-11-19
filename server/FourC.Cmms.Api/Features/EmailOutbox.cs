using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features;

/// <summary>Someone an e-mail may go to. <see cref="UserId"/> links to a user account (for opt-outs and "don't e-mail the actor").</summary>
public sealed record EmailRecipient(string? Email, string? Name, Guid? UserId);

/// <summary>
/// Queues notification e-mails: adds rows to the caller's DbContext, so they are saved with the change that caused
/// them. Applies every rule in one place: e-mail set up on the server and switched on by the organization, the
/// organization's switch for the event, each user's opt-outs, never the person who acted, one e-mail per address.
/// </summary>
public sealed class EmailOutbox(IOptions<EmailOptions> options, IConfiguration config)
{
    public bool Available => options.Value.Available;

    private string? Link(string path) => config["App:PublicUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + path : null;

    // ---------------- Work orders ----------------

    /// <summary>A new work order: its assignees hear (PM work under its own switch).</summary>
    public Task WorkOrderCreatedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct) =>
        AssigneesAddedAsync(db, ctx, w, w.Assignees.Select(a => a.EmployeeId).ToList(), ct);

    public async Task AssigneesAddedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (!Available || employeeIds.Count == 0) return;
        var to = await EmployeesAsync(db, employeeIds, ct);
        var facts = await FactsAsync(db, w, ct);
        var pm = w.PmScheduleId is not null;
        await QueueAsync(db, ctx, pm ? EmailEvents.PmAssigned : EmailEvents.WorkOrderAssigned, to,
            org => EmailTemplates.Assigned(org, facts, Link($"/work-orders/{w.Id}"), pm), "WorkOrder", w.Id, ct);
    }

    /// <summary>Completed: whoever created the work hears, and so does the requester when it came from a request.</summary>
    public async Task WorkOrderCompletedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)
    {
        if (!Available) return;
        var facts = await FactsAsync(db, w, ct);
        if (w.CreatedById is { } creator)
            await QueueAsync(db, ctx, EmailEvents.WorkOrderCompleted, await UsersAsync(db, [creator], ct),
                org => EmailTemplates.WorkOrderCompleted(org, facts, Link($"/work-orders/{w.Id}")), "WorkOrder", w.Id, ct);
        if (w.WorkRequestId is { } rid && await db.WorkRequests.FirstOrDefaultAsync(r => r.Id == rid, ct) is { } r)
        {
            var rf = await FactsAsync(db, r, ct);
            await QueueAsync(db, ctx, EmailEvents.RequestCompleted, [await RequesterAsync(db, r, ct)],
                org => EmailTemplates.RequestCompleted(org, rf, w.Number, w.CompletionNotes, Link($"/work-requests?open={r.Id}")), "WorkRequest", r.Id, ct);
        }
    }

    // ---------------- Work requests ----------------

    /// <summary>A new request: the people who triage requests hear.</summary>
    public async Task RequestSubmittedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, CancellationToken ct)
    {
        if (!Available) return;
        var facts = await FactsAsync(db, r, ct);
        await QueueAsync(db, ctx, EmailEvents.RequestSubmitted, await UsersWithPermissionAsync(db, Permissions.RequestsManage, ct),
            org => EmailTemplates.RequestSubmitted(org, facts, r.RequesterName, Link($"/work-requests?open={r.Id}")), "WorkRequest", r.Id, ct);
    }

    /// <summary>Approved or rejected (by the request's status): the requester hears.</summary>
    public async Task RequestDecidedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, string? workOrderNumber, CancellationToken ct)
    {
        if (!Available) return;
        var facts = await FactsAsync(db, r, ct);
        var link = Link($"/work-requests?open={r.Id}");
        if (r.Status == WorkRequestStatus.Approved)
            await QueueAsync(db, ctx, EmailEvents.RequestApproved, [await RequesterAsync(db, r, ct)],
                org => EmailTemplates.RequestApproved(org, facts, workOrderNumber ?? "", link), "WorkRequest", r.Id, ct);
        else if (r.Status == WorkRequestStatus.Rejected)
            await QueueAsync(db, ctx, EmailEvents.RequestRejected, [await RequesterAsync(db, r, ct)],
                org => EmailTemplates.RequestRejected(org, facts, r.DecisionNote ?? "", link), "WorkRequest", r.Id, ct);
    }

    // ---------------- Core ----------------

    /// <summary>Adds one row per remaining recipient to <paramref name="db"/> (the caller saves). <paramref name="ignoreToggles"/>
    /// is for e-mails a user asks for explicitly (send a PO, a test): they skip the event switch and opt-outs.</summary>
    internal async Task QueueAsync(CmmsDbContext db, ITenantContext ctx, string eventKey, IEnumerable<EmailRecipient> to, Func<string, EmailContent> build,
        string? entityType, Guid? entityId, CancellationToken ct, bool ignoreToggles = false, bool ignoreEnabled = false)
    {
        if (!Available) return;
        var tenantId = db.CurrentTenantId ?? ctx.TenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null || (!tenant.Settings.EmailEnabled && !ignoreEnabled)) return;
        if (!ignoreToggles && (tenant.Settings.DisabledEmails ?? []).Contains(eventKey)) return;

        var actorEmail = ctx.UserId is { } uid ? await db.Users.Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct) : null;
        var list = to.Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r with { Email = r.Email!.Trim() })
            .Where(r => (r.UserId is null || r.UserId != ctx.UserId) && !string.Equals(r.Email, actorEmail, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(r => r.Email!.ToLowerInvariant()).ToList();
        if (!ignoreToggles && list.Any(r => r.UserId is not null))
        {
            var userIds = list.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();
            var optedOut = (await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.EmailOptOuts }).ToListAsync(ct))
                .Where(u => u.EmailOptOuts.Contains(eventKey)).Select(u => u.Id).ToHashSet();
            list = list.Where(r => r.UserId is null || !optedOut.Contains(r.UserId.Value)).ToList();
        }
        if (list.Count == 0) return;

        var content = build(tenant.Name);
        foreach (var r in list)
            db.OutboundEmails.Add(new OutboundEmail
            {
                TenantId = tenant.Id, EventKey = eventKey, ToAddress = r.Email!.Length > 320 ? r.Email[..320] : r.Email,
                ToName = r.Name is null ? null : EmailText.OneLine(r.Name), ReplyTo = actorEmail, Subject = EmailText.OneLine(content.Subject),
                HtmlBody = content.Html, TextBody = content.Text, EntityType = entityType, EntityId = entityId,
            });
    }

    // ---------------- Recipients and facts ----------------

    /// <summary>Employees by their own address, or the address of the user account they're linked to.</summary>
    internal static async Task<List<EmailRecipient>> EmployeesAsync(CmmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        (await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Email, e.FullName, e.UserId, UserEmail = db.Users.Where(u => u.Id == e.UserId).Select(u => u.Email).FirstOrDefault() })
            .ToListAsync(ct)).Select(e => new EmailRecipient(e.Email ?? e.UserEmail, e.FullName, e.UserId)).ToList();

    internal static async Task<List<EmailRecipient>> UsersAsync(CmmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId)
            .Select(u => new EmailRecipient(u.Email, u.FullName, u.Id)).ToListAsync(ct);

    /// <summary>Active users holding <paramref name="permission"/> through any of their roles.</summary>
    internal static async Task<List<EmailRecipient>> UsersWithPermissionAsync(CmmsDbContext db, string permission, CancellationToken ct)
    {
        var roleIds = (await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Permissions }).ToListAsync(ct))
            .Where(r => r.Permissions.Contains(permission)).Select(r => r.Id).ToList();
        var userIds = await db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
        return await UsersAsync(db, userIds, ct);
    }

    /// <summary>The request's contact address; it counts as the creating user (for opt-outs) when it's that user's address.</summary>
    private static async Task<EmailRecipient> RequesterAsync(CmmsDbContext db, WorkRequest r, CancellationToken ct)
    {
        var creator = r.CreatedById is { } id ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.Id, u.Email }).FirstOrDefaultAsync(ct) : null;
        var email = r.RequesterEmail ?? creator?.Email;
        var userId = creator is not null && string.Equals(creator.Email, email, StringComparison.OrdinalIgnoreCase) ? creator.Id : (Guid?)null;
        return new EmailRecipient(email, r.RequesterName, userId);
    }

    private static async Task<string?> PlaceAsync(CmmsDbContext db, Guid siteId, Guid? locationId, Guid? assetId, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == siteId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var location = locationId is { } l ? await db.Locations.AsNoTracking().Where(x => x.Id == l).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var asset = assetId is { } a ? await db.Assets.AsNoTracking().Where(x => x.Id == a).Select(x => x.AssetNumber + " · " + x.Name).FirstOrDefaultAsync(ct) : null;
        var parts = new[] { site, location, asset }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return parts.Count == 0 ? null : string.Join(" › ", parts);
    }

    private static async Task<WorkOrderFacts> FactsAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct) =>
        new(w.Number, w.Title, w.Priority.ToString(), w.DueDate, await PlaceAsync(db, w.SiteId, w.LocationId, w.AssetId, ct), w.Description, w.CompletionNotes);

    private static async Task<RequestFacts> FactsAsync(CmmsDbContext db, WorkRequest r, CancellationToken ct) =>
        new(r.Number, r.Title, r.Priority.ToString(), await PlaceAsync(db, r.SiteId, r.LocationId, r.AssetId, ct));
}
