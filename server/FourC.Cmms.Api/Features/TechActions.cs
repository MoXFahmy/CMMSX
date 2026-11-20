using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Changes queued on a technician's phone. Each is applied with the same rules as the full app, at most once per id, and
/// answered "done" or "rejected" with the reason; only unexpected server errors answer 5xx (the phone then retries).
/// </summary>
public static partial class TechEndpoints
{
    /// <param name="UserId">Who queued the change on the phone. When someone else is signed in now, it is neither applied nor recorded.</param>
    public sealed record ActionInput(Guid ClientId, DateTimeOffset? At, string? Type, Guid? WorkOrderId, JsonElement Payload, Guid? UserId = null);
    public sealed record Answer(string Status, string? Message = null, string? Code = null);

    private sealed record TaskPayload(Guid TaskId, bool? IsDone, string? Reading, string? Remarks, bool Force = false);
    private sealed record LaborPayload(DateOnly? WorkDate, decimal Hours, decimal OvertimeHours, string? Notes);
    private sealed record CommentPayload(string? Body);
    private sealed record ReadingPayload(Guid MeterId, decimal Value, string? Note, bool Confirm = false);
    private sealed record StatusPayload(WorkOrderStatus Status, HoldReason? HoldReason, string? Note, string? CompletionNotes, string? CauseNotes, Guid? FailureCodeId,
        bool? AssetWasDown, decimal? DowntimeHours);
    private sealed record RequestPayload(string? Title, string? Description, WorkPriority? Priority, Guid? AssetId, Guid? LocationId, Guid? SiteId);

    /// <summary>A change that can't be applied; rolls back anything it did and is answered "rejected".</summary>
    private sealed class Rejected(string message, string? code = null) : Exception(message) { public string? Code { get; } = code; }

    private static readonly HashSet<string> ImageTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan OldestChange = TimeSpan.FromDays(30);

    /// <summary>409 "otherUser": the change was queued by another person; the phone keeps it until they sign in again.</summary>
    private static IResult? OtherUser(Guid? queuedBy, ITenantContext ctx) =>
        queuedBy is { } q && q != ctx.UserId
            ? Results.Json(new Answer("otherUser", "This change was made by someone else on this phone. It waits until they sign in again.", "otherUser"), statusCode: 409)
            : null;

    /// <summary>When the technician made the change, never later than now and never more than 30 days back.</summary>
    internal static DateTimeOffset Clamp(DateTimeOffset? at, DateTimeOffset now) =>
        at is not { } a || a > now ? now : a < now - OldestChange ? now - OldestChange : a;

