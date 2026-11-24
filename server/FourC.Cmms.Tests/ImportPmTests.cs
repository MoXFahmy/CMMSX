using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportPmTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] ScheduleHeadings =
        ["Code", "Name", "Task code", "Site code", "Asset number", "Schedule", "Every", "Unit", "Count from", "Start date"];

    private static Sheet[] Basics() =>
    [
        new("Sites", SiteHeadings, ["CAI", "Cairo", null]),
        new("Stores", ["Code", "Name", "Site code"], ["ST1", "Main store", "CAI"]),
        new("Assets", ["Asset number", "Name", "Site code"], ["PUMP-01", "Main pump", "CAI"]),
        new("Parts", ["Part number", "Name", "Unit"], ["BRG-6205", "Ball bearing", "EA"]),
        new("PM tasks", ["Code", "Name"], ["LUB-M", "Monthly lubrication"], ["CHK-W", "Weekly check"]),
    ];

    private static async Task<JsonElement> TaskAsync(HttpClient c, string code)
    {
        var id = (await OkAsync(await c.GetAsync($"/api/pm-tasks?search={code}"))).EnumerateArray().Single(t => t.GetProperty("code").GetString() == code).GetProperty("id");
        return await OkAsync(await c.GetAsync($"/api/pm-tasks/{id}"));
    }

    private static async Task<JsonElement> ScheduleAsync(HttpClient c, string code)
    {
        var id = (await OkAsync(await c.GetAsync($"/api/pm-schedules?search={code}"))).GetProperty("items").EnumerateArray()
            .Single(s => s.GetProperty("code").GetString() == code).GetProperty("id");
        return await OkAsync(await c.GetAsync($"/api/pm-schedules/{id}"));
    }

    [SkippableFact]
    public async Task A_task_gets_its_steps_and_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(),
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 2m, "Check belts"], ["LUB-M", 1m, "Grease bearings"], ["CHK-W", 1m, "Look around"]),
            new Sheet("PM parts", ["Task code", "Part number", "Quantity"], ["LUB-M", "BRG-6205", 2m])]));
        Assert.Equal(3, Count(SheetOf(result, "PmSteps"), "new"));
        var task = await TaskAsync(c, "LUB-M");
        Assert.Equal(["Grease bearings", "Check belts"], task.GetProperty("steps").EnumerateArray().Select(s => s.GetString()));
        var part = Assert.Single(task.GetProperty("parts").EnumerateArray());
        Assert.Equal(2m, part.GetProperty("quantity").GetDecimal());
    }

    [SkippableFact]
    public async Task Steps_listed_again_replace_the_old_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease bearings"], ["LUB-M", 2m, "Check belts"], ["CHK-W", 1m, "Look around"])]));
        var second = await RunAsync(c, Book(new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease all bearings"])));
        Assert.Equal(1, Count(SheetOf(second, "PmSteps"), "changed"));
        Assert.Equal(["Grease all bearings"], (await TaskAsync(c, "LUB-M")).GetProperty("steps").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["Look around"], (await TaskAsync(c, "CHK-W")).GetProperty("steps").EnumerateArray().Select(s => s.GetString()));
    }

    [SkippableFact]
    public async Task Removed_steps_and_parts_show_in_the_preview()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics().Where(x => x.Name != "Parts"),
            new Sheet("Parts", ["Part number", "Name", "Unit"], ["BRG-6205", "Ball bearing", "EA"], ["SEAL-01", "Seal", "EA"]),
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease"], ["LUB-M", 2m, "Check belts"], ["LUB-M", 3m, "Clean"]),
            new Sheet("PM parts", ["Task code", "Part number", "Quantity"], ["LUB-M", "BRG-6205", 2m], ["LUB-M", "SEAL-01", 1m])]));

        var check = await CheckAsync(c, Book(
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease"], ["LUB-M", 2m, "Check belts"]),
            new Sheet("PM parts", ["Task code", "Part number", "Quantity"], ["LUB-M", "BRG-6205", 2m])));
        var steps = SheetOf(check, "PmSteps");
        Assert.Equal(1, Count(steps, "changed"));
        var change = steps.GetProperty("rows")[0].GetProperty("changes").EnumerateArray().Single();
        Assert.Equal(("Steps", "Grease; Check belts; Clean", "Grease; Check belts"),
            (change.GetProperty("column").GetString(), change.GetProperty("old").GetString(), change.GetProperty("new").GetString()));
        Assert.Equal(1, Count(SheetOf(check, "PmParts"), "changed"));
    }

    [SkippableFact]
    public async Task A_problem_row_keeps_the_task_list()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease"], ["LUB-M", 2m, "Check belts"])]));

        var result = await RunAsync(c, Book(new Sheet("PM steps", ["Task code", "Step", "Description"], ["LUB-M", 1m, "Grease more"], ["LUB-M", 2m, null])));
        var sheet = SheetOf(result, "PmSteps");
        Assert.Equal(2, Count(sheet, "problems"));
        Assert.Contains("Not saved: fix row 3 of this task first.", ProblemsOf(sheet));
        Assert.Equal(["Grease", "Check belts"], (await TaskAsync(c, "LUB-M")).GetProperty("steps").EnumerateArray().Select(x => x.GetString()));
    }

    [SkippableFact]
    public async Task A_schedule_comes_out_as_on_screen()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(),
            new Sheet("PM schedules", ScheduleHeadings, ["PM-LUB-01", "Pump lubrication", "LUB-M", "CAI", "PUMP-01", "Every", 3m, "Month", null, new DateOnly(2026, 12, 1)])]));
        Assert.Equal(1, Count(SheetOf(result, "PmSchedules"), "new"));
        var s = await ScheduleAsync(c, "PM-LUB-01");
        Assert.Equal("2026-12-01", s.GetProperty("nextDueDate").GetString());
        Assert.Equal("Every 3 months", s.GetProperty("schedule").GetString());
    }

    [SkippableFact]
    public async Task A_new_schedule_needs_its_task_and_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(),
            new Sheet("PM schedules", ScheduleHeadings,
                ["PM-A", "No task", null, "CAI", "PUMP-01", "Every", 1m, "Month", null, new DateOnly(2026, 12, 1)],
                ["PM-B", "No site", "LUB-M", null, "PUMP-01", "Every", 1m, "Month", null, new DateOnly(2026, 12, 1)])]));
        var sheet = SheetOf(result, "PmSchedules");
        Assert.Equal((0, 2), (Count(sheet, "new"), Count(sheet, "problems")));
        Assert.Contains("Task code is required.", ProblemsOf(sheet));
        Assert.Contains("Choose a site.", ProblemsOf(sheet));
    }

    [SkippableFact]
    public async Task A_meter_schedule_is_not_made_by_import()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var sheet = SheetOf(await CheckAsync(c, Book([.. Basics(),
            new Sheet("PM schedules", ScheduleHeadings, ["PM-M-01", "Run hours", "LUB-M", "CAI", "PUMP-01", "Meter", 1m, null, null, new DateOnly(2026, 12, 1)])])), "PmSchedules");
        Assert.Contains("Set up meter-based schedules on screen.", ProblemsOf(sheet));
    }

    [SkippableFact]
    public async Task Basis_cannot_change_after_generation()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("PM schedules", ScheduleHeadings, ["PM-LUB-01", "Pump lubrication", "LUB-M", "CAI", "PUMP-01", "Every", 1m, "Month", "Due date", start])]));
        var id = (await ScheduleAsync(c, "PM-LUB-01")).GetProperty("id").GetGuid();
        await OkAsync(await c.PostAsJsonAsync("/api/pm/generate", new { until = start.AddDays(10), scheduleIds = new[] { id } }));

        var sheet = SheetOf(await CheckAsync(c, Book(new Sheet("PM schedules", ["Code", "Count from"], ["PM-LUB-01", "Completion"]))), "PmSchedules");
        Assert.Contains("The generation basis cannot change after work orders were generated (as in 4CSQL). Create a new schedule instead.", ProblemsOf(sheet));
    }

    [SkippableFact]
    public async Task Imports_work_templates_with_new_columns()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var book = Book(new Sheet("Work templates", ["Code", "Name", "Title", "Work type", "Priority", "Expected stop hours"],
            ["INS-1", "Chiller inspection", "Inspect chiller", "Inspection", "High", 2.5m]));
        Assert.Equal(1, Count(SheetOf(await RunAsync(c, book), "PmTasks"), "new"));
        var t = await TaskAsync(c, "INS-1");
        Assert.Equal(("Inspect chiller", "Inspection", "High", 2.5m),
            (t.GetProperty("title").GetString(), t.GetProperty("workType").GetString(), t.GetProperty("priority").GetString(), t.GetProperty("expectedStopHours").GetDecimal()));
    }

    [SkippableFact]
    public async Task Old_sheet_name_still_imports()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        Assert.Equal(1, Count(SheetOf(await RunAsync(c, Book(new Sheet("PM tasks", ["Code", "Name"], ["OLD-1", "Old name"]))), "PmTasks"), "new"));
        Assert.Equal(1, Count(SheetOf(await RunAsync(c, Book(new Sheet("مهام الصيانة الوقائية", ["الرمز", "الاسم"], ["OLD-2", "Arabic old name"]))), "PmTasks"), "new"));
        await TaskAsync(c, "OLD-2");
    }

    [SkippableFact]
    public async Task Multi_asset_mode_imports_and_locks_like_the_screen()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var site = (await OkAsync(await c.PostAsJsonAsync("/api/sites", new { code = "CAI", name = "Cairo", isActive = true }))).GetProperty("id").GetGuid();
        var a = await Pm12.AssetAsync(c, site, "Fan A");
        var b = await Pm12.AssetAsync(c, site, "Fan B");
        var task = await Pm12.TemplateAsync(c, "FAN");
        await Pm12.SaveScheduleAsync(c, Pm12.Schedule(site, task, Loop.Today, assets: [a, b], extra: new() { ["code"] = "PM-FANS" }));
        string[] heads = ["Code", "Name", "Task code", "Site code", "Schedule", "Every", "Unit", "Start date", "Multi-asset mode"];
        Assert.Equal(1, Count(SheetOf(await RunAsync(c, Book(new Sheet("PM schedules", heads, ["PM-FANS", "Fans", "FAN", "CAI", "Every", 1m, "Month", Loop.Iso(Loop.Today), "Group"]))), "PmSchedules"), "changed"));
        Assert.Equal("Group", (await ScheduleAsync(c, "PM-FANS")).GetProperty("multiAssetMode").GetString());
        await Pm12.GenerateAsync(c, Loop.Today, (await ScheduleAsync(c, "PM-FANS")).GetProperty("id").GetGuid());
        var again = await RunAsync(c, Book(new Sheet("PM schedules", heads, ["PM-FANS", "Fans", "FAN", "CAI", "Every", 1m, "Month", Loop.Iso(Loop.Today), "Round"])));
        Assert.Contains("The multi-asset mode can't change after the PM has generated work.", ProblemsOf(SheetOf(again, "PmSchedules")));
    }

    [SkippableFact]
    public async Task Export_has_the_new_columns()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var bytes = await (await c.GetAsync("/api/import/template")).Content.ReadAsByteArrayAsync();
        var book = FourC.Cmms.Api.Features.Import.SpreadsheetReader.Read(new MemoryStream(bytes), "book.xlsx");
        string?[] Heads(string sheet) => book.Sheets.Single(x => x.Name == sheet).Rows[0].Select(x => x?.ToString()).ToArray();
        Assert.Equal(["Code", "Name", "Name (Arabic)", "Description", "Trade code", "Estimated hours", "Safety notes", "Safety notes (Arabic)", "Title", "Title (Arabic)", "Work type", "Priority", "Expected stop hours", "Active"],
            Heads("Work templates"));
        Assert.Contains("Multi-asset mode", Heads("PM schedules"));
    }
}
