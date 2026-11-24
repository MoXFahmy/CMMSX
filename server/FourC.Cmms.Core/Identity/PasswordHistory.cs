using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Identity;

/// <summary>
/// A password a person set (0.25.0), as its hash only, so a new one can't repeat one of the last N. The last 24 are kept; they are
/// never shown, logged or returned.
/// </summary>
public class PasswordHistory : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset SetAt { get; set; }
}
