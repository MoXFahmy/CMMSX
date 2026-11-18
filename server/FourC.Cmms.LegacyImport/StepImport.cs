using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Work;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Imports PM checklist steps recorded on work orders (legacy workorderelements) and which assets each round work order
/// covered. Steps are copied when they hold a reading, finding or remark, and for work orders still open (so they can be
/// finished after cut-over); the rest is only a copy of the PM task. Streams the rows and saves in batches: MEG has 3.7 million.
/// </summary>
static class StepImport
{
    private const int BatchSize = 5000;
    private sealed record WoInfo(Guid Id, string Number, WorkOrderStatus Status, Guid? PmScheduleId);

    public static async Task RunAsync(CmmsDbContext db, Guid tenantId, SqlConnection legacy, Report report, IReadOnlyDictionary<string, Asset> assetByNumber)
    {
        if (!await Legacy.HasStepsAsync(legacy)) return;
        var ci = StringComparer.OrdinalIgnoreCase;
        static string K(string? s) => (s ?? "").Trim();
        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

        var wos = (await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted && x.LegacyRef != null)
                .Select(x => new WoInfo(x.Id, x.Number, x.Status, x.PmScheduleId)).ToListAsync())
            .GroupBy(x => x.Number, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        var roundLists = await db.PmSchedules.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && !s.IsDeleted && s.Assets.Any())
            .Select(s => new { s.Id, Assets = s.Assets.OrderBy(a => a.Sequence).Select(a => a.AssetId).ToList() }).ToDictionaryAsync(s => s.Id, s => s.Assets);
        var covered = (await (from a in db.WorkOrderAssets join w in db.WorkOrders.IgnoreQueryFilters() on a.WorkOrderId equals w.Id
                              where w.TenantId == tenantId select a.WorkOrderId).Distinct().ToListAsync()).ToHashSet();
        var existing = (await db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && t.LegacyRef != null).Select(t => t.LegacyRef!).ToListAsync()).ToHashSet(ci);
        var lastSeq = await db.WorkOrderTasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).GroupBy(t => t.WorkOrderId)
            .Select(g => new { g.Key, Max = g.Max(t => t.Sequence) }).ToDictionaryAsync(x => x.Key, x => x.Max);
        var assetIdByNo = assetByNumber.ToDictionary(kv => kv.Key, kv => kv.Value.Id, ci);
        var numberById = assetByNumber.Values.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First().AssetNumber);

        var batch = new List<object>();
        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            await db.SaveChangesAsync();
            foreach (var x in batch) db.Entry(x).State = EntityState.Detached;
            batch.Clear();
        }

        // Which assets a round work order covered: those its steps were recorded for, else the schedule's list.
        var elements = new List<(string AssetNo, int AssetSeqNo)>();
        void Cover(WoInfo wo)
        {
            if (wo.PmScheduleId is not { } sid || !roundLists.TryGetValue(sid, out var list) || covered.Contains(wo.Id)) return;
            var seq = 0;
            foreach (var no in RoundImport.CoveredAssets(elements, list.Where(numberById.ContainsKey).Select(id => numberById[id]).ToList()))
            {
                if (!assetIdByNo.TryGetValue(no, out var assetId)) continue;
                var x = new WorkOrderAsset { WorkOrderId = wo.Id, AssetId = assetId, Sequence = ++seq };
                db.WorkOrderAssets.Add(x); batch.Add(x);
            }
            if (seq > 0) { covered.Add(wo.Id); report.Created("Round work order"); }
        }

        WoInfo? current = null;
        string? currentNo = null;
        var skippedWos = 0;
        var imported = 0;
        await foreach (var r in Legacy.StreamStepsAsync(legacy))
        {
            var no = K(r.WorkorderNo);
            if (!ci.Equals(no, currentNo))
            {
                if (current is not null) Cover(current);
                if (batch.Count >= BatchSize) await FlushAsync();
                elements.Clear();
                currentNo = no;
                current = wos.GetValueOrDefault(no);
                if (current is null && no.Length > 0) skippedWos++;
            }
            if (current is null) continue;
            elements.Add((K(r.assetno), r.AssetSeqNo ?? 0));

            var reading = Trunc(K(r.readings), 100);
            var remarks = Trunc(RoundImport.JoinRemarks(r.findings, r.Remarks), 2000);
            var open = current.Status is WorkOrderStatus.Open or WorkOrderStatus.InProgress or WorkOrderStatus.OnHold;
            if (string.IsNullOrEmpty(reading) && remarks is null && !open) continue;
            var key = $"step:{r.Woelementid}";
            if (!existing.Add(key)) continue;
            var seq = lastSeq.GetValueOrDefault(current.Id) + 1;
            lastSeq[current.Id] = seq;
            var step = new WorkOrderTask
            {
                TenantId = tenantId, WorkOrderId = current.Id, Sequence = seq, LegacyRef = key,
                Description = Trunc(K(r.TaskDesc) is { Length: > 0 } d ? d : $"Step {r.SeqNo}", 1000)!,
                AssetId = assetIdByNo.TryGetValue(K(r.assetno), out var aid) ? aid : null,
                Reading = string.IsNullOrEmpty(reading) ? null : reading, Remarks = remarks,
                IsDone = current.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed,
            };
            db.WorkOrderTasks.Add(step); batch.Add(step); report.Created("Step result");
            if (++imported % 50_000 == 0) Console.WriteLine($"  … {imported} step results");
            if (batch.Count >= BatchSize) await FlushAsync();
        }
        if (current is not null) Cover(current);
        elements.Clear();
        foreach (var wo in wos.Values) // round work orders with no recorded steps cover the schedule's list
        {
            Cover(wo);
            if (batch.Count >= BatchSize) await FlushAsync();
        }
        await FlushAsync();
        if (skippedWos > 0) report.Warn($"PM steps of {skippedWos} work order(s) that were not imported were skipped.");
    }
}
