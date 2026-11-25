using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: asset groups — members anywhere in the tree, list and report filters, PMs that follow the group, import.</summary>
public class AssetGroupTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    internal static async Task<Guid> GroupAsync(HttpClient c, string code, Guid? site = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/asset-groups", new { code, name = $"Group {code}", siteId = site, isActive = true })));

    internal static async Task<JsonElement> MembersAsync(HttpClient c, Guid group, Guid[] add, Guid[]? remove = null) =>
        await Ok(await c.PostAsJsonAsync($"/api/asset-groups/{group}/members", new { add, remove = remove ?? [] }));

    private static async Task<List<Guid>> MemberIdsAsync(HttpClient c, Guid group) =>
        (await Ok(await c.GetAsync($"/api/asset-groups/{group}"))).GetProperty("members").EnumerateArray().Select(Id).ToList();

    private static async Task<HttpResponseMessage> SaveAssetAsync(HttpClient c, Guid asset, Dictionary<string, object?> extra)
    {
        var d = await Ok(await c.GetAsync($"/api/assets/{asset}"));
        var body = new Dictionary<string, object?>
        {
            ["assetNumber"] = d.GetProperty("assetNumber").GetString(), ["name"] = d.GetProperty("name").GetString(), ["siteId"] = d.GetProperty("siteId").GetGuid(),
            ["status"] = d.GetProperty("status").GetString(), ["criticality"] = "Medium", ["warrantyType"] = "None", ["rowVersion"] = d.GetProperty("rowVersion").GetString(),
        };
        foreach (var (k, v) in extra) body[k] = v;
        return await c.PutAsJsonAsync($"/api/assets/{asset}", body);
    }

    private static async Task LabourAsync(PurchaseRequestTests.Org o, Guid asset, decimal hours, string code)
    {
        var e = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/employees", new { code, fullName = code, isContractor = false, hourlyRate = 50m })));
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = $"Work {code}", siteId = o.Site, assetId = asset, type = "Corrective", priority = "Medium" })));
        await Ok(await o.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/labor", new { employeeId = e, workDate = Today.ToString("yyyy-MM-dd"), hours, overtimeHours = 0m }));
    }

    [SkippableFact]
    public async Task Groups_have_members_across_the_tree_and_filter_lists()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var l1 = await CostCentreWorkTests.LocationAsync(o.Admin, o.Site, "L1");
        var l2 = await CostCentreWorkTests.LocationAsync(o.Admin, o.Site, "L2");
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 1", l1);
        var b = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 2", l2);
        var c = await Pm12.AssetAsync(o.Admin, o.Site, "Boiler");
        var g = await GroupAsync(o.Admin, "KITCHEN");
        Assert.Equal(2, (await MembersAsync(o.Admin, g, [a, b])).GetProperty("added").GetInt32());
        Assert.Equal(new[] { a, b }.Order(), (await Ok(await o.Admin.GetAsync($"/api/assets?assetGroupId={g}"))).GetProperty("items").EnumerateArray().Select(Id).Order());
        await LabourAsync(o, a, 2, "E1");
        await LabourAsync(o, c, 3, "E2");
        var wos = (await Ok(await o.Admin.GetAsync($"/api/work-orders?assetGroupId={g}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Single(wos);
        var from = Today.AddDays(-30).ToString("yyyy-MM-dd");
        var costs = await Ok(await o.Admin.GetAsync($"/api/reports/maintenance-costs?from={from}&to={Today:yyyy-MM-dd}&assetGroupId={g}"));
        Assert.Equal(100m, costs.GetProperty("summary").EnumerateArray().First().GetProperty("value").GetDecimal());
        Assert.Equal(1, (await Ok(await o.Admin.GetAsync("/api/asset-groups"))).EnumerateArray().Count(x => Id(x) == g && x.GetProperty("memberCount").GetInt32() == 2));
    }

    [SkippableFact]
    public async Task Site_limited_people_see_and_change_only_their_members()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "A asset");
        var b = await Pm12.AssetAsync(o.Admin, siteB, "B asset");
        var g = await GroupAsync(o.Admin, "SHARED");
        await MembersAsync(o.Admin, g, [a, b]);
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Lead A", ["assets.view", "assets.edit"], siteIds: [o.Site]);
        Assert.Equal([a], await MemberIdsAsync(limited, g));
        var removed = await MembersAsync(limited, g, [], [a, b]);
        Assert.Equal(1, removed.GetProperty("removed").GetInt32());
        Assert.Equal([b], await MemberIdsAsync(o.Admin, g));
        var onlyB = await GroupAsync(o.Admin, "BONLY", siteB);
        var refused = await o.Admin.PostAsJsonAsync($"/api/asset-groups/{onlyB}/members", new { add = new[] { a }, remove = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This group is for Site B only.", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await limited.GetAsync($"/api/asset-groups/{onlyB}")).StatusCode);
    }

    [SkippableFact]
    public async Task Asset_form_sets_groups_and_old_clients_keep_them()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge");
        var g1 = await GroupAsync(o.Admin, "G1");
        var g2 = await GroupAsync(o.Admin, "G2");
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["groupIds"] = new[] { g1, g2 } }));
        Assert.Equal(new[] { g1, g2 }.Order(), (await Ok(await o.Admin.GetAsync($"/api/assets/{a}"))).GetProperty("groupIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["name"] = "Fridge renamed" }));
        Assert.Equal(2, (await Ok(await o.Admin.GetAsync($"/api/assets/{a}"))).GetProperty("groupIds").GetArrayLength());
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["groupIds"] = new[] { g2 } }));
        Assert.Equal([a], await MemberIdsAsync(o.Admin, g2));
        Assert.Empty(await MemberIdsAsync(o.Admin, g1));
    }

    [SkippableFact]
    public async Task Group_pm_follows_membership()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 1");
        var b = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 2");
        var c = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 3");
        var g = await GroupAsync(o.Admin, "KITCHEN");
        await MembersAsync(o.Admin, g, [a, b]);
        var task = await Pm12.TemplateAsync(o.Admin, "CLEAN");
        var start = Today.AddMonths(-3);
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, start,
            extra: new() { ["code"] = "G1", ["multiAssetMode"] = "Round", ["assetGroupId"] = g, ["managesAssetGroup"] = true })));
        Assert.Equal(g, (await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("assetGroupId").GetGuid());
        async Task<int> CoveredAsync(JsonElement results) =>
            (await Ok(await o.Admin.GetAsync($"/api/work-orders/{results.EnumerateArray().Last(r => r.GetProperty("workOrderId").ValueKind != JsonValueKind.Null).GetProperty("workOrderId").GetGuid()}")))
                .GetProperty("assets").GetArrayLength();
        Assert.Equal(2, await CoveredAsync(await Pm12.GenerateAsync(o.Admin, start, pm)));
        await MembersAsync(o.Admin, g, [c]);
        Assert.Equal(3, await CoveredAsync(await Pm12.GenerateAsync(o.Admin, start.AddMonths(1), pm)));

        await MembersAsync(o.Admin, g, [], [a, b, c]);
        var empty = (await Pm12.GenerateAsync(o.Admin, start.AddMonths(2), pm)).EnumerateArray().Last();
        Assert.Equal("No active assets in the list", empty.GetProperty("skipped").GetString());
        Assert.Equal(start.AddMonths(2).ToString("yyyy-MM-dd"), (await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("nextDueDate").GetString());

        await MembersAsync(o.Admin, g, [a]);
        await Ok(await o.Admin.PutAsJsonAsync($"/api/asset-groups/{g}", new { code = "KITCHEN", name = "Kitchen", isActive = false }));
        Assert.Equal("The asset group is inactive.", (await Pm12.GenerateAsync(o.Admin, start.AddMonths(2), pm)).EnumerateArray().Last().GetProperty("skipped").GetString());

        await Ok(await o.Admin.PutAsJsonAsync($"/api/asset-groups/{g}", new { code = "KITCHEN", name = "Kitchen", isActive = true }));
        await TestSettings.SetSettingsAsync(o.Admin, s => s["pmSkipAssetStatuses"] = new JsonArray("UnderRepair"));
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["status"] = "UnderRepair" }));
        var skipped = (await Pm12.GenerateAsync(o.Admin, start.AddMonths(2), pm)).EnumerateArray().Last();
        Assert.StartsWith("Skipped", skipped.GetProperty("skipped").GetString());
        Assert.Equal(start.AddMonths(3).ToString("yyyy-MM-dd"), (await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("nextDueDate").GetString());
    }

    [SkippableFact]
    public async Task Group_mode_pm_makes_one_work_order_per_member()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Chiller 1");
        var b = await Pm12.AssetAsync(o.Admin, o.Site, "Chiller 2");
        var g = await GroupAsync(o.Admin, "CHILLERS");
        await MembersAsync(o.Admin, g, [a, b]);
        var task = await Pm12.TemplateAsync(o.Admin, "SVC");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today,
            extra: new() { ["code"] = "G2", ["multiAssetMode"] = "Group", ["groupPlan"] = "Copy", ["assetGroupId"] = g, ["managesAssetGroup"] = true })));
        var result = (await Pm12.GenerateAsync(o.Admin, Today, pm)).EnumerateArray().Single(r => r.GetProperty("groupId").ValueKind != JsonValueKind.Null);
        Assert.Equal(2, result.GetProperty("children").GetInt32());
    }

    [SkippableFact]
    public async Task Group_sheets_and_pm_column_import()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 1");
        var b = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 2");
        string NumberOf(JsonElement asset) => asset.GetProperty("assetNumber").GetString()!;
        var na = NumberOf(await Ok(await o.Admin.GetAsync($"/api/assets/{a}")));
        var nb = NumberOf(await Ok(await o.Admin.GetAsync($"/api/assets/{b}")));
        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, a, extra: new() { ["code"] = "PM1" })));
        await RunAsync(o.Admin, Book(
            new Sheet("Asset groups", ["Code", "Name", "Active"], ["KITCHEN", "Kitchen equipment", "Yes"]),
            new Sheet("Asset group members", ["Group code", "Asset number"], ["KITCHEN", na], ["KITCHEN", nb]),
            new Sheet("PM schedules", ["Code", "Asset group code"], ["PM1", "KITCHEN"])));
        var g = Id((await Ok(await o.Admin.GetAsync("/api/asset-groups"))).EnumerateArray().Single());
        Assert.Equal(new[] { a, b }.Order(), (await MemberIdsAsync(o.Admin, g)).Order());
        var detail = await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.Equal(g, detail.GetProperty("assetGroupId").GetGuid());
        Assert.Equal(2, detail.GetProperty("groupAssets").GetArrayLength());
        await RunAsync(o.Admin, Book(new Sheet("Asset group members", ["Group code", "Asset number"], ["KITCHEN", na])));
        Assert.Equal(2, (await MemberIdsAsync(o.Admin, g)).Count);
    }

    [SkippableFact]
    public async Task Importing_an_asset_onto_a_group_pm_is_a_row_error()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge 1");
        var na = (await Ok(await o.Admin.GetAsync($"/api/assets/{a}"))).GetProperty("assetNumber").GetString()!;
        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today, a, extra: new() { ["code"] = "PM1" })));
        await RunAsync(o.Admin, Book(
            new Sheet("Asset groups", ["Code", "Name", "Active"], ["KITCHEN", "Kitchen equipment", "Yes"]),
            new Sheet("Asset group members", ["Group code", "Asset number"], ["KITCHEN", na])));
        // Naming an asset and a group on one row used to save the group and drop the asset without a word.
        var sheet = SheetOf(await RunAsync(o.Admin, Book(new Sheet("PM schedules", ["Code", "Asset number", "Asset group code"], ["PM1", na, "KITCHEN"]))), "PmSchedules");
        Assert.Contains("This PM follows an asset group. Add the asset to the group instead of naming it on the PM.", ProblemsOf(sheet));
        var detail = await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("assetGroupId").ValueKind);
        Assert.Equal(a, detail.GetProperty("assetId").GetGuid());
    }

    /// <summary>Review fix: new assets and their memberships in one file (members come after assets).</summary>
    [SkippableFact]
    public async Task New_assets_and_their_groups_import_together()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteCode = (await Ok(await o.Admin.GetAsync($"/api/sites/{o.Site}"))).GetProperty("code").GetString();
        await RunAsync(o.Admin, Book(
            new Sheet("Assets", ["Asset number", "Name", "Site code"], ["NEW-1", "New fridge", siteCode]),
            new Sheet("Asset groups", ["Code", "Name"], ["KITCHEN", "Kitchen"]),
            new Sheet("Asset group members", ["Group code", "Asset number"], ["KITCHEN", "NEW-1"])));
        var g = Id((await Ok(await o.Admin.GetAsync("/api/asset-groups"))).EnumerateArray().Single());
        Assert.Single(await MemberIdsAsync(o.Admin, g));
    }

    /// <summary>Review fix: people limited to some sites give their groups a site and can't delete or change shared ones.</summary>
    [SkippableFact]
    public async Task Site_limited_people_keep_to_their_sites_groups()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var shared = await GroupAsync(o.Admin, "CHILLERS");
        var (limited, _, _) = await UserAsync(fx, o.Admin, "Lead A", ["assets.view", "assets.edit"], siteIds: [o.Site]);
        const string rule = "People with limited access give their asset groups a site.";
        var create = await limited.PostAsJsonAsync("/api/asset-groups", new { code = "MINE", name = "Mine", siteId = (Guid?)null, isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains(rule, await create.Content.ReadAsStringAsync());
        var edit = await limited.PutAsJsonAsync($"/api/asset-groups/{shared}", new { code = "CHILLERS", name = "Chillers", siteId = (Guid?)null, isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        var delete = await limited.DeleteAsync($"/api/asset-groups/{shared}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("Only people who see every site can delete a shared asset group.", await delete.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await limited.PostAsJsonAsync("/api/asset-groups", new { code = "MINE", name = "Mine", siteId = o.Site, isActive = true })).StatusCode);
    }

    /// <summary>Review fix: an asset moved to another site can still be saved with the groups it's already in.</summary>
    [SkippableFact]
    public async Task A_moved_asset_saves_with_its_old_groups()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var siteB = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/sites", new { code = "SB", name = "Site B", isActive = true })));
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge");
        var aGroup = await GroupAsync(o.Admin, "AKITCHEN", o.Site);
        await MembersAsync(o.Admin, aGroup, [a]);
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["siteId"] = siteB }));
        await Ok(await SaveAssetAsync(o.Admin, a, new() { ["name"] = "Fridge moved", ["siteId"] = siteB, ["groupIds"] = new[] { aGroup } }));
    }

    /// <summary>Review fix: a PM on a group shows on the assets it covers.</summary>
    [SkippableFact]
    public async Task Group_pms_show_on_their_assets()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var a = await Pm12.AssetAsync(o.Admin, o.Site, "Fridge");
        var g = await GroupAsync(o.Admin, "KITCHEN");
        await MembersAsync(o.Admin, g, [a]);
        var task = await Pm12.TemplateAsync(o.Admin, "CLEAN");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, Today,
            extra: new() { ["code"] = "G1", ["multiAssetMode"] = "Round", ["assetGroupId"] = g, ["managesAssetGroup"] = true })));
        Assert.Contains(pm, (await Ok(await o.Admin.GetAsync($"/api/pm-schedules?assetId={a}&pageSize=50"))).GetProperty("items").EnumerateArray().Select(Id));
    }
}

