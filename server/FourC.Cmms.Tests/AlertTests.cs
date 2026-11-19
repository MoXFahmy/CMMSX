using System.Net.Http.Json;
using System.Text.Json;

namespace FourC.Cmms.Tests;

public class AlertTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [SkippableFact]
    public async Task Alerts_count_only_what_the_user_may_see()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var admin = fx.Client();
        await Ok(await admin.PostAsJsonAsync("/api/auth/register", new { organizationName = "Alert Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var me = (await Ok(await admin.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var site = Id(await Ok(await admin.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var emp = Id(await Ok(await admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Admin as technician", isContractor = false, userId = me })));
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = "Late job", type = "Corrective", priority = "High", siteId = site, dueDate = yesterday, assigneeIds = new[] { emp } }));
        await Ok(await admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = site, priority = "High", type = "Corrective" }));
        var store = Id(await Ok(await admin.PostAsJsonAsync("/api/stores", new { code = "MAIN", name = "Main", siteId = site })));
        await Ok(await admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P1", name = "Filter", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = store, bin = "A1", minLevel = 0, reorderLevel = 5, maxLevel = 20, reorderQuantity = 10 } },
        }));

        var alerts = await Ok(await admin.GetAsync("/api/alerts"));
        Assert.Equal(1, alerts.GetProperty("overdueWorkOrders").GetInt32());
        Assert.Equal(1, alerts.GetProperty("myOverdueWorkOrders").GetInt32());
        Assert.Equal(1, alerts.GetProperty("requestsToTriage").GetInt32());
        Assert.Equal(1, alerts.GetProperty("partsBelowReorder").GetInt32());

        // A requester may see none of these.
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var requesterRole = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Requester").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Requester", email, isActive = true, roleIds = new[] { requesterRole }, password = "Passw0rd!u" }));
        var requester = fx.Client();
        await Ok(await requester.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        var none = await Ok(await requester.GetAsync("/api/alerts"));
        foreach (var key in new[] { "overdueWorkOrders", "myOverdueWorkOrders", "requestsToTriage", "partsBelowReorder" })
            Assert.Equal(JsonValueKind.Null, none.GetProperty(key).ValueKind);
    }
}
