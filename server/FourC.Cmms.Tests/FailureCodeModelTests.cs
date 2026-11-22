using System.Net.Http.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.14.0 model: symptom and remedy codes, a category's downtime cost, the organization logo setting.</summary>
public class FailureCodeModelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Codes_save_and_link_to_requests_and_work_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var request = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Leak", siteId = org.Site, priority = "High", type = "Corrective" })));
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix leak", siteId = org.Site, priority = "High", type = "Corrective" })));
        var (symptom, remedy) = await DbAsync(fx, async db =>
        {
            var s = new Symptom { TenantId = org.Tenant, Code = "LEAK", Name = "Leaking" };
            var r = new Remedy { TenantId = org.Tenant, Code = "SEAL", Name = "Replaced the seal" };
            db.Symptoms.Add(s); db.Remedies.Add(r);
            (await db.WorkRequests.IgnoreQueryFilters().SingleAsync(x => x.Id == request)).SymptomId = s.Id;
            var w = await db.WorkOrders.IgnoreQueryFilters().SingleAsync(x => x.Id == wo);
            w.SymptomId = s.Id; w.RemedyId = r.Id;
            await db.SaveChangesAsync();
            return (s.Id, r.Id);
        });
        var (req, work) = await DbAsync(fx, async db => (
            await db.WorkRequests.IgnoreQueryFilters().Include(x => x.Symptom).SingleAsync(x => x.Id == request),
            await db.WorkOrders.IgnoreQueryFilters().Include(x => x.Symptom).Include(x => x.Remedy).SingleAsync(x => x.Id == wo)));
        Assert.Equal((symptom, "LEAK"), (req.SymptomId!.Value, req.Symptom!.Code));
        Assert.Equal((symptom, remedy, "SEAL", true), (work.SymptomId!.Value, work.RemedyId!.Value, work.Remedy!.Code, work.Remedy.IsActive));
    }

    [SkippableFact]
    public async Task Category_downtime_cost_saves()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var id = await DbAsync(fx, async db =>
        {
            var c = new AssetCategory { TenantId = org.Tenant, Code = "PUMP", Name = "Pumps", DowntimeCostPerHour = 20.5m };
            db.AssetCategories.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        Assert.Equal(20.5m, (await DbAsync(fx, db => db.AssetCategories.IgnoreQueryFilters().SingleAsync(c => c.Id == id))).DowntimeCostPerHour);
    }

    [Fact]
    public void Asset_cost_wins_over_its_category()
    {
        Assert.Equal(50m, CostMath.DowntimeCostPerHour(50m, 20m));
        Assert.Equal(20m, CostMath.DowntimeCostPerHour(null, 20m));
        Assert.Null(CostMath.DowntimeCostPerHour(null, null));
    }

    [Fact]
    public void Logo_version_defaults_to_none() => Assert.Null(new TenantSettings().LogoVersion);

    [Theory]
    [InlineData("Unknown symptom.")]
    [InlineData("Unknown cause.")]
    [InlineData("Unknown remedy.")]
    [InlineData("Choose a cause for breakdown work.")]
    [InlineData("Choose an asset.")]
    [InlineData("Choose a year from 2000 to 2100.")]
    [InlineData("Choose a PNG, JPEG or WebP image.")]
    [InlineData("The logo can be at most 1 MB.")]
    [InlineData("Downtime cost can't be negative.")]
    [InlineData("Failure tree")]
    [InlineData("Completed work by symptom, cause and remedy, with hours, downtime and cost.")]
    [InlineData("Responsiveness")]
    [InlineData("Each request and work order against its response and completion targets, with averages by priority and trade.")]
    [InlineData("PM annual plan")]
    [InlineData("Each PM's year, month by month: what was done, late, missed or cancelled, and what is still planned.")]
    [InlineData("Asset work history")]
    [InlineData("Every work order on one asset in the period, with its codes, hours, cost and downtime.")]
    [InlineData("Technician")]
    [InlineData("Symptom → cause → remedy")]
    [InlineData("With a symptom")]
    [InlineData("With a cause")]
    [InlineData("With a remedy")]
    [InlineData("Not recorded")]
    [InlineData("Orders")]
    [InlineData("Avg. response (h)")]
    [InlineData("Avg. completion (h)")]
    [InlineData("Respond by")]
    [InlineData("Responded")]
    [InlineData("Response (h)")]
    [InlineData("Response")]
    [InlineData("Completion (h)")]
    [InlineData("Met")]
    [InlineData("Pending")]
    [InlineData("PM")]
    [InlineData("PMs")]
    [InlineData("Done on time")]
    [InlineData("Planned")]
    [InlineData("Round of 2 assets")]
    [InlineData("Group of 3 assets")]
    [InlineData("Type")]
    [InlineData("Symptom")]
    [InlineData("Cause")]
    [InlineData("Remedy")]
    [InlineData("with its components")]
    public void New_texts_have_Arabic(string english) =>
        Assert.NotEqual(english, FourC.Cmms.Api.Infrastructure.Translator.T(english, "ar"));
}
