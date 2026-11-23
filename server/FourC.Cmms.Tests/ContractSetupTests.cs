using System.Net.Http.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: contract types, the two contract permissions and the supplier record's roles.</summary>
public class ContractSetupTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task New_organization_has_the_four_contract_types()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var types = (await Ok(await o.Admin.GetAsync("/api/contract-types"))).EnumerateArray().ToList();
        Assert.Equal(["INSP", "LEASE", "MAINT", "WARR"], types.Select(t => t.GetProperty("code").GetString()!).OrderBy(x => x));
        Assert.All(types, t => Assert.True(t.GetProperty("isActive").GetBoolean()));
    }

    [SkippableFact]
    public async Task Contract_permissions_are_listed_and_given_to_built_in_roles()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var codes = (await Ok(await o.Admin.GetAsync("/api/roles/permissions"))).EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Contains("contracts.view", codes);
        Assert.Contains("contracts.manage", codes);
        var roles = (await Ok(await o.Admin.GetAsync("/api/roles"))).EnumerateArray().ToList();
        List<string?> Perms(string role) => roles.Single(r => r.GetProperty("name").GetString() == role).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        foreach (var role in new[] { "Technician", "Viewer" })
        {
            Assert.Contains("contracts.view", Perms(role));
            Assert.DoesNotContain("contracts.manage", Perms(role));
        }
        Assert.Contains("contracts.view", Perms("Maintenance Manager"));
        Assert.Contains("contracts.manage", Perms("Maintenance Manager"));
    }

    [SkippableFact]
    public async Task Existing_suppliers_keep_the_supplier_role()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var id = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", new { code = "OLD", name = "Old supplier", isContractor = false })));
        var s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"));
        Assert.True(s.GetProperty("isSupplier").GetBoolean());
        Assert.False(s.GetProperty("isManufacturer").GetBoolean());
    }
}
