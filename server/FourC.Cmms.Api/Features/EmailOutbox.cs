using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Purchasing;
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
public sealed class EmailOutbox(IOptions<EmailOptions> options, IConfiguration config, AccessService access)
{
    public bool Available => options.Value.Available;

    /// <summary>E-mail set up on the server and this organization allowed to send (see <see cref="EmailOptions.AllowedOrganizations"/>).</summary>
    public bool AllowedFor(FourC.Cmms.Core.Tenancy.Tenant tenant) => Available && options.Value.AllowsOrganization(tenant.Slug, config.GetValue("Auth:AllowRegistration", true));

    private string? Link(string path) => config["App:PublicUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + path : null;

    // ---------------- Work orders ----------------

    /// <summary>A new work order: its assignees hear (PM work under its own switch).</summary>
    public Task WorkOrderCreatedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct) =>
        AssigneesAddedAsync(db, ctx, w, w.Assignees.Select(a => a.EmployeeId).ToList(), ct);

    public async Task AssigneesAddedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (!Available || employeeIds.Count == 0) return;
        var pm = w.PmScheduleId is not null;
        var key = pm ? EmailEvents.PmAssigned : EmailEvents.WorkOrderAssigned;
        var to = Union(await GroupsAsync(db, key, w, null, employeeIds, ct));
        if (to.Count == 0) return;
        var facts = await FactsAsync(db, w, ct);
        await QueueAsync(db, ctx, key, to, org => EmailTemplates.Assigned(org, facts, Link($"/work-orders/{w.Id}"), pm), "WorkOrder", w.Id, ct,
            siteId: w.SiteId, locationId: w.LocationId);
    }

    /// <summary>Given to a team: its members hear, as when assigned (those already assigned heard that already).</summary>
    public async Task TeamGivenAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)
    {
        if (!Available || w.TeamId is not { } teamId) return;
        var members = await db.Teams.AllSites().Where(t => t.Id == teamId).Select(t => t.MemberIds).FirstOrDefaultAsync(ct) ?? [];
        var assigned = w.Assignees.Select(a => a.EmployeeId).ToHashSet();
        var active = await db.Employees.Where(e => members.Contains(e.Id) && e.IsActive).Select(e => e.Id).ToListAsync(ct);
        await AssigneesAddedAsync(db, ctx, w, active.Where(m => !assigned.Contains(m)).ToList(), ct);
    }

    /// <summary>Completed: the grid's recipients hear (by default whoever created the work); the requester is asked to accept or refuse.</summary>
    public async Task WorkOrderCompletedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)
    {
        if (!Available) return;
        var facts = await FactsAsync(db, w, ct);
        var to = Union(await GroupsAsync(db, EmailEvents.WorkOrderCompleted, w, null, null, ct));
        if (to.Count > 0)
            await QueueAsync(db, ctx, EmailEvents.WorkOrderCompleted, to,
                org => EmailTemplates.WorkOrderCompleted(org, facts, Link($"/work-orders/{w.Id}")), "WorkOrder", w.Id, ct, siteId: w.SiteId, locationId: w.LocationId);
        if (w.WorkRequestId is { } rid && await db.WorkRequests.AllSites().AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) is { } r)
        {
            var requester = Union(await GroupsAsync(db, EmailEvents.RequestCompleted, w, r, null, ct));
            if (requester.Count == 0) return;
            var rf = await FactsAsync(db, r, ct);
            await QueueAsync(db, ctx, EmailEvents.RequestCompleted, requester,
                org => EmailTemplates.RequestCompleted(org, rf, w.Number, w.CompletionNotes, Link($"/work-requests?open={r.Id}")), "WorkRequest", r.Id, ct,
                siteId: r.SiteId, locationId: r.LocationId);
        }
    }

    // ---------------- Work requests ----------------

    /// <summary>A new request: its recipients hear (the site's, the type's and its own extra ones; else the people who triage requests).</summary>
    public async Task RequestSubmittedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, CancellationToken ct)
    {
        if (!Available) return;
        var to = Union(await GroupsAsync(db, EmailEvents.RequestSubmitted, null, r, null, ct));
        if (to.Count == 0) return;
        var facts = await FactsAsync(db, r, ct);
        await QueueAsync(db, ctx, EmailEvents.RequestSubmitted, to,
            org => EmailTemplates.RequestSubmitted(org, facts, r.RequesterName, Link($"/work-requests?open={r.Id}")), "WorkRequest", r.Id, ct,
            siteId: r.SiteId, locationId: r.LocationId);
    }

    /// <summary>Approved or rejected (by the request's status): the grid's recipients hear (by default the requester).</summary>
    public async Task RequestDecidedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, string? workOrderNumber, CancellationToken ct)
    {
        if (!Available) return;
        var key = r.Status switch { WorkRequestStatus.Approved => EmailEvents.RequestApproved, WorkRequestStatus.Rejected => EmailEvents.RequestRejected, _ => null };
        if (key is null) return;
        var w = r.WorkOrderId is { } wid ? await db.WorkOrders.AllSites().AsNoTracking().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == wid, ct) : null;
        var to = Union(await GroupsAsync(db, key, w, r, null, ct));
        if (to.Count == 0) return;
        var facts = await FactsAsync(db, r, ct);
        var link = Link($"/work-requests?open={r.Id}");
        await QueueAsync(db, ctx, key, to, org => key == EmailEvents.RequestApproved
                ? EmailTemplates.RequestApproved(org, facts, workOrderNumber ?? "", link)
                : EmailTemplates.RequestRejected(org, facts, r.DecisionNote ?? "", link),
            "WorkRequest", r.Id, ct, siteId: r.SiteId, locationId: r.LocationId);
    }

    // ---------------- Who gets each e-mail (0.10.0 grid) ----------------

    /// <summary>
    /// The people each recipient kind switched on for <paramref name="eventKey"/> stands for. Technicians and contractors are the work's
    /// assignees and the members of its team (split by "contractor"), or only <paramref name="onlyEmployees"/> when given (people just added).
    /// </summary>
    internal async Task<Dictionary<string, List<EmailRecipient>>> GroupsAsync(CmmsDbContext db, string eventKey, WorkOrder? w, WorkRequest? r,
        IReadOnlyCollection<Guid>? onlyEmployees, CancellationToken ct)
    {
        var groups = new Dictionary<string, List<EmailRecipient>>();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == db.CurrentTenantId, ct);
        if (tenant is null) return groups;
        var kinds = EmailEvents.KindsOn(tenant.Settings, eventKey);
        if (kinds.Count == 0) return groups;
        if (r is null && w?.WorkRequestId is { } rid) r = await db.WorkRequests.AllSites().AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid, ct);
        foreach (var kind in kinds)
            groups[kind] = kind switch
            {
                EmailRecipientKinds.Requester => r is null ? [] : [await RequesterAsync(db, r, ct)],
                EmailRecipientKinds.RequestRecipients => r is null ? [] : await RequestRecipientsAsync(db, r, ct),
                EmailRecipientKinds.Technicians => w is null ? [] : await WorkersAsync(db, w, onlyEmployees, contractors: false, ct),
                EmailRecipientKinds.Contractors => w is null ? [] : await WorkersAsync(db, w, onlyEmployees, contractors: true, ct),
                EmailRecipientKinds.Issuer => (w is not null ? w.CreatedById : r?.CreatedById) is { } issuer ? await UsersAsync(db, [issuer], ct) : [],
                _ => [],
            };
        return groups;
    }

    /// <summary>Everyone in <paramref name="groups"/> (one entry per address is kept later, when queuing).</summary>
    internal static List<EmailRecipient> Union(Dictionary<string, List<EmailRecipient>> groups) => groups.Values.SelectMany(x => x).ToList();

    /// <summary>The work's people: assignees (also those not saved yet) and active team members, in-house or contractors.</summary>
    private static async Task<List<EmailRecipient>> WorkersAsync(CmmsDbContext db, WorkOrder w, IReadOnlyCollection<Guid>? onlyEmployees, bool contractors,
        CancellationToken ct)
    {
        List<Guid> ids;
        if (onlyEmployees is not null) ids = [.. onlyEmployees];
        else
        {
            var saved = await db.WorkOrderAssignees.Where(a => a.WorkOrderId == w.Id).Select(a => a.EmployeeId).ToListAsync(ct);
            var members = w.TeamId is { } team ? await db.Teams.AllSites().Where(t => t.Id == team).Select(t => t.MemberIds).FirstOrDefaultAsync(ct) ?? [] : [];
            var activeMembers = members.Count == 0 ? [] : await db.Employees.Where(e => members.Contains(e.Id) && e.IsActive).Select(e => e.Id).ToListAsync(ct);
            ids = [.. w.Assignees.Select(a => a.EmployeeId).Concat(saved).Concat(activeMembers).Distinct()];
        }
        if (ids.Count == 0) return [];
        var kept = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.IsContractor == contractors).Select(e => e.Id).ToListAsync(ct);
        return kept.Count == 0 ? [] : await EmployeesAsync(db, kept, ct);
    }

    // ---------------- Purchasing ----------------

    /// <summary>Submitted for approval: approvers whose limit covers the order (or who have none) hear.</summary>
    public async Task PoSubmittedAsync(CmmsDbContext db, ITenantContext ctx, PurchaseOrder po, CancellationToken ct)
    {
        if (!Available) return;
        var total = PurchaseOrderMath.TotalInBase(po);
        var ids = await UserIdsWithPermissionAsync(db, Permissions.PurchasingApprove, ct);
        var approvers = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId && (u.PurchaseApprovalLimit == null || u.PurchaseApprovalLimit >= total))
            .Select(u => new EmailRecipient(u.Email, u.FullName, u.Id)).ToListAsync(ct);
        var facts = await FactsAsync(db, po, ct);
        await QueueAsync(db, ctx, EmailEvents.PoSubmitted, approvers, org => EmailTemplates.PoSubmitted(org, facts, Link($"/purchasing/orders/{po.Id}")),
            "PurchaseOrder", po.Id, ct, siteId: po.SiteId, siteLevel: true);
    }

    /// <summary>Approved or returned: whoever created the order hears.</summary>
    public async Task PoDecidedAsync(CmmsDbContext db, ITenantContext ctx, PurchaseOrder po, bool approved, string? reason, CancellationToken ct)
    {
        if (!Available || po.CreatedById is not { } creator) return;
        var facts = await FactsAsync(db, po, ct);
        var link = Link($"/purchasing/orders/{po.Id}");
        await QueueAsync(db, ctx, approved ? EmailEvents.PoApproved : EmailEvents.PoRejected, await UsersAsync(db, [creator], ct),
            org => approved ? EmailTemplates.PoApproved(org, facts, link) : EmailTemplates.PoRejected(org, facts, reason ?? "", link), "PurchaseOrder", po.Id, ct,
            siteId: po.SiteId, siteLevel: true);
    }

    /// <summary>Lines charged to work orders were received: each work order's assignees hear about their lines.</summary>
    public async Task PartsReceivedAsync(CmmsDbContext db, ITenantContext ctx, PurchaseOrder po, IReadOnlyCollection<(Guid WorkOrderId, string Line)> received,
        CancellationToken ct)
    {
        if (!Available) return;
        foreach (var group in received.GroupBy(r => r.WorkOrderId))
        {
            var w = await db.WorkOrders.AsNoTracking().Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == group.Key, ct);
            if (w is null || w.Assignees.Count == 0) continue;
            var facts = await FactsAsync(db, w, ct);
            var lines = group.Select(g => g.Line).ToList();
            // The assignees on the work (not its team): they are the ones waiting for these parts.
            var to = Union(await GroupsAsync(db, EmailEvents.PartsReceived, w, null, w.Assignees.Select(a => a.EmployeeId).ToList(), ct));
            if (to.Count == 0) continue;
            await QueueAsync(db, ctx, EmailEvents.PartsReceived, to,
                org => EmailTemplates.PartsReceived(org, facts, po.Number, lines, Link($"/work-orders/{w.Id}")), "WorkOrder", w.Id, ct, siteId: w.SiteId, locationId: w.LocationId);
        }
    }

    /// <summary>"Send to supplier": queues the order to the supplier. Returns why it can't, or null when queued.</summary>
    public async Task<string?> PoToSupplierAsync(CmmsDbContext db, ITenantContext ctx, PurchaseOrder po, CancellationToken ct)
    {
        if (!Available) return "E-mail isn't set up on this server.";
        if (await NotAllowedAsync(db, ct) is { } notAllowed) return notAllowed;
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        if (!settings.EmailEnabled) return "E-mail is switched off for this organization. Switch it on in Settings › Notifications.";
        var supplier = po.Supplier ?? await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == po.SupplierId, ct);
        if (string.IsNullOrWhiteSpace(supplier.Email)) return "The supplier has no e-mail address.";
        if (!EmailText.IsAddress(supplier.Email.Trim())) return $"The supplier's e-mail ({supplier.Email}) isn't a valid e-mail address. Correct it on the supplier first.";
        string Money(decimal v) => $"{v:N2} {po.Currency}";
        // Built in the e-mail's language (dates and labels), which for a supplier is the organization's.
        PoDocument Doc() => new(po.Number, Translator.Date(po.OrderDate), po.ExpectedDate is { } ed ? Translator.Date(ed) : null, po.DeliverTo, po.PaymentTerms,
            po.SupplierQuoteRef, po.RequestedBy,
            [.. po.Lines.OrderBy(l => l.LineNumber).Select(l => new PoDocumentLine(l.LineNumber, l.Description,
                $"{l.Quantity:0.###} {l.UnitOfMeasure}".Trim(), Money(l.NetUnitPrice), Money(l.LineTotal)))],
            [(Translator.T("Subtotal"), Money(PurchaseOrderMath.Subtotal(po))), (Translator.T($"Tax ({po.TaxPercent:0.##}%)"), Money(PurchaseOrderMath.Tax(po))),
             (Translator.T("Shipping"), Money(po.ShippingCost)), (Translator.T("Total"), Money(PurchaseOrderMath.Total(po)))]);
        await QueueAsync(db, ctx, EmailEvents.PoSent, [new EmailRecipient(supplier.Email, supplier.ContactName ?? supplier.Name, null)],
            org => EmailTemplates.PurchaseOrder(org, Doc()), "PurchaseOrder", po.Id, ct, ignoreToggles: true, siteId: po.SiteId, siteLevel: true);
        return null;
    }

    /// <summary>"Send me a test e-mail": works before the organization switches e-mail on, to check the setup.</summary>
    public async Task<string?> TestAsync(CmmsDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        if (!Available) return "E-mail isn't set up on this server.";
        if (await NotAllowedAsync(db, ct) is { } notAllowed) return notAllowed;
        var me = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => new EmailRecipient(u.Email, u.FullName, u.Id)).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(me?.Email)) return "Your account has no e-mail address.";
        await QueueAsync(db, ctx, EmailEvents.Test, [me], org => EmailTemplates.Layout(org, "Test e-mail from CMX", "E-mail is working",
                [("Organization", org), ("Sent to", me.Email)], "If you can read this, CMX can send e-mail to you.",
                Link("/settings/notifications"), "Open notification settings"),
            null, null, ct, ignoreToggles: true, ignoreEnabled: true, keepActor: true);
        return null;
    }

    private async Task<string?> NotAllowedAsync(CmmsDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
        return AllowedFor(tenant) ? null
            : $"E-mail isn't available for this organization on this server yet. Ask the server's operator to allow organization \"{tenant.Slug}\".";
    }

    // ---------------- Core ----------------

    /// <summary>Adds one row per remaining recipient to <paramref name="db"/> (the caller saves). <paramref name="ignoreToggles"/>
    /// is for e-mails a user asks for explicitly (send a PO, a test): they skip the event switch and opt-outs.</summary>
    /// <param name="siteId">The record's site: people who can't see it (or its location, <paramref name="locationId"/>) are left out, and
    /// the site's sender name is used.</param>
    internal async Task QueueAsync(CmmsDbContext db, ITenantContext ctx, string eventKey, IEnumerable<EmailRecipient> to, Func<string, EmailContent> build,
        string? entityType, Guid? entityId, CancellationToken ct, bool ignoreToggles = false, bool ignoreEnabled = false, bool keepActor = false,
        Guid? siteId = null, Guid? locationId = null, bool siteLevel = false)
    {
        if (!Available) return;
        var tenantId = db.CurrentTenantId ?? ctx.TenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null || !AllowedFor(tenant) || (!tenant.Settings.EmailEnabled && !ignoreEnabled)) return;
        if (!ignoreToggles && (tenant.Settings.DisabledEmails ?? []).Contains(eventKey)) return;

        var actorEmail = ctx.UserId is { } uid ? await db.Users.Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct) : null;
        // Only addresses that can be sent to: a broken one would fail at the mail service on every retry.
        var list = to.Where(r => EmailText.IsAddress(r.Email?.Trim()))
            .Select(r => r with { Email = r.Email!.Trim() })
            .Where(r => keepActor || ((r.UserId is null || r.UserId != ctx.UserId) && !string.Equals(r.Email, actorEmail, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(r => r.Email!.ToLowerInvariant()).ToList();
        if (!ignoreToggles && list.Any(r => r.UserId is not null))
        {
            var userIds = list.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();
            var optedOut = (await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.EmailOptOuts }).ToListAsync(ct))
                .Where(u => u.EmailOptOuts.Contains(eventKey)).Select(u => u.Id).ToHashSet();
            list = list.Where(r => r.UserId is null || !optedOut.Contains(r.UserId.Value)).ToList();
        }
        string? fromName = null;
        if (siteId is { } site)
        {
            fromName = (await db.Sites.AllSites().Where(s => s.Id == site).Select(s => s.Settings).FirstOrDefaultAsync(ct))?.EmailSenderName;
            var path = locationId is { } lid ? await db.Locations.AllSites().Where(l => l.Id == lid).Select(l => l.Path).FirstOrDefaultAsync(ct) : null;
            var kept = new List<EmailRecipient>();
            foreach (var r in list)
            {
                if (r.UserId is not { } recipientId) { kept.Add(r); continue; }
                var theirs = await access.GetAsync(recipientId, ct);
                if (siteLevel ? Access.CoversSite(theirs, site) : Access.Covers(theirs, site, path)) kept.Add(r);
            }
            list = kept;
        }
        if (list.Count == 0) return;

        // Each person in their own language (their choice, else the organization's); people without an account (suppliers,
        // requesters by e-mail) in the organization's.
        var userIdsToAsk = list.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();
        var chosen = userIdsToAsk.Count == 0 ? [] : await db.Users.Where(u => userIdsToAsk.Contains(u.Id)).Select(u => new { u.Id, u.Language }).ToDictionaryAsync(u => u.Id, u => u.Language, ct);
        foreach (var group in list.GroupBy(r => Languages.Effective(r.UserId is { } id ? chosen.GetValueOrDefault(id) : null, tenant.Settings.DefaultLanguage)))
        {
            EmailContent content;
            using (Translator.Use(group.Key)) content = build(tenant.Name);
            foreach (var r in group)
                db.OutboundEmails.Add(new OutboundEmail
                {
                    TenantId = tenant.Id, EventKey = eventKey, ToAddress = r.Email!, FromName = fromName,
                    ToName = r.Name is null ? null : EmailText.OneLine(r.Name), ReplyTo = EmailText.IsAddress(actorEmail) ? actorEmail : null,
                    Subject = EmailText.OneLine(content.Subject),
                    HtmlBody = content.Html, TextBody = content.Text, EntityType = entityType, EntityId = entityId,
                });
        }
    }

    // ---------------- Recipients and facts ----------------

    /// <summary>
    /// A request's recipients: the site's, the request type's and the request's own extra recipients. When neither the site nor the type
    /// names anyone, the people who can manage requests (as before 0.10.0), plus the extra recipients.
    /// </summary>
    internal static async Task<List<EmailRecipient>> RequestRecipientsAsync(CmmsDbContext db, WorkRequest r, CancellationToken ct)
    {
        var site = (await db.Sites.AllSites().Where(s => s.Id == r.SiteId).Select(s => s.Settings).FirstOrDefaultAsync(ct))?.RequestRecipientIds ?? [];
        var type = r.RequestTypeId is { } typeId ? await db.RequestTypes.Where(t => t.Id == typeId).Select(t => t.RecipientIds).FirstOrDefaultAsync(ct) ?? [] : [];
        var named = site.Concat(type).Distinct().ToList();
        var people = named.Count > 0 ? await UsersAsync(db, named, ct) : await UsersWithPermissionAsync(db, Permissions.RequestsManage, ct);
        if (r.ExtraRecipientIds.Count > 0) people.AddRange(await UsersAsync(db, r.ExtraRecipientIds, ct));
        return people.DistinctBy(p => p.UserId).ToList();
    }

    /// <summary>Employees by their own address, or the address of the user account they're linked to.</summary>
    internal static async Task<List<EmailRecipient>> EmployeesAsync(CmmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        (await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Email, e.FullName, e.UserId, UserEmail = db.Users.Where(u => u.Id == e.UserId).Select(u => u.Email).FirstOrDefault() })
            .ToListAsync(ct)).Select(e => new EmailRecipient(e.Email ?? e.UserEmail, e.FullName, e.UserId)).ToList();

    internal static async Task<List<EmailRecipient>> UsersAsync(CmmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId)
            .Select(u => new EmailRecipient(u.Email, u.FullName, u.Id)).ToListAsync(ct);

    /// <summary>Active users holding <paramref name="permission"/> through any of their roles.</summary>
    internal static async Task<List<EmailRecipient>> UsersWithPermissionAsync(CmmsDbContext db, string permission, CancellationToken ct) =>
        await UsersAsync(db, await UserIdsWithPermissionAsync(db, permission, ct), ct);

    private static async Task<List<Guid>> UserIdsWithPermissionAsync(CmmsDbContext db, string permission, CancellationToken ct)
    {
        var roleIds = (await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Permissions }).ToListAsync(ct))
            .Where(r => r.Permissions.Contains(permission)).Select(r => r.Id).ToList();
        return await db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
    }

    private static async Task<PoFacts> FactsAsync(CmmsDbContext db, PurchaseOrder po, CancellationToken ct)
    {
        var supplier = po.Supplier?.Name ?? await db.Suppliers.AsNoTracking().Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return new PoFacts(po.Number, supplier, $"{PurchaseOrderMath.Total(po):N2} {po.Currency}", po.RequestedBy);
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
