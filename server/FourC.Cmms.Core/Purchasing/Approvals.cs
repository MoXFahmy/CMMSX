using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Purchasing;

/// <summary>What kind of order it is (Parts, Services, Capital — an editable list); approval levels can differ by type (0.16.0).</summary>
public class OrderType : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>On a level: every listed approver must sign, or any one of them.</summary>
public enum ApprovalRule { Any = 1, All = 2 }

/// <summary>
/// One approval level (0.16.0): orders whose base-currency total reaches <see cref="MinAmount"/> need it. Levels add up and
/// are signed lowest first. A set is the organization's (<see cref="SiteId"/> null) or a site's own, which replaces it.
/// </summary>
public class ApprovalLevel : TenantEntity
{
    public Guid? SiteId { get; set; }
    /// <summary>Null: every order type.</summary>
    public Guid? OrderTypeId { get; set; }
    public required string Name { get; set; }
    public decimal MinAmount { get; set; }
    public ApprovalRule Rule { get; set; } = ApprovalRule.Any;
    /// <summary>One to five users with "Approve purchases".</summary>
    public List<Guid> ApproverIds { get; set; } = [];
}

public enum ApprovalStepStatus { Pending = 1, Waiting = 2, Approved = 3, Rejected = 4, Stopped = 5 }

/// <summary>A level an order needs, copied onto the order when it is submitted (one row per level per round).</summary>
public class PurchaseOrderApprovalStep : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int Round { get; set; }
    public int Step { get; set; }
    public required string Name { get; set; }
    public decimal MinAmount { get; set; }
    public ApprovalRule Rule { get; set; }
    public List<Guid> ApproverIds { get; set; } = [];
    public ApprovalStepStatus Status { get; set; } = ApprovalStepStatus.Pending;
    public DateTimeOffset? DoneAt { get; set; }
}

/// <summary>Someone signed (or rejected) a step of an order's round.</summary>
public class PurchaseOrderSignature : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int Round { get; set; }
    public int Step { get; set; }
    public Guid UserId { get; set; }
    public required string UserName { get; set; }
    public bool Approved { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset At { get; set; }
}
