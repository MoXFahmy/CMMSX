using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>What the 0.9.0 final review found: organization-wide decisions look past the site limits; limited people stay inside them.</summary>
public class ReviewFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static async Task<Guid> PartAsync(HttpClient admin, World w, string no) =>
        Id(await Ok(await admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = no, name = "Filter " + no, unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = w.A.Store, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 },
                             new { storeId = w.B.Store, bin = "B1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));

    private static async Task<Guid> RoleAsync(HttpClient admin, string name) =>
        (await Ok(await admin.GetAsync("/api/roles"))).EnumerateArray().First(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private async Task<List<OutboundEmail>> QueuedAsync(Guid tenant)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant).ToListAsync();
    }

    // ---- Critical ----

    [SkippableFact]
    public async Task A_limited_receipt_keeps_the_organizations_average_cost()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Avg Co");
        var part = await PartAsync(w.Admin, w, "AVG1");
        await Ok(await w.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = w.B.Store, quantity = 1000, unitCost = 10 }));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = w.A.Store, quantity = 10, unitCost = 20 }));
        var avg = (await Ok(await w.Admin.GetAsync($"/api/parts/{part}"))).GetProperty("averageCost").GetDecimal();
        Assert.InRange(avg, 10.09m, 10.11m); // (1000 × 10 + 10 × 20) / 1010
    }

    [SkippableFact]
    public async Task The_email_log_shows_only_visible_sites_mail()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Mail Log Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        var tech = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech", email = $"t-{Guid.NewGuid():N}@cmms.test", isContractor = false })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Secret B job", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "High", assigneeIds = new[] { tech } }));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Open A job", siteId = w.A.Site, locationId = w.A.Location, type = "Corrective", priority = "High", assigneeIds = new[] { tech } }));
        var (limitedAdmin, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var subjects = (await Ok(await limitedAdmin.GetAsync("/api/email/log"))).EnumerateArray().Select(e => e.GetProperty("subject").GetString() ?? "").ToList();
        Assert.DoesNotContain(subjects, s => s.Contains("Secret B job"));
        Assert.Contains(subjects, s => s.Contains("Open A job"));
        Assert.Contains((await Ok(await w.Admin.GetAsync("/api/email/log"))).EnumerateArray(), e => (e.GetProperty("subject").GetString() ?? "").Contains("Secret B job"));
    }

    [SkippableFact]
    public async Task Shared_records_in_use_in_a_hidden_site_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Shared Delete Co");
        // A PM task only B's schedule uses.
        var task = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/pm-tasks", new { code = "B-ONLY", name = "B check", steps = new[] { "Look" } })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "PM B2", siteId = w.B.Site, locationId = w.B.Location, assetId = w.B.Asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled",
            frequency = 1, unit = "Week", workPeriodDays = 2, wholeMonth = false, leadDays = 0, startDate = Today, timeBased = false, extraTasks = Array.Empty<object>(),
        }));
        // A part only B stocks.
        var part = await PartAsync(w.Admin, w, "DEL1");
        await Ok(await w.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = w.B.Store, quantity = 5, unitCost = 10 }));
        // A supplier only B's purchase order uses.
        var supplier = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/suppliers", new { code = "BSUP", name = "B supplier", isContractor = false })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/purchase-orders", new { siteId = w.B.Site, supplierId = supplier, currency = "USD", exchangeRate = 1, lines = Array.Empty<object>() }));
        // A trade only B's work order uses.
        var trade = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/trades", new { code = "WELD", name = "Welding", isActive = true })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Weld", siteId = w.B.Site, locationId = w.B.Location, type = "Corrective", priority = "Low", tradeId = trade }));

        var (c, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/pm-tasks/{task}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/parts/{part}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/suppliers/{supplier}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/trades/{trade}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.GetAsync($"/api/parts/{part}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.GetAsync($"/api/pm-tasks/{task}")).StatusCode);
    }

    // ---- Important ----

    [SkippableFact]
    public async Task A_limited_admin_cannot_change_people_who_see_more()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Takeover Co");
        var admin = await RoleAsync(w.Admin, "Administrator");
        var (limitedAdmin, _) = await UserAsync(fx, w.Admin, "Administrator", [w.A.Site]);
        var everyoneEmail = $"{Guid.NewGuid():N}@test.local";
        var everyone = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "Everyone", email = everyoneEmail, isActive = true, roleIds = new[] { admin }, password = UserPassword })));
        var reset = await limitedAdmin.PostAsJsonAsync($"/api/users/{everyone}/password", new { password = "Takeover1!x" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Contains("You can only change people whose access is inside yours.", await reset.Content.ReadAsStringAsync());
        var narrow = await limitedAdmin.PutAsJsonAsync($"/api/users/{everyone}", new { fullName = "Everyone", email = everyoneEmail, isActive = true, roleIds = new[] { admin }, siteIds = new[] { w.A.Site } });
        Assert.Equal(HttpStatusCode.BadRequest, narrow.StatusCode);
        // People inside their own access stay theirs to manage.
        var aEmail = $"{Guid.NewGuid():N}@test.local";
        var aUser = Id(await Ok(await limitedAdmin.PostAsJsonAsync("/api/users", new { fullName = "A person", email = aEmail, isActive = true, roleIds = new[] { admin }, password = UserPassword, siteIds = new[] { w.A.Site } })));
        Assert.Equal(HttpStatusCode.NoContent, (await limitedAdmin.PostAsJsonAsync($"/api/users/{aUser}/password", new { password = "Another1!x" })).StatusCode);
    }

    [SkippableFact]
    public async Task The_parts_list_counts_only_visible_reservations()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Reserved Co");
        var part = await PartAsync(w.Admin, w, "RES1");
        await Ok(await w.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = w.B.Store, quantity = 50, unitCost = 10 }));
        await Ok(await w.Admin.PostAsJsonAsync($"/api/work-orders/{w.B.WorkOrder}/planned-parts", new { partId = part, storeId = w.B.Store, quantity = 7 }));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", [w.A.Site]);
        var row = (await Ok(await c.GetAsync("/api/parts"))).GetProperty("items").EnumerateArray().First(x => Id(x) == part);
        Assert.Equal(0m, row.GetProperty("reserved").GetDecimal());
    }

    [SkippableFact]
    public async Task Branch_limited_people_stay_inside_their_branch()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Inside Co");
        var floor = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, parentId = w.A.Location, code = "F1", name = "Floor 1", type = "Floor", criticality = "Medium", isActive = true })));
        var room = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, parentId = floor, code = "R1", name = "Room 1", type = "Room", criticality = "Medium", isActive = true })));
        var (c, _) = await UserAsync(fx, w.Admin, "Administrator", locationIds: [floor]);

        var top = await c.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, code = "NEW", name = "New", type = "Building", criticality = "Medium", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, top.StatusCode);
        Assert.Contains("Choose a parent place in your part of the site.", await top.Content.ReadAsStringAsync());
        var r = await Ok(await c.GetAsync($"/api/locations/{room}"));
        var moveOut = await c.PutAsJsonAsync($"/api/locations/{room}", new { siteId = w.A.Site, parentId = (Guid?)null, code = "R1", name = "Room 1", type = "Room", criticality = "Medium", isActive = true, rowVersion = r.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, moveOut.StatusCode);
        // Their own top place keeps its parent above the branch when renamed.
        var f = await Ok(await c.GetAsync($"/api/locations/{floor}"));
        await Ok(await c.PutAsJsonAsync($"/api/locations/{floor}", new { siteId = w.A.Site, parentId = w.A.Location, code = "F1", name = "First floor", type = "Floor", criticality = "Medium", isActive = true, rowVersion = f.GetProperty("rowVersion").GetString() }));
        // The site record is for people who see all of it.
        var s = await Ok(await c.GetAsync($"/api/sites/{w.A.Site}"));
        var siteEdit = await c.PutAsJsonAsync($"/api/sites/{w.A.Site}", new { code = s.GetProperty("code").GetString(), name = "Renamed", isActive = true, settings = new { requestRecipientIds = Array.Empty<Guid>() } });
        Assert.Equal(HttpStatusCode.BadRequest, siteEdit.StatusCode);
        // Editing work with only an asset keeps it in the branch (its location comes from the asset).
        var asset = Id(await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Fan", siteId = w.A.Site, locationId = room, status = "Active", criticality = "Low", warrantyType = "None" })));
        var wo = Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Fan job", siteId = w.A.Site, assetId = asset, type = "Corrective", priority = "Low" })));
        var detail = await Ok(await c.GetAsync($"/api/work-orders/{wo}"));
        await Ok(await c.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Fan job", siteId = w.A.Site, locationId = (Guid?)null, assetId = asset, type = "Corrective", priority = "Low", rowVersion = detail.GetProperty("rowVersion").GetString() }));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/work-orders/{wo}")).StatusCode);
        // The import keeps to the same rules.
        var result = await RunAsync(c, Book(
            new Sheet("Locations", LocationHeadings, ["A", "TOP2", "Top", null]),
            new Sheet("Assets", ["Asset number", "Name", "Site code", "Location code"], ["LOOSE-1", "Loose", "A", null])));
        Assert.Equal(1, Count(SheetOf(result, "Locations"), "problems"));
        Assert.Equal(1, Count(SheetOf(result, "Assets"), "problems"));
    }

    [SkippableFact]
    public async Task Place_paths_name_the_places_above_a_branch()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Paths Co");
        var floor = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/locations", new { siteId = w.A.Site, parentId = w.A.Location, code = "F1", name = "Floor 1", type = "Floor", criticality = "Medium", isActive = true })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Floor job", siteId = w.A.Site, locationId = floor, type = "Corrective", priority = "Low" }));
        var (c, _) = await UserAsync(fx, w.Admin, "Maintenance Manager", locationIds: [floor]);
        var path = (await Ok(await c.GetAsync("/api/work-orders"))).GetProperty("items")[0].GetProperty("locationPath").GetString();
        Assert.Equal("Hall A › Floor 1", path);
    }

    [SkippableFact]
    public async Task The_site_switch_reaches_downloads()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Download Co");
        var csv = Encoding.UTF8.GetString(await (await w.Admin.GetAsync($"/api/work-orders/export?format=csv&c=title&t=Title&cmxSite={w.A.Site}")).Content.ReadAsByteArrayAsync());
        Assert.Contains("Fix A", csv);
        Assert.DoesNotContain("Fix B", csv);
    }

    [SkippableFact]
    public async Task Purchase_order_emails_reach_branch_limited_people()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Po Mail Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        var tenant = (await Ok(await w.Admin.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var approverEmail = $"approver-{Guid.NewGuid():N}@cmms.test";
        await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "Floor approver", email = approverEmail, isActive = true, roleIds = new[] { await RoleAsync(w.Admin, "Administrator") },
            password = UserPassword, locationIds = new[] { w.A.Location } }));
        await Ok(await w.Admin.PostAsync($"/api/purchase-orders/{w.A.Po}/submit", null));
        Assert.Contains((await QueuedAsync(tenant)), e => e.ToAddress == approverEmail && e.EventKey == EmailEvents.PoSubmitted);
    }

    [SkippableFact]
    public async Task Importing_pm_schedules_keeps_their_team()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Import Team Co");
        var team = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/teams", new { code = "CREW", name = "Crew", siteId = w.A.Site, memberIds = Array.Empty<Guid>(), isActive = true })));
        var s = await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{w.A.Schedule}"));
        var code = s.GetProperty("code").GetString()!;
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(s.GetRawText())!;
        body["teamId"] = team;
        await Ok(await w.Admin.PutAsJsonAsync($"/api/pm-schedules/{w.A.Schedule}", body));
        await RunAsync(w.Admin, Book(new Sheet("PM schedules", ["Code", "Name"], [code, "Renamed by import"])));
        var after = await Ok(await w.Admin.GetAsync($"/api/pm-schedules/{w.A.Schedule}"));
        Assert.Equal("Renamed by import", after.GetProperty("name").GetString());
        Assert.Equal(team, after.GetProperty("teamId").GetGuid());
    }
}
