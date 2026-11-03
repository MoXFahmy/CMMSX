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
}
