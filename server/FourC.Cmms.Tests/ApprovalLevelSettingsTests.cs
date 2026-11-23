using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: approval level settings and order types on orders.</summary>
public class ApprovalLevelSettingsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> TypeAsync(HttpClient c, string code) =>
        Id((await Ok(await c.GetAsync("/api/order-types"))).EnumerateArray().Single(t => t.GetProperty("code").GetString() == code));

    private static async Task<string> RefusedAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [SkippableFact]
    public async Task Level_needs_a_name_one_to_five_approvers_and_a_unique_minimum()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var approvers = new List<Guid>();
        for (var k = 0; k < 6; k++) approvers.Add((await UserAsync(fx, o.Admin, "Maintenance Manager")).Id);
        Assert.Contains("Choose at least one approver.",
            await RefusedAsync(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Manager", minAmount = 0, rule = "Any", approverIds = Array.Empty<Guid>() })));
        Assert.Contains("A level can have at most 5 approvers.",
            await RefusedAsync(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Manager", minAmount = 0, rule = "Any", approverIds = approvers })));
        Assert.Contains("The minimum can't be below 0.",
            await RefusedAsync(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Manager", minAmount = -1, rule = "Any", approverIds = approvers.Take(1) })));
        Assert.Contains("Enter a name.",
            await RefusedAsync(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = " ", minAmount = 0, rule = "Any", approverIds = approvers.Take(1) })));
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Plant manager", minAmount = 10000, rule = "All", approverIds = approvers.Take(2) }));
        Assert.Contains("Another level in this set already starts at 10,000.",
            await RefusedAsync(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Finance", minAmount = 10000, rule = "Any", approverIds = approvers.Take(1) })));
        // The same minimum is fine for another type or another site's set.
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Capital board", minAmount = 10000, rule = "Any", approverIds = approvers.Take(1), orderTypeId = await TypeAsync(o.Admin, "CAPITAL") }));
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Site manager", minAmount = 10000, rule = "Any", approverIds = approvers.Take(1), siteId = o.Site }));
        var list = (await Ok(await o.Admin.GetAsync("/api/approval-levels"))).EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        var plant = list.Single(l => l.GetProperty("name").GetString() == "Plant manager");
        Assert.Equal("All", plant.GetProperty("rule").GetString());
        Assert.Equal(2, plant.GetProperty("approvers").GetArrayLength());
    }

    [SkippableFact]
    public async Task Levels_list_flags_approvers_who_cant_sign()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var other = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var (_, tech, _) = await UserAsync(fx, o.Admin, "Technician");
        var (_, elsewhere, _) = await UserAsync(fx, o.Admin, "Maintenance Manager", siteIds: [other]);
        await Ok(await o.Admin.PostAsJsonAsync("/api/approval-levels", new { name = "Site", minAmount = 0, rule = "Any", approverIds = new[] { tech, elsewhere }, siteId = o.Site }));
        var level = (await Ok(await o.Admin.GetAsync("/api/approval-levels"))).EnumerateArray().Single();
        var problems = level.GetProperty("approvers").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetGuid(), a => a.GetProperty("problem").GetString());
        Assert.Equal("no-permission", problems[tech]);
        Assert.Equal("no-site-access", problems[elsewhere]);
    }

    [SkippableFact]
    public async Task Orders_carry_an_order_type_defaulting_to_parts_in_the_form()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var services = await TypeAsync(o.Admin, "SERVICES");
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, orderTypeId = services,
            lines = new[] { new { description = "Crane hire", quantity = 1, unitPrice = 100m, discountPercent = 0 } },
        })));
        var d = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal("SERVICES", d.GetProperty("orderType").GetProperty("code").GetString());
        var items = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders?orderTypeId={services}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([po], items.Select(Id).ToList());
        Assert.Equal("Services", items[0].GetProperty("orderTypeName").GetString());
        var untyped = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0,
            lines = new[] { new { description = "Gloves", quantity = 1, unitPrice = 5m, discountPercent = 0 } },
        })));
        Assert.Equal(JsonValueKind.Null, (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{untyped}"))).GetProperty("orderType").ValueKind);
    }
}
