using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Contractor categories with default hourly rates, and the rule for choosing a contractor company (0.17.0).</summary>
public static class ContractorEndpoints
{
    public sealed record ContractorCategoryInput(string Code, string Name, decimal DefaultRate, bool IsActive = true);

    public static void MapContractorEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/contractor-categories").WithTags("Contractor categories");
        g.MapGet("/", async (CmmsDbContext db, bool? activeOnly, CancellationToken ct) =>
            await db.ContractorCategories.AsNoTracking().Where(c => activeOnly != true || c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Code, c.Name, c.DefaultRate, c.IsActive }).ToListAsync(ct))
            .RequireAuthorization();
        g.MapPost("/", async (ContractorCategoryInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateCategoryAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var c = new ContractorCategory { Code = "", Name = "" };
            Apply(c, i);
            db.ContractorCategories.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/contractor-categories/{c.Id}", new { c.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        g.MapPut("/{id:guid}", async (Guid id, ContractorCategoryInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.ContractorCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await ValidateCategoryAsync(db, i, id, ct);
            if (!v.IsValid) return v.Problem();
            Apply(c, i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.SuppliersManage);
        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.ContractorCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await db.SupplierCategoryRates.AnyAsync(r => r.ContractorCategoryId == id, ct)
                || await db.WorkOrderContractors.IgnoreQueryFilters([CmmsDbContext.SiteFilter]).AnyAsync(l => l.ContractorCategoryId == id, ct))
                throw new DomainException("This item is in use. Mark it inactive instead.");
            db.ContractorCategories.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SuppliersManage);
    }

    /// <summary>
    /// A company newly chosen for a contractor line, contract or employee must be active and have the Contractor role. One already on
    /// the record stays even after it loses the role or goes inactive.
    /// </summary>
    internal static Validator ValidateContractorCompany(Supplier? s, bool newlyChosen, string field = "supplierId")
    {
        var v = new Validator();
        if (newlyChosen && (s is null || !s.IsActive || !s.IsContractor)) v.Add(field, "Choose an active company with the Contractor role.");
        return v;
    }

    private static async Task<Validator> ValidateCategoryAsync(CmmsDbContext db, ContractorCategoryInput i, Guid? id, CancellationToken ct)
    {
        var v = new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200).When(i.DefaultRate < 0, "defaultRate", "Rates cannot be negative.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.ContractorCategories.AnyAsync(c => c.Code == i.Code.Trim() && c.Id != id, ct)) v.Add("code", "This code is already used.");
        return v;
    }

    private static void Apply(ContractorCategory c, ContractorCategoryInput i)
    {
        c.Code = i.Code.Trim(); c.Name = i.Name.Trim(); c.DefaultRate = i.DefaultRate; c.IsActive = i.IsActive;
    }
}
