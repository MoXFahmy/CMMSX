using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Symptom, cause and remedy codes on requests and work orders (0.14.0).</summary>
internal static class Codes
{
    public const string UnknownSymptom = "Unknown symptom.";
    public const string UnknownCause = "Unknown cause.";
    public const string UnknownRemedy = "Unknown remedy.";
    /// <summary>How many causes per symptom (and remedies per cause) smart ordering keeps.</summary>
    public const int PairsPerCode = 20;

    public sealed record CodePair(Guid First, Guid Second, int Count);
    public sealed record CodePairs(List<CodePair> SymptomCause, List<CodePair> CauseRemedy);

    /// <summary>Smart ordering (0.14.0): <c>GET /api/code-pairs</c>, from the work the person can see that was completed in the last two years.</summary>
    public static void MapCodeEndpoints(this RouteGroupBuilder api) =>
        api.MapGet("/code-pairs", (CmmsDbContext db, CancellationToken ct) => PairsAsync(db, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-2), ct))
            .WithTags("Work orders").RequirePermission(Permissions.WorkOrdersView);

    /// <summary>
    /// The symptom–cause and cause–remedy pairs of work completed since <paramref name="since"/> (the site filter applies), most used
    /// first, at most <see cref="PairsPerCode"/> per first code.
    /// </summary>
    public static async Task<CodePairs> PairsAsync(CmmsDbContext db, DateOnly since, CancellationToken ct)
    {
        var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var done = db.WorkOrders.AsNoTracking().Where(w => w.CompletedAt >= from && (w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed));
        var symptomCause = await done.Where(w => w.SymptomId != null && w.FailureCodeId != null).GroupBy(w => new { F = w.SymptomId!.Value, S = w.FailureCodeId!.Value })
            .Select(g => new CodePair(g.Key.F, g.Key.S, g.Count())).ToListAsync(ct);
        var causeRemedy = await done.Where(w => w.FailureCodeId != null && w.RemedyId != null).GroupBy(w => new { F = w.FailureCodeId!.Value, S = w.RemedyId!.Value })
            .Select(g => new CodePair(g.Key.F, g.Key.S, g.Count())).ToListAsync(ct);
        return new CodePairs(Top(symptomCause), Top(causeRemedy));
    }

    private static List<CodePair> Top(List<CodePair> all) => all.GroupBy(p => p.First)
        .SelectMany(g => g.OrderByDescending(p => p.Count).ThenBy(p => p.Second).Take(PairsPerCode))
        .OrderBy(p => p.First).ThenByDescending(p => p.Count).ToList();

    /// <summary>
    /// Null when the code can be set: none chosen, the one the record already has (even if made inactive since), or an active code —
    /// any code of the organization when <paramref name="anyState"/> (a phone's list can be older than a code's deactivation);
    /// otherwise <paramref name="message"/>.
    /// </summary>
    public static async Task<string?> CheckAsync<T>(DbSet<T> set, Guid? newId, Guid? currentId, string message, CancellationToken ct, bool anyState = false)
        where T : TenantEntity, ICodeLookup =>
        newId is not { } id || id == currentId || await set.AnyAsync(x => x.Id == id && (anyState || x.IsActive), ct) ? null : message;
}
