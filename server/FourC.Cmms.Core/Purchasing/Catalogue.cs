using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Inventory;

namespace FourC.Cmms.Core.Purchasing;

/// <summary>
/// One supplier's terms for one part (0.18.0): their part number, the pack they sell (a name and how many of the part's own units it holds),
/// the price per pack in the row's own currency, discount, lead time and minimum order. Receipts record the last price paid, never the price.
/// </summary>
public class SupplierPart : TenantEntity
{
    public Guid PartId { get; set; }
    public Part? Part { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? SupplierPartNumber { get; set; }
    /// <summary>What the supplier sells, e.g. "Box of 100"; the part's own unit for a pack of 1.</summary>
    public required string PackName { get; set; }
    /// <summary>How many of the part's own units one pack holds (more than 0; decimals allowed).</summary>
    public decimal PackSize { get; set; } = 1;
    /// <summary>Price per pack in <see cref="Currency"/>; empty when not known.</summary>
    public decimal? PackPrice { get; set; }
    /// <summary>The row's own currency, set when it is made; a company changing currency later doesn't change what its prices mean.</summary>
    public required string Currency { get; set; }
    public decimal DiscountPercent { get; set; }
    public int? LeadTimeDays { get; set; }
    public int MinPacks { get; set; } = 1;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>What the last receipt paid per pack (before discount), its discount, currency and date.</summary>
    public decimal? LastPaidPrice { get; set; }
    public decimal? LastPaidDiscount { get; set; }
    public string? LastPaidCurrency { get; set; }
    public DateOnly? LastPaidOn { get; set; }
}

/// <summary>Pack arithmetic (0.18.0): orders in whole packs, stock in the part's own units.</summary>
public static class PackMath
{
    /// <summary>Whole packs for <paramref name="pieces"/>, rounded up, and never fewer than the minimum (at least one).</summary>
    public static decimal Packs(decimal pieces, decimal packSize, int minPacks)
    {
        var least = Math.Max(minPacks, 1);
        if (packSize <= 0) return Math.Max(Math.Ceiling(pieces), least);
        return Math.Max(Math.Ceiling(pieces / packSize), least);
    }

    /// <summary>The cost of one of the part's units: net price per pack × exchange rate ÷ pieces per pack (4 decimals).</summary>
    public static decimal CostPerPiece(decimal netPricePerUnit, decimal exchangeRate, decimal packSize) =>
        Math.Round(netPricePerUnit * exchangeRate / (packSize <= 0 ? 1 : packSize), 4);

    /// <summary>A price for one pack converted to another pack size, through the price of one piece (4 decimals).</summary>
    public static decimal PriceForPack(decimal price, decimal fromPackSize, decimal toPackSize) =>
        Math.Round(price / (fromPackSize <= 0 ? 1 : fromPackSize) * toPackSize, 4);
}
