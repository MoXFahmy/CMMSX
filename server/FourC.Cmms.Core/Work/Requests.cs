using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Work;

/// <summary>
/// A kind of request ("Air conditioning", "Plumbing", "IT"): its default trade and priority, who is told about new requests of
/// this type, and optionally automatic approval into a work order for a set trade or team.
/// </summary>
public class RequestType : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>0.28.0: the name in Arabic; empty = the main name for everyone.</summary>
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? DefaultTradeId { get; set; }
    public WorkPriority? DefaultPriority { get; set; }
    /// <summary>Users told about new requests of this type, on top of the site's request recipients.</summary>
    public List<Guid> RecipientIds { get; set; } = [];
    /// <summary>Requests of this type become work orders as soon as they are raised.</summary>
    public bool AutoApprove { get; set; }
    /// <summary>The team automatically approved work is given to (when it can work at the request's site).</summary>
    public Guid? TeamId { get; set; }
}

/// <summary>Why a work order was cancelled (an editable list).</summary>
public class CancelReason : TenantEntity, ICodeLookup, IBilingualName
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A message on a request's thread between the requester and maintenance.</summary>
public class WorkRequestComment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid WorkRequestId { get; set; }
    public required string Body { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    /// <summary>Written by the person who raised the request (the requester's side of the thread).</summary>
    public bool ByRequester { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The requester's answer to finished work: accepted or refused, with a rating and a comment. One row per answer.</summary>
public class RequestFeedback : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid WorkRequestId { get; set; }
    public Guid WorkOrderId { get; set; }
    public bool Accepted { get; set; }
    /// <summary>1 to 5 stars; required to accept, optional to refuse.</summary>
    public int? Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }
    /// <summary>The signed-in requester who answered; null when answered through the e-mailed link.</summary>
    public Guid? UserId { get; set; }
    public string? ByName { get; set; }
    public bool ViaLink { get; set; }
}

/// <summary>Actions an administrator can make need a comment (<c>TenantSettings.CommentRequiredFor</c>). "Complete" is <c>RequireCompletionNotes</c>.</summary>
public static class CommentActions
{
    public const string Reject = "reject";
    public const string Refuse = "refuse";
    public const string Hold = "hold";
    public const string Reopen = "reopen";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [Reject, Refuse, Hold, Reopen, Cancel];
}
