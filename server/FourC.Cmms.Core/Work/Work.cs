using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;

namespace FourC.Cmms.Core.Work;

public enum WorkType { Corrective = 1, Preventive = 2, Breakdown = 3, Inspection = 4, Predictive = 5, Project = 6, Safety = 7, Other = 99 }
public enum WorkPriority { Low = 1, Medium = 2, High = 3, Urgent = 4 }
public enum WorkOrderStatus { Open = 1, InProgress = 2, OnHold = 3, Completed = 4, Closed = 5, Cancelled = 6 }
public enum HoldReason { WaitingForParts = 1, WaitingForApproval = 2, WaitingForAccess = 3, WaitingForContractor = 4, Other = 99 }
public enum WorkRequestStatus { New = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

/// <summary>Failure / cause code used for reliability reporting (legacy failurecause).</summary>
public class FailureCode : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A request for work from anyone in the organization, triaged into a work order (legacy worequest).</summary>
public class WorkRequest : TenantEntity
{
    public required string Number { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public WorkPriority Priority { get; set; } = WorkPriority.Medium;
    public WorkType Type { get; set; } = WorkType.Corrective;
    public WorkRequestStatus Status { get; set; } = WorkRequestStatus.New;
    public string? RequesterName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RequesterPhone { get; set; }
    public DateOnly? RequiredBy { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedById { get; set; }
    public Guid? WorkOrderId { get; set; }
}

public class WorkOrder : TenantEntity
{
    public required string Number { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkType Type { get; set; } = WorkType.Corrective;
    public WorkPriority Priority { get; set; } = WorkPriority.Medium;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;
    public HoldReason? HoldReason { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid? TradeId { get; set; }
    public Trade? Trade { get; set; }
    public Guid? WorkRequestId { get; set; }
    public Guid? PmScheduleId { get; set; }
    public Guid? PmTaskId { get; set; }
    /// <summary>Target start of the PM occurrence that generated this work order (prevents duplicate generation).</summary>
    public DateOnly? PmDueDate { get; set; }
    public Guid? FailureCodeId { get; set; }
    public FailureCode? FailureCode { get; set; }

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? EstimatedHours { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Legacy "Action Taken".</summary>
    public string? CompletionNotes { get; set; }
    public string? CauseNotes { get; set; }
    /// <summary>Legacy "Prevention Taken".</summary>
    public string? PreventionNotes { get; set; }
    /// <summary>Legacy "Emp/Req. Name" — who asked for the work when there is no formal request.</summary>
    public string? RequestedBy { get; set; }
    public bool AssetWasDown { get; set; }
    public decimal? DowntimeHours { get; set; }

    public List<WorkOrderAssignee> Assignees { get; set; } = [];
}

public class WorkOrderAssignee
{
    public Guid WorkOrderId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
}

public class WorkOrderTask : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public int Sequence { get; set; }
    public required string Description { get; set; }
    public bool IsDone { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? DoneBy { get; set; }
}

public class WorkOrderLabor : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>Rates are snapshotted when logged so later rate changes don't rewrite history.</summary>
    public decimal Rate { get; set; }
    public decimal OvertimeRate { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public string? LegacyRef { get; set; }
}

/// <summary>Non-inventory costs: contractor invoices, rentals, purchases (legacy "direct").</summary>
public class WorkOrderCost : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public required string Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public string? Vendor { get; set; }
    /// <summary>PO number or other reference (legacy direct issue "PO No").</summary>
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}

public class WorkOrderComment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public required string Body { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Every status transition (legacy WOStatusAudit) — drives response/repair-time KPIs.</summary>
public class WorkOrderStatusChange : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrderStatus? FromStatus { get; set; }
    public WorkOrderStatus ToStatus { get; set; }
    public HoldReason? HoldReason { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedById { get; set; }
    public string? ChangedByName { get; set; }
    public string? LegacyRef { get; set; }
}

public static class WorkOrderWorkflow
{
    private static readonly Dictionary<WorkOrderStatus, WorkOrderStatus[]> Allowed = new()
    {
        [WorkOrderStatus.Open] = [WorkOrderStatus.InProgress, WorkOrderStatus.OnHold, WorkOrderStatus.Completed, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.InProgress] = [WorkOrderStatus.OnHold, WorkOrderStatus.Completed, WorkOrderStatus.Cancelled, WorkOrderStatus.Open],
        [WorkOrderStatus.OnHold] = [WorkOrderStatus.InProgress, WorkOrderStatus.Open, WorkOrderStatus.Completed, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.Completed] = [WorkOrderStatus.Closed, WorkOrderStatus.InProgress],
        [WorkOrderStatus.Closed] = [WorkOrderStatus.InProgress],
        [WorkOrderStatus.Cancelled] = [WorkOrderStatus.Open],
    };

    public static IReadOnlyList<WorkOrderStatus> NextStatuses(WorkOrderStatus from) => Allowed[from];

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to) => Allowed[from].Contains(to);

    /// <summary>Closing, cancelling and reopening closed/cancelled work need the elevated "close" permission.</summary>
    public static bool RequiresClosePermission(WorkOrderStatus from, WorkOrderStatus to) =>
        to is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled || from is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled;

    public static bool IsActive(WorkOrderStatus s) => s is WorkOrderStatus.Open or WorkOrderStatus.InProgress or WorkOrderStatus.OnHold;
}
