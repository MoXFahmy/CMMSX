using FourC.Cmms.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Symptom, cause and remedy codes on requests and work orders (0.14.0).</summary>
internal static class Codes
{
    public const string UnknownSymptom = "Unknown symptom.";
    public const string UnknownCause = "Unknown cause.";
    public const string UnknownRemedy = "Unknown remedy.";

    /// <summary>
    /// Null when the code can be set: none chosen, the one the record already has (even if made inactive since), or an active code —
    /// any code of the organization when <paramref name="anyState"/> (a phone's list can be older than a code's deactivation);
    /// otherwise <paramref name="message"/>.
    /// </summary>
    public static async Task<string?> CheckAsync<T>(DbSet<T> set, Guid? newId, Guid? currentId, string message, CancellationToken ct, bool anyState = false)
        where T : TenantEntity, ICodeLookup =>
        newId is not { } id || id == currentId || await set.AnyAsync(x => x.Id == id && (anyState || x.IsActive), ct) ? null : message;
}
