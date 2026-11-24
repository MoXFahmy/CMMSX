using System.Linq.Expressions;
using System.Security.Claims;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Service contracts (0.17.0): terms, periods, amendments, covered assets, termination and files.</summary>
public static class ContractEndpoints
{
    public sealed class ContractFilter
    {
        public string? Search { get; set; }
        /// <summary>Comma list of Upcoming, Active, Expired, Terminated.</summary>
        public string? Status { get; set; }
        public Guid? SupplierId { get; set; }
        public Guid? ContractTypeId { get; set; }
        public Guid? SiteId { get; set; }
        /// <summary>Only contracts in their alert window today.</summary>
        public bool? Attention { get; set; }
        public string? Sort { get; set; }
        public string? Dir { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record ContractListItem(Guid Id, string Number, string Title, string CompanyName, string? TypeName, string? SiteName, DateOnly StartDate, DateOnly EndDate,
        ContractStatus Status, decimal? Value, string? Currency, DateOnly? Deadline, bool NeedsAttention, bool AutoRenew);

    public sealed record ContractAssetInput(Guid AssetId, decimal? YearlyCost);
    public sealed record ContractInput(string Title, Guid SupplierId, Guid? ManagerId, Guid? ContractTypeId, Guid? TradeId, Guid? SiteId, string? Notes,
        DateOnly StartDate, DateOnly EndDate, decimal? Value, string? Currency, string? PaymentTerms, int? ExpectedWorkOrders, ExpectedPer? ExpectedPer,
        bool AutoRenew, int? RenewalMonths, int NoticeDays, int AlertDays, List<ContractAssetInput>? Assets, string? RowVersion);
    public sealed record AmendmentInput(DateOnly Date, string Description, decimal? NewValue);
    public sealed record TerminateInput(DateOnly Date, string Reason);

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Today in the organization's time zone (contract dates are local days).</summary>
    internal static async Task<DateOnly> TodayAsync(CmmsDbContext db, CancellationToken ct)
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Sla.Zone(settings.TimeZone)).DateTime);
    }

    /// <summary>Contracts in their alert window on <paramref name="today"/> (the same rule as <see cref="ContractTerms.NeedsAttention"/>, in SQL).</summary>
    internal static IQueryable<Contract> NeedingAttention(IQueryable<Contract> q, DateOnly today) =>
        q.Where(c => c.TerminatedOn == null
                     && (c.AutoRenew ? c.EndDate.AddDays(-c.NoticeDays) : c.EndDate) >= today
                     && (c.AutoRenew ? c.EndDate.AddDays(-c.NoticeDays - c.AlertDays) : c.EndDate.AddDays(-c.AlertDays)) <= today);

    /// <summary>Contracts in any of <paramref name="statuses"/> on <paramref name="today"/> (the rule of <see cref="ContractTerms.StatusOn"/>, in SQL).</summary>
    internal static IQueryable<Contract> WithStatus(IQueryable<Contract> q, ICollection<ContractStatus> statuses, DateOnly today)
    {
        if (statuses.Count == 0) return q;
        bool t = statuses.Contains(ContractStatus.Terminated), u = statuses.Contains(ContractStatus.Upcoming),
            e = statuses.Contains(ContractStatus.Expired), a = statuses.Contains(ContractStatus.Active);
        return q.Where(c => (t && c.TerminatedOn != null && c.TerminatedOn <= today)
            || (!(c.TerminatedOn != null && c.TerminatedOn <= today) && (
                (u && c.StartDate > today)
                || (e && c.StartDate <= today && c.EndDate < today && !(c.AutoRenew && c.TerminatedOn == null))
                || (a && c.StartDate <= today && (c.EndDate >= today || (c.AutoRenew && c.TerminatedOn == null))))));
    }

    /// <summary>Why a contract can't be newly named on work or a PM at <paramref name="siteId"/>, or null when it can.</summary>
    internal static async Task<string?> ChoiceProblemAsync(CmmsDbContext db, Guid contractId, Guid siteId, CancellationToken ct)
    {
        var c = await db.Contracts.AsNoTracking().Where(x => x.Id == contractId)
            .Select(x => new { x.SiteId, x.StartDate, x.EndDate, x.AutoRenew, x.TerminatedOn }).FirstOrDefaultAsync(ct);
        if (c is null) return "Choose a contract.";
        if (c.SiteId is { } s && s != siteId) return "This contract is for another site.";
        var status = ContractTerms.StatusOn(await TodayAsync(db, ct), c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn);
        return status is ContractStatus.Expired or ContractStatus.Terminated ? "This contract has ended." : null;
    }

    /// <summary>The active contract covering <paramref name="assetId"/> ("Covered by CON-00003 · Lift Co"): the one named on the work first, else the lowest number.</summary>
    internal static async Task<object?> CoveredByAsync(CmmsDbContext db, Guid? assetId, Guid? preferred, CancellationToken ct)
    {
        if (assetId is not { } asset) return null;
        var today = await TodayAsync(db, ct);
        return await WithStatus(db.Contracts.AsNoTracking().Where(c => c.Assets.Any(a => a.AssetId == asset)), [ContractStatus.Active], today)
            .OrderBy(c => c.Id == preferred ? 0 : 1).ThenBy(c => c.Number)
            .Select(c => new { c.Id, c.Number, c.Title, CompanyName = c.Supplier!.Name }).FirstOrDefaultAsync(ct);
    }

    internal static async Task<PagedResult<ContractListItem>> ListAsync(CmmsDbContext db, ContractFilter f, int page, int pageSize, CancellationToken ct, int maxPageSize = 500)
    {
        var today = await TodayAsync(db, ct);
        var q = db.Contracts.AsNoTracking();
        if (f.Search.Clean() is { } s) q = q.Where(c => c.Number.Contains(s) || c.Title.Contains(s) || c.Supplier!.Name.Contains(s) || c.Notes!.Contains(s));
        var statuses = (f.Status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Enum.TryParse<ContractStatus>(x, true, out var v) ? v : (ContractStatus?)null).OfType<ContractStatus>().ToList();
        q = WithStatus(q, statuses, today);
        if (f.SupplierId is { } sup) q = q.Where(c => c.SupplierId == sup);
        if (f.ContractTypeId is { } type) q = q.Where(c => c.ContractTypeId == type);
        if (f.SiteId is { } site) q = q.Where(c => c.SiteId == site);
        if (f.Attention == true) q = NeedingAttention(q, today);
        var map = new Dictionary<string, Expression<Func<Contract, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = c => c.Number, ["title"] = c => c.Title, ["company"] = c => c.Supplier!.Name, ["startDate"] = c => c.StartDate, ["endDate"] = c => c.EndDate,
            ["value"] = c => c.Value,
        };
        var paged = await q.SortBy(f.Sort ?? "number", f.Dir ?? (f.Sort is null ? "desc" : f.Dir), map, "number")
            .Select(c => new
            {
                c.Id, c.Number, c.Title, CompanyName = c.Supplier!.Name, TypeName = c.ContractType == null ? null : c.ContractType.Name, SiteName = c.Site == null ? null : c.Site.Name,
                c.StartDate, c.EndDate, c.AutoRenew, c.NoticeDays, c.AlertDays, c.TerminatedOn, c.Value, c.Currency,
            }).ToPagedAsync(page, pageSize, ct, maxPageSize);
        return new PagedResult<ContractListItem>(paged.Items.Select(c =>
        {
            var deadline = ContractTerms.Deadline(c.AutoRenew, c.EndDate, c.NoticeDays, c.TerminatedOn);
            return new ContractListItem(c.Id, c.Number, c.Title, c.CompanyName, c.TypeName, c.SiteName, c.StartDate, c.EndDate,
                ContractTerms.StatusOn(today, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn), c.Value, c.Currency, deadline,
                ContractTerms.NeedsAttention(today, deadline, c.AlertDays), c.AutoRenew);
        }).ToList(), paged.Total, paged.Page, paged.PageSize);
    }

    public static void MapContractEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/contracts").WithTags("Contracts");

        g.MapGet("/", ([AsParameters] ContractFilter f, CmmsDbContext db, CancellationToken ct) => ListAsync(db, f, f.Page ?? 1, f.PageSize ?? 25, ct))
            .RequirePermission(Permissions.ContractsView);
        g.MapGet("/export", ([AsParameters] ContractFilter f, HttpContext http, CmmsDbContext db, CancellationToken ct) =>
            ListExport.FileAsync(http, db, "contracts", (page, size) => ListAsync(db, f, page, size, ct, ListExport.MaxRows), ct))
            .RequirePermission(Permissions.ContractsView);

        // The contract picker on work orders and PMs: active and upcoming contracts usable at the site, those covering the asset first.
        g.MapGet("/options", async (Guid? siteId, Guid? assetId, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            // People who plan work or PMs, or may see contracts; not every signed-in user.
            var mine = await perms.GetAsync(UserId(http));
            if (!new[] { Permissions.ContractsView, Permissions.WorkOrdersCreate, Permissions.WorkOrdersEdit, Permissions.PmManage }.Any(mine.Contains)) return Results.Forbid();
            var today = await TodayAsync(db, ct);
            var q = WithStatus(db.Contracts.AsNoTracking(), [ContractStatus.Active, ContractStatus.Upcoming], today);
            if (siteId is { } site) q = q.Where(c => c.SiteId == null || c.SiteId == site);
            var list = await q.Select(c => new
            {
                c.Id, c.Number, c.Title, CompanyName = c.Supplier!.Name, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn,
                Covers = assetId != null && c.Assets.Any(a => a.AssetId == assetId),
            }).ToListAsync(ct);
            return Results.Ok(list.OrderByDescending(c => c.Covers).ThenBy(c => c.Number)
                .Select(c => new { c.Id, c.Number, c.Title, c.CompanyName, Status = ContractTerms.StatusOn(today, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn), c.Covers }));
        }).RequireAuthorization();

        // An asset's contracts (the asset page's card).
        api.MapGet("/assets/{id:guid}/contracts", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            var today = await TodayAsync(db, ct);
            var rows = await db.ContractAssets.AsNoTracking().Where(ca => ca.AssetId == id)
                .Join(db.Contracts.AsNoTracking(), ca => ca.ContractId, c => c.Id, (ca, c) => new
                {
                    c.Id, c.Number, c.Title, CompanyName = c.Supplier!.Name, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn, ca.YearlyCost,
                }).ToListAsync(ct);
            return Results.Ok(rows.OrderByDescending(r => r.EndDate).Select(r => new
            {
                r.Id, r.Number, r.Title, r.CompanyName, Status = ContractTerms.StatusOn(today, r.StartDate, r.EndDate, r.AutoRenew, r.TerminatedOn), r.StartDate, r.EndDate, r.YearlyCost,
            }));
        }).WithTags("Contracts").RequirePermission(Permissions.ContractsView);

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, HttpContext http, PermissionService perms, CancellationToken ct) =>
        {
            var c = await db.Contracts.AsNoTracking().Include(x => x.Supplier).Include(x => x.ContractType).Include(x => x.Site)
                .Include(x => x.Periods).Include(x => x.Amendments).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var today = await TodayAsync(db, ct);
            var mine = await perms.GetAsync(UserId(http));
            var manage = mine.Contains(Permissions.ContractsManage);
            var manager = c.ManagerId is { } mid ? await db.Users.AsNoTracking().Where(u => u.Id == mid).Select(u => new { u.Id, Name = u.FullName }).FirstOrDefaultAsync(ct) : null;
            var trade = c.TradeId is { } tid ? await db.Trades.AsNoTracking().Where(t => t.Id == tid).Select(t => new { t.Id, t.Code, t.Name }).FirstOrDefaultAsync(ct) : null;
            // Covered assets the person may see (the asset's own site filter hides the others).
            var assets = await (from ca in db.ContractAssets.AsNoTracking() join a in db.Assets.AsNoTracking() on ca.AssetId equals a.Id
                                where ca.ContractId == id orderby a.AssetNumber
                                select new { ca.AssetId, a.AssetNumber, a.Name, SiteName = a.Site!.Name, ca.YearlyCost }).ToListAsync(ct);
            var work = db.WorkOrders.AsNoTracking().Where(w => w.ContractId == id);
            var workOrders = await work.OrderByDescending(w => w.CreatedAt).Take(50)
                .Select(w => new { w.Id, w.Number, w.Title, w.Status, w.CompletedAt, w.DueDate }).ToListAsync(ct);
            var deadline = ContractTerms.Deadline(c.AutoRenew, c.EndDate, c.NoticeDays, c.TerminatedOn);
            var status = ContractTerms.StatusOn(today, c.StartDate, c.EndDate, c.AutoRenew, c.TerminatedOn);
            return Results.Ok(new
            {
                c.Id, c.Number, c.Title, Status = status, Deadline = deadline, NeedsAttention = ContractTerms.NeedsAttention(today, deadline, c.AlertDays),
                c.SupplierId, Company = new { c.Supplier!.Id, c.Supplier.Code, c.Supplier.Name, c.Supplier.ContactName, c.Supplier.Phone, c.Supplier.Email },
                c.ManagerId, Manager = manager, c.ContractTypeId, Type = c.ContractType is null ? null : new { c.ContractType.Id, c.ContractType.Code, c.ContractType.Name },
                c.TradeId, Trade = trade, c.SiteId, Site = c.Site is null ? null : new { c.Site.Id, c.Site.Code, c.Site.Name },
                c.Notes, c.StartDate, c.EndDate, c.Value, c.Currency, c.PaymentTerms, c.ExpectedWorkOrders, c.ExpectedPer,
                c.AutoRenew, c.RenewalMonths, c.NoticeDays, c.AlertDays, c.TerminatedOn, c.TerminationReason,
                Periods = c.Periods.OrderBy(p => p.Number).Select(p => new { p.Id, p.Number, p.StartDate, p.EndDate, p.Value, p.IsRenewal }),
                Amendments = c.Amendments.OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
                    .Select(a => new { a.Id, a.Date, a.Description, a.NewValue, a.CreatedAt, a.CreatedByName }),
                Assets = assets, WorkOrders = workOrders, WorkOrderCount = await work.CountAsync(ct),
                Actions = new
                {
                    Edit = manage, Amend = manage && c.TerminatedOn is null, Terminate = manage && c.TerminatedOn is null,
                    Delete = manage && !await db.WorkOrders.AllSites().AnyAsync(w => w.ContractId == id, ct) && !await db.PmSchedules.AllSites().AnyAsync(p => p.ContractId == id, ct),
                },
                c.CreatedAt, c.UpdatedAt, RowVersion = Convert.ToBase64String(c.RowVersion),
            });
        }).RequirePermission(Permissions.ContractsView);

        g.MapPost("/", async (ContractInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(db, i, null, ct);
            if (!v.IsValid) return v.Problem();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            var (sequence, prefix) = Numbering.For(settings, null, NumberKind.Contract);
            var number = await Numbering.NextAsync(db, sequence, prefix, 5,
                n => db.Contracts.AllSites().AnyAsync(c => c.Number == n, ct),
                p => db.Contracts.AllSites().Where(c => c.Number.StartsWith(p)).Select(c => c.Number).ToListAsync(ct), ct);
            var c = new Contract { Number = number, Title = "" };
            Apply(c, i, settings.Currency);
            c.Periods.Add(new ContractPeriod { ContractId = c.Id, Number = 1, StartDate = i.StartDate, EndDate = i.EndDate, Value = i.Value, CreatedAt = DateTimeOffset.UtcNow });
            foreach (var a in Distinct(i.Assets)) c.Assets.Add(new ContractAsset { ContractId = c.Id, AssetId = a.AssetId, YearlyCost = a.YearlyCost });
            db.Contracts.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/contracts/{c.Id}", new { c.Id, c.Number });
        }).RequirePermission(Permissions.ContractsManage);

        g.MapPut("/{id:guid}", async (Guid id, ContractInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.Contracts.Include(x => x.Periods).Include(x => x.Assets).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = await ValidateAsync(db, i, c, ct);
            if (!v.IsValid) return v.Problem();
            var renewed = c.Periods.Count > 1;
            if (renewed && i.Value != c.Value) throw new DomainException("Record the new value as an amendment.");
            if (renewed && i.StartDate != c.StartDate) throw new DomainException("The start date can't change after a renewal.");
            if (i.RowVersion is not null) db.Entry(c).Property(x => x.RowVersion).OriginalValue = i.RowVersion.ToVersion();
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
            Apply(c, i, settings.Currency);
            var latest = c.Periods.OrderBy(p => p.Number).Last();
            latest.EndDate = i.EndDate;
            if (!renewed) { latest.StartDate = i.StartDate; latest.Value = i.Value; }
            if (latest.EndDate < latest.StartDate) throw new DomainException("The end date must be on or after the start date.");
            // Covered assets: only those the person can see change; the others stay as they are.
            var visible = await db.Assets.Where(a => c.Assets.Select(x => x.AssetId).Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
            var wanted = Distinct(i.Assets).ToList();
            c.Assets.RemoveAll(x => visible.Contains(x.AssetId) && !wanted.Any(w => w.AssetId == x.AssetId));
            foreach (var w in wanted)
            {
                var row = c.Assets.FirstOrDefault(x => x.AssetId == w.AssetId);
                if (row is null) { row = new ContractAsset { ContractId = c.Id, AssetId = w.AssetId, TenantId = c.TenantId }; c.Assets.Add(row); db.Entry(row).State = EntityState.Added; }
                row.YearlyCost = w.YearlyCost;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.ContractsManage);

        g.MapPost("/{id:guid}/amendments", async (Guid id, AmendmentInput i, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var c = await db.Contracts.Include(x => x.Periods).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var v = new Validator().When(string.IsNullOrWhiteSpace(i.Description), "description", "Describe the amendment.")
                .MaxLength("description", i.Description, "Description", 2000).When(i.NewValue < 0, "newValue", "The value cannot be negative.");
            if (!v.IsValid) return v.Problem();
            var a = new ContractAmendment { ContractId = id, TenantId = c.TenantId, Date = i.Date, Description = i.Description.Trim(), NewValue = i.NewValue, CreatedAt = DateTimeOffset.UtcNow, CreatedByName = ctx.UserName };
            db.ContractAmendments.Add(a);
            // A new value applies to the current period and the renewals after it.
            // The contract's value follows too, so the edit form (which sends it back) and the list show it.
            if (i.NewValue is { } value) { c.Periods.OrderBy(p => p.Number).Last().Value = value; c.Value = value; }
            c.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { a.Id });
        }).RequirePermission(Permissions.ContractsManage);

        g.MapPost("/{id:guid}/terminate", async (Guid id, TerminateInput i, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.Contracts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(i.Reason)) return new Validator().When(true, "reason", "Give a reason.").Problem();
            if (i.Reason.Trim().Length > 1000) return new Validator().MaxLength("reason", i.Reason, "Reason", 1000).Problem();
            c.TerminatedOn = i.Date; c.TerminationReason = i.Reason.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Id });
        }).RequirePermission(Permissions.ContractsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var c = await db.Contracts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (await db.WorkOrders.AllSites().AnyAsync(w => w.ContractId == id, ct) || await db.PmSchedules.AllSites().AnyAsync(p => p.ContractId == id, ct))
                throw new DomainException("This contract is named on work orders or PMs. Terminate it instead.");
            db.Contracts.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ContractsManage);
    }

    private static IEnumerable<ContractAssetInput> Distinct(List<ContractAssetInput>? assets) => (assets ?? []).GroupBy(a => a.AssetId).Select(g => g.Last());

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, ContractInput i, Contract? existing, CancellationToken ct)
    {
        var v = new Validator().MaxLength("title", i.Title, "Title", 300).MaxLength("notes", i.Notes, "Notes", 4000).MaxLength("paymentTerms", i.PaymentTerms, "Payment terms", 200)
            .When(string.IsNullOrWhiteSpace(i.Title), "title", "Enter a title.")
            .When(i.EndDate < i.StartDate, "endDate", "The end date must be on or after the start date.")
            .When(i.AutoRenew && i.RenewalMonths is not (>= 1 and <= 120), "renewalMonths", "Give the renewal length in months (1–120).")
            .When(i.NoticeDays is < 0 or > 3650 || i.AlertDays is < 0 or > 3650, "noticeDays", "Notice and alert days must be between 0 and 3650.")
            .When(i.Value < 0, "value", "The value cannot be negative.").When(i.ExpectedWorkOrders < 0, "expectedWorkOrders", "The expected number cannot be negative.")
            .When(i.Currency.Clean() is { Length: not 3 }, "currency", "Use a 3-letter currency code.");
        var company = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == i.SupplierId, ct);
        v.Merge(ContractorEndpoints.ValidateContractorCompany(company, existing is null || existing.SupplierId != i.SupplierId));
        if (i.ManagerId is { } m && !await db.Users.People().AnyAsync(u => u.Id == m && u.TenantId == db.CurrentTenantId, ct)) v.Add("managerId", "Choose a user of this organization as manager.");
        if (i.ContractTypeId is { } type && type != existing?.ContractTypeId && !await db.ContractTypes.AnyAsync(t => t.Id == type && t.IsActive, ct)) v.Add("contractTypeId", "Choose an active contract type.");
        if (i.TradeId is { } trade && !await db.Trades.AnyAsync(t => t.Id == trade, ct)) v.Add("tradeId", "Unknown trade.");
        if (i.SiteId is { } site && site != existing?.SiteId && !await db.Sites.AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site.");
        // Covered assets: the ones named must exist and be visible; with a site, every covered asset (seen or not) must be at it.
        var ids = Distinct(i.Assets).Select(a => a.AssetId).ToList();
        var named = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.AssetNumber, a.SiteId }).ToListAsync(ct);
        if (named.Count != ids.Count) v.Add("assets", "Unknown asset.");
        if (i.SiteId is { } contractSite)
        {
            var kept = existing is null ? [] : await (from ca in db.ContractAssets.AsNoTracking() join a in db.Assets.AllSites().AsNoTracking() on ca.AssetId equals a.Id
                                                      where ca.ContractId == existing.Id && !ids.Contains(a.Id) && !db.Assets.Any(x => x.Id == a.Id)
                                                      select new { a.Id, a.AssetNumber, a.SiteId }).ToListAsync(ct);
            foreach (var a in named.Concat(kept).Where(a => a.SiteId != contractSite))
                v.Add("assets", $"Asset {a.AssetNumber} is at another site than the contract.");
        }
        return v;
    }

    private static void Apply(Contract c, ContractInput i, string baseCurrency)
    {
        c.Title = i.Title.Trim(); c.SupplierId = i.SupplierId; c.ManagerId = i.ManagerId; c.ContractTypeId = i.ContractTypeId; c.TradeId = i.TradeId; c.SiteId = i.SiteId;
        c.Notes = i.Notes.Clean(); c.StartDate = i.StartDate; c.EndDate = i.EndDate; c.Value = i.Value; c.Currency = i.Currency.Clean()?.ToUpperInvariant() ?? baseCurrency;
        c.PaymentTerms = i.PaymentTerms.Clean(); c.ExpectedWorkOrders = i.ExpectedWorkOrders; c.ExpectedPer = i.ExpectedWorkOrders is null ? null : i.ExpectedPer ?? ExpectedPer.Year;
        c.AutoRenew = i.AutoRenew; c.RenewalMonths = i.RenewalMonths; c.NoticeDays = i.NoticeDays; c.AlertDays = i.AlertDays;
    }
}
