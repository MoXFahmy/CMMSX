using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;
using static FourC.Cmms.Api.Features.Ai.AiText;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary><c>Skip</c> (0.29.2): texts the person left out or the AI couldn't draft; a filter only (at most <see cref="AiTranslate.MaxSkip"/> ids).</summary>
public sealed record TranslateDraftInput(string? List, List<Guid>? Skip = null);
public sealed record TranslateItem(Guid Id, string? Ar);
public sealed record TranslateSaveInput(string? List, string? Field, List<TranslateItem>? Items);
public sealed record TranslateDraftItem(Guid Id, string Text, string Draft);
/// <summary><c>CouldNotIds</c>: the texts of this batch with no draft; <c>SkippedLeft</c>: missing texts left out by the skip list.</summary>
public sealed record TranslateDraft(string? Field, List<TranslateDraftItem> Items, int CouldNot, int Remaining, List<Guid> CouldNotIds, int SkippedLeft);

/// <summary>
/// "Fill in missing Arabic" (0.28.0): Claude drafts the Arabic texts a list is missing, a batch at a time; a person checks them and saves.
/// Nothing is saved by the AI itself, and saving never overwrites an Arabic text that exists.
/// </summary>
public static class AiTranslate
{
    public const string Instructions =
        "You translate short texts from a maintenance management system into Arabic for maintenance technicians in Egypt. Use clear Modern Standard Arabic " +
        "with the technical words technicians use. Keep codes, numbers, units, model numbers, part numbers and brand names exactly as written. Translate each " +
        "item on its own; never add or drop information. Answer with one Arabic text for every input id.";
    public const string ContextHeader = "Existing translations in this organization (keep the same words):";
    public const string UnknownList = "Choose a list to translate.";
    public const string UnknownField = "Choose a text to save.";
    public const string TooManySkipped = "Too many texts to leave out.";
    public const int ContextPairs = 200;
    public const int MaxSkip = 2000;

    /// <summary>A new schema each time (a JSON node can belong to one request only).</summary>
    private static JsonObject Schema() => Obj(("items", Arr(Obj(("id", Str()), ("ar", Str())))));

    /// <summary>One text of a list that can have an Arabic twin.</summary>
    internal interface ISource
    {
        string Field { get; }
        int Batch { get; }
        int Max { get; }
        /// <summary>The next batch without the <paramref name="skip"/> ids; <c>Missing</c> counts the texts not skipped, <c>SkippedLeft</c> the skipped ones still missing.</summary>
        Task<(List<(Guid Id, string Text)> Items, int Missing, int SkippedLeft)> NextAsync(CmmsDbContext db, IReadOnlyCollection<Guid> skip, CancellationToken ct);
        Task<List<(string Main, string Ar)>> PairsAsync(CmmsDbContext db, CancellationToken ct);
        /// <summary>Sets the Arabic text of each record that has none yet; returns how many it set.</summary>
        Task<int> SaveAsync(CmmsDbContext db, IReadOnlyDictionary<Guid, string> texts, CancellationToken ct);
    }

