using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Core.Pm;

/// <summary>Copies a template step's definition (0.13.0) — used by every path that turns template steps into work order steps or copies a template.</summary>
public static class StepCopy
{
    public static WorkOrderTask ToWorkOrder(PmTaskStep s, Guid workOrderId, int sequence, Guid? assetId) => new()
    {
        WorkOrderId = workOrderId, Sequence = sequence, AssetId = assetId,
        Description = s.Description.Length > 1000 ? s.Description[..1000] : s.Description,
        DescriptionAr = s.DescriptionAr is { Length: > 1000 } ar ? ar[..1000] : s.DescriptionAr,
        Type = s.Type, Unit = s.Unit, Decimals = s.Decimals, MeasurementPointId = s.MeasurementPointId, PhotoRequired = s.PhotoRequired, BeforeStart = s.BeforeStart,
        FollowUp = s.FollowUp, FollowUpTemplateId = s.FollowUpTemplateId,
    };

    public static PmTaskStep Copy(PmTaskStep s, Guid pmTaskId) => new()
    {
        PmTaskId = pmTaskId, Sequence = s.Sequence, Description = s.Description, DescriptionAr = s.DescriptionAr,
        Type = s.Type, Unit = s.Unit, Decimals = s.Decimals, MeasurementPointId = s.MeasurementPointId, PhotoRequired = s.PhotoRequired, BeforeStart = s.BeforeStart,
        FollowUp = s.FollowUp, FollowUpTemplateId = s.FollowUpTemplateId,
    };

    /// <summary>
    /// The definitions for a list of step texts taken from a template: each text equal to an unused template step's description takes that
    /// step (in order); any other text is a plain Check step.
    /// </summary>
    public static List<PmTaskStep> Match(IEnumerable<string> texts, IEnumerable<PmTaskStep> template)
    {
        var unused = template.OrderBy(s => s.Sequence).ToList();
        var result = new List<PmTaskStep>();
        foreach (var text in texts)
        {
            var hit = unused.FirstOrDefault(s => string.Equals(s.Description.Trim(), text.Trim(), StringComparison.Ordinal));
            if (hit is not null) { unused.Remove(hit); result.Add(hit); }
            else result.Add(new PmTaskStep { Description = text });
        }
        return result;
    }
}
