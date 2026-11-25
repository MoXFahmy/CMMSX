using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Saves that happen in more than one step (0.21.0-6, 0.29.1) as one transaction: all of it or none. Built on
/// <see cref="CmmsDbContext.InTransactionAsync{T}"/>, so the body may run again after a dropped connection; what a failed try
/// added to the change tracker is forgotten before it leaves, so the next try (or the caller) starts clean.
/// </summary>
public static class Atomic
{
    public static Task<T> RunAsync<T>(CmmsDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        // Already inside a transaction (a phone change that took a meter reading): that one covers it.
        if (db.Database.CurrentTransaction is not null) return work(ct);
        return db.InTransactionAsync(async tk =>
        {
            var known = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            try { return await work(tk); }
            catch
            {
                foreach (var e in db.ChangeTracker.Entries().Where(e => !known.Contains(e.Entity)).ToList()) e.State = EntityState.Detached;
                throw;
            }
        }, ct);
    }

    public static async Task RunAsync(CmmsDbContext db, Func<CancellationToken, Task> work, CancellationToken ct) =>
        await RunAsync(db, async tk => { await work(tk); return true; }, ct);
}