    /// <summary>A text of a tenant record: main text, Arabic twin, and the order the list is drafted in.</summary>
    // ("key", not "field": inside a property, field is the property's own backing field since C# 14.)
    private sealed class Source<T>(string key, int batch, int max, Func<CmmsDbContext, IQueryable<T>> set, Expression<Func<T, string?>> main,
        Expression<Func<T, string?>> ar, Expression<Func<T, string>> order, Func<T, string?> getAr, Action<T, string> setAr) : ISource where T : TenantEntity
    {
        public string Field => key;
        public int Batch => batch;
        public int Max => max;

        private IQueryable<T> Missing(CmmsDbContext db) => set(db).Where(Both(main, ar, (m, a) =>
            Expression.AndAlso(Expression.AndAlso(Expression.NotEqual(m, Null), Expression.NotEqual(m, Empty)), Expression.Equal(a, Null))));

        public async Task<(List<(Guid Id, string Text)> Items, int Missing, int SkippedLeft)> NextAsync(CmmsDbContext db, IReadOnlyCollection<Guid> skip, CancellationToken ct)
        {
            // Only this organization's rows are in the query, so a foreign or unknown id in the skip list matches nothing.
            var all = Missing(db);
            var q = all.Where(x => !skip.Contains(x.Id));
            var rows = await q.OrderBy(order).Take(batch).Select(Pair(x => x.Id, main)).ToListAsync(ct);
            return (rows.Select(r => (r.Id, r.Text!)).ToList(), await q.CountAsync(ct), skip.Count == 0 ? 0 : await all.CountAsync(x => skip.Contains(x.Id), ct));
        }

        public async Task<List<(string Main, string Ar)>> PairsAsync(CmmsDbContext db, CancellationToken ct)
        {
            var q = set(db).Where(Both(main, ar, (m, a) => Expression.AndAlso(Expression.NotEqual(m, Null), Expression.NotEqual(a, Null))));
            var rows = await q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(ContextPairs).Select(Two(main, ar)).ToListAsync(ct);
            return rows.Select(r => (r.Main!, r.Ar!)).ToList();
        }

        public async Task<int> SaveAsync(CmmsDbContext db, IReadOnlyDictionary<Guid, string> texts, CancellationToken ct)
        {
            var ids = texts.Keys.ToList();
            var saved = 0;
            foreach (var x in await set(db).Where(x => ids.Contains(x.Id)).ToListAsync(ct))
                if (getAr(x) is null) { setAr(x, texts[x.Id]); saved++; }
            return saved;
        }
    }

    /// <summary>Template steps: no tenant of their own, so only the steps of templates the organization can see.</summary>
    private sealed class Steps : ISource
    {
        public string Field => "steps";
        public int Batch => 100;
        public int Max => 2000;

        private static IQueryable<Core.Pm.PmTaskStep> Visible(CmmsDbContext db) => db.PmTaskSteps.Where(s => db.PmTasks.Any(t => t.Id == s.PmTaskId));

        public async Task<(List<(Guid Id, string Text)> Items, int Missing, int SkippedLeft)> NextAsync(CmmsDbContext db, IReadOnlyCollection<Guid> skip, CancellationToken ct)
        {
            var all = Visible(db).Where(s => s.Description != "" && s.DescriptionAr == null);
            var q = all.Where(s => !skip.Contains(s.Id));
            var rows = await (from s in q join t in db.PmTasks on s.PmTaskId equals t.Id orderby t.Code, s.Sequence select new { s.Id, s.Description })
                .Take(Batch).ToListAsync(ct);
            return (rows.Select(r => (r.Id, r.Description)).ToList(), await q.CountAsync(ct), skip.Count == 0 ? 0 : await all.CountAsync(s => skip.Contains(s.Id), ct));
        }

        public async Task<List<(string Main, string Ar)>> PairsAsync(CmmsDbContext db, CancellationToken ct) =>
            // Steps carry no change time of their own: the templates' last change orders them, newest first (0.29.2).
            (await (from s in Visible(db).Where(s => s.DescriptionAr != null) join t in db.PmTasks on s.PmTaskId equals t.Id
                    orderby (t.UpdatedAt ?? t.CreatedAt) descending, s.Sequence select new { s.Description, s.DescriptionAr })
                .Take(ContextPairs).ToListAsync(ct)).Select(r => (r.Description, r.DescriptionAr!)).ToList();

        public async Task<int> SaveAsync(CmmsDbContext db, IReadOnlyDictionary<Guid, string> texts, CancellationToken ct)
        {
            var ids = texts.Keys.ToList();
            var saved = 0;
            foreach (var s in await Visible(db).Where(s => ids.Contains(s.Id)).ToListAsync(ct))
                if (s.DescriptionAr is null) { s.DescriptionAr = texts[s.Id]; saved++; }
            return saved;
        }
    }

    private static Source<T> Name<T>(Func<CmmsDbContext, IQueryable<T>> set, Expression<Func<T, string?>> main, Expression<Func<T, string?>> ar,
        Expression<Func<T, string>> order, Func<T, string?> get, Action<T, string> put, int max, string field = "name", int batch = 50) where T : TenantEntity =>
        new(field, batch, max, set, main, ar, order, get, put);

