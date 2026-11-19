using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Notifications;

public enum EmailStatus { Pending = 1, Sent = 2, Failed = 3 }

/// <summary>The e-mails 4C CMMS can send; organizations switch them off and users opt out by these keys.</summary>
public static class EmailEvents
{
    public const string RequestSubmitted = "request.submitted";
    public const string RequestApproved = "request.approved";
    public const string RequestRejected = "request.rejected";
    public const string RequestCompleted = "request.completed";
    public const string WorkOrderAssigned = "workorder.assigned";
    public const string PmAssigned = "pm.assigned";
    public const string WorkOrderCompleted = "workorder.completed";
    public const string PoSubmitted = "po.submitted";
    public const string PoApproved = "po.approved";
    public const string PoRejected = "po.rejected";
    public const string PoSent = "po.sent";
    public const string PartsReceived = "parts.received";
    /// <summary>"Send me a test e-mail" from Settings; not a notification anyone can switch off.</summary>
    public const string Test = "test";

    public static readonly IReadOnlyList<string> All =
    [
        RequestSubmitted, RequestApproved, RequestRejected, RequestCompleted, WorkOrderAssigned, PmAssigned, WorkOrderCompleted,
        PoSubmitted, PoApproved, PoRejected, PoSent, PartsReceived,
    ];
}

/// <summary>
/// An e-mail waiting to be sent, or the record of one sent or failed (the outbox). It is saved with the change that
/// caused it, so either both happen or neither; a background sender delivers it and retries failures.
/// </summary>
public class OutboundEmail : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string EventKey { get; set; }
    public required string ToAddress { get; set; }
    public string? ToName { get; set; }
    public string? ReplyTo { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public required string TextBody { get; set; }
    /// <summary>The record the e-mail is about, e.g. "WorkOrder".</summary>
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Set while a sender works on the row, so two app instances never send it twice.</summary>
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
