using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: contractor lines on work orders, their copied rates, who may change what, and the contractor totals.</summary>
public class ContractorLineTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    /// <summary>An organization with ELEC (default 100), contractor C (its ELEC rate 150), employee E linked to C at 80, staff S at 50 and an open work order.</summary>
    internal sealed record Setup(PurchaseRequestTests.Org O, Guid Elec, Guid C, Guid E, Guid S, Guid Wo);

    internal static async Task<Setup> SetupAsync(ApiFixture fx, Guid? assetId = null)
    {
        var o = await OrgAsync(fx);
        var elec = await CategoryAsync(o.Admin, "ELEC", 100);
        var c = await ContractorAsync(o.Admin, "LIFT", (elec, 150m));
        var e = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Lift tech", isContractor = true, hourlyRate = 80m, companyId = c })));
        var s = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code = "S1", fullName = "Staff tech", isContractor = false, hourlyRate = 50m })));
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Lift service", siteId = o.Site, type = "Corrective", priority = "Medium", assetId })));
        return new Setup(o, elec, c, e, s, wo);
    }

    internal static object Line(Guid company, Guid? category = null, decimal planned = 0, decimal done = 0, decimal? rate = null, DateOnly? doneOn = null, Guid? contact = null, string? person = null) =>
        new { supplierId = company, categoryId = category, plannedHours = planned, hoursDone = done, rate, doneOn, contactId = contact, personName = person };

    internal static async Task<Guid> AddLineAsync(HttpClient c, Guid wo, object line) => Id(await Ok(await c.PostAsJsonAsync($"/api/work-orders/{wo}/contractors", line)));

    private static async Task<JsonElement> WoAsync(HttpClient c, Guid wo) => await Ok(await c.GetAsync($"/api/work-orders/{wo}"));
    private static JsonElement LineOf(JsonElement w, Guid id) => w.GetProperty("contractors").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [SkippableFact]
    public async Task Planner_adds_a_line_with_the_company_rate_and_planned_hours()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var id = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 4));
        var w = await WoAsync(x.O.Admin, x.Wo);
        var l = LineOf(w, id);
        Assert.Equal(150m, l.GetProperty("rate").GetDecimal());
        Assert.Equal(600m, l.GetProperty("plannedCost").GetDecimal());
        Assert.Equal("LIFT Co", l.GetProperty("companyName").GetString());
        Assert.Equal("ELEC work", l.GetProperty("categoryName").GetString());
        Assert.Equal(4m, w.GetProperty("planned").GetProperty("contractorHours").GetDecimal());
        Assert.Equal(600m, w.GetProperty("planned").GetProperty("contractorCost").GetDecimal());
    }

    [SkippableFact]
    public async Task Category_default_rate_is_used_when_the_company_has_none()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var d = await ContractorAsync(x.O.Admin, "DOOR");
        var withCategory = await AddLineAsync(x.O.Admin, x.Wo, Line(d, x.Elec, planned: 1));
        var bare = await AddLineAsync(x.O.Admin, x.Wo, Line(d, planned: 1));
        var given = await AddLineAsync(x.O.Admin, x.Wo, Line(d, x.Elec, planned: 1, rate: 175));
        var w = await WoAsync(x.O.Admin, x.Wo);
        Assert.Equal(100m, LineOf(w, withCategory).GetProperty("rate").GetDecimal());
        Assert.Equal(0m, LineOf(w, bare).GetProperty("rate").GetDecimal());
        Assert.Equal(175m, LineOf(w, given).GetProperty("rate").GetDecimal());
    }

    [SkippableFact]
    public async Task Technician_records_hours_done_but_cannot_change_the_rest()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var id = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 4));
        var (tech, _, _) = await UserAsync(fx, x.O.Admin, "Doer", [Core.Identity.Permissions.WorkOrdersView, Core.Identity.Permissions.WorkOrdersExecute]);
        var contact = (await Ok(await x.O.Admin.GetAsync($"/api/suppliers/{x.C}"))).GetProperty("contacts")[0].GetProperty("id").GetGuid();

        await Ok(await tech.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{id}", Line(x.C, x.Elec, planned: 4, done: 3, contact: contact)));
        var l = LineOf(await WoAsync(x.O.Admin, x.Wo), id);
        Assert.Equal(Today, DateOnly.Parse(l.GetProperty("doneOn").GetString()!));
        Assert.Equal(450m, l.GetProperty("cost").GetDecimal());
        Assert.Equal("Omar Lift", l.GetProperty("personName").GetString());

        Assert.Contains("You can record the hours done, the date and the person; ask a planner to change the rest.",
            await Problem(await tech.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{id}", Line(x.C, x.Elec, planned: 9, done: 3))));
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, x.Elec, planned: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.DeleteAsync($"/api/work-orders/{x.Wo}/contractors/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Line_checks_hours_dates_and_contacts()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var other = await ContractorAsync(x.O.Admin, "DOOR");
        var foreignContact = (await Ok(await x.O.Admin.GetAsync($"/api/suppliers/{other}"))).GetProperty("contacts")[0].GetProperty("id").GetGuid();
        var c = x.O.Admin;
        Assert.Contains("Hours cannot be negative.", await Problem(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, planned: -1))));
        Assert.Contains("Rate cannot be negative.", await Problem(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, planned: 1, rate: -5))));
        Assert.Contains("Date done cannot be in the future.", await Problem(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, done: 1, doneOn: Today.AddDays(3)))));
        Assert.Contains("Choose a contact of this company.", await Problem(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, planned: 1, contact: foreignContact))));
        Assert.Contains("Choose an active company with the Contractor role.", await Problem(await c.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.O.Supplier, planned: 1))));
    }

    [SkippableFact]
    public async Task Lines_on_closed_work_are_refused()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var id = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 4));
        var reason = (await Ok(await x.O.Admin.GetAsync("/api/cancel-reasons")))[0].GetProperty("id").GetGuid();
        await Ok(await x.O.Admin.PostAsJsonAsync($"/api/work-orders/{x.Wo}/status", new { status = "Cancelled", note = "Not needed", cancelReasonId = reason }));
        Assert.Contains("This work order is closed.", await Problem(await x.O.Admin.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, planned: 1))));
        Assert.Contains("This work order is closed.", await Problem(await x.O.Admin.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{id}", Line(x.C, x.Elec, planned: 4, done: 1))));
    }

    [SkippableFact]
    public async Task Work_order_totals_move_company_linked_labour_to_contractor()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, done: 2));
        await Ok(await x.O.Admin.PostAsJsonAsync($"/api/work-orders/{x.Wo}/labor", new { employeeId = x.E, workDate = Today, hours = 1, overtimeHours = 0 }));
        await Ok(await x.O.Admin.PostAsJsonAsync($"/api/work-orders/{x.Wo}/labor", new { employeeId = x.S, workDate = Today, hours = 2, overtimeHours = 0 }));
        var w = await WoAsync(x.O.Admin, x.Wo);
        var t = w.GetProperty("totals");
        Assert.Equal(100m, t.GetProperty("laborCost").GetDecimal());
        Assert.Equal(3m, t.GetProperty("laborHours").GetDecimal());
        Assert.Equal(380m, t.GetProperty("contractorCost").GetDecimal());
        Assert.Equal(2m, t.GetProperty("contractorHours").GetDecimal());
        Assert.Equal(480m, t.GetProperty("totalCost").GetDecimal());
        var linked = w.GetProperty("labor").EnumerateArray().Single(l => l.GetProperty("employeeId").GetGuid() == x.E);
        Assert.Equal("LIFT Co", linked.GetProperty("companyName").GetString());
    }

    [SkippableFact]
    public async Task Rate_change_never_changes_recorded_lines()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var id = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 2));
        var company = await Ok(await x.O.Admin.GetAsync($"/api/suppliers/{x.C}"));
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/suppliers/{x.C}", new
        {
            code = "LIFT", name = "LIFT Co", isSupplier = false, isContractor = true, isManufacturer = false, isActive = true,
            contacts = company.GetProperty("contacts").EnumerateArray().Select(c => new { id = c.GetProperty("id").GetGuid(), name = c.GetProperty("name").GetString() }).ToArray(),
            rates = new[] { new { categoryId = x.Elec, rate = 200m } },
        }));
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/contractor-categories/{x.Elec}", new { code = "ELEC", name = "ELEC work", defaultRate = 300m, isActive = true }));
        Assert.Equal(150m, LineOf(await WoAsync(x.O.Admin, x.Wo), id).GetProperty("rate").GetDecimal());
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{id}", Line(x.C, x.Elec, planned: 2, done: 1)));
        Assert.Equal(150m, LineOf(await WoAsync(x.O.Admin, x.Wo), id).GetProperty("rate").GetDecimal());
    }

    [SkippableFact]
    public async Task Company_losing_contractor_role_keeps_existing_lines()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var id = await AddLineAsync(x.O.Admin, x.Wo, Line(x.C, x.Elec, planned: 2));
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/suppliers/{x.C}", new { code = "LIFT", name = "LIFT Co", isSupplier = true, isContractor = false, isManufacturer = false, isActive = false }));
        await Ok(await x.O.Admin.PutAsJsonAsync($"/api/work-orders/{x.Wo}/contractors/{id}", Line(x.C, x.Elec, planned: 3)));
        Assert.Contains("Choose an active company with the Contractor role.", await Problem(await x.O.Admin.PostAsJsonAsync($"/api/work-orders/{x.Wo}/contractors", Line(x.C, x.Elec, planned: 1))));
        Assert.Empty((await Ok(await x.O.Admin.GetAsync("/api/suppliers/lookup?role=contractor"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task Work_group_rolls_up_contractor_cost()
    {
        Skip.IfNot(fx.Enabled);
        var x = await SetupAsync(fx);
        var c = x.O.Admin;
        var assets = new[] { await Pm12.AssetAsync(c, x.O.Site, "Lift 1"), await Pm12.AssetAsync(c, x.O.Site, "Lift 2") };
        var pm = Id(await Pm12.SaveScheduleAsync(c, Pm12.Schedule(x.O.Site, await Pm12.TemplateAsync(c, "LIFTPM"), Today, assets: assets,
            extra: new() { ["multiAssetMode"] = "Group" })));
        var group = (await Pm12.GenerateAsync(c, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        var child = await Loop.DbAsync(fx, db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).OrderBy(w => w.Number).Select(w => w.Id)));
        await AddLineAsync(c, child, Line(x.C, x.Elec, done: 2));
        var g = await Ok(await c.GetAsync($"/api/work-groups/{group}"));
        var row = g.GetProperty("children").EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == child);
        Assert.Equal(300m, row.GetProperty("contractorCost").GetDecimal());
        Assert.Equal(300m, row.GetProperty("cost").GetDecimal());
        Assert.Equal(300m, g.GetProperty("totals").GetProperty("contractorCost").GetDecimal());
        Assert.Equal(300m, g.GetProperty("totals").GetProperty("totalCost").GetDecimal());
    }
}
