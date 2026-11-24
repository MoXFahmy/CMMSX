using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.BilingualEditTests;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>0.28.0: the phone shows names, titles, steps and notes in its language, and keeps the other text of places for its search.</summary>
public class BilingualPhoneTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [SkippableFact]
    public async Task Phone_jobs_show_names_steps_and_notes_in_the_phones_language()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        var me = await EmployeeAsync(o.Admin, "ME", userId: o.AdminId);
        var room = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new
        {
            siteId = o.Site, code = "PR", name = "Plant room", nameAr = "غرفة المعدات", type = "Room", criticality = "Medium", isActive = true,
            notesToTechnician = "Ear protection", notesToTechnicianAr = "واقي الأذن",
        })));
        var asset = await AssetAsync(o.Admin, o.Site, location: room, notes: "Isolate first", notesAr: "افصل أولاً");
        var task = await Pm12.TemplateAsync(o.Admin, "CHK", extra: new()
        {
            ["title"] = "Chiller check-up", ["titleAr"] = "فحص دوري للمبرد", ["safetyNotes"] = "Lock out the pump", ["safetyNotesAr"] = "اقفل المضخة",
            ["stepDetails"] = new[] { new { description = "Check coupling", descriptionAr = "افحص الوصلة", type = "Check" } },
        });
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Chiller check-up", siteId = o.Site, locationId = room, assetId = asset, type = "Preventive", priority = "Medium", templateId = task, assigneeIds = new[] { me.Id },
        })));

        var ar = (await GetAsync(o.Admin, "/api/tech/bundle", "ar")).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.Equal("فحص دوري للمبرد", S(ar, "title"));
        Assert.Equal(PumpAr, S(ar.GetProperty("asset"), "name"));
        Assert.Equal("افصل أولاً", S(ar.GetProperty("asset"), "notesToTechnician"));
        Assert.Equal("واقي الأذن", S(ar.GetProperty("location"), "notesToTechnician"));
        Assert.Contains("غرفة المعدات", S(ar.GetProperty("location"), "path"));
        Assert.Equal("افحص الوصلة", S(ar.GetProperty("tasks").EnumerateArray().Single(), "description"));
        Assert.Equal("اقفل المضخة", S(ar, "safetyNotes"));

        var en = (await GetAsync(o.Admin, "/api/tech/bundle", "en")).GetProperty("jobs").EnumerateArray().Single(j => Id(j) == wo);
        Assert.Equal("Chiller check-up", S(en, "title"));
        Assert.Equal(Pump, S(en.GetProperty("asset"), "name"));
        Assert.Equal("Check coupling", S(en.GetProperty("tasks").EnumerateArray().Single(), "description"));
        Assert.Equal("Lock out the pump", S(en, "safetyNotes"));
    }

    [SkippableFact]
    public async Task Phone_bundle_carries_the_other_text_for_search()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx, emailOn: false);
        await EmployeeAsync(o.Admin, "ME", userId: o.AdminId); // the phone's places are for a person linked to an employee
        var a = await AssetAsync(o.Admin, o.Site);
        var b = await AssetAsync(o.Admin, o.Site, name: "Boiler 1", nameAr: null);
        var room = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/locations", new
        {
            siteId = o.Site, code = "PR", name = "Plant room", nameAr = "غرفة المعدات", type = "Room", criticality = "Medium", isActive = true,
        })));

        var ar = await GetAsync(o.Admin, "/api/tech/places", "ar");
        var arA = ar.GetProperty("assets").EnumerateArray().Single(x => Id(x) == a);
        Assert.Equal(PumpAr, S(arA, "name"));
        Assert.Equal(Pump, S(arA, "otherName"));
        Assert.Null(S(ar.GetProperty("assets").EnumerateArray().Single(x => Id(x) == b), "otherName"));
        var arRoom = ar.GetProperty("locations").EnumerateArray().Single(x => Id(x) == room);
        Assert.Equal("غرفة المعدات", S(arRoom, "name"));
        Assert.Equal("Plant room", S(arRoom, "otherName"));

        var en = await GetAsync(o.Admin, "/api/tech/places", "en");
        var enA = en.GetProperty("assets").EnumerateArray().Single(x => Id(x) == a);
        Assert.Equal(Pump, S(enA, "name"));
        Assert.Equal(PumpAr, S(enA, "otherName"));
    }
}
