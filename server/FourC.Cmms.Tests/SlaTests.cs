using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Tests;

/// <summary>The deadline rules (no database).</summary>
public class SlaTests
{
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static DateTimeOffset U(string iso) => DateTimeOffset.Parse(iso);

    [Fact]
    public void End_of_day_on_cairos_last_thursday_of_october_is_after_the_repeated_hour()
    {
        // Summer time ends at midnight: 23:00-23:59 on Thursday 29 October 2026 happens twice. The day ends after the second time.
        Assert.Equal(U("2026-10-29T21:59:59Z"), Sla.EndOfDay(new DateOnly(2026, 10, 29), Cairo));
        Assert.Equal(new DateOnly(2026, 10, 29), Sla.LocalDay(U("2026-10-29T21:30:00Z"), Cairo)); // second 23:30
        Assert.Equal(new DateOnly(2026, 10, 29), Sla.LocalDay(U("2026-10-29T20:30:00Z"), Cairo)); // first 23:30
    }

    [Fact]
    public void End_of_day_in_a_zone_that_skips_the_last_half_hour_is_just_before_the_jump()
    {
        // A zone whose clocks jump from 23:30 to 00:30 on 1 March 2026: 23:59:59 doesn't exist that day.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 30, 0), 3, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 1));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Jump", TimeSpan.Zero, "Jump", "Jump", "Jump summer", [rule]);
        Assert.Equal(U("2026-03-01T23:29:59Z"), Sla.EndOfDay(new DateOnly(2026, 3, 1), zone));
    }

    [Fact]
    public void End_of_day_is_the_last_second_of_the_local_day()
    {
        Assert.Equal(U("2026-11-20T21:59:59Z"), Sla.EndOfDay(new DateOnly(2026, 11, 20), Cairo));
        Assert.Equal(new DateOnly(2026, 11, 21), Sla.LocalDay(U("2026-11-20T22:30:00Z"), Cairo));
        Assert.Equal(new DateOnly(2026, 11, 20), Sla.LocalDay(U("2026-11-20T21:30:00Z"), Cairo));
    }

    [Fact]
    public void Unknown_zone_falls_back_to_UTC()
    {
        Assert.Equal(TimeZoneInfo.Utc, Sla.Zone("Mars/Base"));
        Assert.Equal(TimeZoneInfo.Utc, Sla.Zone(null));
        Assert.Equal(Cairo.Id, Sla.Zone("Africa/Cairo").Id);
    }

    [Fact]
    public void Targets_count_every_hour_from_when_work_was_raised()
    {
        var s = new TenantSettings { SlaTargets = [new SlaTarget { Priority = WorkPriority.Urgent, RespondMinutes = 60, CompleteMinutes = 240 }] };
        var raised = U("2026-11-19T16:00:00Z"); // a Thursday
        Assert.Equal(U("2026-11-19T17:00:00Z"), Sla.RespondBy(s, WorkPriority.Urgent, raised));
        Assert.Equal(U("2026-11-19T20:00:00Z"), Sla.CompleteBy(s, WorkPriority.Urgent, raised));
        Assert.Null(Sla.RespondBy(s, WorkPriority.Low, raised));
        Assert.Null(Sla.CompleteBy(s, WorkPriority.Low, raised));
    }

    [Fact]
    public void States()
    {
        var deadline = U("2026-11-20T12:00:00Z");
        var window = TimeSpan.FromHours(4);
        Assert.Equal(SlaState.OnTrack, Sla.State(deadline, null, U("2026-11-20T08:30:00Z"), window));
        Assert.Equal(SlaState.DueSoon, Sla.State(deadline, null, U("2026-11-20T11:30:00Z"), window));
        Assert.Equal(SlaState.Late, Sla.State(deadline, null, U("2026-11-20T12:01:00Z"), window));
        Assert.Equal(SlaState.Met, Sla.State(deadline, U("2026-11-20T11:59:00Z"), U("2026-11-20T15:00:00Z"), window));
        Assert.Equal(SlaState.Missed, Sla.State(deadline, U("2026-11-20T12:01:00Z"), U("2026-11-20T15:00:00Z"), window));
        Assert.Equal(SlaState.DueSoon, Sla.State(deadline, null, deadline.AddMinutes(-90), null));
        Assert.Equal(SlaState.OnTrack, Sla.State(deadline, null, deadline.AddHours(-3), null));
        Assert.Equal(SlaState.None, Sla.State(null, null, deadline, window));
    }
}

/// <summary>Targets and the time zone in Settings.</summary>
public class SlaSettingsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private async Task<HttpClient> OrgAsync()
    {
        var c = fx.Client();
        var r = await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Targets Co", fullName = "Ann Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return c;
    }

    /// <summary>PUTs the current settings with <paramref name="change"/> applied; returns the response.</summary>
    internal static async Task<HttpResponseMessage> SaveAsync(HttpClient c, Action<JsonObject> change)
    {
        var current = JsonNode.Parse(await c.GetStringAsync("/api/settings"))!.AsObject();
        var settings = current["settings"]!.AsObject();
        change(settings);
        return await c.PutAsJsonAsync("/api/settings", new { name = current["name"]!.GetValue<string>(), settings });
    }

    private static JsonArray Urgent(int? respond, int? complete) =>
        [new JsonObject { ["priority"] = "Urgent", ["respondMinutes"] = respond, ["completeMinutes"] = complete }];

    [SkippableFact]
    public async Task Targets_and_time_zone_are_validated()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await OrgAsync();
        async Task Bad(Action<JsonObject> change, string field)
        {
            var r = await SaveAsync(c, change);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains(field, await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        await Bad(s => s["timeZone"] = "Nowhere/City", "settings.timeZone");
        await Bad(s => s["slaTargets"] = new JsonArray(Urgent(60, 240)[0]!.DeepClone(), Urgent(30, 120)[0]!.DeepClone()), "settings.slaTargets");
        await Bad(s => s["slaTargets"] = Urgent(0, 240), "settings.slaTargets");
        await Bad(s => s["dailyCapacityHours"] = 30, "settings.dailyCapacityHours");

        var ok = await SaveAsync(c, s => { s["slaTargets"] = Urgent(60, 240); s["timeZone"] = "Africa/Cairo"; s["dailyCapacityHours"] = 8; });
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var saved = JsonDocument.Parse(await c.GetStringAsync("/api/settings")).RootElement.GetProperty("settings");
        Assert.Equal("Africa/Cairo", saved.GetProperty("timeZone").GetString());
        var t = saved.GetProperty("slaTargets").EnumerateArray().Single();
        Assert.Equal(("Urgent", 60, 240), (t.GetProperty("priority").GetString(), t.GetProperty("respondMinutes").GetInt32(), t.GetProperty("completeMinutes").GetInt32()));
    }
}
