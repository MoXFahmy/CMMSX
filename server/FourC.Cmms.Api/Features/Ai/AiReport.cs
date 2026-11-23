using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features.Ai;

public sealed record CodeRef(Guid Id, string Code, string Name);
public sealed record PartSuggestion(Guid PartId, string PartNumber, string Name, string? UnitOfMeasure, decimal Quantity);

/// <summary>Suggestions for the completion report; nothing is saved until the person completes the work.</summary>
public sealed record AiReportResult(string WorkDone, string? CauseDetails, CodeRef? Symptom, CodeRef? Cause, CodeRef? Remedy, decimal? Hours, List<PartSuggestion> Parts);

public sealed record AiReportInput(Guid? WorkOrderId, string? Notes);

/// <summary>Shared pieces for the AI prompts and answers.</summary>
public static class AiText
{
    public const int MaxNotes = 8000;

    public static string Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();

    /// <summary>A property that may be null in a structured answer.</summary>
    public static JsonObject OrNull(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };

    public static JsonObject Str() => new() { ["type"] = "string" };
    public static JsonObject Num() => new() { ["type"] = "number" };
    public static JsonObject Bool() => new() { ["type"] = "boolean" };
    public static JsonObject Arr(JsonObject items) => new() { ["type"] = "array", ["items"] = items };

    /// <summary>An object whose properties are all required and nothing else is allowed (as structured outputs want).</summary>
    public static JsonObject Obj(params (string Name, JsonObject Schema)[] props)
    {
        var p = new JsonObject();
        foreach (var (n, s) in props) p[n] = s;
        return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = new JsonArray(props.Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray()), ["additionalProperties"] = false };
    }

    public static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web);

    public static string Lines<T>(IEnumerable<T> items, Func<T, string> line, string none = "(none)")
    {
        var list = items.Select(line).ToList();
        return list.Count == 0 ? none : string.Join('\n', list);
    }
}

/// <summary>The AI report (0.15.0): the technician's notes become the completion report's fields, using only the organization's own codes and parts.</summary>
public static class AiReport
{
    public const string NoNotes = "Type or dictate some notes first.";
    public const string LongNotes = "Notes can be at most 8,000 characters.";
    public const string Refused = "The AI assistant couldn't help with these notes.";

    private const string Instructions = """
        You fill in a maintenance technician's completion report from the notes they typed or dictated on the job.
        Answer only with the requested JSON.
        - workDone: a clear, short report of the work that was done, in the same language as the notes (Arabic or English). Keep the technician's facts; add nothing they didn't say.
        - causeDetails: what caused the fault, if the notes say; otherwise null. Same language as the notes.
        - symptomCode, causeCode, remedyCode: a code from the organization's lists below that matches what the notes say was seen, what was wrong and what was done; null when the notes don't support one. Never invent a code.
        - hours: the technician's own working hours if the notes say how long the work took; otherwise null.
        - parts: parts the notes say were used, only from the work order's candidate parts (by part number), each with the quantity used; an empty list when none. Never invent a part number.
        """;

    private static readonly JsonObject Schema = AiText.Obj(
        ("workDone", AiText.Str()),
        ("causeDetails", AiText.OrNull(AiText.Str())),
        ("symptomCode", AiText.OrNull(AiText.Str())),
        ("causeCode", AiText.OrNull(AiText.Str())),
        ("remedyCode", AiText.OrNull(AiText.Str())),
        ("hours", AiText.OrNull(AiText.Num())),
        ("parts", AiText.Arr(AiText.Obj(("partNumber", AiText.Str()), ("quantity", AiText.Num())))));

    private sealed record Answer(string? WorkDone, string? CauseDetails, string? SymptomCode, string? CauseCode, string? RemedyCode, decimal? Hours, List<PartAnswer>? Parts);
    private sealed record PartAnswer(string? PartNumber, decimal Quantity);

    private sealed record Candidate(Guid PartId, string PartNumber, string Name, string? UnitOfMeasure);

