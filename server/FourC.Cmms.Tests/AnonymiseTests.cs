using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.People;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Anonymising a person who has left (0.25.0): the name leaves every copy, the history and costs stay.</summary>
public class AnonymiseTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> PersonAsync(HttpClient admin, string code, string name, Guid? userId = null, string? email = null) =>
        Id(await Ok(await admin.PostAsJsonAsync("/api/employees", new { code, fullName = name, email = email ?? Addr(code.ToLowerInvariant()), userId, hourlyRate = 10m, isActive = true })));

    private Task Deactivate(Guid? employeeId = null, Guid? userId = null) => DbAsync(fx, async db =>
    {
        if (employeeId is { } e) await db.Employees.IgnoreQueryFilters().Where(x => x.Id == e).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (userId is { } u) await db.Users.IgnoreQueryFilters().Where(x => x.Id == u).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        return 0;
    });

    private static async Task<JsonElement> Preview(HttpClient c, string query) => await Ok(await c.GetAsync($"/api/anonymise/preview?{query}"));

    private static Task<HttpResponseMessage> Anonymise(HttpClient c, object body) => c.PostAsJsonAsync("/api/anonymise", body);

    /// <summary>How many rows of a listed name column (or the log) still hold <paramref name="text"/>.</summary>
    private Task<List<string>> LeftoversAsync(Guid tenant, string text) => DbAsync(fx, async db =>
    {
        var found = new List<string>();
        foreach (var (entity, property, _) in NameColumns.All)
        {
            var type = db.Model.FindEntityType(entity)!;
            var column = type.FindProperty(property)!.GetColumnName();
            var sql = $"SELECT COUNT(*) AS [Value] FROM [{type.GetTableName()}] WHERE [TenantId] = @t AND [{column}] LIKE @n";
            var n = await db.Database.SqlQueryRaw<int>(sql, new SqlParameter("t", tenant), new SqlParameter("n", $"%{text}%")).SingleAsync();
            if (n > 0) found.Add($"{entity.Name}.{property}: {n}");
        }
        var log = await db.AuditEntries.IgnoreQueryFilters().Where(a => a.TenantId == tenant && ((a.Changes != null && a.Changes.Contains(text)) || a.UserName == text))
            .Select(a => a.EntityType + " " + a.Action).ToListAsync();
        found.AddRange(log.Select(l => $"log: {l}"));
        return found;
    });

    [SkippableFact]
    public async Task Name_leaves_every_copy()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var karim = await UserAsync(fx, org.Admin, "Administrator", name: "Karim Saad");
        var employee = await PersonAsync(org.Admin, "E-012", "Karim Saad", karim.Id);
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low", assetId = asset })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = employee, hours = 2, overtimeHours = 0, workDate = Today }));
        await Ok(await karim.Client.PostAsJsonAsync($"/api/work-orders/{wo}/comments", new { body = "Replaced the seal" }));
        var meter = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/meters", new { code = "RH", name = "Run hours", assetId = asset, unit = "h", isActive = true })));
        await Ok(await karim.Client.PostAsJsonAsync($"/api/meters/{meter}/readings", new { value = 10, confirm = true }));
        await DbAsync(fx, async db =>
        {
            db.Attachments.Add(new Attachment { TenantId = org.Tenant, EntityType = "WorkOrder", EntityId = wo, FileName = "signature.png", ContentType = "image/png", StorageKey = $"{org.Tenant}/sig", IsSignature = true, SignedBy = "Karim Saad" });
            return await db.SaveChangesAsync();
        });
        var labourBefore = await DbAsync(fx, db => db.WorkOrderLabor.IgnoreQueryFilters().AsNoTracking().Where(l => l.WorkOrderId == wo).Select(l => new { l.Hours, l.Rate, l.OvertimeRate }).SingleAsync());
        var oldEmail = karim.Email;
        Assert.NotEmpty(await LeftoversAsync(org.Tenant, "Karim Saad"));

        await Deactivate(employee, karim.Id);
        var preview = await Preview(org.Admin, $"employeeId={employee}");
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("blocked").ValueKind);
        Assert.Equal("Former employee E-012", preview.GetProperty("placeholder").GetString());
        Assert.True(preview.GetProperty("records").GetInt32() > 0);
        await Ok(await Anonymise(org.Admin, new { employeeId = employee, confirm = "E-012" }));

        Assert.Empty(await LeftoversAsync(org.Tenant, "Karim Saad"));
        Assert.Empty(await LeftoversAsync(org.Tenant, oldEmail));
        var labourAfter = await DbAsync(fx, db => db.WorkOrderLabor.IgnoreQueryFilters().AsNoTracking().Where(l => l.WorkOrderId == wo).Select(l => new { l.Hours, l.Rate, l.OvertimeRate }).SingleAsync());
        Assert.Equal(labourBefore, labourAfter);
        var shown = await Ok(await org.Admin.GetAsync($"/api/employees/{employee}"));
        Assert.Equal("Former employee E-012", shown.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("email").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, shown.GetProperty("anonymisedAt").ValueKind);
        Assert.Contains("Anonymised", await DbAsync(fx, db => db.AuditEntries.IgnoreQueryFilters().Where(a => a.EntityId == employee).Select(a => a.Action).ToListAsync()));
        // Karim's old address is free for someone new, and the old sign-in is closed.
        var roles = await Ok(await org.Admin.GetAsync("/api/roles"));
        var role = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Technician").GetProperty("id").GetGuid();
        await Ok(await org.Admin.PostAsJsonAsync("/api/users", new { fullName = "New Karim", email = oldEmail, isActive = true, roleIds = new[] { role }, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await karim.Client.GetAsync("/api/auth/me")).StatusCode);
        // Done once is done.
        Assert.Equal("This person is already anonymised.", (await Preview(org.Admin, $"employeeId={employee}")).GetProperty("blocked").GetString());
    }

    [SkippableFact]
    public async Task Anonymising_clears_reply_to_on_queued_mail()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx);
        var karim = await UserAsync(fx, org.Admin, "Administrator", name: "Karim Saad");
        var employee = await PersonAsync(org.Admin, "E-012", "Karim Saad", karim.Id);
        // Karim raises a request: the mail the office gets still carries his address as its Reply-To while it waits in the queue.
        await Ok(await karim.Client.PostAsJsonAsync("/api/work-requests", new { title = "Light flickers", siteId = org.Site, locationId = org.Location, priority = "Low", type = "Corrective" }));
        var queued = await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == org.Tenant && e.ReplyTo == karim.Email).CountAsync());
        Assert.True(queued > 0, "the setup should have queued mail with Karim as the Reply-To");

        await Deactivate(employee, karim.Id);
        await Ok(await Anonymise(org.Admin, new { employeeId = employee, confirm = "E-012" }));

        Assert.Equal(0, await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == org.Tenant && e.ReplyTo != null && e.ReplyTo.Contains(karim.Email)).CountAsync()));
        // Mail triggered afterwards carries none, and the rows still exist (only the address left them).
        Assert.Equal(queued, await DbAsync(fx, db => db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == org.Tenant && e.EventKey == "request.submitted").CountAsync()));
    }

    [SkippableFact]
    public async Task Refused_for_active_people_and_unconfirmed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var active = await PersonAsync(org.Admin, "E-020", "Still Here");
        Assert.Equal("Make the person inactive first.", (await Preview(org.Admin, $"employeeId={active}")).GetProperty("blocked").GetString());
        Assert.Contains("Make the person inactive first.", await Bad(await Anonymise(org.Admin, new { employeeId = active, confirm = "E-020" })));

        var user = await UserAsync(fx, org.Admin, "Technician", name: "Signed In");
        var linked = await PersonAsync(org.Admin, "E-012", "Signed In", user.Id);
        await Deactivate(linked);
        Assert.Contains("Make their sign-in inactive first.", await Bad(await Anonymise(org.Admin, new { employeeId = linked, confirm = "E-012" })));

        await Deactivate(userId: user.Id);
        Assert.Contains("Type E-012 to confirm.", await Bad(await Anonymise(org.Admin, new { employeeId = linked, confirm = "E-013" })));
        Assert.Equal("Signed In", (await Ok(await org.Admin.GetAsync($"/api/employees/{linked}"))).GetProperty("fullName").GetString());
    }

    [SkippableFact]
    public async Task Same_name_leaves_text_names_alone()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var ali = await UserAsync(fx, org.Admin, "Administrator", name: "Ali Hassan");
        var leaving = await PersonAsync(org.Admin, "E-030", "Ali Hassan", ali.Id);
        await PersonAsync(org.Admin, "E-031", " ali hassan ");
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low", requestedBy = "Ali Hassan" })));
        await Ok(await ali.Client.PostAsJsonAsync($"/api/work-orders/{wo}/comments", new { body = "On it" }));
        await Deactivate(leaving, ali.Id);

        var preview = await Preview(org.Admin, $"employeeId={leaving}");
        Assert.Contains(preview.GetProperty("sameNameAs").EnumerateArray(), n => n.GetString()!.Contains("E-031"));
        await Ok(await Anonymise(org.Admin, new { employeeId = leaving, confirm = "E-030" }));

        Assert.Equal("Ali Hassan", (await Ok(await org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("requestedBy").GetString());
        var comment = await DbAsync(fx, db => db.WorkOrderComments.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.WorkOrderId == wo));
        Assert.Equal("Former employee E-030", comment.UserName);
    }

    [SkippableFact]
    public async Task Login_only_people_too()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var second = await UserAsync(fx, org.Admin, "Administrator", name: "Sam Nour");
        await Ok(await second.Client.PostAsJsonAsync("/api/assets", new { name = "Fan", siteId = org.Site, status = "Active", criticality = "Low", warrantyType = "None" }));
        Assert.Equal("Make the person inactive first.", (await Preview(org.Admin, $"userId={second.Id}")).GetProperty("blocked").GetString());
        await Deactivate(userId: second.Id);
        var code = second.Id.ToString("N")[..6];
        var preview = await Preview(org.Admin, $"userId={second.Id}");
        Assert.Equal($"Former user {code}", preview.GetProperty("placeholder").GetString());
        await Ok(await Anonymise(org.Admin, new { userId = second.Id, confirm = code }));

        var user = (await Ok(await org.Admin.GetAsync("/api/users"))).EnumerateArray().Single(u => Id(u) == second.Id);
        Assert.Equal($"Former user {code}", user.GetProperty("fullName").GetString());
        Assert.Equal($"anonymised-{second.Id}@invalid", user.GetProperty("email").GetString());
        Assert.Empty(await LeftoversAsync(org.Tenant, "Sam Nour"));
        Assert.Contains("can't be used again", await Bad(await org.Admin.PostAsJsonAsync($"/api/users/{second.Id}/password", new { password = "Another0ne!x" })));
    }
}
