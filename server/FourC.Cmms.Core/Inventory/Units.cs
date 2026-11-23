using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Inventory;

/// <summary>
/// A unit of measure (0.21.0): picked for parts, measurement points and number steps, which keep the symbol as their text.
/// Symbols are unique per organization and case matters ("mW" isn't "MW").
/// </summary>
public class Unit : TenantEntity
{
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public string? NameAr { get; set; }
    public bool IsActive { get; set; } = true;
}
