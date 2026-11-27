using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: companies with roles, extra contacts and addresses, contractor categories and company rates; employees linked to a company.</summary>
public class CompanyTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<string> Problem(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        return body;
    }

    internal static async Task<Guid> CategoryAsync(HttpClient c, string code, decimal rate) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/contractor-categories", new { code, name = code + " work", defaultRate = rate })));

    /// <summary>A company with only the Contractor role, optionally with its own rates.</summary>
    internal static async Task<Guid> ContractorAsync(HttpClient c, string code, params (Guid Category, decimal Rate)[] rates) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new
        {
            code, name = code + " Co", isSupplier = false, isContractor = true, isManufacturer = false,
            contacts = new[] { new { name = "Omar Lift", role = "Engineer", phone = "0100", email = "omar@lift.test" } },
            rates = rates.Select(r => new { categoryId = r.Category, rate = r.Rate }).ToArray(),
        })));

    private static object CompanyBody(string code, bool supplier, bool contractor, bool manufacturer, object[]? contacts = null, object[]? addresses = null, object[]? rates = null, bool active = true) => new
    {
        code, name = code + " Co", isSupplier = supplier, isContractor = contractor, isManufacturer = manufacturer, isActive = active,
        contacts = contacts ?? [], addresses = addresses ?? [], rates = rates ?? [],
    };

    [SkippableFact]
    public async Task Company_has_roles_contacts_addresses_and_category_rates()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 120);
        var id = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", CompanyBody("LIFT", false, true, true,
            [new { name = "Omar", role = "Engineer", phone = "0100", email = "omar@lift.test" }, new { name = "Sara", role = "Accounts", phone = (string?)null, email = (string?)null }],
            [new { label = "Workshop", address = "12 Ring Road", city = "Cairo", country = "Egypt" }],
            [new { categoryId = elec, rate = 150m }]))));
        var s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"));
        Assert.False(s.GetProperty("isSupplier").GetBoolean());
        Assert.True(s.GetProperty("isContractor").GetBoolean());
        Assert.True(s.GetProperty("isManufacturer").GetBoolean());
        Assert.Equal(["Omar", "Sara"], s.GetProperty("contacts").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("Workshop", s.GetProperty("addresses")[0].GetProperty("label").GetString());
        var rate = s.GetProperty("rates")[0];
        Assert.Equal(elec, rate.GetProperty("categoryId").GetGuid());
        Assert.Equal(150m, rate.GetProperty("rate").GetDecimal());
        Assert.Equal("ELEC work", rate.GetProperty("categoryName").GetString());

        var omar = s.GetProperty("contacts")[0].GetProperty("id").GetGuid();
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", false, true, true,
            [new { id = omar, name = "Omar Hassan", role = "Engineer", phone = "0100", email = "omar@lift.test" }], [], [new { categoryId = elec, rate = 160m }])));
        s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"));
        var contact = Assert.Single(s.GetProperty("contacts").EnumerateArray());
        Assert.Equal(omar, contact.GetProperty("id").GetGuid());
        Assert.Equal("Omar Hassan", contact.GetProperty("name").GetString());
        Assert.Empty(s.GetProperty("addresses").EnumerateArray());
        Assert.Equal(160m, s.GetProperty("rates")[0].GetProperty("rate").GetDecimal());
    }

    private static Dictionary<Guid, decimal> Rates(JsonElement company) =>
        company.GetProperty("rates").EnumerateArray().ToDictionary(r => r.GetProperty("categoryId").GetGuid(), r => r.GetProperty("rate").GetDecimal());

    private static List<Guid> Ids(JsonElement company, string list) => company.GetProperty(list).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();

    /// <summary>0.29.2 (0.17.0-10): contacts, an address and a rate added to a company that already exists are saved with the old rows kept, edit after edit.</summary>
    [SkippableFact]
    public async Task Adding_to_an_existing_company_keeps_old_rows()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 120);
        var mech = await CategoryAsync(o.Admin, "MECH", 100);
        var civil = await CategoryAsync(o.Admin, "CIVIL", 90);
        var id = await ContractorAsync(o.Admin, "LIFT", (elec, 150m));
        var omar = Assert.Single(Ids(await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}")), "contacts"));
        static object Contact(Guid? id, string name) => new { id, name, role = (string?)null, phone = (string?)null, email = (string?)null };
        static object Address(Guid? id, string label) => new { id, label, address = label + " road", city = "Cairo", country = "Egypt" };

        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", false, true, false,
            [Contact(omar, "Omar Lift"), Contact(null, "Sara")], [Address(null, "Workshop")], [new { categoryId = elec, rate = 150m }, new { categoryId = mech, rate = 110m }])));
        var s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"));
        var contacts = Ids(s, "contacts");
        Assert.Equal(2, contacts.Count);
        Assert.Equal(omar, contacts[0]);
        var workshop = Assert.Single(Ids(s, "addresses"));
        Assert.Equal(new Dictionary<Guid, decimal> { [elec] = 150m, [mech] = 110m }, Rates(s));

        // A second edit adds again and keeps what the first one added.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{id}", CompanyBody("LIFT", false, true, false,
            [Contact(omar, "Omar Lift"), Contact(contacts[1], "Sara"), Contact(null, "Hany")], [Address(workshop, "Workshop"), Address(null, "Store")],
            [new { categoryId = elec, rate = 150m }, new { categoryId = mech, rate = 110m }, new { categoryId = civil, rate = 95m }])));
        s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{id}"));
        var again = Ids(s, "contacts");
        Assert.Equal(3, again.Count);
        Assert.Equal(contacts, again.Take(2));
        Assert.Equal(["Omar Lift", "Sara", "Hany"], s.GetProperty("contacts").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        var addresses = Ids(s, "addresses");
        Assert.Equal(2, addresses.Count);
        Assert.Equal(workshop, addresses[0]);
        Assert.Equal(new Dictionary<Guid, decimal> { [elec] = 150m, [mech] = 110m, [civil] = 95m }, Rates(s));
    }

    [SkippableFact]
    public async Task Company_needs_a_role_and_named_contacts()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 120);
        Assert.Contains("Choose at least one role.", await Problem(await o.Admin.PostAsJsonAsync("/api/suppliers", CompanyBody("NONE", false, false, false))));
        Assert.Contains("Contact 1: enter a name.", await Problem(await o.Admin.PostAsJsonAsync("/api/suppliers",
            CompanyBody("NONAME", true, false, false, [new { name = " ", role = (string?)null, phone = (string?)null, email = (string?)null }]))));
        Assert.Contains("Each category can have one rate.", await Problem(await o.Admin.PostAsJsonAsync("/api/suppliers",
            CompanyBody("TWICE", false, true, false, rates: [new { categoryId = elec, rate = 1m }, new { categoryId = elec, rate = 2m }]))));
    }

    [SkippableFact]
    public async Task Lookup_and_list_filter_by_role()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 120);
        var contractor = await ContractorAsync(o.Admin, "LIFT", (elec, 150m));
        var inactive = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", CompanyBody("OLDC", false, true, false, active: false))));

        var plain = (await Ok(await o.Admin.GetAsync("/api/suppliers/lookup"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([o.Supplier], plain);
        var contractors = (await Ok(await o.Admin.GetAsync("/api/suppliers/lookup?role=contractor"))).EnumerateArray().ToList();
        var c = Assert.Single(contractors);
        Assert.Equal(contractor, c.GetProperty("id").GetGuid());
        Assert.Equal("Omar Lift", c.GetProperty("contacts")[0].GetProperty("name").GetString());
        Assert.Equal(150m, c.GetProperty("rates")[0].GetProperty("rate").GetDecimal());
        Assert.DoesNotContain(inactive, contractors.Select(x => x.GetProperty("id").GetGuid()));
        Assert.Empty((await Ok(await o.Admin.GetAsync("/api/suppliers?role=manufacturer"))).GetProperty("items").EnumerateArray());
        Assert.Equal(2, (await Ok(await o.Admin.GetAsync("/api/suppliers?role=contractor"))).GetProperty("items").GetArrayLength());
    }

    [SkippableFact]
    public async Task Purchase_order_needs_a_company_with_the_supplier_role()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var contractor = await ContractorAsync(o.Admin, "LIFT");
        Assert.Contains("Choose a company with the Supplier role.", await Problem(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new { siteId = o.Site, supplierId = contractor, lines = Array.Empty<object>() })));

        // An existing order keeps its supplier after the supplier loses the role.
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new { siteId = o.Site, supplierId = o.Supplier, lines = Array.Empty<object>() })));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/suppliers/{o.Supplier}", CompanyBody("SUP", false, true, false)));
        var detail = await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/purchase-orders/{po}", new
        {
            siteId = o.Site, supplierId = o.Supplier, notes = "kept", lines = Array.Empty<object>(), rowVersion = detail.GetProperty("rowVersion").GetString(),
        }));
    }

    [SkippableFact]
    public async Task Contractor_categories_have_default_rates_and_in_use_ones_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 120);
        var cat = Assert.Single((await Ok(await o.Admin.GetAsync("/api/contractor-categories"))).EnumerateArray());
        Assert.Equal(120m, cat.GetProperty("defaultRate").GetDecimal());
        await Ok(await o.Admin.PutAsJsonAsync($"/api/contractor-categories/{elec}", new { code = "ELEC", name = "Electrician", defaultRate = 130m, isActive = true }));
        Assert.Equal(130m, (await Ok(await o.Admin.GetAsync("/api/contractor-categories")))[0].GetProperty("defaultRate").GetDecimal());
        await ContractorAsync(o.Admin, "LIFT", (elec, 150m));
        Assert.Contains("This item is in use. Mark it inactive instead.", await Problem(await o.Admin.DeleteAsync($"/api/contractor-categories/{elec}")));
        var unused = await CategoryAsync(o.Admin, "PLMB", 90);
        Assert.Equal(HttpStatusCode.NoContent, (await o.Admin.DeleteAsync($"/api/contractor-categories/{unused}")).StatusCode);
    }

    [SkippableFact]
    public async Task Employee_links_to_a_contractor_company()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var contractor = await ContractorAsync(o.Admin, "LIFT");
        var emp = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Lift tech", isContractor = true, companyId = contractor })));
        var e = await Ok(await o.Admin.GetAsync($"/api/employees/{emp}"));
        Assert.Equal(contractor, e.GetProperty("companyId").GetGuid());
        Assert.Equal("LIFT Co", e.GetProperty("companyName").GetString());
        Assert.Contains("Choose an active company with the Contractor role.", await Problem(await o.Admin.PutAsJsonAsync($"/api/employees/{emp}", new
        {
            code = "E1", fullName = "Lift tech", isContractor = true, companyId = o.Supplier, rowVersion = e.GetProperty("rowVersion").GetString(),
        })));
    }

    [SkippableFact]
    public async Task Company_used_on_work_or_contracts_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var contractor = await ContractorAsync(o.Admin, "LIFT");
        await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Lift tech", isContractor = true, companyId = contractor }));
        Assert.Contains("This company is used on work orders, contracts or employees. Mark it inactive instead.", await Problem(await o.Admin.DeleteAsync($"/api/suppliers/{contractor}")));
    }
}
