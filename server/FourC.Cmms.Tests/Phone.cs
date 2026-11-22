using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>Shared helpers for the 0.11.0 phone tests.</summary>
public static class Phone
{
    public static async Task<Guid> StoreAsync(HttpClient c, Guid site, string code = "ST1") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/stores", new { code, name = $"Store {code}", siteId = site })));

    public static async Task<Guid> PartAsync(HttpClient c, Guid store, string number = "P1", bool negative = false, string bin = "A1") =>
        Id(await Ok(await c.PostAsJsonAsync("/api/parts", new
        {
            partNumber = number, name = $"Part {number}", unitOfMeasure = "EA", standardPrice = 10, isCritical = false, isShelfItem = false, allowNegativeStock = negative,
            levels = new[] { new { storeId = store, bin, minLevel = 0, reorderLevel = 0, maxLevel = 0, reorderQuantity = 0 } },
        })));

    public static async Task ReceiveAsync(HttpClient c, Guid part, Guid store, decimal quantity) =>
        await Ok(await c.PostAsJsonAsync("/api/inventory/receive", new { partId = part, storeId = store, quantity, unitCost = 5 }));

    public static async Task<decimal> OnHandAsync(HttpClient c, Guid part, Guid store)
    {
        var p = await Ok(await c.GetAsync($"/api/parts/{part}"));
        return p.GetProperty("levels").EnumerateArray().Single(l => l.GetProperty("storeId").GetGuid() == store).GetProperty("quantityOnHand").GetDecimal();
    }

    /// <summary>Sends one phone change; returns the answer (status "done" or "rejected").</summary>
    public static async Task<JsonElement> ActAsync(HttpClient c, string type, object payload, Guid? workOrderId = null, Guid? clientId = null, DateTimeOffset? at = null) =>
        await Ok(await c.PostAsJsonAsync("/api/tech/actions", new { clientId = clientId ?? Guid.NewGuid(), at = at ?? DateTimeOffset.UtcNow, type, workOrderId, payload }));

    public static string Status(JsonElement answer) => answer.GetProperty("status").GetString()!;
    public static string? Message(JsonElement answer) => answer.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
}
