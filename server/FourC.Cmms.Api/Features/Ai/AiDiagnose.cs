using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Ai;

public sealed record SourceRef(Guid Id, string Number, string Title);
public sealed record DiagnosisAction(string Title, string Why, List<PartSuggestion> Parts, decimal? Hours, List<SourceRef> Sources);

/// <summary>A diagnosis as kept: texts, actions with checked parts and source work orders.</summary>
public sealed record DiagnosisAnswer(string Context, string Observed, string? ProbableCause, string? Confidence, List<DiagnosisAction> Actions, bool HistoryTooThin, string? Message);

/// <summary>A diagnosis as a page shows it. <see cref="Id"/> is empty for a thin-history answer the server gave itself (not kept).</summary>
public sealed record DiagnosisDto(Guid? Id, DateTimeOffset CreatedAt, string CreatedByName, Guid? WorkOrderId, Guid? WorkRequestId, string Context, string Observed,
    string? ProbableCause, string? Confidence, List<DiagnosisAction> Actions, bool HistoryTooThin, string? Message, int SentCount, int Up, int Down, bool? MyVote);

public sealed record VoteInput(bool? Up);

/// <summary>
/// The AI diagnosis (0.15.0): the server picks the record's asset history (completed work on the asset, then on assets of the same
/// category — same maker and model first — newest first, at most 40, only what the person may see) and Claude answers once in a fixed
/// structure. Work orders and parts it cites are kept only if they were sent. People's names and contacts are never sent.
/// </summary>
public static class AiDiagnose
{
    public const int MaxHistory = 40;
    public const int MinHistory = 3;
    public const int MaxActions = 5;
    public const string NeedsAsset = "Choose an asset first: a diagnosis works from the asset's history.";
    public const string Thin = "There are only {0} completed work orders on this asset and similar ones — too few for a diagnosis.";
    public const string Refused = "The AI assistant couldn't help with this one.";

    private const string Instructions = """
        You help a maintenance engineer diagnose a fault from the asset's own maintenance history.
        You get the record (a work order or a request) and past completed work orders on the same asset and on similar assets, most related first.
        Answer only with the requested JSON:
        - context: what is known about the asset and the record, in one or two sentences.
        - observed: what was seen (the reported symptom), in one or two sentences.
        - probableCause: the most likely cause given the history and the record, or null when the history doesn't point to one; confidence: low, medium or high (null when there is no probable cause).
        - actions: up to 5 actions ranked by relevance. Each has a short title; why (what happened before that supports it); parts: only part numbers that appear under "Parts used" in the history, with quantities; hours: estimated hours or null; sources: the numbers of the past work orders the action relies on (only numbers listed in the history).
        - historyTooThin: true when the history isn't related enough to support a diagnosis; then explain in message, leave probableCause null and actions empty. Otherwise message is null.
        Never invent work order numbers or part numbers. Base everything on the record and the history.
        """;

