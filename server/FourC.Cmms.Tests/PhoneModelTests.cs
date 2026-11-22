using System.Net.Http.Json;
using FourC.Cmms.Core.Inventory;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class PhoneModelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task The_new_permission_is_listed_and_given_to_managers()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var all = await Ok(await org.Admin.GetAsync("/api/roles/permissions"));
        Assert.Contains(all.EnumerateArray(), p => p.GetProperty("code").GetString() == "workorders.logForOthers");
        var roles = await Ok(await org.Admin.GetAsync("/api/roles"));
        var manager = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Maintenance Manager");
        Assert.Contains("workorders.logForOthers", manager.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
        var tech = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Technician");
        Assert.DoesNotContain("workorders.logForOthers", tech.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
    }

    [SkippableFact]
    public async Task A_stock_count_keeps_one_line_per_part()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        var part = await PartAsync(org.Admin, store);
        var id = await DbAsync(fx, async db =>
        {
            var count = new StockCount { TenantId = org.Tenant, StoreId = store, Name = "November count" };
            count.Lines.Add(new StockCountLine { TenantId = org.Tenant, PartId = part, CountedQuantity = 4, CountedAt = DateTimeOffset.UtcNow });
            db.StockCounts.Add(count);
            await db.SaveChangesAsync();
            return count.Id;
        });
        var saved = await DbAsync(fx, db => db.StockCounts.IgnoreQueryFilters().Include(c => c.Lines).FirstAsync(c => c.Id == id));
        Assert.Equal(StockCountStatus.Open, saved.Status);
        Assert.Equal(4, Assert.Single(saved.Lines).CountedQuantity);
        await Assert.ThrowsAsync<DbUpdateException>(() => DbAsync(fx, async db =>
        {
            db.StockCountLines.Add(new StockCountLine { TenantId = org.Tenant, StockCountId = id, PartId = part, CountedQuantity = 5, CountedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        }));
    }

    [Theory]
    [InlineData("Only 2 in the store; the rest is on the store keeper's list.")]
    [InlineData("This took parts reserved for WO-00001.")]
    [InlineData("Added to the job's parts; the store keeper issues them.")]
    [InlineData("This count was already posted or cancelled.")]
    [InlineData("The asset isn't down.")]
    [InlineData("The asset is already down.")]
    [InlineData("The time can't be in the future.")]
    [InlineData("clip.mp4 is larger than 50 MB.")]
    [InlineData("New job: WO-00001")]
    [InlineData("Request approved: WR-00001")]
    [InlineData("The phone can start, hold, complete or cancel work only.")]
    [InlineData("You can't cancel work orders.")]
    public void New_phone_messages_have_Arabic(string english) =>
        Assert.NotEqual(english, FourC.Cmms.Api.Infrastructure.Translator.T(english, "ar"));
}
