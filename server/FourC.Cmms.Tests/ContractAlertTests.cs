using System.Net.Http.Json;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.ContractTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.17.0: automatic renewal, the alert e-mail (once per deadline) and the bell's contracts needing attention.</summary>
public class ContractAlertTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private ContractNotifier Notifier => fx.Factory!.Services.GetRequiredService<ContractNotifier>();
    private static DateOnly D(string s) => DateOnly.Parse(s);
    private static DateTimeOffset At(string day) => new(DateOnly.Parse(day), new TimeOnly(9, 0), TimeSpan.Zero);

    private Task<(int Renewed, int Emails)> RunAsync(Guid tenant, string day) => Notifier.RunOnceAsync(At(day), CancellationToken.None, tenant);

    private async Task<List<OutboundEmail>> MailsAsync(Guid tenant)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenant && e.EventKey == EmailEvents.ContractAlert).ToListAsync();
    }

    private async Task<World> WorldAsync(bool email)
    {
        var w = await ContractTests.WorldAsync(fx);
        if (email) await TestSettings.SetSettingsAsync(w.O.Admin, s => s["emailEnabled"] = true);
        return w;
    }

    [SkippableFact]
    public async Task Auto_renewal_catches_up_missed_periods_and_month_ends()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: false);
        var id = await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-31"), D("2026-02-28"), value: 1000, autoRenew: true, renewalMonths: 1));
        var (renewed, _) = await RunAsync(w.O.Tenant, "2026-06-15");
        Assert.Equal(4, renewed);
        var k = await Ok(await w.O.Admin.GetAsync($"/api/contracts/{id}"));
        var periods = k.GetProperty("periods").EnumerateArray().Select(p => (p.GetProperty("number").GetInt32(), D(p.GetProperty("startDate").GetString()!),
            D(p.GetProperty("endDate").GetString()!), p.GetProperty("value").GetDecimal(), p.GetProperty("isRenewal").GetBoolean())).ToList();
        Assert.Equal([
            (1, D("2026-01-31"), D("2026-02-28"), 1000m, false), (2, D("2026-03-01"), D("2026-03-31"), 1000m, true), (3, D("2026-04-01"), D("2026-04-30"), 1000m, true),
            (4, D("2026-05-01"), D("2026-05-31"), 1000m, true), (5, D("2026-06-01"), D("2026-06-30"), 1000m, true),
        ], periods);
        Assert.Equal(D("2026-06-30"), D(k.GetProperty("endDate").GetString()!));
        Assert.Equal(0, (await RunAsync(w.O.Tenant, "2026-06-15")).Renewed);
    }

    [SkippableFact]
    public async Task Alert_email_goes_once_per_deadline_and_again_after_renewal()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: true);
        var (_, manager, managerEmail) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), autoRenew: true, renewalMonths: 12, noticeDays: 30, alertDays: 60, manager: manager));
        Assert.Equal(1, (await RunAsync(w.O.Tenant, "2027-10-15")).Emails);
        var first = Assert.Single(await MailsAsync(w.O.Tenant));
        Assert.Equal(managerEmail, first.ToAddress);
        Assert.Contains("CON-00001", first.Subject);
        Assert.Equal(0, (await RunAsync(w.O.Tenant, "2027-10-20")).Emails);
        var after = await RunAsync(w.O.Tenant, "2028-10-15");
        Assert.Equal(1, after.Renewed);
        Assert.Equal(1, after.Emails);
        Assert.Equal(2, (await MailsAsync(w.O.Tenant)).Count);
    }

    [SkippableFact]
    public async Task Contract_without_renewal_alerts_before_its_end()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: true);
        var (_, manager, _) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), alertDays: 60, manager: manager));
        Assert.Equal(0, (await RunAsync(w.O.Tenant, "2027-10-31")).Emails);
        Assert.Equal(1, (await RunAsync(w.O.Tenant, "2027-11-01")).Emails);
    }

    [SkippableFact]
    public async Task Terminated_contract_neither_renews_nor_alerts()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: true);
        var (_, manager, _) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        var id = await ContractAsync(w.O.Admin, Body(w.C, D("2026-01-01"), D("2026-03-31"), autoRenew: true, renewalMonths: 3, noticeDays: 10, manager: manager));
        await Ok(await w.O.Admin.PostAsJsonAsync($"/api/contracts/{id}/terminate", new { date = D("2026-09-30"), reason = "Moving out" }));
        var run = await RunAsync(w.O.Tenant, "2026-05-01");
        Assert.Equal((0, 0), run);
        Assert.Single((await Ok(await w.O.Admin.GetAsync($"/api/contracts/{id}"))).GetProperty("periods").EnumerateArray());
    }

    [SkippableFact]
    public async Task Without_manager_or_recipients_contract_managers_are_emailed()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: true);
        var (_, viewerId, _) = await UserAsync(fx, w.O.Admin, "Viewer");
        var (_, mmId, mmEmail) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        var (_, optedId, _) = await UserAsync(fx, w.O.Admin, "Maintenance Manager");
        await Loop.DbAsync(fx, async db =>
        {
            var u = await db.Users.FirstAsync(x => x.Id == optedId);
            u.EmailOptOuts = [EmailEvents.ContractAlert];
            return await db.SaveChangesAsync();
        });
        await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-31"), title: "No manager"));
        await RunAsync(w.O.Tenant, "2027-12-15");
        var to = (await MailsAsync(w.O.Tenant)).Select(m => m.ToAddress).ToList();
        Assert.Contains(mmEmail, to);
        Assert.DoesNotContain(await EmailOfAsync(optedId), to);
        Assert.DoesNotContain(await EmailOfAsync(viewerId), to);

        // Named recipients replace "everyone who may manage contracts".
        await TestSettings.SetSettingsAsync(w.O.Admin, s => s["contractAlertRecipientIds"] = new System.Text.Json.Nodes.JsonArray(viewerId.ToString()));
        await ContractAsync(w.O.Admin, Body(w.C, D("2027-01-01"), D("2027-12-30"), title: "With recipients"));
        var before = (await MailsAsync(w.O.Tenant)).Select(m => m.Id).ToHashSet();
        await RunAsync(w.O.Tenant, "2027-12-16");
        var added = (await MailsAsync(w.O.Tenant)).Where(m => !before.Contains(m.Id)).Select(m => m.ToAddress).ToList();
        Assert.Equal([await EmailOfAsync(viewerId)], added);
        Assert.DoesNotContain(mmEmail, added);
        Assert.NotEqual(Guid.Empty, mmId);
    }

    private Task<string> EmailOfAsync(Guid user) => Loop.DbAsync(fx, db => db.Users.Where(u => u.Id == user).Select(u => u.Email!).FirstAsync());

    [SkippableFact]
    public async Task Bell_and_alert_emails_respect_contract_sites()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: true);
        var (siteA, siteAId, siteAEmail) = await UserAsync(fx, w.O.Admin, "Site A", [Permissions.ContractsView, Permissions.ContractsManage], siteIds: [w.O.Site]);
        await TestSettings.SetSettingsAsync(w.O.Admin, s => s["contractAlertRecipientIds"] = new System.Text.Json.Nodes.JsonArray(siteAId.ToString()));
        // At site B, in its alert window today.
        await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-100), Today.AddDays(10), site: w.SiteB, alertDays: 30));
        Assert.Equal(0, (await Ok(await siteA.GetAsync("/api/alerts"))).GetProperty("contractsNeedingAttention").GetInt32());
        Assert.Equal(1, (await Ok(await w.O.Admin.GetAsync("/api/alerts"))).GetProperty("contractsNeedingAttention").GetInt32());
        await Notifier.RunOnceAsync(DateTimeOffset.UtcNow, CancellationToken.None, w.O.Tenant);
        Assert.DoesNotContain(siteAEmail, (await MailsAsync(w.O.Tenant)).Select(m => m.ToAddress));
    }

    [SkippableFact]
    public async Task Bell_shows_contracts_needing_attention_only_to_viewers()
    {
        Skip.IfNot(fx.Enabled);
        var w = await WorldAsync(email: false);
        await ContractAsync(w.O.Admin, Body(w.C, Today.AddDays(-100), Today.AddDays(10), alertDays: 30));
        var (tech, _, _) = await UserAsync(fx, w.O.Admin, "Technician");
        var (requester, _, _) = await UserAsync(fx, w.O.Admin, "Requester");
        Assert.Equal(1, (await Ok(await tech.GetAsync("/api/alerts"))).GetProperty("contractsNeedingAttention").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Ok(await requester.GetAsync("/api/alerts"))).GetProperty("contractsNeedingAttention").ValueKind);
    }
}
