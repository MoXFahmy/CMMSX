using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;

namespace FourC.Cmms.Tests;

public class StockCountTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<(Loop.Org Org, Guid Store, Guid P1, Guid P2, Guid Count)> CountAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        var p1 = await PartAsync(org.Admin, store, "P1");
        var p2 = await PartAsync(org.Admin, store, "P2");
        await ReceiveAsync(org.Admin, p1, store, 5);
        await ReceiveAsync(org.Admin, p2, store, 3);
        await EmployeeAsync(org.Admin, "ME", userId: org.AdminId);
        var count = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/stock-counts", new { storeId = store, name = "November" })));
        return (org, store, p1, p2, count);
    }

    private static JsonElement Line(JsonElement count, Guid part) => count.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("partId").GetGuid() == part);

    [SkippableFact]
    public async Task Count_on_the_phone_then_post()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, p1, p2, count) = await CountAsync();
        Assert.Contains((await Ok(await org.Admin.GetAsync("/api/tech/stock"))).GetProperty("counts").EnumerateArray(), c => Id(c) == count);
        Assert.Equal("done", Status(await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = 5 })));
        Assert.Equal("done", Status(await ActAsync(org.Admin, "count.line", new { countId = count, partId = p2, quantity = 1 })));
        var detail = await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}"));
        Assert.Equal(-2m, Line(detail, p2).GetProperty("difference").GetDecimal());
        Assert.Equal(0m, Line(detail, p1).GetProperty("difference").GetDecimal());
        var posted = await Ok(await org.Admin.PostAsync($"/api/stock-counts/{count}/post", null));
        Assert.Equal(1, posted.GetProperty("adjusted").GetInt32());
        Assert.Equal(1m, await OnHandAsync(org.Admin, p2, store));
        Assert.Equal("Posted", (await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}"))).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Stock_moved_after_a_part_was_counted_is_kept_when_posting()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, p1, _, count) = await CountAsync();
        // Counted 4 on the shelf (5 on record), then 5 more were received before the office posted.
        await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = 4 }, at: DateTimeOffset.UtcNow);
        await Task.Delay(50);
        await ReceiveAsync(org.Admin, p1, store, 5);
        var line = Line(await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}")), p1);
        Assert.Equal(5m, line.GetProperty("movedSince").GetDecimal());
        Assert.Equal(-1m, line.GetProperty("difference").GetDecimal());
        await Ok(await org.Admin.PostAsync($"/api/stock-counts/{count}/post", null));
        Assert.Equal(9m, await OnHandAsync(org.Admin, p1, store));
    }

    [SkippableFact]
    public async Task Shelf_life_parts_are_counted_by_batch_in_the_office()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, _, _, count) = await CountAsync();
        var oil = await PartAsync(org.Admin, store, "OIL-1", shelf: true);
        var answer = await ActAsync(org.Admin, "count.line", new { countId = count, partId = oil, quantity = 3 });
        Assert.Equal("rejected", Status(answer));
        Assert.Equal("Count shelf-life parts by batch in the office.", Message(answer));
    }

    [SkippableFact]
    public async Task The_latest_count_of_a_part_wins()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, p1, _, count) = await CountAsync();
        var now = DateTimeOffset.UtcNow;
        await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = 4 }, at: now.AddMinutes(-1));
        await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = 9 }, at: now.AddMinutes(-10)); // counted earlier, arrived later
        Assert.Equal(4m, Line(await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}")), p1).GetProperty("counted").GetDecimal());
    }

    [SkippableFact]
    public async Task Lines_after_posting_are_rejected()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, store, p1, _, count) = await CountAsync();
        await Ok(await org.Admin.PostAsync($"/api/stock-counts/{count}/post", null));
        var answer = await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = 1 });
        Assert.Equal("rejected", Status(answer));
        Assert.Equal("This count was already posted or cancelled.", Message(answer));
        Assert.Equal(5m, await OnHandAsync(org.Admin, p1, store));
    }

    [SkippableFact]
    public async Task Counts_follow_store_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, p1, _, count) = await CountAsync();
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var tech = await UserAsync(fx, org.Admin, "Technician", siteIds: [other]);
        await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "T2", fullName = "Tech", userId = tech.Id, siteIds = new[] { other }, isContractor = false }));
        Assert.DoesNotContain((await Ok(await tech.Client.GetAsync("/api/stock-counts"))).EnumerateArray(), c => Id(c) == count);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.Client.GetAsync($"/api/stock-counts/{count}")).StatusCode);
        Assert.Equal("rejected", Status(await ActAsync(tech.Client, "count.line", new { countId = count, partId = p1, quantity = 1 })));
    }

    [SkippableFact]
    public async Task Negative_counts_are_refused_and_counts_can_be_cancelled()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (org, _, p1, _, count) = await CountAsync();
        Assert.Equal("rejected", Status(await ActAsync(org.Admin, "count.line", new { countId = count, partId = p1, quantity = -1 })));
        await Ok(await org.Admin.PostAsync($"/api/stock-counts/{count}/cancel", null));
        Assert.Equal("Cancelled", (await Ok(await org.Admin.GetAsync($"/api/stock-counts/{count}"))).GetProperty("status").GetString());
        Assert.DoesNotContain((await Ok(await org.Admin.GetAsync("/api/tech/stock"))).GetProperty("counts").EnumerateArray(), c => Id(c) == count);
    }
}
