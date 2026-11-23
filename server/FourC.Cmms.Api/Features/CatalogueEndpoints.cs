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
        decimal DiscountPercent, int? LeadTimeDays, int MinPacks, string? Notes, bool IsActive = true, bool IsPreferred = false);

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
            await db.SaveChangesAsync(ct);
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
            await db.SaveChangesAsync(ct);
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
        var baseCurrency = await BaseCurrencyAsync(db, ct);
        var currency = company.Currency.Clean()?.ToUpperInvariant() ?? baseCurrency;
        var row = new SupplierPart
        {
            PartId = part.Id, SupplierId = supplierId, PackName = part.UnitOfMeasure, PackSize = 1, Currency = currency, LeadTimeDays = part.LeadTimeDays, MinPacks = 1,
            PackPrice = currency == baseCurrency ? part.LastPurchasePrice ?? part.StandardPrice : null,
        };
        db.SupplierParts.Add(row);
        return row;
    }

    private static void Prefer(Part part, SupplierPart row, bool preferred)
    {
        if (preferred) part.PreferredSupplierId = row.SupplierId;
        else if (part.PreferredSupplierId == row.SupplierId) part.PreferredSupplierId = null;
    }

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, Part part, SupplierPartInput i, SupplierPart? existing, CancellationToken ct)
    {
        var v = new Validator().When(i.PackSize <= 0, "packSize", "Pieces per pack must be more than 0.")
            .When(i.PackPrice < 0, "packPrice", "The price can't be below 0.").When(i.DiscountPercent is < 0 or > 100, "discountPercent", "Discount must be between 0 and 100%.")
            .When(i.MinPacks < 1, "minPacks", "Minimum order must be at least 1 pack.").When(i.Currency.Clean() is { Length: not 3 }, "currency", "Use a 3-letter currency code.")
            .When(i.LeadTimeDays is < 0 or > 3650, "leadTimeDays", "Lead time must be between 0 and 3650 days.")
            .MaxLength("supplierPartNumber", i.SupplierPartNumber, "Supplier's part number", 100).MaxLength("packName", i.PackName, "Pack", 100).MaxLength("notes", i.Notes, "Notes", 1000);
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
