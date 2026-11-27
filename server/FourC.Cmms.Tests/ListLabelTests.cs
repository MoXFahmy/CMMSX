using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Tenancy;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.21.0: each organization's names, colours, order and hidden values for work types, priorities, asset statuses and criticality.</summary>
public class ListLabelTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static Task<HttpResponseMessage> PutListsAsync(HttpClient c, object listLabels) => c.PutAsJsonAsync("/api/settings/lists", new { listLabels });

    private static async Task<Guid> WorkOrderAsync(HttpClient c, Guid site, string priority = "Urgent", string type = "Corrective", string title = "Leak") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/work-orders", new { title, siteId = site, type, priority })));

    [SkippableFact]
    public async Task List_labels_save_and_show_in_reports_and_exports()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await Ok(await PutListsAsync(o.Admin, new { priority = new { Urgent = new { name = "Emergency", nameAr = "طارئ", color = "red", order = 0 } } }));
        await WorkOrderAsync(o.Admin, o.Site);
        var backlog = await Ok(await o.Admin.GetAsync("/api/reports/wo-backlog"));
        Assert.Contains(backlog.GetProperty("rows").EnumerateArray(), r => r.GetProperty("priority").GetString() == "Emergency");
        var csv = await (await o.Admin.GetAsync("/api/work-orders/export?format=csv&c=number&t=Number&c=priority&t=Priority")).Content.ReadAsStringAsync();
        Assert.Contains("Emergency", csv);
        var arabic = new HttpRequestMessage(HttpMethod.Get, "/api/reports/wo-backlog");
        arabic.Headers.AcceptLanguage.ParseAdd("ar");
        var ar = await Ok(await o.Admin.SendAsync(arabic));
        Assert.Contains(ar.GetProperty("rows").EnumerateArray(), r => r.GetProperty("priority").GetString() == "طارئ");
        var settings = await Ok(await o.Admin.GetAsync("/api/settings"));
        Assert.Equal("red", settings.GetProperty("settings").GetProperty("listLabels").GetProperty("priority").GetProperty("Urgent").GetProperty("color").GetString());
    }

    [SkippableFact]
    public async Task List_names_cannot_clash_and_import_reads_both()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var clash = await PutListsAsync(o.Admin, new { priority = new { High = new { name = "Urgent" } } });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Contains("This name is already used by Urgent.", await clash.Content.ReadAsStringAsync());
        await Ok(await PutListsAsync(o.Admin, new { priority = new { Urgent = new { name = "Emergency" } } }));
        var task = await Pm12.TemplateAsync(o.Admin, "CHK");
        var pump = await Pm12.AssetAsync(o.Admin, o.Site, "Pump");
        var pm = Id(await Pm12.SaveScheduleAsync(o.Admin, Pm12.Schedule(o.Site, task, DateOnly.FromDateTime(DateTime.UtcNow), pump, extra: new() { ["code"] = "PM1" })));
        foreach (var word in new[] { "Emergency", "urgent" })
        {
            await RunAsync(o.Admin, Book(new Sheet("PM schedules", ["Code", "Priority"], ["PM1", word])));
            Assert.Equal("Urgent", (await Ok(await o.Admin.GetAsync($"/api/pm-schedules/{pm}"))).GetProperty("priority").GetString());
            await RunAsync(o.Admin, Book(new Sheet("PM schedules", ["Code", "Priority"], ["PM1", "Low"])));
        }
    }

    /// <summary>0.29.2 (0.21.0-12): an Arabic reader is told the clashing value by the name they see for it; English is told as before.</summary>
    [SkippableFact]
    public async Task Clash_in_arabic_names_the_value_as_the_reader_sees_it()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var labels = new { priority = new { Urgent = new { name = "Emergency", nameAr = "طارئ" }, High = new { name = "Emergency" } } };
        var ar = await BilingualEditTests.SendAsync(o.Admin, HttpMethod.Put, "/api/settings/lists", "ar", new { listLabels = labels });
        Assert.Equal(HttpStatusCode.BadRequest, ar.StatusCode);
        var body = await ar.Content.ReadAsStringAsync();
        Assert.Contains("طارئ", body);
        Assert.DoesNotContain("Urgent", body);
        Assert.DoesNotContain("عاجلة", body); // the built-in word, which this organization's Arabic readers never see
        var en = await PutListsAsync(o.Admin, labels);
        Assert.Contains("This name is already used by Urgent.", await en.Content.ReadAsStringAsync());
    }

    /// <summary>0.29.2 (0.21.0-12): when no free number is found, the message names the kind of document, never the internal sequence key.</summary>
    [Fact]
    public void Numbering_failure_names_the_kind_of_document()
    {
        var org = new TenantSettings { RestartNumbersYearly = true };
        var site = new Site { Code = "CAI", Name = "Cairo" };
        site.Settings.WorkOrderNumberPrefix = site.Settings.WorkRequestNumberPrefix = site.Settings.PurchaseOrderNumberPrefix = site.Settings.PurchaseRequestNumberPrefix = "CAI-";
        var sequences = Enum.GetValues<NumberKind>().Select(k => Numbering.For(org, site, k).Sequence).Append("PmSchedule").ToList();
        Assert.Contains(sequences, s => s.Count(c => c == ':') == 2); // a site's own yearly sequence: "WorkOrder:<site>:2026"
        foreach (var sequence in sequences)
        {
            var english = Numbering.NoNumberMessage(sequence);
            Assert.DoesNotContain(":", english);
            Assert.DoesNotContain(sequence.Split(':')[0], english);
            var arabic = Translator.T(english, "ar");
            Assert.DoesNotContain(":", arabic);
            Assert.DoesNotMatch("[A-Za-z]", arabic);
        }
        Assert.Equal("Could not generate a unique work order number. Check the numbering settings.", Numbering.NoNumberMessage("WorkOrder:2026"));
    }

    [SkippableFact]
    public async Task Hidden_value_stays_on_existing_records()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var wo = await WorkOrderAsync(o.Admin, o.Site, priority: "Medium", type: "Project", title: "Old project");
        await Ok(await PutListsAsync(o.Admin, new { workType = new { Project = new { hidden = true } } }));
        var w = await Ok(await o.Admin.GetAsync($"/api/work-orders/{wo}"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/work-orders/{wo}", new
        {
            title = "Old project, renamed", siteId = o.Site, type = "Project", priority = "Medium", rowVersion = w.GetProperty("rowVersion").GetString(),
        }));
        var found = (await Ok(await o.Admin.GetAsync("/api/work-orders?type=Project&pageSize=50"))).GetProperty("items").EnumerateArray().Select(Id).ToList();
        Assert.Contains(wo, found);
    }

    [SkippableFact]
    public async Task Every_list_keeps_a_visible_value()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        async Task<string> Refused(object labels)
        {
            var r = await PutListsAsync(o.Admin, labels);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return await r.Content.ReadAsStringAsync();
        }
        Assert.Contains("Leave at least one priority showing.", await Refused(new
        {
            priority = new { Low = new { hidden = true }, Medium = new { hidden = true }, High = new { hidden = true }, Urgent = new { hidden = true } },
        }));
        Assert.Contains("Choose a colour from the list.", await Refused(new { criticality = new { High = new { color = "purple" } } }));
        Assert.Contains("A name can be at most 50 characters.", await Refused(new { assetStatus = new { Active = new { name = new string('x', 51) } } }));
        var (reader, _, _) = await UserAsync(fx, o.Admin, "Reader", ["workorders.view"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutListsAsync(reader, new { })).StatusCode);
    }

    /// <summary>Review fix: the asset register summary names statuses as the organization does.</summary>
    [SkippableFact]
    public async Task Asset_register_uses_the_organizations_status_names()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await Ok(await PutListsAsync(o.Admin, new { assetStatus = new { UnderRepair = new { name = "Out of service" } } }));
        var report = await Ok(await o.Admin.GetAsync("/api/reports/assets-by-category"));
        Assert.Contains(report.GetProperty("columns").EnumerateArray(), c => c.GetProperty("label").GetString() == "Out of service");
        Assert.DoesNotContain(report.GetProperty("columns").EnumerateArray(), c => c.GetProperty("label").GetString() == "Under repair");
    }
}

