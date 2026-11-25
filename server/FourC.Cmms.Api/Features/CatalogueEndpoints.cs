using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>The supplier catalogue (0.18.0): each part's suppliers with their packs and prices, and filling order lines from it.</summary>
public static class CatalogueEndpoints
{
    public sealed record SupplierPartInput(Guid SupplierId, string? SupplierPartNumber, string? PackName, decimal PackSize, decimal? PackPrice, string? Currency,
        decimal DiscountPercent, int? LeadTimeDays, int MinPacks, string? Notes, bool IsActive = true, bool? IsPreferred = null);

    /// <summary>A part line to fill: the pieces it needs and its current price per piece, kept when the catalogue has no price for it.</summary>
    public sealed record FillRequest(Guid PartId, decimal Pieces, decimal? PerPiece = null);
    public sealed record FillInput(Guid SupplierId, string? Currency, List<FillRequest> Lines);
    /// <summary>An order line as the catalogue fills it: its order unit and pieces per unit, whole packs, price and discount.</summary>
    public sealed record FilledLine(Guid PartId, string UnitOfMeasure, decimal PackSize, decimal Quantity, decimal UnitPrice, decimal DiscountPercent,
        string? SupplierPartNumber, int? LeadTimeDays, bool CataloguePrice);

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Catalogue rows change with either Manage parts or Manage suppliers.</summary>
    private static async Task<bool> MayEditAsync(HttpContext http, PermissionService perms)
    {
        var mine = await perms.GetAsync(UserId(http));
        return mine.Contains(Permissions.InventoryManage) || mine.Contains(Permissions.SuppliersManage);
    }

    /// <summary>The organization's base currency.</summary>
    internal static async Task<string> BaseCurrencyAsync(CmmsDbContext db, CancellationToken ct) =>
        (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings.Currency.ToUpperInvariant();

    public static void MapCatalogueEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/parts/{partId:guid}/suppliers").WithTags("Supplier catalogue");

        g.MapGet("/", async (Guid partId, CmmsDbContext db, CancellationToken ct) =>
        {
            var part = await db.Parts.AsNoTracking().Where(p => p.Id == partId).Select(p => new { p.Id, p.PreferredSupplierId }).FirstOrDefaultAsync(ct);
            if (part is null) return Results.NotFound();
            var rows = await db.SupplierParts.AsNoTracking().Where(r => r.PartId == partId).OrderBy(r => r.Supplier!.Name)
                .Select(r => new
                {
                    r.Id, r.SupplierId, SupplierCode = r.Supplier!.Code, SupplierName = r.Supplier.Name, SupplierActive = r.Supplier.IsActive && r.Supplier.IsSupplier,
                    r.SupplierPartNumber, r.PackName, r.PackSize, r.PackPrice, r.Currency, r.DiscountPercent, r.LeadTimeDays, r.MinPacks, r.Notes, r.IsActive,
                    IsPreferred = r.SupplierId == part.PreferredSupplierId, r.LastPaidPrice, r.LastPaidDiscount, r.LastPaidCurrency, r.LastPaidOn,
                }).ToListAsync(ct);
            return Results.Ok(rows.OrderByDescending(r => r.IsPreferred));
        }).RequirePermission(Permissions.InventoryView);

        g.MapPost("/", async (Guid partId, SupplierPartInput i, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct);
            if (part is null) return Results.NotFound();
            var v = await ValidateAsync(db, part, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var row = new SupplierPart { PartId = partId, SupplierId = i.SupplierId, PackName = "", Currency = "" };
            await ApplyAsync(db, part, row, i, ct);
            db.SupplierParts.Add(row);
            Prefer(part, row, i.IsPreferred);
            // Two people adding one supplier at the same moment: the second is told it is there, not given the generic duplicate text (0.18.0-13).
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (Unique.Violated(e)) { return Results.Problem(title: Translator.T("This supplier is already in the part's catalogue."), statusCode: 409); }
            return Results.Created($"/api/parts/{partId}/suppliers/{row.Id}", new { row.Id });
        }).RequireAuthorization();

        g.MapPut("/{rowId:guid}", async (Guid partId, Guid rowId, SupplierPartInput i, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct);
            var row = await db.SupplierParts.FirstOrDefaultAsync(r => r.Id == rowId && r.PartId == partId, ct);
            if (part is null || row is null) return Results.NotFound();
            var v = await ValidateAsync(db, part, i, row, ct);
            if (!v.IsValid) return v.Problem();
            await ApplyAsync(db, part, row, i, ct);
            Prefer(part, row, i.IsPreferred);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (Unique.Violated(e)) { return Results.Problem(title: Translator.T("This supplier is already in the part's catalogue."), statusCode: 409); }
            return Results.Ok(new { row.Id });
        }).RequireAuthorization();

