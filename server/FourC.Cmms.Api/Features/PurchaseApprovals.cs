using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Orders signed by approval levels (0.16.0). On submit, the levels an order needs are copied onto it for this round; each step
/// is signed in turn (any one or all of its approvers); a rejection returns the order to Draft; Restart re-reads the levels.
/// Orders that need no level are approved as before (one approver within their limit) — none of this applies to them.
/// </summary>
internal static class PurchaseApprovals
{
    /// <summary>The steps of the order's current round (empty when the order isn't being signed by levels).</summary>
    public static async Task<List<PurchaseOrderApprovalStep>> StepsAsync(CmmsDbContext db, PurchaseOrder po, CancellationToken ct) =>
        po.ApprovalRound == 0 ? [] : await db.PurchaseOrderApprovalSteps.Where(s => s.PurchaseOrderId == po.Id && s.Round == po.ApprovalRound).OrderBy(s => s.Step).ToListAsync(ct);

    /// <summary>
    /// The order's current round is signed by levels (it has steps), whatever their state — so approve and reject always go through
    /// <see cref="SignAsync"/>, which decides under the order's lock. Deciding from a step's state here instead would race: a second
    /// approver who read the order just before the first signature was saved would land on the old single-approver path (0.16.1).
    /// A returned order (Draft) isn't being signed; an order resubmitted when no level applied has an empty current round.
    /// </summary>
    public static async Task<bool> ByLevelsAsync(CmmsDbContext db, PurchaseOrder po, CancellationToken ct) =>
        po.Status != PurchaseOrderStatus.Draft && po.ApprovalRound > 0
        && await db.PurchaseOrderApprovalSteps.AnyAsync(s => s.PurchaseOrderId == po.Id && s.Round == po.ApprovalRound, ct);

    /// <summary>Whether levels would apply to this order now (to refuse approving a draft straight away).</summary>
    public static async Task<IReadOnlyList<ApprovalLevel>> LevelsForAsync(CmmsDbContext db, PurchaseOrder po, CancellationToken ct) =>
        ApprovalLevelRules.Required(await db.ApprovalLevels.AsNoTracking().ToListAsync(ct), po.SiteId, po.OrderTypeId, PurchaseOrderMath.TotalInBase(po));

