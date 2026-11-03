using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Files;

public class Attachment : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required string StorageKey { get; set; }
    public string? Description { get; set; }
    public bool IsPrimaryImage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
