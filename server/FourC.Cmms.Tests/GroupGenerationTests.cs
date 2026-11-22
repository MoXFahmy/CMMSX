using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Phone;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class GroupGenerationTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Setup(Loop.Org Org, Guid A, Guid B, Guid Template, Guid Part);

    private async Task<Setup> SetupAsync()
    {
        var org = await OrgAsync(fx, emailOn: false);
        var store = await StoreAsync(org.Admin, org.Site);
        await SiteSettingsTests.SetSiteAsync(org.Admin, org.Site, new { defaultStoreId = store });
        var part = await PartAsync(org.Admin, store, "FLT");
        var a = await AssetAsync(org.Admin, org.Site, "Chiller A");
        var b = await AssetAsync(org.Admin, org.Site, "Chiller B");
        var t = await TemplateAsync(org.Admin, "CH", ["Check"], new() { ["estimatedHours"] = 4m, ["parts"] = new[] { new { partId = part, quantity = 2 } } });
        return new Setup(org, a, b, t, part);
    }

    private static Dictionary<string, object?> Group(string plan = "Copy") => new() { ["multiAssetMode"] = "Group", ["groupPlan"] = plan, ["name"] = "Chillers" };

    private Task<List<WorkOrder>> ChildrenAsync(Guid group) =>
        DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().Where(w => w.GroupId == group).OrderBy(w => w.Number).ToListAsync());

    private Task<List<WorkOrderPart>> PlannedAsync(Guid wo) => DbAsync(fx, db => db.WorkOrderParts.IgnoreQueryFilters().Where(p => p.WorkOrderId == wo).ToListAsync());

    [SkippableFact]
    public async Task Group_mode_makes_a_group_with_a_child_per_asset()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group())));
        var r = (await GenerateAsync(s.Org.Admin, Today, pm)).EnumerateArray().Single();
        Assert.Equal("GRP-00001", r.GetProperty("groupNumber").GetString());
        var group = await DbAsync(fx, db => db.WorkOrderGroups.IgnoreQueryFilters().SingleAsync(g => g.PmScheduleId == pm));
        var children = await ChildrenAsync(group.Id);
        Assert.Equal([s.A, s.B], children.Select(c => c.AssetId!.Value));
        Assert.All(children, c => Assert.Equal(group.DueDate, c.DueDate));
        Assert.All(children, c => Assert.Equal(pm, c.PmScheduleId));
        var steps = await DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.WorkOrderId == children[0].Id).Select(t => t.Description).ToListAsync());
        Assert.Equal(["Check"], steps);
    }

    [SkippableFact]
    public async Task Split_divides_hours_and_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group("Split"))));
        var groupId = (await GenerateAsync(s.Org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        foreach (var c in await ChildrenAsync(groupId))
        {
            Assert.Equal(2m, c.EstimatedHours);
            Assert.Equal(1m, (await PlannedAsync(c.Id)).Single(p => p.PartId == s.Part).Quantity);
        }
    }

    [SkippableFact]
    public async Task Copy_gives_each_child_the_plan()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group("Copy"))));
        var groupId = (await GenerateAsync(s.Org.Admin, Today, pm)).EnumerateArray().Single().GetProperty("groupId").GetGuid();
        foreach (var c in await ChildrenAsync(groupId))
        {
            Assert.Equal(4m, c.EstimatedHours);
            Assert.Equal(2m, (await PlannedAsync(c.Id)).Single(p => p.PartId == s.Part).Quantity);
        }
    }

    [SkippableFact]
    public async Task Mode_locks_after_first_generation()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var body = Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group());
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, body));
        await GenerateAsync(s.Org.Admin, Today, pm);
        body["multiAssetMode"] = "Round";
        Assert.Contains("The multi-asset mode can't change after the PM has generated work.", await Bad(await s.Org.Admin.PutAsJsonAsync($"/api/pm-schedules/{pm}", body)));
    }

    [SkippableFact]
    public async Task Groups_are_not_generated_twice()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today.AddMonths(1), assets: [s.A, s.B], extra: Group())));
        var made = await Ok(await s.Org.Admin.PostAsJsonAsync($"/api/pm-schedules/{pm}/occurrences/generate", new { date = Iso(Today.AddMonths(2)) }));
        Assert.Equal("GRP-00001", made.GetProperty("groupNumber").GetString());
        Assert.Contains("This occurrence was already generated as GRP-00001.",
            await Bad(await s.Org.Admin.PostAsJsonAsync($"/api/pm-schedules/{pm}/occurrences/generate", new { date = Iso(Today.AddMonths(2)) })));
        var results = (await GenerateAsync(s.Org.Admin, Today.AddMonths(2), pm)).EnumerateArray().ToList();
        Assert.Equal("Already generated", results[1].GetProperty("skipped").GetString());
        Assert.Equal(2, await DbAsync(fx, db => db.WorkOrderGroups.IgnoreQueryFilters().CountAsync(g => g.PmScheduleId == pm)));
    }

    [SkippableFact]
    public async Task Children_count_as_work_and_groups_do_not()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group())));
        await GenerateAsync(s.Org.Admin, Today, pm);
        var list = await Ok(await s.Org.Admin.GetAsync("/api/work-orders?pageSize=50"));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
    }

    [SkippableFact]
    public async Task Group_mode_needs_several_assets_and_the_scheduled_basis()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        Assert.Contains("multiAssetMode", await Bad(await s.Org.Admin.PostAsJsonAsync("/api/pm-schedules", Schedule(s.Org.Site, s.Template, Today, s.A, extra: Group()))));
        var actual = Group();
        actual["basis"] = "Actual";
        Assert.Contains("multiAssetMode", await Bad(await s.Org.Admin.PostAsJsonAsync("/api/pm-schedules", Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: actual))));
    }

    [SkippableFact]
    public async Task Generate_counts_the_groups_work_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var s = await SetupAsync();
        var pm = Id(await SaveScheduleAsync(s.Org.Admin, Schedule(s.Org.Site, s.Template, Today, assets: [s.A, s.B], extra: Group())));
        var answer = await Ok(await s.Org.Admin.PostAsJsonAsync("/api/pm/generate", new { until = Iso(Today), scheduleIds = new[] { pm } }));
        Assert.Equal(2, answer.GetProperty("created").GetInt32());
    }
}
