using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.Pm12;

namespace FourC.Cmms.Tests;

public class TemplatesModelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Templates_model_round_trips()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Chiller");
        var meter = await MeterAsync(org.Admin, asset, "CH-H");
        var task = await TemplateAsync(org.Admin, "T1");
        var day = new DateOnly(2026, 12, 1);
        var (scheduleId, groupId, calendarId) = await DbAsync(fx, async db =>
        {
            var t = await db.PmTasks.IgnoreQueryFilters().FirstAsync(x => x.Id == task);
            t.Title = "Chiller service"; t.WorkType = WorkType.Inspection; t.Priority = WorkPriority.High; t.RecipientIds = [org.AdminId]; t.ExpectedStopHours = 2.5m;
            var cal = new ScheduleCalendar { TenantId = org.Tenant, Code = "LOW", Name = "Low season", Kind = CalendarKind.Dates, Dates = [day, day.AddDays(7)] };
            db.ScheduleCalendars.Add(cal);
            var s = new PmSchedule
            {
                TenantId = org.Tenant, Code = "PM-X", Name = "Chillers", SiteId = org.Site, PmTaskId = task, StartDate = day, Type = PmScheduleType.Calendar,
                RecipientIds = [org.AdminId], SkipStatusesOverride = [AssetStatus.UnderRepair], NonWorkingDaysOverride = [DayOfWeek.Friday], NoStackingOverride = true,
                MultiAssetMode = MultiAssetMode.Group, GroupPlan = GroupPlan.Split, SpreadParts = 2,
            };
            s.Calendars.Add(new PmScheduleCalendar { PmScheduleId = s.Id, ScheduleCalendarId = cal.Id });
            s.ExtraMeters.Add(new PmScheduleMeter { PmScheduleId = s.Id, MeterId = meter, Interval = 500, NextDueReading = 500 });
            s.Assets.Add(new PmScheduleAsset { PmScheduleId = s.Id, AssetId = asset, Sequence = 1, PmTaskId = task });
            db.PmSchedules.Add(s);
            var g = new WorkOrderGroup { TenantId = org.Tenant, Number = "GRP-00001", Title = "Chillers", SiteId = org.Site, PmScheduleId = s.Id, PmTaskId = task, PmDueDate = day, DueDate = day };
            db.WorkOrderGroups.Add(g);
            db.WorkOrderGroupComments.Add(new WorkOrderGroupComment { TenantId = org.Tenant, GroupId = g.Id, Body = "Bring the ladder", UserName = "Admin" });
            db.AssetChecklists.Add(new AssetChecklist { TenantId = org.Tenant, AssetId = asset, PmTaskId = task });
            var m = await db.Meters.IgnoreQueryFilters().FirstAsync(x => x.Id == meter);
            m.SourceMeterId = null;
            await db.SaveChangesAsync();
            return (s.Id, g.Id, cal.Id);
        });

        await DbAsync(fx, async db =>
        {
            var t = await db.PmTasks.IgnoreQueryFilters().FirstAsync(x => x.Id == task);
            Assert.Equal(("Chiller service", WorkType.Inspection, WorkPriority.High, 2.5m), (t.Title, t.WorkType, t.Priority, t.ExpectedStopHours));
            Assert.Equal([org.AdminId], t.RecipientIds);
            var cal = await db.ScheduleCalendars.IgnoreQueryFilters().FirstAsync(x => x.Id == calendarId);
            Assert.Equal([day, day.AddDays(7)], cal.Dates);
            var s = await db.PmSchedules.IgnoreQueryFilters().Include(x => x.Calendars).Include(x => x.ExtraMeters).Include(x => x.Assets).FirstAsync(x => x.Id == scheduleId);
            Assert.Equal(PmScheduleType.Calendar, s.Type);
            Assert.Equal([AssetStatus.UnderRepair], s.SkipStatusesOverride);
            Assert.Equal([DayOfWeek.Friday], s.NonWorkingDaysOverride);
            Assert.True(s.NoStackingOverride);
            Assert.Equal((MultiAssetMode.Group, GroupPlan.Split, 2), (s.MultiAssetMode, s.GroupPlan, s.SpreadParts));
            Assert.Equal(calendarId, Assert.Single(s.Calendars).ScheduleCalendarId);
            Assert.Equal(500m, Assert.Single(s.ExtraMeters).Interval);
            Assert.Equal(task, Assert.Single(s.Assets).PmTaskId);
            var g = await db.WorkOrderGroups.IgnoreQueryFilters().FirstAsync(x => x.Id == groupId);
            Assert.Equal("GRP-00001", g.Number);
            Assert.Equal("Bring the ladder", (await db.WorkOrderGroupComments.IgnoreQueryFilters().SingleAsync(x => x.GroupId == groupId)).Body);
            Assert.True(await db.AssetChecklists.IgnoreQueryFilters().AnyAsync(x => x.AssetId == asset && x.PmTaskId == task));
            return 0;
        });
    }

    [SkippableFact]
    public async Task An_existing_schedule_gets_the_defaults()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = await AssetAsync(org.Admin, org.Site, "Pump");
        var task = await TemplateAsync(org.Admin, "T2");
        var id = Id(await SaveScheduleAsync(org.Admin, Schedule(org.Site, task, Today, asset)));
        var s = await DbAsync(fx, db => db.PmSchedules.IgnoreQueryFilters().FirstAsync(x => x.Id == id));
        Assert.Equal((MultiAssetMode.Round, GroupPlan.Copy, (int?)null, 0), (s.MultiAssetMode, s.GroupPlan, s.SpreadParts, s.SpreadIndex));
        Assert.Null(s.SkipStatusesOverride);
        Assert.Empty(s.RecipientIds);
        var t = await DbAsync(fx, db => db.PmTasks.IgnoreQueryFilters().FirstAsync(x => x.Id == task));
        Assert.Equal(WorkType.Preventive, t.WorkType);
        Assert.Null(t.Priority);
    }

    [Fact]
    public void Group_numbers_use_their_own_sequence()
    {
        var (sequence, prefix) = Numbering.For(new TenantSettings(), null, NumberKind.WorkGroup);
        Assert.Equal(("WorkGroup", "GRP-"), (sequence, prefix));
    }
}
