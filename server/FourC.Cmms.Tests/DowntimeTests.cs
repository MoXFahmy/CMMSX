using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>Asset stops from requests, work orders and by hand; availability from them (0.8.0).</summary>
public class DowntimeTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static async Task<string> Refused(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        return body;
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static Guid? G(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetGuid() : null;
    private static DateTimeOffset? At(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDateTimeOffset() : null;
    private static bool IsNull(JsonElement e, string p) => !e.TryGetProperty(p, out var v) || v.ValueKind == JsonValueKind.Null;

    private sealed record Org(HttpClient C, Guid Site, Guid Asset, Guid Asset2);

    private async Task<Org> SetupAsync()
    {
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Downtime Co", fullName = "Dina Down", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" }));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "P", name = "Plant", isActive = true })));
        Guid Asset(string name) => Id(Ok(c.PostAsJsonAsync("/api/assets", new { name, siteId = site, status = "Active", criticality = "High", warrantyType = "None" }).Result).Result);
        return new Org(c, site, Asset("Press"), Asset("Lathe"));
    }

    private static object Request(Org o, Guid asset, bool down = true, string title = "Press stopped") =>
        new { title, siteId = o.Site, assetId = asset, priority = "High", type = "Breakdown", assetIsDown = down };
    private static object Wo(Org o, Guid? asset, bool? down = true, string title = "Fix the press") =>
        new { title, siteId = o.Site, assetId = asset, type = "Breakdown", priority = "High", tasks = Array.Empty<string>(), assetIsDown = down };

    private static async Task<Guid> NewRequestAsync(Org o, object body) => Id(await Ok(await o.C.PostAsJsonAsync("/api/work-requests", body)));
    private static async Task<Guid> NewWoAsync(Org o, object body) => Id(await Ok(await o.C.PostAsJsonAsync("/api/work-orders", body)));
    private static async Task<JsonElement> WoAsync(Org o, Guid id) => await Ok(await o.C.GetAsync($"/api/work-orders/{id}"));
    private static async Task<JsonElement> RequestAsync(Org o, Guid id) => await Ok(await o.C.GetAsync($"/api/work-requests/{id}"));
    private static async Task CompleteAsync(Org o, Guid wo) =>
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Replaced the seal", assetWasDown = false, downtimeHours = 99 }));

    private async Task<List<DowntimeEvent>> StopsAsync(Guid asset)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.DowntimeEvents.IgnoreQueryFilters().AsNoTracking().Where(e => e.AssetId == asset && !e.IsDeleted).OrderBy(e => e.StartedAt).ToListAsync();
    }

    [SkippableFact]
    public async Task A_request_that_says_down_starts_a_stop_and_its_work_order_ends_it()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = await NewRequestAsync(o, Request(o, o.Asset));
        var r = await RequestAsync(o, rid);

        var stop = Assert.Single(await StopsAsync(o.Asset));
        Assert.Null(stop.EndedAt);
        Assert.Equal(At(r, "createdAt")!.Value.UtcTicks / 10_000, stop.StartedAt.UtcTicks / 10_000);
        Assert.Equal(DowntimeSource.Request, stop.Source);
        Assert.Equal(r.GetProperty("number").GetString(), stop.SourceNumber);
        Assert.Equal("Press stopped", stop.Reason);
        Assert.Equal(stop.Id, G(r, "downtimeEventId"));

        var wo = (await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        var w = await WoAsync(o, wo);
        Assert.Equal(stop.Id, G(w.GetProperty("downtime"), "id"));
        Assert.NotNull(At(w, "assetDownSince"));

        await CompleteAsync(o, wo);
        stop = Assert.Single(await StopsAsync(o.Asset));
        Assert.NotNull(stop.EndedAt);
        w = await WoAsync(o, wo);
        Assert.True(w.GetProperty("assetWasDown").GetBoolean());
        Assert.Equal(Math.Round((decimal)(stop.EndedAt!.Value - stop.StartedAt).TotalHours, 2), w.GetProperty("downtimeHours").GetDecimal());
        Assert.NotNull(At(w.GetProperty("downtime"), "endedAt"));
        Assert.True(IsNull(w, "assetDownSince"));
    }

    [SkippableFact]
    public async Task A_second_down_report_joins_the_open_stop()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var first = await NewRequestAsync(o, Request(o, o.Asset));
        var second = await NewRequestAsync(o, Request(o, o.Asset, title: "Press is down again?"));
        var stop = Assert.Single(await StopsAsync(o.Asset));
        Assert.Equal(stop.Id, G(await RequestAsync(o, first), "downtimeEventId"));
        Assert.Equal(stop.Id, G(await RequestAsync(o, second), "downtimeEventId"));
        Assert.Equal("Press stopped", stop.Reason);
    }

    [SkippableFact]
    public async Task Back_running_ends_the_stop_early()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await NewWoAsync(o, Wo(o, o.Asset));
        var back = await o.C.PostAsync($"/api/assets/{o.Asset}/back-running", null);
        Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        var stop = Assert.Single(await StopsAsync(o.Asset));
        Assert.NotNull(stop.EndedAt);
        Assert.InRange(stop.EndedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(DowntimeSource.WorkOrder, stop.Source);

        await CompleteAsync(o, wo);
        var after = Assert.Single(await StopsAsync(o.Asset));
        Assert.Equal(stop.EndedAt, after.EndedAt);
        var w = await WoAsync(o, wo);
        Assert.Equal(Math.Round((decimal)(stop.EndedAt!.Value - stop.StartedAt).TotalHours, 2), w.GetProperty("downtimeHours").GetDecimal());
    }

    [SkippableFact]
    public async Task Back_running_when_not_down_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        Assert.Contains("The asset isn't down.", await Refused(await o.C.PostAsync($"/api/assets/{o.Asset}/back-running", null)));
        await NewWoAsync(o, Wo(o, o.Asset));
        await Ok(await o.C.PostAsync($"/api/assets/{o.Asset}/back-running", null));
        // A second press, for example by someone else a moment later, changes nothing.
        var ended = Assert.Single(await StopsAsync(o.Asset)).EndedAt;
        Assert.Contains("The asset isn't down.", await Refused(await o.C.PostAsync($"/api/assets/{o.Asset}/back-running", null)));
        Assert.Equal(ended, Assert.Single(await StopsAsync(o.Asset)).EndedAt);
    }

    [SkippableFact]
    public async Task Paperwork_does_not_end_a_stop()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = await NewRequestAsync(o, Request(o, o.Asset));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/reject", new { note = "Already reported" }));
        Assert.Null(Assert.Single(await StopsAsync(o.Asset)).EndedAt);

        var wo = await NewWoAsync(o, Wo(o, o.Asset2));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Cancelled", note = "Duplicate" }));
        Assert.Null(Assert.Single(await StopsAsync(o.Asset2)).EndedAt);
        Assert.NotNull(At(await WoAsync(o, wo), "assetDownSince"));
    }

    [SkippableFact]
    public async Task Unticking_a_mistake_removes_its_stop()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await NewWoAsync(o, Wo(o, o.Asset));
        Assert.Single(await StopsAsync(o.Asset));
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo}", Wo(o, o.Asset, down: false)));
        Assert.Empty(await StopsAsync(o.Asset));
        Assert.True(IsNull(await WoAsync(o, wo), "downtime"));

        // A stop a request reported stays when its work order is unticked.
        var rid = await NewRequestAsync(o, Request(o, o.Asset2));
        var wo2 = (await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { }))).GetProperty("workOrderId").GetGuid();
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo2}", Wo(o, o.Asset2, down: false)));
        Assert.Null(Assert.Single(await StopsAsync(o.Asset2)).EndedAt);
        Assert.True(IsNull(await WoAsync(o, wo2), "downtime"));

        // Leaving the box out of an edit keeps the stop.
        var wo3 = await NewWoAsync(o, Wo(o, o.Asset));
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo3}", Wo(o, o.Asset, down: null, title: "Fix the press properly")));
        Assert.Single(await StopsAsync(o.Asset));
        Assert.False(IsNull(await WoAsync(o, wo3), "downtime"));
    }

    [SkippableFact]
    public async Task Changing_the_asset_leaves_the_stop_on_the_old_one()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var wo = await NewWoAsync(o, Wo(o, o.Asset));
        await Ok(await o.C.PutAsJsonAsync($"/api/work-orders/{wo}", Wo(o, o.Asset2, down: null)));
        Assert.Null(Assert.Single(await StopsAsync(o.Asset)).EndedAt);
        Assert.Empty(await StopsAsync(o.Asset2));
        Assert.True(IsNull(await WoAsync(o, wo), "downtime"));
    }

    private static async Task<JsonElement> DowntimeAsync(Org o, Guid asset) => await Ok(await o.C.GetAsync($"/api/assets/{asset}/downtime"));
    private static object Stop(DateTimeOffset start, DateTimeOffset? end, string reason) => new { startedAt = start, endedAt = end, reason };
    private static readonly string[] AllDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    [SkippableFact]
    public async Task Manual_stops_are_checked_and_listed()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var now = DateTimeOffset.UtcNow;
        var url = $"/api/assets/{o.Asset}/downtime";
        var first = Id(await Ok(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(-10), now.AddHours(-8), "Power cut"))));
        Assert.Contains("This stop overlaps another stop of this asset.", await Refused(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(-9), now.AddHours(-7), "Again"))));
        Assert.Contains("The end must be after the start.", await Refused(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(-5), now.AddHours(-6), "Backwards"))));
        Assert.Contains("The start can't be in the future.", await Refused(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(2), null, "Later"))));
        await Ok(await o.C.PutAsJsonAsync($"/api/downtime/{first}", Stop(now.AddHours(-10), now.AddHours(-8), "Power cut (grid)")));
        var open = Id(await Ok(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(-3), null, "Belt snapped"))));
        Assert.Contains("The asset is already down.", await Refused(await o.C.PostAsJsonAsync(url, Stop(now.AddHours(-1), null, "Belt again"))));

        var d = await DowntimeAsync(o, o.Asset);
        var stops = d.GetProperty("stops");
        Assert.Equal(2, stops.GetArrayLength());
        Assert.Equal(open, Id(stops[0]));
        Assert.Equal("Power cut (grid)", stops[1].GetProperty("reason").GetString());
        Assert.Equal("Manual", stops[1].GetProperty("source").GetString());
        Assert.Equal(2m, stops[1].GetProperty("hours").GetDecimal());
        Assert.NotNull(At(d, "downSince"));

        var history = await Ok(await o.C.GetAsync($"/api/audit?entityType=DowntimeEvent&entityId={first}"));
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("action").GetString() == "Updated");
    }

    [SkippableFact]
    public async Task Availability_uses_the_assets_running_time()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var asset = Id(await Ok(await o.C.PostAsJsonAsync("/api/assets", new { name = "Mixer", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None",
            runsHoursPerDay = 8, runsOn = AllDays })));
        var yesterday = DateTimeOffset.UtcNow.Date.AddDays(-1);
        await Ok(await o.C.PostAsJsonAsync($"/api/assets/{asset}/downtime", Stop(new DateTimeOffset(yesterday.AddHours(10), TimeSpan.Zero), new DateTimeOffset(yesterday.AddHours(14), TimeSpan.Zero), "Jam")));
        var d = await DowntimeAsync(o, asset);
        Assert.Equal(8m, d.GetProperty("runsHoursPerDay").GetDecimal());
        Assert.Equal(240m, d.GetProperty("plannedHours30").GetDecimal());
        Assert.Equal(4m, d.GetProperty("downHours30").GetDecimal());
        Assert.Equal(98.3m, d.GetProperty("availability30").GetDecimal());

        // Work completed the old way (hours typed, no stop) still counts.
        var wo = await NewWoAsync(o, Wo(o, asset, down: false));
        await Ok(await o.C.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Cleared", assetWasDown = true, downtimeHours = 2 }));
        d = await DowntimeAsync(o, asset);
        Assert.Equal(6m, d.GetProperty("downHours30").GetDecimal());
        Assert.Equal(97.5m, d.GetProperty("availability30").GetDecimal());
    }

    [SkippableFact]
    public async Task Running_time_must_make_sense()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        object Body(object? hours, object? days) => new { name = "Kiln", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None", runsHoursPerDay = hours, runsOn = days };
        Assert.Contains("Running hours must be between 0.5 and 24.", await Refused(await o.C.PostAsJsonAsync("/api/assets", Body(25, null))));
        Assert.Contains("Running hours must be between 0.5 and 24.", await Refused(await o.C.PostAsJsonAsync("/api/assets", Body(0.25, null))));
        Assert.Contains("Choose at least one running day.", await Refused(await o.C.PostAsJsonAsync("/api/assets", Body(null, Array.Empty<string>()))));

        var kiln = Id(await Ok(await o.C.PostAsJsonAsync("/api/assets", Body(16, new[] { "Monday", "Tuesday" }))));
        var detail = await Ok(await o.C.GetAsync($"/api/assets/{kiln}"));
        Assert.Equal(16m, detail.GetProperty("runsHoursPerDay").GetDecimal());
        Assert.Equal(2, detail.GetProperty("runsOn").GetArrayLength());
        // An edit that leaves the running time out keeps it.
        await Ok(await o.C.PutAsJsonAsync($"/api/assets/{kiln}", new { assetNumber = detail.GetProperty("assetNumber").GetString(), name = "Kiln 2", siteId = o.Site,
            status = "Active", criticality = "High", warrantyType = "None" }));
        detail = await Ok(await o.C.GetAsync($"/api/assets/{kiln}"));
        Assert.Equal(16m, detail.GetProperty("runsHoursPerDay").GetDecimal());
        Assert.Equal(2, detail.GetProperty("runsOn").GetArrayLength());
    }

    [SkippableFact]
    public async Task Down_assets_show_in_the_list_and_on_the_dashboard()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var user = (await Ok(await o.C.GetAsync("/api/auth/me"))).GetProperty("user").GetProperty("id").GetGuid();
        var me = Id(await Ok(await o.C.PostAsJsonAsync("/api/employees", new { code = "E1", fullName = "Dina Down", isContractor = false, userId = user, siteIds = new[] { o.Site } })));
        var wo = await NewWoAsync(o, new { title = "Fix the press", siteId = o.Site, assetId = o.Asset, type = "Breakdown", priority = "High", tasks = Array.Empty<string>(),
            assigneeIds = new[] { me }, assetIsDown = true });

        var list = await Ok(await o.C.GetAsync("/api/assets?downNow=true"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(o.Asset, Id(list.GetProperty("items")[0]));
        Assert.NotNull(At(list.GetProperty("items")[0], "downSince"));
        var all = await Ok(await o.C.GetAsync("/api/assets"));
        Assert.Contains(all.GetProperty("items").EnumerateArray(), a => Id(a) == o.Asset2 && IsNull(a, "downSince"));

        var dash = await Ok(await o.C.GetAsync("/api/dashboard"));
        Assert.Equal(1, dash.GetProperty("totals").GetProperty("assetsDown").GetInt32());

        var bundle = await Ok(await o.C.GetAsync("/api/tech/bundle"));
        var job = bundle.GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.NotNull(At(job.GetProperty("asset"), "downSince"));
    }

    [SkippableFact]
    public async Task A_stop_links_to_its_records()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await SetupAsync();
        var rid = await NewRequestAsync(o, Request(o, o.Asset));
        var approved = await Ok(await o.C.PostAsJsonAsync($"/api/work-requests/{rid}/approve", new { }));
        var links = (await DowntimeAsync(o, o.Asset)).GetProperty("stops")[0].GetProperty("links").EnumerateArray().ToList();
        Assert.Contains(links, l => l.GetProperty("kind").GetString() == "request" && Id(l) == rid);
        Assert.Contains(links, l => l.GetProperty("kind").GetString() == "workOrder" && Id(l) == approved.GetProperty("workOrderId").GetGuid()
            && l.GetProperty("number").GetString() == approved.GetProperty("number").GetString());
    }
}
