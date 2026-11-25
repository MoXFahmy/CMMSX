using System.Linq.Expressions;
using System.Reflection;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Sign-ups never confirmed (0.29.0) are removed after <see cref="Keep"/>: every row their organization has, in every table, then the
/// organization itself. The address is then free to sign up again, and the old link fails like any used one.
/// </summary>
public static class PendingSignUps
{
    /// <summary>How long an organization waits for its owner's e-mail link before it is removed.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    /// <summary>
    /// Removes each organization whose sign-up has been pending since before <paramref name="now"/> minus <see cref="Keep"/>, each in one
    /// transaction of its own, and writes one <c>PendingSignUpRemoved</c> line per organization (its id only: no name, no address).
    /// One that can't be removed doesn't stop the others; the failures are thrown together at the end. Returns how many were removed.
    /// </summary>
    public static async Task<int> PurgeAsync(CmmsDbContext db, DateTimeOffset now, CancellationToken ct, ILogger? log = null)
    {
        var cutoff = now - Keep;
        var due = await db.Tenants.AsNoTracking().Where(t => t.SignUpPendingSince != null && t.SignUpPendingSince < cutoff)
            .OrderBy(t => t.SignUpPendingSince).Select(t => t.Id).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var steps = Steps(db.Model);
        var removed = 0;
        List<Exception>? failed = null;
        foreach (var id in due)
        {
            try
            {
                if (!await RemoveAsync(db, steps, id, cutoff, ct)) continue;
                removed++;
                log?.LogInformation("PendingSignUpRemoved {TenantId}", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failed ??= []).Add(new InvalidOperationException($"Couldn't remove unconfirmed organization {id}.", ex));
            }
        }
        if (failed is not null) throw new AggregateException($"{failed.Count} unconfirmed organization(s) couldn't be removed; {removed} were.", failed);
        return removed;
    }