    public static void MapTechActions(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tech").WithTags("Technician app").RequireAuthorization();

        g.MapPost("/actions", async (ActionInput input, CmmsDbContext db, ITenantContext ctx, PermissionService perms, EmailOutbox mail, IOptions<JsonOptions> json,
            CancellationToken ct) =>
        {
            if (input.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(input.Type)) return Results.Problem(title: "A change needs an id and a type.", statusCode: 400);
            if (OtherUser(input.UserId, ctx) is { } other) return other;
            var at = Clamp(input.At, DateTimeOffset.UtcNow);
            var allowed = await perms.GetAsync(ctx.UserId!.Value);
            return Results.Ok(await RunAsync(db, ctx, input.ClientId, input.Type!, at, () => ApplyAsync(db, ctx, mail, allowed, json.Value.SerializerOptions, input, at, ct), ct));
        });

        g.MapPost("/files", async (HttpRequest req, CmmsDbContext db, ITenantContext ctx, PermissionService perms, IFileStorage storage, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.Problem(title: "Upload must be multipart/form-data.", statusCode: 400);
            var form = await req.ReadFormAsync(ct);
            if (!Guid.TryParse(form["clientId"], out var clientId) || clientId == Guid.Empty) return Results.Problem(title: "A file needs an id.", statusCode: 400);
            if (OtherUser(Guid.TryParse(form["userId"], out var by) ? by : null, ctx) is { } other) return other;
            var at = Clamp(DateTimeOffset.TryParse(form["at"], out var a) ? a : null, DateTimeOffset.UtcNow);
            var signature = form["kind"].ToString() == "signature";
            var allowed = await perms.GetAsync(ctx.UserId!.Value);
            return Results.Ok(await RunAsync(db, ctx, clientId, signature ? "signature.add" : "photo.add", at, async () =>
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, Guid.TryParse(form["workOrderId"], out var wid) ? wid : null, ct);
                var file = form.Files.GetFile("file") ?? throw new Rejected("The file is missing.");
                if (!ImageTypes.Contains(AttachmentEndpoints.ContentTypeOf(file.FileName, file.ContentType)))
                    throw new Rejected($"{file.FileName} isn't a photo (JPEG, PNG or WebP).");
                if (file.Length is 0 or > MaxImageBytes) throw new Rejected($"{file.FileName} is larger than 10 MB.");
                var att = await AttachmentEndpoints.SaveAttachmentAsync(db, storage, "WorkOrder", w.Id, file, ct);
                if (signature)
                {
                    var by = form["signedBy"].ToString().Clean() ?? ctx.UserName;
                    att.IsSignature = true;
                    att.SignedBy = by is { Length: > 200 } ? by[..200] : by;
                    att.Description = $"Signature — {att.SignedBy}";
                }
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }, ct));
        }).DisableAntiforgery();
    }

    /// <summary>
    /// Applies a change once per (user, client id): a repeat gets the stored answer. The change and its record commit together;
    /// a rejection rolls the change back and is recorded on its own, so a retry gets the same answer.
    /// </summary>
    private static async Task<Answer> RunAsync(CmmsDbContext db, ITenantContext ctx, Guid clientId, string type, DateTimeOffset at, Func<Task<Answer>> apply, CancellationToken ct)
    {
        var userId = ctx.UserId!.Value;
        if (await PriorAsync(db, userId, clientId, ct) is { } prior) return prior;
        Answer answer;
        try
        {
            answer = await db.InTransactionAsync(async tk =>
            {
                var result = await apply();
                db.TechActions.Add(Record(userId, clientId, type, at, result));
                await db.SaveChangesAsync(tk);
                return result;
            }, ct);
            return answer;
        }
        catch (Exception ex) when (ex is Rejected or DomainException or BadHttpRequestException)
        {
            db.ChangeTracker.Clear();
            answer = new Answer("rejected", ex.Message, (ex as Rejected)?.Code);
        }
        catch (DbUpdateException)
        {
            // The same change arrived twice at the same moment: the other copy was applied.
            db.ChangeTracker.Clear();
            return await PriorAsync(db, userId, clientId, ct) ?? throw new InvalidOperationException("Change neither applied nor recorded.");
        }
        try
        {
            db.TechActions.Add(Record(userId, clientId, type, at, answer));
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return await PriorAsync(db, userId, clientId, ct) ?? answer; }
        return answer;
    }

    private static async Task<Answer?> PriorAsync(CmmsDbContext db, Guid userId, Guid clientId, CancellationToken ct) =>
        await db.TechActions.AsNoTracking().Where(a => a.UserId == userId && a.ClientId == clientId).Select(a => new Answer(a.Status, a.Message, a.Code)).FirstOrDefaultAsync(ct);

    private static TechAction Record(Guid userId, Guid clientId, string type, DateTimeOffset at, Answer a) => new()
    {
        UserId = userId, ClientId = clientId, Type = type.Length > 40 ? type[..40] : type, Status = a.Status, Code = a.Code,
        Message = a.Message is { Length: > 2000 } m ? m[..2000] : a.Message, At = at, ReceivedAt = DateTimeOffset.UtcNow,
    };

    private static void Require(HashSet<string> allowed, string permission)
    {
        if (!allowed.Contains(permission)) throw new Rejected("You don't have permission to do this.");
    }

    private static async Task<WorkOrder> WorkOrderAsync(CmmsDbContext db, Guid? id, CancellationToken ct) =>
        (id is { } wid ? await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == wid, ct) : null) ?? throw new Rejected("This work order no longer exists.");

    private static T Read<T>(JsonElement payload, JsonSerializerOptions options)
    {
        try { return payload.Deserialize<T>(options) ?? throw new Rejected("This change is empty."); }
        catch (JsonException) { throw new Rejected("This change couldn't be read."); }
    }

    private static void Ensure(Validator v)
    {
        if (!v.IsValid) throw new Rejected(string.Join(" ", v.Messages));
    }

    private static async Task<Answer> ApplyAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, HashSet<string> allowed, JsonSerializerOptions options,
        ActionInput input, DateTimeOffset at, CancellationToken ct)
    {
        switch (input.Type)
        {
            case "task.update":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var p = Read<TaskPayload>(input.Payload, options);
                var t = await db.WorkOrderTasks.FirstOrDefaultAsync(x => x.Id == p.TaskId && x.WorkOrderId == w.Id, ct) ?? throw new Rejected("This checklist step was removed.");
                if (!p.Force && t.UpdatedAt is { } changed && changed > at && t.UpdatedById != ctx.UserId)
                    throw new Rejected($"Changed by {t.UpdatedByName ?? "someone else"} at {await LocalTimeAsync(db, changed, ct)}.", "conflict");
                if (p.Reading is { Length: > 100 }) throw new Rejected("Reading can be at most 100 characters.");
                if (p.Remarks is { Length: > 2000 }) throw new Rejected("Remarks can be at most 2000 characters.");
                await WorkOrderEndpoints.ApplyTaskChangeAsync(db, ctx, w.Id, t, p.IsDone, p.Reading, p.Remarks, at, ct);
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "labor.add":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var p = Read<LaborPayload>(input.Payload, options);
                var me = await EmployeeAsync(db, ctx, ct) ?? throw new Rejected("Your account isn't linked to an employee.");
                var (v, _) = await WorkOrderEndpoints.AddLaborAsync(db, ctx, w.Id,
                    new WorkOrderEndpoints.LaborInput(me.EmployeeId, p.WorkDate ?? DateOnly.FromDateTime(at.UtcDateTime), p.Hours, p.OvertimeHours, p.Notes), ct, at);
                Ensure(v);
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "comment.add":
            {
                Require(allowed, Permissions.WorkOrdersView);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                Ensure(WorkOrderEndpoints.AddComment(db, ctx, w.Id, Read<CommentPayload>(input.Payload, options).Body, at));
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "reading.add":
            {
                Require(allowed, Permissions.MetersRecord);
                var p = Read<ReadingPayload>(input.Payload, options);
                var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == p.MeterId, ct) ?? throw new Rejected("This meter no longer exists.");
                var (v, _) = await PmEndpoints.RecordReadingAsync(db, ctx, mail, meter, new PmEndpoints.ReadingInput(p.Value, at, p.Note, p.Confirm), ct);
                Ensure(v);
                return new Answer("done");
            }
            case "status.change":
                return await ChangeStatusAsync(db, ctx, mail, allowed, Read<StatusPayload>(input.Payload, options), input.WorkOrderId, at, ct);
            case "request.create":
            {
                Require(allowed, Permissions.RequestsCreate);
                var p = Read<RequestPayload>(input.Payload, options);
                var site = p.SiteId ?? (p.AssetId is { } aid ? await db.Assets.Where(x => x.Id == aid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? (p.LocationId is { } lid ? await db.Locations.Where(x => x.Id == lid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? throw new Rejected("Choose where the problem is.");
                var (v, _) = await WorkRequestEndpoints.CreateAsync(db, ctx, mail, new WorkRequestEndpoints.RequestInput(p.Title ?? "", p.Description, site, p.LocationId, p.AssetId,
                    p.Priority ?? WorkPriority.Medium, WorkType.Corrective, null, null, null, null), ct, raisedAt: at);
                Ensure(v);
                return new Answer("done");
            }
            default:
                throw new Rejected($"This app version sent a change the server doesn't know ({input.Type}). Update the app.");
        }
    }

    private static void RequireNotClosed(WorkOrder w)
    {
        if (w.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
            throw new Rejected($"This work order is {(w.Status == WorkOrderStatus.Closed ? "closed" : "cancelled")}, so it can't be changed.");
    }

    private static async Task<Answer> ChangeStatusAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, HashSet<string> allowed, StatusPayload p, Guid? workOrderId,
        DateTimeOffset at, CancellationToken ct)
    {
        Require(allowed, Permissions.WorkOrdersExecute);
        if (p.Status is not (WorkOrderStatus.InProgress or WorkOrderStatus.OnHold or WorkOrderStatus.Completed)) throw new Rejected("The phone can start, hold or complete work only.");
        var w = await WorkOrderAsync(db, workOrderId, ct);
        if (p.Status == WorkOrderStatus.Completed && w.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed)
        {
            // Two technicians completed it offline: keep what the second one wrote instead of making them type it again.
            if (p.CompletionNotes.Clean() is not { } notes) return new Answer("done", "This job was already completed.");
            Ensure(WorkOrderEndpoints.AddComment(db, ctx, w.Id, $"Completion notes from {ctx.UserName}: {notes}", at));
            await db.SaveChangesAsync(ct);
            return new Answer("done", "This job was already completed; your notes were added as a comment.");
        }
        if (p.Status is WorkOrderStatus.InProgress or WorkOrderStatus.OnHold && w.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed)
            throw new Rejected($"This work order was already {Words(w.Status)}, so it wasn't reopened. Ask the office if it needs more work.");
        if (w.Status == p.Status)
        {
            // Already so (another phone or the office did it): keep the note the technician typed instead of dropping it.
            if (p.Status == WorkOrderStatus.OnHold && p.Note.Clean() is { } note)
            {
                var why = p.HoldReason is { } hr ? $" ({Words(hr)})" : "";
                Ensure(WorkOrderEndpoints.AddComment(db, ctx, w.Id, $"Hold note from {ctx.UserName}{why}: {note}", at));
                await db.SaveChangesAsync(ct);
            }
            return new Answer("done");
        }
        if (WorkOrderWorkflow.RequiresClosePermission(w.Status, p.Status) && !allowed.Contains(Permissions.WorkOrdersClose))
            throw new Rejected("You can't reopen closed or cancelled work orders.");
        if (!WorkOrderWorkflow.CanTransition(w.Status, p.Status))
            throw new Rejected($"This work order is now {Words(w.Status)}, so it can't be {Words(p.Status)}.");
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var completed = p.Status == WorkOrderStatus.Completed;
        var input = new WorkOrderEndpoints.StatusInput(p.Status, p.HoldReason, p.Note, p.CompletionNotes, p.CauseNotes, p.FailureCodeId, p.AssetWasDown, p.DowntimeHours,
            // When the technician started (offline) or, on completion, never later than when they finished.
            ActualStart: p.Status == WorkOrderStatus.InProgress || (completed && w.StartedAt > at) ? at : null, ActualEnd: completed ? at : null, At: at);
        Ensure(await WorkOrderEndpoints.ChangeStatusAsync(db, ctx, mail, settings, w, input, ct));
        await db.SaveChangesAsync(ct);
        return new Answer("done");
    }

    private static string Words(HoldReason r) => System.Text.RegularExpressions.Regex.Replace(r.ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

    private static string Words(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.InProgress => "in progress", WorkOrderStatus.OnHold => "on hold", _ => s.ToString().ToLowerInvariant(),
    };

    private static async Task<string> LocalTimeAsync(CmmsDbContext db, DateTimeOffset at, CancellationToken ct)
    {
        var zone = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.TimeZone;
        try { return TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString("d MMM HH:mm"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return at.ToString("d MMM HH:mm") + " UTC"; }
    }
}
