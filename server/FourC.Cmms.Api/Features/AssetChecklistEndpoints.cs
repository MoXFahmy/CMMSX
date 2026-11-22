using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Checklists on an asset or an asset category (0.12.0): their steps are added to every new work order on the asset.</summary>
public static class AssetChecklists
{
    public sealed record ChecklistInput(Guid? AssetId, Guid? AssetCategoryId, Guid PmTaskId);
    public const string AlreadyOn = "This template is already on it.";

    /// <summary>
    /// Appends the steps of the templates attached to each of <paramref name="assetIds"/> and to its category, after the work
    /// order's own steps; a template the work order already uses is not added again. On a round (several assets) each step names
    /// its asset. Called while the work order is being created, before it is saved.
    /// </summary>
    public static async Task AppendAsync(CmmsDbContext db, WorkOrder w, IReadOnlyList<Guid> assetIds, CancellationToken ct)
    {
        if (assetIds.Count == 0) return;
        var categories = await db.Assets.AllSites().AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.CategoryId, ct);
        var categoryIds = categories.Values.OfType<Guid>().Distinct().ToList();
        var links = await db.AssetChecklists.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == db.CurrentTenantId && !c.IsDeleted && ((c.AssetId != null && assetIds.Contains(c.AssetId.Value))
                || (c.AssetCategoryId != null && categoryIds.Contains(c.AssetCategoryId.Value))))
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        if (links.Count == 0) return;
        var templateIds = links.Select(l => l.PmTaskId).Distinct().ToList();
        var steps = (await db.PmTaskSteps.AsNoTracking().Where(s => templateIds.Contains(s.PmTaskId) && db.PmTasks.Any(t => t.Id == s.PmTaskId)).OrderBy(s => s.Sequence).ToListAsync(ct)).ToLookup(s => s.PmTaskId);
        var seq = db.ChangeTracker.Entries<WorkOrderTask>().Where(e => e.State == EntityState.Added && e.Entity.WorkOrderId == w.Id)
            .Select(e => e.Entity.Sequence).DefaultIfEmpty(0).Max();
        var round = assetIds.Count > 1 || w.Assets.Count > 0;
        foreach (var assetId in assetIds)
        {
            var category = categories.GetValueOrDefault(assetId);
            var own = links.Where(l => l.AssetId == assetId).Concat(links.Where(l => l.AssetCategoryId != null && l.AssetCategoryId == category))
                .Select(l => l.PmTaskId).Distinct().Where(t => t != w.PmTaskId);
            foreach (var templateId in own)
                foreach (var step in steps[templateId]) db.WorkOrderTasks.Add(StepCopy.ToWorkOrder(step, w.Id, ++seq, round ? assetId : null));
        }
    }

    public static void MapAssetChecklistEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/asset-checklists").WithTags("Asset checklists").RequireAuthorization();

        g.MapGet("/", async (Guid? assetId, Guid? categoryId, CmmsDbContext db, CancellationToken ct) =>
        {
            if (assetId is null && categoryId is null) throw new DomainException("Choose an asset or a category.");
            var q = db.AssetChecklists.AsNoTracking().Where(c => (assetId != null && c.AssetId == assetId) || (categoryId != null && c.AssetCategoryId == categoryId));
            return await q.OrderBy(c => c.CreatedAt).Select(c => new
            {
                c.Id, c.AssetId, c.AssetCategoryId, c.PmTaskId,
                Code = db.PmTasks.Where(t => t.Id == c.PmTaskId).Select(t => t.Code).FirstOrDefault(),
                Name = db.PmTasks.Where(t => t.Id == c.PmTaskId).Select(t => t.Name).FirstOrDefault(),
                StepCount = db.PmTaskSteps.Count(s => s.PmTaskId == c.PmTaskId),
            }).ToListAsync(ct);
        }).RequirePermission(Permissions.AssetsView);

        g.MapPost("/", async (ChecklistInput input, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            var v = new Validator().When((input.AssetId is null) == (input.AssetCategoryId is null), "assetId", "Choose an asset or a category.");
            if (!v.IsValid) return v.Problem();
            if (!await AllowedAsync(perms, ctx, input.AssetId is not null)) return Results.Forbid();
            if (input.AssetId is { } a && !await db.Assets.AnyAsync(x => x.Id == a, ct)) return Results.NotFound();
            if (input.AssetCategoryId is { } k && !await db.AssetCategories.AnyAsync(x => x.Id == k, ct)) return Results.NotFound();
            if (!await db.PmTasks.AnyAsync(t => t.Id == input.PmTaskId, ct)) v.Add("pmTaskId", "Unknown work template.");
            else if (await db.AssetChecklists.AnyAsync(c => c.PmTaskId == input.PmTaskId && c.AssetId == input.AssetId && c.AssetCategoryId == input.AssetCategoryId, ct))
                v.Add("pmTaskId", AlreadyOn);
            if (!v.IsValid) return v.Problem();
            var row = new AssetChecklist { AssetId = input.AssetId, AssetCategoryId = input.AssetCategoryId, PmTaskId = input.PmTaskId };
            db.AssetChecklists.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/asset-checklists/{row.Id}", new { row.Id });
        });

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct) =>
        {
            var row = await db.AssetChecklists.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (!await AllowedAsync(perms, ctx, row.AssetId is not null)) return Results.Forbid();
            db.AssetChecklists.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>An asset's checklists need permission to edit assets; a category's, to manage settings.</summary>
    private static async Task<bool> AllowedAsync(PermissionService perms, ITenantContext ctx, bool asset) =>
        ctx.UserId is { } user && (await perms.GetAsync(user)).Contains(asset ? Permissions.AssetsEdit : Permissions.SettingsManage);
}
