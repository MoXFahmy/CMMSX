using System.Net.Http.Json;
using static FourC.Cmms.Tests.CatalogueTests;
using static FourC.Cmms.Tests.ImportFiles;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: the re-order proposal's supplier and catalogue options, and the Supplier catalogue sheet.</summary>
public class CatalogueProposalImportTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> PartAsync(PurchaseRequestTests.Org o, string number, Guid? preferred = null) =>
        Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = number, name = number, unitOfMeasure = "EA", standardPrice = 2m, isCritical = false, isShelfItem = false, allowNegativeStock = false,
            preferredSupplierId = preferred,
            levels = new[] { new { storeId = o.Store, bin = "A1", minLevel = 0, reorderLevel = 10, maxLevel = 0, reorderQuantity = 5 } },
        })));

    [SkippableFact]
    public async Task Proposal_suggests_the_preferred_else_only_else_last_supplier_with_catalogue_options()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var local = await SupplierAsync(o.Admin, "LOCAL");
        var acme = await SupplierAsync(o.Admin, "ACME");
        var a = await PartAsync(o, "PA", skf);
        await AddRowAsync(o.Admin, a, Row(local, 10, 3, "Pack of 10"));
        var b = await PartAsync(o, "PB");
        await AddRowAsync(o.Admin, b, Row(local, 10, 3, "Pack of 10"));
        var c = await PartAsync(o, "PC");
        await Ok(await o.Admin.PostAsJsonAsync("/api/inventory/receive", new { partId = c, storeId = o.Store, quantity = 1, unitCost = 2, supplierId = acme }));

        var rows = (await Ok(await o.Admin.GetAsync("/api/inventory/proposal"))).EnumerateArray().ToDictionary(r => r.GetProperty("partId").GetGuid());
        Assert.Equal(skf, rows[a].GetProperty("preferredSupplierId").GetGuid());
        Assert.Equal(local, rows[b].GetProperty("preferredSupplierId").GetGuid());
        Assert.Equal(acme, rows[c].GetProperty("preferredSupplierId").GetGuid());
        var options = rows[a].GetProperty("catalogue").EnumerateArray().ToList();
        Assert.Equal([skf, local], options.Select(x => x.GetProperty("supplierId").GetGuid()));
        Assert.True(options[0].GetProperty("isPreferred").GetBoolean());
        Assert.Equal(10m, options[1].GetProperty("packSize").GetDecimal());
    }

    [SkippableFact]
    public async Task Supplier_catalogue_sheet_imports_and_exports()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        await RunAsync(o.Admin, Book(new Sheet("SupplierCatalogue",
            ["Part number", "Supplier code", "Supplier's part number", "Pack", "Pieces per pack", "Price per pack", "Currency", "Discount %", "Lead time (days)", "Minimum packs", "Notes"],
            ["BRG-1", "SKF", "6205-2RS", "Box of 100", 100m, 45m, null, 2m, 10m, 1m, null])));
        var row = (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}/suppliers"))).EnumerateArray().Single(r => r.GetProperty("supplierId").GetGuid() == skf);
        Assert.Equal("Box of 100", row.GetProperty("packName").GetString());
        Assert.Equal(100m, row.GetProperty("packSize").GetDecimal());
        Assert.Equal(45m, row.GetProperty("packPrice").GetDecimal());
        Assert.Equal("6205-2RS", row.GetProperty("supplierPartNumber").GetString());
        Assert.Equal(10, row.GetProperty("leadTimeDays").GetInt32());

        var current = await (await o.Admin.GetAsync("/api/import/current")).Content.ReadAsByteArrayAsync();
        var sheet = SheetOf(await CheckAsync(o.Admin, current), "SupplierCatalogue");
        Assert.Equal((0, 0, 0), (Count(sheet, "new"), Count(sheet, "changed"), Count(sheet, "problems")));
    }

    [SkippableFact]
    public async Task Parts_sheet_preferred_supplier_adds_a_catalogue_row()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        await RunAsync(o.Admin, Book(new Sheet("Parts", ["Part number", "Name", "Unit", "Standard price", "Preferred supplier code"], ["NEW-1", "New part", "EA", 4m, "SKF"])));
        var part = (await Ok(await o.Admin.GetAsync("/api/parts?search=NEW-1"))).GetProperty("items")[0].GetProperty("id").GetGuid();
        var row = Assert.Single((await Ok(await o.Admin.GetAsync($"/api/parts/{part}/suppliers"))).EnumerateArray());
        Assert.Equal(skf, row.GetProperty("supplierId").GetGuid());
        Assert.Equal(1m, row.GetProperty("packSize").GetDecimal());
        Assert.Equal(4m, row.GetProperty("packPrice").GetDecimal());
    }
}
