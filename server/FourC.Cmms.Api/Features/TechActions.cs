using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
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

    private sealed record TaskPayload(Guid TaskId, bool? IsDone, string? Reading, string? Remarks, bool Force = false, StepAnswer? Answer = null, decimal? Value = null,
        string? Text = null, Guid? MeterId = null);
    /// <summary>EmployeeId (0.11.0): a crew leader logging another technician's hours (needs "Log hours for others").</summary>
    private sealed record LaborPayload(DateOnly? WorkDate, decimal Hours, decimal OvertimeHours, string? Notes, Guid? EmployeeId = null);
    private sealed record CommentPayload(string? Body);
    private sealed record ReadingPayload(Guid MeterId, decimal Value, string? Note, bool Confirm = false);
    private sealed record StatusPayload(WorkOrderStatus Status, HoldReason? HoldReason, string? Note, string? CompletionNotes, string? CauseNotes, Guid? FailureCodeId,
        bool? AssetWasDown, decimal? DowntimeHours, Guid? CancelReasonId = null, Guid? RemedyId = null, Guid? SymptomId = null);
    /// <summary>AssetIsDown: "The asset is down" (0.8.1); older app versions don't send it.</summary>
    private sealed record RequestPayload(string? Title, string? Description, WorkPriority? Priority, Guid? AssetId, Guid? LocationId, Guid? SiteId, bool? AssetIsDown = null,
        Guid? RequestTypeId = null, Guid? SymptomId = null);

    /// <summary>A part used on, returned from or issued to a job (0.11.0); a planned line's store is used when no store is given.</summary>
    private sealed record PartPayload(Guid PartId, Guid? StoreId, decimal Quantity, Guid? LineId = null, Guid? WorkOrderId = null);
    /// <summary>A delivery received against a purchase order line on the phone (0.11.0).</summary>
    private sealed record ReceivePayload(Guid PurchaseOrderId, Guid LineId, decimal Quantity, Guid? StoreId = null, string? BatchNumber = null, DateOnly? ExpiryDate = null);
    /// <summary>A serial unit fitted to one of the job's assets (0.24.0).</summary>
    private sealed record UnitFitPayload(Guid UnitId, Guid AssetId);
    /// <summary>A fitted unit taken off on the job (0.24.0): Store (the job's site's default store), Repair or Scrap.</summary>
    private sealed record UnitRemovePayload(Guid UnitId, string? Outcome, string? Note);
    /// <summary>A counted quantity for a stock count (0.11.0).</summary>
    private sealed record CountPayload(Guid CountId, Guid PartId, decimal Quantity);
    /// <summary>The asset's stop started or ended from a job (0.11.0); At = the time the technician chose, else when the change was made.</summary>
    private sealed record StopPayload(Guid? AssetId, DateTimeOffset? At, string? Reason);
    /// <summary>A PM task's steps added to a job's checklist (0.11.0).</summary>
    private sealed record PmTasksPayload(Guid PmTaskId);
    /// <summary>A work order raised on the phone (0.11.0), by someone who may create work orders; assigned to them unless AssignToMe is false.</summary>
    private sealed record WorkOrderPayload(string? Title, string? Description, WorkPriority? Priority, WorkType? Type, Guid? SiteId, Guid? LocationId, Guid? AssetId,
        bool? AssignToMe = null, Guid? SymptomId = null);
    /// <summary>A change that can't be applied; rolls back anything it did and is answered "rejected".</summary>
    private sealed class Rejected(string message, string? code = null) : Exception(message) { public string? Code { get; } = code; }

    private static readonly HashSet<string> ImageTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxImageBytes = 10 * 1024 * 1024;
    /// <summary>Videos and documents a technician can attach from the phone (0.11.0), by the type their file name gives.</summary>
    private static readonly HashSet<string> FileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/quicktime", "video/webm", "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/plain", "text/csv",
    };
    private const long MaxFileBytes = 50 * 1024 * 1024;
    private static readonly TimeSpan OldestChange = TimeSpan.FromDays(30);

    /// <summary>409 "otherUser": the change was queued by another person; the phone keeps it until they sign in again.</summary>
    private static IResult? OtherUser(Guid? queuedBy, ITenantContext ctx) =>
        queuedBy is { } q && q != ctx.UserId
            ? Results.Json(new Answer("otherUser", Translator.T("This change was made by someone else on this phone. It waits until they sign in again."), "otherUser"), statusCode: 409)
            : null;

    /// <summary>When the technician made the change, never later than now and never more than 30 days back.</summary>
    internal static DateTimeOffset Clamp(DateTimeOffset? at, DateTimeOffset now) =>
        at is not { } a || a > now ? now : a < now - OldestChange ? now - OldestChange : a;

    public static void MapTechActions(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tech").WithTags("Technician app").RequireAuthorization();

        g.MapPost("/actions", async (ActionInput input, CmmsDbContext db, ITenantContext ctx, PermissionService perms, EmailOutbox mail, AccessService access,
            IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            if (input.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(input.Type)) return Results.Problem(title: Translator.T("A change needs an id and a type."), statusCode: 400);
            if (OtherUser(input.UserId, ctx) is { } other) return other;
            var at = Clamp(input.At, DateTimeOffset.UtcNow);
            var allowed = await perms.GetAsync(ctx.UserId!.Value);
            return Results.Ok(await RunAsync(db, ctx, input.ClientId, input.Type!, at, () => ApplyAsync(db, ctx, mail, access, allowed, json.Value.SerializerOptions, input, at, ct), ct));
        });

        g.MapPost("/files", async (HttpRequest req, CmmsDbContext db, ITenantContext ctx, PermissionService perms, IFileStorage storage, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.Problem(title: Translator.T("Upload must be multipart/form-data."), statusCode: 400);
            var form = await req.ReadFormAsync(ct);
            if (!Guid.TryParse(form["clientId"], out var clientId) || clientId == Guid.Empty) return Results.Problem(title: Translator.T("A file needs an id."), statusCode: 400);
            if (OtherUser(Guid.TryParse(form["userId"], out var by) ? by : null, ctx) is { } other) return other;
            var at = Clamp(DateTimeOffset.TryParse(form["at"], out var a) ? a : null, DateTimeOffset.UtcNow);
            var kind = form["kind"].ToString();
            var signature = kind == "signature";
            var document = kind == "file"; // a video or document (0.11.0)
            var allowed = await perms.GetAsync(ctx.UserId!.Value);
            return Results.Ok(await RunAsync(db, ctx, clientId, signature ? "signature.add" : document ? "file.add" : "photo.add", at, async () =>
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, Guid.TryParse(form["workOrderId"], out var wid) ? wid : null, ct);
                var file = form.Files.GetFile("file") ?? throw new Rejected("The file is missing.");
                var type = AttachmentEndpoints.ContentTypeOf(file.FileName, file.ContentType);
                if (file.Length == 0) throw new Rejected($"{file.FileName} is empty.");
                if (document)
                {
                    if (!FileTypes.Contains(type)) throw new Rejected($"{file.FileName} isn't a video or document the app can attach (MP4, MOV, WebM, PDF, Word, Excel, text).");
                    if (file.Length > MaxFileBytes) throw new Rejected($"{file.FileName} is larger than 50 MB.");
                }
                else
                {
                    if (!ImageTypes.Contains(type)) throw new Rejected($"{file.FileName} isn't a photo (JPEG, PNG or WebP).");
                    if (file.Length > MaxImageBytes) throw new Rejected($"{file.FileName} is larger than 10 MB.");
                }
                var att = await AttachmentEndpoints.SaveAttachmentAsync(db, storage, "WorkOrder", w.Id, file, ct);
                // A photo taken on a checklist step (0.13.0).
                if (Guid.TryParse(form["taskId"], out var taskId))
                    att.WorkOrderTaskId = await db.WorkOrderTasks.AnyAsync(t => t.Id == taskId && t.WorkOrderId == w.Id, ct) ? taskId : throw new Rejected("This checklist step was removed.");
                if (signature)
                {
                    var by = form["signedBy"].ToString().Clean() ?? ctx.UserName;
                    att.IsSignature = true;
                    att.SignedBy = by is { Length: > 200 } ? by[..200] : by;
                    att.Description = $"Signature — {att.SignedBy}";
                }
                await Housekeeping.SaveOrRemoveFileAsync(storage, att.StorageKey, () => db.SaveChangesAsync(ct), ct);
                return new Answer("done");
            }, ct));
        }).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxFileBytes + 2 * 1024 * 1024));
    }

    /// <summary>
    /// Applies a change once per (user, client id): a repeat gets the stored answer. The change and its record commit together;
    /// a rejection rolls the change back and is recorded on its own, so a retry gets the same answer.
    /// </summary>
    /// <summary>Runs a change once (see <see cref="RunOnceAsync"/>); the answer is kept in English and said in the phone's language.</summary>
    private static async Task<Answer> RunAsync(CmmsDbContext db, ITenantContext ctx, Guid clientId, string type, DateTimeOffset at, Func<Task<Answer>> apply, CancellationToken ct)
    {
        var answer = await RunOnceAsync(db, ctx, clientId, type, at, apply, ct);
        return answer.Message is null ? answer : answer with { Message = Translator.T(answer.Message) };
    }

    private static async Task<Answer> RunOnceAsync(CmmsDbContext db, ITenantContext ctx, Guid clientId, string type, DateTimeOffset at, Func<Task<Answer>> apply, CancellationToken ct)
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

    /// <summary>The store a part change uses: the one sent, else its planned line's, else the job site's default store.</summary>
    private static async Task<Guid> StoreOfAsync(CmmsDbContext db, WorkOrder w, PartPayload p, CancellationToken ct)
    {
        if (p.StoreId is { } given) return given;
        if (p.LineId is { } lineId && await db.WorkOrderParts.Where(l => l.Id == lineId && l.WorkOrderId == w.Id).Select(l => l.StoreId).FirstOrDefaultAsync(ct) is { } lineStore)
            return lineStore;
        return await db.Sites.AllSites().Where(s => s.Id == w.SiteId).Select(s => s.Settings.DefaultStoreId).FirstOrDefaultAsync(ct) ?? throw new Rejected("Choose the store.");
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

    private static async Task<Answer> ApplyAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, AccessService access, HashSet<string> allowed, JsonSerializerOptions options,
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
                var settings = (await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == db.CurrentTenantId, ct)).Settings;
                Ensure(await Checklists.AnswerAsync(db, ctx, mail, settings, w, t,
                    new Checklists.StepChange(p.IsDone, p.Answer, p.Value, p.Text, p.MeterId, p.Reading, p.Remarks, Confirm: true), at, ct));
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
                var who = me.EmployeeId;
                if (p.EmployeeId is { } other && other != me.EmployeeId)
                {
                    // A crew leader logs another technician's hours (0.11.0).
                    Require(allowed, Permissions.WorkOrdersLogForOthers);
                    if (!await db.Employees.AnyAsync(e => e.Id == other && e.IsActive, ct)) throw new Rejected("Choose an active person.");
                    who = other;
                }
                // Like the changes themselves: hours for the last 30 days (a day of slack for time zones), never for days to come.
                var day = DateOnly.FromDateTime(at.UtcDateTime);
                var workDate = p.WorkDate ?? day;
                if (workDate < day.AddDays(-OldestChange.Days) || workDate > day.AddDays(1))
                    throw new Rejected("The work date must be within the last 30 days.");
                var (v, _) = await WorkOrderEndpoints.AddLaborAsync(db, ctx, w.Id,
                    new WorkOrderEndpoints.LaborInput(who, workDate, p.Hours, p.OvertimeHours, p.Notes), ct, at);
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
            case "part.use":
            case "part.return":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var p = Read<PartPayload>(input.Payload, options);
                var store = await StoreOfAsync(db, w, p, ct);
                if (input.Type == "part.return")
                {
                    Require(allowed, Permissions.InventoryTransact);
                    await PhoneStock.ReturnAsync(db, ctx, w, p.PartId, store, p.Quantity, at, ct);
                    return new Answer("done");
                }
                // Someone who can't take parts from the store asks for them instead (the store keeper issues them).
                var note = allowed.Contains(Permissions.InventoryTransact)
                    ? await PhoneStock.UseAsync(db, ctx, w, p.PartId, store, p.Quantity, at, ct)
                    : await PhoneStock.AskAsync(db, w, p.PartId, store, p.Quantity, ct);
                return new Answer("done", note);
            }
            case "stock.issue":
            {
                Require(allowed, Permissions.InventoryTransact);
                var p = Read<PartPayload>(input.Payload, options);
                var w = await WorkOrderAsync(db, p.WorkOrderId ?? input.WorkOrderId, ct);
                RequireNotClosed(w);
                var store = p.StoreId ?? throw new Rejected("Choose the store.");
                return new Answer("done", await PhoneStock.UseAsync(db, ctx, w, p.PartId, store, p.Quantity, at, ct));
            }
            case "po.receive":
            {
                Require(allowed, Permissions.PurchasingReceive);
                var p = Read<ReceivePayload>(input.Payload, options);
                // Serial parts need a serial number per piece (0.24.0), entered on the web.
                if (await db.PurchaseOrderLines.AnyAsync(l => l.Id == p.LineId && db.Parts.Any(x => x.Id == l.PartId && x.IsSerialTracked), ct))
                    throw new Rejected("Receive serial parts on the web, where their serial numbers are entered.");
                var day = Sla.LocalDay(at, Sla.Zone((await SlaQueries.SettingsAsync(db, ct)).TimeZone));
                await PurchasingEndpoints.ReceiveCoreAsync(db, ctx, mail, p.PurchaseOrderId,
                    new PurchasingEndpoints.ReceiveInput(day, null, [new PurchasingEndpoints.ReceiveLine(p.LineId, p.Quantity, p.StoreId, p.BatchNumber, p.ExpiryDate)]), day, ct);
                return new Answer("done");
            }
            case "unit.fit":
            case "unit.remove":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                Require(allowed, Permissions.InventoryTransact);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var assets = await SerialUnitEndpoints.WorkOrderAssetsAsync(db, w, ct);
                if (input.Type == "unit.fit")
                {
                    var p = Read<UnitFitPayload>(input.Payload, options);
                    if (!assets.Contains(p.AssetId)) throw new Rejected("Choose an asset of this work order.");
                    var target = await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == p.AssetId, ct) ?? throw new Rejected("Choose an asset of this work order.");
                    var unit = await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == p.UnitId && a.PartId != null, ct) ?? throw new Rejected("This unit no longer exists.");
                    await SerialUnits.FitAsync(db, ctx, unit, target, w, countHours: true, null, at, ct);
                }
                else
                {
                    var p = Read<UnitRemovePayload>(input.Payload, options);
                    var outcome = SerialUnitEndpoints.OutcomeOf(p.Outcome) ?? throw new Rejected("Choose what happens to the unit.");
                    var unit = await db.Assets.AllSites().FirstOrDefaultAsync(a => a.Id == p.UnitId && a.PartId != null, ct) ?? throw new Rejected("This unit no longer exists.");
                    if (unit.ParentAssetId is not { } parent || !assets.Contains(parent)) throw new Rejected("This unit isn't fitted to this work order's asset.");
                    Guid? store = null;
                    if (outcome == UnitOutcome.Store)
                        store = (await db.Sites.AllSites().AsNoTracking().Where(x => x.Id == w.SiteId).Select(x => x.Settings).FirstAsync(ct)).DefaultStoreId
                                ?? throw new Rejected("This job's site has no default store.");
                    await SerialUnits.RemoveAsync(db, ctx, unit, w, outcome, store, p.Note, at, ct);
                }
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "count.line":
            {
                Require(allowed, Permissions.InventoryView);
                var p = Read<CountPayload>(input.Payload, options);
                await StockCountEndpoints.CountLineAsync(db, ctx, p.CountId, p.PartId, p.Quantity, at, ct);
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "downtime.start":
            case "downtime.end":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var p = Read<StopPayload>(input.Payload, options);
                var asset = p.AssetId ?? w.AssetId ?? throw new Rejected("Choose the asset.");
                if (!await db.Assets.AnyAsync(a => a.Id == asset, ct)) throw new Rejected("Asset not found.");
                if (p.At > DateTimeOffset.UtcNow.AddMinutes(1)) throw new Rejected("The time can't be in the future.");
                var when = p.At ?? at;
                if (input.Type == "downtime.end")
                {
                    if (!await Downtime.EndOpenAsync(db, ctx, asset, when, ct)) throw new Rejected("The asset isn't down.");
                    await db.SaveChangesAsync(ct);
                    return new Answer("done");
                }
                var already = await db.DowntimeEvents.AnyAsync(e => e.AssetId == asset && e.EndedAt == null, ct);
                var stop = await Downtime.StartOrJoinAsync(db, asset, when, p.Reason ?? w.Title, FourC.Cmms.Core.Assets.DowntimeSource.WorkOrder, w.Number, ct);
                // The job is about this stop: completing it ends the stop (as "The asset is down" on the web).
                if (w.AssetId == asset && w.DowntimeEventId is null) { w.DowntimeEventId = stop.Id; w.AssetWasDown = true; }
                await db.SaveChangesAsync(ct);
                return new Answer("done", already ? "The asset is already down." : null);
            }
            case "tasks.fromPm":
            {
                Require(allowed, Permissions.WorkOrdersExecute);
                var w = await WorkOrderAsync(db, input.WorkOrderId, ct);
                RequireNotClosed(w);
                var p = Read<PmTasksPayload>(input.Payload, options);
                var steps = await db.PmTaskSteps.AsNoTracking().Where(s => s.PmTaskId == p.PmTaskId).OrderBy(s => s.Sequence).ToListAsync(ct);
                if (steps.Count == 0 && !await db.PmTasks.AnyAsync(t => t.Id == p.PmTaskId, ct)) throw new Rejected("This checklist no longer exists.");
                var next = (await db.WorkOrderTasks.Where(t => t.WorkOrderId == w.Id).MaxAsync(t => (int?)t.Sequence, ct) ?? 0) + 1;
                foreach (var step in steps) db.WorkOrderTasks.Add(StepCopy.ToWorkOrder(step, w.Id, next++, null));
                await Checklists.ResolveMetersAsync(db, w, ct);
                await db.SaveChangesAsync(ct);
                return new Answer("done");
            }
            case "workorder.create":
            {
                Require(allowed, Permissions.WorkOrdersCreate);
                var p = Read<WorkOrderPayload>(input.Payload, options);
                var site = p.SiteId ?? (p.AssetId is { } aid ? await db.Assets.Where(x => x.Id == aid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? (p.LocationId is { } lid ? await db.Locations.Where(x => x.Id == lid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? throw new Rejected("Choose where the work is.");
                var me = await EmployeeAsync(db, ctx, ct);
                // A symptom picked offline is accepted even if made inactive since (0.14.0).
                if (await Codes.CheckAsync(db.Symptoms, p.SymptomId, null, Codes.UnknownSymptom, ct, anyState: true) is { } badSymptom) throw new Rejected(badSymptom);
                var woInput = new WorkOrderEndpoints.WorkOrderInput(p.Title ?? "", p.Description, p.Type ?? WorkType.Corrective, p.Priority ?? WorkPriority.Medium, site, p.LocationId,
                    p.AssetId, null, p.AssignToMe != false && me is not null ? [me.EmployeeId] : null, null, null, null, null, SymptomId: p.SymptomId);
                Ensure(await WorkOrderEndpoints.ValidateAsync(woInput, db, ct));
                await WorkOrderEndpoints.CreateAsync(db, ctx, woInput, null, ct, mail: mail, raisedAt: at, note: "Created on the phone");
                return new Answer("done");
            }
            case "request.create":
            {
                Require(allowed, Permissions.RequestsCreate);
                var p = Read<RequestPayload>(input.Payload, options);
                var site = p.SiteId ?? (p.AssetId is { } aid ? await db.Assets.Where(x => x.Id == aid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? (p.LocationId is { } lid ? await db.Locations.Where(x => x.Id == lid).Select(x => (Guid?)x.SiteId).FirstOrDefaultAsync(ct) : null)
                           ?? throw new Rejected("Choose where the problem is.");
                var (v, _) = await WorkRequestEndpoints.CreateAsync(db, ctx, mail, access, new WorkRequestEndpoints.RequestInput(p.Title ?? "", p.Description, site, p.LocationId, p.AssetId,
                    p.Priority ?? WorkPriority.Medium, WorkType.Corrective, null, null, null, null, AssetIsDown: p.AssetIsDown == true, RequestTypeId: p.RequestTypeId, SymptomId: p.SymptomId),
                    ct, raisedAt: at, fromPhone: true);
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
        if (p.Status is not (WorkOrderStatus.InProgress or WorkOrderStatus.OnHold or WorkOrderStatus.Completed or WorkOrderStatus.Cancelled))
            throw new Rejected("The phone can start, hold, complete or cancel work only.");
        // Cancelling (0.11.0): as on the web — the close permission, and the reason the organization asks for.
        if (p.Status == WorkOrderStatus.Cancelled && !allowed.Contains(Permissions.WorkOrdersClose)) throw new Rejected("You can't cancel work orders.");
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
            ActualStart: p.Status == WorkOrderStatus.InProgress || (completed && w.StartedAt > at) ? at : null, ActualEnd: completed ? at : null, At: at,
            CancelReasonId: p.CancelReasonId, RemedyId: p.RemedyId, SymptomId: p.SymptomId);
        Ensure(await WorkOrderEndpoints.ChangeStatusAsync(db, ctx, mail, settings, w, input, ct, fromPhone: true));
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
