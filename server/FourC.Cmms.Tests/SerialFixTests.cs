using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.PurchaseRequestTests;
using static FourC.Cmms.Tests.SerialReceiptTests;
using static FourC.Cmms.Tests.UnitActionTests;
using static FourC.Cmms.Tests.UnitFitTests;

namespace FourC.Cmms.Tests;

/// <summary>0.26.1: serial-numbered parts fixes left by 0.24.0's review.</summary>
public class SerialFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<HttpClient> RoleUserAsync(ApiFixture fx, HttpClient admin, string[] permissions, Guid[]? sites = null)
    {
        var role = Id(await Ok(await admin.PostAsJsonAsync("/api/roles", new { name = $"Role {Guid.NewGuid():N}"[..20], description = (string?)null, permissions })));
        var email = Loop.Addr("fitter");
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Fitter", email, isActive = true, roleIds = new[] { role }, password = "Passw0rd!x", siteIds = sites }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", rememberMe = false }));
        return c;
    }

    [SkippableFact]
    public async Task Serial_and_shelf_life_dont_mix()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        await Refused(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "SH-1", name = "Seal kit", unitOfMeasure = "EA", standardPrice = 5, isCritical = false, isShelfItem = true, allowNegativeStock = false,
            isSerialTracked = true, levels = Array.Empty<object>(),
        }), "A serial-tracked part can't also be a shelf-life item.");
        var shelf = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "SH-2", name = "Grease", unitOfMeasure = "EA", standardPrice = 5, isCritical = false, isShelfItem = true, allowNegativeStock = false, levels = Array.Empty<object>(),
        })));
        await Refused(await Track(o.Admin, shelf, on: true), "A serial-tracked part can't also be a shelf-life item.");
    }

    [SkippableFact]
    public async Task Repair_line_is_received_whole()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var po = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = s.O.Site, supplierId = s.O.Supplier, shippingCost = 0, currency = "USD", exchangeRate = 1m,
            lines = new[] { new { partId = (Guid?)null, description = (string?)null, quantity = 1m, unitPrice = 300m, discountPercent = 0m, storeId = (Guid?)null, workOrderId = (Guid?)null, unitId = (Guid?)s.U1 } },
        })));
        await Ok(await s.O.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = (await Ok(await s.O.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Refused(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 0.5m, storeId = (Guid?)s.O.Store } } }),
            "Receive a repair line whole.");
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1m, storeId = (Guid?)s.O.Store } } }));
    }

    [SkippableFact]
    public async Task Units_list_filters_before_the_cap()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var serials = Enumerable.Range(1, 503).Select(i => $"S-{i:0000}").ToArray();
        var part = await SerialPartAsync(o.Admin, o, "MANY-1", serials);
        var units = await UnitsAsync(fx, part);
        var scrapped = units.Take(2).Select(u => u.Id).ToList();
        await Loop.DbAsync(fx, db => db.Assets.IgnoreQueryFilters().Where(a => scrapped.Contains(a.Id)).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AssetStatus.Disposed)));
        var active = await Ok(await o.Admin.GetAsync($"/api/serial-units?partId={part}&state=active"));
        Assert.Equal((500, true), (active.GetProperty("items").GetArrayLength(), active.GetProperty("capped").GetBoolean()));
        Assert.DoesNotContain(active.GetProperty("items").EnumerateArray(), u => scrapped.Contains(Id(u)));
        var found = await Ok(await o.Admin.GetAsync($"/api/serial-units?partId={part}&search=S-0503"));
        Assert.Equal((1, false), (found.GetProperty("items").GetArrayLength(), found.GetProperty("capped").GetBoolean()));
    }

    [SkippableFact]
    public async Task Fitted_units_follow_site_limits()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        var siteB = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var limited = await Loop.UserAsync(fx, s.O.Admin, "Administrator", [siteB]);
        Assert.Equal(HttpStatusCode.NotFound, (await limited.Client.GetAsync($"/api/assets/{s.Chiller}/fitted-units")).StatusCode);
        await Ok(await s.O.Admin.GetAsync($"/api/assets/{s.Chiller}/fitted-units"));
    }

    [SkippableFact]
    public async Task Units_list_follows_site_limits()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        var siteB = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var viewer = await RoleUserAsync(fx, s.O.Admin, ["inventory.view"], [siteB]);
        Assert.Empty((await Ok(await viewer.GetAsync($"/api/serial-units?partId={s.Part}"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/serial-units/{s.U1}")).StatusCode);
    }

    [SkippableFact]
    public async Task Fitted_units_move_with_their_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        await Ok(await Fit(s.O.Admin, s.Wo, s.U1, s.Chiller));
        var siteB = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        await Ok(await s.O.Admin.PostAsJsonAsync($"/api/assets/{s.Chiller}/transfer", new { siteId = siteB, locationId = (Guid?)null, reason = "Moved", includeChildren = false }));
        Assert.Equal(siteB, (await UnitAsync(fx, s.U1)).SiteId);
    }

    [SkippableFact]
    public async Task Fit_candidates_follow_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync(fx);
        var fitter = await RoleUserAsync(fx, s.O.Admin, ["workorders.view", "workorders.execute", "inventory.transact"]);
        var units = (await Ok(await fitter.GetAsync($"/api/serial-units/to-fit?partId={s.Part}&siteId={s.O.Site}"))).EnumerateArray().Select(Id).ToList();
        Assert.Contains(s.U1, units);
        Assert.Contains(s.U2, units);
        var siteB = Id(await Ok(await s.O.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var elsewhere = await RoleUserAsync(fx, s.O.Admin, ["workorders.view", "workorders.execute", "inventory.transact"], [siteB]);
        var seen = await Ok(await elsewhere.GetAsync($"/api/serial-units/to-fit?partId={s.Part}"));
        Assert.Empty(seen.EnumerateArray());
        var viewer = await RoleUserAsync(fx, s.O.Admin, ["workorders.view"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/serial-units/to-fit?partId={s.Part}")).StatusCode);
        // Repair lines: units under repair, for people who make purchase orders.
        await Ok(await Act(s.O.Admin, s.U1, "send-for-repair", new { }));
        var buyer = await RoleUserAsync(fx, s.O.Admin, ["purchasing.view", "purchasing.create"]);
        Assert.Equal([s.U1], (await Ok(await buyer.GetAsync("/api/serial-units/at-repair"))).EnumerateArray().Select(Id));
    }
}
