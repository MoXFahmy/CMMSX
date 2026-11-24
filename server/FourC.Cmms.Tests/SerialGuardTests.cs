using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>Stock moves by quantity are refused on serial-tracked parts, with what to do instead (0.24.0).</summary>
public class SerialGuardTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task SerialAsync(Guid partId)
    {
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
        var part = await db.Parts.IgnoreQueryFilters().SingleAsync(p => p.Id == partId);
        part.IsSerialTracked = true;
        await db.SaveChangesAsync();
    }

    private static async Task Refused(HttpResponseMessage r, string message)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {body}");
        Assert.Contains(message, body);
    }

    [SkippableFact]
    public async Task Quantity_moves_are_refused_on_serial_parts()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var a = o.Admin;
        var store2 = Id(await Ok(await a.PostAsJsonAsync("/api/stores", new { code = "S2", name = "Second", siteId = o.Site })));
        var wo = Id(await Ok(await a.PostAsJsonAsync("/api/work-orders", new { title = "Leak", siteId = o.Site, type = "Corrective", priority = "Medium", tasks = Array.Empty<string>() })));
        // Some stock first, while the part is still counted by quantity.
        await Ok(await a.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 2, unitCost = 10 }));
        await SerialAsync(o.Part);

        await Refused(await a.PostAsJsonAsync("/api/inventory/receive", new { partId = o.Part, storeId = o.Store, quantity = 1, unitCost = 10 }),
            "Enter a serial number for each piece received.");
        await Refused(await a.PostAsJsonAsync("/api/inventory/issue", new { partId = o.Part, storeId = o.Store, quantity = 1, workOrderId = wo }),
            "This part is serial-tracked: fit or remove a unit on the work order.");
        await Refused(await a.PostAsJsonAsync("/api/inventory/return", new { partId = o.Part, storeId = o.Store, quantity = 1, workOrderId = wo }),
            "This part is serial-tracked: fit or remove a unit on the work order.");
        await Refused(await a.PostAsJsonAsync("/api/inventory/adjust", new { partId = o.Part, storeId = o.Store, quantity = -1, notes = "Lost" }),
            "This part is serial-tracked: scrap a unit or register a found one on the part's units.");
        await Refused(await a.PostAsJsonAsync("/api/inventory/transfer", new { partId = o.Part, fromStoreId = o.Store, toStoreId = store2, quantity = 1 }),
            "This part is serial-tracked: transfer units by choosing them on the part's units.");

        // A normal part still moves as before.
        var other = Id(await Ok(await a.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "FLT-1", name = "Filter", unitOfMeasure = "EA", standardPrice = 5, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            levels = new[] { new { storeId = o.Store, bin = "B1", minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));
        await Ok(await a.PostAsJsonAsync("/api/inventory/receive", new { partId = other, storeId = o.Store, quantity = 3, unitCost = 5 }));
        await Ok(await a.PostAsJsonAsync("/api/inventory/issue", new { partId = other, storeId = o.Store, quantity = 1, workOrderId = wo }));
        await Ok(await a.PostAsJsonAsync("/api/inventory/adjust", new { partId = other, storeId = o.Store, quantity = -1, notes = "Broken" }));
    }
}
