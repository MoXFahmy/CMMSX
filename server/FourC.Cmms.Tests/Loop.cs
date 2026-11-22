using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FourC.Cmms.Tests;

/// <summary>Shared helpers for the 0.10.0 tests (requests that close the loop).</summary>
public static class Loop
{
    public const string Password = "Passw0rd!u";

    public static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts a 400 and returns its body text (for message checks).</summary>
    public static async Task<string> Bad(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True((int)r.StatusCode == 400, $"expected 400, got {(int)r.StatusCode}: {body}");
        return body;
    }

    public static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    public static string Addr(string who = "x") => $"{who}-{Guid.NewGuid():N}@cmms.test";
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public sealed record Org(HttpClient Admin, Guid Tenant, Guid AdminId, string AdminEmail, Guid Site, Guid Location);

    /// <summary>A new organization with its admin signed in, one site with one location; e-mail on unless <paramref name="emailOn"/> is false.</summary>
    public static async Task<Org> OrgAsync(ApiFixture fx, bool emailOn = true)
    {
        var c = fx.Client();
        var email = Addr("admin");
        await Ok(await c.PostAsJsonAsync("/api/auth/register", new { organizationName = "Loop Co", fullName = "Admin", email, password = "Passw0rd!x" }));
        var me = await Ok(await c.GetAsync("/api/auth/me"));
        var site = Id(await Ok(await c.PostAsJsonAsync("/api/sites", new { code = "S1", name = "Site", isActive = true })));
        var loc = Id(await Ok(await c.PostAsJsonAsync("/api/locations", new { siteId = site, code = "L1", name = "Hall", type = "Building", criticality = "Medium", isActive = true })));
        if (emailOn) await TestSettings.SetSettingsAsync(c, s => s["emailEnabled"] = true);
        return new Org(c, me.GetProperty("tenant").GetProperty("id").GetGuid(), me.GetProperty("user").GetProperty("id").GetGuid(), email, site, loc);
    }

    public sealed record User(HttpClient Client, Guid Id, string Email);

    /// <summary>A signed-in user with one of the default roles, limited to <paramref name="siteIds"/> when given.</summary>
    public static async Task<User> UserAsync(ApiFixture fx, HttpClient admin, string role, Guid[]? siteIds = null, string? name = null)
    {
        var roles = await Ok(await admin.GetAsync("/api/roles"));
        var roleId = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == role).GetProperty("id").GetGuid();
        var email = Addr(role.ToLowerInvariant().Replace(' ', '-'));
        var user = await Ok(await admin.PostAsJsonAsync("/api/users", new { fullName = name ?? role, email, isActive = true, roleIds = new[] { roleId }, password = Password, siteIds }));
        var c = fx.Client();
        await Ok(await c.PostAsJsonAsync("/api/auth/login", new { email, password = Password, rememberMe = false }));
        return new User(c, Id(user), email);
    }

    /// <summary>An employee (technician) with their own e-mail address.</summary>
    public static async Task<(Guid Id, string Email)> EmployeeAsync(HttpClient admin, string code, bool contractor = false, Guid? userId = null)
    {
        var email = Addr(code.ToLowerInvariant());
        var id = Id(await Ok(await admin.PostAsJsonAsync("/api/employees", new { code, fullName = $"Tech {code}", email, userId, isContractor = contractor, company = contractor ? "Outside Co" : null })));
        return (id, email);
    }

    public static async Task<List<OutboundEmail>> RowsAsync(ApiFixture fx, params string[] to)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        return await db.OutboundEmails.IgnoreQueryFilters().AsNoTracking().Where(e => to.Contains(e.ToAddress)).ToListAsync();
    }

    /// <summary>One of the organization's cancel reasons (cancelling work needs one since 0.10.0).</summary>
    public static async Task<Guid> AnyCancelReasonAsync(HttpClient c) => Id((await Ok(await c.GetAsync("/api/cancel-reasons"))).EnumerateArray().First());

    /// <summary>Runs <paramref name="work"/> on a database context that sees every organization (for checks and test setup).</summary>
    public static async Task<T> DbAsync<T>(ApiFixture fx, Func<CmmsDbContext, Task<T>> work)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<CmmsDbContext>());
    }
}
