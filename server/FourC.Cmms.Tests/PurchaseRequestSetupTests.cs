using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: order types, the two purchase-request permissions and their built-in roles.</summary>
public class PurchaseRequestSetupTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task<(HttpClient admin, Guid site)> OrgAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Buyers Co", fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        return (c, site);
    }

    [SkippableFact]
    public async Task New_organization_has_parts_services_and_capital_order_types()
    {
        Skip.IfNot(fx.Enabled);
        var (c, _) = await OrgAsync();
        var types = (await Ok(await c.GetAsync("/api/order-types"))).EnumerateArray().ToList();
        Assert.Equal(["CAPITAL", "PARTS", "SERVICES"], types.Select(t => t.GetProperty("code").GetString()!).OrderBy(x => x));
        Assert.All(types, t => Assert.True(t.GetProperty("isActive").GetBoolean()));
    }

    [SkippableFact]
    public async Task Purchase_request_permissions_are_listed_and_given_to_built_in_roles()
    {
        Skip.IfNot(fx.Enabled);
        var (c, _) = await OrgAsync();
        var codes = (await Ok(await c.GetAsync("/api/roles/permissions"))).EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Contains("purchasing.request", codes);
        Assert.Contains("purchasing.approveRequests", codes);
        var roles = (await Ok(await c.GetAsync("/api/roles"))).EnumerateArray().ToList();
        List<string?> Perms(string role) => roles.Single(r => r.GetProperty("name").GetString() == role).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("purchasing.request", Perms("Technician"));
        Assert.DoesNotContain("purchasing.approveRequests", Perms("Technician"));
        Assert.Contains("purchasing.request", Perms("Maintenance Manager"));
        Assert.Contains("purchasing.approveRequests", Perms("Maintenance Manager"));
    }

    [SkippableFact]
    public async Task Order_type_in_use_by_an_order_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var (c, site) = await OrgAsync();
        var supplier = Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var po = Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = site, supplierId = supplier, shippingCost = 0,
            lines = new[] { new { description = "Crane hire", quantity = 1, unitPrice = 100m, discountPercent = 0 } },
        })));
        var parts = (await Ok(await c.GetAsync("/api/order-types"))).EnumerateArray().Single(t => t.GetProperty("code").GetString() == "PARTS");
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            var order = await db.PurchaseOrders.IgnoreQueryFilters().FirstAsync(o => o.Id == po);
            order.OrderTypeId = Id(parts);
            await db.SaveChangesAsync();
        }
        var r = await c.DeleteAsync($"/api/order-types/{Id(parts)}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("in use", await r.Content.ReadAsStringAsync());
    }
}
