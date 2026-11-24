using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Request types (0.10.0): an editable list with a default trade and priority, recipients, and optional automatic approval.</summary>
public static class RequestTypeEndpoints
{
    public sealed record RequestTypeInput(string Code, string Name, bool IsActive, Guid? DefaultTradeId, WorkPriority? DefaultPriority, List<Guid>? RecipientIds,
        bool AutoApprove, Guid? TeamId, string? NameAr = null);

    public static void MapRequestTypeEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/request-types").WithTags("Request types");

        g.MapGet("/", async (CmmsDbContext db, CancellationToken ct) =>
            await db.RequestTypes.AsNoTracking().OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Code, t.Name, t.NameAr, t.IsActive, t.DefaultTradeId, t.DefaultPriority, t.RecipientIds, t.AutoApprove, t.TeamId }).ToListAsync(ct))
            .RequireAuthorization();

        g.MapPost("/", async (RequestTypeInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(db, input, null, ct);
            if (!v.IsValid) return v.Problem();
            var t = new RequestType { Code = "", Name = "" };
            Apply(t, input);
            db.RequestTypes.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/request-types/{t.Id}", new { t.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, RequestTypeInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var t = await db.RequestTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var v = await ValidateAsync(db, input, id, ct);
            if (!v.IsValid) return v.Problem();
            Apply(t, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var t = await db.RequestTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            // Requests anywhere in the organization count, also at sites this person can't see.
            if (await db.WorkRequests.AllSites().AnyAsync(r => r.RequestTypeId == id, ct)) throw new DomainException("This item is in use. Mark it inactive instead.");
            db.RequestTypes.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static void Apply(RequestType t, RequestTypeInput i)
    {
        t.Code = i.Code.Trim(); t.Name = i.Name.Trim(); if (i.NameAr is not null) t.NameAr = i.NameAr.Clean(); t.IsActive = i.IsActive; t.DefaultTradeId = i.DefaultTradeId; t.DefaultPriority = i.DefaultPriority;
        t.RecipientIds = (i.RecipientIds ?? []).Distinct().ToList(); t.AutoApprove = i.AutoApprove; t.TeamId = i.TeamId;
    }

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, RequestTypeInput i, Guid? id, CancellationToken ct)
    {
        var v = PeopleEndpoints.LookupRules(new PeopleEndpoints.LookupInput(i.Code, i.Name, null, i.IsActive, i.NameAr))
            .When(i.DefaultPriority is { } p && !Enum.IsDefined(p), "defaultPriority", "Choose a priority.")
            .When(i.AutoApprove && i.DefaultTradeId is null && i.TeamId is null, "autoApprove", "Choose the trade or team the work goes to.");
        if (!string.IsNullOrWhiteSpace(i.Code) && await db.RequestTypes.AnyAsync(x => x.Code == i.Code.Trim() && x.Id != id, ct)) v.Add("code", "This code is already used.");
        if (i.DefaultTradeId is { } trade && !await db.Trades.AnyAsync(x => x.Id == trade, ct)) v.Add("defaultTradeId", "Unknown trade.");
        if (i.TeamId is { } team && !await db.Teams.AllSites().AnyAsync(x => x.Id == team, ct)) v.Add("teamId", "Unknown team.");
        var people = (i.RecipientIds ?? []).Distinct().ToList();
        if (people.Count > 0 && await db.Users.People().CountAsync(u => people.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId, ct) != people.Count)
            v.Add("recipientIds", "Choose active people of this organization.");
        return v;
    }
}
