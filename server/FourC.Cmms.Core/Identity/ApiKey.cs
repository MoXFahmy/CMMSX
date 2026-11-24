using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Identity;

/// <summary>
/// An API key (0.26.0): acts as its service account. Only a hash is kept; the key itself is shown once when it's made. The
/// first characters after "cmx_" (<see cref="Prefix"/>) find it and show in lists.
/// </summary>
public class ApiKey : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ServiceAccountId { get; set; }
    public required string Name { get; set; }
    public required string Prefix { get; set; }
    /// <summary>SHA-256 of the whole key, as lower-case hex.</summary>
    public required string Hash { get; set; }
    /// <summary>May only read (GET).</summary>
    public bool ReadOnly { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    /// <summary>Kept to the minute.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedById { get; set; }
}
