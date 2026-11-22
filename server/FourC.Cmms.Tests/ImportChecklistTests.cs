using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportChecklistTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] PointHeadings =
        ["Code", "Name", "Kind", "Unit", "Decimals", "Valid min", "Valid max", "Alarm low", "Alert low", "Target", "Alert high", "Alarm high", "Alarm high template", "Assets"];
    private static readonly string[] StepHeadings =
        ["Task code", "Step", "Description", "Type", "Unit", "Measurement point", "Photo required", "Before starting", "Follow-up", "Follow-up template"];

    private static Sheet[] Basics() =>
    [
        new("Sites", SiteHeadings, ["CAI", "Cairo", null]),
        new("Assets", ["Asset number", "Name", "Site code"], ["AHU-01", "Air handler", "CAI"], ["AHU-02", "Air handler 2", "CAI"]),
        new("Work templates", ["Code", "Name"], ["SVC", "Service"], ["REP", "Repair"], ["HOT", "Overheating"]),
    ];

    private static async Task<JsonElement> PointAsync(HttpClient c, string code)
    {
        var id = (await OkAsync(await c.GetAsync("/api/measurement-points"))).EnumerateArray().Single(p => p.GetProperty("code").GetString() == code).GetProperty("id");
        return await OkAsync(await c.GetAsync($"/api/measurement-points/{id}"));
    }

    private static async Task<List<JsonElement>> StepsAsync(HttpClient c, string code)
    {
        var id = (await OkAsync(await c.GetAsync($"/api/pm-tasks?search={code}"))).EnumerateArray().Single(t => t.GetProperty("code").GetString() == code).GetProperty("id");
        return (await OkAsync(await c.GetAsync($"/api/pm-tasks/{id}"))).GetProperty("stepDetails").EnumerateArray().ToList();
    }

    [SkippableFact]
    public async Task Steps_sheet_imports_step_types()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("Measurement points", PointHeadings, ["SAT", "Supply air", "Temperature", "°C", 1m, 0m, 60m, 5m, 10m, 22m, 28m, 32m, "HOT", "AHU-01, AHU-02"]),
            new Sheet("PM steps", StepHeadings,
                ["SVC", 1m, "Lock out", "Check", null, null, null, "Yes", null, null],
                ["SVC", 2m, "Supply air", "Number", null, "SAT", null, null, null, null],
                ["SVC", 3m, "Belt", "Check", null, null, "Yes", null, "Work order", "REP"]),
        ]));
        var steps = await StepsAsync(c, "SVC");
        Assert.Equal(["Check", "Number", "Check"], steps.Select(s => s.GetProperty("type").GetString()));
        Assert.True(steps[0].GetProperty("beforeStart").GetBoolean());
        Assert.Equal((await PointAsync(c, "SAT")).GetProperty("id").GetGuid(), steps[1].GetProperty("measurementPointId").GetGuid());
        Assert.Equal(("WorkOrder", true), (steps[2].GetProperty("followUp").GetString(), steps[2].GetProperty("photoRequired").GetBoolean()));
    }

    [SkippableFact]
    public async Task Unknown_point_code_is_refused_per_row()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(),
            new Sheet("PM steps", StepHeadings, ["SVC", 1m, "Supply air", "Number", null, "NOPE", null, null, null, null],
                ["SVC", 2m, "Belt", "Check", null, null, null, null, "Work order", "NOPE"]),
        ]));
        var problems = ProblemsOf(SheetOf(result, "PmSteps")).ToList();
        Assert.Contains("Measurement point NOPE not found.", problems);
        Assert.Contains("Work template NOPE not found.", problems);
    }

    [SkippableFact]
    public async Task Points_sheet_adds_and_updates_by_code()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("Measurement points", PointHeadings, ["SAT", "Supply air", "Temperature", "°C", 1m, 0m, 60m, 5m, 10m, 22m, 28m, 32m, "HOT", "AHU-01"]),
        ]));
        var p = await PointAsync(c, "SAT");
        Assert.Equal(("Temperature", 32m, 1), (p.GetProperty("kind").GetString(), p.GetProperty("alarmHigh").GetDecimal(), p.GetProperty("assets").GetArrayLength()));
        Assert.Equal(JsonValueKind.String, p.GetProperty("alarmHighTemplateId").ValueKind);
        var again = await RunAsync(c, Book(new Sheet("Measurement points", ["Code", "Alarm high", "Assets"], ["SAT", 35m, "AHU-01, AHU-02"])));
        Assert.Equal(1, Count(SheetOf(again, "MeasurementPoints"), "changed"));
        p = await PointAsync(c, "SAT");
        Assert.Equal((35m, 28m, 2), (p.GetProperty("alarmHigh").GetDecimal(), p.GetProperty("alertHigh").GetDecimal(), p.GetProperty("assets").GetArrayLength()));
    }

    [SkippableFact]
    public async Task Point_level_order_is_checked_in_the_import()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var result = await RunAsync(c, Book([.. Basics(),
            new Sheet("Measurement points", PointHeadings, ["BAD", "Bad", "Temperature", "°C", 1m, 0m, 60m, 5m, 10m, 22m, 40m, 32m, null, null]),
        ]));
        Assert.Contains("Levels must go up: alarm low, alert low, target, alert high, alarm high.", ProblemsOf(SheetOf(result, "MeasurementPoints")));
    }

    [SkippableFact]
    public async Task Export_round_trips_points_and_typed_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book([.. Basics(),
            new Sheet("Measurement points", PointHeadings, ["SAT", "Supply air", "Temperature", "°C", 1m, 0m, 60m, 5m, 10m, 22m, 28m, 32m, "HOT", "AHU-01"]),
            new Sheet("PM steps", StepHeadings, ["SVC", 1m, "Lock out", "Check", null, null, null, "Yes", null, null],
                ["SVC", 2m, "Supply air", "Number", null, "SAT", null, null, "None", null]),
        ]));
        var current = await (await c.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var check = await CheckAsync(c, current);
        foreach (var sheet in check.GetProperty("sheets").EnumerateArray())
            Assert.True(Count(sheet, "new") == 0 && Count(sheet, "changed") == 0 && Count(sheet, "problems") == 0,
                $"{sheet.GetProperty("sheet").GetString()}: {sheet.GetProperty("rows")}");
        Assert.Equal(1, Count(SheetOf(check, "MeasurementPoints"), "unchanged"));
        Assert.Equal(2, Count(SheetOf(check, "PmSteps"), "unchanged"));
    }
}