    /// <summary>The lists a page can draft, each with its texts in the order they are drafted and the permission that edits them.</summary>
    internal static readonly Dictionary<string, (string Permission, ISource[] Fields)> Lists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assets"] = (Permissions.AssetsEdit, [
            Name(db => db.Assets, x => x.Name, x => x.NameAr, x => x.AssetNumber, x => x.NameAr, (x, v) => x.NameAr = v, 300),
            Name(db => db.Assets, x => x.NotesToTechnician, x => x.NotesToTechnicianAr, x => x.AssetNumber, x => x.NotesToTechnicianAr, (x, v) => x.NotesToTechnicianAr = v, 4000, "notesToTechnician", 20)]),
        ["locations"] = (Permissions.LocationsManage, [
            Name(db => db.Locations, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 300),
            Name(db => db.Locations, x => x.NotesToTechnician, x => x.NotesToTechnicianAr, x => x.Code, x => x.NotesToTechnicianAr, (x, v) => x.NotesToTechnicianAr = v, 4000, "notesToTechnician", 20)]),
        ["parts"] = (Permissions.InventoryManage, [Name(db => db.Parts, x => x.Name, x => x.NameAr, x => x.PartNumber, x => x.NameAr, (x, v) => x.NameAr = v, 300)]),
        ["categories"] = (Permissions.SettingsManage, [Name(db => db.AssetCategories, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["symptoms"] = (Permissions.SettingsManage, [Name(db => db.Symptoms, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["causes"] = (Permissions.SettingsManage, [Name(db => db.FailureCodes, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["remedies"] = (Permissions.SettingsManage, [Name(db => db.Remedies, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["cancel-reasons"] = (Permissions.SettingsManage, [Name(db => db.CancelReasons, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["request-types"] = (Permissions.SettingsManage, [Name(db => db.RequestTypes, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 200)]),
        ["templates"] = (Permissions.PmManage, [
            Name(db => db.PmTasks, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 300),
            Name(db => db.PmTasks, x => x.Title, x => x.TitleAr, x => x.Code, x => x.TitleAr, (x, v) => x.TitleAr = v, 300, "title"),
            Name(db => db.PmTasks, x => x.SafetyNotes, x => x.SafetyNotesAr, x => x.Code, x => x.SafetyNotesAr, (x, v) => x.SafetyNotesAr = v, 4000, "safetyNotes", 20),
            new Steps()]),
        ["pm-schedules"] = (Permissions.PmManage, [Name(db => db.PmSchedules, x => x.Name, x => x.NameAr, x => x.Code, x => x.NameAr, (x, v) => x.NameAr = v, 300)]),
    };

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/translations").WithTags("AI assistant");
        g.MapPost("/draft", (TranslateDraftInput input, HttpContext http, CmmsDbContext db, AiService ai, PermissionService perms, CancellationToken ct) =>
            AiEndpoints.Guard(() => DraftAsync(input, http, db, ai, perms, ct))).RequireAuthorization();
        g.MapPost("/save", (TranslateSaveInput input, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct) =>
            SaveAsync(input, http, db, perms, ct)).RequireAuthorization();
    }

    private static async Task<IResult?> RefuseAsync(string? list, HttpContext http, PermissionService perms)
    {
        if (list is null || !Lists.TryGetValue(list, out var def)) return Results.Problem(title: Translator.T(UnknownList), statusCode: 400);
        var mine = await perms.GetAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return mine.Contains(def.Permission) ? null : Results.Forbid();
    }

    private static async Task<IResult> DraftAsync(TranslateDraftInput input, HttpContext http, CmmsDbContext db, AiService ai, PermissionService perms, CancellationToken ct)
    {
        if (await RefuseAsync(input.List, http, perms) is { } refused) return refused;
        if (input.Skip is { Count: > MaxSkip }) return Results.Problem(title: Translator.T(TooManySkipped), statusCode: 400);
        var skip = (input.Skip ?? []).Distinct().ToList();
        var skippedLeft = 0;
        foreach (var source in Lists[input.List!].Fields)
        {
            var (items, missing, left) = await source.NextAsync(db, skip, ct);
            skippedLeft += left;
            if (items.Count == 0) continue;
            var pairs = await source.PairsAsync(db, ct);
            var context = $"{ContextHeader}\n{Lines(pairs, p => $"{p.Main} ⇒ {p.Ar}")}";
            // Short ids ("1", "2"…) instead of GUIDs: fewer tokens in the answer; mapped back below.
            var keys = items.Select((i, n) => (Key: (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), i.Id)).ToDictionary(x => x.Key, x => x.Id);
            var prompt = JsonSerializer.Serialize(items.Select((i, n) => new { id = (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), text = i.Text }));
            var ask = new AiAsk(AiFeature.Translation, Instructions, context, prompt, Schema(), AiEffort.Low, 16000);
            var run = await ai.RunAsync(AiFeature.Translation, null, null, ask, ct);
            var answered = Answers(run.Reply).Where(a => keys.ContainsKey(a.Key)).ToDictionary(a => keys[a.Key], a => a.Value);
            var drafts = items.Where(i => answered.TryGetValue(i.Id, out var ar) && ar.Length <= source.Max)
                .Select(i => new TranslateDraftItem(i.Id, i.Text, answered[i.Id])).ToList();
            var drafted = drafts.Select(d => d.Id).ToHashSet();
            return Results.Ok(new TranslateDraft(source.Field, drafts, items.Count - drafts.Count, missing - items.Count,
                items.Where(i => !drafted.Contains(i.Id)).Select(i => i.Id).ToList(), skippedLeft));
        }
        return Results.Ok(new TranslateDraft(null, [], 0, 0, [], skippedLeft));
    }

    /// <summary>Claude's Arabic text per short id: non-blank answers only (a refusal or an unreadable answer gives none).</summary>
    private static Dictionary<string, string> Answers(AiReply reply)
    {
        var result = new Dictionary<string, string>();
        if (reply.Refused || reply.Json is null) return result;
        try
        {
            foreach (var item in JsonNode.Parse(reply.Json)?["items"]?.AsArray() ?? [])
                if (item?["id"]?.GetValue<string>() is { Length: > 0 } id && item["ar"]?.GetValue<string>()?.Trim() is { Length: > 0 } ar) result[id] = ar;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { }
        return result;
    }

    private static async Task<IResult> SaveAsync(TranslateSaveInput input, HttpContext http, CmmsDbContext db, PermissionService perms, CancellationToken ct)
    {
        if (await RefuseAsync(input.List, http, perms) is { } refused) return refused;
        var source = Lists[input.List!].Fields.FirstOrDefault(f => string.Equals(f.Field, input.Field, StringComparison.OrdinalIgnoreCase));
        if (source is null) return Results.Problem(title: Translator.T(UnknownField), statusCode: 400);
        var items = input.Items ?? [];
        var texts = items.Where(i => i.Ar.Clean() is { } ar && ar.Length <= source.Max).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Ar.Clean()!);
        var saved = texts.Count == 0 ? 0 : await source.SaveAsync(db, texts, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { saved, skipped = items.Count - saved });
    }

    // ---------------- Expression plumbing: the same main/Arabic selectors in filters and projections ----------------

    private static readonly ConstantExpression Null = Expression.Constant(null, typeof(string));
    private static readonly ConstantExpression Empty = Expression.Constant("", typeof(string));

    private sealed record IdText(Guid Id, string? Text);
    private sealed record MainAr(string? Main, string? Ar);

    private static Expression<Func<T, bool>> Both<T>(Expression<Func<T, string?>> a, Expression<Func<T, string?>> b, Func<Expression, Expression, Expression> body)
    {
        var p = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(body(Swap(a, p), Swap(b, p)), p);
    }

    private static Expression<Func<T, IdText>> Pair<T>(Expression<Func<T, Guid>> id, Expression<Func<T, string?>> text)
    {
        var p = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, IdText>>(Expression.New(typeof(IdText).GetConstructors()[0], Swap(id, p), Swap(text, p)), p);
    }

    private static Expression<Func<T, MainAr>> Two<T>(Expression<Func<T, string?>> main, Expression<Func<T, string?>> ar)
    {
        var p = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, MainAr>>(Expression.New(typeof(MainAr).GetConstructors()[0], Swap(main, p), Swap(ar, p)), p);
    }

    private static Expression Swap(LambdaExpression e, ParameterExpression p) => new Replace(e.Parameters[0], p).Visit(e.Body);

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
