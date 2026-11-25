using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: the contract record — numbering, periods, status, covered assets, site access, amendments, termination and files.</summary>
public class ContractTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Sites A (the org's first) and B, contractor company C, asset a1 at A and b1 at B.</summary>
    internal sealed record World(PurchaseRequestTests.Org O, Guid SiteB, Guid C, Guid A1, Guid B1);

    internal static async Task<World> WorldAsync(ApiFixture fx)
    {
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Alex Plant", isActive = true })));
        var c = await ContractorAsync(o.Admin, "LIFT");
        var a1 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Lift A1", siteId = o.Site, status = "Active", criticality = "Medium", warrantyType = "None" })));
        var b1 = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Lift B1", siteId = siteB, status = "Active", criticality = "Medium", warrantyType = "None" })));
        return new World(o, siteB, c, a1, b1);
    }

    internal static Dictionary<string, object?> Body(Guid company, DateOnly start, DateOnly end, Guid? site = null, decimal? value = 12000, Guid[]? assets = null,
        bool autoRenew = false, int? renewalMonths = null, int noticeDays = 0, int alertDays = 60, Guid? manager = null, string title = "Lift service") => new()
    {
        ["title"] = title, ["supplierId"] = company, ["siteId"] = site, ["startDate"] = start, ["endDate"] = end, ["value"] = value, ["currency"] = "EGP",
        ["autoRenew"] = autoRenew, ["renewalMonths"] = renewalMonths, ["noticeDays"] = noticeDays, ["alertDays"] = alertDays, ["managerId"] = manager,
        ["expectedWorkOrders"] = 12, ["expectedPer"] = "Year",
        ["assets"] = (assets ?? []).Select(a => new { assetId = a, yearlyCost = 1000m }).ToArray(),
    };

    internal static async Task<Guid> ContractAsync(HttpClient c, Dictionary<string, object?> body) => Id(await Ok(await c.PostAsJsonAsync("/api/contracts", body)));
    private static async Task<JsonElement> GetAsync(HttpClient c, Guid id) => await Ok(await c.GetAsync($"/api/contracts/{id}"));

    private static Dictionary<string, object?> WithVersion(Dictionary<string, object?> body, JsonElement current)
    {
        body["rowVersion"] = current.GetProperty("rowVersion").GetString();
        return body;
    }

    [SkippableFact]
    public async Task Contract_is_numbered_CON_with_a_first_period_and_a_status()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-10), Today.AddDays(355)));
        var k = await GetAsync(w.O.Admin, id);
        Assert.Equal("CON-00001", k.GetProperty("number").GetString());
        Assert.Equal("Active", k.GetProperty("status").GetString());
        var period = Assert.Single(k.GetProperty("periods").EnumerateArray());
        Assert.Equal(1, period.GetProperty("number").GetInt32());
        Assert.Equal(Today.AddDays(-10), DateOnly.Parse(period.GetProperty("startDate").GetString()!));
        Assert.Equal(Today.AddDays(355), DateOnly.Parse(period.GetProperty("endDate").GetString()!));
        Assert.Equal(12000m, period.GetProperty("value").GetDecimal());
        Assert.Equal("LIFT Co", k.GetProperty("company").GetProperty("name").GetString());

        var upcoming = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(1), Today.AddDays(100)));
        Assert.Equal("Upcoming", (await GetAsync(w.O.Admin, upcoming)).GetProperty("status").GetString());
        var expired = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-100), Today.AddDays(-1)));
        Assert.Equal("Expired", (await GetAsync(w.O.Admin, expired)).GetProperty("status").GetString());
        var list = (await Ok(await w.O.Admin.GetAsync("/api/contracts?status=Active"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([id], list.Select(x => x.GetProperty("id").GetGuid()));
    }

    [SkippableFact]
    public async Task Only_active_contractor_companies_can_be_chosen()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var c = w.O.Admin;
        Assert.Contains("Choose an active company with the Contractor role.", await Problem(await c.PostAsJsonAsync("/api/contracts", Body(w.O.Supplier, Today, Today.AddDays(30)))));
        Assert.Contains("The end date must be on or after the start date.", await Problem(await c.PostAsJsonAsync("/api/contracts", Body(w.C, Today, Today.AddDays(-1)))));
        Assert.Contains("Give the renewal length in months (1–120).", await Problem(await c.PostAsJsonAsync("/api/contracts", Body(w.C, Today, Today.AddDays(30), autoRenew: true))));
        var noTitle = Body(w.C, Today, Today.AddDays(30));
        noTitle["title"] = " ";
        Assert.Contains("Enter a title.", await Problem(await c.PostAsJsonAsync("/api/contracts", noTitle)));
    }

    [SkippableFact]
    public async Task Site_contract_covers_only_its_site_assets()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        Assert.Contains("is at another site than the contract.", await Problem(await w.O.Admin.PostAsJsonAsync("/api/contracts",
            Body(w.C, Today, Today.AddDays(30), site: w.O.Site, assets: [w.A1, w.B1]))));
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(30), assets: [w.A1, w.B1]));
        Assert.Equal(2, (await GetAsync(w.O.Admin, id)).GetProperty("assets").GetArrayLength());
        // Giving it a site later is checked the same way.
        var current = await GetAsync(w.O.Admin, id);
        Assert.Contains("is at another site than the contract.", await Problem(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}",
            WithVersion(Body(w.C, Today, Today.AddDays(30), site: w.O.Site, assets: [w.A1, w.B1]), current))));
    }

    [SkippableFact]
    public async Task Site_limited_user_sees_organization_wide_and_own_site_contracts_only()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var orgWide = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300), assets: [w.A1, w.B1], title: "Org wide"));
        var atA = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [w.A1], title: "At A"));
        var atB = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300), site: w.SiteB, assets: [w.B1], title: "At B"));
        var (user, _, _) = await UserAsync(fx, w.O.Admin, "Site A", [Permissions.ContractsView, Permissions.ContractsManage, Permissions.AssetsView], siteIds: [w.O.Site]);

        var seen = (await Ok(await user.GetAsync("/api/contracts"))).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { orgWide, atA }, seen);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/contracts/{atB}")).StatusCode);
        var mine = await GetAsync(user, orgWide);
        Assert.Equal([w.A1], mine.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("assetId").GetGuid()));

        // Saving it with the assets they can see keeps the one they can't.
        await Ok(await user.PutAsJsonAsync($"/api/contracts/{orgWide}", WithVersion(Body(w.C, Today, Today.AddDays(300), assets: [w.A1], title: "Org wide"), mine)));
        var all = (await GetAsync(w.O.Admin, orgWide)).GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("assetId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { w.A1, w.B1 }, all);
    }

    private const string HiddenCover = "This contract also covers assets at other sites that you can't see, so it can't be limited to one site.";

    [SkippableFact]
    public async Task Limited_editor_cannot_narrow_an_org_contract_covering_hidden_assets()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var orgWide = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300), assets: [w.A1, w.B1], title: "Org wide"));
        var hiddenNumber = (await Ok(await w.O.Admin.GetAsync($"/api/assets/{w.B1}"))).GetProperty("assetNumber").GetString()!;
        var (user, _, _) = await UserAsync(fx, w.O.Admin, "Site A", [Permissions.ContractsView, Permissions.ContractsManage, Permissions.AssetsView], siteIds: [w.O.Site]);
        var mine = await GetAsync(user, orgWide);

        // Giving the contract site A would leave it covering the asset at B that this person can't see: refused, and the hidden asset's number stays hidden.
        var refused = await Problem(await user.PutAsJsonAsync($"/api/contracts/{orgWide}", WithVersion(Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [w.A1], title: "Org wide"), mine)));
        Assert.Contains(HiddenCover, refused);
        Assert.DoesNotContain(hiddenNumber, refused);
        Assert.Equal(JsonValueKind.Null, (await GetAsync(w.O.Admin, orgWide)).GetProperty("siteId").ValueKind);

        // Someone who sees both sites may do it by naming what they want covered.
        var current = await GetAsync(w.O.Admin, orgWide);
        Assert.Contains("is at another site than the contract.", await Problem(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{orgWide}",
            WithVersion(Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [w.A1, w.B1], title: "Org wide"), current))));
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{orgWide}", WithVersion(Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [w.A1], title: "Org wide"), current)));
    }

    [SkippableFact]
    public async Task Branch_limited_editor_is_refused_only_for_hidden_assets_at_other_sites()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var admin = w.O.Admin;
        async Task<Guid> Loc(string code) => Id(await Ok(await admin.PostAsJsonAsync("/api/locations", new { siteId = w.O.Site, code, name = code, type = "Building", criticality = "Medium", isActive = true })));
        var mine = await Loc("B1");
        var theirs = await Loc("B2");
        async Task<Guid> Asset(string name, Guid site, Guid? location) =>
            Id(await Ok(await admin.PostAsJsonAsync("/api/assets", new { name, siteId = site, locationId = location, status = "Active", criticality = "Medium", warrantyType = "None" })));
        var seen = await Asset("Seen", w.O.Site, mine);
        var sameSiteHidden = await Asset("Same site, other branch", w.O.Site, theirs);
        var role = Id(await Ok(await admin.PostAsJsonAsync("/api/roles", new { name = "Branch editor", description = "", permissions = new[] { Permissions.ContractsView, Permissions.ContractsManage, Permissions.AssetsView, Permissions.LocationsView } })));
        var email = $"{Guid.NewGuid():N}@test.local";
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = "Branch", email, isActive = true, roleIds = new[] { role }, password = "Passw0rd!u", locationIds = new[] { mine } }));
        var editor = fx.Client();
        await Ok(await editor.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));

        // A hidden asset at the same site is no reason to refuse; one at another site is.
        var same = await ContractAsync(admin, Body(w.C, Today, Today.AddDays(300), assets: [seen, sameSiteHidden], title: "Same site"));
        await Ok(await editor.PutAsJsonAsync($"/api/contracts/{same}", WithVersion(Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [seen], title: "Same site"), await GetAsync(editor, same))));
        var far = await ContractAsync(admin, Body(w.C, Today, Today.AddDays(300), assets: [seen, w.B1], title: "Far"));
        Assert.Contains(HiddenCover, await Problem(await editor.PutAsJsonAsync($"/api/contracts/{far}",
            WithVersion(Body(w.C, Today, Today.AddDays(300), site: w.O.Site, assets: [seen], title: "Far"), await GetAsync(editor, far)))));
    }

    [SkippableFact]
    public async Task Amendment_changes_the_current_value_and_termination_needs_a_reason()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-10), Today.AddDays(300), autoRenew: true, renewalMonths: 12, noticeDays: 30));
        Assert.Contains("Describe the amendment.", await Problem(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/amendments", new { date = Today, description = "", newValue = 15000m })));
        await Ok(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/amendments", new { date = Today, description = "Price review", newValue = 15000m }));
        var k = await GetAsync(w.O.Admin, id);
        Assert.Equal(15000m, k.GetProperty("periods")[0].GetProperty("value").GetDecimal());
        Assert.Equal("Price review", k.GetProperty("amendments")[0].GetProperty("description").GetString());
        Assert.Equal(Today.AddDays(270), DateOnly.Parse(k.GetProperty("deadline").GetString()!));

        Assert.Contains("Give a reason.", await Problem(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/terminate", new { date = Today, reason = "" })));
        await Ok(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/terminate", new { date = Today, reason = "Closed site" }));
        k = await GetAsync(w.O.Admin, id);
        Assert.Equal("Terminated", k.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, k.GetProperty("deadline").ValueKind);
        Assert.Equal("Closed site", k.GetProperty("terminationReason").GetString());
    }

    [SkippableFact]
    public async Task Value_and_start_change_only_before_the_first_renewal()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var start = Today.AddDays(-400);
        var id = await ContractAsync(w.O.Admin, Body(w.C, start, start.AddDays(364), autoRenew: true, renewalMonths: 12));
        await Loop.DbAsync(fx, async db =>
        {
            var k = await db.Contracts.IgnoreQueryFilters().Include(x => x.Periods).FirstAsync(x => x.Id == id);
            var end = ContractTerms.PeriodEnd(k.EndDate.AddDays(1), 12);
            db.ContractPeriods.Add(new ContractPeriod { TenantId = k.TenantId, ContractId = id, Number = 2, StartDate = k.EndDate.AddDays(1), EndDate = end, Value = 12000, IsRenewal = true });
            k.EndDate = end;
            await db.SaveChangesAsync();
            return 0;
        });
        var current = await GetAsync(w.O.Admin, id);
        var end = DateOnly.Parse(current.GetProperty("endDate").GetString()!);
        Assert.Contains("Record the new value as an amendment.", await Problem(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}",
            WithVersion(Body(w.C, start, end, value: 13000, autoRenew: true, renewalMonths: 12), current))));
        Assert.Contains("The start date can't change after a renewal.", await Problem(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}",
            WithVersion(Body(w.C, start.AddDays(1), end, autoRenew: true, renewalMonths: 12), current))));
        // Moving the end moves the latest period's end.
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}", WithVersion(Body(w.C, start, end.AddDays(5), autoRenew: true, renewalMonths: 12), current)));
        var periods = (await GetAsync(w.O.Admin, id)).GetProperty("periods").EnumerateArray().ToList();
        Assert.Equal(end.AddDays(5), DateOnly.Parse(periods.Single(p => p.GetProperty("number").GetInt32() == 2).GetProperty("endDate").GetString()!));
    }

    [SkippableFact]
    public async Task Company_losing_contractor_role_keeps_existing_contracts()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300)));
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/suppliers/{w.C}", new { code = "LIFT", name = "LIFT Co", isSupplier = true, isContractor = false, isManufacturer = false, isActive = false }));
        var current = await GetAsync(w.O.Admin, id);
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}", WithVersion(Body(w.C, Today, Today.AddDays(300), title: "Renamed"), current)));
        Assert.Contains("Choose an active company with the Contractor role.", await Problem(await w.O.Admin.PostAsJsonAsync("/api/contracts", Body(w.C, Today, Today.AddDays(30)))));
    }

    [SkippableFact]
    public async Task Contract_named_on_work_cannot_be_deleted()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var used = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300)));
        var unused = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300)));
        var wo = Id(await Ok(await w.O.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Lift check", siteId = w.O.Site, type = "Corrective", priority = "Medium" })));
        await Loop.DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(x => x.Id == wo).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContractId, used)));
        Assert.Contains("This contract is named on work orders or PMs. Terminate it instead.", await Problem(await w.O.Admin.DeleteAsync($"/api/contracts/{used}")));
        Assert.Equal(HttpStatusCode.NoContent, (await w.O.Admin.DeleteAsync($"/api/contracts/{unused}")).StatusCode);
    }

    [SkippableFact]
    public async Task Contract_files_need_contract_permissions()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300)));
        await Ok(await UploadAsync(w.O.Admin, id));
        var (viewer, _, _) = await UserAsync(fx, w.O.Admin, "Viewer");
        Assert.Equal(1, (await Ok(await viewer.GetAsync($"/api/attachments?entityType=Contract&entityId={id}"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, id)).StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient c, Guid id)
    {
        using var form = new MultipartFormDataContent { { new StringContent("Contract"), "entityType" }, { new StringContent(id.ToString()), "entityId" } };
        var file = new ByteArrayContent("%PDF-1.4\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "contract.pdf");
        return await c.PostAsync("/api/attachments", form);
    }
}
