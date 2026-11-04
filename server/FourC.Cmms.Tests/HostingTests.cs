using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using FourC.Cmms.Core.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    /// <summary>Runs against an S3-compatible server (MinIO, moto) when CMMS_TEST_S3 holds its URL.</summary>
    [SkippableFact]
    public async Task Files_round_trip_through_amazon_s3()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var url = Environment.GetEnvironmentVariable("CMMS_TEST_S3");
        Skip.If(string.IsNullOrWhiteSpace(url), "Set CMMS_TEST_S3 to test S3 storage.");
        await using var aws = fx.Factory!.WithWebHostBuilder(b =>
        {
            b.UseSetting("Storage:Provider", "S3");
            b.UseSetting("Storage:Bucket", $"test-{Guid.NewGuid():N}");
            b.UseSetting("Storage:Region", "me-central-1"); // a region other than us-east-1, as most deployments use
            b.UseSetting("Storage:S3:ServiceUrl", url);
            b.UseSetting("Storage:S3:AccessKey", Environment.GetEnvironmentVariable("CMMS_TEST_S3_ACCESS_KEY") ?? "test");
            b.UseSetting("Storage:S3:SecretKey", Environment.GetEnvironmentVariable("CMMS_TEST_S3_SECRET_KEY") ?? "test");
        });
        await AttachmentRoundTripAsync(aws.CreateClient());
    }

    [SkippableFact]
    public async Task Https_is_recognised_behind_cloudfront()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        await using var behindCdn = fx.Factory!.WithWebHostBuilder(b =>
        {
            b.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
            b.UseSetting("ForwardedHeaders:ProtoHeaderName", "CloudFront-Forwarded-Proto");
        });
        var c = behindCdn.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var viaHttps = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register") { Content = JsonContent.Create(Org("Cdn Co")) };
        viaHttps.Headers.Add("CloudFront-Forwarded-Proto", "https");
        var r = await c.SendAsync(viaHttps);
        await Ok(r);
        Assert.Contains("secure", r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("cmms.auth=")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Database_login_can_be_supplied_separately_from_the_connection_string()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cmms"] = "Server=tcp:db.example.com,1433;Database=cmms;Encrypt=True",
            ["Database:User"] = "cmmsadmin",
            ["Database:Password"] = "p;w=d\"x",
        }).Build();
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(FourC.Cmms.Api.Infrastructure.DatabaseConnection.Build(config));
        Assert.Equal(("tcp:db.example.com,1433", "cmms", "cmmsadmin", "p;w=d\"x"), (cs.DataSource, cs.InitialCatalog, cs.UserID, cs.Password));

        var plain = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cmms"] = "Server=sql;Database=Cmms;User Id=sa;Password=x",
        }).Build();
        Assert.Equal("Server=sql;Database=Cmms;User Id=sa;Password=x", FourC.Cmms.Api.Infrastructure.DatabaseConnection.Build(plain));
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
