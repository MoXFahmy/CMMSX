using System.Net.Http.Json;
using FourC.Cmms.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class PushTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private PushSenderService Sender => fx.Factory!.Services.GetRequiredService<PushSenderService>();

    /// <summary>A technician with a CMX login, linked to an employee, whose phone registered <paramref name="token"/>.</summary>
    private async Task<(Loop.User User, Guid Employee)> TechWithPhoneAsync(Loop.Org org, string token, Guid[]? siteIds = null)
    {
        var tech = await UserAsync(fx, org.Admin, "Technician", siteIds: siteIds);
        var emp = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = $"T{Guid.NewGuid():N}"[..8], fullName = "Tech", userId = tech.Id, siteIds, isContractor = false })));
        await Ok(await tech.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        return (tech, emp);
    }

    private static Task<Guid> JobAsync(HttpClient c, Loop.Org org, Guid assignee, Guid? site = null) =>
        Task.Run(async () => Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "Check chiller", siteId = site ?? org.Site, priority = "High", type = "Corrective", assigneeIds = new[] { assignee } }))));

    [SkippableFact]
    public async Task An_assigned_technician_with_a_phone_gets_a_push()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"tok-{Guid.NewGuid():N}";
        var (_, emp) = await TechWithPhoneAsync(org, token);
        var wo = await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        var push = Assert.Single(fx.Pushes.To(token));
        Assert.Contains("Check chiller", push.Body);
        Assert.Equal($"/tech/job/{wo}", push.Link);
    }

    [SkippableFact]
    public async Task An_approved_request_pushes_to_the_requester()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var requester = await UserAsync(fx, org.Admin, "Requester");
        var token = $"tok-{Guid.NewGuid():N}";
        await Ok(await requester.Client.PostAsJsonAsync("/api/tech/push-token", new { token }));
        var id = Id(await Ok(await requester.Client.PostAsJsonAsync("/api/work-requests", new { title = "Lights out", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-requests/{id}/approve", new { }));
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Single(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Gone_tokens_are_removed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"gone-{Guid.NewGuid():N}";
        fx.Pushes.Gone[token] = true;
        var (tech, emp) = await TechWithPhoneAsync(org, token);
        await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
        var left = await DbAsync(fx, db => db.UserSessions.IgnoreQueryFilters().CountAsync(s => s.UserId == tech.Id && s.PushToken == token));
        Assert.Equal(0, left);
    }

    [SkippableFact]
    public async Task Pushes_respect_site_access()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other", isActive = true })));
        var token = $"tok-{Guid.NewGuid():N}";
        var (_, emp) = await TechWithPhoneAsync(org, token, [other]);
        await JobAsync(org.Admin, org, emp, org.Site);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Signing_out_stops_pushes_to_that_phone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var token = $"tok-{Guid.NewGuid():N}";
        var (tech, emp) = await TechWithPhoneAsync(org, token);
        await Ok(await tech.Client.PostAsync("/api/auth/logout", null));
        await JobAsync(org.Admin, org, emp);
        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(fx.Pushes.To(token));
    }

    [SkippableFact]
    public async Task Nothing_is_queued_without_a_key()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await using var bare = fx.Factory!.WithWebHostBuilder(b => b.UseSetting("Push:ServiceAccountJson", ""));
        var c = bare.CreateClient();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "No Push Co", fullName = "Admin", email = Addr("admin"), password = "Passw0rd!x" }));
        var tenant = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("tenant").GetProperty("id").GetGuid();
        var me = (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var emp = Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code = "ME", fullName = "Me", userId = me, isContractor = false })));
        await Ok(await c.PostAsJsonAsync("/api/tech/push-token", new { token = "tok-none" }));
        await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title = "x", siteId = site, priority = "Low", type = "Corrective", assigneeIds = new[] { emp } }));
        var queued = await DbAsync(fx, db => db.PushMessages.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenant));
        Assert.Equal(0, queued);
    }
}
