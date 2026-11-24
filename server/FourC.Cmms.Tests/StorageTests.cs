using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FourC.Cmms.Core.Files;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Settings › Storage (0.25.0): space by kind and by record, and the largest files.</summary>
public class StorageTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task UploadAsync(HttpClient c, string type, Guid id, byte[] bytes, string contentType, string name)
    {
        using var form = new MultipartFormDataContent { { new StringContent(type), "entityType" }, { new StringContent(id.ToString()), "entityId" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "files", name);
        await Ok(await c.PostAsync("/api/attachments", form));
    }

    [SkippableFact]
    public async Task Totals_match_the_files()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var org = await OrgAsync(fx, emailOn: false);
        var asset = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/assets", new { name = "Boiler", siteId = org.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var number = (await Ok(await org.Admin.GetAsync($"/api/assets/{asset}"))).GetProperty("assetNumber").GetString()!;
        var wo = Id(await Ok(await org.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Fix", siteId = org.Site, type = "Corrective", priority = "Low" })));
        var pdf = "%PDF-1.4\n% boiler manual\n%%EOF"u8.ToArray();
        await UploadAsync(org.Admin, "Asset", asset, Png, "image/png", "front.png");
        await UploadAsync(org.Admin, "Asset", asset, [.. Png, .. Png], "image/png", "side.png");
        await UploadAsync(org.Admin, "Asset", asset, pdf, "application/pdf", "boiler-manual.pdf");
        await DbAsync(fx, async db =>
        {
            db.Attachments.Add(new Attachment { TenantId = org.Tenant, EntityType = "WorkOrder", EntityId = wo, FileName = "signature.png", ContentType = "image/png", Size = 500, StorageKey = $"{org.Tenant}/sig", IsSignature = true, SignedBy = "Customer" });
            return await db.SaveChangesAsync();
        });
        var expected = Png.Length * 3 + pdf.Length + 500;

        var s = await Ok(await org.Admin.GetAsync("/api/storage"));
        Assert.Equal((expected, 4), (s.GetProperty("totalBytes").GetInt64(), s.GetProperty("files").GetInt32()));
        var kinds = s.GetProperty("byKind").EnumerateArray().ToDictionary(k => k.GetProperty("kind").GetString()!, k => k.GetProperty("files").GetInt32());
        Assert.Equal((2, 1, 1), (kinds["Images"], kinds["Documents"], kinds["Signatures"]));
        var records = s.GetProperty("byRecord").EnumerateArray().ToDictionary(k => k.GetProperty("entityType").GetString()!, k => k.GetProperty("bytes").GetInt64());
        Assert.Equal(Png.Length * 3 + pdf.Length, records["Asset"]);
        Assert.Equal(0, s.GetProperty("deletedBytes").GetInt64());

        var found = Assert.Single((await Ok(await org.Admin.GetAsync("/api/storage/files?search=manual"))).GetProperty("items").EnumerateArray());
        Assert.Equal("boiler-manual.pdf", found.GetProperty("fileName").GetString());
        Assert.Contains(number, found.GetProperty("recordLabel").GetString());
        Assert.Equal("Admin", found.GetProperty("uploadedBy").GetString());
        var all = (await Ok(await org.Admin.GetAsync("/api/storage/files"))).GetProperty("items").EnumerateArray().Select(f => f.GetProperty("size").GetInt64()).ToList();
        Assert.Equal(all.OrderDescending(), all);
        // By the record's number, too.
        Assert.Equal(3, (await Ok(await org.Admin.GetAsync($"/api/storage/files?search={number}"))).GetProperty("items").GetArrayLength());

        // A deleted asset's files count under deleted records.
        Assert.True((await org.Admin.DeleteAsync($"/api/assets/{asset}")).IsSuccessStatusCode);
        Assert.Equal(Png.Length * 3 + pdf.Length, (await Ok(await org.Admin.GetAsync("/api/storage"))).GetProperty("deletedBytes").GetInt64());

        var tech = await UserAsync(fx, org.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Client.GetAsync("/api/storage")).StatusCode);
    }
}
