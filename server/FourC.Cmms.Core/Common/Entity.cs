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
}

public sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null, string? userName = null) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
    public Guid? UserId { get; } = userId;
    public string? UserName { get; } = userName;
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
