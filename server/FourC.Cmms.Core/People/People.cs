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
    /// <summary>Free-text company name (before 0.17.0); shown when no company is linked.</summary>
    public string? Company { get; set; }
    /// <summary>The contractor company this person works for (0.17.0): their logged hours count as contractor cost.</summary>
    public Guid? CompanyId { get; set; }
    public FourC.Cmms.Core.Purchasing.Supplier? CompanyRecord { get; set; }
    public Guid? UserId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>This person's weekly working hours; null = their site's (when they work at one site that has its own), else the organization's.</summary>
    public List<WorkingDay>? WorkingHours { get; set; }
}

/// <summary>A named group of technicians that work orders and PM schedules can be given to.</summary>
public class Team : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>The site the team works at; null = every site.</summary>
    public Guid? SiteId { get; set; }
    public Locations.Site? Site { get; set; }
    public string? Color { get; set; }
    /// <summary>Employees in the team.</summary>
    public List<Guid> MemberIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
}