    /// <summary>Someone may sign for this order today: active, "Approve purchases", the order's site, and not the one who submitted it.</summary>
    public static async Task<bool> MaySignAsync(CmmsDbContext db, PermissionService perms, AccessService access, PurchaseOrder po, Guid userId, CancellationToken ct)
    {
        if (userId == po.SubmittedById) return false;
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.TenantId == db.CurrentTenantId, ct)) return false;
        if (!(await perms.GetAsync(userId)).Contains(Permissions.PurchasingApprove)) return false;
        return Access.CoversSite(await access.GetAsync(userId, ct), po.SiteId);
    }

    /// <summary>The listed approvers of <paramref name="step"/> who could still sign it now.</summary>
    public static async Task<List<Guid>> CanSignNowAsync(CmmsDbContext db, PermissionService perms, AccessService access, PurchaseOrder po, PurchaseOrderApprovalStep step,
        CancellationToken ct)
    {
        var signed = await db.PurchaseOrderSignatures.Where(s => s.PurchaseOrderId == po.Id && s.Round == step.Round && s.Step == step.Step).Select(s => s.UserId).ToListAsync(ct);
        var result = new List<Guid>();
        foreach (var id in step.ApproverIds.Where(a => !signed.Contains(a)))
            if (await MaySignAsync(db, perms, access, po, id, ct)) result.Add(id);
        return result;
    }

    /// <summary>The waiting step can't be completed by the people listed on it today (nobody left on an "any" step, someone missing on an "all" step).</summary>
    public static async Task<bool> StuckAsync(CmmsDbContext db, PermissionService perms, AccessService access, PurchaseOrder po, PurchaseOrderApprovalStep step, CancellationToken ct)
    {
        var signed = await db.PurchaseOrderSignatures.Where(s => s.PurchaseOrderId == po.Id && s.Round == step.Round && s.Step == step.Step && s.Approved).Select(s => s.UserId).ToListAsync(ct);
        var able = await CanSignNowAsync(db, perms, access, po, step, ct);
        var missing = step.ApproverIds.Where(a => !signed.Contains(a)).ToList();
        return step.Rule == ApprovalRule.Any ? able.Count == 0 : missing.Any(m => !able.Contains(m));
    }

    /// <summary>
    /// On submit: copies the levels the order needs onto it as a new round. False when none apply (today's rule). Refuses when a level
    /// could never be signed for this order's site today.
    /// </summary>
    public static async Task<bool> StartAsync(CmmsDbContext db, ITenantContext ctx, PermissionService perms, AccessService access, EmailOutbox mail, PurchaseOrder po,
        CancellationToken ct)
    {
        var levels = await LevelsForAsync(db, po, ct);
        if (levels.Count == 0)
        {
            // Signed by levels before (then returned), none apply now: an empty round, so today's rule takes over.
            if (po.ApprovalRound > 0) po.ApprovalRound++;
            return false;
        }
        foreach (var level in levels)
        {
            var able = new List<Guid>();
            foreach (var id in level.ApproverIds) if (await MaySignAsync(db, perms, access, po, id, ct)) able.Add(id);
            var site = await db.Sites.AllSites().Where(s => s.Id == po.SiteId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            if (level.Rule == ApprovalRule.Any && able.Count == 0)
                throw new DomainException($"Level '{level.Name}' has nobody who can sign orders for {site}. Ask an administrator to fix the approval levels.");
            if (level.Rule == ApprovalRule.All && able.Count < level.ApproverIds.Count)
            {
                // Say exactly why an "all must sign" level could never be completed.
                if (po.SubmittedById is { } me && level.ApproverIds.Contains(me))
                    throw new DomainException($"Level '{level.Name}' needs all its approvers, and you submitted this order, so you can't sign it. Ask someone else to submit it, or let any one of them sign.");
                var missing = level.ApproverIds.First(a => !able.Contains(a));
                var who = await db.Users.Where(u => u.Id == missing).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "?";
                throw new DomainException($"Level '{level.Name}' needs all its approvers, but {who} can't sign orders for {site}. Ask an administrator to fix the approval levels.");
            }
        }
        po.ApprovalRound++;
        var n = 1;
        foreach (var level in levels)
            db.PurchaseOrderApprovalSteps.Add(new PurchaseOrderApprovalStep
            {
                TenantId = po.TenantId, PurchaseOrderId = po.Id, Round = po.ApprovalRound, Step = n, Name = level.Name, MinAmount = level.MinAmount, Rule = level.Rule,
                ApproverIds = [.. level.ApproverIds], Status = n++ == 1 ? ApprovalStepStatus.Waiting : ApprovalStepStatus.Pending,
            });
        await mail.PoApprovalWaitingAsync(db, ctx, po, levels[0].ApproverIds.Where(a => a != po.SubmittedById).ToList(), levels[0].Name, ct);
        return true;
    }

    /// <summary>Signs (or rejects) the waiting step. The order row is locked first, so two signatures at once are taken one after the other.</summary>
    public static async Task SignAsync(CmmsDbContext db, ITenantContext ctx, PermissionService perms, AccessService access, EmailOutbox mail, Guid orderId, bool approve,
        string? comment, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw new DomainException("Sign in first.");
        var why = comment.Clean();
        if (!approve && why is null) throw new DomainException("Give a reason.");
        await db.InTransactionAsync<bool>(async tk =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM PurchaseOrders WITH (UPDLOCK, ROWLOCK) WHERE Id = {orderId}", tk);
            db.ChangeTracker.Clear(); // read everything again now that we hold the order: another signature may just have been saved
            var po = await db.PurchaseOrders.Include(o => o.Lines).Include(o => o.Supplier).FirstOrDefaultAsync(o => o.Id == orderId, tk)
                ?? throw new BadHttpRequestException("Purchase order not found.", 404);
            var steps = await StepsAsync(db, po, tk);
            var waiting = steps.FirstOrDefault(s => s.Status == ApprovalStepStatus.Waiting);
            if (waiting is null || po.Status != PurchaseOrderStatus.Submitted)
                throw new DomainException(steps.Any(s => s.Status == ApprovalStepStatus.Approved && s.ApproverIds.Contains(me))
                    ? "This step is already signed off." : "This order isn't waiting for approval.");
            if (!waiting.ApproverIds.Contains(me))
            {
                if (steps.Any(s => s.Step > waiting.Step && s.ApproverIds.Contains(me))) throw new DomainException("It isn't your turn to sign this order.");
                if (steps.Any(s => s.Step < waiting.Step && s.ApproverIds.Contains(me))) throw new DomainException("This step is already signed off.");
                throw new DomainException("You aren't an approver of this order.");
            }
            if (me == po.SubmittedById) throw new DomainException("You submitted this order, so someone else must sign it.");
            if (await db.PurchaseOrderSignatures.AnyAsync(s => s.PurchaseOrderId == po.Id && s.Round == waiting.Round && s.Step == waiting.Step && s.UserId == me, tk))
                throw new DomainException("You have already signed this step.");
            if (!await MaySignAsync(db, perms, access, po, me, tk)) throw new DomainException("You can't sign this order any more.");

            var now = DateTimeOffset.UtcNow;
            db.PurchaseOrderSignatures.Add(new PurchaseOrderSignature
            {
                TenantId = po.TenantId, PurchaseOrderId = po.Id, Round = waiting.Round, Step = waiting.Step, UserId = me, UserName = ctx.UserName ?? "", Approved = approve,
                Comment = why, At = now,
            });
            if (!approve)
            {
                waiting.Status = ApprovalStepStatus.Rejected; waiting.DoneAt = now;
                foreach (var later in steps.Where(s => s.Status == ApprovalStepStatus.Pending)) later.Status = ApprovalStepStatus.Stopped;
                po.Status = PurchaseOrderStatus.Draft; po.SubmittedAt = null;
                await mail.PoDecidedAsync(db, ctx, po, approved: false, why, tk);
                await db.SaveChangesAsync(tk);
                return false;
            }
            var signedBefore = await db.PurchaseOrderSignatures.CountAsync(s => s.PurchaseOrderId == po.Id && s.Round == waiting.Round && s.Step == waiting.Step && s.Approved, tk);
            var done = waiting.Rule == ApprovalRule.Any || signedBefore + 1 >= waiting.ApproverIds.Count;
            if (done)
            {
                waiting.Status = ApprovalStepStatus.Approved; waiting.DoneAt = now;
                var next = steps.FirstOrDefault(s => s.Step > waiting.Step && s.Status == ApprovalStepStatus.Pending);
                if (next is not null)
                {
                    next.Status = ApprovalStepStatus.Waiting;
                    await mail.PoApprovalWaitingAsync(db, ctx, po, next.ApproverIds.Where(a => a != po.SubmittedById).ToList(), next.Name, tk);
                }
                else
                {
                    po.Status = PurchaseOrderStatus.Approved; po.ApprovedAt = now; po.ApprovedById = me; po.ApprovedByName = ctx.UserName;
                    await mail.PoDecidedAsync(db, ctx, po, approved: true, null, tk);
                }
            }
            await db.SaveChangesAsync(tk);
            return true;
        }, ct);
    }

    /// <summary>Stops the current round and starts a new one from today's levels (an approver left, or the levels changed).</summary>
    public static async Task RestartAsync(CmmsDbContext db, ITenantContext ctx, PermissionService perms, AccessService access, EmailOutbox mail, Guid orderId, CancellationToken ct)
    {
        await db.InTransactionAsync<bool>(async tk =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM PurchaseOrders WITH (UPDLOCK, ROWLOCK) WHERE Id = {orderId}", tk);
            db.ChangeTracker.Clear(); // read everything again now that we hold the order: another signature may just have been saved
            var po = await db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, tk) ?? throw new BadHttpRequestException("Purchase order not found.", 404);
            var steps = await StepsAsync(db, po, tk);
            if (po.Status != PurchaseOrderStatus.Submitted || steps.Count == 0) throw new DomainException("This order isn't being signed by approval levels.");
            foreach (var s in steps.Where(s => s.Status is ApprovalStepStatus.Waiting or ApprovalStepStatus.Pending)) s.Status = ApprovalStepStatus.Stopped;
            await StartAsync(db, ctx, perms, access, mail, po, tk); // no level applies now: an empty round, today's rule from here on
            await db.SaveChangesAsync(tk);
            return true;
        }, ct);
    }

    /// <summary>The approval panel on the order page (null for orders approved by today's rule), whether a step is waiting, and whether I may sign it.</summary>
    public sealed record Panel(object? View, bool Waiting, bool ICanSign);

    public static async Task<Panel> PanelAsync(CmmsDbContext db, PermissionService perms, AccessService access, PurchaseOrder po, Guid me, CancellationToken ct)
    {
        if (po.ApprovalRound == 0) return new Panel(null, false, false);
        var steps = await db.PurchaseOrderApprovalSteps.AsNoTracking().Where(s => s.PurchaseOrderId == po.Id).OrderBy(s => s.Round).ThenBy(s => s.Step).ToListAsync(ct);
        if (steps.Count == 0) return new Panel(null, false, false);
        var signatures = await db.PurchaseOrderSignatures.AsNoTracking().Where(s => s.PurchaseOrderId == po.Id).ToListAsync(ct);
        var ids = steps.SelectMany(s => s.ApproverIds).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var current = steps.Where(s => s.Round == po.ApprovalRound).ToList();
        var waiting = current.FirstOrDefault(s => s.Status == ApprovalStepStatus.Waiting);
        var able = waiting is null ? [] : await CanSignNowAsync(db, perms, access, po, waiting, ct);
        object Step(PurchaseOrderApprovalStep s) => new
        {
            s.Step, s.Name, s.MinAmount, s.Rule, s.Status, s.DoneAt,
            Approvers = s.ApproverIds.Select(a =>
            {
                var sig = signatures.FirstOrDefault(x => x.Round == s.Round && x.Step == s.Step && x.UserId == a);
                return new { Id = a, Name = names.GetValueOrDefault(a) ?? sig?.UserName, Signed = sig is not null, Approved = sig?.Approved, At = sig?.At, Comment = sig?.Comment,
                    CanSign = s == waiting && able.Contains(a) };
            }),
        };
        var isWaiting = waiting is not null && po.Status == PurchaseOrderStatus.Submitted;
        var view = new
        {
            Round = po.ApprovalRound,
            Steps = current.Select(Step).ToList(),
            EarlierRounds = steps.Where(s => s.Round < po.ApprovalRound).GroupBy(s => s.Round).OrderByDescending(g => g.Key)
                .Select(g => new { Round = g.Key, Steps = g.Select(Step).ToList() }).ToList(),
            NobodyCanSign = isWaiting && await StuckAsync(db, perms, access, po, waiting!, ct),
            Waiting = isWaiting,
        };
        return new Panel(view, isWaiting, isWaiting && able.Contains(me));
    }
}
