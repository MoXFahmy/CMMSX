namespace FourC.Cmms.Core.Assets;

/// <summary>Asset cost rules (0.14.0); no database.</summary>
public static class CostMath
{
    /// <summary>An hour of downtime costs the asset's own figure, else its category's, else nothing is known.</summary>
    public static decimal? DowntimeCostPerHour(decimal? asset, decimal? category) => asset ?? category;
}
