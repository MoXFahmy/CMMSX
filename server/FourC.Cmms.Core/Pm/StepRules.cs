using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Core.Pm;

/// <summary>Checklist rules over a work order's steps (0.13.0): answered, failed, safety steps, completion; no database.</summary>
public static class StepRules
{
    public const string AnswerSafety = "Answer the safety steps first: {0}.";
    public const string SafetyNotOk = "A safety step isn't OK: {0}.";
    public const string TakePhoto = "Take a photo for: {0}.";
    public const string AnswerSteps = "Answer these steps first: {0}.";

    /// <summary>Check: an answer (a step ticked before 0.13.0 counts as OK); Number and Meter: a value; Text: some text.</summary>
    public static bool Answered(WorkOrderTask t) => t.Type switch
    {
        StepType.Number or StepType.Meter => t.Value is not null,
        StepType.Text => !string.IsNullOrWhiteSpace(t.TextValue),
        _ => t.Answer is not null || t.IsDone,
    };

    /// <summary>A Check step's answer as shown: one ticked before 0.13.0 (done, no answer stored) is OK.</summary>
    public static StepAnswer? ShownAnswer(WorkOrderTask t) => t.Answer ?? (t.Type == StepType.Check && t.IsDone ? StepAnswer.Ok : null);

    /// <summary>Not OK, or a reading at an alert or alarm level.</summary>
    public static bool Failed(WorkOrderTask t) => t.Answer == StepAnswer.NotOk || t.Level is { } l && l != MeasureLevel.Normal;

    public static string Name(WorkOrderTask t) => $"#{t.Sequence} {t.Description}";

    /// <summary>
    /// Why work can't go on yet, or null. Starting (<paramref name="forStart"/>) needs the safety steps without an asset — all of
    /// them on an ordinary work order, the round-wide ones on a round; answering a step needs those plus its asset's.
    /// </summary>
    public static string? SafetyBlock(IEnumerable<WorkOrderTask> steps, Guid? assetId, bool forStart)
    {
        var safety = steps.Where(t => t.BeforeStart && (t.AssetId is null || !forStart && assetId is not null && t.AssetId == assetId))
            .OrderBy(t => t.Sequence).ToList();
        var open = safety.Where(t => !Answered(t)).ToList();
        if (open.Count > 0) return string.Format(AnswerSafety, string.Join(", ", open.Select(Name)));
        return safety.FirstOrDefault(Failed) is { } bad ? string.Format(SafetyNotOk, Name(bad)) : null;
    }

    /// <summary>What completing still needs: a photo on each photo-required step, and (when the organization asks) every Number, Meter and Text step answered.</summary>
    public static IReadOnlyList<string> CompletionProblems(IEnumerable<WorkOrderTask> steps, IReadOnlySet<Guid> stepsWithPhotos, bool requireReadings)
    {
        var list = steps.OrderBy(t => t.Sequence).ToList();
        var problems = new List<string>();
        var photos = list.Where(t => t.PhotoRequired && !stepsWithPhotos.Contains(t.Id)).ToList();
        if (photos.Count > 0) problems.Add(string.Format(TakePhoto, string.Join(", ", photos.Select(Name))));
        var readings = requireReadings ? list.Where(t => t.Type is StepType.Number or StepType.Meter or StepType.Text && !Answered(t)).ToList() : [];
        if (readings.Count > 0) problems.Add(string.Format(AnswerSteps, string.Join(", ", readings.Select(Name))));
        return problems;
    }

    /// <summary>A round asset's progress: red when a step failed, green when all are answered, else none.</summary>
    public static string? RoundProgress(IEnumerable<WorkOrderTask> assetSteps)
    {
        var list = assetSteps.ToList();
        if (list.Any(Failed)) return "red";
        return list.Count > 0 && list.All(Answered) ? "green" : null;
    }
}
