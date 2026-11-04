using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using FourC.Cmms.Core.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>Behaviour that matters once the app runs in the cloud: restarts, file storage, closed sign-up.</summary>
public class HostingTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static object Org(string name) => new { organizationName = name, fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" };

    [SkippableFact]
    public async Task Sign_in_survives_an_app_restart()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var first = fx.Factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var reg = await first.PostAsJsonAsync("/api/auth/register", Org("Restart Co"));
        await Ok(reg);
        var cookie = reg.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("cmms.auth=")).Split(';')[0];

        // The key ring lives in the database, not on the container's own (throw-away) disk.
        using (var scope = fx.Factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<CmmsDbContext>().DataProtectionKeys.AnyAsync());

        // A second host has its own in-memory state, like a new container after a redeployment.
        await using var restarted = fx.Factory.WithWebHostBuilder(_ => { });
        var second = restarted.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await second.SendAsync(me)).StatusCode);
    }

    [SkippableFact]
    public async Task Registration_can_be_switched_off()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Assert.True((await Ok(await fx.Client().GetAsync("/api/auth/options"))).GetProperty("allowRegistration").GetBoolean());

        await using var closed = fx.Factory!.WithWebHostBuilder(b => b.UseSetting("Auth:AllowRegistration", "false"));
        var c = closed.CreateClient();
        Assert.False((await Ok(await c.GetAsync("/api/auth/options"))).GetProperty("allowRegistration").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/register", Org("Blocked Co"))).StatusCode);
    }

    [SkippableFact]
    public async Task Sample_data_loads_next_to_records_the_organization_already_has()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/register", Org("Own Data Co")));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Own site", isActive = true })));
        // An asset with no category or location, as people often create first.
        await Ok(await c.PostAsJsonAsync("/api/assets", new { name = "Pump", siteId = site, status = "Active", criticality = "High", warrantyType = "None" }));

        await Ok(await c.PostAsync("/api/settings/sample-data", null));
        Assert.True((await Ok(await c.GetAsync("/api/work-orders?pageSize=1"))).GetProperty("total").GetInt32() > 100);
    }

    [SkippableFact]
    public async Task Files_attach_to_parts_suppliers_and_purchase_orders()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await AttachmentRoundTripAsync(fx.Client());
    }

    /// <summary>Runs against Azurite (or a real account) when CMMS_TEST_BLOB holds a storage connection string.</summary>
    [SkippableFact]
    public async Task Files_round_trip_through_azure_blob_storage()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var blob = Environment.GetEnvironmentVariable("CMMS_TEST_BLOB");
        Skip.If(string.IsNullOrWhiteSpace(blob), "Set CMMS_TEST_BLOB to test Azure Blob storage.");
        await using var azure = fx.Factory!.WithWebHostBuilder(b =>
        {
            b.UseSetting("Storage:Provider", "AzureBlob");
            b.UseSetting("Storage:ConnectionString", blob);
            b.UseSetting("Storage:Container", $"test-{Guid.NewGuid():N}");
        });
        await AttachmentRoundTripAsync(azure.CreateClient());
    }

    private static async Task AttachmentRoundTripAsync(HttpClient c)
    {
        await Ok(await c.PostAsJsonAsync("/api/auth/register", Org("Files Co")));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var group = Id(await Ok(await c.PostAsJsonAsync("/api/part-groups", new { code = "G", name = "Group" })));
        var part = Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "P-1", name = "Bearing", groupId = group, unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = Array.Empty<object>(),
        })));
        var supplier = Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = "SUP", name = "Supplier", isContractor = false })));
        var po = Id(await Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = site, supplierId = supplier, lines = new[] { new { description = "Service", quantity = 1, unitPrice = 50, discountPercent = 0 } },
        })));

        foreach (var (type, id) in new[] { ("Part", part), ("Supplier", supplier), ("PurchaseOrder", po) })
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"datasheet for {type}");
            using var form = new MultipartFormDataContent
            {
                { new StringContent(type), "entityType" },
                { new StringContent(id.ToString()), "entityId" },
            };
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "files", "datasheet.txt");
            var saved = await Ok(await c.PostAsync("/api/attachments", form));
            var attachment = saved[0].GetProperty("id").GetGuid();

            var list = await Ok(await c.GetAsync($"/api/attachments?entityType={type}&entityId={id}"));
            Assert.Equal("datasheet.txt", list[0].GetProperty("fileName").GetString());
            Assert.Equal(bytes, await (await c.GetAsync($"/api/attachments/{attachment}/content")).Content.ReadAsByteArrayAsync());

            Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/attachments/{attachment}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/attachments/{attachment}/content")).StatusCode);
        }
    }
}
