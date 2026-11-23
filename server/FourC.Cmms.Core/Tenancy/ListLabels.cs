namespace FourC.Cmms.Core.Tenancy;

/// <summary>
/// An organization's own wording for one value of a fixed list (0.21.0): work types, priorities, asset statuses and criticality.
/// The meaning stays the built-in value; empty names fall back to the built-in words.
/// </summary>
public sealed class ListLabel
{
    public string? Name { get; set; }
    public string? NameAr { get; set; }
    /// <summary>A Mantine palette colour (gray, red, …); empty = the screen's own.</summary>
    public string? Color { get; set; }
    /// <summary>Place in pickers and filters; empty = after the ordered ones, in the built-in order.</summary>
    public int? Order { get; set; }
    /// <summary>Not offered for new choices; existing records keep it.</summary>
    public bool Hidden { get; set; }
}
