using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Saved views and column choices of the list toolkit. Both are personal: every query is scoped to the signed-in user,
/// so another person's view id answers 404.
/// </summary>
public static class ViewEndpoints
{
    public sealed record ViewInput(string? List, string? Name, string? Query, List<string>? HiddenColumns, bool IsDefault);
    public sealed record ListSettingInput(List<string>? HiddenColumns);
    public sealed record PinInput(bool Pinned);
    public sealed record HomeOrderInput(List<Guid>? Ids);

    private const int MaxViewsPerList = 50;
    private const int MaxNameLength = 60;
    private const int MaxQueryLength = 2000;
    private const int MaxColumns = 100;
    private const int MaxColumnLength = 80;
    /// <summary>Counter tiles a person may have on their home page (0.22.0).</summary>
    public const int MaxPinned = 24;

    public static void MapViewEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/views").WithTags("Saved views").RequireAuthorization();

        g.MapGet("/", async (string? list, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!ListKeys.IsValid(list)) throw new DomainException("Unknown list.");
            var me = Me(ctx);
            return await db.SavedViews.AsNoTracking().Where(v => v.UserId == me && v.List == list).OrderBy(v => v.Name)
                .Select(v => new { v.Id, v.Name, v.Query, v.HiddenColumns, v.IsDefault, v.PinnedToHome }).ToListAsync(ct);
        });

        // The person's views pinned to the home page, in their order (0.22.0).
        g.MapGet("/home", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            return await db.SavedViews.AsNoTracking().Where(v => v.UserId == me && v.PinnedToHome).OrderBy(v => v.HomeOrder).ThenBy(v => v.Name)
                .Select(v => new { v.Id, v.List, v.Name, v.Query }).ToListAsync(ct);
        });

        g.MapPut("/{id:guid}/pin", async (Guid id, PinInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            var view = await db.SavedViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct);
            if (view is null) return Results.NotFound();
            if (input.Pinned && !view.PinnedToHome)
            {
                var pinned = await db.SavedViews.Where(x => x.UserId == me && x.PinnedToHome).Select(x => (int?)x.HomeOrder).ToListAsync(ct);
                if (pinned.Count >= MaxPinned) throw new DomainException($"You can pin up to {MaxPinned} views to your home page. Unpin one first.");
                view.HomeOrder = (pinned.Max() ?? -1) + 1;
            }
            view.PinnedToHome = input.Pinned;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { view.PinnedToHome, view.HomeOrder });
        });

        // The order of the tiles: listed views first, in that order; pinned views left out follow, keeping theirs.
        g.MapPut("/home-order", async (HomeOrderInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            var pinned = await db.SavedViews.Where(x => x.UserId == me && x.PinnedToHome).OrderBy(x => x.HomeOrder).ThenBy(x => x.Name).ToListAsync(ct);
            var ids = (input.Ids ?? []).Distinct().ToList();
            var ordered = ids.Select(i => pinned.FirstOrDefault(v => v.Id == i)).OfType<SavedView>().Concat(pinned.Where(v => !ids.Contains(v.Id))).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].HomeOrder = i;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/", async (ViewInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            if (!ListKeys.IsValid(input.List)) throw new DomainException("Unknown list.");
            var v = await ValidateAsync(db, me, input.List!, input, null, ct);
            if (!v.IsValid) return v.Problem();
            if (await db.SavedViews.CountAsync(x => x.UserId == me && x.List == input.List, ct) >= MaxViewsPerList)
                throw new DomainException($"You can save up to {MaxViewsPerList} views per list. Delete one first.");
            var now = DateTimeOffset.UtcNow;
            var view = new SavedView
            {
                UserId = me, List = input.List!, Name = input.Name!.Trim(), Query = input.Query ?? "", HiddenColumns = Columns(input.HiddenColumns),
                IsDefault = input.IsDefault, CreatedAt = now, UpdatedAt = now,
            };
            if (view.IsDefault) await ClearDefaultAsync(db, me, view.List, null, ct);
            db.SavedViews.Add(view);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { view.Id });
        });

        g.MapPut("/{id:guid}", async (Guid id, ViewInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            var view = await db.SavedViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct);
            if (view is null) return Results.NotFound();
            var v = await ValidateAsync(db, me, view.List, input, id, ct);
            if (!v.IsValid) return v.Problem();
            view.Name = input.Name!.Trim();
            view.Query = input.Query ?? "";
            view.HiddenColumns = Columns(input.HiddenColumns);
            if (input.IsDefault && !view.IsDefault) await ClearDefaultAsync(db, me, view.List, id, ct);
            view.IsDefault = input.IsDefault;
            view.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { view.Id });
        });

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            var view = await db.SavedViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct);
            if (view is null) return Results.NotFound();
            db.SavedViews.Remove(view);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var s = api.MapGroup("/auth/me/list-settings").WithTags("Saved views").RequireAuthorization();

        // The person's own column choices; for a list they haven't chosen on, the default of the first of their roles (by name) that has one (0.22.0).
        s.MapGet("/", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var me = Me(ctx);
            var rows = await db.UserListSettings.AsNoTracking().Where(x => x.UserId == me).ToListAsync(ct);
            var result = rows.ToDictionary(x => x.List, x => new { x.HiddenColumns, FromRole = false });
            var roleDefaults = await (from ur in db.UserRoles
                                      join rs in db.RoleListSettings on ur.RoleId equals rs.RoleId
                                      where ur.UserId == me
                                      select new { rs.List, rs.HiddenColumns, ur.Role!.Name }).AsNoTracking().ToListAsync(ct);
            foreach (var d in roleDefaults.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                result.TryAdd(d.List, new { d.HiddenColumns, FromRole = true });
            return result;
        });

        // "Reset columns": the person's own choice goes, so their role's default (or the list's own) applies again.
        s.MapDelete("/{list}", async (string list, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!ListKeys.IsValid(list)) throw new DomainException("Unknown list.");
            var me = Me(ctx);
            await db.UserListSettings.Where(x => x.UserId == me && x.List == list).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        s.MapPut("/{list}", async (string list, ListSettingInput input, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            if (!ListKeys.IsValid(list)) throw new DomainException("Unknown list.");
            var v = ValidateColumns(new Validator(), input.HiddenColumns);
            if (!v.IsValid) return v.Problem();
            var me = Me(ctx);
            var cols = Columns(input.HiddenColumns);
            for (var attempt = 1; ; attempt++)
            {
                var row = await db.UserListSettings.FirstOrDefaultAsync(x => x.UserId == me && x.List == list, ct);
                if (row is null) db.UserListSettings.Add(row = new UserListSetting { UserId = me, List = list });
                row.HiddenColumns = cols;
                try
                {
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { row.HiddenColumns });
                }
                // Two quick saves on a list with no row yet: the other one inserted first, so update its row instead.
                catch (DbUpdateException) when (attempt < 3) { db.ChangeTracker.Clear(); }
            }
        });
    }

    private static Guid Me(ITenantContext ctx) => ctx.UserId ?? throw new UnauthorizedAccessException();

    internal static List<string> Columns(List<string>? cols) => (cols ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();

    internal static Validator ValidateColumns(Validator v, List<string>? cols) => v
        .When((cols?.Count ?? 0) > MaxColumns, "hiddenColumns", $"Hide at most {MaxColumns} columns.")
        .When(cols?.Any(c => c is null || c.Length > MaxColumnLength) == true, "hiddenColumns", "A column name is too long.");

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, Guid me, string list, ViewInput input, Guid? id, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        var v = new Validator()
            .When(name.Length == 0, "name", "Give the view a name.")
            .When(name.Length > MaxNameLength, "name", $"Keep the name to {MaxNameLength} characters.")
            .When((input.Query?.Length ?? 0) > MaxQueryLength, "query", "This view has too many filters to save.");
        ValidateColumns(v, input.HiddenColumns);
        // SQL Server compares names without case under the default collation; ToLower keeps the check explicit.
        if (name.Length > 0 && await db.SavedViews.AnyAsync(x => x.UserId == me && x.List == list && x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            v.Add("name", "You already have a view with this name.");
        return v;
    }

    private static async Task ClearDefaultAsync(CmmsDbContext db, Guid me, string list, Guid? except, CancellationToken ct)
    {
        foreach (var other in await db.SavedViews.Where(x => x.UserId == me && x.List == list && x.IsDefault && x.Id != except).ToListAsync(ct))
            other.IsDefault = false;
    }
}
