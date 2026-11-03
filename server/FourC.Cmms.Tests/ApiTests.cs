using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace FourC.Cmms.Tests;

/// <summary>Spins up the real API against a throwaway SQL Server database (CMMS_TEST_SQL = server connection string).</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program>? Factory { get; private set; }
    private string? _db;
    private string? _server;
    public bool Enabled => Factory is not null;

    public Task InitializeAsync()
    {
        _server = Environment.GetEnvironmentVariable("CMMS_TEST_SQL");
        if (string.IsNullOrWhiteSpace(_server)) return Task.CompletedTask;
        _db = $"CmmsTest_{Guid.NewGuid():N}";
        var cs = new SqlConnectionStringBuilder(_server) { InitialCatalog = _db }.ConnectionString;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Cmms", cs);
            b.UseSetting("Storage:Path", Path.Combine(Path.GetTempPath(), _db));
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Factory is null) return;
        await Factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var c = new SqlConnection(_server);
        await c.OpenAsync();
        await using var cmd = new SqlCommand($"ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}]", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient Client() => Factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}

public class ApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> NewTenantAsync(string org)
    {
        var c = fx.Client();
        var r = await c.PostAsJsonAsync("/api/auth/register", new { organizationName = org, fullName = "Admin", email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!x" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    private static async Task<JsonElement> Json200(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<Guid> CreateSiteAsync(HttpClient c, string code) =>
        (await Json200(await c.PostAsJsonAsync("/api/sites", new { code, name = $"Site {code}", isActive = true }))).GetProperty("id").GetGuid();

    private static async Task<Guid> CreateLocationAsync(HttpClient c, Guid site, Guid? parent, string code) =>
        (await Json200(await c.PostAsJsonAsync("/api/locations", new { siteId = site, parentId = parent, code, name = code, type = "Room", criticality = "Medium", isActive = true }))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreateAssetAsync(HttpClient c, Guid site, Guid? loc, string? number = null) =>
        await Json200(await c.PostAsJsonAsync("/api/assets", new { assetNumber = number, name = "Pump", siteId = site, locationId = loc, status = "Active", criticality = "High", warrantyType = "None" }));

    [SkippableFact]
    public async Task Tenants_cannot_see_each_others_data()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var a = await NewTenantAsync("Tenant A");
        var b = await NewTenantAsync("Tenant B");
        var site = await CreateSiteAsync(a, "A1");
        var asset = await CreateAssetAsync(a, site, null);
        var id = asset.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/assets/{id}")).StatusCode);
        Assert.Equal(0, (await Json200(await b.GetAsync("/api/assets"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/sites/{site}", new { code = "X", name = "Hijack", isActive = true })).StatusCode);
        // Same codes are allowed in different tenants.
        await CreateSiteAsync(b, "A1");
    }

    [SkippableFact]
    public async Task Anonymous_and_unprivileged_requests_are_rejected()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Client().GetAsync("/api/assets")).StatusCode);
        var admin = await NewTenantAsync("Perm Co");
        var roles = await Json200(await admin.GetAsync("/api/roles"));
        var viewer = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Viewer").GetProperty("id").GetGuid();
        var email = $"{Guid.NewGuid():N}@test.local";
        await Json200(await admin.PostAsJsonAsync("/api/users", new { fullName = "Viewer", email, isActive = true, roleIds = new[] { viewer }, password = "Passw0rd!v" }));
        var v = fx.Client();
        await Json200(await v.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!v", rememberMe = false }));
        Assert.Equal(HttpStatusCode.OK, (await v.GetAsync("/api/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await v.PostAsJsonAsync("/api/sites", new { code = "V", name = "V" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await v.GetAsync("/api/users")).StatusCode);
    }

    [SkippableFact]
    public async Task Moving_a_location_rewrites_the_subtree_and_blocks_cycles()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewTenantAsync("Tree Co");
        var site = await CreateSiteAsync(c, "S");
        var building = await CreateLocationAsync(c, site, null, "B1");
        var floor = await CreateLocationAsync(c, site, building, "F1");
        var room = await CreateLocationAsync(c, site, floor, "R1");
        var other = await CreateLocationAsync(c, site, null, "B2");
        await CreateAssetAsync(c, site, room);

        var b = await Json200(await c.GetAsync($"/api/locations/{building}"));
        var cycle = await c.PutAsJsonAsync($"/api/locations/{building}", new { siteId = site, parentId = room, code = "B1", name = "B1", type = "Building", criticality = "Medium", isActive = true, rowVersion = b.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        var f = await Json200(await c.GetAsync($"/api/locations/{floor}"));
        await Json200(await c.PutAsJsonAsync($"/api/locations/{floor}", new { siteId = site, parentId = other, code = "F1", name = "F1", type = "Floor", criticality = "Medium", isActive = true, rowVersion = f.GetProperty("rowVersion").GetString() }));
        var r = await Json200(await c.GetAsync($"/api/locations/{room}"));
        Assert.Equal(["B2", "F1"], r.GetProperty("ancestors").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToArray());
        Assert.Equal(1, (await Json200(await c.GetAsync($"/api/locations/{other}"))).GetProperty("subtreeAssetCount").GetInt32());
        Assert.Equal(0, (await Json200(await c.GetAsync($"/api/locations/{building}"))).GetProperty("subtreeAssetCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/locations/{other}")).StatusCode);
    }

    [SkippableFact]
    public async Task Asset_numbers_skip_past_manual_numbers_and_edits_detect_conflicts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewTenantAsync("Numbers Co");
        var site = await CreateSiteAsync(c, "N");
        await CreateAssetAsync(c, site, null, "AST-00001");
        await CreateAssetAsync(c, site, null, "AST-00002");
        var auto = await CreateAssetAsync(c, site, null);
        Assert.Equal("AST-00003", auto.GetProperty("assetNumber").GetString());

        var id = auto.GetProperty("id").GetGuid();
        var detail = await Json200(await c.GetAsync($"/api/assets/{id}"));
        var version = detail.GetProperty("rowVersion").GetString();
        object Body(string name) => new { assetNumber = "AST-00003", name, siteId = site, status = "Active", criticality = "Low", warrantyType = "None", rowVersion = version };
        await Json200(await c.PutAsJsonAsync($"/api/assets/{id}", Body("First edit")));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/assets/{id}", Body("Stale edit"))).StatusCode);

        var history = await Json200(await c.GetAsync($"/api/audit?entityType=Asset&entityId={id}"));
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("action").GetString() == "Updated" && h.GetProperty("changes").GetString()!.Contains("First edit"));
    }
}
