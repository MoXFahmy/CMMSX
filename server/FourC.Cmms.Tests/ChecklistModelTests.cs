using System.Net.Http.Json;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class ChecklistModelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed class Ctx(IReadOnlyList<Guid>? sites) : ITenantContext
    {
        public Guid? TenantId { get; } = Guid.NewGuid();
        public Guid? UserId => null;
        public string? UserName => null;
        public IReadOnlyList<Guid>? SiteIds => sites;
    }

    [SkippableFact]
    public async Task Existing_steps_are_check_steps()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var template = await TemplateAsync(org.Admin, "T1", ["Look"]);
        var step = await DbAsync(fx, db => db.PmTaskSteps.IgnoreQueryFilters().SingleAsync(s => s.PmTaskId == template));
        Assert.Equal((StepType.Check, FollowUpKind.Default, false, false), (step.Type, step.FollowUp, step.PhotoRequired, step.BeforeStart));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Fix", siteId = org.Site, locationId = org.Location, priority = "Medium", type = "Corrective", tasks = new[] { "Look" },
        })));
        var task = await DbAsync(fx, db => db.WorkOrderTasks.IgnoreQueryFilters().SingleAsync(t => t.WorkOrderId == wo));
        Assert.Equal(StepType.Check, task.Type);
        Assert.Null(task.Answer);
        Assert.Null(task.Value);
    }

    [SkippableFact]
    public async Task Points_and_readings_round_trip()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var a = await AssetAsync(org.Admin, org.Site, "AHU-1");
        var b = await AssetAsync(org.Admin, org.Site, "AHU-2");
        var template = await TemplateAsync(org.Admin, "ALARM");
        var pointId = await DbAsync(fx, async db =>
        {
            var p = new MeasurementPoint
            {
                TenantId = org.Tenant, Code = "SAT", Name = "Supply air temperature", Kind = MeasurementKind.Temperature, Unit = "°C", Decimals = 1,
                ValidMin = 0, ValidMax = 60, AlarmLow = 5, AlertLow = 10, Target = 22, AlertHigh = 28, AlarmHigh = 32, AlarmHighTemplateId = template,
            };
            p.Assets.Add(new MeasurementPointAsset { PointId = p.Id, AssetId = a });
            p.Assets.Add(new MeasurementPointAsset { PointId = p.Id, AssetId = b });
            db.MeasurementPoints.Add(p);
            db.MeasurementReadings.Add(new MeasurementReading
            {
                TenantId = org.Tenant, PointId = p.Id, AssetId = a, SiteId = org.Site, Value = 30.5m, ReadAt = DateTimeOffset.UtcNow, Level = MeasureLevel.AlertHigh, ByName = "Admin",
            });
            await db.SaveChangesAsync();
            return p.Id;
        });
        var (point, readings) = await DbAsync(fx, async db => (await db.MeasurementPoints.IgnoreQueryFilters().Include(p => p.Assets).SingleAsync(p => p.Id == pointId),
            await db.MeasurementReadings.IgnoreQueryFilters().Where(r => r.PointId == pointId).ToListAsync()));
        Assert.Equal((28m, 32m, template), (point.AlertHigh!.Value, point.AlarmHigh!.Value, point.AlarmHighTemplateId!.Value));
        Assert.Equal(2, point.Assets.Count);
        Assert.Equal((30.5m, MeasureLevel.AlertHigh, org.Site), (readings.Single().Value, readings.Single().Level, readings.Single().SiteId));
    }

    [Fact]
    public void Readings_follow_site_access()
    {
        using var db = new CmmsDbContext(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none;TrustServerCertificate=True").Options,
            new Ctx([Guid.NewGuid()]));
        var sql = db.MeasurementReadings.Where(r => r.Value > 1).ToQueryString();
        Assert.Contains("[Sites]", sql);
        Assert.Contains("[Assets]", sql);
    }

    [Fact]
    public void Settings_default_to_request_follow_ups()
    {
        var s = new TenantSettings();
        Assert.Equal(FollowUpKind.Request, s.FollowUpDefault);
        Assert.Null(s.FollowUpTemplateId);
        Assert.False(s.RequireReadingSteps);
    }
}
