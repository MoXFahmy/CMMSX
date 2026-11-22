using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Tests;

public class ChecklistPureTests
{
    private static MeasurementPoint Sat() => new()
    {
        Code = "SAT", Name = "Supply air", Unit = "°C", ValidMin = 0, ValidMax = 60, AlarmLow = 5, AlertLow = 10, Target = 22, AlertHigh = 28, AlarmHigh = 32,
    };

    private static WorkOrderTask Step(int seq, string text, StepType type = StepType.Check, bool safety = false, StepAnswer? answer = null, decimal? value = null,
        Guid? asset = null, bool photo = false, MeasureLevel? level = null, string? textValue = null) => new()
    {
        Sequence = seq, Description = text, Type = type, BeforeStart = safety, Answer = answer, Value = value, AssetId = asset, PhotoRequired = photo, Level = level, TextValue = textValue,
    };

    [Fact]
    public void Levels_from_alarm_to_alarm()
    {
        var p = Sat();
        Assert.Equal(MeasureLevel.AlarmLow, MeasureLevels.LevelOf(p, 4));
        Assert.Equal(MeasureLevel.AlarmLow, MeasureLevels.LevelOf(p, 5));
        Assert.Equal(MeasureLevel.AlertLow, MeasureLevels.LevelOf(p, 10));
        Assert.Equal(MeasureLevel.Normal, MeasureLevels.LevelOf(p, 22));
        Assert.Equal(MeasureLevel.AlertHigh, MeasureLevels.LevelOf(p, 28));
        Assert.Equal(MeasureLevel.AlarmHigh, MeasureLevels.LevelOf(p, 33));
    }

    [Fact]
    public void Unset_levels_are_skipped()
    {
        var p = new MeasurementPoint { Code = "I", Name = "Current", AlarmHigh = 50 };
        Assert.Equal(MeasureLevel.Normal, MeasureLevels.LevelOf(p, 49));
        Assert.Equal(MeasureLevel.AlarmHigh, MeasureLevels.LevelOf(p, 50));
        Assert.Equal(MeasureLevel.Normal, MeasureLevels.LevelOf(p, -100));
    }

    [Fact]
    public void Out_of_range_is_detected()
    {
        Assert.True(MeasureLevels.OutOfRange(Sat(), 61));
        Assert.True(MeasureLevels.OutOfRange(Sat(), -1));
        Assert.False(MeasureLevels.OutOfRange(Sat(), 60));
        Assert.False(MeasureLevels.OutOfRange(new MeasurementPoint { Code = "X", Name = "X" }, 1e9m));
    }

    [Fact]
    public void Range_message_fits_the_bounds_set()
    {
        Assert.Equal("Enter a value from 0 to 60 °C.", MeasureLevels.RangeMessage(Sat()));
        var max = Sat(); max.ValidMin = null;
        Assert.Equal("Enter a value of at most 60 °C.", MeasureLevels.RangeMessage(max));
        var min = Sat(); min.ValidMax = null;
        Assert.Equal("Enter a value of at least 0 °C.", MeasureLevels.RangeMessage(min));
        min.Unit = null;
        Assert.Equal("Enter a value of at least 0.", MeasureLevels.RangeMessage(min));
    }

    [Fact]
    public void Point_problems()
    {
        var range = Sat(); range.ValidMin = 60; range.ValidMax = 60;
        Assert.Contains(("validMax", "The valid range's minimum must be below its maximum."), MeasureLevels.Problems(range));
        var order = Sat(); order.AlertHigh = 40;
        Assert.Contains(("alarmHigh", "Levels must go up: alarm low, alert low, target, alert high, alarm high."), MeasureLevels.Problems(order));
        var outside = Sat(); outside.AlarmHigh = 70;
        Assert.Contains(("alarmHigh", "Levels must be inside the valid range."), MeasureLevels.Problems(outside));
        Assert.Empty(MeasureLevels.Problems(Sat()));
    }

    [Fact]
    public void Level_templates()
    {
        var p = Sat(); var alarm = Guid.NewGuid(); var alert = Guid.NewGuid();
        p.AlarmHighTemplateId = alarm; p.AlertHighTemplateId = alert;
        Assert.Equal(alarm, MeasureLevels.TemplateFor(p, MeasureLevel.AlarmHigh));
        Assert.Equal(alert, MeasureLevels.TemplateFor(p, MeasureLevel.AlertHigh));
        Assert.Null(MeasureLevels.TemplateFor(p, MeasureLevel.AlarmLow));
        Assert.Null(MeasureLevels.TemplateFor(p, MeasureLevel.Normal));
    }

