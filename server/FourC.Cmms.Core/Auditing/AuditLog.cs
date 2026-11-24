using System.Text.Json;
using FourC.Cmms.Core.Data;

namespace FourC.Cmms.Core.Auditing;

/// <summary>
/// Change entries written by hand (0.25.0): a summary of a change made in bulk, and security events. Their source and change
/// number are filled in by the save that writes them; the caller saves.
/// </summary>
public static class AuditLog
{
    /// <summary>One entry on the record that was changed, saying what followed it ("Brand merged into Grundfos: 14 assets").</summary>
    public static void Summary(CmmsDbContext db, string entityType, Guid entityId, string action, string summary) =>
        Event(db, entityType, entityId, action, new Dictionary<string, object?> { ["Summary"] = new { to = summary } });

    /// <summary>
    /// An entry for an event or a change the save pipeline can't see. The organization and person default to the context's;
    /// sign-ins (no one signed in yet) pass them.
    /// </summary>
    public static void Event(CmmsDbContext db, string entityType, Guid entityId, string action, object? changes = null,
        Guid? tenantId = null, Guid? userId = null, string? userName = null)
    {
        if ((tenantId ?? db.Actor.TenantId) is not { } tid || tid == Guid.Empty) return;
        db.AuditEntries.Add(new AuditEntry
        {
            TenantId = tid, EntityType = entityType, EntityId = entityId, Action = action,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, CmmsDbContext.Json),
            UserId = userId ?? db.Actor.UserId, UserName = userName ?? db.Actor.UserName, Timestamp = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>"1 asset" / "14 assets".</summary>
    public static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
