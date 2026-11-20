using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Work;

/// <summary>
/// A change queued on a technician's phone and sent when there was signal. Kept so a change sent twice (the connection
/// dropped after the server saved it) is applied once and answers the same both times.
/// </summary>
public class TechAction : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The id the phone gave the change.</summary>
    public Guid ClientId { get; set; }
    public required string Type { get; set; }
    /// <summary>"done" or "rejected".</summary>
    public required string Status { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    /// <summary>When the technician made the change (clamped).</summary>
    public DateTimeOffset At { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