    /// <summary>The organization's active code lists (cached with the instructions).</summary>
    public static async Task<(List<CodeRef> Symptoms, List<CodeRef> Causes, List<CodeRef> Remedies)> CodesAsync(CmmsDbContext db, CancellationToken ct) => (
        await db.Symptoms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new CodeRef(x.Id, x.Code, x.Name)).ToListAsync(ct),
        await db.FailureCodes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new CodeRef(x.Id, x.Code, x.Name)).ToListAsync(ct),
        await db.Remedies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new CodeRef(x.Id, x.Code, x.Name)).ToListAsync(ct));

    public static string CodeContext(List<CodeRef> symptoms, List<CodeRef> causes, List<CodeRef> remedies) => new StringBuilder()
        .Append("Symptoms (what was seen):\n").Append(AiText.Lines(symptoms, c => $"{c.Code} — {c.Name}")).Append("\n\n")
        .Append("Causes (what was wrong):\n").Append(AiText.Lines(causes, c => $"{c.Code} — {c.Name}")).Append("\n\n")
        .Append("Remedies (what was done):\n").Append(AiText.Lines(remedies, c => $"{c.Code} — {c.Name}")).ToString();

    public static async Task<IResult> HandleAsync(AiReportInput input, CmmsDbContext db, AiService ai, CancellationToken ct)
    {
        var notes = input.Notes?.Trim() ?? "";
        if (notes.Length == 0) return AiEndpoints.Problem(StatusCodes.Status400BadRequest, NoNotes);
        if (notes.Length > AiText.MaxNotes) return AiEndpoints.Problem(StatusCodes.Status400BadRequest, LongNotes);
        var wo = await db.WorkOrders.AsNoTracking().Where(w => w.Id == input.WorkOrderId).Select(w => new
        {
            w.Id, w.Number, w.Title, w.Description, w.Type, w.AssetId,
            Category = w.Asset == null || w.Asset.Category == null ? null : w.Asset.Category.Name,
            Manufacturer = w.Asset == null ? null : w.Asset.Manufacturer, Model = w.Asset == null ? null : w.Asset.Model,
        }).FirstOrDefaultAsync(ct);
        if (wo is null) return Results.NotFound();

        var (symptoms, causes, remedies) = await CodesAsync(db, ct);
        var planned = await db.WorkOrderParts.AsNoTracking().Where(p => p.WorkOrderId == wo.Id)
            .Select(p => new Candidate(p.PartId, p.Part!.PartNumber, p.Part.Name, p.Part.UnitOfMeasure)).ToListAsync(ct);
        var spares = wo.AssetId is { } assetId
            ? await db.AssetParts.AsNoTracking().Where(p => p.AssetId == assetId).OrderBy(p => p.Part!.PartNumber)
                .Select(p => new Candidate(p.PartId, p.Part!.PartNumber, p.Part.Name, p.Part.UnitOfMeasure)).ToListAsync(ct)
            : [];
        var candidates = planned.Concat(spares).DistinctBy(p => p.PartId).ToList();

        var prompt = new StringBuilder()
            .Append($"Work order {wo.Number} ({wo.Type})\nTitle: {AiText.Clip(wo.Title, 1000)}\n")
            .Append($"Description: {AiText.Clip(wo.Description, 1000)}\n")
            .Append($"Asset category: {wo.Category}\nManufacturer: {wo.Manufacturer}\nModel: {wo.Model}\n\n")
            .Append("Candidate parts (part number — name (unit)):\n").Append(AiText.Lines(candidates, p => $"{p.PartNumber} — {p.Name} ({p.UnitOfMeasure})")).Append("\n\n")
            .Append("Technician's notes:\n<notes>\n").Append(notes).Append("\n</notes>").ToString();
        var ask = new AiAsk(AiFeature.Report, Instructions, CodeContext(symptoms, causes, remedies), prompt, Schema, AiEffort.Low, 4000);

        var run = await ai.RunAsync(AiFeature.Report, wo.Id, null, ask, ct);
        if (run.Reply.Refused || run.Reply.Json is null) return AiEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, Refused);
        Answer? a;
        try { a = JsonSerializer.Deserialize<Answer>(run.Reply.Json, AiText.Read); }
        catch (JsonException) { a = null; }
        if (a is null) return AiEndpoints.Problem(StatusCodes.Status502BadGateway, AiService.NoAnswer);
        return Results.Ok(Map(a, symptoms, causes, remedies, candidates));
    }

    private static AiReportResult Map(Answer a, List<CodeRef> symptoms, List<CodeRef> causes, List<CodeRef> remedies, List<Candidate> candidates)
    {
        static CodeRef? Pick(List<CodeRef> list, string? code) =>
            string.IsNullOrWhiteSpace(code) ? null : list.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        var byNumber = candidates.GroupBy(c => c.PartNumber, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var parts = new List<PartSuggestion>();
        foreach (var p in a.Parts ?? [])
        {
            if (p.PartNumber is null || p.Quantity <= 0 || !byNumber.TryGetValue(p.PartNumber.Trim(), out var c) || parts.Any(x => x.PartId == c.PartId)) continue;
            parts.Add(new PartSuggestion(c.PartId, c.PartNumber, c.Name, c.UnitOfMeasure, Math.Round(p.Quantity, 3)));
        }
        var hours = a.Hours is > 0 and <= 24 ? Math.Round(a.Hours.Value, 2) : (decimal?)null;
        var cause = AiText.Clip(a.CauseDetails, 2000);
        return new AiReportResult(AiText.Clip(a.WorkDone, 4000), cause.Length == 0 ? null : cause,
            Pick(symptoms, a.SymptomCode), Pick(causes, a.CauseCode), Pick(remedies, a.RemedyCode), hours, parts);
    }
}
