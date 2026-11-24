using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.26.1: activity log and privacy fixes left by 0.25.0's review.</summary>
public class ActivityFixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private Task Deactivate(Guid? employeeId = null, Guid? userId = null) => DbAsync(fx, async db =>
    {
        if (employeeId is { } e) await db.Employees.IgnoreQueryFilters().Where(x => x.Id == e).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (userId is { } u) await db.Users.IgnoreQueryFilters().Where(x => x.Id == u).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        return 0;
    });

    private static async Task<JsonElement> Log(HttpClient c, string query) => (await Ok(await c.GetAsync($"/api/activity?{query}"))).GetProperty("items");

    [SkippableFact]
    public async Task Anonymise_reaches_the_email_log()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var email = Addr("nadia");
        var nadia = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "E-031", fullName = "Nadia Fawzy", email, hourlyRate = 10m, isActive = true })));
        var row = await DbAsync(fx, async db =>
        {
            var m = new OutboundEmail { TenantId = org.Tenant, EventKey = EmailEvents.WorkOrderAssigned, ToAddress = email, ToName = "Nadia Fawzy", Subject = "WO-00001", HtmlBody = "x", TextBody = "x" };
            db.OutboundEmails.Add(m);
            await db.SaveChangesAsync();
            return m.Id;
        });
        await Deactivate(nadia);
        await Ok(await org.Admin.PostAsJsonAsync("/api/anonymise", new { employeeId = nadia, confirm = "E-031" }));
        var after = await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == row));
        Assert.EndsWith("@invalid", after.ToAddress);
        Assert.Equal("Former employee E-031", after.ToName);
    }

    [SkippableFact]
    public async Task Anonymised_pending_emails_are_not_sent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var email = Addr("salma");
        var salma = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "E-033", fullName = "Salma Ezz", email, hourlyRate = 10m, isActive = true })));
        var (pending, sent) = await DbAsync(fx, async db =>
        {
            var p = new OutboundEmail { TenantId = org.Tenant, EventKey = EmailEvents.WorkOrderAssigned, ToAddress = email, ToName = "Salma Ezz", Subject = "WO-00002", HtmlBody = "x", TextBody = "x" };
            var s = new OutboundEmail { TenantId = org.Tenant, EventKey = EmailEvents.WorkOrderAssigned, ToAddress = email, ToName = "Salma Ezz", Subject = "WO-00001", HtmlBody = "x", TextBody = "x",
                Status = EmailStatus.Sent, SentAt = DateTimeOffset.UtcNow };
            db.OutboundEmails.AddRange(p, s);
            await db.SaveChangesAsync();
            return (p.Id, s.Id);
        });
        await Deactivate(salma);
        await Ok(await org.Admin.PostAsJsonAsync("/api/anonymise", new { employeeId = salma, confirm = "E-033" }));
        var rows = await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(m => m.Id == pending || m.Id == sent).ToDictionaryAsync(m => m.Id, m => m.Status));
        Assert.Equal((EmailStatus.Failed, EmailStatus.Sent), (rows[pending], rows[sent]));
    }

    [SkippableFact]
    public async Task Anonymise_ignores_a_second_sign_in()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var omar = await UserAsync(fx, org.Admin, "Viewer", name: "Omar Adel");
        var hany = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/employees", new { code = "E-032", fullName = "Hany Nabil", email = Addr("hany"), hourlyRate = 10m, isActive = true })));
        await Deactivate(hany, omar.Id);
        await Ok(await org.Admin.PostAsJsonAsync("/api/anonymise", new { employeeId = hany, userId = omar.Id, confirm = "E-032" }));
        var user = await DbAsync(fx, db => db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == omar.Id));
        Assert.Equal(("Omar Adel", (DateTimeOffset?)null), (user.FullName, user.AnonymisedAt));
    }

    [SkippableFact]
    public async Task Search_finds_arabic_values()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Pump", siteId = org.Site, locationId = org.Location, type = "Corrective", priority = "Low" })));
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "تغيير مضخة", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" }));
        // Renamed back: only the changed values hold the Arabic now, not the record's title.
        await Ok(await org.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new { title = "Pump swap", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" }));
        var found = await Log(org.Admin, $"search={Uri.EscapeDataString("مضخة")}&entityType=WorkOrder");
        Assert.Contains(found.EnumerateArray(), e => e.GetProperty("entityId").GetGuid() == wo && e.GetProperty("action").GetString() == "Updated");
    }

    [SkippableFact]
    public async Task Dates_are_local_days()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["timeZone"] = "Asia/Riyadh")); // UTC+3, no daylight saving
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var late = new DateTimeOffset(day.AddDays(-1).ToDateTime(new TimeOnly(22, 30)), TimeSpan.Zero); // 01:30 on `day` in Riyadh
        var entry = await DbAsync(fx, async db =>
        {
            var a = new AuditEntry { TenantId = org.Tenant, EntityType = "Asset", EntityId = Guid.NewGuid(), Action = "Updated", Timestamp = late, ChangeId = Guid.NewGuid() };
            db.AuditEntries.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        var d = day.ToString("yyyy-MM-dd");
        Assert.Contains((await Log(org.Admin, $"from={d}&to={d}")).EnumerateArray(), e => e.GetProperty("id").GetGuid() == entry);
        var before = day.AddDays(-1).ToString("yyyy-MM-dd");
        Assert.DoesNotContain((await Log(org.Admin, $"from={before}&to={before}")).EnumerateArray(), e => e.GetProperty("id").GetGuid() == entry);
    }

    [SkippableFact]
    public async Task Renew_failures_are_logged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var c = fx.Client();
        var r = await c.PostAsJsonAsync("/api/auth/renew-password", new { email = org.AdminEmail, password = "Wrong0ne!x", newPassword = "Renewed0ne!x", rememberMe = false });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var failed = await DbAsync(fx, db => db.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.TenantId == org.Tenant && a.EntityId == org.AdminId && a.Action == "SignInFailed"));
        Assert.Equal(1, failed);
    }

    [SkippableFact]
    public async Task Storage_list_follows_sites()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var other = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/sites", new { code = "S2", name = "Other site", isActive = true })));
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Boiler", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        using (var form = new MultipartFormDataContent { { new StringContent("Asset"), "entityType" }, { new StringContent(asset.ToString()), "entityId" } })
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "files", "boiler-front.png");
            await Ok(await org.Admin.PostAsync("/api/attachments", form));
        }
        var limited = await UserAsync(fx, org.Admin, "Administrator", [other]);
        var files = (await Ok(await limited.Client.GetAsync("/api/storage/files"))).GetProperty("items");
        Assert.DoesNotContain(files.EnumerateArray(), f => f.GetProperty("fileName").GetString() == "boiler-front.png");
        Assert.Equal(1, (await Ok(await limited.Client.GetAsync("/api/storage"))).GetProperty("files").GetInt32());
        Assert.Contains((await Ok(await org.Admin.GetAsync("/api/storage/files"))).GetProperty("items").EnumerateArray(), f => f.GetProperty("fileName").GetString() == "boiler-front.png");
    }

    [SkippableFact]
    public async Task People_filter_without_manage_users()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var (settings, _, _) = await PurchaseRequestTests.UserAsync(fx, org.Admin, "Settings only", ["settings.manage"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await settings.GetAsync("/api/users")).StatusCode);
        var people = (await Ok(await settings.GetAsync("/api/activity/people"))).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(org.AdminId, people);
    }

    [SkippableFact]
    public async Task Labels_stay_inside_the_organization()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var a = await OrgAsync(fx, emailOn: false);
        var b = await OrgAsync(fx, emailOn: false);
        var theirs = Id(await Ok(await b.Admin.PostAsJsonAsync("/api/assets", new { name = "Secret press", siteId = b.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        using var scope = fx.Factory!.Services.CreateScope();
        await using var db = new CmmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>(), new FixedTenantContext(a.Tenant));
        Assert.Empty(await FourC.Cmms.Api.Features.ActivityLabels.ResolveAsync(db, [("Asset", theirs)], default));
    }
}
