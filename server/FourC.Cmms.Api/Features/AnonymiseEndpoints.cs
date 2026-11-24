using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Anonymising a person who has left (0.25.0): their name is replaced wherever it was copied, their contact details are
/// cleared and their sign-in closed for good; their history, hours and costs stay.
/// </summary>
public static class AnonymiseEndpoints
{
    public sealed record AnonymiseInput(Guid? EmployeeId, Guid? UserId, string? Confirm);

    public const string Inactive = "Make the person inactive first.";
    public const string SignInActive = "Make their sign-in inactive first.";
    public const string Already = "This person is already anonymised.";
    /// <summary>An anonymised sign-in stays closed: it can't be edited or given a password again.</summary>
    public const string Closed = "This sign-in was anonymised and can't be used again.";
    /// <summary>Why an e-mail waiting to go to an anonymised person was never sent.</summary>
    public const string AnonymisedMail = "Not sent: the person was anonymised.";

    /// <summary>
    /// Who is being anonymised: an employee (with their sign-in, if linked) or a sign-in with no employee record. The names
    /// and e-mails are every form they were known by, trimmed.
    /// </summary>
    private sealed record Person(Employee? Employee, AppUser? User, Guid TenantId, string Placeholder, string ConfirmWith, List<string> Names, List<string> Emails)
    {
        public Guid? UserId => User?.Id;
        public string NewEmail => $"anonymised-{User!.Id}@invalid";
    }

    public static void MapAnonymiseEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/anonymise").WithTags("Users").RequirePermission(Permissions.UsersManage);

