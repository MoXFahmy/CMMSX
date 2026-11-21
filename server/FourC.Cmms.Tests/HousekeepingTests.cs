using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

public class HousekeepingTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Records_of_phone_changes_older_than_90_days_are_trimmed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        TechAction Row(int daysAgo) => new() { TenantId = tenant, UserId = Guid.NewGuid(), ClientId = Guid.NewGuid(), Type = "comment.add", Status = "done",
            At = now.AddDays(-daysAgo), ReceivedAt = now.AddDays(-daysAgo) };
        var old = Row(91);
        var recent = Row(89);
        db.TechActions.AddRange(old, recent);
        await db.SaveChangesAsync();

        await Housekeeping.TrimAsync(db, now);

        var left = await db.TechActions.IgnoreQueryFilters().Where(a => a.TenantId == tenant).Select(a => a.Id).ToListAsync();
        Assert.Equal([recent.Id], left);
    }

    private sealed class FakeStorage : IFileStorage
    {
        public readonly HashSet<string> Keys = [];
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) { Keys.Add(key); return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string key, CancellationToken ct = default) { Keys.Remove(key); return Task.CompletedTask; }
    }

    [Fact]
    public async Task A_file_whose_record_could_not_be_saved_is_removed_again()
    {
        var storage = new FakeStorage();
        await storage.SaveAsync("t/workorder/1/a", Stream.Null);
        await Assert.ThrowsAsync<DbUpdateException>(() => Housekeeping.SaveOrRemoveFileAsync(storage, "t/workorder/1/a", () => throw new DbUpdateException("boom")));
        Assert.Empty(storage.Keys);
    }

    [Fact]
    public async Task A_file_whose_record_was_saved_stays()
    {
        var storage = new FakeStorage();
        await storage.SaveAsync("t/workorder/1/a", Stream.Null);
        await Housekeeping.SaveOrRemoveFileAsync(storage, "t/workorder/1/a", () => Task.FromResult(1));
        Assert.Single(storage.Keys);
    }
}
