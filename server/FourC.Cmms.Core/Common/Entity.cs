namespace FourC.Cmms.Core.Common;

/// <summary>Base type for every persisted entity.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>An entity that belongs to exactly one tenant. Queries are filtered automatically.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedById { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedById { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Standard tenant-scoped business record: audited, soft-deletable, optimistic concurrency.</summary>
public abstract class TenantEntity : Entity, ITenantOwned, IAuditable, ISoftDeletable
{
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    /// <summary>Key of the source record when imported from a legacy 4CSQL database (idempotent re-imports).</summary>
    public string? LegacyRef { get; set; }
}

/// <summary>Who is acting, and for which tenant. Implemented per-request by the API and explicitly by tools.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    /// <summary>Sites this request may see; null = every site (no limit, and every background job).</summary>
    IReadOnlyList<Guid>? SiteIds => null;
    /// <summary>Location paths (Location.Path) this request may see under; null = no branch limit, empty = nothing.
    /// Never set without <see cref="SiteIds"/>.</summary>
    IReadOnlyList<string>? BranchPaths => null;
    /// <summary>Where changes come from (0.25.0): Web, Phone, Import or Job.</summary>
    string Source => "Web";
    /// <summary>The change number everything this request or run saves shares; empty = one per database context.</summary>
    Guid ChangeId => Guid.Empty;
}

/// <summary>A tenant context set in code: background jobs ("Job", the default), tools, and a few anonymous web pages.</summary>
public sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null, string? userName = null, string source = "Job",
    IReadOnlyList<Guid>? siteIds = null, IReadOnlyList<string>? branchPaths = null) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
    public Guid? UserId { get; } = userId;
    public string? UserName { get; } = userName;
    public string Source { get; } = source;
    /// <summary>A background job running for a site-limited person (0.26.0 imports) sees what they see.</summary>
    public IReadOnlyList<Guid>? SiteIds { get; } = siteIds;
    public IReadOnlyList<string>? BranchPaths { get; } = branchPaths;
    public Guid ChangeId { get; } = Guid.CreateVersion7();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Business rule violation; surfaced to clients as HTTP 400 with the message.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Simple per-tenant lookup with a code and a name (trades, failure codes, categories...).</summary>
public interface ICodeLookup
{
    Guid Id { get; }
    string Code { get; set; }
    string Name { get; set; }
    bool IsActive { get; set; }
}
