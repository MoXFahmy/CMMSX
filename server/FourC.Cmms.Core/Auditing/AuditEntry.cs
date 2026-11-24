using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Auditing;

public class AuditEntry : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    /// <summary>Created, Updated, Deleted.</summary>
    public required string Action { get; set; }
    /// <summary>JSON: { "Field": { "from": x, "to": y } }</summary>
    public string? Changes { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Where the change came from (0.25.0): Web, Phone, Import or Job (work done without a signed-in person).</summary>
    public string Source { get; set; } = "Web";
    /// <summary>One per web request, phone change or job run: everything one action saved shares it.</summary>
    public Guid ChangeId { get; set; }
    /// <summary>The record a child row belongs to (a work order's labour line → the work order); null for a record of its own.</summary>
    public string? ParentType { get; set; }
    public Guid? ParentId { get; set; }
}
