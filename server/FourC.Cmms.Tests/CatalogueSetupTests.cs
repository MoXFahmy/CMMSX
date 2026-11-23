using System.Net.Http.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: order lines carry a pack; lines made without one are packs of one in the part's unit.</summary>
public class CatalogueSetupTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [SkippableFact]
    public async Task Order_lines_without_a_pack_are_packs_of_one()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier,
            lines = new[] { new { partId = o.Part, description = "Bearing", unitOfMeasure = "EA", quantity = 4m, unitPrice = 10m, discountPercent = 0m, storeId = o.Store } },
        })));
        var line = (await Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines")[0];
        Assert.Equal(1m, line.GetProperty("packSize").GetDecimal());
        Assert.Equal("EA", line.GetProperty("unitOfMeasure").GetString());
    }
}
