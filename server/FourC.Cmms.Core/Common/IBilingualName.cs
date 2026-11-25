namespace FourC.Cmms.Core.Common;

/// <summary>0.28.0: a code list whose names have an optional Arabic twin (symptoms, causes, remedies, cancel reasons).</summary>
public interface IBilingualName
{
    string? NameAr { get; set; }
}
