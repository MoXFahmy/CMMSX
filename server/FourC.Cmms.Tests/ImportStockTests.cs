using System.Text.Json;
using static FourC.Cmms.Tests.ImportFiles;

namespace FourC.Cmms.Tests;

public class ImportStockTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly string[] PartHeadings = ["Part number", "Name", "Unit", "Standard price", "Shelf-life item"];
    private static readonly string[] StockHeadings = ["Part number", "Store code", "Re-order level", "Opening quantity", "Batch", "Expiry"];

    private static Sheet Places() => new("Sites", SiteHeadings, ["CAI", "Cairo", null]);
    private static Sheet Stores() => new("Stores", ["Code", "Name", "Site code"], ["ST1", "Main store", "CAI"]);

    private static async Task<JsonElement> PartAsync(HttpClient c, string number)
    {
        var list = await OkAsync(await c.GetAsync($"/api/parts?search={number}"));
        var id = list.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("partNumber").GetString() == number).GetProperty("id");
        return await OkAsync(await c.GetAsync($"/api/parts/{id}"));
    }

    private static async Task<List<JsonElement>> LedgerAsync(HttpClient c, JsonElement part) =>
        (await OkAsync(await c.GetAsync($"/api/inventory/transactions?partId={part.GetProperty("id")}"))).GetProperty("items").EnumerateArray().ToList();

    [SkippableFact]
    public async Task Opening_stock_is_posted_once()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var file = Book(Places(), Stores(), new Sheet("Parts", PartHeadings, ["BRG-6205", "Ball bearing", "EA", 6.8m, "No"]),
            new Sheet("Stock", StockHeadings, ["BRG-6205", "ST1", 5m, 24m, null, null]));
        await RunAsync(c, file);
        var part = await PartAsync(c, "BRG-6205");
        Assert.Equal(24m, part.GetProperty("levels")[0].GetProperty("quantityOnHand").GetDecimal());
        var ledger = await LedgerAsync(c, part);
        Assert.Equal("Opening balance (import)", Assert.Single(ledger).GetProperty("notes").GetString());
        Assert.Equal(6.8m, ledger[0].GetProperty("unitCost").GetDecimal());

        var again = await RunAsync(c, file);
        Assert.Equal(1, Count(SheetOf(again, "Stock"), "unchanged"));
        Assert.Contains("Opening quantity ignored for existing stock: use a stock take to change quantities.", again.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        part = await PartAsync(c, "BRG-6205");
        Assert.Equal(24m, part.GetProperty("levels")[0].GetProperty("quantityOnHand").GetDecimal());
        Assert.Single(await LedgerAsync(c, part));
    }

    [SkippableFact]
    public async Task Levels_update_on_existing_stock()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        await RunAsync(c, Book(Places(), Stores(), new Sheet("Parts", PartHeadings, ["BRG-6205", "Ball bearing", "EA", 6.8m, null]),
            new Sheet("Stock", StockHeadings, ["BRG-6205", "ST1", 5m, 10m, null, null])));
        var changed = SheetOf(await RunAsync(c, Book(new Sheet("Stock", ["Part number", "Store code", "Re-order level"], ["BRG-6205", "ST1", 8m]))), "Stock");
        Assert.Equal(1, Count(changed, "changed"));
        var level = (await PartAsync(c, "BRG-6205")).GetProperty("levels")[0];
        Assert.Equal((8m, 10m), (level.GetProperty("reorderLevel").GetDecimal(), level.GetProperty("quantityOnHand").GetDecimal()));
    }

    [SkippableFact]
    public async Task Shelf_life_parts_need_batch_and_expiry()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var parts = new Sheet("Parts", PartHeadings, ["SEAL-01", "Seal kit", "EA", 12m, "Yes"]);
        var without = SheetOf(await CheckAsync(c, Book(Places(), Stores(), parts, new Sheet("Stock", StockHeadings, ["SEAL-01", "ST1", null, 4m, null, null]))), "Stock");
        Assert.Contains("SEAL-01 is a shelf-life item: enter the batch and expiry.", ProblemsOf(without));

        await RunAsync(c, Book(Places(), Stores(), parts, new Sheet("Stock", StockHeadings, ["SEAL-01", "ST1", null, 4m, "B-77", new DateOnly(2027, 6, 30)])));
        var row = Assert.Single(await LedgerAsync(c, await PartAsync(c, "SEAL-01")));
        Assert.Equal(("B-77", "2027-06-30"), (row.GetProperty("batchNumber").GetString(), row.GetProperty("expiryDate").GetString()));
    }

    [SkippableFact]
    public async Task Unknown_part_or_store_is_a_problem()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var c = await NewOrganizationAsync(fx);
        var sheet = SheetOf(await CheckAsync(c, Book(Places(), Stores(), new Sheet("Stock", StockHeadings, ["XX", "YY", null, null, null, null]))), "Stock");
        Assert.Contains("Part XX not found.", ProblemsOf(sheet));
        Assert.Contains("Store YY not found.", ProblemsOf(sheet));
    }
}
