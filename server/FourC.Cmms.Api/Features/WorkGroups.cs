using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Services;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>
/// Groups of work orders (0.12.0): a group PM occurrence makes a group and one ordinary work order per covered asset. The group
/// is never a work order, so nothing counts it as work.
/// </summary>
public static partial class WorkGroups
{
    /// <summary>
    /// Creates the group for one occurrence of <paramref name="s"/> and its work orders, one per <paramref name="covered"/> asset,
    /// each with its asset's template (routine visits) or <paramref name="task"/>. Hours and parts are copied to each work order or
    /// split evenly, as the PM says.
    /// </summary>
    public static Task<WorkOrderGroup> CreateGroupAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, PmTask task, DateOnly? due,
        IReadOnlyList<RoundAsset> covered, IReadOnlyCollection<DayOfWeek> nonWorkingDays, CancellationToken ct, EmailOutbox? mail = null) =>
        // One transaction (0.29.1): the group and every work order of it are saved together, or none.
        Atomic.RunAsync(db, tk => CreateGroupCoreAsync(db, ctx, s, task, due, covered, nonWorkingDays, tk, mail), ct);

    private static async Task<WorkOrderGroup> CreateGroupCoreAsync(CmmsDbContext db, ITenantContext ctx, PmSchedule s, PmTask task, DateOnly? due,
        IReadOnlyList<RoundAsset> covered, IReadOnlyCollection<DayOfWeek> nonWorkingDays, CancellationToken ct, EmailOutbox? mail)
    {
        var settings = await db.Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => t.Settings).FirstAsync(ct);
        var (sequence, prefix) = Numbering.For(settings, null, NumberKind.WorkGroup); // yearly when the organization wants it (0.21.0)
        var number = await Numbering.NextAsync(db, sequence, prefix, 5, n => db.WorkOrderGroups.AllSites().AnyAsync(g => g.Number == n, ct),
            p => db.WorkOrderGroups.AllSites().Where(g => g.Number.StartsWith(p)).Select(g => g.Number).ToListAsync(ct), ct);
        var title = task.Title is { Length: > 0 } tt ? tt : task.Id == s.PmTaskId ? s.Name : $"{s.Name} — {task.Name}";
        // Its Arabic twin from the same source (0.28.0); none when that source has no Arabic text.
        var titleAr = task.Title is { Length: > 0 } ? task.TitleAr : task.Id == s.PmTaskId ? s.NameAr
            : s.NameAr is null && task.NameAr is null ? null : $"{s.NameAr ?? s.Name} — {task.NameAr ?? task.Name}";
        var start = PmCalendar.NextWorkingDay(due ?? DateOnly.FromDateTime(DateTime.UtcNow), nonWorkingDays);
        var group = new WorkOrderGroup
        {
            Number = number, Title = title.Length > 300 ? title[..300] : title, SiteId = s.SiteId, PmScheduleId = s.Id, PmTaskId = task.Id, PmDueDate = due,
            PlannedStart = start, DueDate = PmCalendar.DueDate(s, start, nonWorkingDays),
        };
        db.WorkOrderGroups.Add(group);

        // Each asset's own template on routine visits; an every-Nth visit uses the Nth template for all.
        var own = task.Id != s.PmTaskId ? [] : s.Assets.Where(a => a.PmTaskId is not null).ToDictionary(a => a.AssetId, a => a.PmTaskId!.Value);
        var ids = own.Values.Append(task.Id).Distinct().ToList();
        var templates = await db.PmTasks.AsNoTracking().Include(t => t.Steps).Include(t => t.Parts).Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var shared = covered.Count(a => !own.ContainsKey(a.Id));
        var numbers = await db.Assets.AllSites().AsNoTracking().Where(a => covered.Select(c => c.Id).Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.AssetNumber, ct);
        var assignees = s.AssigneeIds.Count == 0 ? [] : await db.Employees.Where(e => s.AssigneeIds.Contains(e.Id) && e.IsActive).Select(e => e.Id).ToListAsync(ct);
        var wanted = task.RecipientIds.Concat(s.RecipientIds).Distinct().ToList();
        var recipients = wanted.Count == 0 ? [] : await db.Users.Where(u => wanted.Contains(u.Id) && u.IsActive && u.TenantId == db.CurrentTenantId).Select(u => u.Id).ToListAsync(ct);

        foreach (var asset in covered)
        {
            var t = own.TryGetValue(asset.Id, out var tid) && templates.TryGetValue(tid, out var ot) ? ot : templates[task.Id];
            // Split shares the PM template's plan among the work orders that use it; an asset's own template keeps its whole plan.
            var share = s.GroupPlan == GroupPlan.Split && t.Id == task.Id && shared > 0 ? shared : 1;
            var childTitle = $"{group.Title} · {numbers.GetValueOrDefault(asset.Id)}";
            var childTitleAr = titleAr is null ? null : $"{titleAr} · {numbers.GetValueOrDefault(asset.Id)}";
            var description = string.Join("\n\n", new[] { t.Description ?? s.Description }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var input = new WorkOrderEndpoints.WorkOrderInput(childTitle.Length > 300 ? childTitle[..300] : childTitle, description.Length == 0 ? null : description,
                task.WorkType, task.Priority ?? s.Priority, s.SiteId, asset.LocationId, asset.Id, s.TradeId ?? t.TradeId, assignees, start, group.DueDate,
                t.EstimatedHours is { } h ? Math.Round(h / share, 2) : null, null, TeamId: s.TeamId, CostCentreId: s.CostCentreId,
                TitleAr: childTitleAr is { Length: > 300 } ? childTitleAr[..300] : childTitleAr, SafetyNotes: t.SafetyNotes, SafetyNotesAr: t.SafetyNotesAr);
            var planned = new List<WorkOrderPart>();
            foreach (var p in t.Parts)
                planned.Add(new WorkOrderPart { PartId = p.PartId, Quantity = Math.Round(p.Quantity / share, 3), StoreId = await Planning.DefaultStoreAsync(db, p.PartId, s.SiteId, ct) });
            await WorkOrderEndpoints.CreateAsync(db, ctx, input, null, ct, w =>
            {
                w.PmScheduleId = s.Id; w.PmTaskId = t.Id; w.PmDueDate = due; w.GroupId = group.Id; w.OnlyWhenStopped = s.OnlyWhenStopped; w.RecipientIds = [.. recipients];
                w.ContractId = s.ContractId;
                foreach (var p in planned.Where(p => p.Quantity > 0)) { p.WorkOrderId = w.Id; db.WorkOrderParts.Add(p); }
            }, $"Generated from PM {s.Code} (group {group.Number})", mail, applyTargets: false, typedSteps: t.Steps.OrderBy(x => x.Sequence).ToList());
        }
        return group;
    }
}
