using System.Net;
using System.Text.Json;
using static FourC.Cmms.Tests.ApiKeyTests;
using static FourC.Cmms.Tests.Loop;
using static FourC.Cmms.Tests.ServiceAccountTests;

namespace FourC.Cmms.Tests;

/// <summary>The API description (0.26.0): readable when signed in or with a key, and it declares the key scheme.</summary>
public class ApiDocsTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Docs_need_sign_in_or_a_key()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Client().GetAsync("/openapi/v1.json")).StatusCode);
        var org = await OrgAsync(fx, emailOn: false);
        await Ok(await org.Admin.GetAsync("/openapi/v1.json"));
        var account = await ServiceAccountAsync(org.Admin, "Docs reader", "Viewer");
        var doc = await Ok(await KeyClient(fx, (await KeyAsync(org.Admin, account)).Key).GetAsync("/openapi/v1.json"));
        var scheme = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal(("http", "bearer"), (scheme.GetProperty("type").GetString(), scheme.GetProperty("scheme").GetString()));
    }
}
