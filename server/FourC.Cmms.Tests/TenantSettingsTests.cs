using System.Text.Json;
using System.Text.Json.Serialization;
using FourC.Cmms.Core.Tenancy;

namespace FourC.Cmms.Tests;

public class TenantSettingsTests
{
    // The same options CmmsDbContext uses to store TenantSettings as JSON.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Settings_saved_before_0_0_2_read_as_off()
    {
        var s = JsonSerializer.Deserialize<TenantSettings>("""{"currency":"EUR"}""", Json)!;
        Assert.Equal("EUR", s.Currency);
        Assert.Empty(s.PmSkipAssetStatuses);
        Assert.Empty(s.PmNonWorkingDays);
        Assert.False(s.PmNoStacking);
    }
}