        g.MapDelete("/{rowId:guid}", async (Guid partId, Guid rowId, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct);
            var row = await db.SupplierParts.FirstOrDefaultAsync(r => r.Id == rowId && r.PartId == partId, ct);
            if (part is null || row is null) return Results.NotFound();
            if (part.PreferredSupplierId == row.SupplierId) part.PreferredSupplierId = null;
            db.SupplierParts.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // The last price paid (and its discount) becomes the catalogue price — only in the same currency.
        g.MapPost("/{rowId:guid}/use-last-price", async (Guid partId, Guid rowId, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            if (!await MayEditAsync(http, perms)) return Results.Forbid();
            var row = await db.SupplierParts.FirstOrDefaultAsync(r => r.Id == rowId && r.PartId == partId, ct);
            if (row is null) return Results.NotFound();
            if (row.LastPaidPrice is not { } paid) throw new DomainException("Nothing has been received from this supplier yet.");
            if (!string.Equals(row.LastPaidCurrency, row.Currency, StringComparison.OrdinalIgnoreCase)) throw new DomainException("The last price was paid in another currency.");
            row.PackPrice = paid; row.DiscountPercent = row.LastPaidDiscount ?? 0;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.PackPrice, row.DiscountPercent });
        }).RequireAuthorization();

        // The order form's lines from the catalogue: a part and the pieces needed become the supplier's whole packs at their price.
        api.MapPost("/purchase-orders/fill", async (FillInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var company = await db.Suppliers.AsNoTracking().Where(s => s.Id == i.SupplierId).Select(s => new { s.Currency }).FirstOrDefaultAsync(ct);
            if (company is null) return Results.NotFound();
            var currency = i.Currency.Clean()?.ToUpperInvariant() ?? company.Currency.Clean()?.ToUpperInvariant() ?? await BaseCurrencyAsync(db, ct);
            var ids = i.Lines.Select(l => l.PartId).Distinct().ToList();
            var parts = await db.Parts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var filled = new List<FilledLine>();
            foreach (var l in i.Lines)
                if (parts.GetValueOrDefault(l.PartId) is { } part) filled.Add(await FillAsync(db, part, i.SupplierId, currency, l.Pieces, null, ct, fallbackPerPiece: l.PerPiece is > 0 ? l.PerPiece : null));
            return Results.Ok(filled);
        }).WithTags("Supplier catalogue").RequirePermission(Permissions.PurchasingCreate);

        // The parts a company supplies (its catalogue rows), read-only on the company.
        api.MapGet("/suppliers/{id:guid}/parts", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.SupplierParts.AsNoTracking().Where(r => r.SupplierId == id).OrderBy(r => r.Part!.PartNumber)
                .Select(r => new { r.PartId, r.Part!.PartNumber, PartName = r.Part.Name, r.SupplierPartNumber, r.PackName, r.PackSize, r.PackPrice, r.Currency, r.IsActive })
                .ToListAsync(ct))
            .WithTags("Supplier catalogue").RequirePermission(Permissions.PurchasingView);
    }

    /// <summary>
    /// The row for the part's preferred supplier, made when missing: a pack of 1 in the part's unit and its lead time, in the company's currency
    /// (else the base currency), at the part's last purchase / standard price only when that is the base currency (those are base-currency prices).
    /// </summary>
    internal static async Task<SupplierPart?> EnsureRowAsync(CmmsDbContext db, Part part, Guid supplierId, CancellationToken ct)
    {
        var existing = await db.SupplierParts.FirstOrDefaultAsync(r => r.PartId == part.Id && r.SupplierId == supplierId, ct)
                       ?? db.SupplierParts.Local.FirstOrDefault(r => r.PartId == part.Id && r.SupplierId == supplierId && db.Entry(r).State == EntityState.Added);
        if (existing is not null) return existing;
        var company = await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId).Select(s => new { s.Currency }).FirstOrDefaultAsync(ct);
        if (company is null) return null;
        var row = PreferredRow(part, supplierId, company.Currency, await BaseCurrencyAsync(db, ct));
        db.SupplierParts.Add(row);
        return row;
    }

    /// <summary>
    /// A new row for the part's preferred supplier (no database): a pack of 1 in the part's unit and its lead time, in the company's currency
    /// (else the base currency), at the part's last purchase price, else standard price, only in the base currency and only when more than 0.
    /// </summary>
    internal static SupplierPart PreferredRow(Part part, Guid supplierId, string? companyCurrency, string baseCurrency)
    {
        var currency = companyCurrency.Clean()?.ToUpperInvariant() ?? baseCurrency.ToUpperInvariant();
        static decimal? Priced(decimal? v) => v is > 0 ? v : null;
        return new SupplierPart
        {
            PartId = part.Id, SupplierId = supplierId, PackName = part.UnitOfMeasure, PackSize = 1, Currency = currency, LeadTimeDays = part.LeadTimeDays, MinPacks = 1,
            PackPrice = currency == baseCurrency.ToUpperInvariant() ? Priced(part.LastPurchasePrice) ?? Priced(part.StandardPrice) : null,
        };
    }

    /// <summary>
    /// An order line for <paramref name="pieces"/> of a part from a supplier. With an active catalogue row: its pack, whole packs (rounded up, at
    /// least the minimum) and supplier part number, at the catalogue price and discount when the row has a price in the order's currency and no
    /// price was typed; otherwise priced per piece (typed, else <paramref name="fallbackPerPiece"/>, else last purchase price, else average cost) ×
    /// pieces per pack, without discount. No row: the part's own unit as before.
    /// </summary>
    internal static async Task<FilledLine> FillAsync(CmmsDbContext db, Part part, Guid supplierId, string orderCurrency, decimal pieces, decimal? typedPerPiece,
        CancellationToken ct, decimal? fallbackPerPiece = null)
    {
        var row = await db.SupplierParts.AsNoTracking().FirstOrDefaultAsync(r => r.PartId == part.Id && r.SupplierId == supplierId && r.IsActive, ct);
        var perPiece = typedPerPiece ?? fallbackPerPiece ?? part.LastPurchasePrice ?? part.AverageCost;
        if (row is null)
            return new FilledLine(part.Id, part.UnitOfMeasure, 1, pieces, perPiece, 0, null, part.LeadTimeDays, false);
        var packs = PackMath.Packs(pieces, row.PackSize, row.MinPacks);
        var catalogue = typedPerPiece is null && row.PackPrice is { } price && string.Equals(row.Currency, orderCurrency, StringComparison.OrdinalIgnoreCase);
        return catalogue
            ? new FilledLine(part.Id, row.PackName, row.PackSize, packs, row.PackPrice!.Value, row.DiscountPercent, row.SupplierPartNumber, row.LeadTimeDays ?? part.LeadTimeDays, true)
            : new FilledLine(part.Id, row.PackName, row.PackSize, packs, Math.Round(perPiece * row.PackSize, 4), 0, row.SupplierPartNumber, row.LeadTimeDays ?? part.LeadTimeDays, false);
    }

    /// <summary>The order date plus the longest lead time of its lines; none without lead times.</summary>
    internal static DateOnly? ExpectedFrom(DateOnly orderDate, IEnumerable<int?> leadTimes)
    {
        var longest = leadTimes.Where(d => d is > 0).Select(d => d!.Value).DefaultIfEmpty(0).Max();
        return longest > 0 ? orderDate.AddDays(longest) : null;
    }

    /// <summary>
    /// The star (0.18.0). Left out (older pages, scripts) it changes nothing; true stars this row; only an explicit false clears the
    /// part's preferred supplier, and only when it is this row's (0.29.1).
    /// </summary>
    private static void Prefer(Part part, SupplierPart row, bool? preferred)
    {
        if (preferred == true) part.PreferredSupplierId = row.SupplierId;
        else if (preferred == false && part.PreferredSupplierId == row.SupplierId) part.PreferredSupplierId = null;
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator Rules(SupplierPartInput i) => new Validator().When(i.PackSize <= 0, "packSize", "Pieces per pack must be more than 0.")
            .When(i.PackPrice < 0, "packPrice", "The price can't be below 0.").When(i.DiscountPercent is < 0 or > 100, "discountPercent", "Discount must be between 0 and 100%.")
            .When(i.MinPacks < 1, "minPacks", "Minimum order must be at least 1 pack.").When(i.Currency.Clean() is { Length: not 3 }, "currency", "Use a 3-letter currency code.")
            .When(i.LeadTimeDays is < 0 or > 3650, "leadTimeDays", "Lead time must be between 0 and 3650 days.")
            .MaxLength("supplierPartNumber", i.SupplierPartNumber, "Supplier's part number", 100).MaxLength("packName", i.PackName, "Pack", 100).MaxLength("notes", i.Notes, "Notes", 1000);

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, Part part, SupplierPartInput i, SupplierPart? existing, CancellationToken ct)
    {
        var v = Rules(i);
        if (existing is null || existing.SupplierId != i.SupplierId)
        {
            if (!await db.Suppliers.AnyAsync(s => s.Id == i.SupplierId && s.IsSupplier && s.IsActive, ct)) v.Add("supplierId", "Choose a company with the Supplier role.");
            else if (await db.SupplierParts.AnyAsync(r => r.PartId == part.Id && r.SupplierId == i.SupplierId, ct)) v.Add("supplierId", "This supplier is already in the part's catalogue.");
        }
        return v;
    }

    private static async Task ApplyAsync(CmmsDbContext db, Part part, SupplierPart row, SupplierPartInput i, CancellationToken ct)
    {
        if (row.SupplierId != i.SupplierId || string.IsNullOrEmpty(row.Currency) || i.Currency.Clean() is not null)
        {
            var company = await db.Suppliers.AsNoTracking().Where(s => s.Id == i.SupplierId).Select(s => s.Currency).FirstOrDefaultAsync(ct);
            row.Currency = i.Currency.Clean()?.ToUpperInvariant() ?? (row.SupplierId == i.SupplierId && !string.IsNullOrEmpty(row.Currency) ? row.Currency : null)
                           ?? company.Clean()?.ToUpperInvariant() ?? await BaseCurrencyAsync(db, ct);
        }
        row.SupplierId = i.SupplierId; row.SupplierPartNumber = i.SupplierPartNumber.Clean(); row.PackName = i.PackName.Clean() ?? part.UnitOfMeasure;
        row.PackSize = i.PackSize; row.PackPrice = i.PackPrice; row.DiscountPercent = i.DiscountPercent; row.LeadTimeDays = i.LeadTimeDays; row.MinPacks = i.MinPacks;
        row.Notes = i.Notes.Clean(); row.IsActive = i.IsActive;
    }
}
