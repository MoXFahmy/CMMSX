using System.Net;
using System.Net.Http.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.ContractTests;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0 final review: the fixes for the background pass, amended values, the supplier import, alert recipients and who may read contractor data.</summary>
public class ContractReviewFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private ContractNotifier Notifier => fx.Factory!.Services.GetRequiredService<ContractNotifier>();
    private static DateOnly D(string s) => DateOnly.Parse(s);
    private static DateTimeOffset At(string day) => new(DateOnly.Parse(day), new TimeOnly(9, 0), TimeSpan.Zero);

    private async Task<List<OutboundEmail>> MailsAsync(Guid tenant)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && e.EventKey == EmailEvents.ContractAlert).ToListAsync();
    }

    private async Task<World> EmailWorldAsync()
    {
        var w = await WorldAsync(fx);
        await TestSettings.SetSettingsAsync(w.O.Admin, s => s["emailEnabled"] = true);
        return w;
    }

    [SkippableFact]
    public async Task One_contract_failing_to_save_does_not_spoil_the_others()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var other = await OrgAsync(fx);
        // Healthy contracts on both sides of the broken one, whatever order the pass takes them in.
        var healthy = new List<Guid>();
        for (var n = 0; n < 3; n++) healthy.Add(await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-01"), D("2026-01-31"), value: 100, autoRenew: true, renewalMonths: 1, title: $"Before {n}")));
        var a = await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-01"), D("2026-01-31"), value: 100, autoRenew: true, renewalMonths: 1, title: "Broken"));
        for (var n = 0; n < 3; n++) healthy.Add(await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-01"), D("2026-01-31"), value: 100, autoRenew: true, renewalMonths: 1, title: $"After {n}")));
        // A period row the organization can't see takes A's next number, so saving A's renewal fails every time.
        await Loop.DbAsync(fx, async db =>
        {
            db.ContractPeriods.Add(new Core.Contracts.ContractPeriod { TenantId = other.Tenant, ContractId = a, Number = 2, StartDate = D("2030-01-01"), EndDate = D("2030-01-31") });
            return await db.SaveChangesAsync();
        });
        await Notifier.RunOnceAsync(At("2026-03-15"), CancellationToken.None, w.O.Tenant);
        await Notifier.RunOnceAsync(At("2026-03-15"), CancellationToken.None, w.O.Tenant);
        foreach (var b in healthy)
        {
            var k = await Ok(await w.O.Admin.GetAsync($"/api/contracts/{b}"));
            Assert.Equal(["2026-01-01", "2026-02-01", "2026-03-01"], k.GetProperty("periods").EnumerateArray().Select(p => p.GetProperty("startDate").GetString()));
            Assert.Equal("2026-03-31", k.GetProperty("endDate").GetString());
        }
    }

    [SkippableFact]
    public async Task Two_passes_at_once_add_each_renewal_once()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var ids = new List<Guid>();
        for (var n = 0; n < 6; n++)
            ids.Add(await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-01"), D("2026-01-31"), value: 100, autoRenew: true, renewalMonths: 1, title: $"Monthly {n}")));
        await Task.WhenAll(Notifier.RunOnceAsync(At("2026-05-15"), CancellationToken.None, w.O.Tenant), Notifier.RunOnceAsync(At("2026-05-15"), CancellationToken.None, w.O.Tenant));
        await Notifier.RunOnceAsync(At("2026-05-15"), CancellationToken.None, w.O.Tenant);
        foreach (var id in ids)
        {
            var k = await Ok(await w.O.Admin.GetAsync($"/api/contracts/{id}"));
            var starts = k.GetProperty("periods").EnumerateArray().Select(p => p.GetProperty("startDate").GetString()).ToList();
            Assert.Equal(["2026-01-01", "2026-02-01", "2026-03-01", "2026-04-01", "2026-05-01"], starts);
            Assert.Equal("2026-05-31", k.GetProperty("endDate").GetString());
        }
    }

    [SkippableFact]
    public async Task Amended_value_survives_an_edit_and_shows_as_the_contract_value()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var id = await ContractAsync(w.O.Admin, Body(w.C, Today, Today.AddDays(300), value: 12000));
        await Ok(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/amendments", new { date = Today, description = "Price review", newValue = 15000m }));
        var k = await Ok(await w.O.Admin.GetAsync($"/api/contracts/{id}"));
        Assert.Equal(15000m, k.GetProperty("value").GetDecimal());
        var body = Body(w.C, Today, Today.AddDays(300), value: k.GetProperty("value").GetDecimal(), title: "Renamed");
        body["rowVersion"] = k.GetProperty("rowVersion").GetString();
        await Ok(await w.O.Admin.PutAsJsonAsync($"/api/contracts/{id}", body));
        k = await Ok(await w.O.Admin.GetAsync($"/api/contracts/{id}"));
        Assert.Equal(15000m, k.GetProperty("periods")[0].GetProperty("value").GetDecimal());
        var listed = (await Ok(await w.O.Admin.GetAsync("/api/contracts"))).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(15000m, listed.GetProperty("value").GetDecimal());
    }

    [SkippableFact]
    public async Task Importing_a_supplier_keeps_its_roles()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var lift = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/suppliers", new { code = "LIFT", name = "LIFT Co", isSupplier = false, isContractor = true, isManufacturer = true })));
        await RunAsync(o.Admin, Book(new Sheet("Suppliers", ["Code", "Name", "Phone", "Contractor"], ["LIFT", "LIFT Co", "0100 222", "Yes"])));
        var s = await Ok(await o.Admin.GetAsync($"/api/suppliers/{lift}"));
        Assert.Equal("0100 222", s.GetProperty("phone").GetString());
        Assert.False(s.GetProperty("isSupplier").GetBoolean());
        Assert.True(s.GetProperty("isContractor").GetBoolean());
        Assert.True(s.GetProperty("isManufacturer").GetBoolean());
    }

    [SkippableFact]
    public async Task Alert_falls_back_to_contract_managers_when_the_manager_cannot_receive_it()
    {
        Skip.IfNot(fx.Enabled);
        var w = await EmailWorldAsync();
        var (_, mm, mmEmail) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        var (_, gone, goneEmail) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        var (_, elsewhere, elsewhereEmail) = await UserAsync(fx, w.O.Admin, "Site A manager", [Core.Identity.Permissions.ContractsView, Core.Identity.Permissions.ContractsManage],
            siteIds: [w.O.Site]);
        await Loop.DbAsync(fx, async db => { (await db.Users.FirstAsync(u => u.Id == gone)).IsActive = false; return await db.SaveChangesAsync(); });
        var left = await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), manager: gone, title: "Manager left"));
        var atB = await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), site: w.SiteB, manager: elsewhere, title: "Manager can't see B"));
        await Notifier.RunOnceAsync(At("2027-11-15"), CancellationToken.None, w.O.Tenant);
        var mails = await MailsAsync(w.O.Tenant);
        Assert.Contains(mails, m => m.EntityId == left && m.ToAddress == mmEmail);
        Assert.Contains(mails, m => m.EntityId == atB && m.ToAddress == mmEmail);
        Assert.DoesNotContain(mails, m => m.ToAddress == goneEmail);
        // The site-A manager may manage contracts, so the organization-wide one reaches them as a fallback; site B's never does.
        Assert.DoesNotContain(mails, m => m.EntityId == atB && m.ToAddress == elsewhereEmail);
        Assert.NotEqual(Guid.Empty, mm);
    }

    [SkippableFact]
    public async Task Alerts_reach_contracts_beyond_the_first_batch()
    {
        Skip.IfNot(fx.Enabled);
        var w = await EmailWorldAsync();
        var (_, manager, _) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        for (var n = 0; n < 3; n++) await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), manager: manager, title: $"Contract {n}"));
        Assert.Equal(2, (await Notifier.RunOnceAsync(At("2027-11-15"), CancellationToken.None, w.O.Tenant, maxPerRun: 2)).Emails);
        Assert.Equal(1, (await Notifier.RunOnceAsync(At("2027-11-16"), CancellationToken.None, w.O.Tenant, maxPerRun: 2)).Emails);
    }

    [SkippableFact]
    public async Task Contract_options_and_contractor_rates_need_permission()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(fx);
        var elec = await CategoryAsync(w.O.Admin, "ELEC", 100);
        await ContractorAsync(w.O.Admin, "RATED", (elec, 150m));
        var (requester, _, _) = await UserAsync(fx, w.O.Admin, "Requester");
        var (tech, _, _) = await UserAsync(fx, w.O.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync($"/api/contracts/options?siteId={w.O.Site}")).StatusCode);
        await Ok(await tech.GetAsync($"/api/contracts/options?siteId={w.O.Site}"));
        var seen = (await Ok(await requester.GetAsync("/api/suppliers/lookup?role=contractor"))).EnumerateArray().ToList();
        Assert.All(seen, c => { Assert.Empty(c.GetProperty("rates").EnumerateArray()); Assert.Empty(c.GetProperty("contacts").EnumerateArray()); });
        var techSees = (await Ok(await tech.GetAsync("/api/suppliers/lookup?role=contractor"))).EnumerateArray().Single(c => c.GetProperty("code").GetString() == "RATED");
        Assert.Equal(150m, techSees.GetProperty("rates")[0].GetProperty("rate").GetDecimal());
    }
}
