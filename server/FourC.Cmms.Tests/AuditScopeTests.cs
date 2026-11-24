using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Tests;

/// <summary>Which records the activity log covers, their parents and masking (0.25.0); no database needed.</summary>
public class AuditScopeTests
{
    private static CmmsDbContext Model() =>
        new(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none").Options, new FixedTenantContext(null));

    [Fact]
    public void Every_entity_is_recorded_or_excluded()
    {
        using var db = Model();
        var unlisted = db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType).Select(t => t.ClrType).Distinct()
            .Where(t => !typeof(TenantEntity).IsAssignableFrom(t) && !AuditScope.IsRecorded(t) && !AuditScope.Excluded.Contains(t))
            .Select(t => $"{t.Name} is neither recorded nor excluded from the activity log").ToList();
        Assert.Empty(unlisted);
        Assert.All(db.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(TenantEntity).IsAssignableFrom(t)), t => Assert.True(AuditScope.IsRecorded(t), t.Name));
    }

    [Fact]
    public void Children_name_their_parent()
    {
        var w = Guid.NewGuid();
        var a = Guid.NewGuid();
        Assert.Equal(("WorkOrder", w), AuditScope.ParentOf(new WorkOrderLabor { WorkOrderId = w }));
        Assert.Equal(("Asset", a), AuditScope.ParentOf(new Attachment { EntityType = "Asset", EntityId = a, FileName = "x", ContentType = "image/png", StorageKey = "k" }));
        Assert.Null(AuditScope.ParentOf(new WorkOrder { Number = "WO-1", Title = "T" }));
    }

    [Fact]
    public void Secrets_are_masked()
    {
        Assert.True(AuditScope.IsSensitive("PasswordHash"));
        Assert.True(AuditScope.IsSensitive("PushToken"));
        Assert.True(AuditScope.IsSensitive("ApiKeyHash"));
        Assert.True(AuditScope.IsSensitive("ClientSecret"));
        Assert.False(AuditScope.IsSensitive("FullName"));
    }
}
