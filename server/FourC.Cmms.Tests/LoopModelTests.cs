using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

public class LoopModelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task New_organization_has_the_defaults_and_cancel_reasons()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var settings = (await Ok(await org.Admin.GetAsync("/api/settings"))).GetProperty("settings");
        Assert.Equal(["reject", "refuse"], settings.GetProperty("commentRequiredFor").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Empty(settings.GetProperty("emailRecipients").EnumerateObject());
        var codes = await DbAsync(fx, db => db.CancelReasons.IgnoreQueryFilters().Where(r => r.TenantId == org.Tenant).Select(r => r.Code).ToListAsync());
        Assert.Equal(["DUP", "ERROR", "NOTNEEDED", "OTHER"], codes.Order().ToArray());
    }

    [SkippableFact]
    public async Task Settings_refuse_unknown_comment_actions_and_email_recipients()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        Assert.Contains("Unknown action", await Bad(await TestSettings.TrySetSettingsAsync(org.Admin, s => s["commentRequiredFor"] = new JsonArray("nap"))));
        Assert.Contains("Unknown recipient", await Bad(await TestSettings.TrySetSettingsAsync(org.Admin,
            s => s["emailRecipients"] = new JsonObject { ["request.submitted"] = new JsonArray("boss") })));
        Assert.Contains("Unknown e-mail", await Bad(await TestSettings.TrySetSettingsAsync(org.Admin,
            s => s["emailRecipients"] = new JsonObject { ["nope"] = new JsonArray("requester") })));
        await TestSettings.SetSettingsAsync(org.Admin, s =>
        {
            s["emailRecipients"] = new JsonObject { ["request.submitted"] = new JsonArray("requester", "requestRecipients") };
            s["commentRequiredFor"] = new JsonArray("hold", "cancel");
        });
        var settings = (await Ok(await org.Admin.GetAsync("/api/settings"))).GetProperty("settings");
        Assert.Equal(["requester", "requestRecipients"], settings.GetProperty("emailRecipients").GetProperty("request.submitted").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(["hold", "cancel"], settings.GetProperty("commentRequiredFor").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public void Access_window_rules()
    {
        var t = new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        Assert.Null(AccessWindows.Problem(null));
        Assert.Null(AccessWindows.Problem([]));
        Assert.Null(AccessWindows.Problem([new AccessWindow(t, t.AddHours(2), "Room free")]));
        Assert.NotNull(AccessWindows.Problem([new AccessWindow(t, t, null)]));
        Assert.NotNull(AccessWindows.Problem([new AccessWindow(t, t.AddHours(-1), null)]));
        Assert.NotNull(AccessWindows.Problem(Enumerable.Range(0, 11).Select(i => new AccessWindow(t.AddDays(i), t.AddDays(i).AddHours(1), null)).ToList()));
        Assert.NotNull(AccessWindows.Problem([new AccessWindow(t, t.AddHours(1), new string('x', 201))]));

        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var day = new DateOnly(2026, 11, 23);
        Assert.True(AccessWindows.Covers(null, day, cairo));
        Assert.True(AccessWindows.Covers([], day, cairo));
        // 22:00–23:00 UTC on the 22nd is after midnight on the 23rd in Cairo (UTC+2).
        Assert.True(AccessWindows.Covers([new AccessWindow(new(2026, 11, 22, 22, 0, 0, TimeSpan.Zero), new(2026, 11, 22, 23, 0, 0, TimeSpan.Zero), null)], day, cairo));
        Assert.False(AccessWindows.Covers([new AccessWindow(new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero), new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero), null)], day, cairo));
        Assert.True(AccessWindows.Covers([new AccessWindow(new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero), new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero), null)], day, cairo));
    }

    [Fact]
    public void Email_kinds_use_the_defaults_until_configured()
    {
        Assert.Equal(new HashSet<string> { EmailRecipientKinds.Issuer }, EmailEvents.KindsOn(new TenantSettings(), EmailEvents.WorkOrderCompleted));
        var s = new TenantSettings { EmailRecipients = new() { [EmailEvents.WorkOrderCompleted] = [EmailRecipientKinds.Issuer, EmailRecipientKinds.Technicians] } };
        Assert.Equal(new HashSet<string> { EmailRecipientKinds.Issuer }, EmailEvents.KindsOn(s, EmailEvents.WorkOrderCompleted));
        s.EmailRecipients[EmailEvents.WorkOrderCompleted] = [];
        Assert.Empty(EmailEvents.KindsOn(s, EmailEvents.WorkOrderCompleted));
        Assert.Equal(new HashSet<string> { EmailRecipientKinds.Technicians, EmailRecipientKinds.Contractors }, EmailEvents.KindsOn(new TenantSettings(), EmailEvents.WorkOrderAssigned));
        foreach (var (key, row) in EmailEvents.Grid) Assert.All(row.Defaults, d => Assert.Contains(d, row.Applicable));
    }

    [Theory]
    [InlineData("Say why the work is on hold.")]
    [InlineData("Say why the work is reopened.")]
    [InlineData("Say why the work is cancelled.")]
    [InlineData("Choose why it is cancelled.")]
    [InlineData("Choose the trade or team the work goes to.")]
    [InlineData("Choose people who can see this place.")]
    [InlineData("This link was already used or has expired.")]
    [InlineData("This item is in use. Mark it inactive instead.")]
    [InlineData("Nothing is waiting for your answer.")]
    [InlineData("Give a rating from 1 to 5.")]
    [InlineData("Say why the work isn't done.")]
    [InlineData("Choose a request type.")]
    [InlineData("New comment on request WR-00001")]
    [InlineData("Refused: work order WO-00001")]
    [InlineData("Cancelled: work order WO-00001")]
    [InlineData("Overdue: work order WO-00001")]
    [InlineData("The work you asked for has been done. Please accept or refuse it.")]
    [InlineData("Accept or refuse the work")]
    [InlineData("An access window must end after it starts.")]
    [InlineData("Requester satisfaction")]
    [InlineData("Cancelled work")]
    public void New_messages_have_Arabic(string english) =>
        Assert.NotEqual(english, FourC.Cmms.Api.Infrastructure.Translator.T(english, "ar"));
}