    [Fact]
    public void Answered_per_type()
    {
        Assert.False(StepRules.Answered(Step(1, "a")));
        Assert.True(StepRules.Answered(new WorkOrderTask { Description = "ticked before 0.13.0", IsDone = true }));
        Assert.True(StepRules.Answered(Step(1, "a", answer: StepAnswer.NotApplicable)));
        Assert.False(StepRules.Answered(Step(1, "a", StepType.Number)));
        Assert.True(StepRules.Answered(Step(1, "a", StepType.Number, value: 0)));
        Assert.True(StepRules.Answered(Step(1, "a", StepType.Meter, value: 120)));
        Assert.False(StepRules.Answered(Step(1, "a", StepType.Text)));
        Assert.True(StepRules.Answered(Step(1, "a", StepType.Text, textValue: "worn")));
    }

    [Fact]
    public void Safety_block_names_the_steps()
    {
        var steps = new[] { Step(1, "Lock out", safety: true), Step(2, "Permit", safety: true, answer: StepAnswer.Ok), Step(3, "Clean") };
        Assert.Equal("Answer the safety steps first: #1 Lock out.", StepRules.SafetyBlock(steps, null, forStart: true));
        Assert.Equal("Answer the safety steps first: #1 Lock out.", StepRules.SafetyBlock(steps, null, forStart: false));
        steps[0].Answer = StepAnswer.NotApplicable;
        Assert.Null(StepRules.SafetyBlock(steps, null, forStart: true));
    }

    [Fact]
    public void Safety_not_ok_blocks()
    {
        var steps = new[] { Step(1, "Lock out", safety: true, answer: StepAnswer.NotOk), Step(2, "Gas", StepType.Number, safety: true, value: 3, level: MeasureLevel.Normal) };
        Assert.Equal("A safety step isn't OK: #1 Lock out.", StepRules.SafetyBlock(steps, null, forStart: true));
        steps[0].Answer = StepAnswer.Ok; steps[1].Level = MeasureLevel.AlarmHigh;
        Assert.Equal("A safety step isn't OK: #2 Gas.", StepRules.SafetyBlock(steps, null, forStart: true));
    }

    [Fact]
    public void Round_safety_only_blocks_its_asset()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var steps = new[] { Step(1, "Lock out A", safety: true, asset: a), Step(2, "Check A", asset: a), Step(3, "Lock out B", safety: true, asset: b, answer: StepAnswer.Ok), Step(4, "Check B", asset: b) };
        Assert.Null(StepRules.SafetyBlock(steps, b, forStart: false));
        Assert.Equal("Answer the safety steps first: #1 Lock out A.", StepRules.SafetyBlock(steps, a, forStart: false));
        Assert.Null(StepRules.SafetyBlock(steps, null, forStart: true)); // starting a round needs only the safety steps without an asset
    }

    [Fact]
    public void Completion_problems()
    {
        var steps = new[] { Step(1, "Look", answer: StepAnswer.Ok), Step(2, "Temperature", StepType.Number), Step(3, "Belt", photo: true, answer: StepAnswer.Ok) };
        Assert.Equal(["Take a photo for: #3 Belt.", "Answer these steps first: #2 Temperature."], StepRules.CompletionProblems(steps, new HashSet<Guid>(), requireReadings: true));
        Assert.Equal(["Take a photo for: #3 Belt."], StepRules.CompletionProblems(steps, new HashSet<Guid>(), requireReadings: false));
        Assert.Empty(StepRules.CompletionProblems(steps, new HashSet<Guid> { steps[2].Id }, requireReadings: false));
    }

    [Fact]
    public void Round_progress_colours()
    {
        Assert.Null(StepRules.RoundProgress([Step(1, "a", answer: StepAnswer.Ok), Step(2, "b")]));
        Assert.Equal("green", StepRules.RoundProgress([Step(1, "a", answer: StepAnswer.Ok), Step(2, "b", StepType.Number, value: 20, level: MeasureLevel.Normal)]));
        Assert.Equal("red", StepRules.RoundProgress([Step(1, "a", answer: StepAnswer.NotOk), Step(2, "b")]));
        Assert.Equal("red", StepRules.RoundProgress([Step(1, "a", StepType.Number, value: 40, level: MeasureLevel.AlarmHigh)]));
    }
}
