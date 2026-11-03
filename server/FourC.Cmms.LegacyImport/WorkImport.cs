using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

/// <summary>Imports trades, failure codes, employees, work requests and work orders (with assignees, labour,
/// direct costs, job tasks, notes and status history). Idempotent by LegacyRef / record number.</summary>
static class WorkImport
{
    public static async Task RunAsync(CmmsDbContext db, Guid tenantId, LegacyWork w, Report report,
        Func<string?, Site> siteFor, IReadOnlyDictionary<string, Location> locByCode, IReadOnlyDictionary<string, Asset> assetByNumber)
    {
        var ci = StringComparer.OrdinalIgnoreCase;
        static string K(string? s) => (s ?? "").Trim();
        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
        static DateTimeOffset? Utc(DateTime? d) => d is { Year: > 1900 } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
        static DateOnly? Day(DateTime? d) => d is { Year: > 1900 } v ? DateOnly.FromDateTime(v) : null;
        static decimal Dec(double? v) => v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value) ? 0 : Math.Round((decimal)v.Value, 2);

        // ---------- Trades & failure codes ----------
        var trades = await db.Trades.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && !t.IsDeleted).ToListAsync();
        var tradeById = new Dictionary<int, Trade>();
        foreach (var (id, name) in w.Trades)
        {
            var t = trades.FirstOrDefault(x => x.LegacyRef == $"trade:{id}") ?? trades.FirstOrDefault(x => ci.Equals(x.Name, name));
            if (t is null)
            {
                var code = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(10).ToArray());
                if (code.Length == 0 || trades.Any(x => ci.Equals(x.Code, code))) code = $"T{id}";
                t = new Trade { Code = code, Name = Trunc(name, 200)! };
                db.Trades.Add(t); trades.Add(t); report.Created("Trade");
            }
            else report.Updated("Trade");
            t.LegacyRef = $"trade:{id}";
            tradeById[id] = t;
        }
        var codes = await db.FailureCodes.IgnoreQueryFilters().Where(f => f.TenantId == tenantId && !f.IsDeleted).ToListAsync();
        var failureByCode = new Dictionary<string, FailureCode>(ci);
        foreach (var f in codes) failureByCode.TryAdd(f.Code, f);
        foreach (var f in w.FailureCodes.Where(f => !string.IsNullOrWhiteSpace(f.FailureCauseCode)))
        {
            var code = Trunc(K(f.FailureCauseCode), 50)!;
            if (!failureByCode.TryGetValue(code, out var fc)) { fc = new FailureCode { Code = code, Name = "" }; db.FailureCodes.Add(fc); failureByCode[code] = fc; report.Created("Failure code"); }
            else report.Updated("Failure code");
            fc.Name = Trunc(string.IsNullOrWhiteSpace(f.FailureCauseDescription) ? code : f.FailureCauseDescription.Trim(), 200)!;
            fc.IsActive = f.IsActive ?? true; fc.LegacyRef = $"failure:{code}";
        }
        await db.SaveChangesAsync();

        // ---------- Employees (workforce records; logins are not migrated) ----------
        var emps = await db.Employees.IgnoreQueryFilters().Where(e => e.TenantId == tenantId && !e.IsDeleted).ToListAsync();
        var empByNo = new Dictionary<string, Employee>(ci);
        foreach (var e in emps) empByNo.TryAdd(e.Code, e);
        foreach (var e in w.Employees.Where(e => !string.IsNullOrWhiteSpace(e.EmployeeNO)))
        {
            var code = Trunc(K(e.EmployeeNO), 50)!;
            if (!empByNo.TryGetValue(code, out var emp)) { emp = new Employee { Code = code, FullName = "" }; db.Employees.Add(emp); empByNo[code] = emp; report.Created("Employee"); }
            else report.Updated("Employee");
            emp.LegacyRef = $"employee:{code}";
            emp.FullName = Trunc(string.IsNullOrWhiteSpace(e.FirstName) ? code : e.FirstName.Trim(), 200)!;
            emp.JobTitle = Trunc(e.Positions?.Trim(), 200);
            emp.Email = Trunc(e.Email?.Trim() is { Length: > 0 } em && em.Contains('@') ? em : null, 256);
            emp.Phone = Trunc((e.HandPhone ?? e.WorkPhone)?.Trim(), 50);
            emp.HourlyRate = e.HourlySalary is > 0 ? Dec(e.HourlySalary) : null;
            emp.OvertimeRate = e.OverTime1 is > 0 ? Dec(e.OverTime1) : null;
            emp.TradeIds = e.WorkTrade is { } wt && tradeById.TryGetValue(wt, out var tr) ? [tr.Id] : [];
            emp.SiteIds = e.Central == 1 || string.IsNullOrWhiteSpace(e.SiteCode) ? [] : [siteFor(e.SiteCode).Id];
            emp.IsContractor = e.IsInternal == false;
        }
        await db.SaveChangesAsync();
        string? EmpName(string? no) => string.IsNullOrWhiteSpace(no) ? null : empByNo.TryGetValue(K(no), out var e) ? e.FullName : K(no);

        // ---------- Lookup mapping by description (works for customer-specific additions too) ----------
        WorkType MapType(int? id)
        {
            var n = (id is { } i ? w.WorkTypes.GetValueOrDefault(i) : null)?.ToLowerInvariant() ?? "";
            return n.Contains("prevent") ? WorkType.Preventive : n.Contains("breakdown") ? WorkType.Breakdown : n.Contains("inspect") ? WorkType.Inspection
                : n.Contains("predict") ? WorkType.Predictive : n.Contains("project") ? WorkType.Project : n.Contains("safety") ? WorkType.Safety
                : n.Contains("correct") || n.Length == 0 ? WorkType.Corrective : WorkType.Other;
        }
        WorkPriority MapPriority(int? id)
        {
            var n = (id is { } i ? w.Priorities.GetValueOrDefault(i) : null)?.ToLowerInvariant() ?? "";
            return n.Contains("emergen") ? WorkPriority.Urgent : n.Contains("urgent") || n.Contains("safety") || n.Contains("high") ? WorkPriority.High
                : n.Contains("routine") || n.Contains("low") ? WorkPriority.Low : WorkPriority.Medium;
        }
        (WorkOrderStatus, HoldReason?) MapStatus(int? id)
        {
            var n = (id is { } i ? w.Statuses.GetValueOrDefault(i) : null)?.ToLowerInvariant() ?? "";
            if (n.Contains("cancel")) return (WorkOrderStatus.Cancelled, null);
            if (n.Contains("close")) return (WorkOrderStatus.Closed, null);
            if (n.Contains("complete")) return (WorkOrderStatus.Completed, null);
            if (n.Contains("part")) return (WorkOrderStatus.OnHold, HoldReason.WaitingForParts);
            if (n.Contains("approv")) return (WorkOrderStatus.OnHold, HoldReason.WaitingForApproval);
            if (n.Contains("access")) return (WorkOrderStatus.OnHold, HoldReason.WaitingForAccess);
            if (n.Contains("contract")) return (WorkOrderStatus.OnHold, HoldReason.WaitingForContractor);
            if (n.Contains("wait") || n.Contains("hold")) return (WorkOrderStatus.OnHold, HoldReason.Other);
            if (n.Contains("progress") || n.Contains("acknowledg") || n.Contains("start")) return (WorkOrderStatus.InProgress, null);
            return (WorkOrderStatus.Open, null);
        }
        static (string Title, string? Description) Split(string? text, string fallback)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return (fallback, null);
            var first = t.Split('\n')[0].Trim();
            var title = first.Length > 300 ? first[..297] + "…" : first;
            return (title, t == first && first.Length <= 300 ? null : Trunc(t, 8000));
        }
        (Site site, Guid? loc, Guid? asset) Place(string? siteCode, string? locNo, string? assetNo)
        {
            var asset = !string.IsNullOrWhiteSpace(assetNo) && assetByNumber.TryGetValue(K(assetNo), out var a) ? a : null;
            var loc = !string.IsNullOrWhiteSpace(locNo) && locByCode.TryGetValue(K(locNo), out var l) ? l : null;
            var site = siteFor(siteCode);
            if (asset is not null && asset.SiteId != site.Id && string.IsNullOrWhiteSpace(siteCode)) site = siteFor(null);
            // Keep the record consistent: location and asset must belong to the chosen site.
            Guid? locId = loc is not null && loc.SiteId == site.Id ? loc.Id : asset is not null && asset.SiteId == site.Id ? asset.LocationId : null;
            Guid? assetId = asset is not null && asset.SiteId == site.Id ? asset.Id : null;
            return (site, locId, assetId);
        }

        // ---------- Work orders ----------
        var existing = await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToDictionaryAsync(x => x.Number, ci);
        var woByNo = new Dictionary<string, WorkOrder>(existing, ci);
        var created = new HashSet<Guid>();
        var n = 0;
        foreach (var r in w.WorkOrders.Where(r => !string.IsNullOrWhiteSpace(r.WorkorderNo)))
        {
            var number = Trunc(K(r.WorkorderNo), 50)!;
            if (!woByNo.TryGetValue(number, out var wo)) { wo = new WorkOrder { Number = number, Title = "" }; db.WorkOrders.Add(wo); woByNo[number] = wo; created.Add(wo.Id); report.Created("Work order"); }
            else report.Updated("Work order");
            var (title, desc) = Split(r.ProblemDescription, number);
            var (site, loc, asset) = Place(r.SiteCode, r.LocationNo, r.AssetNo);
            var (status, hold) = MapStatus(r.WorkStatus);
            wo.LegacyRef = $"wo:{number}"; wo.Title = title; wo.Description = desc; wo.Type = MapType(r.WorkType); wo.Priority = MapPriority(r.WorkPriority);
            wo.Status = status; wo.HoldReason = hold; wo.SiteId = site.Id; wo.LocationId = loc; wo.AssetId = asset;
            wo.TradeId = r.WOTrade is { } t && tradeById.TryGetValue(t, out var trd) ? trd.Id : null;
            wo.FailureCodeId = !string.IsNullOrWhiteSpace(r.FailureCauseCode) && failureByCode.TryGetValue(K(r.FailureCauseCode), out var fc) ? fc.Id : null;
            wo.CreatedAt = Utc(r.DateReceived) ?? wo.CreatedAt;
            wo.DueDate = Day(r.DateRequired); wo.PlannedStart = Day(r.EstDateStart);
            wo.EstimatedHours = r.EstDuration is > 0 and < 100000 ? Dec(r.EstDuration) : null;
            wo.StartedAt = Utc(r.ActDateStart);
            wo.CompletedAt = status is WorkOrderStatus.Completed or WorkOrderStatus.Closed ? Utc(r.ActDateEnd) ?? Utc(r.ActDateStart) : null;
            wo.ClosedAt = status == WorkOrderStatus.Closed ? wo.CompletedAt : null;
            wo.CompletionNotes = Trunc(r.ActionTaken?.Trim(), 8000) is { Length: > 0 } at ? at : null;
            wo.CauseNotes = Trunc(r.CauseDescription?.Trim(), 8000) is { Length: > 0 } cd ? cd : null;
            wo.PreventionNotes = Trunc(r.PreventionTaken?.Trim(), 8000) is { Length: > 0 } pt ? pt : null;
            wo.AssetWasDown = r.DownTime is > 0; wo.DowntimeHours = r.DownTime is > 0 and < 100000 ? Dec(r.DownTime) : null;
            wo.RequestedBy = Trunc((r.requestby?.Trim() is { Length: > 0 } rb ? rb : null) ?? EmpName(r.RequestorNo) ?? EmpName(r.EmployeeNo), 200);
            if (++n % 1000 == 0) { await db.SaveChangesAsync(); Console.WriteLine($"  … {n} work orders"); }
        }
        await db.SaveChangesAsync();

        // Children are only added for work orders created in this run (re-runs refresh headers without duplicating history).
        WorkOrder? NewWo(string? no) => !string.IsNullOrWhiteSpace(no) && woByNo.TryGetValue(K(no), out var x) && created.Contains(x.Id) ? x : null;
        var assigned = new HashSet<(Guid, Guid)>();
        foreach (var a in w.Assignments)
            if (NewWo(a.WorkorderNo) is { } wo && empByNo.TryGetValue(K(a.EmployeeNo), out var e) && assigned.Add((wo.Id, e.Id)))
            { db.WorkOrderAssignees.Add(new WorkOrderAssignee { WorkOrderId = wo.Id, EmployeeId = e.Id }); report.Created("Assignee"); }
        foreach (var l in w.Labor)
        {
            if (NewWo(l.WorkorderNo) is not { } wo || !empByNo.TryGetValue(K(l.EmployeeNo), out var e)) continue;
            var overtime = Dec(l.OverTime1) + Dec(l.OverTime2) + Dec(l.OverTime3) + Dec(l.OverTime4);
            var normal = l.SalaryHours is > 0 ? Dec(l.SalaryHours) : Math.Max(Dec(l.TotHour) - overtime, 0);
            var total = normal + overtime;
            var rate = total > 0 ? Math.Round(Dec(l.TotCost) / total, 2) : 0; // preserves the legacy labour cost
            db.WorkOrderLabor.Add(new WorkOrderLabor
            {
                TenantId = tenantId, WorkOrderId = wo.Id, EmployeeId = e.Id, WorkDate = Day(l.StartDate) ?? Day(l.LogDate) ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Hours = normal, OvertimeHours = overtime, Rate = rate, OvertimeRate = rate, Notes = Trunc(l.Comment?.Trim(), 2000) is { Length: > 0 } c ? c : null,
                CreatedAt = Utc(l.LogDate) ?? Utc(l.StartDate) ?? DateTimeOffset.UtcNow, LegacyRef = $"labor:{l.WorkorderLaborID}",
            });
            report.Created("Labour entry");
        }
        foreach (var d in w.Direct)
        {
            if (NewWo(d.WONo) is not { } wo) continue;
            db.WorkOrderCosts.Add(new WorkOrderCost
            {
                TenantId = tenantId, WorkOrderId = wo.Id, Description = Trunc(string.IsNullOrWhiteSpace(d.StockDesc) ? "Direct issue" : d.StockDesc.Trim(), 500)!,
                Quantity = d.Qty is > 0 ? (decimal)d.Qty.Value : 1, UnitCost = Math.Max(Dec(d.Price), 0), Vendor = Trunc(d.supplier?.Trim(), 200),
                Reference = Trunc(new[] { d.PRNo, d.invoicenumber }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim(), 100), CreatedAt = Utc(d.Date) ?? DateTimeOffset.UtcNow,
            });
            report.Created("Direct cost");
        }
        foreach (var g in w.Tasks.GroupBy(t => K(t.WoNo), ci))
        {
            if (NewWo(g.Key) is not { } wo) continue;
            var seq = 0;
            foreach (var t in g.OrderBy(t => t.SeqId).ThenBy(t => t.AutoId).Where(t => !string.IsNullOrWhiteSpace(t.Details)))
            {
                db.WorkOrderTasks.Add(new WorkOrderTask { TenantId = tenantId, WorkOrderId = wo.Id, Sequence = ++seq, Description = Trunc(t.Details!.Trim(), 1000)!,
                    IsDone = wo.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed });
                report.Created("Task");
            }
        }
        foreach (var r in w.WorkOrders)
            if (NewWo(r.WorkorderNo) is { } wo && !string.IsNullOrWhiteSpace(r.Notes))
                db.WorkOrderComments.Add(new WorkOrderComment { TenantId = tenantId, WorkOrderId = wo.Id, Body = r.Notes.Trim(), UserName = "4CSQL notes", CreatedAt = wo.CreatedAt });
        foreach (var g in w.StatusAudit.GroupBy(s => K(s.WONo), ci))
        {
            if (NewWo(g.Key) is not { } wo) continue;
            WorkOrderStatus? prev = null;
            foreach (var s in g.OrderBy(s => s.DT).ThenBy(s => s.ID))
            {
                var (st, hr) = MapStatus(s.WOStatus);
                if (prev == st) continue;
                db.WorkOrderStatusChanges.Add(new WorkOrderStatusChange { TenantId = tenantId, WorkOrderId = wo.Id, FromStatus = prev, ToStatus = st, HoldReason = hr,
                    ChangedAt = Utc(s.DT) ?? wo.CreatedAt, ChangedByName = EmpName(s.EmpNo), LegacyRef = $"status:{s.ID}" });
                prev = st; report.Created("Status change");
            }
        }
        await db.SaveChangesAsync();

        // ---------- Work requests ----------
        var requests = await db.WorkRequests.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToDictionaryAsync(x => x.Number, ci);
        foreach (var r in w.Requests.Where(r => !string.IsNullOrWhiteSpace(r.RequestNo)))
        {
            var number = Trunc(K(r.RequestNo), 50)!;
            if (!requests.TryGetValue(number, out var wr)) { wr = new WorkRequest { Number = number, Title = "" }; db.WorkRequests.Add(wr); requests[number] = wr; report.Created("Work request"); }
            else report.Updated("Work request");
            var (title, desc) = Split(r.ProblemDesc, number);
            var (site, loc, asset) = Place(r.SiteCode, r.LocationNo, r.AssetNo);
            var linked = !string.IsNullOrWhiteSpace(r.WorkorderNo) && woByNo.TryGetValue(K(r.WorkorderNo), out var wo) ? wo : null;
            var st = (r.RequestStatus is { } rs ? w.RequestStatuses.GetValueOrDefault(rs) : null)?.ToLowerInvariant() ?? "";
            wr.LegacyRef = $"wr:{number}"; wr.Title = title; wr.Description = desc ?? (r.Remarks?.Trim() is { Length: > 0 } rm ? rm : null);
            wr.SiteId = site.Id; wr.LocationId = loc; wr.AssetId = asset; wr.Type = MapType(r.WorkType); wr.Priority = MapPriority(r.WorkPriority);
            wr.Status = st.Contains("cancel") ? WorkRequestStatus.Cancelled : linked is not null || st.Contains("raised") || st.Contains("close") ? WorkRequestStatus.Approved : WorkRequestStatus.New;
            wr.WorkOrderId = linked?.Id; wr.RequiredBy = Day(r.RequiredDate); wr.CreatedAt = Utc(r.ReceivedDate) ?? wr.CreatedAt;
            wr.RequesterName = Trunc((r.requestby?.Trim() is { Length: > 0 } rb ? rb : null) ?? EmpName(r.EmployeeNo), 200);
            wr.RequesterPhone = Trunc(r.telno?.Trim() is { Length: > 0 } tel ? tel : null, 50);
            if (linked is not null && linked.WorkRequestId is null) linked.WorkRequestId = wr.Id;
        }
        await db.SaveChangesAsync();
    }
}