    /// <summary>
    /// One organization, in one transaction. Its row is claimed first, still pending and still old enough (so one confirmed meanwhile is
    /// left alone); that write holds the row to the end, so a link used at the same moment either confirmed it first or finds it gone.
    /// </summary>
    private static Task<bool> RemoveAsync(CmmsDbContext db, IReadOnlyList<Delete> steps, Guid tenantId, DateTimeOffset cutoff, CancellationToken ct) =>
        db.InTransactionAsync(async tk =>
        {
            var claimed = await db.Tenants
                .Where(t => t.Id == tenantId && t.SignUpPendingSince != null && t.SignUpPendingSince < cutoff)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SignUpTokenHash, (string?)null), tk);
            if (claimed == 0) return false;
            foreach (var delete in steps) await delete(db, tenantId, tk);
            await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(tk);
            return true;
        }, ct);

    /// <summary>One table's delete for one organization.</summary>
    private delegate Task Delete(CmmsDbContext db, Guid tenantId, CancellationToken ct);

    private delegate Task DeleteLinked(CmmsDbContext db, Guid tenantId, (string Name, Type Type)[] dependent, (string Name, Type Type)[] principal, CancellationToken ct);

    /// <summary>
    /// The tables an organization's rows live in, from the model itself, in an order that deletes rows before the rows they point at:
    /// every table with a TenantId (people included), and every table without one whose rows hang off them (a person's roles and
    /// sign-in records, an order's lines, a schedule's assets...). A table whose rows point at rows of the same table (locations,
    /// assets and cost centres in trees) is deleted in one statement, which the database checks as a whole.
    /// </summary>
    private static List<Delete> Steps(IModel model)
    {
        var tables = model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned() && t.GetTableName() is not null && t.FindPrimaryKey() is not null).ToList();
        var owned = tables.Where(t => t.FindProperty(TenantId) is not null).ToHashSet();
        foreach (var t in owned)
            if (t.FindProperty(TenantId)!.ClrType != typeof(Guid))
                throw new InvalidOperationException($"{t.DisplayName()}.TenantId isn't a Guid; unconfirmed sign-ups can't be removed safely.");

        // Tables without a TenantId whose rows point at an organization's rows (directly, or through another such table).
        var children = new HashSet<IEntityType>();
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var t in tables.Where(t => !owned.Contains(t) && !children.Contains(t)))
                if (t.GetForeignKeys().Any(fk => owned.Contains(fk.PrincipalEntityType.GetRootType()) || children.Contains(fk.PrincipalEntityType.GetRootType())))
                    grew |= children.Add(t);
        }

        var steps = new List<Delete>();
        foreach (var t in Ordered(owned.Concat(children).ToHashSet()))
        {
            if (owned.Contains(t))
            {
                steps.Add(DeleteOwnedMethod.MakeGenericMethod(t.ClrType).CreateDelegate<Delete>());
                continue;
            }
            // Found through each of its links to a table with a TenantId: a row any of them places in the organization goes.
            var links = t.GetForeignKeys().Where(fk => owned.Contains(fk.PrincipalEntityType.GetRootType())).ToList();
            if (links.Count == 0)
                throw new InvalidOperationException($"{t.DisplayName()} only points at tables without a TenantId; unconfirmed sign-ups can't be removed safely.");
            foreach (var fk in links)
            {
                var delete = DeleteLinkedMethod.MakeGenericMethod(t.ClrType, fk.PrincipalEntityType.GetRootType().ClrType).CreateDelegate<DeleteLinked>();
                var dependent = fk.Properties.Select(p => (p.Name, p.ClrType)).ToArray();
                var principal = fk.PrincipalKey.Properties.Select(p => (p.Name, p.ClrType)).ToArray();
                steps.Add((db, id, ct) => delete(db, id, dependent, principal, ct));
            }
        }
        return steps;
    }

    /// <summary>Dependents before the rows they point at: Kahn's sort over the model's foreign keys, by name within a round for a fixed order.</summary>
    private static List<IEntityType> Ordered(HashSet<IEntityType> nodes)
    {
        // pointedAtBy[p]: the tables whose rows point at p's, which go first.
        var pointedAtBy = nodes.ToDictionary(n => n, _ => new HashSet<IEntityType>());
        foreach (var d in nodes)
            foreach (var fk in d.GetForeignKeys())
                if (fk.PrincipalEntityType.GetRootType() is var p && p != d && nodes.Contains(p))
                    pointedAtBy[p].Add(d);
        var order = new List<IEntityType>();
        var left = nodes.ToHashSet();
        while (left.Count > 0)
        {
            var ready = left.Where(n => !pointedAtBy[n].Overlaps(left)).OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
            if (ready.Count == 0)
                throw new InvalidOperationException("These tables point at each other in a loop, so unconfirmed sign-ups can't be removed safely: "
                    + string.Join(", ", left.Select(n => n.DisplayName()).Order(StringComparer.Ordinal)));
            foreach (var n in ready) { order.Add(n); left.Remove(n); }
        }
        return order;
    }

    private const string TenantId = "TenantId";

    private static readonly MethodInfo DeleteOwnedMethod = typeof(PendingSignUps).GetMethod(nameof(DeleteOwnedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo DeleteLinkedMethod = typeof(PendingSignUps).GetMethod(nameof(DeleteLinkedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The organization's rows of a table with a TenantId, deleted ones included. The only place that reads past the filters, and only
    /// ever for the one organization being removed.
    /// </summary>
    private static Task DeleteOwnedAsync<T>(CmmsDbContext db, Guid tenantId, CancellationToken ct) where T : class =>
        db.Set<T>().IgnoreQueryFilters().Where(e => EF.Property<Guid>(e, TenantId) == tenantId).ExecuteDeleteAsync(ct);

    /// <summary>Rows of a table without a TenantId that point, through one foreign key, at the organization's rows of <typeparamref name="TParent"/>.</summary>
    private static Task DeleteLinkedAsync<TChild, TParent>(CmmsDbContext db, Guid tenantId, (string Name, Type Type)[] dependent, (string Name, Type Type)[] principal,
        CancellationToken ct) where TChild : class where TParent : class
    {
        var parents = db.Set<TParent>().IgnoreQueryFilters().Where(p => EF.Property<Guid>(p, TenantId) == tenantId);
        // c => parents.Any(p => p.Key1 == c.Fk1 && ...)
        var c = Expression.Parameter(typeof(TChild), "c");
        var p = Expression.Parameter(typeof(TParent), "p");
        Expression? match = null;
        for (var i = 0; i < dependent.Length; i++)
        {
            var (key, fk) = Lift(Property(p, principal[i]), Property(c, dependent[i]));
            var equal = Expression.Equal(key, fk);
            match = match is null ? equal : Expression.AndAlso(match, equal);
        }
        Expression<Func<IQueryable<TParent>>> source = () => parents;
        var any = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(TParent)], source.Body,
            Expression.Quote(Expression.Lambda<Func<TParent, bool>>(match!, p)));
        return db.Set<TChild>().IgnoreQueryFilters().Where(Expression.Lambda<Func<TChild, bool>>(any, c)).ExecuteDeleteAsync(ct);
    }

    private static MethodCallExpression Property(ParameterExpression e, (string Name, Type Type) prop) =>
        Expression.Call(typeof(EF), nameof(EF.Property), [prop.Type], e, Expression.Constant(prop.Name));

    /// <summary>A key and a nullable foreign key compare as nullable.</summary>
    private static (Expression, Expression) Lift(Expression a, Expression b)
    {
        if (a.Type == b.Type) return (a, b);
        if (Nullable.GetUnderlyingType(b.Type) == a.Type) return (Expression.Convert(a, b.Type), b);
        if (Nullable.GetUnderlyingType(a.Type) == b.Type) return (a, Expression.Convert(b, a.Type));
        throw new InvalidOperationException($"Can't compare a {a.Type.Name} key with a {b.Type.Name} foreign key.");
    }
}
