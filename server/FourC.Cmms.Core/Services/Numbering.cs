using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;

namespace FourC.Cmms.Core.Services;

public enum NumberKind { Asset, WorkOrder, WorkRequest, PurchaseOrder, WorkGroup, PurchaseRequest, Contract }

/// <summary>Human-readable, per-tenant numbers (WO-00042). Skips past numbers that already exist (manual entry, imports).</summary>
public static class Numbering
{
    /// <summary>
    /// The sequence and prefix for a new number at <paramref name="site"/>: the site's own prefix counts in its own sequence
    /// ("WorkOrder:&lt;site id&gt;"); otherwise the organization's prefix and sequence.
    /// </summary>
    /// <remarks>
    /// With <see cref="Tenancy.TenantSettings.RestartNumbersYearly"/> (0.21.0), every kind but assets counts in a sequence per year
    /// and carries the year: "WO-2027-00001". The year is the organization's (its time zone) at <paramref name="now"/>.
    /// </remarks>
    public static (string Sequence, string Prefix) For(Tenancy.TenantSettings org, Locations.Site? site, NumberKind kind, DateTimeOffset? now = null)
    {
        var (sequence, orgPrefix, sitePrefix) = kind switch
        {
            NumberKind.Asset => ("Asset", org.AssetNumberPrefix, site?.Settings.AssetNumberPrefix),
            NumberKind.WorkOrder => ("WorkOrder", org.WorkOrderNumberPrefix, site?.Settings.WorkOrderNumberPrefix),
            NumberKind.WorkRequest => ("WorkRequest", org.WorkRequestNumberPrefix, site?.Settings.WorkRequestNumberPrefix),
            // Groups of work orders (0.12.0): one sequence and prefix for the organization.
            NumberKind.WorkGroup => ("WorkGroup", (string?)"GRP-", (string?)null),
            // Service contracts (0.17.0): one sequence and prefix for the organization.
            NumberKind.Contract => ("Contract", (string?)"CON-", (string?)null),
            NumberKind.PurchaseRequest => ("PurchaseRequest", org.PurchaseRequestNumberPrefix, site?.Settings.PurchaseRequestNumberPrefix),
            _ => ("PurchaseOrder", org.PurchaseOrderNumberPrefix, site?.Settings.PurchaseOrderNumberPrefix),
        };
        var (seq, prefix) = string.IsNullOrWhiteSpace(sitePrefix) ? (sequence, orgPrefix ?? "") : ($"{sequence}:{site!.Id:N}", sitePrefix.Trim());
        if (!org.RestartNumbersYearly || kind == NumberKind.Asset) return (seq, prefix);
        var year = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, Work.Sla.Zone(org.TimeZone)).Year;
        return ($"{seq}:{year}", $"{prefix}{year}-");
    }

    public static async Task<string> NextAsync(CmmsDbContext db, string sequence, string? prefix, int padding,
        Func<string, Task<bool>> exists, Func<string, Task<List<string>>> existingWithPrefix, CancellationToken ct = default)
    {
        prefix ??= "";
        var format = new string('0', Math.Clamp(padding, 1, 12));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = prefix + (await db.NextNumberAsync(sequence, ct)).ToString(format);
            if (!await exists(number)) return number;
            var max = (await existingWithPrefix(prefix)).Select(n => long.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
            await db.SetSequenceAtLeastAsync(sequence, max + 1, ct);
        }
        throw new DomainException($"Could not generate a unique number for {sequence}. Check the numbering settings.");
    }
}
