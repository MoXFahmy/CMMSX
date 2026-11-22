using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>People limited to some sites see only those sites' records, everywhere (0.9.0).</summary>
public class SiteAccessTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    public const string UserPassword = "Passw0rd!u";

    public static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>One site's records, for checking who sees what.</summary>
    public sealed record SiteSet(Guid Site, Guid Location, Guid Asset, string AssetNumber, Guid Request, Guid WorkOrder, Guid Schedule, Guid Store, Guid Po);

    public sealed record World(HttpClient Admin, SiteSet A, SiteSet B, Guid Supplier, Guid Task);

    public static async Task<World> WorldAsync(ApiFixture fx, string org)
    {
        var admin = fx.Client();
        await Ok(await admin.PostAsJsonAsync("/api/auth/register", new { organizationName = org, fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var supplier = Id(await Ok(await admin.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var task = Id(await Ok(await admin.PostAsJsonAsync("/api/pm-tasks", new { code = "W1", name = "Weekly check", steps = new[] { "Look" } })));
        async Task<SiteSet> SetAsync(string code)
        {
            var site = Id(await Ok(await admin.PostAsJsonAsync("/api/sites", new { code, name = $"Site {code}", isActive = true })));
            var loc = Id(await Ok(await admin.PostAsJsonAsync("/api/locations", new { siteId = site, code = $"L{code}", name = $"Hall {code}", type = "Building", criticality = "Medium", isActive = true })));
            var asset = await Ok(await admin.PostAsJsonAsync("/api/assets", new { name = $"Pump {code}", siteId = site, locationId = loc, status = "Active", criticality = "High", warrantyType = "None" }));
            var req = Id(await Ok(await admin.PostAsJsonAsync("/api/work-requests", new { title = $"Leak {code}", siteId = site, locationId = loc, priority = "High", type = "Corrective" })));
            var wo = Id(await Ok(await admin.PostAsJsonAsync("/api/work-orders", new { title = $"Fix {code}", siteId = site, locationId = loc, assetId = Id(asset), type = "Corrective", priority = "Medium" })));
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var schedule = Id(await Ok(await admin.PostAsJsonAsync("/api/pm-schedules", new
            {
                name = $"PM {code}", siteId = site, locationId = loc, assetId = Id(asset), priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled",
                frequency = 1, unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Iso(today), timeBased = false, extraTasks = Array.Empty<object>(),
            })));
            var store = Id(await Ok(await admin.PostAsJsonAsync("/api/stores", new { code = $"ST{code}", name = $"Store {code}", siteId = site })));
            var po = Id(await Ok(await admin.PostAsJsonAsync("/api/purchase-orders", new
            {
                siteId = site, supplierId = supplier, currency = "USD", exchangeRate = 1, taxPercent = 0, shippingCost = 0,
                lines = new[] { new { description = $"Seal {code}", quantity = 1, unitPrice = 10, discountPercent = 0 } },
            })));
            return new SiteSet(site, loc, Id(asset), asset.GetProperty("assetNumber").GetString()!, req, wo, schedule, store, po);
        }
        var a = await SetAsync("A");
        var b = await SetAsync("B");
        return new World(admin, a, b, supplier, task);
    }

    /// <summary>A signed-in user with <paramref name="role"/>, limited as given (null = no limit).</summary>
    public static async Task<(HttpClient Client, Guid Id)> UserAsync(ApiFixture fx, HttpClient admin, string role, Guid[]? siteIds = null, Guid[]? locationIds = null)
    {
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        var user = await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = UserPassword, siteIds, locationIds }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = UserPassword, rememberMe = false }));
        return (c, Id(user));
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient c, string path)
    {
        var e = await Ok(await c.GetAsync(path));
        var items = e.ValueKind == JsonValueKind.Array ? e : e.GetProperty("items");
        return items.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
    }

    [SkippableFact]
    public async Task A_user_with_no_sites_sees_every_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "All Sites Co");
        Assert.Equal(2, (await IdsAsync(w.Admin, "/api/sites")).Count);
        var (manager, _) = await UserAsync(fx, w.Admin, "Maintenance Manager");
        Assert.Equal(2, (await IdsAsync(manager, "/api/work-orders")).Count);
    }

    [SkippableFact]
    public async Task A_site_limited_user_sees_only_their_sites_records()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Limited Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal([w.A.Site], await IdsAsync(c, "/api/sites"));
        Assert.Equal([w.A.Location], await IdsAsync(c, "/api/locations"));
        Assert.Equal([w.A.Asset], await IdsAsync(c, "/api/assets"));
        Assert.Equal([w.A.Request], await IdsAsync(c, "/api/work-requests"));
        Assert.Equal([w.A.WorkOrder], await IdsAsync(c, "/api/work-orders"));
        Assert.Equal([w.A.Schedule], await IdsAsync(c, "/api/pm-schedules"));
        Assert.Equal([w.A.Store], await IdsAsync(c, "/api/stores"));
        Assert.Equal([w.A.Po], await IdsAsync(c, "/api/purchase-orders"));
    }

    [SkippableFact]
    public async Task Another_sites_record_answers_not_found()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Not Found Co");
        using var form = new MultipartFormDataContent { { new StringContent("Asset"), "entityType" }, { new StringContent(w.B.Asset.ToString()), "entityId" } };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("secret"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "files", "b.txt");
        var attachment = (await Ok(await w.Admin.PostAsync("/api/attachments", form)))[0].GetProperty("id").GetGuid();

        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/assets/{w.B.Asset}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/work-orders/{w.B.WorkOrder}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/purchase-orders/{w.B.Po}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/attachments?entityType=Asset&entityId={w.B.Asset}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/audit?entityType=WorkOrder&entityId={w.B.WorkOrder}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/attachments/{attachment}/content")).StatusCode);
        // Their own site's records still answer.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/audit?entityType=WorkOrder&entityId={w.A.WorkOrder}")).StatusCode);
    }

    [SkippableFact]
    public async Task Reports_and_exports_count_only_visible_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Report Co");
        var yesterday = Iso(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Late B", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "High", dueDate = yesterday }));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);

        var dash = await Ok(await c.GetAsync("/api/dashboard"));
        Assert.Equal(1, dash.GetProperty("totals").GetProperty("sites").GetInt32());
        Assert.Equal(1, dash.GetProperty("work").GetProperty("open").GetInt32());
        var csv = Encoding.UTF8.GetString(await (await c.GetAsync("/api/work-orders/export?format=csv&c=title&t=Title")).Content.ReadAsByteArrayAsync());
        Assert.Contains("Fix A", csv);
        Assert.DoesNotContain("Fix B", csv);
        Assert.DoesNotContain("Late B", csv);
        Assert.Equal(0, (await Ok(await c.GetAsync("/api/alerts"))).GetProperty("overdueWorkOrders").GetInt32());
        Assert.Equal(1, (await Ok(await w.Admin.GetAsync("/api/alerts"))).GetProperty("overdueWorkOrders").GetInt32());
    }

    [SkippableFact]
    public async Task Child_rows_follow_their_parent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Child Co");
        var part = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P1", name = "Filter", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = w.A.Store, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 },
                             new { storeId = w.B.Store, bin = "B1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var levels = (await Ok(await c.GetAsync($"/api/parts/{part}"))).GetProperty("levels");
        Assert.Equal([w.A.Store], levels.EnumerateArray().Select(l => l.GetProperty("storeId").GetGuid()).ToArray());
        Assert.Equal(2, (await Ok(await w.Admin.GetAsync($"/api/parts/{part}"))).GetProperty("levels").GetArrayLength());
    }

    [SkippableFact]
    public async Task A_limited_user_cannot_create_in_another_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Create Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var r = await c.PostAsJsonAsync("/api/work-orders", new { title = "Sneaky", siteId = w.B.Site, type = "Corrective", priority = "Low" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("siteId", await r.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Codes_stay_unique_across_sites_the_user_cannot_see()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Unique Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var r = await c.PostAsJsonAsync("/api/assets", new { assetNumber = w.B.AssetNumber, name = "Copy", siteId = w.A.Site, status = "Active", criticality = "Low", warrantyType = "None" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("This asset number is already used in a site you can't see.", await r.Content.ReadAsStringAsync());
        // Numbers the app makes skip numbers it can't see too.
        var made = await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Next", siteId = w.A.Site, type = "Corrective", priority = "Low" }));
        Assert.False(string.IsNullOrEmpty(made.GetProperty("number").GetString()));
    }

    [SkippableFact]
    public async Task A_limited_admin_can_only_give_access_inside_their_own()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Admin Limit Co");
        var (limitedAdmin, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var roles = await Ok(await w.Admin.GetAsync("/api/roles"));
        var viewer = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Viewer").GetProperty("id").GetGuid();

        var all = await limitedAdmin.PostAsJsonAsync("/api/users", new { fullName = "Everyone", email = $"{Guid.NewGuid():N}@test.local", isActive = true, roleIds = new[] { viewer }, password = UserPassword, siteIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.BadRequest, all.StatusCode);
        Assert.Contains("Choose at least one of your sites.", await all.Content.ReadAsStringAsync());
        var other = await limitedAdmin.PostAsJsonAsync("/api/users", new { fullName = "Other", email = $"{Guid.NewGuid():N}@test.local", isActive = true, roleIds = new[] { viewer }, password = UserPassword, siteIds = new[] { w.B.Site } });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        // A user who already has site B can still be edited (name only) by the limited admin.
        var email = $"{Guid.NewGuid():N}@test.local";
        var bUser = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "B person", email, isActive = true, roleIds = new[] { viewer }, password = UserPassword, siteIds = new[] { w.B.Site } })));
        await Ok(await limitedAdmin.PutAsJsonAsync($"/api/users/{bUser}", new { fullName = "B person renamed", email, isActive = true, roleIds = new[] { viewer }, siteIds = new[] { w.B.Site } }));
        var listed = (await Ok(await w.Admin.GetAsync("/api/users"))).EnumerateArray().First(u => u.GetProperty("id").GetGuid() == bUser);
        Assert.Equal([w.B.Site], listed.GetProperty("siteIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
    }

    [SkippableFact]
    public async Task Changing_access_applies_at_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Change Co");
        var roles = await Ok(await w.Admin.GetAsync("/api/roles"));
        var manager = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Maintenance Manager").GetProperty("id").GetGuid();
        var (c, id) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        Assert.Equal([w.A.Asset], await IdsAsync(c, "/api/assets"));
        var me = (await Ok(await w.Admin.GetAsync("/api/users"))).EnumerateArray().First(u => u.GetProperty("id").GetGuid() == id);
        await Ok(await w.Admin.PutAsJsonAsync($"/api/users/{id}", new { fullName = "Moved", email = me.GetProperty("email").GetString(), isActive = true, roleIds = new[] { manager }, siteIds = new[] { w.B.Site } }));
        Assert.Equal([w.B.Asset], await IdsAsync(c, "/api/assets"));
    }

    [SkippableFact]
    public async Task Pm_generation_is_not_limited_by_any_user()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Pm Co");
        await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        // The hourly scheduler runs with no user: it creates work at both sites.
        var scheduler = fx.Factory!.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<FourC.Cmms.Api.Features.PmSchedulerService>().Single();
        await scheduler.RunOnceAsync(default);
        var pmWork = (await Ok(await w.Admin.GetAsync("/api/work-orders?type=Preventive"))).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("siteId").GetGuid()).Distinct().ToList();
        Assert.Contains(w.A.Site, pmWork);
        Assert.Contains(w.B.Site, pmWork);
    }

    [SkippableFact]
    public async Task Me_returns_the_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Me Co");
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var access = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("access");
        Assert.True(access.GetProperty("limited").GetBoolean());
        Assert.Equal([w.A.Site], access.GetProperty("sites").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToArray());
        var adminAccess = (await Ok(await w.Admin.GetAsync("/api/auth/me"))).GetProperty("access");
        Assert.False(adminAccess.GetProperty("limited").GetBoolean());
        Assert.Equal(2, adminAccess.GetProperty("sites").GetArrayLength());
    }
}
