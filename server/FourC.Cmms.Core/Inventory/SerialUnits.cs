using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Inventory;

/// <summary>What happened to a serial unit (0.24.0).</summary>
public enum UnitMovementKind { Received = 1, Registered = 2, Fitted = 3, Removed = 4, SentForRepair = 5, BackFromRepair = 6, Scrapped = 7, Transferred = 8 }

/// <summary>Where a removed unit goes.</summary>
public enum UnitOutcome { Store = 1, Repair = 2, Scrap = 3 }

/// <summary>Where a unit is, read from its asset record.</summary>
public enum UnitState { InStore = 1, Fitted = 2, AtRepair = 3, Scrapped = 4 }

/// <summary>
/// One step in a serial unit's life (0.24.0): received, fitted, removed, sent for repair and so on, with where and on which
/// work order or purchase order. Written in the same save as the change; never changed afterwards.
/// </summary>
public class UnitMovement : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    /// <summary>The unit: an asset with a part.</summary>
    public Guid UnitId { get; set; }
    public UnitMovementKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? StoreId { get; set; }
    /// <summary>The asset it was fitted to or removed from.</summary>
    public Guid? AssetId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public UnitOutcome? Outcome { get; set; }
    public string? Note { get; set; }
    /// <summary>A repair's cost, in the organization's currency (back from repair).</summary>
    public decimal? Cost { get; set; }
    public Guid? ByUserId { get; set; }
    public string? ByName { get; set; }
}

/// <summary>Serial units' rules that need no database.</summary>
public static class UnitRules
{
    private static readonly HashSet<string> HoursUnits = new(StringComparer.OrdinalIgnoreCase) { "h", "hr", "hrs", "hour", "hours", "ساعة", "ساعات" };

    public static UnitState StateOf(Asset unit) => unit.Status switch
    {
        AssetStatus.Disposed => UnitState.Scrapped,
        AssetStatus.UnderRepair => UnitState.AtRepair,
        _ when unit.ParentAssetId is not null => UnitState.Fitted,
        _ => UnitState.InStore,
    };

    /// <summary>A meter counting hours, which a fitted unit's running hours follow.</summary>
    public static bool IsHoursUnit(string? meterUnit) => meterUnit is not null && HoursUnits.Contains(meterUnit.Trim());

    /// <summary>The end of a unit's operating life: <paramref name="from"/> plus the part's life in months.</summary>
    public static DateOnly? EndOfLife(DateOnly from, int? months) => months is > 0 ? from.AddMonths(months.Value) : null;

    /// <summary>A part's unit life, when given: 1 to 600 months.</summary>
    public static void CheckLife(int? months)
    {
        if (months is { } m && (m < 1 || m > 600)) throw new DomainException("Enter the life in months, from 1 to 600.");
    }

    /// <summary>Serial numbers as typed: trimmed, blanks dropped, case and order kept.</summary>
    public static IReadOnlyList<string> CleanSerials(IEnumerable<string?> raw) =>
        raw.Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).ToList();
}
