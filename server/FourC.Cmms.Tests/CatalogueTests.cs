using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Tests.CompanyTests;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: a part's suppliers with their packs and prices, the preferred star, the last price paid, and the company's parts.</summary>
public class CatalogueTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    internal static async Task<Guid> SupplierAsync(HttpClient c, string code, string? currency = null) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code, name = code + " Ltd", currency, isSupplier = true, isContractor = false })));

    internal static object Row(Guid supplier, decimal packSize = 1, decimal? price = null, string? packName = null, decimal discount = 0, int? lead = null, int minPacks = 1,
        string? currency = null, bool preferred = false, string? number = null) => new
    {
        supplierId = supplier, supplierPartNumber = number, packName, packSize, packPrice = price, currency, discountPercent = discount, leadTimeDays = lead, minPacks,
        isPreferred = preferred, isActive = true,
    };

    internal static async Task<Guid> AddRowAsync(HttpClient c, Guid part, object row) => Id(await Ok(await c.PostAsJsonAsync($"/api/parts/{part}/suppliers", row)));
    private static async Task<List<JsonElement>> RowsAsync(HttpClient c, Guid part) => (await Ok(await c.GetAsync($"/api/parts/{part}/suppliers"))).EnumerateArray().ToList();

    [SkippableFact]
    public async Task Part_has_several_suppliers_with_packs_and_prices()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var local = await SupplierAsync(o.Admin, "LOCAL");
        await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100", 2, 10, number: "6205-2RS"));
        await AddRowAsync(o.Admin, o.Part, Row(local, 1, 0.6m));
        var rows = await RowsAsync(o.Admin, o.Part);
        var box = rows.Single(r => r.GetProperty("supplierId").GetGuid() == skf);
        Assert.Equal("Box of 100", box.GetProperty("packName").GetString());
        Assert.Equal(100m, box.GetProperty("packSize").GetDecimal());
        Assert.Equal(45m, box.GetProperty("packPrice").GetDecimal());
        Assert.Equal("6205-2RS", box.GetProperty("supplierPartNumber").GetString());
        Assert.Equal("USD", box.GetProperty("currency").GetString());
        var each = rows.Single(r => r.GetProperty("supplierId").GetGuid() == local);
        Assert.Equal("EA", each.GetProperty("packName").GetString());
        Assert.Contains("This supplier is already in the part's catalogue.", await Problem(await o.Admin.PostAsJsonAsync($"/api/parts/{o.Part}/suppliers", Row(skf, 10, 5))));
    }

    [SkippableFact]
    public async Task Catalogue_rows_are_checked()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var lift = await ContractorAsync(o.Admin, "LIFT");
        var url = $"/api/parts/{o.Part}/suppliers";
        Assert.Contains("Pieces per pack must be more than 0.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(skf, 0, 1))));
        Assert.Contains("The price can't be below 0.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(skf, 1, -1))));
        Assert.Contains("Discount must be between 0 and 100%.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(skf, 1, 1, discount: 120))));
        Assert.Contains("Minimum order must be at least 1 pack.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(skf, 1, 1, minPacks: 0))));
        Assert.Contains("Use a 3-letter currency code.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(skf, 1, 1, currency: "EURO"))));
        Assert.Contains("Choose a company with the Supplier role.", await Problem(await o.Admin.PostAsJsonAsync(url, Row(lift, 1, 1))));
    }

    [SkippableFact]
    public async Task Star_sets_the_preferred_supplier_and_choosing_one_on_the_part_adds_a_row()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var row = await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100"));
        await Ok(await o.Admin.PutAsJsonAsync($"/api/parts/{o.Part}/suppliers/{row}", Row(skf, 100, 45, "Box of 100", preferred: true)));
        Assert.Equal(skf, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("preferredSupplierId").GetGuid());
        Assert.True((await RowsAsync(o.Admin, o.Part)).Single(r => r.GetProperty("supplierId").GetGuid() == skf).GetProperty("isPreferred").GetBoolean());

        // A new part naming a preferred supplier gets a catalogue row for it: a pack of 1 at its standard price (company in the base currency).
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "SEAL-1", name = "Seal", unitOfMeasure = "PC", standardPrice = 7.5m, isCritical = false, isShelfItem = false, allowNegativeStock = false, preferredSupplierId = skf,
        })));
        var made = Assert.Single(await RowsAsync(o.Admin, part));
        Assert.Equal(1m, made.GetProperty("packSize").GetDecimal());
        Assert.Equal("PC", made.GetProperty("packName").GetString());
        Assert.Equal(7.5m, made.GetProperty("packPrice").GetDecimal());
        Assert.True(made.GetProperty("isPreferred").GetBoolean());
    }

    private static object RowWithoutPreferred(Guid supplier, string note) => new
    {
        supplierId = supplier, packName = "Box", packSize = 100m, packPrice = 45m, discountPercent = 0m, minPacks = 1, notes = note, isActive = true,
    };

    [SkippableFact]
    public async Task Saving_a_row_without_preferred_keeps_the_preferred_supplier()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var row = await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box", preferred: true));
        Assert.Equal(skf, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("preferredSupplierId").GetGuid());
        // An older page (or a script) saves the row and says nothing about the star.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/parts/{o.Part}/suppliers/{row}", RowWithoutPreferred(skf, "New price list")));
        Assert.Equal(skf, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("preferredSupplierId").GetGuid());
        Assert.True((await RowsAsync(o.Admin, o.Part)).Single(r => r.GetProperty("supplierId").GetGuid() == skf).GetProperty("isPreferred").GetBoolean());
    }

    [SkippableFact]
    public async Task Unstarring_a_row_clears_it()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var other = await SupplierAsync(o.Admin, "OTHER");
        var row = await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box", preferred: true));
        var otherRow = await AddRowAsync(o.Admin, o.Part, Row(other, 1, 1));
        // Un-starring another supplier's row leaves the star where it is.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/parts/{o.Part}/suppliers/{otherRow}", Row(other, 1, 1, preferred: false)));
        Assert.Equal(skf, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("preferredSupplierId").GetGuid());
        // Un-starring the preferred row itself clears it.
        await Ok(await o.Admin.PutAsJsonAsync($"/api/parts/{o.Part}/suppliers/{row}", Row(skf, 100, 45, "Box", preferred: false)));
        Assert.Equal(JsonValueKind.Null, (await Ok(await o.Admin.GetAsync($"/api/parts/{o.Part}"))).GetProperty("preferredSupplierId").ValueKind);
    }

    [SkippableFact]
    public async Task Foreign_currency_supplier_row_starts_without_a_price()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        await TestSettings.SetSettingsAsync(o.Admin, s => s["currency"] = "EGP");
        var usd = await SupplierAsync(o.Admin, "IMPORT", "USD");
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "PUMP-SEAL", name = "Pump seal", unitOfMeasure = "EA", standardPrice = 300m, isCritical = false, isShelfItem = false, allowNegativeStock = false, preferredSupplierId = usd,
        })));
        var made = Assert.Single(await RowsAsync(o.Admin, part));
        Assert.Equal("USD", made.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Null, made.GetProperty("packPrice").ValueKind);
    }

    [SkippableFact]
    public async Task Use_last_price_copies_price_and_discount_in_the_same_currency()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var row = await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100", 2));
        Assert.Contains("Nothing has been received from this supplier yet.", await Problem(await o.Admin.PostAsync($"/api/parts/{o.Part}/suppliers/{row}/use-last-price", null)));
        await Loop.DbAsync(fx, db => db.SupplierParts.IgnoreQueryFilters().Where(r => r.Id == row).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.LastPaidPrice, 48m).SetProperty(r => r.LastPaidDiscount, 5m).SetProperty(r => r.LastPaidCurrency, "USD").SetProperty(r => r.LastPaidOn, DateOnly.FromDateTime(DateTime.UtcNow))));
        await Ok(await o.Admin.PostAsync($"/api/parts/{o.Part}/suppliers/{row}/use-last-price", null));
        var r1 = (await RowsAsync(o.Admin, o.Part)).Single(r => r.GetProperty("id").GetGuid() == row);
        Assert.Equal(48m, r1.GetProperty("packPrice").GetDecimal());
        Assert.Equal(5m, r1.GetProperty("discountPercent").GetDecimal());
        await Loop.DbAsync(fx, db => db.SupplierParts.IgnoreQueryFilters().Where(r => r.Id == row).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastPaidCurrency, "EUR")));
        Assert.Contains("The last price was paid in another currency.", await Problem(await o.Admin.PostAsync($"/api/parts/{o.Part}/suppliers/{row}/use-last-price", null)));
    }

    [SkippableFact]
    public async Task Company_lists_the_parts_it_supplies_and_deleting_it_removes_its_rows()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100"));
        var listed = Assert.Single((await Ok(await o.Admin.GetAsync($"/api/suppliers/{skf}/parts"))).EnumerateArray());
        Assert.Equal(o.Part, listed.GetProperty("partId").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent, (await o.Admin.DeleteAsync($"/api/suppliers/{skf}")).StatusCode);
        Assert.DoesNotContain(await RowsAsync(o.Admin, o.Part), r => r.GetProperty("supplierId").GetGuid() == skf);
    }

    [SkippableFact]
    public async Task Viewer_reads_the_catalogue_but_cannot_change_it()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        await AddRowAsync(o.Admin, o.Part, Row(skf, 100, 45, "Box of 100"));
        var (viewer, _, _) = await UserAsync(fx, o.Admin, "Viewer");
        Assert.Contains(await RowsAsync(viewer, o.Part), r => r.GetProperty("supplierId").GetGuid() == skf);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/parts/{o.Part}/suppliers", Row(o.Supplier, 1, 1))).StatusCode);
    }

    [SkippableFact]
    public async Task A_part_without_any_price_gets_a_row_without_a_price()
    {
        Skip.IfNot(fx.Enabled);
        var o = await OrgAsync(fx);
        var skf = await SupplierAsync(o.Admin, "SKF");
        var part = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/parts", new
        {
            partNumber = "NO-PRICE", name = "No price yet", unitOfMeasure = "EA", standardPrice = 0m, isCritical = false, isShelfItem = false, allowNegativeStock = false, preferredSupplierId = skf,
        })));
        var made = Assert.Single(await RowsAsync(o.Admin, part));
        Assert.Equal(JsonValueKind.Null, made.GetProperty("packPrice").ValueKind);
    }
}
