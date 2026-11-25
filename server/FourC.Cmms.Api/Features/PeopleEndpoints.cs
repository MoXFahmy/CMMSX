using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class PeopleEndpoints
{
    /// <param name="NameAr">0.28.0: the Arabic name, for the lists that have one (symptoms, causes, remedies, cancel reasons); left out = kept, blank = cleared.</param>
    public sealed record LookupInput(string Code, string Name, string? Color, bool IsActive = true, string? NameAr = null);
    public sealed record EmployeeInput(string Code, string FullName, string? JobTitle, string? Email, string? Phone, List<Guid>? TradeIds, List<Guid>? SiteIds,
        decimal? HourlyRate, decimal? OvertimeRate, bool IsContractor, string? Company, Guid? UserId, string? Notes, bool IsActive = true, string? RowVersion = null,
        List<WorkingDay>? WorkingHours = null, Guid? CompanyId = null);

    public static void MapPeopleEndpoints(this RouteGroupBuilder api)
    {
        MapLookup(api, "/trades", "Trades", db => db.Trades, () => new Trade { Code = "", Name = "" }, Permissions.PeopleManage,
            async (db, id, ct) => await db.Employees.AnyAsync(e => e.TradeIds.Contains(id), ct) || await db.WorkOrders.AllSites().AnyAsync(w => w.TradeId == id, ct),
            (t, color) => t.Color = color, t => t.Color);
        MapLookup(api, "/failure-codes", "Failure codes", db => db.FailureCodes, () => new FailureCode { Code = "", Name = "" }, Permissions.SettingsManage,
            async (db, id, ct) => await db.WorkOrders.AllSites().AnyAsync(w => w.FailureCodeId == id, ct), null, null);
        // Symptoms and remedies (0.14.0): the failure codes are the causes between them.
        MapLookup(api, "/symptoms", "Symptoms", db => db.Symptoms, () => new Symptom { Code = "", Name = "" }, Permissions.SettingsManage,
            async (db, id, ct) => await db.WorkOrders.AllSites().AnyAsync(w => w.SymptomId == id, ct) || await db.WorkRequests.AllSites().AnyAsync(r => r.SymptomId == id, ct), null, null);
        MapLookup(api, "/remedies", "Remedies", db => db.Remedies, () => new Remedy { Code = "", Name = "" }, Permissions.SettingsManage,
            async (db, id, ct) => await db.WorkOrders.AllSites().AnyAsync(w => w.RemedyId == id, ct), null, null);
        MapLookup(api, "/cancel-reasons", "Cancel reasons", db => db.CancelReasons, () => new CancelReason { Code = "", Name = "" }, Permissions.SettingsManage,
            async (db, id, ct) => await db.WorkOrders.AllSites().AnyAsync(w => w.CancelReasonId == id, ct), null, null);
        // Kinds of purchase order (0.16.0): approval levels can differ by type.
        MapLookup(api, "/order-types", "Order types", db => db.OrderTypes, () => new FourC.Cmms.Core.Purchasing.OrderType { Code = "", Name = "" }, Permissions.SettingsManage,
            async (db, id, ct) => await db.PurchaseOrders.AllSites().AnyAsync(o => o.OrderTypeId == id, ct) || await db.ApprovalLevels.AnyAsync(l => l.OrderTypeId == id, ct), null, null);
        // Kinds of service contract (0.17.0).
        MapLookup(api, "/contract-types", "Contract types", db => db.ContractTypes, () => new FourC.Cmms.Core.Contracts.ContractType { Code = "", Name = "" }, Permissions.ContractsManage,
            async (db, id, ct) => await db.Contracts.AllSites().AnyAsync(c => c.ContractTypeId == id, ct), null, null);

        var g = api.MapGroup("/employees").WithTags("Employees");
        g.MapGet("/", async (CmmsDbContext db, string? search, Guid? tradeId, bool? activeOnly, CancellationToken ct) =>
        {
            var q = db.Employees.AsNoTracking();
            if (search.Clean() is { } s) q = q.Where(e => e.FullName.Contains(s) || e.Code.Contains(s) || e.Email!.Contains(s) || e.Company!.Contains(s));
            if (tradeId is not null) q = q.Where(e => e.TradeIds.Contains(tradeId.Value));
            if (activeOnly == true) q = q.Where(e => e.IsActive);
            var list = await q.OrderBy(e => e.FullName).Include(e => e.CompanyRecord).ToListAsync(ct);
            var open = await db.WorkOrderAssignees.Where(a => db.WorkOrders.Any(w => w.Id == a.WorkOrderId && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)))
                .GroupBy(a => a.EmployeeId).Select(x => new { x.Key, C = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            return list.Select(e => ToDto(e, open.GetValueOrDefault(e.Id)));
        }).RequirePermission(Permissions.PeopleView);

        // Lightweight list for assignment pickers (anyone who can see work orders).
        g.MapGet("/lookup", async (CmmsDbContext db, Guid? siteId, CancellationToken ct) =>
            await db.Employees.AsNoTracking().Where(e => e.IsActive && (siteId == null || e.SiteIds.Count == 0 || e.SiteIds.Contains(siteId.Value)))
                .OrderBy(e => e.FullName).Select(e => new { e.Id, e.Code, e.FullName, e.TradeIds, e.IsContractor, e.Company }).ToListAsync(ct))
            .RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.Employees.AsNoTracking().Include(e => e.CompanyRecord).FirstOrDefaultAsync(e => e.Id == id, ct) is { } e ? Results.Ok(ToDto(e, 0)) : Results.NotFound())
            .RequirePermission(Permissions.PeopleView);

        g.MapPost("/", async (EmployeeInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var e = CreateEmployee(db, input);
            e.CompanyId = input.CompanyId; // set here, not in Apply: the import has no company and must not clear it
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/employees/{e.Id}", new { e.Id });
        }).RequirePermission(Permissions.PeopleManage);

        g.MapPut("/{id:guid}", async (Guid id, EmployeeInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            var v = await ValidateAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            if (input.RowVersion is not null) db.Entry(e).Property(x => x.RowVersion).OriginalValue = input.RowVersion.ToVersion();
            UpdateEmployee(e, input);
            e.CompanyId = input.CompanyId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { e.Id });
        }).RequirePermission(Permissions.PeopleManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            if (await db.WorkOrderLabor.AllSites().AnyAsync(l => l.EmployeeId == id, ct) || await db.WorkOrderAssignees.AllSites().AnyAsync(a => a.EmployeeId == id, ct))
                throw new DomainException("This person has work order history. Mark them inactive instead.");
            db.Employees.Remove(e);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PeopleManage);
    }

    private static object ToDto(Employee e, int openWork) => new
    {
        e.Id, e.Code, e.FullName, e.JobTitle, e.Email, e.Phone, e.TradeIds, e.SiteIds, e.HourlyRate, e.OvertimeRate, e.IsContractor, e.Company,
        e.CompanyId, CompanyName = e.CompanyRecord?.Name, e.UserId, e.Notes, e.IsActive, e.AnonymisedAt, e.WorkingHours, OpenWorkOrders = openWork, RowVersion = Convert.ToBase64String(e.RowVersion),
    };

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator EmployeeRules(EmployeeInput i) => new Validator().Required("code", i.Code, "Employee no.", 50).Required("fullName", i.FullName, "Name", 200)
        .MaxLength("email", i.Email, "Email", 256).MaxLength("phone", i.Phone, "Phone", 50).MaxLength("company", i.Company, "Company", 200)
        .When(i.HourlyRate < 0 || i.OvertimeRate < 0, "hourlyRate", "Rates cannot be negative.")
        .When(i.Email.Clean() is { } em && !em.Contains('@'), "email", "Enter a valid email address.")
        .When(WorkingHours.Problem(i.WorkingHours) is not null, "workingHours", WorkingHours.Problem(i.WorkingHours) ?? "");

    internal static Employee CreateEmployee(CmmsDbContext db, EmployeeInput i)
    {
        var e = new Employee { Code = i.Code.Trim(), FullName = i.FullName.Trim() };
        Apply(e, i);
        db.Employees.Add(e);
        return e;
    }

    internal static void UpdateEmployee(Employee e, EmployeeInput i)
    {
        e.Code = i.Code.Trim(); e.FullName = i.FullName.Trim();
        Apply(e, i);
    }

    private static async Task<Validator> ValidateAsync(EmployeeInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = EmployeeRules(i);
        // The contractor company (0.17.0): a newly chosen one must be an active contractor; a linked one that lost the role stays.
        if (i.CompanyId is { } company)
        {
            var current = id is null ? null : await db.Employees.Where(e => e.Id == id).Select(e => e.CompanyId).FirstOrDefaultAsync(ct);
            var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == company, ct);
            v.Merge(ContractorEndpoints.ValidateContractorCompany(s, current != company, "companyId"));
        }
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.Employees.AnyAsync(e => e.Code == i.Code.Trim() && e.Id != id, ct)) v.Add("code", "Another person already uses this number.");
        var trades = i.TradeIds ?? [];
        if (trades.Count > 0 && await db.Trades.CountAsync(t => trades.Contains(t.Id), ct) != trades.Distinct().Count()) v.Add("tradeIds", "Unknown trade.");
        if (i.UserId is { } uid)
        {
            if (!await db.Users.People().AnyAsync(u => u.Id == uid && u.TenantId == db.CurrentTenantId, ct)) v.Add("userId", "Unknown user.");
            else if (await db.Employees.AnyAsync(e => e.UserId == uid && e.Id != id, ct)) v.Add("userId", "This login is already linked to another person.");
        }
        return v;
    }

    internal static void Apply(Employee e, EmployeeInput i)
    {
        e.JobTitle = i.JobTitle.Clean(); e.Email = i.Email.Clean(); e.Phone = i.Phone.Clean(); e.TradeIds = (i.TradeIds ?? []).Distinct().ToList();
        e.SiteIds = (i.SiteIds ?? []).Distinct().ToList(); e.HourlyRate = i.HourlyRate; e.OvertimeRate = i.OvertimeRate; e.IsContractor = i.IsContractor;
        e.Company = i.Company.Clean(); e.UserId = i.UserId; e.Notes = i.Notes.Clean(); e.IsActive = i.IsActive;
        // Null keeps the person's hours (older apps, the import); an empty list goes back to the default.
        if (i.WorkingHours is not null) e.WorkingHours = i.WorkingHours.Count == 0 ? null : i.WorkingHours.OrderBy(d => d.Day).ToList();
    }

    /// <summary>Generic code/name CRUD for simple lookups.</summary>
    internal static void MapLookup<T>(RouteGroupBuilder api, string path, string tag, Func<CmmsDbContext, DbSet<T>> set, Func<T> create, string managePermission,
        Func<CmmsDbContext, Guid, CancellationToken, Task<bool>> inUse, Action<T, string?>? setColor, Func<T, string?>? getColor) where T : TenantEntity, ICodeLookup
    {
        var g = api.MapGroup(path).WithTags(tag);
        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            (await set(db).AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)).Select(x => LookupRow(x, getColor)))
            .RequireAuthorization();
        g.MapPost("/", async (LookupInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateLookupAsync(set(db), input, null, ct);
            if (!v.IsValid) return v.Problem();
            var item = create();
            ApplyLookup(item, input, setColor);
            set(db).Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api{path}/{item.Id}", new { item.Id });
        }).RequirePermission(managePermission);
        g.MapPut("/{id:guid}", async (Guid id, LookupInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var item = await set(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var v = await ValidateLookupAsync(set(db), input, id, ct);
            if (!v.IsValid) return v.Problem();
            ApplyLookup(item, input, setColor);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { item.Id });
        }).RequirePermission(managePermission);
        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var item = await set(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            if (await inUse(db, id, ct)) throw new DomainException("This item is in use. Mark it inactive instead.");
            set(db).Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(managePermission);
    }

    /// <summary>The checks that need no database (shared with the import).</summary>
    internal static Validator LookupRules(LookupInput i) => new Validator().Required("code", i.Code, "Code", 50).Required("name", i.Name, "Name", 200)
        .MaxLength("nameAr", i.NameAr, "Name (Arabic)", 200);

    /// <summary>A list row as edit forms read it: raw names, and the Arabic name only for the lists that have one.</summary>
    private static object LookupRow<T>(T x, Func<T, string?>? getColor) where T : TenantEntity, ICodeLookup => x is IBilingualName b
        ? new { x.Id, x.Code, x.Name, b.NameAr, Color = getColor?.Invoke(x), x.IsActive }
        : new { x.Id, x.Code, x.Name, Color = getColor?.Invoke(x), x.IsActive };

    internal static void ApplyLookup<T>(T item, LookupInput i, Action<T, string?>? setColor) where T : ICodeLookup
    {
        item.Code = i.Code.Trim(); item.Name = i.Name.Trim(); item.IsActive = i.IsActive; setColor?.Invoke(item, i.Color.Clean());
        if (item is IBilingualName b && i.NameAr is not null) b.NameAr = i.NameAr.Clean();
    }

    private static async Task<Validator> ValidateLookupAsync<T>(DbSet<T> set, LookupInput i, Guid? id, CancellationToken ct) where T : TenantEntity, ICodeLookup
    {
        var v = LookupRules(i);
        if (!string.IsNullOrWhiteSpace(i.Code))
        {
            var code = i.Code.Trim();
            if (await set.AnyAsync(x => x.Code == code && x.Id != id, ct)) v.Add("code", "This code is already used.");
        }
        return v;
    }
}
