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
