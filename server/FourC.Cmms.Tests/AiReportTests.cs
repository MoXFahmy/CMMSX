using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features.Ai;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>The AI report (0.15.0): the technician's notes become the completion report's fields, from what the organization has.</summary>
public class AiReportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    public sealed record Job(Loop.Org Org, Guid Wo, Guid P1, Guid P2, string P1No, string P2No, Guid Symptom, Guid Cause, Guid Remedy, string TechEmail);

    public static async Task<Guid> CodeAsync(HttpClient c, string path, string code, string name, bool active = true) =>
        Id(await Ok(await c.PostAsJsonAsync(path, new { code, name, isActive = active })));

    public static async Task<Guid> PartAsync(HttpClient c, string no, string name) => Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
    {
        partNumber = no, name, unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false, levels = Array.Empty<object>(),
    })));

    /// <summary>An organization with AI on and a work order on a categorised pump: one planned part, one spare part on the asset, codes and a technician.</summary>
    public async Task<Job> JobAsync()
    {
        fx.Ai.Reset();
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await AiSettingsTests.PutSettingsAsync(org.Admin, s => s["aiEnabled"] = true));
        var category = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/asset-categories", new { code = "PMPX", name = "Pumps", isActive = true })));
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new
        {
            name = "Pump 1", siteId = org.Site, categoryId = category, manufacturer = "Grundfos", model = "CR5", status = "Active", criticality = "High", warrantyType = "None",
        })));
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (p1No, p2No) = ($"SEAL-{tag}", $"BRG-{tag}");
        var p1 = await PartAsync(org.Admin, p1No, "Mechanical seal");
        var p2 = await PartAsync(org.Admin, p2No, "Bearing");
        await Ok(await org.Admin.PutAsJsonAsync($"/api/assets/{asset}/parts", new[] { new { partId = p2, quantity = 1m, note = (string?)null } }));
        var tech = await EmployeeAsync(org.Admin, $"T{tag}");
        var symptom = await CodeAsync(org.Admin, "/api/symptoms", "SYX", "Leaking");
        var cause = await CodeAsync(org.Admin, "/api/failure-codes", "CXA", "Worn seal");
        var remedy = await CodeAsync(org.Admin, "/api/remedies", "RMX", "Replaced");
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new
        {
            title = "Pump leaking", description = "Water under the pump", type = "Corrective", priority = "High", siteId = org.Site, assetId = asset, assigneeIds = new[] { tech.Id },
        })));
        await Ok(await org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/planned-parts", new { partId = p1, quantity = 2m }));
        return new Job(org, wo, p1, p2, p1No, p2No, symptom, cause, remedy, tech.Email);
    }

    public static Task<HttpResponseMessage> ReportAsync(HttpClient c, Guid wo, string notes) =>
        c.PostAsJsonAsync("/api/ai/report", new { workOrderId = wo, notes });

    private void Answer(object json) => fx.Ai.Responder = _ => Task.FromResult(FakeAiClient.Answer(JsonSerializer.Serialize(json)));

    [SkippableFact]
    public async Task Report_fills_fields_from_the_answer()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        Answer(new
        {
            workDone = "Replaced the seal", causeDetails = "Seal worn", symptomCode = "SYX", causeCode = "CXA", remedyCode = "RMX", hours = 1.5,
            parts = new[] { new { partNumber = j.P1No, quantity = 2m }, new { partNumber = j.P2No, quantity = 1m } },
        });
        var r = await Ok(await ReportAsync(j.Org.Admin, j.Wo, "changed the seal, bearing too, took an hour and a half"));
        Assert.Equal("Replaced the seal", r.GetProperty("workDone").GetString());
        Assert.Equal("Seal worn", r.GetProperty("causeDetails").GetString());
        Assert.Equal((j.Symptom, "SYX"), (r.GetProperty("symptom").GetProperty("id").GetGuid(), r.GetProperty("symptom").GetProperty("code").GetString()));
        Assert.Equal(j.Cause, r.GetProperty("cause").GetProperty("id").GetGuid());
        Assert.Equal(j.Remedy, r.GetProperty("remedy").GetProperty("id").GetGuid());
        Assert.Equal(1.5m, r.GetProperty("hours").GetDecimal());
        var parts = r.GetProperty("parts").EnumerateArray().ToList();
        Assert.Equal([(j.P1, "Mechanical seal", 2m), (j.P2, "Bearing", 1m)],
            parts.Select(p => (p.GetProperty("partId").GetGuid(), p.GetProperty("name").GetString(), p.GetProperty("quantity").GetDecimal())));
        var call = Assert.Single(await DbAsync(fx, db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.AiCalls).Where(c => c.TenantId == j.Org.Tenant))));
        Assert.Equal((FourC.Cmms.Core.Ai.AiFeature.Report, FourC.Cmms.Core.Ai.AiResult.Answered, (Guid?)j.Wo), (call.Feature, call.Result, call.WorkOrderId));
        Assert.True(call.EstimatedCost > 0);
        Assert.Equal("Admin", call.UserName);
    }

    [SkippableFact]
    public async Task Report_drops_codes_and_parts_not_offered()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        await CodeAsync(j.Org.Admin, "/api/failure-codes", "CXI", "Old cause", active: false);
        var other = await OrgAsync(fx, emailOn: false);
        await CodeAsync(other.Admin, "/api/remedies", "RMO", "Elsewhere");
        Answer(new
        {
            workDone = "Done", causeDetails = (string?)null, symptomCode = "NOPE", causeCode = "CXI", remedyCode = "RMO", hours = 30,
            parts = new[] { new { partNumber = "NOT-A-PART", quantity = 1m }, new { partNumber = j.P1No, quantity = 0m }, new { partNumber = j.P2No, quantity = 1m }, new { partNumber = j.P2No, quantity = 5m } },
        });
        var r = await Ok(await ReportAsync(j.Org.Admin, j.Wo, "did it"));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("symptom").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("cause").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("remedy").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("hours").ValueKind);
        var part = Assert.Single(r.GetProperty("parts").EnumerateArray());
        Assert.Equal((j.P2, 1m), (part.GetProperty("partId").GetGuid(), part.GetProperty("quantity").GetDecimal()));
    }

    [SkippableFact]
    public async Task Report_prompt_lists_offered_codes_and_parts_and_no_people()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        await CodeAsync(j.Org.Admin, "/api/failure-codes", "CXI", "Old cause", active: false);
        Answer(new { workDone = "x", causeDetails = (string?)null, symptomCode = (string?)null, causeCode = (string?)null, remedyCode = (string?)null, hours = (decimal?)null, parts = Array.Empty<object>() });
        await Ok(await ReportAsync(j.Org.Admin, j.Wo, "seal was leaking, replaced it"));
        var ask = Assert.Single(fx.Ai.Asks);
        Assert.Equal(FourC.Cmms.Core.Ai.AiFeature.Report, ask.Feature);
        Assert.Equal(AiEffort.Low, ask.Effort);
        Assert.Contains("SYX — Leaking", ask.SharedContext);
        Assert.Contains("CXA — Worn seal", ask.SharedContext);
        Assert.Contains("RMX — Replaced", ask.SharedContext);
        Assert.DoesNotContain("CXI", ask.SharedContext);
        Assert.Contains(j.P1No, ask.Prompt);
        Assert.Contains(j.P2No, ask.Prompt);
        Assert.Contains("seal was leaking, replaced it", ask.Prompt);
        Assert.Contains("Grundfos", ask.Prompt);
        Assert.DoesNotContain("Tech T", ask.Prompt);
        Assert.DoesNotContain(j.TechEmail, ask.Prompt);
        Assert.DoesNotContain(j.Org.AdminEmail, ask.Prompt);
    }

    [SkippableFact]
    public async Task Report_validates_notes()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        Assert.Contains("Type or dictate some notes first.", await Bad(await ReportAsync(j.Org.Admin, j.Wo, "  ")));
        Assert.Contains("Notes can be at most 8,000 characters.", await Bad(await ReportAsync(j.Org.Admin, j.Wo, new string('a', 8001))));
        Assert.Empty(fx.Ai.Asks);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(j.Org.Admin, Guid.NewGuid(), "notes")).StatusCode);
    }

    [SkippableFact]
    public async Task Report_refusal_counts_and_says_so()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        fx.Ai.Responder = _ => Task.FromResult(new AiReply(true, null, "claude-opus-5-5", 100, 5, 0, 0));
        var r = await ReportAsync(j.Org.Admin, j.Wo, "notes");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("The AI assistant couldn't help with these notes.", await r.Content.ReadAsStringAsync());
        Assert.Equal(1, (await AiSettingsTests.StatusAsync(j.Org.Admin)).GetProperty("used").GetInt32());
    }

    [SkippableFact]
    public async Task Completion_sets_symptom_when_given()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var j = await JobAsync();
        await Ok(await j.Org.Admin.PostAsJsonAsync($"/api/work-orders/{j.Wo}/status", new { status = "Completed", completionNotes = "Done", symptomId = j.Symptom }));
        Assert.Equal(j.Symptom, (await Ok(await j.Org.Admin.GetAsync($"/api/work-orders/{j.Wo}"))).GetProperty("symptomId").GetGuid());

        var other = await CodeAsync(j.Org.Admin, "/api/symptoms", "SYO", "Noisy");
        var wo = Id(await Ok(await j.Org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Noise", type = "Corrective", priority = "Low", siteId = j.Org.Site, symptomId = other })));
        await Ok(await j.Org.Admin.PostAsJsonAsync($"/api/work-orders/{wo}/status", new { status = "Completed", completionNotes = "Done" }));
        Assert.Equal(other, (await Ok(await j.Org.Admin.GetAsync($"/api/work-orders/{wo}"))).GetProperty("symptomId").GetGuid());
    }
}
