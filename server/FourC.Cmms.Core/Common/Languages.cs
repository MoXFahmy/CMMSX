namespace FourC.Cmms.Core.Common;

/// <summary>The languages CMX speaks. A person's choice wins; without one, their organization's default.</summary>
public static class Languages
{
    public const string English = "en";
    public const string Arabic = "ar";
    public static readonly IReadOnlyList<string> Supported = [English, Arabic];

    public static bool IsSupported(string? code) => code is not null && Supported.Contains(code);

    /// <summary>The language to use: the person's choice, else the organization's, else English.</summary>
    public static string Effective(string? choice, string? organizationDefault) =>
        IsSupported(choice) ? choice! : IsSupported(organizationDefault) ? organizationDefault! : English;

    /// <summary>Right-to-left script.</summary>
    public static bool IsRightToLeft(string code) => code == Arabic;
}
