using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Identity;

/// <summary>
/// A signed-in device (browser or app). The auth cookie carries its id; signing it out (RevokedAt) stops the cookie
/// working, so a lost phone can be cut off.
/// </summary>
public class UserSession : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>"Android app 0.4.0", "Chrome on Windows"… (no IP address is kept).</summary>
    public required string DeviceName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
