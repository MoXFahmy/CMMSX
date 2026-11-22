using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Notifications;

/// <summary>
/// A push notification waiting to be sent to one person's phones, or the record of one sent or failed (0.11.0). Saved with
/// the change that caused it; a background sender delivers it through Firebase and retries failures.
/// </summary>
public class PushMessage : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    /// <summary>Where tapping it leads in the phone app, e.g. "/tech/job/{id}".</summary>
    public string? Link { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