    private static readonly JsonObject Schema = AiText.Obj(
        ("context", AiText.Str()),
        ("observed", AiText.Str()),
        ("probableCause", AiText.OrNull(AiText.Str())),
        ("confidence", AiText.OrNull(new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("low", "medium", "high") })),
        ("actions", AiText.Arr(AiText.Obj(
            ("title", AiText.Str()),
            ("why", AiText.Str()),
            ("parts", AiText.Arr(AiText.Obj(("partNumber", AiText.Str()), ("quantity", AiText.Num())))),
            ("hours", AiText.OrNull(AiText.Num())),
            ("sources", AiText.Arr(AiText.Str()))))),
        ("historyTooThin", AiText.Bool()),
        ("message", AiText.OrNull(AiText.Str())));

    private sealed record Answer(string? Context, string? Observed, string? ProbableCause, string? Confidence, List<ActionAnswer>? Actions, bool HistoryTooThin, string? Message);
    private sealed record ActionAnswer(string? Title, string? Why, List<PartAnswer>? Parts, decimal? Hours, List<string>? Sources);
    private sealed record PartAnswer(string? PartNumber, decimal Quantity);

    private sealed record Subject(Guid? WorkOrderId, Guid? WorkRequestId, string Kind, string Number, string Title, string? Description, string? Symptom,
        Guid? AssetId, string? AssetNumber, string? AssetName, Guid? CategoryId, string? Category, string? Manufacturer, string? Model);

    private sealed record Past(Guid Id, string Number, string Title, WorkType Type, DateTimeOffset? CompletedAt, Guid? AssetId, string? Manufacturer, string? Model,
        string? Symptom, string? Cause, string? Remedy, string? CompletionNotes, string? CauseNotes);

    private sealed record UsedPart(Guid WorkOrderId, Guid PartId, string PartNumber, string Name, string? UnitOfMeasure, decimal Quantity);

    private static readonly JsonSerializerOptions Store = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/work-orders/{id:guid}/diagnose", (Guid id, CmmsDbContext db, AiService ai, ITenantContext ctx, CancellationToken ct) =>
            AiEndpoints.Guard(async () => await WorkOrderSubjectAsync(db, id, ct) is { } s ? await DiagnoseAsync(db, ai, ctx, s, ct) : Results.NotFound()))
            .WithTags("AI assistant").RequirePermission(Core.Identity.Permissions.WorkOrdersExecute);
        api.MapPost("/work-requests/{id:guid}/diagnose", (Guid id, CmmsDbContext db, AiService ai, ITenantContext ctx, CancellationToken ct) =>
            AiEndpoints.Guard(async () => await RequestSubjectAsync(db, id, ct) is { } s ? await DiagnoseAsync(db, ai, ctx, s, ct) : Results.NotFound()))
            .WithTags("AI assistant").RequirePermission(Core.Identity.Permissions.RequestsManage);

        // Earlier diagnoses: a work order lists its own and its request's, newest first.
        api.MapGet("/work-orders/{id:guid}/diagnoses", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var wo = await db.WorkOrders.AsNoTracking().Where(w => w.Id == id).Select(w => new { w.Id, w.WorkRequestId }).FirstOrDefaultAsync(ct);
            if (wo is null) return Results.NotFound();
            var q = db.AiDiagnoses.AsNoTracking().Where(d => d.WorkOrderId == wo.Id || (wo.WorkRequestId != null && d.WorkRequestId == wo.WorkRequestId));
            return Results.Ok(await ListAsync(db, ctx, q, ct));
        }).WithTags("AI assistant").RequirePermission(Core.Identity.Permissions.WorkOrdersView);
        api.MapGet("/work-requests/{id:guid}/diagnoses", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!await db.WorkRequests.AnyAsync(r => r.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await ListAsync(db, ctx, db.AiDiagnoses.AsNoTracking().Where(d => d.WorkRequestId == id), ct));
        }).WithTags("AI assistant").RequirePermission(Core.Identity.Permissions.RequestsView);

        // Thumbs up or down: one per person, changeable; null takes it back. Stays in CMX.
        api.MapPut("/ai/diagnoses/{id:guid}/vote", async (Guid id, VoteInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var d = await db.AiDiagnoses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null || ctx.UserId is not { } me || !await VisibleAsync(db, d, ct)) return Results.NotFound();
            var vote = await db.AiDiagnosisVotes.FirstOrDefaultAsync(v => v.DiagnosisId == id && v.UserId == me, ct);
            if (input.Up is null) { if (vote is not null) db.AiDiagnosisVotes.Remove(vote); }
            else if (vote is null) db.AiDiagnosisVotes.Add(new AiDiagnosisVote { TenantId = d.TenantId, DiagnosisId = id, UserId = me, Up = input.Up.Value, UpdatedAt = DateTimeOffset.UtcNow });
            else { vote.Up = input.Up.Value; vote.UpdatedAt = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(ct);
            var votes = await db.AiDiagnosisVotes.AsNoTracking().Where(v => v.DiagnosisId == id).Select(v => new { v.UserId, v.Up }).ToListAsync(ct);
            return Results.Ok(new { up = votes.Count(v => v.Up), down = votes.Count(v => !v.Up), myVote = votes.FirstOrDefault(v => v.UserId == me)?.Up });
        }).WithTags("AI assistant").RequireAuthorization();
    }

    private static async Task<bool> VisibleAsync(CmmsDbContext db, AiDiagnosis d, CancellationToken ct) =>
        d.WorkOrderId is { } w ? await db.WorkOrders.AnyAsync(x => x.Id == w, ct)
        : d.WorkRequestId is { } r && (await db.WorkRequests.AnyAsync(x => x.Id == r, ct) || await db.WorkOrders.AnyAsync(x => x.WorkRequestId == r, ct));

    private static Task<Subject?> WorkOrderSubjectAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        db.WorkOrders.AsNoTracking().Where(w => w.Id == id).Select(w => new Subject(w.Id, null, "Work order", w.Number, w.Title, w.Description,
            w.Symptom == null ? null : w.Symptom.Name, w.AssetId, w.Asset == null ? null : w.Asset.AssetNumber, w.Asset == null ? null : w.Asset.Name,
            w.Asset == null ? null : w.Asset.CategoryId, w.Asset == null || w.Asset.Category == null ? null : w.Asset.Category.Name,
            w.Asset == null ? null : w.Asset.Manufacturer, w.Asset == null ? null : w.Asset.Model)).FirstOrDefaultAsync(ct);

    private static Task<Subject?> RequestSubjectAsync(CmmsDbContext db, Guid id, CancellationToken ct) =>
        db.WorkRequests.AsNoTracking().Where(w => w.Id == id).Select(w => new Subject(null, w.Id, "Request", w.Number, w.Title, w.Description,
            w.Symptom == null ? null : w.Symptom.Name, w.AssetId, w.Asset == null ? null : w.Asset.AssetNumber, w.Asset == null ? null : w.Asset.Name,
            w.Asset == null ? null : w.Asset.CategoryId, w.Asset == null || w.Asset.Category == null ? null : w.Asset.Category.Name,
            w.Asset == null ? null : w.Asset.Manufacturer, w.Asset == null ? null : w.Asset.Model)).FirstOrDefaultAsync(ct);

    private static async Task<IResult> DiagnoseAsync(CmmsDbContext db, AiService ai, ITenantContext ctx, Subject s, CancellationToken ct)
    {
        if (s.AssetId is not { } assetId) return AiEndpoints.Problem(StatusCodes.Status400BadRequest, NeedsAsset);
        var (maker, model, category) = (s.Manufacturer, s.Model, s.CategoryId);
        var sameModel = !string.IsNullOrWhiteSpace(maker) && !string.IsNullOrWhiteSpace(model);
        // Site access applies: the query filters keep out work the person can't see.
        var history = await db.WorkOrders.AsNoTracking()
            .Where(w => (w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed) && w.Id != s.WorkOrderId && w.AssetId != null
                && (w.AssetId == assetId || (category != null && w.Asset!.CategoryId == category)))
            .OrderBy(w => w.AssetId == assetId ? 0 : sameModel && w.Asset!.Manufacturer == maker && w.Asset.Model == model ? 1 : 2)
            .ThenByDescending(w => w.CompletedAt).ThenByDescending(w => w.Number)
            .Take(MaxHistory)
            .Select(w => new Past(w.Id, w.Number, w.Title, w.Type, w.CompletedAt, w.AssetId, w.Asset!.Manufacturer, w.Asset.Model,
                w.Symptom == null ? null : w.Symptom.Name, w.FailureCode == null ? null : w.FailureCode.Name, w.Remedy == null ? null : w.Remedy.Name,
                w.CompletionNotes, w.CauseNotes))
            .ToListAsync(ct);
        if (history.Count < MinHistory)
            return Results.Ok(new DiagnosisDto(null, DateTimeOffset.UtcNow, ctx.UserName ?? "", s.WorkOrderId, s.WorkRequestId, "", "", null, null, [], true,
                Translator.T(string.Format(Thin, history.Count)), history.Count, 0, 0, null));

        var ids = history.Select(h => h.Id).ToList();
        var used = (await db.StockTransactions.AsNoTracking()
                .Where(t => t.WorkOrderId != null && ids.Contains(t.WorkOrderId.Value) && (t.Type == StockTransactionType.Issue || t.Type == StockTransactionType.Return))
                .GroupBy(t => new { WorkOrderId = t.WorkOrderId!.Value, t.PartId, t.Part!.PartNumber, t.Part.Name, t.Part.UnitOfMeasure })
                .Select(g => new UsedPart(g.Key.WorkOrderId, g.Key.PartId, g.Key.PartNumber, g.Key.Name, g.Key.UnitOfMeasure, -g.Sum(x => x.Quantity)))
                .ToListAsync(ct)).Where(p => p.Quantity > 0).ToList();
        var hours = await db.WorkOrderLabor.AsNoTracking().Where(l => ids.Contains(l.WorkOrderId))
            .GroupBy(l => l.WorkOrderId).Select(g => new { g.Key, Hours = g.Sum(x => x.Hours + x.OvertimeHours) }).ToDictionaryAsync(x => x.Key, x => x.Hours, ct);

        var (symptoms, causes, remedies) = await AiReport.CodesAsync(db, ct);
        var ask = new AiAsk(AiFeature.Diagnosis, Instructions, AiReport.CodeContext(symptoms, causes, remedies),
            Prompt(s, history, used, hours, Translator.CurrentLanguage == "ar" ? "Arabic" : "English"), Schema, AiEffort.Medium, 12000);
        var run = await ai.RunAsync(AiFeature.Diagnosis, s.WorkOrderId, s.WorkRequestId, ask, ct);
        if (run.Reply.Refused || run.Reply.Json is null) return AiEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, Refused);
        Answer? a;
        try { a = JsonSerializer.Deserialize<Answer>(run.Reply.Json, AiText.Read); }
        catch (JsonException) { a = null; }
        if (a is null) return AiEndpoints.Problem(StatusCodes.Status502BadGateway, AiService.NoAnswer);

        var answer = Check(a, history, used);
        var d = new AiDiagnosis
        {
            TenantId = db.CurrentTenantId!.Value, WorkOrderId = s.WorkOrderId, WorkRequestId = s.WorkRequestId, AssetId = assetId,
            AnswerJson = JsonSerializer.Serialize(answer, Store), SentCount = history.Count, CreatedByName = AiText.Clip(ctx.UserName, 200),
        };
        db.AiDiagnoses.Add(d);
        await db.SaveChangesAsync(ct);
        return Results.Ok(Dto(d, answer, 0, 0, null));
    }

    private static string Prompt(Subject s, List<Past> history, List<UsedPart> used, Dictionary<Guid, decimal> hours, string language)
    {
        var b = new StringBuilder()
            .Append($"The record: {s.Kind} {s.Number}\nTitle: {AiText.Clip(s.Title, 1000)}\nDescription: {AiText.Clip(s.Description, 1000)}\nSymptom: {s.Symptom}\n")
            .Append($"Asset: {s.AssetNumber} — {s.AssetName}\nCategory: {s.Category}\nManufacturer: {s.Manufacturer}\nModel: {s.Model}\n\n")
            .Append($"Past completed work orders, most related first ({history.Count}):\n");
        foreach (var h in history)
        {
            var relation = h.AssetId == s.AssetId ? "same asset"
                : !string.IsNullOrWhiteSpace(s.Manufacturer) && h.Manufacturer == s.Manufacturer && h.Model == s.Model ? "same maker and model" : "same category";
            b.Append($"\n### {h.Number} · completed {h.CompletedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · {h.Type} · {relation}\n")
             .Append($"Title: {AiText.Clip(h.Title, 1000)}\n")
             .Append($"Symptom: {h.Symptom} | Cause: {h.Cause} | Remedy: {h.Remedy}\n")
             .Append($"Work done: {AiText.Clip(h.CompletionNotes, 1000)}\n")
             .Append($"Cause details: {AiText.Clip(h.CauseNotes, 1000)}\n")
             .Append("Parts used: ").Append(AiText.Lines(used.Where(p => p.WorkOrderId == h.Id), p => $"{p.PartNumber} — {p.Name} × {p.Quantity:0.###} ({p.UnitOfMeasure})", "none").Replace("\n", "; ")).Append('\n')
             .Append($"Labour: {(hours.TryGetValue(h.Id, out var hr) ? hr.ToString("0.##", CultureInfo.InvariantCulture) + " h" : "not recorded")}\n");
        }
        return b.Append($"\nWrite every text in {language}.").ToString();
    }

    private static DiagnosisAnswer Check(Answer a, List<Past> history, List<UsedPart> used)
    {
        var byNumber = history.ToDictionary(h => h.Number, StringComparer.OrdinalIgnoreCase);
        var parts = used.GroupBy(p => p.PartNumber, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var thin = a.HistoryTooThin;
        var actions = thin ? [] : (a.Actions ?? []).Take(MaxActions).Select(x =>
        {
            var ps = new List<PartSuggestion>();
            foreach (var p in x.Parts ?? [])
                if (p.PartNumber is not null && p.Quantity > 0 && parts.TryGetValue(p.PartNumber.Trim(), out var u) && ps.All(e => e.PartId != u.PartId))
                    ps.Add(new PartSuggestion(u.PartId, u.PartNumber, u.Name, u.UnitOfMeasure, Math.Round(p.Quantity, 3)));
            var sources = (x.Sources ?? []).Select(n => n?.Trim() ?? "").Where(byNumber.ContainsKey).Select(n => byNumber[n])
                .DistinctBy(h => h.Id).Select(h => new SourceRef(h.Id, h.Number, h.Title)).ToList();
            return new DiagnosisAction(AiText.Clip(x.Title, 300), AiText.Clip(x.Why, 1000), ps, x.Hours is > 0 and < 1000 ? Math.Round(x.Hours.Value, 2) : null, sources);
        }).ToList();
        var cause = thin ? "" : AiText.Clip(a.ProbableCause, 1000);
        var confidence = cause.Length > 0 && a.Confidence?.Trim().ToLowerInvariant() is "low" or "medium" or "high" ? a.Confidence.Trim().ToLowerInvariant() : null;
        var message = AiText.Clip(a.Message, 1000);
        return new DiagnosisAnswer(AiText.Clip(a.Context, 2000), AiText.Clip(a.Observed, 2000), cause.Length == 0 ? null : cause, confidence, actions, thin,
            message.Length == 0 ? null : message);
    }

    private static DiagnosisDto Dto(AiDiagnosis d, DiagnosisAnswer a, int up, int down, bool? mine) =>
        new(d.Id, d.CreatedAt, d.CreatedByName, d.WorkOrderId, d.WorkRequestId, a.Context, a.Observed, a.ProbableCause, a.Confidence, a.Actions,
            a.HistoryTooThin, a.Message, d.SentCount, up, down, mine);

    private static async Task<List<DiagnosisDto>> ListAsync(CmmsDbContext db, ITenantContext ctx, IQueryable<AiDiagnosis> q, CancellationToken ct)
    {
        var rows = await q.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var votes = await db.AiDiagnosisVotes.AsNoTracking().Where(v => ids.Contains(v.DiagnosisId)).ToListAsync(ct);
        var answers = rows.ToDictionary(r => r.Id, r => JsonSerializer.Deserialize<DiagnosisAnswer>(r.AnswerJson, Store)
            ?? new DiagnosisAnswer("", "", null, null, [], false, null));
        // Sources the viewer can't see now (site access) are left out.
        var cited = answers.Values.SelectMany(a => a.Actions).SelectMany(x => x.Sources).Select(x => x.Id).Distinct().ToList();
        var visible = (await db.WorkOrders.AsNoTracking().Where(w => cited.Contains(w.Id)).Select(w => w.Id).ToListAsync(ct)).ToHashSet();
        return rows.Select(r =>
        {
            var a = answers[r.Id];
            a = a with { Actions = a.Actions.Select(x => x with { Sources = x.Sources.Where(s => visible.Contains(s.Id)).ToList() }).ToList() };
            var mine = votes.Where(v => v.DiagnosisId == r.Id).ToList();
            return Dto(r, a, mine.Count(v => v.Up), mine.Count(v => !v.Up), mine.FirstOrDefault(v => v.UserId == ctx.UserId)?.Up);
        }).ToList();
    }
}
