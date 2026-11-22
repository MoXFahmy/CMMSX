using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.SiteAccessTests;

namespace FourC.Cmms.Tests;

/// <summary>Each site can override the organization's numbering, default store, request recipients and sender name (0.9.0).</summary>
public class SiteSettingsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";

    public static async Task<HttpResponseMessage> TrySetSiteAsync(HttpClient c, Guid site, object settings)
    {
        var s = await Ok(await c.GetAsync($"/api/sites/{site}"));
        return await c.PutAsJsonAsync($"/api/sites/{site}", new { code = s.GetProperty("code").GetString(), name = s.GetProperty("name").GetString(), isActive = true, settings });
    }

    public static async Task SetSiteAsync(HttpClient c, Guid site, object settings) => await Ok(await TrySetSiteAsync(c, site, settings));

    private async Task<List<OutboundEmail>> QueuedAsync(Guid tenant)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant).ToListAsync();
    }

    private static async Task<string> NumberAsync(HttpClient c, string path, object body) =>
        (await Ok(await c.PostAsJsonAsync(path, body))).GetProperty("id").GetGuid() is var id
            ? (await Ok(await c.GetAsync($"{path}/{id}"))).GetProperty("number").GetString()! : "";

    [SkippableFact]
    public async Task A_site_with_its_own_prefix_numbers_in_its_own_sequence()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Prefix Co");
        await SetSiteAsync(w.Admin, w.A.Site, new { workOrderNumberPrefix = "A-WO-", workRequestNumberPrefix = "A-WR-", purchaseOrderNumberPrefix = "A-PO-" });
        object Wo(Guid site) => new { title = "Job", siteId = site, type = "Corrective", priority = "Low" };
        Assert.Equal("A-WO-00001", await NumberAsync(w.Admin, "/api/work-orders", Wo(w.A.Site)));
        Assert.Equal("A-WO-00002", await NumberAsync(w.Admin, "/api/work-orders", Wo(w.A.Site)));
        Assert.StartsWith("WO-", await NumberAsync(w.Admin, "/api/work-orders", Wo(w.B.Site)));
        var req = await Ok(await w.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = w.A.Site, priority = "Low", type = "Corrective" }));
        Assert.Equal("A-WR-00001", req.GetProperty("number").GetString());
        var po = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/purchase-orders", new { siteId = w.A.Site, supplierId = w.Supplier, currency = "USD", exchangeRate = 1, lines = Array.Empty<object>() })));
        Assert.Equal("A-PO-00001", (await Ok(await w.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("number").GetString());
    }

    [SkippableFact]
    public async Task Planned_parts_start_from_the_sites_default_store()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Store Co");
        var s2 = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/stores", new { code = "S2", name = "Second", siteId = w.A.Site })));
        await SetSiteAsync(w.Admin, w.A.Site, new { defaultStoreId = s2 });
        var part = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P1", name = "Filter", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = w.A.Store, bin = "A1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await Ok(await w.Admin.PostAsJsonAsync($"/api/work-orders/{w.A.WorkOrder}/planned-parts", new { partId = part, quantity = 1 }));
        var planned = (await Ok(await w.Admin.GetAsync($"/api/work-orders/{w.A.WorkOrder}"))).GetProperty("plannedParts")[0];
        Assert.Equal(s2, planned.GetProperty("storeId").GetGuid());
    }

    [SkippableFact]
    public async Task New_requests_go_to_the_sites_recipients()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Recipients Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        var tenant = (await Ok(await w.Admin.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var m1 = Addr("manager1"); var m2 = Addr("manager2");
        var roles = await Ok(await w.Admin.GetAsync("/api/roles"));
        var manager = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Maintenance Manager").GetProperty("id").GetGuid();
        var u1 = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "M1", email = m1, isActive = true, roleIds = new[] { manager }, password = UserPassword })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "M2", email = m2, isActive = true, roleIds = new[] { manager }, password = UserPassword }));
        await SetSiteAsync(w.Admin, w.A.Site, new { requestRecipientIds = new[] { u1 } });

        await Ok(await w.Admin.PostAsJsonAsync("/api/work-requests", new { title = "At A", siteId = w.A.Site, priority = "Low", type = "Corrective" }));
        var atA = (await QueuedAsync(tenant)).Where(e => e.EventKey == "request.submitted").Select(e => e.ToAddress).ToList();
        Assert.Equal([m1], atA);
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-requests", new { title = "At B", siteId = w.B.Site, priority = "Low", type = "Corrective" }));
        var all = (await QueuedAsync(tenant)).Where(e => e.EventKey == "request.submitted").Select(e => e.ToAddress).ToList();
        Assert.Equal(2, all.Count(a => a == m1));
        Assert.Contains(m2, all);
    }

    [SkippableFact]
    public async Task E_mails_skip_people_who_cannot_see_the_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Skip Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        var tenant = (await Ok(await w.Admin.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var bManager = Addr("bmanager");
        var roles = await Ok(await w.Admin.GetAsync("/api/roles"));
        var manager = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Maintenance Manager").GetProperty("id").GetGuid();
        await Ok(await w.Admin.PostAsJsonAsync("/api/users", new { fullName = "B only", email = bManager, isActive = true, roleIds = new[] { manager }, password = UserPassword, siteIds = new[] { w.B.Site } }));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-requests", new { title = "At A", siteId = w.A.Site, priority = "Low", type = "Corrective" }));
        Assert.DoesNotContain(bManager, (await QueuedAsync(tenant)).Select(e => e.ToAddress));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-requests", new { title = "At B", siteId = w.B.Site, priority = "Low", type = "Corrective" }));
        Assert.Contains(bManager, (await QueuedAsync(tenant)).Select(e => e.ToAddress));
    }

    [SkippableFact]
    public async Task E_mails_about_a_site_use_its_sender_name()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Sender Co");
        await TestSettings.SetSettingsAsync(w.Admin, s => s["emailEnabled"] = true);
        await SetSiteAsync(w.Admin, w.A.Site, new { emailSenderName = "Plant A" });
        var tech = Addr("tech");
        var emp = Id(await Ok(await w.Admin.PostAsJsonAsync("/api/employees", new { code = "T1", fullName = "Tech", email = tech, isContractor = false })));
        await Ok(await w.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Seal", siteId = w.A.Site, type = "Corrective", priority = "Low", assigneeIds = new[] { emp } }));
        await fx.Factory!.Services.GetRequiredService<EmailSenderService>().RunOnceAsync();
        var sent = fx.Emails.Sent.Single(m => m.To == tech);
        Assert.Equal("Plant A via CMX", sent.FromName);
    }

    [SkippableFact]
    public async Task Site_settings_are_checked()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Checked Co");
        async Task Refused(object settings, string field)
        {
            var r = await TrySetSiteAsync(w.Admin, w.A.Site, settings);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains(field, await r.Content.ReadAsStringAsync());
        }
        await Refused(new { defaultStoreId = w.B.Store }, "settings.defaultStoreId");
        await Refused(new { requestRecipientIds = new[] { Guid.NewGuid() } }, "settings.requestRecipientIds");
        await Refused(new { workOrderNumberPrefix = new string('X', 21) }, "settings.workOrderNumberPrefix");
        await Refused(new { workingHours = new[] { new { day = "Monday", start = "17:00", end = "08:00" } } }, "settings.workingHours");
    }

    [SkippableFact]
    public async Task Site_managers_can_pick_request_recipients()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var w = await WorldAsync(fx, "Lookup Co");
        var users = await Ok(await w.Admin.GetAsync("/api/users/lookup"));
        Assert.Contains(users.EnumerateArray(), u => u.GetProperty("fullName").GetString() == "Admin");
        var (viewer, _) = await UserAsync(fx, w.Admin, "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users/lookup")).StatusCode);
    }
}
