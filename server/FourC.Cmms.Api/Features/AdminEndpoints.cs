using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Settings;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using FourC.Cmms.Core.Work;
using FourC.Cmms.Core.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public static class AdminEndpoints
{
    public sealed record UserInput(string FullName, string Email, string? JobTitle, string? Phone, bool IsActive, List<Guid> RoleIds, string? Password, decimal? PurchaseApprovalLimit = null,
        List<Guid>? SiteIds = null, List<Guid>? LocationIds = null);
    /// <summary>A role. Home and HiddenModules (0.22.0) are left unchanged when null, so older clients never clear them.</summary>
    public sealed record RoleInput(string Name, string? Description, List<string> Permissions, List<HomeCardInput>? Home = null, List<string>? HiddenModules = null);
    public sealed record SettingsInput(string Name, TenantSettings Settings);
    public sealed record ListLabelsInput(Dictionary<string, Dictionary<string, ListLabel>>? ListLabels);
    public sealed record PasswordInput(string Password);

    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        // Active people to pick (a site's request recipients) without the right to manage users.
        api.MapGet("/users/lookup", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.TenantId == db.CurrentTenantId && u.IsActive).OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
            .WithTags("Users").RequirePermission(Permissions.LocationsManage);

        var users = api.MapGroup("/users").WithTags("Users").RequirePermission(Permissions.UsersManage);

        users.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.TenantId == db.CurrentTenantId).OrderBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id, u.FullName, u.Email, u.JobTitle, u.Phone, u.IsActive, u.LastLoginAt, u.CreatedAt, u.PurchaseApprovalLimit, u.SiteIds, u.LocationIds,
                    Roles = db.UserRoles.Where(r => r.UserId == u.Id).Select(r => new { r.RoleId, r.Role!.Name }).ToList(),
                }).ToListAsync(ct));

        users.MapPost("/", async (UserInput input, CmmsDbContext db, UserManager<AppUser> um, IPasswordHasher<AppUser> hasher, HttpContext http, CancellationToken ct) =>
        {
            var v = await ValidateUserAsync(input, null, db, um, ct);
            if (string.IsNullOrWhiteSpace(input.Password)) v.Add("password", "Set an initial password.");
            var access = await CheckAccessAsync(input, null, db, Access.Of(http) ?? UserAccess.Everything, v, ct);
            if (!v.IsValid) return v.Problem();
            var user = TenantProvisioning.NewUser(db.CurrentTenantId!.Value, input.FullName, input.Email);
            user.JobTitle = input.JobTitle.Clean(); user.Phone = input.Phone.Clean(); user.IsActive = input.IsActive; user.PurchaseApprovalLimit = input.PurchaseApprovalLimit;
            (user.SiteIds, user.LocationIds) = access;
            user.PasswordHash = hasher.HashPassword(user, input.Password!);
            db.Users.Add(user);
            db.UserRoles.AddRange(input.RoleIds.Distinct().Select(r => new UserRole { UserId = user.Id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", new { user.Id });
        });

        users.MapPut("/{id:guid}", async (Guid id, UserInput input, CmmsDbContext db, UserManager<AppUser> um, ITenantContext ctx, HttpContext http, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId, ct);
            if (user is null) return Results.NotFound();
            var editor = Access.Of(http) ?? UserAccess.Everything;
            if (await UserAccessRules.OutsideAsync(db, editor, user, ct) is { } outside) throw new DomainException(outside);
            var v = await ValidateUserAsync(input, id, db, um, ct);
            var access = await CheckAccessAsync(input, user, db, editor, v, ct);
            if (id == ctx.UserId && !input.IsActive) v.Add("isActive", "You cannot deactivate your own account.");
            var adminRole = await db.Roles.Where(r => r.IsSystem).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (id == ctx.UserId && !input.RoleIds.Contains(adminRole) && await db.UserRoles.AnyAsync(r => r.UserId == id && r.RoleId == adminRole, ct))
                v.Add("roleIds", "You cannot remove the Administrator role from yourself.");
            if (!v.IsValid) return v.Problem();
            var emailChanged = !string.Equals(user.Email, input.Email.Trim(), StringComparison.OrdinalIgnoreCase);
            user.FullName = input.FullName.Trim(); user.JobTitle = input.JobTitle.Clean(); user.Phone = input.Phone.Clean(); user.IsActive = input.IsActive; user.PurchaseApprovalLimit = input.PurchaseApprovalLimit;
            if (emailChanged)
            {
                user.Email = user.UserName = input.Email.Trim();
                user.NormalizedEmail = user.NormalizedUserName = input.Email.Trim().ToUpperInvariant();
            }
            if (emailChanged || !input.IsActive) user.SecurityStamp = Guid.NewGuid().ToString("N");
            (user.SiteIds, user.LocationIds) = access;
            var existing = await db.UserRoles.Where(r => r.UserId == id).ToListAsync(ct);
            db.UserRoles.RemoveRange(existing.Where(r => !input.RoleIds.Contains(r.RoleId)));
            db.UserRoles.AddRange(input.RoleIds.Distinct().Where(r => existing.All(e => e.RoleId != r)).Select(r => new UserRole { UserId = id, RoleId = r }));
            await db.SaveChangesAsync(ct);
            PermissionService.Invalidate(user.TenantId);
            AccessService.Invalidate(user.Id);
            return Results.Ok(new { user.Id });
        });

        users.MapPost("/{id:guid}/password", async (Guid id, PasswordInput input, CmmsDbContext db, UserManager<AppUser> um, HttpContext http, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.CurrentTenantId, ct);
            if (user is null) return Results.NotFound();
            if (await UserAccessRules.OutsideAsync(db, Access.Of(http) ?? UserAccess.Everything, user, ct) is { } outside) throw new DomainException(outside);
            var v = new Validator();
            foreach (var pv in um.PasswordValidators)
                foreach (var e in (await pv.ValidateAsync(um, user, input.Password ?? "")).Errors) v.Add("password", e.Description);
            if (!v.IsValid) return v.Problem();
            user.PasswordHash = um.PasswordHasher.HashPassword(user, input.Password!);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.LockoutEnd = null; user.AccessFailedCount = 0;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var roles = api.MapGroup("/roles").WithTags("Roles").RequirePermission(Permissions.UsersManage);
        roles.MapGet("/permissions", () => Permissions.All);
        roles.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var counts = await db.UserRoles.Where(r => db.Roles.Any(x => x.Id == r.RoleId)).GroupBy(r => r.RoleId).Select(x => new { x.Key, C = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
            var list = await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct);
            var columns = (await db.RoleListSettings.AsNoTracking().Select(x => new { x.RoleId, x.List }).ToListAsync(ct)).ToLookup(x => x.RoleId, x => x.List);
            return list.Select(r => new
            {
                r.Id, r.Name, r.Description, r.IsSystem, r.Permissions, UserCount = counts.GetValueOrDefault(r.Id),
                Home = r.HomeLayout, r.HiddenModules, DefaultColumns = columns[r.Id].Order().ToList(),
            });
        });
        roles.MapGet("/menu-modules", () => MenuModules.All.Select(m => new { m.Key, m.AnyOf }));
        roles.MapPost("/", async (RoleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateRoleAsync(input, null, db, ct);
            if (!v.IsValid) return v.Problem();
            var role = new Role
            {
                Name = input.Name.Trim(), Description = input.Description.Clean(), Permissions = CleanPermissions(input.Permissions),
                HomeLayout = RoleHomes.CleanHome(input.Home), HiddenModules = RoleHomes.CleanModules(input.HiddenModules ?? []),
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/roles/{role.Id}", new { role.Id });
        });
        roles.MapPut("/{id:guid}", async (Guid id, RoleInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.NotFound();
            var v = await ValidateRoleAsync(input, id, db, ct);
            if (!v.IsValid) return v.Problem();
            role.Name = role.IsSystem ? role.Name : input.Name.Trim();
            role.Description = input.Description.Clean();
            role.Permissions = role.IsSystem ? Permissions.All.Select(p => p.Code).ToList() : CleanPermissions(input.Permissions);
            if (input.Home is not null) role.HomeLayout = RoleHomes.CleanHome(input.Home);
            if (input.HiddenModules is not null) role.HiddenModules = RoleHomes.CleanModules(input.HiddenModules);
            await db.SaveChangesAsync(ct);
            PermissionService.Invalidate(role.TenantId);
            return Results.Ok(new { role.Id });
        });
        // A copy with the same permissions, home, hidden menu items and default columns, and nobody in it (0.22.0).
        roles.MapPost("/{id:guid}/duplicate", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.NotFound();
            var taken = (await db.Roles.Select(r => r.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = CopyName(role.Name, taken);
            var copy = new Role
            {
                Name = name, Description = role.Description, Permissions = role.IsSystem ? Permissions.All.Select(p => p.Code).ToList() : [.. role.Permissions],
                HomeLayout = role.HomeLayout, HiddenModules = [.. role.HiddenModules],
            };
            db.Roles.Add(copy);
            foreach (var c in await db.RoleListSettings.AsNoTracking().Where(x => x.RoleId == id).ToListAsync(ct))
                db.RoleListSettings.Add(new RoleListSetting { RoleId = copy.Id, List = c.List, HiddenColumns = [.. c.HiddenColumns] });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/roles/{copy.Id}", new { copy.Id, copy.Name });
        });
        roles.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.NotFound();
            if (role.IsSystem) throw new DomainException("The Administrator role cannot be deleted.");
            if (await db.UserRoles.AnyAsync(r => r.RoleId == id, ct)) throw new DomainException("This role is assigned to users. Remove it from them first.");
            db.Roles.Remove(role);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => new { t.Name, t.Slug, t.Settings }).FirstAsync(ct))
            .RequireAuthorization();
        settings.MapPut("/", async (SettingsInput input, CmmsDbContext db, FourC.Cmms.Api.Features.Ai.IAiClient ai,
            Microsoft.Extensions.Options.IOptions<FourC.Cmms.Api.Features.Ai.AiOptions> aiOptions, CancellationToken ct) =>
        {
            // A list sent as null means "none"; never store null where generation expects a list.
            input.Settings.PmNonWorkingDays ??= [];
            input.Settings.PmSkipAssetStatuses ??= [];
            input.Settings.TerminologyArabic ??= Terminology.Arabic();
            input.Settings.SlaTargets ??= [];
            input.Settings.CommentRequiredFor = (input.Settings.CommentRequiredFor ?? []).Distinct().ToList();
            input.Settings.EmailRecipients = (input.Settings.EmailRecipients ?? []).ToDictionary(x => x.Key, x => (x.Value ?? []).Distinct().ToList());
            var v = new Validator().Required("name", input.Name, "Organization name", 200)
                .MaxLength("settings.assetNumberPrefix", input.Settings.AssetNumberPrefix, "Asset number prefix", 20)
                .When(input.Settings.AssetNumberPadding is < 1 or > 12, "settings.assetNumberPadding", "Padding must be between 1 and 12.")
                .When(!Languages.IsSupported(input.Settings.DefaultLanguage), "settings.defaultLanguage", "Choose a language CMX has.")
                .When(input.Settings.PmNonWorkingDays.Distinct().Count() >= 7, "settings.pmNonWorkingDays", "Leave at least one working day.")
                .When(!TimeZoneInfo.TryFindSystemTimeZoneById(input.Settings.TimeZone ?? "", out _), "settings.timeZone", "Choose a time zone from the list.")
                .When(input.Settings.DailyCapacityHours is < 1 or > 24, "settings.dailyCapacityHours", "Hours per day must be between 1 and 24.")
                .When(FourC.Cmms.Core.People.WorkingHours.Problem(input.Settings.WorkingHours) is not null, "settings.workingHours",
                    FourC.Cmms.Core.People.WorkingHours.Problem(input.Settings.WorkingHours) ?? "")
                .When(input.Settings.SlaTargets.GroupBy(x => x.Priority).Any(g => g.Count() > 1), "settings.slaTargets", "Set each priority's targets once.")
                .When(input.Settings.SlaTargets.Any(x => !Enum.IsDefined(x.Priority) || OutOfRange(x.RespondMinutes) || OutOfRange(x.CompleteMinutes)),
                    "settings.slaTargets", "Targets must be between 1 minute and 365 days.")
                .When(!Enum.IsDefined(input.Settings.FollowUpDefault) || input.Settings.FollowUpDefault == FourC.Cmms.Core.Pm.FollowUpKind.Default, "settings.followUpDefault",
                    "Choose what a failed step raises.")
                .When(input.Settings.FollowUpDefault == FourC.Cmms.Core.Pm.FollowUpKind.WorkOrder && input.Settings.FollowUpTemplateId is null, "settings.followUpTemplateId",
                    PmEndpoints.NeedsTemplate);
            if (input.Settings.FollowUpTemplateId is { } fut && !await db.PmTasks.AnyAsync(x => x.Id == fut, ct)) v.Add("settings.followUpTemplateId", "Unknown work template.");
            foreach (var action in input.Settings.CommentRequiredFor.Where(a => !CommentActions.All.Contains(a)))
                v.Add("settings.commentRequiredFor", $"Unknown action: {action}.");
            foreach (var (key, kinds) in input.Settings.EmailRecipients)
            {
                if (!EmailEvents.Grid.TryGetValue(key, out var row)) { v.Add("settings.emailRecipients", $"Unknown e-mail: {key}."); continue; }
                foreach (var kind in kinds.Where(k => !row.Applicable.Contains(k))) v.Add("settings.emailRecipients", $"Unknown recipient: {kind}.");
            }
            foreach (var (k, val) in new[] { ("site", input.Settings.Terminology.Site), ("location", input.Settings.Terminology.Location), ("asset", input.Settings.Terminology.Asset) })
                v.Required($"settings.terminology.{k}", val, "Label", 40);
            // The AI assistant (0.15.0): a limit under the server's ceiling, and switching on needs the server's key.
            var ceiling = Math.Max(0, aiOptions.Value.MonthlyCeiling);
            v.When(input.Settings.AiMonthlyLimit < 0, "settings.aiMonthlyLimit", "The limit can't be below 0.")
             .When(input.Settings.AiMonthlyLimit > ceiling, "settings.aiMonthlyLimit", string.Format("The limit can be at most {0} a month.", ceiling));
            var t = await db.Tenants.FirstAsync(x => x.Id == db.CurrentTenantId, ct);
            if (input.Settings.AiEnabled && !t.Settings.AiEnabled && !ai.Configured) v.Add("settings.aiEnabled", "AI isn't set up on this server.");
            if (!v.IsValid) return v.Problem();
            t.Name = input.Name.Trim();
            // The logo (0.14.0) changes only through its own upload, whatever a page sends.
            input.Settings.LogoVersion = t.Settings.LogoVersion;
            input.Settings.LogoContentType = t.Settings.LogoContentType;
            // The lists' wording (0.21.0) changes only through its own page, whatever this page sends.
            input.Settings.ListLabels = t.Settings.ListLabels ?? [];
            t.Settings = input.Settings;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Name, t.Settings });
        }).RequirePermission(Permissions.SettingsManage);

        // The organization's wording for the fixed lists (0.21.0): names, colours, order and hidden values.
        settings.MapPut("/lists", async (ListLabelsInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var labels = Lists.Clean(input.ListLabels);
            if (Lists.Problem(labels) is { } problem) return new Validator().When(true, "listLabels", problem).Problem();
            var t = await db.Tenants.FirstAsync(x => x.Id == db.CurrentTenantId, ct);
            var copy = System.Text.Json.JsonSerializer.Deserialize<TenantSettings>(System.Text.Json.JsonSerializer.Serialize(t.Settings))!;
            copy.ListLabels = labels;
            t.Settings = copy;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ListLabels = labels });
        }).RequirePermission(Permissions.SettingsManage);

        // The organization's logo (0.14.0), on printed reports and job cards: PNG, JPEG or WebP up to 1 MB, one per organization.
        settings.MapPost("/logo", async (HttpRequest req, CmmsDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) throw new DomainException("Upload must be multipart/form-data.");
            var file = (await req.ReadFormAsync(ct)).Files.FirstOrDefault() ?? throw new DomainException(Logos.ChooseImage);
            if (file.Length > Logos.MaxBytes) throw new DomainException(Logos.TooBig);
            byte[] bytes;
            await using (var s = file.OpenReadStream()) { using var m = new MemoryStream(); await s.CopyToAsync(m, ct); bytes = m.ToArray(); }
            var type = Logos.TypeOf(bytes) ?? throw new DomainException(Logos.ChooseImage);
            await using (var m = new MemoryStream(bytes)) await storage.SaveAsync(Logos.Key(db.CurrentTenantId), m, ct);
            var t = await db.Tenants.FirstAsync(x => x.Id == db.CurrentTenantId, ct);
            t.Settings = Logos.With(t.Settings, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), type);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Settings.LogoVersion });
        }).RequirePermission(Permissions.SettingsManage).DisableAntiforgery();
        settings.MapDelete("/logo", async (CmmsDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var t = await db.Tenants.FirstAsync(x => x.Id == db.CurrentTenantId, ct);
            if (t.Settings.LogoVersion is not null) await storage.DeleteAsync(Logos.Key(db.CurrentTenantId), ct);
            t.Settings = Logos.With(t.Settings, null, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
        settings.MapGet("/logo", async (HttpContext http, CmmsDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            var s = (await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == db.CurrentTenantId, ct)).Settings;
            if (s.LogoVersion is null || await storage.OpenReadAsync(Logos.Key(db.CurrentTenantId), ct) is not { } stream) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400"; // pages ask with ?v={version}, so a new logo is a new address
            return Results.Stream(stream, s.LogoContentType ?? "image/png");
        }).RequireAuthorization();

        settings.MapPost("/sample-data", async (CmmsDbContext db) =>
        {
            // Not tied to the request: if a proxy gives up waiting on a slow database, loading still completes
            // instead of rolling back, and the data is there on the next refresh.
            var ct = CancellationToken.None;
            var created = await SampleData.SeedAsync(db, ct: ct);
            var history = await db.InTransactionAsync(tk => SampleOperations.SeedAsync(db, ct: tk), ct);
            await DueBackfill.RunAsync(db, ct);
            return Results.Ok(new { created, history });
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static bool OutOfRange(int? minutes) => minutes is < 1 or > 365 * 24 * 60;

    /// <summary>"Name (copy)", then "(copy 2)", "(copy 3)"…, shortened so it fits a role name's 100 characters.</summary>
    internal static string CopyName(string name, ISet<string> taken)
    {
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " (copy)" : $" (copy {n})";
            var candidate = (name.Length + suffix.Length > 100 ? name[..(100 - suffix.Length)].TrimEnd() : name) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static List<string> CleanPermissions(List<string>? p) => (p ?? []).Intersect(Permissions.All.Select(x => x.Code)).Distinct().ToList();

    /// <summary>
    /// The person's new site and branch lists (null input keeps what they have). Branches decide the sites (theirs), so the
    /// two never disagree. Someone whose own access is limited can only give access inside it; unchanged lists always pass.
    /// </summary>
    private static async Task<(List<Guid> Sites, List<Guid> Locations)> CheckAccessAsync(UserInput i, AppUser? current, CmmsDbContext db, UserAccess editor,
        Validator v, CancellationToken ct)
    {
        var sites = (i.SiteIds ?? current?.SiteIds ?? []).Distinct().ToList();
        var locationIds = (i.LocationIds ?? current?.LocationIds ?? []).Distinct().ToList();
        var places = await db.Locations.AllSites().AsNoTracking().Where(l => locationIds.Contains(l.Id)).Select(l => new { l.Id, l.SiteId, l.Path }).ToListAsync(ct);
        // Places deleted since they were given stay listed (the person keeps seeing nothing there); only new ones must exist.
        var missing = locationIds.Where(l => places.All(p => p.Id != l)).ToList();
        if (missing.Any(l => current is null || !current.LocationIds.Contains(l))) v.Add("locationIds", "Unknown location.");
        if (places.Count > 0) sites = places.Select(p => p.SiteId).Distinct().ToList();
        else if (sites.Count > 0 && await db.Sites.AllSites().CountAsync(s => sites.Contains(s.Id), ct) != sites.Count) v.Add("siteIds", "Unknown site.");

        var changed = current is null || !sites.ToHashSet().SetEquals(current.SiteIds) || !locationIds.ToHashSet().SetEquals(current.LocationIds);
        if (changed && editor.Limited)
        {
            if (sites.Count == 0) v.Add("siteIds", "Choose at least one of your sites.");
            else if (sites.Any(s => !editor.SiteIds!.Contains(s))) v.Add("siteIds", "You can only give access to your own sites.");
            else if (editor.Branches is not null && (places.Count == 0 || places.Any(p => !Access.Covers(editor, p.SiteId, p.Path))))
                v.Add("locationIds", "You can only give access to places you can see.");
        }
        return (sites, locationIds);
    }

    private static async Task<Validator> ValidateUserAsync(UserInput i, Guid? id, CmmsDbContext db, UserManager<AppUser> um, CancellationToken ct)
    {
        var v = new Validator().Required("fullName", i.FullName, "Name", 200).Required("email", i.Email, "Email", 256)
            .MaxLength("jobTitle", i.JobTitle, "Job title", 200).MaxLength("phone", i.Phone, "Phone", 50).When(i.PurchaseApprovalLimit < 0, "purchaseApprovalLimit", "The limit cannot be negative.");
        if (!string.IsNullOrWhiteSpace(i.Email))
        {
            if (!i.Email.Contains('@')) v.Add("email", "Enter a valid email address.");
            else if (await um.FindByEmailAsync(i.Email.Trim()) is { } other && other.Id != id) v.Add("email", "This email is already used by another account.");
        }
        var roleIds = i.RoleIds ?? [];
        if (roleIds.Count == 0) v.Add("roleIds", "Assign at least one role.");
        else if (await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct) != roleIds.Distinct().Count()) v.Add("roleIds", "Unknown role.");
        if (!string.IsNullOrWhiteSpace(i.Password))
            foreach (var pv in um.PasswordValidators)
                foreach (var e in (await pv.ValidateAsync(um, null!, i.Password)).Errors) v.Add("password", e.Description);
        return v;
    }

    private static async Task<Validator> ValidateRoleAsync(RoleInput i, Guid? id, CmmsDbContext db, CancellationToken ct)
    {
        var v = new Validator().Required("name", i.Name, "Name", 100).MaxLength("description", i.Description, "Description", 500);
        RoleHomes.ValidateHome(v, i.Home);
        RoleHomes.ValidateModules(v, i.HiddenModules);
        if (!string.IsNullOrWhiteSpace(i.Name) && await db.Roles.AnyAsync(r => r.Name == i.Name.Trim() && r.Id != id, ct)) v.Add("name", "A role with this name exists.");
        return v;
    }
}

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (CmmsDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var in90 = today.AddDays(90);
            var byStatus = await db.Assets.GroupBy(a => a.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var byCriticality = await db.Assets.GroupBy(a => a.Criticality).Select(x => new { Criticality = x.Key, Count = x.Count() }).ToListAsync(ct);
            var byCategory = await db.Assets.GroupBy(a => new { a.CategoryId, Name = a.Category!.Name, Color = a.Category.Color })
                .Select(x => new { x.Key.CategoryId, Name = x.Key.Name ?? "Uncategorized", x.Key.Color, Count = x.Count() }).OrderByDescending(x => x.Count).Take(10).ToListAsync(ct);
            var bySite = await db.Assets.GroupBy(a => new { a.SiteId, a.Site!.Name }).Select(x => new { x.Key.SiteId, x.Key.Name, Count = x.Count() }).OrderByDescending(x => x.Count).ToListAsync(ct);
            var expiring = await db.Assets.AsNoTracking().Where(a => a.WarrantyExpiresOn != null && a.WarrantyExpiresOn >= today && a.WarrantyExpiresOn <= in90)
                .OrderBy(a => a.WarrantyExpiresOn).Take(8).Select(a => new { a.Id, a.AssetNumber, a.Name, a.WarrantyExpiresOn, a.WarrantyType }).ToListAsync(ct);
            var recent = await db.Assets.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(8)
                .Select(a => new { a.Id, a.AssetNumber, a.Name, a.Status, a.CreatedAt, SiteName = a.Site!.Name }).ToListAsync(ct);
            var active = new[] { FourC.Cmms.Core.Work.WorkOrderStatus.Open, FourC.Cmms.Core.Work.WorkOrderStatus.InProgress, FourC.Cmms.Core.Work.WorkOrderStatus.OnHold };
            var since = DateTimeOffset.UtcNow.AddDays(-30);
            var woByStatus = await db.WorkOrders.GroupBy(w => w.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
            var woByPriority = await db.WorkOrders.Where(w => active.Contains(w.Status)).GroupBy(w => w.Priority).Select(x => new { Priority = x.Key, Count = x.Count() }).ToListAsync(ct);
            var work = new
            {
                Open = woByStatus.Where(x => active.Contains(x.Status)).Sum(x => x.Count),
                // The same rules as the list's Late, Due soon and Not started filters the cards open; old clients read Late as "Overdue".
                Overdue = await SlaQueries.LateWork(db.WorkOrders, DateTimeOffset.UtcNow).CountAsync(ct),
                Late = await SlaQueries.LateWork(db.WorkOrders, DateTimeOffset.UtcNow).CountAsync(ct),
                DueSoon = await SlaQueries.DueSoonAsync(db.WorkOrders, await SlaQueries.SettingsAsync(db, ct), DateTimeOffset.UtcNow, ct),
                // Nobody has acted yet: work orders past their start-by (late requests are on the requests' Late tab).
                ResponseLate = await SlaQueries.LateResponse(db.WorkOrders, DateTimeOffset.UtcNow).CountAsync(ct),
                CompletedLast30 = await db.WorkOrders.CountAsync(w => w.CompletedAt >= since, ct),
                RequestsAwaiting = await db.WorkRequests.CountAsync(r => r.Status == FourC.Cmms.Core.Work.WorkRequestStatus.New, ct),
                ByStatus = woByStatus, ByPriority = woByPriority,
                Urgent = await db.WorkOrders.AsNoTracking().Where(w => active.Contains(w.Status)).OrderByDescending(w => w.Priority).ThenBy(w => w.DueDate).Take(6)
                    .Select(w => new { w.Id, w.Number, w.Title, w.Priority, w.Status, w.DueDate, AssetNumber = w.Asset!.AssetNumber }).ToListAsync(ct),
            };
            // People who see several sites: the work at each, side by side (each number opens that site's list).
            var visibleSites = await db.Sites.AsNoTracking().OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
            var bySiteWork = new List<object>();
            if (visibleSites.Count > 1)
            {
                var now = DateTimeOffset.UtcNow;
                var openBy = await db.WorkOrders.Where(w => active.Contains(w.Status)).GroupBy(w => w.SiteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                var lateBy = await SlaQueries.LateWork(db.WorkOrders, now).GroupBy(w => w.SiteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                var doneBy = await db.WorkOrders.Where(w => w.CompletedAt >= since).GroupBy(w => w.SiteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                var waitingBy = await db.WorkRequests.Where(r => r.Status == FourC.Cmms.Core.Work.WorkRequestStatus.New).GroupBy(r => r.SiteId)
                    .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                bySiteWork.AddRange(visibleSites.Select(s => new
                {
                    SiteId = s.Id, s.Name, Open = openBy.GetValueOrDefault(s.Id), Late = lateBy.GetValueOrDefault(s.Id),
                    CompletedLast30 = doneBy.GetValueOrDefault(s.Id), RequestsWaiting = waitingBy.GetValueOrDefault(s.Id),
                }));
            }
            return new
            {
                Work = work,
                BySiteWork = bySiteWork,
                Totals = new
                {
                    Sites = await db.Sites.CountAsync(ct), Locations = await db.Locations.CountAsync(ct), Assets = byStatus.Sum(x => x.Count),
                    CriticalAssets = byCriticality.Where(x => x.Criticality == Criticality.Critical).Sum(x => x.Count),
                    UnderRepair = byStatus.Where(x => x.Status == AssetStatus.UnderRepair).Sum(x => x.Count),
                    WarrantyExpiring90 = await db.Assets.CountAsync(a => a.WarrantyExpiresOn != null && a.WarrantyExpiresOn >= today && a.WarrantyExpiresOn <= in90, ct),
                    AssetValue = await db.Assets.SumAsync(a => a.CurrentValue ?? 0, ct),
                    AssetsDown = await db.DowntimeEvents.CountAsync(e => e.EndedAt == null && db.Assets.Any(a => a.Id == e.AssetId), ct),
                },
                ByStatus = byStatus, ByCriticality = byCriticality, ByCategory = byCategory, BySite = bySite,
                WarrantyExpiring = expiring, RecentAssets = recent,
            };
        }).WithTags("Dashboard").RequirePermission(Permissions.DashboardView);
    }
}

/// <summary>The organization's logo (0.14.0).</summary>
internal static class Logos
{
    public const long MaxBytes = 1024 * 1024;
    public const string ChooseImage = "Choose a PNG, JPEG or WebP image.";
    public const string TooBig = "The logo can be at most 1 MB.";

    public static string Key(Guid? tenantId) => $"{tenantId:N}/tenant/logo";

    /// <summary>The image type from the file's first bytes (not its name): PNG, JPEG or WebP; null for anything else.</summary>
    public static string? TypeOf(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' ? "image/webp"
        : null;

    /// <summary>A copy of the settings with the logo set (a new object, so the JSON column is saved).</summary>
    public static TenantSettings With(TenantSettings s, long? version, string? type)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<TenantSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        copy.LogoVersion = version; copy.LogoContentType = type;
        return copy;
    }
}

