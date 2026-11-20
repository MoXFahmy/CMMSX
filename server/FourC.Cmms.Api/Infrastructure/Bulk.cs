namespace FourC.Cmms.Api.Infrastructure;

/// <summary>A row a bulk action left alone, and why.</summary>
public sealed record BulkSkip(Guid Id, string Number, string Reason);

/// <summary>The answer of every bulk action: how many rows changed and which were skipped.</summary>
public sealed record BulkResult(int Updated, List<BulkSkip> Skipped);

/// <summary>{ ids, active } — switch many records on or off.</summary>
public sealed record BulkActiveInput(Guid[]? Ids, bool Active);

public static class Bulk
{
    public const int MaxRows = 500;

    /// <summary>The distinct ids of a bulk request; throws when there are none or more than <see cref="MaxRows"/>.</summary>
    public static List<Guid> Ids(Guid[]? ids, string what)
    {
        var list = (ids ?? []).Distinct().ToList();
        if (list.Count is 0 or > MaxRows) throw new FourC.Cmms.Core.Common.DomainException($"Select between 1 and {MaxRows} {what}.");
        return list;
    }
}
