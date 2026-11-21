using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Pm;
using Microsoft.EntityFrameworkCore;

/// <summary>Imports 4CSQL PM parts lists (<c>checklistInv</c>) as PM task parts. Runs after the inventory import, which creates the parts.</summary>
internal static class PmPartsImport
{
    public static async Task RunAsync(CmmsDbContext db, Guid tenantId, LegacyWork w, Report report)
    {
        var ci = StringComparer.OrdinalIgnoreCase;
        var rows = w.ChecklistParts.Select(r => (r.ChecklistNo, r.PartNo, r.Qty)).ToList();
        var lines = PmPartsRules.Lines(rows);
        var tasks = await db.PmTasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && !t.IsDeleted).Select(t => new { t.Id, t.Code }).ToListAsync();
        var taskByCode = tasks.GroupBy(t => t.Code, ci).ToDictionary(g => g.Key, g => g.First().Id, ci);
        var parts = await db.Parts.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && !p.IsDeleted).Select(p => new { p.Id, p.PartNumber }).ToListAsync();
        var partByNo = parts.GroupBy(p => p.PartNumber, ci).ToDictionary(g => g.Key, g => g.First().Id, ci);
        var taskIds = taskByCode.Values.ToList();
        var existing = await db.PmTaskParts.Where(p => taskIds.Contains(p.PmTaskId)).ToDictionaryAsync(p => (p.PmTaskId, p.PartId));
        var skipped = PmPartsRules.Unusable(rows); // repeats added into one line are not skipped
        foreach (var (taskCode, partNo, qty) in lines)
        {
            var code = taskCode.Length > 50 ? taskCode[..50] : taskCode; // PM task numbers are cut to 50 characters, as in PmImport
            if (!taskByCode.TryGetValue(code, out var taskId) || !partByNo.TryGetValue(partNo, out var partId)) { skipped++; continue; }
            if (existing.TryGetValue((taskId, partId), out var line))
            {
                if (line.Quantity != qty) { line.Quantity = qty; report.Updated("PM task part"); }
                continue;
            }
            line = new PmTaskPart { PmTaskId = taskId, PartId = partId, Quantity = qty };
            db.PmTaskParts.Add(line);
            existing[(taskId, partId)] = line;
            report.Created("PM task part");
        }
        await db.SaveChangesAsync();
        if (skipped > 0) report.Warn($"PM parts: {skipped} row(s) of checklistInv skipped (task or part not found, or no quantity).");
    }
}