        g.MapGet("/preview", async (Guid? employeeId, Guid? userId, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
        {
            if (await FindAsync(db, employeeId, userId, ct) is not { } p) return Results.NotFound();
            await InsideAsync(http, db, p, ct);
            var same = await SameNameAsync(db, p, ct);
            var records = Blocked(p) is null ? await RunAsync(db, p, same.Count > 0, apply: false, ct) : 0;
            return Results.Ok(new { p.Placeholder, p.ConfirmWith, records, sameNameAs = same, blocked = Blocked(p) is { } b ? Translator.T(b) : null });
        });

        g.MapPost("/", async (AnonymiseInput input, HttpContext http, CmmsDbContext db, IMemoryCache cache, CancellationToken ct) =>
        {
            if (await FindAsync(db, input.EmployeeId, input.UserId, ct) is not { } p) return Results.NotFound();
            await InsideAsync(http, db, p, ct);
            if (Blocked(p) is { } blocked) return new Validator().When(true, "confirm", blocked).Problem();
            var typed = input.Confirm?.Trim() ?? "";
            if (!typed.Equals(p.ConfirmWith, StringComparison.OrdinalIgnoreCase) && !typed.Equals(p.Placeholder, StringComparison.OrdinalIgnoreCase))
                return new Validator().When(true, "confirm", Translator.T($"Type {p.ConfirmWith} to confirm.")).Problem();
            var clash = (await SameNameAsync(db, p, ct)).Count > 0;
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await RunAsync(db, p, clash, apply: true, ct);
                await CloseAsync(db, cache, p, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
            return Results.Ok(new { p.Placeholder });
        });
    }

    /// <summary>Like every other change to a person's sign-in: only someone whose access covers theirs (a site-limited manager can't).</summary>
    private static async Task InsideAsync(HttpContext http, CmmsDbContext db, Person p, CancellationToken ct)
    {
        if (p.User is { IsServiceAccount: true }) throw new DomainException(ServiceAccountEndpoints.NotPeople);
        if (p.User is not null && await UserAccessRules.OutsideAsync(db, Access.Of(http) ?? UserAccess.Everything, p.User, ct) is { } outside)
            throw new DomainException(outside);
    }

    private static async Task<Person?> FindAsync(CmmsDbContext db, Guid? employeeId, Guid? userId, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId!.Value;
        Employee? e = null;
        if (employeeId is { } eid && (e = await db.Employees.FirstOrDefaultAsync(x => x.Id == eid, ct)) is null) return null;
        // A sign-in that belongs to an employee is anonymised as that employee.
        if (e is null && userId is { } linked) e = await db.Employees.FirstOrDefaultAsync(x => x.UserId == linked, ct);
        // A named employee brings only their own linked sign-in; a sign-in sent with them is ignored (0.26.1).
        var uid = employeeId is not null ? e!.UserId : e?.UserId ?? userId;
        var u = uid is { } id ? await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct) : null;
        if (e is null && u is null) return null;
        var arabic = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct)).Settings.DefaultLanguage == Languages.Arabic;
        var code = e?.Code.Trim() ?? u!.Id.ToString("N")[..6];
        var placeholder = e is not null ? (arabic ? $"موظف سابق {code}" : $"Former employee {code}") : (arabic ? $"مستخدم سابق {code}" : $"Former user {code}");
        var names = new[] { e?.FullName, u?.FullName }.Select(n => n?.Trim()).Where(n => !string.IsNullOrEmpty(n) && n != placeholder)
            .Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        var emails = new[] { e?.Email, u?.Email }.Select(n => n?.Trim()).Where(n => !string.IsNullOrEmpty(n) && !n.EndsWith("@invalid", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        return new Person(e, u, tenantId, placeholder, code, names, emails);
    }

    private static string? Blocked(Person p) =>
        (p.Employee?.AnonymisedAt ?? (p.Employee is null ? p.User?.AnonymisedAt : null)) is not null ? Already
        : p.Employee is { IsActive: true } || (p.Employee is null && p.User is { IsActive: true }) ? Inactive
        : p.User is { IsActive: true } ? SignInActive
        : null;

    /// <summary>Other people, current or former, known by the same name (ignoring case and spaces).</summary>
    private static async Task<List<string>> SameNameAsync(CmmsDbContext db, Person p, CancellationToken ct)
    {
        if (p.Names.Count == 0) return [];
        var names = p.Names;
        var employeeId = p.Employee?.Id;
        var userId = p.UserId;
        var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == p.TenantId && e.Id != employeeId && (userId == null || e.UserId != userId) && names.Contains(e.FullName.Trim()))
            .Select(e => e.FullName.Trim() + " (" + e.Code + ")").ToListAsync(ct);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == p.TenantId && u.Id != userId && names.Contains(u.FullName.Trim()))
            .Select(u => u.FullName.Trim() + " (" + u.Email + ")").ToListAsync(ct);
        return [.. employees.Concat(users).Distinct().Order()];
    }

    /// <summary>
    /// Replaces the name everywhere it was copied, and inside the activity log; with <paramref name="apply"/> false only counts
    /// the rows that would change. Names kept only as text are left when someone else has the same name.
    /// </summary>
    private static async Task<int> RunAsync(CmmsDbContext db, Person p, bool clash, bool apply, CancellationToken ct)
    {
        var total = 0;
        var linked = new HashSet<(string Type, Guid Id)>();
        var column = typeof(AnonymiseEndpoints).GetMethod(nameof(ColumnAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (entity, property, idProperty) in NameColumns.All)
        {
            var task = (Task<(int Count, List<Guid> ById)>)column.MakeGenericMethod(entity)
                .Invoke(null, [db, p.TenantId, property, idProperty, p.UserId, clash ? new List<string>() : p.Names, p.Placeholder, apply, ct])!;
            var (count, byId) = await task;
            total += count;
            foreach (var id in byId) linked.Add((entity.Name, id));
        }
        // A signature's caption repeats the signer's name ("Signature — Karim Saad").
        if (!clash)
        {
            var captions = p.Names.Select(n => $"Signature — {n}").ToList();
            var signatures = db.Attachments.IgnoreQueryFilters().Where(a => a.TenantId == p.TenantId && a.IsSignature && captions.Contains(a.Description!));
            total += apply ? await signatures.ExecuteUpdateAsync(s => s.SetProperty(a => a.Description, $"Signature — {p.Placeholder}"), ct) : await signatures.CountAsync(ct);
        }
        // A request typed by the person with their own e-mail address.
        if (p.Emails.Count > 0)
        {
            var emails = p.Emails;
            var requests = db.WorkRequests.IgnoreQueryFilters().Where(r => r.TenantId == p.TenantId && emails.Contains(r.RequesterEmail!.Trim()));
            total += apply ? await requests.ExecuteUpdateAsync(s => s.SetProperty(r => r.RequesterEmail, (string?)null).SetProperty(r => r.RequesterPhone, (string?)null), ct)
                : await requests.CountAsync(ct);
        }
        // The e-mail log: rows sent to their address, or to their name when no one else shares it (0.26.1). Subjects and bodies stay.
        total += await MailAsync(db, p, clash, apply, ct);
        total += await LogAsync(db, p, clash, linked, apply, ct);
        return total;
    }

    private static async Task<int> MailAsync(CmmsDbContext db, Person p, bool clash, bool apply, CancellationToken ct)
    {
        var emails = p.Emails;
        var names = clash ? [] : p.Names;
        if (emails.Count == 0 && names.Count == 0) return 0;
        var address = p.User is not null ? p.NewEmail : $"anonymised-{p.Employee!.Id}@invalid";
        var rows = db.OutboundEmails.IgnoreQueryFilters().Where(m => m.TenantId == p.TenantId
            && (emails.Contains(m.ToAddress.Trim()) || (m.ToName != null && names.Contains(m.ToName.Trim()))));
        // Those not sent yet aren't sent to the placeholder address: they end as failed.
        var reason = Translator.T(AnonymisedMail);
        return apply ? await rows.ExecuteUpdateAsync(s => s.SetProperty(m => m.ToAddress, address).SetProperty(m => m.ToName, p.Placeholder)
                .SetProperty(m => m.Status, m => m.Status == EmailStatus.Pending ? EmailStatus.Failed : m.Status)
                .SetProperty(m => m.LastError, m => m.Status == EmailStatus.Pending ? reason : m.LastError), ct)
            : await rows.CountAsync(ct);
    }

    /// <summary>One name column: rows carrying the person's (user) id, then, unless text is off, rows with no id whose text is their name.</summary>
    private static async Task<(int Count, List<Guid> ById)> ColumnAsync<T>(CmmsDbContext db, Guid tenantId, string property, string? idProperty, Guid? userId,
        List<string> names, string placeholder, bool apply, CancellationToken ct) where T : Entity
    {
        var rows = db.Set<T>().IgnoreQueryFilters().Where(x => EF.Property<Guid>(x, "TenantId") == tenantId);
        var count = 0;
        List<Guid> byId = [];
        if (idProperty is not null && userId is { } uid)
        {
            var mine = rows.Where(Id<T>(idProperty, uid));
            byId = await mine.Select(x => x.Id).ToListAsync(ct);
            count += apply ? await mine.ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<string>(x, property), placeholder), ct) : byId.Count;
        }
        var idType = idProperty is null ? null : typeof(T).GetProperty(idProperty)!.PropertyType;
        if (names.Count > 0 && (idType is null || Nullable.GetUnderlyingType(idType) is not null))
        {
            var text = rows.Where(x => names.Contains(EF.Property<string>(x, property).Trim()));
            if (idProperty is not null) text = text.Where(Id<T>(idProperty, null));
            count += apply ? await text.ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<string>(x, property), placeholder), ct) : await text.CountAsync(ct);
        }
        return (count, byId);
    }

    /// <summary>x => EF.Property&lt;Guid or Guid?&gt;(x, idProperty) == value (null: rows with no id).</summary>
    private static Expression<Func<T, bool>> Id<T>(string idProperty, Guid? value)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var type = typeof(T).GetProperty(idProperty)!.PropertyType;
        var read = Expression.Call(typeof(EF), nameof(EF.Property), [type], x, Expression.Constant(idProperty));
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(read, Expression.Constant(value, type)), x);
    }

    /// <summary>Personal values in a person's own employee and user records' entries.</summary>
    private static readonly HashSet<string> Personal = new(StringComparer.OrdinalIgnoreCase)
        { "FullName", "Email", "NormalizedEmail", "UserName", "NormalizedUserName", "Phone", "PhoneNumber", "JobTitle", "Notes" };

    /// <summary>
    /// The activity log: their name in the user column, every personal value in their own records' entries masked, and
    /// elsewhere any value equal to their name or e-mail replaced (names only where no one else shares it, or the entry is a
    /// row that carried their id).
    /// </summary>
    private static async Task<int> LogAsync(CmmsDbContext db, Person p, bool clash, HashSet<(string Type, Guid Id)> linked, bool apply, CancellationToken ct)
    {
        var count = 0;
        var log = db.AuditEntries.IgnoreQueryFilters().Where(a => a.TenantId == p.TenantId);
        if (p.UserId is { } uid)
        {
            var mine = log.Where(a => a.UserId == uid);
            count += apply ? await mine.ExecuteUpdateAsync(s => s.SetProperty(a => a.UserName, p.Placeholder), ct) : await mine.CountAsync(ct);
        }
        if (!clash && p.Names.Count > 0)
        {
            var names = p.Names;
            var unnamed = log.Where(a => a.UserId == null && names.Contains(a.UserName!.Trim()));
            count += apply ? await unnamed.ExecuteUpdateAsync(s => s.SetProperty(a => a.UserName, p.Placeholder), ct) : await unnamed.CountAsync(ct);
        }

        var employeeId = p.Employee?.Id ?? Guid.Empty;
        var userId = p.UserId ?? Guid.Empty;
        // Values are stored JSON-escaped (Arabic as \uXXXX), so both spellings are searched.
        var terms = p.Names.Concat(p.Emails).SelectMany(t => new[] { t, JsonSerializer.Serialize(t)[1..^1] }).Distinct().ToList();
        var candidates = log.Where(a => (a.EntityType == "Employee" && a.EntityId == employeeId) || (a.EntityType == "AppUser" && a.EntityId == userId));
        foreach (var term in terms) { var t = term; candidates = candidates.Union(log.Where(a => a.Changes != null && a.Changes.Contains(t))); }
        var entries = await candidates.Where(a => a.Changes != null).ToListAsync(ct);
        foreach (var entry in entries)
        {
            var own = (entry.EntityType == "Employee" && entry.EntityId == employeeId) || (entry.EntityType == "AppUser" && entry.EntityId == userId);
            var namesHere = !clash || linked.Contains((entry.EntityType, entry.EntityId)) || own;
            var rewritten = Rewrite(entry.Changes!, own, namesHere ? p.Names : [], p.Emails, p.Placeholder);
            if (rewritten == entry.Changes) continue;
            count++;
            if (apply) entry.Changes = rewritten;
        }
        if (!apply) db.ChangeTracker.Clear();
        return count;
    }

    /// <summary>The entry's changes with personal values masked (own records) and the name or e-mail replaced wherever a value equals one.</summary>
    internal static string Rewrite(string changes, bool own, IReadOnlyCollection<string> names, IReadOnlyCollection<string> emails, string placeholder)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(changes); } catch (JsonException) { return changes; }
        if (root is null) return changes;
        if (own && root is JsonObject fields)
            foreach (var (key, value) in fields.ToList())
                if (Personal.Contains(key) && value is JsonObject change)
                    foreach (var side in new[] { "from", "to" })
                        if (change[side] is JsonValue v && v.GetValueKind() != JsonValueKind.Null) change[side] = AuditScope.Masked;
        Replace(root, names, emails, placeholder);
        return JsonNode.DeepEquals(JsonNode.Parse(changes), root) ? changes : root.ToJsonString();
    }

    private static void Replace(JsonNode node, IReadOnlyCollection<string> names, IReadOnlyCollection<string> emails, string placeholder)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o.ToList())
                {
                    if (child is JsonValue v && v.TryGetValue<string>(out var s) && Swap(s, names, emails, placeholder) is { } swapped) o[key] = swapped;
                    else if (child is not null) Replace(child, names, emails, placeholder);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue<string>(out var s) && Swap(s, names, emails, placeholder) is { } swapped) a[i] = swapped;
                    else if (a[i] is { } child) Replace(child, names, emails, placeholder);
                }
                break;
        }
    }

    private static string? Swap(string value, IReadOnlyCollection<string> names, IReadOnlyCollection<string> emails, string placeholder)
    {
        var v = value.Trim();
        if (names.Any(n => v.Equals(n, StringComparison.OrdinalIgnoreCase))) return placeholder;
        if (names.Any(n => v.Equals($"Signature — {n}", StringComparison.OrdinalIgnoreCase))) return $"Signature — {placeholder}";
        if (emails.Any(e => v.Equals(e, StringComparison.OrdinalIgnoreCase))) return AuditScope.Masked;
        return null;
    }

    /// <summary>Clears the employee's contact details and closes the sign-in for good, then records "Anonymised" with no old values.</summary>
    private static async Task CloseAsync(CmmsDbContext db, IMemoryCache cache, Person p, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (p.Employee is { } e)
        {
            await db.Employees.IgnoreQueryFilters().Where(x => x.Id == e.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FullName, p.Placeholder).SetProperty(x => x.Email, (string?)null).SetProperty(x => x.Phone, (string?)null)
                .SetProperty(x => x.JobTitle, (string?)null).SetProperty(x => x.Notes, (string?)null).SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.AnonymisedAt, now).SetProperty(x => x.UpdatedAt, now), ct);
            AuditLog.Event(db, "Employee", e.Id, "Anonymised");
        }
        if (p.User is { } u)
        {
            var email = p.NewEmail;
            await db.Users.IgnoreQueryFilters().Where(x => x.Id == u.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FullName, p.Placeholder).SetProperty(x => x.Email, email).SetProperty(x => x.NormalizedEmail, email.ToUpperInvariant())
                .SetProperty(x => x.UserName, email).SetProperty(x => x.NormalizedUserName, email.ToUpperInvariant())
                .SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.PhoneNumber, (string?)null).SetProperty(x => x.JobTitle, (string?)null)
                .SetProperty(x => x.IsActive, false).SetProperty(x => x.SecurityStamp, Guid.NewGuid().ToString("N")).SetProperty(x => x.AnonymisedAt, now), ct);
            await Sessions.RevokeAsync(db, cache, db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id), ct);
            AuditLog.Event(db, "AppUser", u.Id, "Anonymised");
        }
    }
}
