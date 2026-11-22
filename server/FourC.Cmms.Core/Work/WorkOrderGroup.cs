using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Locations;

namespace FourC.Cmms.Core.Work;

/// <summary>
/// A group of work orders (0.12.0): one per asset of a multi-asset PM occurrence, managed together. The group is not a work
/// order itself, so it never counts as work; its work orders do.
/// </summary>
public class WorkOrderGroup : TenantEntity
{
    public required string Number { get; set; }
    public required string Title { get; set; }
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? PmScheduleId { get; set; }
    public Guid? PmTaskId { get; set; }
    /// <summary>Target start of the PM occurrence that generated it (prevents duplicate generation).</summary>
    public DateOnly? PmDueDate { get; set; }
    public DateOnly? PlannedStart { get; set; }
    /// <summary>Its work orders' due date; changing it changes theirs (unfinished ones).</summary>
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? DueAt { get; set; }
}

/// <summary>A comment on a group, shown on each of its work orders.</summary>
public class WorkOrderGroupComment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid GroupId { get; set; }
    public required string Body { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
}
