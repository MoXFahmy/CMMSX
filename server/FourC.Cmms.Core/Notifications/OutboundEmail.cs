using FourC.Cmms.Core.Common;
using K = FourC.Cmms.Core.Notifications.EmailRecipientKinds;

namespace FourC.Cmms.Core.Notifications;

public enum EmailStatus { Pending = 1, Sent = 2, Failed = 3 }

/// <summary>The e-mails CMX can send; organizations switch them off and users opt out by these keys.</summary>
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
    public const string RequestComment = "request.comment";
    public const string RequestRefused = "request.refused";
    public const string WorkOrderCancelled = "workorder.cancelled";
    public const string WorkOrderOverdue = "workorder.overdue";
    /// <summary>"Send me a test e-mail" from Settings; not a notification anyone can switch off.</summary>
    public const string Test = "test";

    public static readonly IReadOnlyList<string> All =
    [
        RequestSubmitted, RequestApproved, RequestRejected, RequestComment, WorkOrderAssigned, PmAssigned, WorkOrderCompleted, RequestCompleted,
        RequestRefused, WorkOrderCancelled, WorkOrderOverdue, PartsReceived,
        PoSubmitted, PoApproved, PoRejected, PoSent,
    ];

    /// <summary>
    /// The work and request e-mails whose recipients the organization chooses (Settings › Notifications grid): the kinds of
    /// recipient that make sense for each, and those on by default (today's behaviour for the e-mails that existed before 0.10.0).
    /// Purchasing e-mails keep their fixed recipients.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (IReadOnlyList<string> Applicable, IReadOnlyList<string> Defaults)> Grid =
        new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>
        {
            [RequestSubmitted] = ([K.Requester, K.RequestRecipients], [K.RequestRecipients]),
            [RequestApproved] = ([K.Requester, K.RequestRecipients, K.Technicians, K.Contractors], [K.Requester]),
            [RequestRejected] = ([K.Requester, K.RequestRecipients], [K.Requester]),
            [RequestComment] = ([K.Requester, K.RequestRecipients, K.Technicians, K.Contractors], [K.Requester, K.RequestRecipients]),
            [WorkOrderAssigned] = ([K.Technicians, K.Contractors], [K.Technicians, K.Contractors]),
            [PmAssigned] = ([K.Technicians, K.Contractors], [K.Technicians, K.Contractors]),
            [WorkOrderCompleted] = ([K.RequestRecipients, K.Issuer], [K.Issuer]),
            [RequestCompleted] = ([K.Requester], [K.Requester]),
            [RequestRefused] = ([K.RequestRecipients, K.Technicians, K.Contractors, K.Issuer], [K.RequestRecipients, K.Technicians, K.Contractors, K.Issuer]),
            [WorkOrderCancelled] = ([K.Requester, K.RequestRecipients, K.Technicians, K.Contractors, K.Issuer], [K.Requester, K.Technicians, K.Contractors]),
            [WorkOrderOverdue] = ([K.RequestRecipients, K.Technicians, K.Contractors, K.Issuer], [K.Technicians, K.Contractors, K.Issuer]),
            [PartsReceived] = ([K.Technicians, K.Contractors], [K.Technicians, K.Contractors]),
        };

    /// <summary>The recipient kinds switched on for <paramref name="eventKey"/>: the organization's choice (only kinds that apply), else the defaults.</summary>
    public static IReadOnlySet<string> KindsOn(Tenancy.TenantSettings settings, string eventKey)
    {
        if (!Grid.TryGetValue(eventKey, out var row)) return new HashSet<string>();
        return (settings.EmailRecipients ?? []).TryGetValue(eventKey, out var chosen) && chosen is not null
            ? chosen.Where(row.Applicable.Contains).ToHashSet()
            : row.Defaults.ToHashSet();
    }
}

/// <summary>Who an e-mail can go to, in the Settings › Notifications grid.</summary>
public static class EmailRecipientKinds
{
    /// <summary>The request's contact (name and e-mail on the request).</summary>
    public const string Requester = "requester";
    /// <summary>The site's request recipients, the request type's and the request's own extra recipients.</summary>
    public const string RequestRecipients = "requestRecipients";
    /// <summary>In-house people assigned, and members of the team the work is given to.</summary>
    public const string Technicians = "technicians";
    /// <summary>Assigned people and team members marked as contractors.</summary>
    public const string Contractors = "contractors";
    /// <summary>Who created the work order (for e-mails about a request only, who created the request).</summary>
    public const string Issuer = "issuer";

    public static readonly IReadOnlyList<string> All = [Requester, RequestRecipients, Technicians, Contractors, Issuer];
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
    /// <summary>Who it is from (a site's sender name); null = the organization's name.</summary>
    public string? FromName { get; set; }
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
