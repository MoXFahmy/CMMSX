using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Pm;

/// <summary>
/// A measurement point (0.13.0): what is measured (supply air temperature, motor current), its unit, the values that are possible
/// at all, and up to five levels — each alert/alarm level can name the work template its follow-up comes from. Organization-wide,
/// like work templates; it can list the assets it is measured on.
/// </summary>
public class MeasurementPoint : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public MeasurementKind Kind { get; set; } = MeasurementKind.Other;
    public string? Unit { get; set; }
    public int Decimals { get; set; } = 1;
    public decimal? ValidMin { get; set; }
    public decimal? ValidMax { get; set; }
    public decimal? AlarmLow { get; set; }
    public decimal? AlertLow { get; set; }
    public decimal? Target { get; set; }
    public decimal? AlertHigh { get; set; }
    public decimal? AlarmHigh { get; set; }
    public Guid? AlarmLowTemplateId { get; set; }
    public Guid? AlertLowTemplateId { get; set; }
    public Guid? AlertHighTemplateId { get; set; }
    public Guid? AlarmHighTemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MeasurementPointAsset> Assets { get; set; } = [];
}

/// <summary>An asset a measurement point is measured on (shown on that asset's page).</summary>
public class MeasurementPointAsset
{
    public Guid PointId { get; set; }
    public Guid AssetId { get; set; }
}

/// <summary>One value read for a measurement point, on a checklist step or entered directly, with the level it fell at.</summary>
public class MeasurementReading : TenantEntity
{
    public Guid PointId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid SiteId { get; set; }
    public decimal Value { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    public MeasureLevel Level { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? WorkOrderTaskId { get; set; }
    public string? Note { get; set; }
    public Guid? ById { get; set; }
    public string? ByName { get; set; }
    public Guid? FollowUpWorkOrderId { get; set; }
    public Guid? FollowUpRequestId { get; set; }
}
