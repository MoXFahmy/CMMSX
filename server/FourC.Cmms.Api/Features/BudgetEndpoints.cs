using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Costs;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Budgets (0.20.0): one amount per cost centre per month, entered a year at a time; read with View reports, edited with Manage settings.</summary>
public static class BudgetEndpoints
{
    public sealed record BudgetRowInput(Guid CostCentreId, decimal?[] Months);
    public sealed record BudgetInput(int Year, List<BudgetRowInput> Rows);

    public static void MapBudgetEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/budgets").WithTags("Budgets");

        g.MapGet("/", async (int year, CmmsDbContext db, CancellationToken ct) =>
        {
            var paths = await CostCentreEndpoints.PathsAsync(db, ct);
            var centres = await db.CostCentres.AsNoTracking().Select(c => new { c.Id, c.IsActive }).ToListAsync(ct);
            var ids = centres.Select(c => c.Id).ToList();
            var amounts = await db.Budgets.AsNoTracking().Where(b => b.Year == year && ids.Contains(b.CostCentreId)).ToListAsync(ct);
            return Results.Ok(new
            {
                Year = year,
                Rows = centres.Select(c =>
                {
                    var months = Enumerable.Range(1, 12).Select(m => amounts.FirstOrDefault(b => b.CostCentreId == c.Id && b.Month == m)?.Amount ?? 0).ToArray();
                    return new { CostCentreId = c.Id, Path = paths.GetValueOrDefault(c.Id), c.IsActive, Months = months, Total = months.Sum() };
                }).OrderBy(r => r.Path),
            });
        }).RequirePermission(Permissions.ReportsView);

        g.MapPut("/", async (BudgetInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            if (i.Year is < 2000 or > 2100) throw new DomainException("Choose a year from 2000 to 2100.");
            if (i.Rows.Any(r => r.Months is not { Length: 12 })) throw new DomainException("Give twelve months for each cost centre.");
            if (i.Rows.SelectMany(r => r.Months).Any(m => m < 0)) throw new DomainException("A budget can't be below 0.");
            var ids = i.Rows.Select(r => r.CostCentreId).Distinct().ToList();
            if (await db.CostCentres.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count) throw new DomainException("Choose an active cost centre.");
            var existing = await db.Budgets.Where(b => b.Year == i.Year && ids.Contains(b.CostCentreId)).ToListAsync(ct);
            foreach (var row in i.Rows)
                for (var m = 1; m <= 12; m++)
                {
                    var amount = row.Months[m - 1];
                    var current = existing.FirstOrDefault(b => b.CostCentreId == row.CostCentreId && b.Month == m);
                    if (amount is null or 0) { if (current is not null) db.Budgets.Remove(current); continue; }
                    if (current is null) db.Budgets.Add(new Budget { CostCentreId = row.CostCentreId, Year = i.Year, Month = m, Amount = Math.Round(amount.Value, 2) });
                    else current.Amount = Math.Round(amount.Value, 2);
                }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                // Two first saves of the same year at once: the other one added these months first (0.29.1).
                return Results.Problem(title: Translator.T("These budgets were changed by someone else. Reload them and try again."), statusCode: 409);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Problem(title: Translator.T("These budgets were changed by someone else. Reload them and try again."), statusCode: 409);
            }
            return Results.Ok(new { i.Year });
        }).RequirePermission(Permissions.SettingsManage);
    }
}
