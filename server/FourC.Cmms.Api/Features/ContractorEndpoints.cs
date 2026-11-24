using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Contractor categories with default hourly rates, and the rule for choosing a contractor company (0.17.0).</summary>
public static class ContractorEndpoints
{
    public sealed record ContractorCategoryInput(string Code, string Name, decimal DefaultRate, bool IsActive = true);
    /// <param name="PlannedOn">The visit day (0.19.0), a planner field; none = the work order's day (a technician's save leaves it as it is).</param>
    public sealed record ContractorLineInput(Guid SupplierId, Guid? CategoryId, Guid? ContactId, string? PersonName, decimal PlannedHours, decimal? Rate,
        decimal HoursDone, DateOnly? DoneOn, string? Notes, DateOnly? PlannedOn = null);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

        // ---- Contractor lines on a work order: planners add, edit and remove; technicians record what was done ----
        var w = api.MapGroup("/work-orders/{id:guid}/contractors").WithTags("Work orders");
        w.MapPost("/", async (Guid id, ContractorLineInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            await WorkOrderEndpoints.RequireOpenAsync(db, id, ct);
            var line = new WorkOrderContractor { WorkOrderId = id };
            var v = await ValidateLineAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            await ApplyLineAsync(db, line, i, rateFromCompany: i.Rate is null, ct);
            line.PlannedOn = i.PlannedOn;
            db.WorkOrderContractors.Add(line);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { line.Id });
        }).RequirePermission(Permissions.WorkOrdersEdit);

        w.MapPut("/{lineId:guid}", async (Guid id, Guid lineId, ContractorLineInput i, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var mine = await perms.GetAsync(UserId(http));
            var canEdit = mine.Contains(Permissions.WorkOrdersEdit);
            if (!canEdit && !mine.Contains(Permissions.WorkOrdersExecute)) return Results.Forbid();
            await WorkOrderEndpoints.RequireOpenAsync(db, id, ct);
            var line = await db.WorkOrderContractors.FirstOrDefaultAsync(l => l.Id == lineId && l.WorkOrderId == id, ct);
            if (line is null) return Results.NotFound();
            if (!canEdit && (i.SupplierId != line.SupplierId || i.CategoryId != line.ContractorCategoryId || i.PlannedHours != line.PlannedHours
                             || (i.Rate is { } r && r != line.Rate) || (i.Notes.Clean() ?? "") != (line.Notes ?? "") || (i.PlannedOn is { } po && po != line.PlannedOn)))
                throw new DomainException("You can record the hours done, the date and the person; ask a planner to change the rest.");
            var v = await ValidateLineAsync(db, i, line, ct);
            if (!v.IsValid) return v.Problem();
            // The rate is taken again only when the company or category changes and no rate is given.
            var retake = i.Rate is null && (i.SupplierId != line.SupplierId || i.CategoryId != line.ContractorCategoryId);
            await ApplyLineAsync(db, line, i, retake, ct);
            if (canEdit) line.PlannedOn = i.PlannedOn;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { line.Id });
        }).RequireAuthorization();

        w.MapDelete("/{lineId:guid}", async (Guid id, Guid lineId, CmmsDbContext db, CancellationToken ct) =>
        {
            await WorkOrderEndpoints.RequireOpenAsync(db, id, ct);
            db.WorkOrderContractors.RemoveRange(await db.WorkOrderContractors.Where(l => l.Id == lineId && l.WorkOrderId == id).ToListAsync(ct)); // logged (0.25.0)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkOrdersEdit);
    }

    private static async Task<Validator> ValidateLineAsync(CmmsDbContext db, ContractorLineInput i, WorkOrderContractor? existing, CancellationToken ct)
    {
        var v = new Validator().When(i.PlannedHours < 0 || i.HoursDone < 0, "hours", "Hours cannot be negative.")
            .When(i.PlannedHours > 10000 || i.HoursDone > 10000, "hours", "Hours are too large.")
            .When(i.Rate < 0, "rate", "Rate cannot be negative.").When(i.DoneOn > Today.AddDays(1), "doneOn", "Date done cannot be in the future.")
            .MaxLength("personName", i.PersonName, "Person", 200).MaxLength("notes", i.Notes, "Notes", 2000);
        var company = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == i.SupplierId, ct);
        v.Merge(ValidateContractorCompany(company, existing is null || existing.SupplierId != i.SupplierId));
        if (i.CategoryId is { } cat && cat != existing?.ContractorCategoryId && !await db.ContractorCategories.AnyAsync(c => c.Id == cat && c.IsActive, ct))
            v.Add("categoryId", "Choose an active contractor category.");
        if (i.ContactId is { } contact && !await db.SupplierContacts.AnyAsync(c => c.Id == contact && c.SupplierId == i.SupplierId, ct))
            v.Add("contactId", "Choose a contact of this company.");
        return v;
    }

    private static async Task ApplyLineAsync(CmmsDbContext db, WorkOrderContractor line, ContractorLineInput i, bool rateFromCompany, CancellationToken ct)
    {
        if (rateFromCompany) line.Rate = await RateAsync(db, i.SupplierId, i.CategoryId, ct);
        else if (i.Rate is { } rate) line.Rate = rate;
        line.SupplierId = i.SupplierId; line.ContractorCategoryId = i.CategoryId; line.ContactId = i.ContactId;
        line.PersonName = i.ContactId is null ? i.PersonName.Clean() : null;
        line.PlannedHours = i.PlannedHours; line.HoursDone = i.HoursDone; line.Notes = i.Notes.Clean();
        line.DoneOn = i.HoursDone > 0 ? i.DoneOn ?? line.DoneOn ?? Today : i.DoneOn;
    }

    /// <summary>The company's rate for the category, else the category's default, else 0.</summary>
    internal static async Task<decimal> RateAsync(CmmsDbContext db, Guid company, Guid? category, CancellationToken ct)
    {
        if (category is null) return 0;
        var own = await db.SupplierCategoryRates.Where(r => r.SupplierId == company && r.ContractorCategoryId == category).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);
        return own ?? await db.ContractorCategories.Where(c => c.Id == category).Select(c => (decimal?)c.DefaultRate).FirstOrDefaultAsync(ct) ?? 0;
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
