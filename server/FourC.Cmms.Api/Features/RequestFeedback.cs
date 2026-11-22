using System.Security.Cryptography;
using System.Text;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>The requester's answer to finished work: accept (closes the request) or refuse (reopens the work order).</summary>
public sealed record FeedbackInput(bool Accepted, int? Rating, string? Comment);

/// <summary>Asked when the work is completed; <see cref="Token"/> is the single-use link's token for a requester without a login.</summary>
public sealed record FeedbackAsk(string? Token);

public static class RequestFeedbackService
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(30);
    public const string LinkGone = "This link was already used or has expired.";

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>
    /// The work order was completed: if it came from an approved request, the requester is asked to accept or refuse it. A requester who
    /// isn't the request's creator with a login gets a new single-use link (any earlier one stops working). Null when nobody is asked.
    /// </summary>
    public static async Task<FeedbackAsk?> AskAsync(CmmsDbContext db, WorkOrder w, DateTimeOffset now, CancellationToken ct)
    {
        if (w.WorkRequestId is not { } rid) return null;
        var r = await db.WorkRequests.AllSites().FirstOrDefaultAsync(x => x.Id == rid, ct);
        if (r is null || r.Status != WorkRequestStatus.Approved) return null;
        var inApp = await AnswersInAppAsync(db, r, ct);
        // Nobody can answer (no contact address and nobody who raised it in CMX, e.g. imported): don't wait for an answer that can't come.
        if (!inApp && string.IsNullOrWhiteSpace(r.RequesterEmail)) return null;
        r.AwaitingFeedbackSince = now;
        r.FeedbackTokenHash = null; r.FeedbackTokenExpiresAt = null;
        if (inApp) return new FeedbackAsk(null);
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        r.FeedbackTokenHash = Hash(token);
        r.FeedbackTokenExpiresAt = now + LinkLifetime;
        return new FeedbackAsk(token);
    }

    /// <summary>The requester is the request's creator, signed in with that address: they answer on the request page.</summary>
    public static async Task<bool> AnswersInAppAsync(CmmsDbContext db, WorkRequest r, CancellationToken ct)
    {
        if (r.CreatedById is not { } creator) return false;
        var email = await db.Users.AsNoTracking().Where(u => u.Id == creator && u.IsActive).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return email is not null && (r.RequesterEmail is null || string.Equals(email, r.RequesterEmail, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <paramref name="userId"/> is the request's requester: they raised it and it is their own contact address. Someone who logged it for a
    /// caller (a typed e-mail) is maintenance's side: they don't answer for the caller, and their comments go to the caller.
    /// </summary>
    public static async Task<bool> IsRequesterAsync(CmmsDbContext db, WorkRequest r, Guid? userId, CancellationToken ct) =>
        userId is { } u && r.CreatedById == u && await AnswersInAppAsync(db, r, ct);

    /// <summary>Maintenance reopened or cancelled the work: nothing is waiting for the requester any more, and the link stops working.</summary>
    public static async Task EndWaitAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)
    {
        if (w.WorkRequestId is not { } rid) return;
        if (await db.WorkRequests.AllSites().FirstOrDefaultAsync(x => x.Id == rid, ct) is { AwaitingFeedbackSince: not null } r) EndWait(r);
    }

    public static void EndWait(WorkRequest r)
    {
        r.AwaitingFeedbackSince = null; r.FeedbackTokenHash = null; r.FeedbackTokenExpiresAt = null;
    }

    /// <summary>Records the answer. Accept closes the request; refuse reopens the work order (as the system, for the requester). The caller saves.</summary>
    public static async Task<Validator> AnswerAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, WorkRequest r, FeedbackInput input, bool viaLink, string? byName,
        CancellationToken ct)
    {
        if (r.AwaitingFeedbackSince is null || r.WorkOrderId is not { } woId) throw new DomainException("Nothing is waiting for your answer.");
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var comment = input.Comment.Clean();
        var v = new Validator().MaxLength("comment", comment, "Comment", 2000)
            .When(input.Accepted && input.Rating is null, "rating", "Give a rating from 1 to 5.")
            .When(input.Rating is < 1 or > 5, "rating", "Give a rating from 1 to 5.")
            .When(!input.Accepted && comment is null && (settings.CommentRequiredFor ?? []).Contains(CommentActions.Refuse), "comment", "Say why the work isn't done.");
        if (!v.IsValid) return v;

        var w = await db.WorkOrders.AllSites().FirstAsync(x => x.Id == woId, ct);
        var now = DateTimeOffset.UtcNow;
        db.RequestFeedback.Add(new RequestFeedback
        {
            WorkRequestId = r.Id, WorkOrderId = w.Id, Accepted = input.Accepted, Rating = input.Rating, Comment = comment, AnsweredAt = now,
            UserId = viaLink ? null : ctx.UserId, ByName = byName is { Length: > 200 } n ? n[..200] : byName, ViaLink = viaLink,
        });
        EndWait(r);
        if (input.Accepted)
        {
            r.Status = WorkRequestStatus.Closed;
            return v;
        }
        if (w.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed)
        {
            var note = comment is null ? $"Refused by {byName}" : $"Refused by {byName}: {comment}";
            var reopened = await WorkOrderEndpoints.ChangeStatusAsync(db, ctx, mail, settings, w,
                new WorkOrderEndpoints.StatusInput(WorkOrderStatus.InProgress, null, note.Length > 2000 ? note[..2000] : note, null, null, null, null, null), ct);
            if (!reopened.IsValid) return reopened;
        }
        await mail.RequestRefusedAsync(db, ctx, r, w, byName, comment, ct);
        return v;
    }

    public static void MapRequestFeedbackEndpoints(this RouteGroupBuilder api)
    {
        // The signed-in requester (the request's creator) answers on the request page.
        api.MapPost("/work-requests/{id:guid}/feedback", async (Guid id, FeedbackInput input, CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, CancellationToken ct) =>
        {
            var r = await db.WorkRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (!await IsRequesterAsync(db, r, ctx.UserId, ct)) return Results.Forbid();
            var v = await db.InTransactionAsync(async token =>
            {
                var result = await AnswerAsync(db, ctx, mail, r, input, viaLink: false, ctx.UserName, token);
                if (result.IsValid) await db.SaveChangesAsync(token);
                return result;
            }, ct);
            return v.IsValid ? Results.Ok(new { r.Id, r.Status }) : v.Problem();
        }).WithTags("Work requests").RequireAuthorization();

        // Anyone else answers through the single-use link e-mailed to them: no sign-in, and only this one request.
        var pub = api.MapGroup("/public/feedback").WithTags("Requester answer").AllowAnonymous();

        pub.MapGet("/{token}", async (string token, CmmsDbContext db, DbContextOptionsFactory factory, CancellationToken ct) =>
        {
            if (await FindAsync(db, token, ct) is not { } found) return Gone();
            await using var scoped = factory.For(found.TenantId, null);
            var r = await scoped.WorkRequests.AsNoTracking().FirstAsync(x => x.Id == found.Id, ct);
            var tenant = await scoped.Tenants.AsNoTracking().FirstAsync(t => t.Id == found.TenantId, ct);
            var w = await scoped.WorkOrders.AsNoTracking().Where(x => x.Id == r.WorkOrderId)
                .Select(x => new { x.Number, x.CompletedAt, x.CompletionNotes }).FirstOrDefaultAsync(ct);
            var site = await scoped.Sites.AsNoTracking().Where(s => s.Id == r.SiteId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            var location = r.LocationId is { } l ? await scoped.Locations.AsNoTracking().Where(x => x.Id == l).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
            var asset = r.AssetId is { } a ? await scoped.Assets.AsNoTracking().Where(x => x.Id == a).Select(x => x.AssetNumber + " · " + x.Name).FirstOrDefaultAsync(ct) : null;
            return Results.Ok(new
            {
                Organization = tenant.Name, Language = tenant.Settings.DefaultLanguage, r.Number, r.Title, r.RequesterName,
                Place = string.Join(" › ", new[] { site, location, asset }.Where(x => !string.IsNullOrWhiteSpace(x))),
                WorkOrderNumber = w?.Number, w?.CompletedAt, w?.CompletionNotes,
                RefuseNeedsComment = (tenant.Settings.CommentRequiredFor ?? []).Contains(CommentActions.Refuse),
            });
        });

        pub.MapPost("/{token}", async (string token, FeedbackInput input, CmmsDbContext db, DbContextOptionsFactory factory, EmailOutbox mail, CancellationToken ct) =>
        {
            if (await FindAsync(db, token, ct) is not { } found) return Gone();
            var name = found.RequesterName ?? found.RequesterEmail;
            var ctx = new FixedTenantContext(found.TenantId, null, name);
            await using var scoped = factory.For(found.TenantId, name);
            var v = await scoped.InTransactionAsync(async tk =>
            {
                var r = await scoped.WorkRequests.FirstAsync(x => x.Id == found.Id, tk);
                if (r.FeedbackTokenHash != Hash(token)) return null; // answered a moment ago
                var result = await AnswerAsync(scoped, ctx, mail, r, input, viaLink: true, name, tk);
                if (result.IsValid) await scoped.SaveChangesAsync(tk);
                return result;
            }, ct);
            if (v is null) return Gone();
            return v.IsValid ? Results.Ok(new { ok = true }) : v.Problem();
        });
    }

    private sealed record Found(Guid Id, Guid TenantId, string? RequesterName, string? RequesterEmail);

    /// <summary>The open request this token belongs to (any organization), or null when unknown, used or expired.</summary>
    private static async Task<Found?> FindAsync(CmmsDbContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var now = DateTimeOffset.UtcNow;
        return await db.WorkRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.FeedbackTokenHash == hash && !r.IsDeleted && r.AwaitingFeedbackSince != null && r.FeedbackTokenExpiresAt > now)
            .Select(r => new Found(r.Id, r.TenantId, r.RequesterName, r.RequesterEmail)).FirstOrDefaultAsync(ct);
    }

    private static IResult Gone() => Results.NotFound(new { detail = Translator.T(LinkGone) });
}

/// <summary>Opens a database context for one organization outside a signed-in request (the requester's answer page).</summary>
public sealed class DbContextOptionsFactory(DbContextOptions<CmmsDbContext> options)
{
    public CmmsDbContext For(Guid tenantId, string? userName) => new(options, new FixedTenantContext(tenantId, null, userName));
}
