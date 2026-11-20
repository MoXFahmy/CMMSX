using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Planned parts and planned hours on a work order. Changing the plan needs Edit work orders and View inventory.</summary>
public static class PlanningEndpoints
{
    public sealed record PlannedPartInput(Guid? PartId, Guid? StoreId, decimal Quantity, string? Notes);
    public sealed record PlannedHoursLine(Guid EmployeeId, decimal? PlannedHours);
    public sealed record PlannedHoursInput(List<PlannedHoursLine>? Lines);

    private const decimal MaxQuantity = 1_000_000_000m;
    private const decimal MaxHours = 10_000m;

    public static void MapPlanningEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/work-orders/{id:guid}").WithTags("Work order planning")
            .RequirePermission(Permissions.WorkOrdersEdit).RequirePermission(Permissions.InventoryView);
        // Planned hours are part of editing the work order; they don't touch inventory.
        var hoursGroup = api.MapGroup("/work-orders/{id:guid}").WithTags("Work order planning").RequirePermission(Permissions.WorkOrdersEdit);

        g.MapPost("/planned-parts", async (Guid id, PlannedPartInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var w = await PlannableAsync(db, id, ct);
            if (w is null) return Results.NotFound();
            var v = Validate(i).When(i.PartId is null, "partId", "Choose a part.");
            if (!v.IsValid) return v.Problem();
            var part = await db.Parts.AsNoTracking().Where(p => p.Id == i.PartId).Select(p => new { p.Id, p.PartNumber }).FirstOrDefaultAsync(ct)
                       ?? throw new DomainException("Part not found.");
            var already = $"{part.PartNumber} is already planned on this work order. Change that line instead.";
            if (await db.WorkOrderParts.AnyAsync(x => x.WorkOrderId == id && x.PartId == part.Id, ct)) throw new DomainException(already);
            var line = new WorkOrderPart
            {
                WorkOrderId = id, PartId = part.Id, Quantity = Math.Round(i.Quantity, 3), Notes = Notes(i.Notes),
                StoreId = i.StoreId is { } sid ? await ActiveStoreAsync(db, sid, ct) : await Planning.DefaultStoreAsync(db, part.Id, w.SiteId, ct),
            };
            if (line.StoreId is { } chosen) await EnsureLevelAsync(db, part.Id, chosen, ct);
            db.WorkOrderParts.Add(line);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw new DomainException(already); } // two people planned it at the same moment
            return Results.Created($"/api/work-orders/{id}/planned-parts/{line.Id}", new { line.Id });
        });

        // A null store keeps the line's current store.
        g.MapPut("/planned-parts/{lineId:guid}", async (Guid id, Guid lineId, PlannedPartInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            if (await PlannableAsync(db, id, ct) is null) return Results.NotFound();
            var line = await db.WorkOrderParts.FirstOrDefaultAsync(x => x.Id == lineId && x.WorkOrderId == id, ct);
            if (line is null) return Results.NotFound();
            var v = Validate(i);
            if (!v.IsValid) return v.Problem();
            line.Quantity = Math.Round(i.Quantity, 3);
            line.Notes = Notes(i.Notes);
            if (i.StoreId is { } sid)
            {
                line.StoreId = await ActiveStoreAsync(db, sid, ct);
                await EnsureLevelAsync(db, line.PartId, sid, ct);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { line.Id });
        });

        g.MapDelete("/planned-parts/{lineId:guid}", async (Guid id, Guid lineId, CmmsDbContext db, CancellationToken ct) =>
        {
            if (await PlannableAsync(db, id, ct) is null) return Results.NotFound();
            var line = await db.WorkOrderParts.FirstOrDefaultAsync(x => x.Id == lineId && x.WorkOrderId == id, ct);
            if (line is null) return Results.NotFound();
            db.WorkOrderParts.Remove(line);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        hoursGroup.MapPut("/planned-hours", async (Guid id, PlannedHoursInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var w = await PlannableAsync(db, id, ct);
            if (w is null) return Results.NotFound();
            var lines = input.Lines ?? [];
            var v = new Validator().When(lines.Any(l => l.PlannedHours is < 0 or > MaxHours), "lines", $"Planned hours must be between 0 and {MaxHours:0}.");
            if (!v.IsValid) return v.Problem();
            foreach (var l in lines)
            {
                var a = w.Assignees.FirstOrDefault(x => x.EmployeeId == l.EmployeeId) ?? throw new DomainException("Plan hours only for technicians assigned to this work order.");
                a.PlannedHours = l.PlannedHours is { } h ? Math.Round(h, 2) : null;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { w.Id });
        });
    }

    /// <summary>The work order, tracked with its technicians, if its plan may still change; null when it does not exist.</summary>
    private static async Task<WorkOrder?> PlannableAsync(CmmsDbContext db, Guid id, CancellationToken ct)
    {
        var w = await db.WorkOrders.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is not null && !WorkOrderWorkflow.IsActive(w.Status))
            throw new DomainException("This work order is no longer open, so its plan can't change. Reopen it first.");
        return w;
    }

    private static Validator Validate(PlannedPartInput i) => new Validator()
        .When(i.Quantity <= 0, "quantity", "Enter a quantity above zero.")
        .When(i.Quantity > MaxQuantity, "quantity", "This quantity is too large.")
        .When(i.Notes?.Trim().Length > 500, "notes", "Keep the notes to 500 characters.");

    private static string? Notes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>
    /// Gives the part a (zero) stock level in a store it is planned in, so every screen that lists store levels — the part page,
    /// its "reserved for" list, the proposal — sees the reservation, as the parts list (which sums the view) does.
    /// </summary>
    private static async Task EnsureLevelAsync(CmmsDbContext db, Guid partId, Guid storeId, CancellationToken ct)
    {
        if (!await db.StockLevels.AnyAsync(l => l.PartId == partId && l.StoreId == storeId, ct))
            db.StockLevels.Add(new StockLevel { PartId = partId, StoreId = storeId });
    }

    private static async Task<Guid> ActiveStoreAsync(CmmsDbContext db, Guid storeId, CancellationToken ct) =>
        await db.Stores.AnyAsync(s => s.Id == storeId && s.IsActive, ct) ? storeId : throw new DomainException("Choose an active store.");
}
