namespace FourC.Cmms.Api.Features.Ai;

/// <summary>
/// The AI assistant's server settings (section <c>Ai</c>, 0.15.0). One CMX key for every organization (cworks pays); the monthly
/// ceiling caps every organization's own limit. Prices are US dollars per million tokens, for the usage page's estimate.
/// </summary>
public sealed class AiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    public int MonthlyCeiling { get; set; } = 300;
    public decimal InputPrice { get; set; } = 4m;
    public decimal OutputPrice { get; set; } = 20m;
    public decimal CacheReadPrice { get; set; } = 0.2m;
    public decimal CacheWritePrice { get; set; } = 5m;
    /// <summary>One call's limit. CloudFront gives the server 60 seconds, so this stays well under it (history and lock queries come first).</summary>
    public int TimeoutSeconds { get; set; } = 45;
}
