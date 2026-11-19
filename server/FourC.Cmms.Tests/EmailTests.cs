using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

public class EmailTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who = "x") => $"{who}-{Guid.NewGuid():N}@cmms.test";
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private EmailSenderService Sender => fx.Factory!.Services.GetRequiredService<EmailSenderService>();

    /// <summary>A new organization with its admin signed in; e-mail switched on unless <paramref name="enable"/> is false.</summary>
    private async Task<(HttpClient admin, Guid tenant, Guid adminId, string adminEmail, Guid site)> OrgAsync(bool enable = true)
    {
        var c = fx.Client();
        var email = Addr("admin");
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Mail Co", fullName = "Admin", email, password = "Passw0rd!x" }));
        var me = await Ok(await c.GetAsync("/api/auth/me"));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        if (enable) await TestSettings.SetSettingsAsync(c, s => s["emailEnabled"] = true);
        return (c, me.GetProperty("tenant").GetProperty("id").GetGuid(), me.GetProperty("user").GetProperty("id").GetGuid(), email, site);
    }

    private static async Task<Guid> EmployeeAsync(HttpClient c, string code, string name, string? email, Guid? userId = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/employees", new { code, fullName = name, email, userId, isContractor = false })));

    /// <summary>A signed-in user with one of the organization's default roles.</summary>
    private async Task<(HttpClient client, Guid id)> UserAsync(HttpClient admin, string role, string email, decimal? approvalLimit = null)
    {
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = role, email, isActive = true, roleIds = new[] { roleId }, password = "Passw0rd!u", purchaseApprovalLimit = approvalLimit }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!u", rememberMe = false }));
        return (c, (await Ok(await c.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> NewWorkOrderAsync(HttpClient c, Guid site, Guid[] assignees, string title = "Replace seal") =>
        await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, type = "Corrective", priority = "High", siteId = site, assigneeIds = assignees, dueDate = Iso(Today.AddDays(2)) }));

    private static async Task<Guid> SupplierAsync(HttpClient c, string? email) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = $"S{Guid.NewGuid():N}"[..10], name = "Filters Ltd", email, isContractor = false })));

    /// <summary>A draft PO with one service line (quantity × price), optionally charged to a work order.</summary>
    private static async Task<Guid> PurchaseOrderAsync(HttpClient c, Guid site, Guid supplier, string description, decimal quantity, decimal price, Guid? workOrder = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = site, supplierId = supplier,
            lines = new[] { new { description, quantity, unitPrice = price, discountPercent = 0, workOrderId = workOrder } },
        })));

    private async Task QueueRawAsync(Guid tenant, IEnumerable<string> to)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        foreach (var a in to)
            db.OutboundEmails.Add(new OutboundEmail { TenantId = tenant, EventKey = EmailEvents.Test, ToAddress = a, Subject = "Hello", HtmlBody = "<p>Hello</p>", TextBody = "Hello" });
        await db.SaveChangesAsync();
    }

    private async Task<List<OutboundEmail>> RowsAsync(params string[] to)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => to.Contains(e.ToAddress)).ToListAsync();
    }

    [SkippableFact]
    public async Task Nothing_is_queued_until_the_organization_switches_email_on()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync(enable: false);
        var x = Addr("tech");
        await NewWorkOrderAsync(admin, site, [await EmployeeAsync(admin, "E1", "Tech X", x)]);
        await Sender.RunOnceAsync();
        Assert.Empty(await RowsAsync(x));
        Assert.Empty(fx.Emails.To(x));
    }

    [SkippableFact]
    public async Task Assignees_hear_once_and_the_actor_never_does()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, adminId, adminEmail, site) = await OrgAsync();
        var x = Addr("x");
        var ex = await EmployeeAsync(admin, "EX", "Tech X", x);
        var ey = await EmployeeAsync(admin, "EY", "Tech Y", null);           // no address, no user: skipped quietly
        var ez = await EmployeeAsync(admin, "EZ", "Admin", null, adminId);   // the admin's own employee record: the actor
        var wo = await NewWorkOrderAsync(admin, site, [ex, ey, ez]);
        await Sender.RunOnceAsync();

        var mx = fx.Emails.To(x);
        Assert.Single(mx);
        Assert.Contains(wo.GetProperty("number").GetString()!, mx[0].Subject);
        Assert.Contains($"https://cmms.test/work-orders/{Id(wo)}", mx[0].Html);
        Assert.Empty(fx.Emails.To(adminEmail));

        // Adding a technician later e-mails only the new one.
        var w = Addr("w");
        var ew = await EmployeeAsync(admin, "EW", "Tech W", w);
        var detail = await Ok(await admin.GetAsync($"/api/work-orders/{Id(wo)}"));
        await Ok(await admin.PutAsJsonAsync($"/api/work-orders/{Id(wo)}", new
        {
            title = "Replace seal", type = "Corrective", priority = "High", siteId = site, assigneeIds = new[] { ex, ey, ez, ew },
            dueDate = Iso(Today.AddDays(2)), rowVersion = detail.GetProperty("rowVersion").GetString(),
        }));
        await Sender.RunOnceAsync();
        Assert.Single(fx.Emails.To(w));
        Assert.Single(fx.Emails.To(x));
    }

    [SkippableFact]
    public async Task Requester_hears_the_decision_and_completion()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var reqEmail = Addr("req");
        var (requester, _) = await UserAsync(admin, "Requester", reqEmail);
        var mgrEmail = Addr("mgr");
        await UserAsync(admin, "Maintenance Manager", mgrEmail);

        var r1 = Id(await Ok(await requester.PostAsJsonAsync("/api/work-requests", new { title = "Leak in plant room", siteId = site, priority = "High", type = "Corrective" })));
        await Sender.RunOnceAsync();
        Assert.Single(fx.Emails.To(mgrEmail), m => m.Subject.Contains("Leak in plant room")); // request.submitted
        Assert.Empty(fx.Emails.To(reqEmail));

        var approved = await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{r1}/approve", new { }));
        await Sender.RunOnceAsync();
        var toRequester = fx.Emails.To(reqEmail);
        Assert.Single(toRequester);
        Assert.Contains(approved.GetProperty("number").GetString()!, toRequester[0].Html);

        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{approved.GetProperty("workOrderId").GetGuid()}/status", new { status = "Completed", completionNotes = "Fixed" }));
        await Sender.RunOnceAsync();
        Assert.Equal(2, fx.Emails.To(reqEmail).Count); // request.completed

        var r2 = Id(await Ok(await requester.PostAsJsonAsync("/api/work-requests", new { title = "Paint door", siteId = site, priority = "Low", type = "Corrective" })));
        await Ok(await admin.PostAsJsonAsync($"/api/work-requests/{r2}/reject", new { note = "<b>no</b>" }));
        await Sender.RunOnceAsync();
        var rejected = fx.Emails.To(reqEmail).Last();
        Assert.Contains("&lt;b&gt;no&lt;/b&gt;", rejected.Html);
        Assert.DoesNotContain("<b>no</b>", rejected.Html);
    }

    [SkippableFact]
    public async Task Opt_outs_and_disabled_events_are_respected()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var techEmail = Addr("tech");
        var (_, techId) = await UserAsync(admin, "Technician", techEmail);
        var techEmployee = await EmployeeAsync(admin, "ET", "Tech", null, techId);
        using (var scope = fx.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
            var u = await db.Users.FirstAsync(x => x.Id == techId);
            u.EmailOptOuts = [EmailEvents.WorkOrderAssigned];
            await db.SaveChangesAsync();
        }
        await NewWorkOrderAsync(admin, site, [techEmployee]);
        await Sender.RunOnceAsync();
        Assert.Empty(fx.Emails.To(techEmail));

        // A manager's work, completed by the admin: the manager hears, until the organization switches that e-mail off.
        var mgrEmail = Addr("mgr");
        var (manager, _) = await UserAsync(admin, "Maintenance Manager", mgrEmail);
        var first = Id(await NewWorkOrderAsync(manager, site, []));
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{first}/status", new { status = "Completed", completionNotes = "Done" }));
        await Sender.RunOnceAsync();
        Assert.Single(fx.Emails.To(mgrEmail));
        await TestSettings.SetSettingsAsync(admin, s => s["disabledEmails"] = new JsonArray(EmailEvents.WorkOrderCompleted));
        var second = Id(await NewWorkOrderAsync(manager, site, []));
        await Ok(await admin.PostAsJsonAsync($"/api/work-orders/{second}/status", new { status = "Completed", completionNotes = "Done" }));
        await Sender.RunOnceAsync();
        Assert.Single(fx.Emails.To(mgrEmail));
    }

    [SkippableFact]
    public async Task Pm_assignees_hear_about_generated_work()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var x = Addr("pm");
        var ex = await EmployeeAsync(admin, "EP", "PM tech", x);
        var asset = Id(await Ok(await admin.PostAsJsonAsync("/api/assets", new { name = "Boiler", siteId = site, status = "Active", criticality = "High", warrantyType = "None" })));
        var task = Id(await Ok(await admin.PostAsJsonAsync("/api/pm-tasks", new { code = "B1", name = "Boiler check", steps = new[] { "Look" } })));
        await Ok(await admin.PostAsJsonAsync("/api/pm-schedules", new
        {
            name = "Boiler PM", siteId = site, assetId = asset, priority = "Medium", pmTaskId = task, type = "FixedInterval", basis = "Scheduled", frequency = 1, unit = "Week",
            workPeriodDays = 1, wholeMonth = false, leadDays = 0, startDate = Iso(Today), timeBased = false, extraTasks = Array.Empty<object>(), assigneeIds = new[] { ex },
        }));

        // The scheduler runs with nobody signed in: there is no "actor" to leave out.
        var scheduler = fx.Factory!.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<PmSchedulerService>().Single();
        await scheduler.RunOnceAsync(CancellationToken.None);
        await Sender.RunOnceAsync();
        var m = Assert.Single(fx.Emails.To(x));
        Assert.Contains("Boiler PM", m.Subject);
    }

    [SkippableFact]
    public async Task Subjects_never_carry_line_breaks()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var x = Addr("x");
        await NewWorkOrderAsync(admin, site, [await EmployeeAsync(admin, "EX", "Tech X", x)], "Leak\r\nBcc: evil@example.com");
        await Sender.RunOnceAsync();
        var m = Assert.Single(fx.Emails.To(x));
        Assert.DoesNotContain('\r', m.Subject);
        Assert.DoesNotContain('\n', m.Subject);
    }

    [SkippableFact]
    public async Task Approvers_hear_about_orders_within_their_limit()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var a = Addr("approver-a"); var b = Addr("approver-b");
        await UserAsync(admin, "Maintenance Manager", a, approvalLimit: 1000);
        await UserAsync(admin, "Maintenance Manager", b, approvalLimit: 100);
        var po = await PurchaseOrderAsync(admin, site, await SupplierAsync(admin, null), "Filter element", 5, 100); // 500
        await Ok(await admin.PostAsync($"/api/purchase-orders/{po}/submit", null));
        await Sender.RunOnceAsync();
        Assert.Single(fx.Emails.To(a));
        Assert.Empty(fx.Emails.To(b)); // above B's limit: B can't approve it
    }

    [SkippableFact]
    public async Task Buyer_hears_approval_and_rejection()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var buyerEmail = Addr("buyer");
        var (buyer, _) = await UserAsync(admin, "Maintenance Manager", buyerEmail, approvalLimit: 100);
        var supplier = await SupplierAsync(admin, null);
        var first = await PurchaseOrderAsync(buyer, site, supplier, "Filter element", 5, 100);
        await Ok(await buyer.PostAsync($"/api/purchase-orders/{first}/submit", null));
        await Ok(await admin.PostAsync($"/api/purchase-orders/{first}/approve", null));
        var second = await PurchaseOrderAsync(buyer, site, supplier, "Pump", 1, 900);
        await Ok(await buyer.PostAsync($"/api/purchase-orders/{second}/submit", null));
        await Ok(await admin.PostAsJsonAsync($"/api/purchase-orders/{second}/reject", new { reason = "Too dear" }));
        await Sender.RunOnceAsync();
        var mails = fx.Emails.To(buyerEmail);
        Assert.Equal(2, mails.Count);
        Assert.Contains("approved", mails[0].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Too dear", mails[1].Html);
    }

    [SkippableFact]
    public async Task Supplier_gets_the_order_when_sent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var s = Addr("supplier");
        var po = await PurchaseOrderAsync(admin, site, await SupplierAsync(admin, s), "Filter element", 2, 25);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/purchase-orders/{po}/send", null)).StatusCode); // still a draft
        await Ok(await admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var sent = await Ok(await admin.PostAsync($"/api/purchase-orders/{po}/send", null));
        Assert.Equal(s, sent.GetProperty("to").GetString());
        await Sender.RunOnceAsync();
        var m = Assert.Single(fx.Emails.To(s));
        Assert.Contains("Filter element", m.Html);
        Assert.Contains("50.00", m.Html);

        var noAddress = await PurchaseOrderAsync(admin, site, await SupplierAsync(admin, null), "Belt", 1, 10);
        await Ok(await admin.PostAsync($"/api/purchase-orders/{noAddress}/approve", null));
        var refused = await admin.PostAsync($"/api/purchase-orders/{noAddress}/send", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("The supplier has no e-mail address.", await refused.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Technicians_hear_when_parts_for_their_job_arrive()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (admin, _, _, _, site) = await OrgAsync();
        var x = Addr("tech");
        var wo = Id(await NewWorkOrderAsync(admin, site, [await EmployeeAsync(admin, "EX", "Tech X", x)]));
        var po = await PurchaseOrderAsync(admin, site, await SupplierAsync(admin, null), "Gasket set", 1, 30, wo);
        await Ok(await admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        var line = (await Ok(await admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Ok(await admin.PostAsJsonAsync($"/api/purchase-orders/{po}/receive", new { lines = new[] { new { lineId = line, quantity = 1 } } }));
        await Sender.RunOnceAsync();
        var mails = fx.Emails.To(x);
        Assert.Equal(2, mails.Count); // assigned, then parts received
        Assert.Contains("Gasket set", mails[1].Html);
    }

    [SkippableFact]
    public async Task Sender_sends_due_emails_and_marks_them_sent()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, tenant, _, _, _) = await OrgAsync();
        var to = new[] { Addr(), Addr(), Addr() };
        await QueueRawAsync(tenant, to);

        Assert.True(await Sender.RunOnceAsync() >= 3);
        Assert.Equal(3, fx.Emails.To(to).Count);
        var m = fx.Emails.To(to[0])[0];
        Assert.Equal("noreply@cmms.test", m.From);
        Assert.Equal("Mail Co via 4C CMMS", m.FromName);
        Assert.All(await RowsAsync(to), r => { Assert.Equal(EmailStatus.Sent, r.Status); Assert.NotNull(r.SentAt); });
    }

    [SkippableFact]
    public async Task Sender_retries_then_fails_and_never_blocks_the_action()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, tenant, _, _, _) = await OrgAsync();
        var bad = $"bob-{Guid.NewGuid():N}"; // not an e-mail address
        await QueueRawAsync(tenant, [bad]);
        var start = DateTimeOffset.UtcNow;

        await Sender.RunOnceAsync(start);
        var row = (await RowsAsync(bad)).Single();
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("mailbox rejected", row.LastError);
        Assert.InRange(row.NextAttemptAt, start.AddSeconds(50), start.AddSeconds(70)); // retried after 1 minute

        // Retries after 5, 30 and 120 more minutes; the fifth failure gives up.
        foreach (var at in new[] { 2, 8, 40, 161 }) await Sender.RunOnceAsync(start.AddMinutes(at));
        row = (await RowsAsync(bad)).Single();
        Assert.Equal(EmailStatus.Failed, row.Status);
        Assert.Equal(5, row.Attempts);
        await Sender.RunOnceAsync(start.AddDays(1));
        Assert.Equal(5, (await RowsAsync(bad)).Single().Attempts); // never tried again
    }

    [SkippableFact]
    public async Task Concurrent_runs_send_each_email_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (_, tenant, _, _, _) = await OrgAsync();
        var to = Enumerable.Range(0, 50).Select(_ => Addr("burst")).ToArray();
        await QueueRawAsync(tenant, to);

        await Task.WhenAll(Sender.RunOnceAsync(), Sender.RunOnceAsync());
        await Sender.RunOnceAsync();
        await Sender.RunOnceAsync();
        var sent = fx.Emails.To(to);
        Assert.Equal(50, sent.Count);
        Assert.Equal(50, sent.Select(m => m.To).Distinct().Count());
    }
}
