using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.Loop;

namespace FourC.Cmms.Tests;

/// <summary>
/// 0.21.0-6 (0.29.1): saves that used to happen in two steps are one transaction now. Each test makes the second step fail on
/// purpose (<see cref="FailureInjection"/>) and checks that the first one is not left behind.
/// </summary>
public class SavesAreAllOrNothingTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<HttpResponseMessage> Failed(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.False(r.IsSuccessStatusCode, "The save was meant to fail.");
        return r;
    }

    // ---------------- purchase orders ----------------

    /// <summary>An approved request of a part line and a free-text line, both on one draft order. Returns (request, order).</summary>
    private async Task<(Guid Request, Guid Order, PurchaseRequestTests.Org Org)> OrderedAsync()
    {
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var (tech, _, _) = await PurchaseRequestTests.UserAsync(fx, o.Admin, "Technician");
        var req = await PurchaseRequestTests.RequestAsync(tech, o, PurchaseRequestTests.PartLine(o, 3), new { description = "Crane hire", quantity = 1 });
        await PurchaseRequestTests.Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await PurchaseRequestTests.Ok(await o.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var d = await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-requests/{req}"));
        var lines = d.GetProperty("lines").EnumerateArray().Select(l => new { requestLineId = l.GetProperty("id").GetGuid(), supplierId = o.Supplier }).ToArray();
        var orders = await PurchaseRequestTests.Ok(await o.Admin.PostAsJsonAsync("/api/purchase-requests/order", new { lines }));
        return (req, PurchaseRequestTests.Id(orders.EnumerateArray().Single()), o);
    }

    private Task<PurchaseRequestStatus> RequestStatusAsync(Guid request) =>
        DbAsync(fx, db => db.PurchaseRequests.IgnoreQueryFilters().Where(r => r.Id == request).Select(r => r.Status).SingleAsync());

    private static async Task<JsonNode> PoBodyAsync(HttpClient c, Guid po, int keepLines, string notes)
    {
        var d = await PurchaseRequestTests.Ok(await c.GetAsync($"/api/purchase-orders/{po}"));
        var lines = d.GetProperty("lines").EnumerateArray().Take(keepLines).Select(l => new
        {
            id = l.GetProperty("id").GetGuid(), partId = G(l, "partId"), description = l.GetProperty("description").GetString(), unitOfMeasure = l.GetProperty("unitOfMeasure").GetString(),
            quantity = l.GetProperty("quantity").GetDecimal(), unitPrice = l.GetProperty("unitPrice").GetDecimal(), discountPercent = l.GetProperty("discountPercent").GetDecimal(),
            storeId = G(l, "storeId"), workOrderId = G(l, "workOrderId"), accountCode = (string?)null, purchaseRequestLineId = G(l, "purchaseRequestLineId"),
        }).ToArray();
        return JsonSerializer.SerializeToNode(new
        {
            siteId = d.GetProperty("siteId").GetGuid(), supplierId = d.GetProperty("supplierId").GetGuid(), currency = d.GetProperty("currency").GetString(),
            exchangeRate = d.GetProperty("exchangeRate").GetDecimal(), shippingCost = 0m, notes, lines, rowVersion = d.GetProperty("rowVersion").GetString(),
        })!;
    }
    private static Guid? G(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetGuid() : null;

    [SkippableFact]
    public async Task Order_edit_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        Assert.Equal(PurchaseRequestStatus.Ordered, await RequestStatusAsync(req));
        // Taking a line off the order puts its request back to "partly ordered"; that write fails.
        await using (await FailureInjection.RefuseAsync(fx, "PurchaseRequests", $"NOT ([Id] = '{req}' AND [Status] = 'PartlyOrdered')"))
            await Failed(o.Admin.PutAsJsonAsync($"/api/purchase-orders/{po}", await PoBodyAsync(o.Admin, po, 1, "Edited")));
        var after = await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal(2, after.GetProperty("lines").GetArrayLength());
        Assert.NotEqual("Edited", after.GetProperty("notes").GetString());
        Assert.Equal(PurchaseRequestStatus.Ordered, await RequestStatusAsync(req));
    }

    [SkippableFact]
    public async Task Order_delete_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        await using (await FailureInjection.RefuseAsync(fx, "PurchaseRequests", $"NOT ([Id] = '{req}' AND [Status] = 'Approved')"))
            await Failed(o.Admin.DeleteAsync($"/api/purchase-orders/{po}"));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await o.Admin.GetAsync($"/api/purchase-orders/{po}")).StatusCode);
        Assert.Equal(PurchaseRequestStatus.Ordered, await RequestStatusAsync(req));
    }

    [SkippableFact]
    public async Task Order_cancel_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        await using (await FailureInjection.RefuseAsync(fx, "PurchaseRequests", $"NOT ([Id] = '{req}' AND [Status] = 'Approved')"))
            await Failed(o.Admin.PostAsJsonAsync($"/api/purchase-orders/{po}/cancel", new { reason = "Supplier closed" }));
        var after = await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal("Draft", after.GetProperty("status").GetString());
        Assert.Equal(PurchaseRequestStatus.Ordered, await RequestStatusAsync(req));
    }

    // ---------------- stores and parts ----------------

    [SkippableFact]
    public async Task Store_delete_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        Task<int> Levels() => DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().CountAsync(l => l.StoreId == o.Store));
        Assert.Equal(1, await Levels());
        // Its stock levels go first, then the store itself; the second write fails.
        await using (await FailureInjection.RefuseAsync(fx, "Stores", $"NOT ([Id] = '{o.Store}' AND [IsDeleted] = 1)"))
            await Failed(o.Admin.DeleteAsync($"/api/stores/{o.Store}"));
        Assert.Equal(1, await Levels());
        Assert.False(await DbAsync(fx, db => db.Stores.IgnoreQueryFilters().Where(x => x.Id == o.Store).Select(x => x.IsDeleted).SingleAsync()));
    }

    [SkippableFact]
    public async Task Part_delete_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        Task<int> Levels() => DbAsync(fx, db => db.StockLevels.IgnoreQueryFilters().CountAsync(l => l.PartId == o.Part));
        Task<int> Rows() => DbAsync(fx, db => db.SupplierParts.IgnoreQueryFilters().CountAsync(r => r.PartId == o.Part && !r.IsDeleted));
        Assert.Equal(1, await Levels());
        var rows = await Rows();
        Assert.True(rows > 0);
        await using (await FailureInjection.RefuseAsync(fx, "Parts", $"NOT ([Id] = '{o.Part}' AND [IsDeleted] = 1)"))
            await Failed(o.Admin.DeleteAsync($"/api/parts/{o.Part}"));
        Assert.Equal(1, await Levels());
        Assert.Equal(rows, await Rows());
    }

    // ---------------- brands, models, units: a new name, then every record that carries it ----------------

    private static async Task<Guid> AssetOnBrandAsync(Loop.Org o, Guid brand, Guid? model = null) =>
        Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new
        {
            name = "Press " + Tag(), siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None", brandId = brand, modelId = model, managesBrand = true,
        })));

    private static Task<Guid> BrandAsync(Loop.Org o, string name) => Task.Run(async () => Id(await Ok(await o.Admin.PostAsJsonAsync("/api/brands", new { name, isActive = true }))));

    private Task<string?> ManufacturerAsync(Guid asset) => DbAsync(fx, db => db.Assets.IgnoreQueryFilters().Where(a => a.Id == asset).Select(a => a.Manufacturer).SingleAsync());

    [SkippableFact]
    public async Task Brand_rename_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var old = "Old" + Tag(); var renamed = "New" + Tag();
        var brand = await BrandAsync(o, old);
        var asset = await AssetOnBrandAsync(o, brand);
        // The assets carry the name as text; writing the new name to them fails, so the brand keeps its name.
        await using (await FailureInjection.RefuseAsync(fx, "Assets", $"[Manufacturer] IS NULL OR [Manufacturer] <> '{renamed}'"))
            await Failed(o.Admin.PutAsJsonAsync($"/api/brands/{brand}", new { name = renamed, isActive = true }));
        Assert.Equal(old, await DbAsync(fx, db => db.Brands.IgnoreQueryFilters().Where(b => b.Id == brand).Select(b => b.Name).SingleAsync()));
        Assert.Equal(old, await ManufacturerAsync(asset));
    }

    [SkippableFact]
    public async Task Brand_merge_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var from = "From" + Tag(); var into = "Into" + Tag();
        var a = await BrandAsync(o, from);
        var b = await BrandAsync(o, into);
        var asset = await AssetOnBrandAsync(o, a);
        await using (await FailureInjection.RefuseAsync(fx, "Assets", $"[Manufacturer] IS NULL OR [Manufacturer] <> '{into}'"))
            await Failed(o.Admin.PutAsJsonAsync($"/api/brands/{a}", new { name = into, isActive = true, merge = true }));
        Assert.False(await DbAsync(fx, db => db.Brands.IgnoreQueryFilters().Where(x => x.Id == a).Select(x => x.IsDeleted).SingleAsync()));
        Assert.Equal(a, await DbAsync(fx, db => db.Assets.IgnoreQueryFilters().Where(x => x.Id == asset).Select(x => x.BrandId).SingleAsync()));
        Assert.Equal(from, await ManufacturerAsync(asset));
    }

    [SkippableFact]
    public async Task Model_rename_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var brand = await BrandAsync(o, "Maker" + Tag());
        var old = "M-" + Tag(); var renamed = "N-" + Tag();
        var model = Id(await Ok(await o.Admin.PostAsJsonAsync($"/api/brands/{brand}/models", new { name = old, isActive = true })));
        var asset = await AssetOnBrandAsync(o, brand, model);
        await using (await FailureInjection.RefuseAsync(fx, "Assets", $"[Model] IS NULL OR [Model] <> '{renamed}'"))
            await Failed(o.Admin.PutAsJsonAsync($"/api/brand-models/{model}", new { name = renamed, isActive = true }));
        Assert.Equal(old, await DbAsync(fx, db => db.BrandModels.IgnoreQueryFilters().Where(m => m.Id == model).Select(m => m.Name).SingleAsync()));
        Assert.Equal(old, await DbAsync(fx, db => db.Assets.IgnoreQueryFilters().Where(x => x.Id == asset).Select(x => x.Model).SingleAsync()));
    }

    [SkippableFact]
    public async Task Unit_rename_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var old = "ZU" + Tag()[..5]; var renamed = "ZV" + Tag()[..5];
        var unit = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/units", new { symbol = old, name = "Test unit", isActive = true })));
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new { partNumber = "U-" + Tag(), name = "Rope", unitOfMeasure = old, standardPrice = 1, isCritical = false, isShelfItem = false, allowNegativeStock = false })));
        await using (await FailureInjection.RefuseAsync(fx, "Parts", $"[UnitOfMeasure] <> '{renamed}'"))
            await Failed(o.Admin.PutAsJsonAsync($"/api/units/{unit}", new { symbol = renamed, name = "Test unit", isActive = true }));
        Assert.Equal(old, await DbAsync(fx, db => db.Units.IgnoreQueryFilters().Where(u => u.Id == unit).Select(u => u.Symbol).SingleAsync()));
        Assert.Equal(old, await DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(p => p.Id == part).Select(p => p.UnitOfMeasure).SingleAsync()));
    }

    // ---------------- a new work order and its stop ----------------

    [SkippableFact]
    public async Task Work_order_with_a_stop_failing_part_way_changes_nothing()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var asset = await Pm12.AssetAsync(o.Admin, o.Site, "Press " + Tag());
        var tag = "STOP" + Tag();
        // The work order is saved first, then the stop is linked to it; the link fails.
        await using (await FailureInjection.RefuseAsync(fx, "WorkOrders", $"[DowntimeEventId] IS NULL OR [Title] NOT LIKE '%{tag}'"))
            await Failed(o.Admin.PostAsJsonAsync("/api/work-orders", new
            {
                title = "Fix " + tag, siteId = o.Site, assetId = asset, type = "Breakdown", priority = "High", tasks = Array.Empty<string>(), assetIsDown = true,
            }));
        Assert.Equal(0, await DbAsync(fx, db => db.WorkOrders.IgnoreQueryFilters().CountAsync(w => w.Title.EndsWith(tag))));
        Assert.Equal(0, await DbAsync(fx, db => db.DowntimeEvents.IgnoreQueryFilters().CountAsync(e => e.AssetId == asset && !e.IsDeleted)));
    }

    // ---------------- run again after a transient failure ----------------
    // A dropped connection on the second write makes the retry strategy run the whole body again: the first write must be made again too.

    private async Task<(HttpClient Client, FlakyOnce Flaky)> SecondHostAsync(HttpClient admin)
    {
        var u = await UserAsync(fx, admin, "Administrator");
        var flaky = new FlakyOnce();
        return (await FlakyOnce.HostAsync(fx, flaky, u.Email, Password), flaky);
    }

    [SkippableFact]
    public async Task Order_cancel_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        var (c, flaky) = await SecondHostAsync(o.Admin);
        flaky.Arm("UPDATE [PurchaseRequests]");
        await PurchaseRequestTests.Ok(await c.PostAsJsonAsync($"/api/purchase-orders/{po}/cancel", new { reason = "Supplier closed" }));
        Assert.True(flaky.Fired);
        Assert.Equal("Cancelled", (await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("status").GetString());
        Assert.Equal(PurchaseRequestStatus.Approved, await RequestStatusAsync(req));
    }

    [SkippableFact]
    public async Task Order_delete_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        var (c, flaky) = await SecondHostAsync(o.Admin);
        flaky.Arm("UPDATE [PurchaseRequests]");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/purchase-orders/{po}")).StatusCode);
        Assert.True(flaky.Fired);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await o.Admin.GetAsync($"/api/purchase-orders/{po}")).StatusCode);
        Assert.Equal(PurchaseRequestStatus.Approved, await RequestStatusAsync(req));
    }

    [SkippableFact]
    public async Task Order_edit_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var (req, po, o) = await OrderedAsync();
        var (c, flaky) = await SecondHostAsync(o.Admin);
        var body = await PoBodyAsync(o.Admin, po, 1, "Edited");
        flaky.Arm("UPDATE [PurchaseRequests]");
        await PurchaseRequestTests.Ok(await c.PutAsJsonAsync($"/api/purchase-orders/{po}", body));
        Assert.True(flaky.Fired);
        var after = await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"));
        Assert.Equal("Edited", after.GetProperty("notes").GetString());
        Assert.Equal(1, after.GetProperty("lines").GetArrayLength());
        Assert.Equal(PurchaseRequestStatus.PartlyOrdered, await RequestStatusAsync(req));
    }

    [SkippableFact]
    public async Task Order_save_linking_a_request_line_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var (tech, _, _) = await PurchaseRequestTests.UserAsync(fx, o.Admin, "Technician");
        var req = await PurchaseRequestTests.RequestAsync(tech, o, PurchaseRequestTests.PartLine(o, 3));
        await PurchaseRequestTests.Ok(await tech.PostAsync($"/api/purchase-requests/{req}/submit", null));
        await PurchaseRequestTests.Ok(await o.Admin.PostAsync($"/api/purchase-requests/{req}/approve", null));
        var line = (await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-requests/{req}"))).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var (c, flaky) = await SecondHostAsync(o.Admin);
        // The order is saved with the claim on its request (first write); the request's new status (second write) is the one that drops.
        flaky.Arm("UPDATE [PurchaseRequests]", skip: 1);
        var made = await PurchaseRequestTests.Ok(await c.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, currency = "USD", exchangeRate = 1m, shippingCost = 0m,
            lines = new[] { new { partId = (Guid?)o.Part, description = "Bearing", unitOfMeasure = "EA", quantity = 3m, unitPrice = 10m, discountPercent = 0m, storeId = (Guid?)o.Store, purchaseRequestLineId = (Guid?)line } },
        }));
        Assert.True(flaky.Fired);
        var po = PurchaseRequestTests.Id(made);
        Assert.Equal(1, await DbAsync(fx, db => db.PurchaseOrders.IgnoreQueryFilters().CountAsync(x => x.Lines.Any(l => l.PurchaseRequestLineId == line))));
        Assert.Equal(PurchaseRequestStatus.Ordered, await RequestStatusAsync(req));
        Assert.Equal(1, (await PurchaseRequestTests.Ok(await o.Admin.GetAsync($"/api/purchase-orders/{po}"))).GetProperty("lines").GetArrayLength());
    }

    [SkippableFact]
    public async Task Brand_rename_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, emailOn: false);
        var old = "Old" + Tag(); var renamed = "New" + Tag();
        var brand = await BrandAsync(o, old);
        var asset = await AssetOnBrandAsync(o, brand);
        var flaky = new FlakyOnce();
        var c = await FlakyOnce.HostAsync(fx, flaky, o.AdminEmail, "Passw0rd!x");
        flaky.Arm("[Manufacturer] =");
        await Ok(await c.PutAsJsonAsync($"/api/brands/{brand}", new { name = renamed, isActive = true }));
        Assert.True(flaky.Fired);
        Assert.Equal(renamed, await DbAsync(fx, db => db.Brands.IgnoreQueryFilters().Where(b => b.Id == brand).Select(b => b.Name).SingleAsync()));
        Assert.Equal(renamed, await ManufacturerAsync(asset));
    }

    [SkippableFact]
    public async Task Unit_rename_run_again_after_a_dropped_connection_is_fully_applied()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await PurchaseRequestTests.OrgAsync(fx);
        var u = await UserAsync(fx, o.Admin, "Administrator");
        var old = "ZU" + Tag()[..5]; var renamed = "ZV" + Tag()[..5];
        var unit = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/units", new { symbol = old, name = "Test unit", isActive = true })));
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new { partNumber = "U-" + Tag(), name = "Rope", unitOfMeasure = old, standardPrice = 1, isCritical = false, isShelfItem = false, allowNegativeStock = false })));
        var flaky = new FlakyOnce();
        var c = await FlakyOnce.HostAsync(fx, flaky, u.Email, Password);
        flaky.Arm("[UnitOfMeasure] =");
        await Ok(await c.PutAsJsonAsync($"/api/units/{unit}", new { symbol = renamed, name = "Test unit", isActive = true }));
        Assert.True(flaky.Fired);
        Assert.Equal(renamed, await DbAsync(fx, db => db.Units.IgnoreQueryFilters().Where(x => x.Id == unit).Select(x => x.Symbol).SingleAsync()));
        Assert.Equal(renamed, await DbAsync(fx, db => db.Parts.IgnoreQueryFilters().Where(p => p.Id == part).Select(p => p.UnitOfMeasure).SingleAsync()));
    }

    // ---------------- Arabic ----------------

    [Theory]
    [InlineData("This work order is completed, so ticking \"The asset is down\" starts no stop. Report a stop in the asset's Downtime tab instead.")]
    [InlineData("This PM follows an asset group. Add the asset to the group instead of naming it on the PM.")]
    public void New_messages_have_Arabic(string english) =>
        Assert.NotEqual(english, FourC.Cmms.Api.Infrastructure.Translator.T(english, "ar"));
}
