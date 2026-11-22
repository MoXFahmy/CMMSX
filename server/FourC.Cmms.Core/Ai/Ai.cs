using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Ai;

/// <summary>What an AI action was for (0.15.0).</summary>
public enum AiFeature { Report, Diagnosis }

/// <summary>
/// How an AI call ended. Answered and Refused reached Claude and count against the month's allowance; Pending holds a place while
/// Claude answers (so two calls at once can't pass the limit); Failed and OverLimit don't count.
/// </summary>
public enum AiResult { Pending, Answered, Refused, Failed, OverLimit }

/// <summary>One press of an AI button (0.15.0): who, what for, the tokens Claude used and the estimated cost. Kept for the usage page.</summary>
public class AiCall : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>The person's name when they pressed the button.</summary>
    public string UserName { get; set; } = "";
    public AiFeature Feature { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? WorkRequestId { get; set; }
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    /// <summary>US dollars, from the server's price table.</summary>
    public decimal EstimatedCost { get; set; }
    public AiResult Result { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A diagnosis Claude gave for a work order or a request (0.15.0), with the work orders and parts it drew on, already checked.</summary>
public class AiDiagnosis : TenantEntity
{
    public Guid? WorkOrderId { get; set; }
    public Guid? WorkRequestId { get; set; }
    public Guid AssetId { get; set; }
    /// <summary>The answer as JSON: texts, actions, parts (ids and names) and source work orders (ids, numbers and titles).</summary>
    public string AnswerJson { get; set; } = "{}";
    /// <summary>How many past work orders were sent.</summary>
    public int SentCount { get; set; }
    public string CreatedByName { get; set; } = "";
}

/// <summary>A person's thumbs up or down on a diagnosis: one per person, changeable. Stays in CMX.</summary>
public class AiDiagnosisVote : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid DiagnosisId { get; set; }
    public Guid UserId { get; set; }
    public bool Up { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
