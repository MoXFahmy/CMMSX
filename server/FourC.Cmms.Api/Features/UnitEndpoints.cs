using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Units of measure (0.21.0). Parts, measurement points and template steps keep the symbol as their text, so a new symbol is
/// written to them all; a work order's own steps keep what they were copied with. Symbols are case-sensitive ("mW" ≠ "MW").
/// </summary>
public static class UnitEndpoints
{
    public sealed record UnitInput(string Symbol, string? Name, string? NameAr, bool IsActive = true, bool Merge = false);

    private const string Exact = "Latin1_General_100_CS_AS";

    public static void MapUnitEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/units").WithTags("Units");
        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            (await db.Units.AsNoTracking().ToListAsync(ct)).OrderBy(u => u.Symbol, StringComparer.Ordinal)
                .Select(u => new { u.Id, u.Symbol, u.Name, u.NameAr, u.IsActive })).RequireAuthorization();

        g.MapPost("/", async (UnitInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var symbol = SymbolOf(i.Symbol);
            if (await db.Units.AnyAsync(u => u.Symbol == symbol, ct)) throw new DomainException("This symbol is already used.");
            var unit = new Unit { Symbol = symbol, Name = NameOf(i.Name, symbol), NameAr = i.NameAr.Clean(), IsActive = i.IsActive };
            db.Units.Add(unit);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/units/{unit.Id}", new { unit.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, UnitInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var symbol = SymbolOf(i.Symbol);
            // The unit and every record that carries its symbol as text are saved together; one attempt from the load on, so a retry
            // after a dropped connection starts again from what is stored (0.29.1).
            return await Atomic.RunAsync<IResult>(db, async tk =>
            {
                db.ChangeTracker.Clear();
                var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, tk);
                if (unit is null) return Results.NotFound();
                var old = unit.Symbol;
                var other = symbol == old ? null : await db.Units.FirstOrDefaultAsync(u => u.Id != id && u.Symbol == symbol, tk);
                if (other is not null && !i.Merge) return Results.Conflict(new { MergeWith = other.Id, Message = Translator.T("Another unit has this symbol. Merge the two?") });
                if (other is not null) db.Units.Remove(unit);
                else { unit.Symbol = symbol; unit.Name = NameOf(i.Name, symbol); unit.NameAr = i.NameAr.Clean(); unit.IsActive = i.IsActive; }
                await db.SaveChangesAsync(tk);
                if (symbol != old)
                {
                    var (parts, points) = await RenameAsync(db, old, symbol, tk);
                    if (parts + points > 0)
                    {
                        FourC.Cmms.Core.Auditing.AuditLog.Summary(db, "Unit", other?.Id ?? unit.Id, "Updated",
                            $"Unit renamed to {symbol}: {FourC.Cmms.Core.Auditing.AuditLog.Count(parts, "part", "parts")} and {FourC.Cmms.Core.Auditing.AuditLog.Count(points, "point or step", "points and steps")}");
                        await db.SaveChangesAsync(tk);
                    }
                }
                return Results.Ok(new { Id = other?.Id ?? unit.Id });
            }, ct);
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (unit is null) return Results.NotFound();
            if (await InUseAsync(db, unit.Symbol, ct)) throw new DomainException("This unit is in use. Make it inactive instead.");
            db.Units.Remove(unit);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static string SymbolOf(string? symbol) =>
        symbol is { } s && ReferenceLists.Tidy(s) is { Length: > 0 } t ? t.Length <= 30 ? t : throw new DomainException("A symbol can be at most 30 characters.")
            : throw new DomainException("Enter a symbol.");

    private static string NameOf(string? name, string symbol) =>
        name.Clean() is { } n ? n.Length <= 100 ? n : throw new DomainException("A name can be at most 100 characters.") : symbol;

    /// <summary>Parts, measurement points and template steps with exactly <paramref name="from"/> (case counts) take <paramref name="to"/>.</summary>
    private static async Task<(int Parts, int Points)> RenameAsync(CmmsDbContext db, string from, string to, CancellationToken ct)
    {
        var parts = await db.Parts.AllSites().Where(p => EF.Functions.Collate(p.UnitOfMeasure, Exact) == from).ExecuteUpdateAsync(s => s.SetProperty(p => p.UnitOfMeasure, to), ct);
        var points = await db.MeasurementPoints.AllSites().Where(p => EF.Functions.Collate(p.Unit!, Exact) == from).ExecuteUpdateAsync(s => s.SetProperty(p => p.Unit, to), ct);
        var tasks = db.PmTasks.AllSites().Select(t => t.Id);
        var steps = await db.PmTaskSteps.Where(x => tasks.Contains(x.PmTaskId) && EF.Functions.Collate(x.Unit!, Exact) == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.Unit, to), ct);
        return (parts, points + steps);
    }

    private static async Task<bool> InUseAsync(CmmsDbContext db, string symbol, CancellationToken ct)
    {
        var tasks = db.PmTasks.AllSites().Select(t => t.Id);
        return await db.Parts.AllSites().AnyAsync(p => EF.Functions.Collate(p.UnitOfMeasure, Exact) == symbol, ct)
            || await db.MeasurementPoints.AllSites().AnyAsync(p => EF.Functions.Collate(p.Unit!, Exact) == symbol, ct)
            || await db.PmTaskSteps.AnyAsync(x => tasks.Contains(x.PmTaskId) && EF.Functions.Collate(x.Unit!, Exact) == symbol, ct);
    }
}
