using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Settings;

/// <summary>Per-tenant counters for human-readable numbers (assets, work orders, requests...).</summary>
public class NumberSequence : ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public long NextValue { get; set; } = 1;
}
