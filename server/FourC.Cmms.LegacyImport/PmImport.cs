using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

/// <summary>Imports PM tasks (checklists), time and meter schedules, multiple-PM rules, meters, readings and links PM work orders.</summary>
static class PmImport
{
    public static async Task RunAsync(CmmsDbContext db, Guid tenantId, LegacyWork w, Report report,
        Func<string?, Site> siteFor, IReadOnlyDictionary<string, Location> locByCode, IReadOnlyDictionary<string, Asset> assetByNumber)
    {
        var ci = StringComparer.OrdinalIgnoreCase;
        static string K(string? s) => (s ?? "").Trim();
        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
        static DateOnly? Day(DateTime? d) => d is { Year: > 1900 } v ? DateOnly.FromDateTime(v) : null;
        static decimal? Dec(double? v) => v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value) ? null : Math.Round((decimal)v.Value, 3);
        static bool Active(int? v) => v is null || v != 0;
        static (string Title, string? Remainder) Split(string? text, string fallback)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return (fallback, null);
            var first = t.Split('\n')[0].Trim();
            return (first.Length > 300 ? first[..297] + "…" : first, t == first ? null : Trunc(t, 8000));
        }
        var trades = await db.Trades.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && t.LegacyRef != null).ToDictionaryAsync(t => t.LegacyRef!, t => t.Id, ci);
        Guid? Trade(string? id) => int.TryParse(id, out var i) && trades.TryGetValue($"trade:{i}", out var g) ? g : null;
        WorkPriority Priority(int? id)
        {
            var n = (id is { } i ? w.Priorities.GetValueOrDefault(i) : null)?.ToLowerInvariant() ?? "";
            return n.Contains("emergen") ? WorkPriority.Urgent : n.Contains("urgent") || n.Contains("safety") ? WorkPriority.High : n.Contains("routine") ? WorkPriority.Low : WorkPriority.Medium;
        }
        static (int Frequency, FrequencyUnit Unit) Interval(int? freqUnits, int? frequency)
        {
            var f = Math.Max(1, frequency ?? 1);
            return (freqUnits ?? 1) switch
            {
                7 => (f, FrequencyUnit.Week),
                365 => (f, FrequencyUnit.Year),
                var u => (Math.Clamp(u * f, 1, 1000), FrequencyUnit.Day), // 4CSQL months are 28 days: keep the exact legacy cadence
            };
        }

        // ---------- PM tasks ----------
        var tasks = await db.PmTasks.IgnoreQueryFilters().Include(t => t.Steps).Where(t => t.TenantId == tenantId && !t.IsDeleted).ToListAsync();
        var taskByCode = new Dictionary<string, PmTask>(ci);
        foreach (var t in tasks) taskByCode.TryAdd(t.Code, t);
        var steps = w.ChecklistSteps.GroupBy(s => K(s.ChecklistNo), ci).ToDictionary(g => g.Key, g => g.OrderBy(s => s.SeqNo).ThenBy(s => s.ElementId).ToList(), ci);
        foreach (var c in w.Checklists.Where(c => !string.IsNullOrWhiteSpace(c.ChecklistNo)))
        {
            var code = Trunc(K(c.ChecklistNo), 50)!;
            var isNew = !taskByCode.TryGetValue(code, out var task);
            if (isNew) { task = new PmTask { Code = code, Name = "" }; db.PmTasks.Add(task); taskByCode[code] = task; report.Created("PM task"); }
            else report.Updated("PM task");
            var (name, rest) = Split(c.CheckListName, code);
            task!.Name = name; task.Description = rest ?? (string.IsNullOrWhiteSpace(c.Tasks) ? null : Trunc(c.Tasks.Trim(), 8000));
            task.EstimatedHours = c.EstimatedLaborHours is > 0 and < 100000 ? Math.Round((decimal)c.EstimatedLaborHours.Value, 2) : null;
            task.IsActive = Active(c.Active); task.LegacyRef = $"pmtask:{code}";
            if (isNew && steps.TryGetValue(code, out var list))
            {
                var seq = 0;
                foreach (var st in list.Where(s => !string.IsNullOrWhiteSpace(s.TaskDesc)))
                { task.Steps.Add(new PmTaskStep { PmTaskId = task.Id, Sequence = ++seq, Description = Trunc(st.TaskDesc!.Trim(), 2000)! }); report.Created("PM step"); }
            }
        }
        await db.SaveChangesAsync();

        // ---------- Meters (legacy pmmeters combine the meter and its PM) ----------
        var meters = await db.Meters.IgnoreQueryFilters().Where(m => m.TenantId == tenantId && !m.IsDeleted).ToListAsync();
        var meterByCode = new Dictionary<string, Meter>(ci);
        foreach (var m in meters) meterByCode.TryAdd(m.Code, m);
        var lastReading = w.Readings.Where(r => r.Reading is not null).GroupBy(r => K(r.MeterNo), ci).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Date).Last(), ci);
        foreach (var lm in w.Meters.Where(m => !string.IsNullOrWhiteSpace(m.MeterNo)))
        {
            if (string.IsNullOrWhiteSpace(lm.AssetNo) || !assetByNumber.TryGetValue(K(lm.AssetNo), out var asset)) { report.Warn($"Meter {lm.MeterNo}: asset '{lm.AssetNo}' not found, skipped."); continue; }
            var code = Trunc(K(lm.MeterNo), 50)!;
            if (!meterByCode.TryGetValue(code, out var meter)) { meter = new Meter { Code = code, Name = "", Unit = "" }; db.Meters.Add(meter); meterByCode[code] = meter; report.Created("Meter"); }
            else report.Updated("Meter");
            meter.Name = Split(lm.MeterDescription, code).Title is var n && n.Length > 200 ? n[..200] : Split(lm.MeterDescription, code).Title;
            meter.AssetId = asset.Id; meter.Unit = Trunc(string.IsNullOrWhiteSpace(lm.Units) ? "units" : lm.Units.Trim(), 30)!;
            meter.MaxDailyIncrement = lm.max_daily_reading is > 0 ? lm.max_daily_reading : null; meter.LegacyRef = $"meter:{code}";
            if (lastReading.TryGetValue(code, out var lr)) { meter.CurrentReading = Dec(lr.Reading); meter.LastReadAt = lr.Date is { } dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : null; }
            else if (meter.CurrentReading is null) meter.CurrentReading = Dec(lm.LastPMreading);
        }
        await db.SaveChangesAsync();
        var existingReadings = (await db.MeterReadings.IgnoreQueryFilters().Where(r => r.TenantId == tenantId && r.LegacyRef != null).Select(r => r.LegacyRef!).ToListAsync()).ToHashSet(ci);
        foreach (var r in w.Readings)
        {
            if (r.Reading is null || !meterByCode.TryGetValue(K(r.MeterNo), out var meter) || existingReadings.Contains($"reading:{r.ReadingID}")) continue;
            db.MeterReadings.Add(new MeterReading { TenantId = tenantId, MeterId = meter.Id, Value = Dec(r.Reading) ?? 0, Source = ReadingSource.Import, LegacyRef = $"reading:{r.ReadingID}",
                ReadAt = r.Date is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : DateTimeOffset.UtcNow });
            report.Created("Meter reading");
        }
        await db.SaveChangesAsync();

        // ---------- PM schedules ----------
        var schedules = await db.PmSchedules.IgnoreQueryFilters().Include(s => s.ExtraTasks).Where(s => s.TenantId == tenantId && !s.IsDeleted).ToListAsync();
        var schedByCode = new Dictionary<string, PmSchedule>(ci);
        foreach (var s in schedules) schedByCode.TryAdd(s.Code, s);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (Site site, Guid? loc, Guid? asset)? Place(string? siteCode, string? assetNo, string? locNo)
        {
            var asset = !string.IsNullOrWhiteSpace(assetNo) && assetByNumber.TryGetValue(K(assetNo), out var a) ? a : null;
            var loc = !string.IsNullOrWhiteSpace(locNo) && locByCode.TryGetValue(K(locNo), out var l) ? l : null;
            if (asset is null && loc is null) return null;
            var site = asset is not null ? siteFor(null) : siteFor(siteCode);
            Guid siteId = asset?.SiteId ?? loc!.SiteId;
            return (site.Id == siteId ? site : new Site { Id = siteId, Code = "", Name = "" }, loc?.SiteId == siteId ? loc.Id : asset?.LocationId, asset?.Id);
        }
        PmSchedule Upsert(string code, string legacyRef)
        {
            if (!schedByCode.TryGetValue(code, out var s)) { s = new PmSchedule { Code = code, Name = "" }; db.PmSchedules.Add(s); schedByCode[code] = s; report.Created("PM schedule"); }
            else report.Updated("PM schedule");
            s.LegacyRef = legacyRef;
            return s;
        }
        foreach (var p in w.PmSchedules.Where(p => !string.IsNullOrWhiteSpace(p.PMNo)))
        {
            var place = Place(p.SiteCode, K(p.TypePM) == "2" ? null : p.AssetNo, p.PhyLocationNo);
            if (place is null) { report.Warn($"PM {p.PMNo}: neither asset '{p.AssetNo}' nor location '{p.PhyLocationNo}' was found, skipped."); continue; }
            if (string.IsNullOrWhiteSpace(p.ChecklistNo) || !taskByCode.TryGetValue(K(p.ChecklistNo), out var task)) { report.Warn($"PM {p.PMNo}: PM task '{p.ChecklistNo}' not found, skipped."); continue; }
            var s = Upsert(Trunc(K(p.PMNo), 50)!, $"pm:{K(p.PMNo)}");
            s.Name = Split(p.PMName, s.Code).Title; s.SiteId = place.Value.site.Id; s.LocationId = place.Value.loc; s.AssetId = place.Value.asset;
            s.PmTaskId = task.Id; s.TradeId = Trade(p.PMWOTrade); s.Priority = Priority(p.workpriority);
            s.WorkPeriodDays = Math.Clamp(p.PeriodDays ?? 1, 1, 366); s.GenerationCount = Math.Max(0, p.PMCounter ?? 0); s.IsActive = Active(p.PMActive);
            s.StartDate = Day(p.StartDate) ?? Day(p.TargetStartDate) ?? Day(p.NextDate) ?? today;
            s.LastDueDate = Day(p.TargetStartDate); s.NextDueDate = Day(p.NextDate);
            switch (K(p.TypePMgen))
            {
                case "3":
                    s.Type = PmScheduleType.DayOfWeekInMonth; s.Basis = PmBasis.Scheduled; s.Frequency = Math.Max(1, p.Frequency ?? 1); s.Unit = FrequencyUnit.Month;
                    s.WeekOfMonth = Math.Clamp((p.WeekofMonth ?? 0) + 1, 1, 5); s.DayOfWeek = (DayOfWeek)Math.Clamp((p.DayofWeek ?? 2) - 1, 0, 6); // VB weekday: 1 = Sunday
                    break;
                case "4" or "5":
                    s.Type = PmScheduleType.DateOfMonth; s.Basis = PmBasis.Scheduled; s.Frequency = Math.Max(1, p.Frequency ?? 1); s.Unit = FrequencyUnit.Month;
                    s.DayOfMonth = (Day(p.NextDate) ?? Day(p.TargetStartDate) ?? s.StartDate).Day; s.WholeMonth = K(p.TypePMgen) == "5";
                    break;
                default:
                    s.Type = PmScheduleType.FixedInterval; s.Basis = K(p.TypePMgen) == "2" ? PmBasis.Actual : PmBasis.Scheduled;
                    (s.Frequency, s.Unit) = Interval(p.FreqUnits, p.Frequency);
                    break;
            }
            var rules = w.MultiplePm.Where(m => ci.Equals(K(m.MPMNo), K(p.PMNo)) && m.MTaskSeq is > 1).GroupBy(m => K(m.MTaskNo), ci)
                .Select(g => (Task: taskByCode.GetValueOrDefault(g.Key), Nth: g.Max(x => x.MTaskSeq!.Value))).Where(r => r.Task is not null && r.Task.Id != task.Id).ToList();
            s.ExtraTasks.RemoveAll(e => rules.All(r => r.Task!.Id != e.PmTaskId));
            foreach (var (mt, nth) in rules)
            {
                var existing = s.ExtraTasks.FirstOrDefault(e => e.PmTaskId == mt!.Id);
                if (existing is null) { s.ExtraTasks.Add(new PmScheduleTask { PmScheduleId = s.Id, PmTaskId = mt!.Id, EveryNth = nth }); report.Created("Multiple-PM rule"); }
                else existing.EveryNth = nth;
            }
        }
        foreach (var lm in w.Meters.Where(m => !string.IsNullOrWhiteSpace(m.MeterNo)))
        {
            if (!meterByCode.TryGetValue(K(lm.MeterNo), out var meter)) continue;
            var taskCode = new[] { lm.PMIncTaskNo, lm.PMLowTaskNo, lm.PMUppTaskNo }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (taskCode is null || !taskByCode.TryGetValue(K(taskCode), out var task)) { report.Warn($"Meter {lm.MeterNo}: no PM task found, meter imported without a schedule."); continue; }
            var place = Place(lm.SiteCode, lm.AssetNo, null)!.Value;
            var code = schedByCode.TryGetValue(meter.Code, out var clash) && clash.LegacyRef != $"meterpm:{meter.Code}" ? Trunc($"{meter.Code}-PM", 50)! : meter.Code;
            var s = Upsert(code, $"meterpm:{meter.Code}");
            var threshold = (lm.MeterType ?? "").Trim().StartsWith("T", StringComparison.OrdinalIgnoreCase);
            s.Name = meter.Name; s.SiteId = place.site.Id; s.LocationId = place.loc; s.AssetId = place.asset; s.PmTaskId = task.Id; s.TradeId = Trade(lm.PMWOTrade);
            s.Type = PmScheduleType.Meter; s.MeterId = meter.Id; s.MeterTrigger = threshold ? MeterTrigger.Threshold : MeterTrigger.Incremental;
            s.MeterInterval = Dec(lm.IncFreq); s.NextDueReading = threshold ? null : Dec(lm.IncPMDue);
            s.LowerLimit = threshold ? Dec(lm.Lowerlimit) : null; s.UpperLimit = threshold ? Dec(lm.UpperLimit) : null;
            s.LowerTaskId = !string.IsNullOrWhiteSpace(lm.PMLowTaskNo) && taskByCode.TryGetValue(K(lm.PMLowTaskNo), out var lt) ? lt.Id : null;
            s.UpperTaskId = !string.IsNullOrWhiteSpace(lm.PMUppTaskNo) && taskByCode.TryGetValue(K(lm.PMUppTaskNo), out var ut) ? ut.Id : null;
            s.Basis = lm.PMType == 2 ? PmBasis.Scheduled : PmBasis.Actual; // guide: "Actual" is the default meter PM type
            s.TimeBased = !threshold && lm.TimeBased is not (null or 0);
            (s.Frequency, s.Unit) = Interval(lm.FreqUnits, lm.Frequency);
            s.NextDueDate = s.TimeBased ? Day(lm.NextDate) : null; s.StartDate = Day(lm.NextDate) ?? today;
            s.GenerationCount = Math.Max(0, lm.PMCounter ?? 0); s.IsActive = Active(lm.PMActive);
        }
        await db.SaveChangesAsync();

        // ---------- Link historical PM work orders to their schedule ----------
        var byNumber = await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted && x.LegacyRef != null && x.PmScheduleId == null)
            .ToDictionaryAsync(x => x.Number, ci);
        var usedDue = new HashSet<(Guid, DateOnly)>(await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.PmScheduleId != null && x.PmDueDate != null)
            .Select(x => new { S = x.PmScheduleId!.Value, D = x.PmDueDate!.Value }).ToListAsync().ContinueWith(t => t.Result.Select(x => (x.S, x.D))));
        foreach (var lw in w.WorkOrders.Where(x => !string.IsNullOrWhiteSpace(x.PMNo)))
        {
            if (!byNumber.TryGetValue(K(lw.WorkorderNo), out var wo) || !schedByCode.TryGetValue(K(lw.PMNo), out var s)) continue;
            wo.PmScheduleId = s.Id;
            wo.PmTaskId = !string.IsNullOrWhiteSpace(lw.TaskNo) && taskByCode.TryGetValue(K(lw.TaskNo), out var t) ? t.Id : s.PmTaskId;
            if (Day(lw.PMTarStartDate) is { } due && usedDue.Add((s.Id, due))) wo.PmDueDate = due;
            report.Updated("PM work order link");
        }
        // The latest linked work order becomes each schedule's "last work order".
        foreach (var s in schedByCode.Values.Where(s => s.LegacyRef != null))
        {
            var last = byNumber.Values.Where(x => x.PmScheduleId == s.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            if (last is not null) s.LastWorkOrderId = last.Id;
        }
        await db.SaveChangesAsync();
    }
}
