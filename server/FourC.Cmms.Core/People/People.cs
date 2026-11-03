using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.People;

/// <summary>Craft/discipline (legacy worktrade): Mechanical, Electrical, HVAC...</summary>
public class Trade : TenantEntity, ICodeLookup
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A member of the maintenance workforce (in-house technician or contractor). Separate from login accounts:
/// many technicians never sign in, and an employee can optionally be linked to a user.
/// </summary>
public class Employee : TenantEntity
{
    public required string Code { get; set; }
    public required string FullName { get; set; }
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<Guid> TradeIds { get; set; } = [];
    /// <summary>Sites this person works at; empty = all sites.</summary>
    public List<Guid> SiteIds { get; set; } = [];
    public decimal? HourlyRate { get; set; }
    public decimal? OvertimeRate { get; set; }
    public bool IsContractor { get; set; }
    public string? Company { get; set; }
    public Guid? UserId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
