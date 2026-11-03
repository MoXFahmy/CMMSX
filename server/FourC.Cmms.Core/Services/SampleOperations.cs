using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>
/// Simulates twelve months of operations on top of the sample hierarchy: technicians, corrective and breakdown work with labour and downtime,
/// monthly/quarterly PM with realistic compliance, spare parts consumed from stores and replenished through purchase orders.
/// The simulation runs day by day so stock balances, average costs and receipts stay consistent. Runs once per organisation.
/// </summary>
public static class SampleOperations
{
    private sealed record PartSpec(string No, string Name, string Group, string Unit, decimal Price, string Supplier, string[] Cats, decimal Max, decimal Reorder, decimal Qty, int Lead, bool Critical = false, string? Mfr = null);

    private static readonly (string Code, string Name, string Contact, string City, string Country, string Terms, string Email)[] Suppliers =
    [
        ("SKF", "SKF Distribution MEA", "Hossam Fathy", "Cairo", "Egypt", "Net 30", "orders@skf-mea.example"),
        ("SCHN", "Schneider Electric Parts", "Laila Mansour", "Cairo", "Egypt", "Net 45", "parts@se-eg.example"),
        ("ATCO", "Atlas Copco Service", "Mark de Vries", "Alexandria", "Egypt", "Net 30", "service@atlascopco.example"),
        ("GRNG", "Delta Industrial Supply", "Nadia Saleh", "Cairo", "Egypt", "Net 30", "sales@delta-industrial.example"),
        ("GULF", "Gulf Lubricants Co.", "Rashid Al Mansoori", "Dubai", "United Arab Emirates", "Net 60", "orders@gulflube.example"),
        ("COOL", "CoolAir HVAC Spares", "Priya Nair", "Dubai", "United Arab Emirates", "Net 30", "spares@coolair.example"),
    ];

    private static readonly PartSpec[] Parts =
    [
        new("BRG-6205-2RS", "Deep groove ball bearing 6205-2RS", "BEAR", "EA", 6.8m, "SKF", ["MECH", "PROD", "VEH"], 40, 12, 24, 10, Mfr: "SKF"),
        new("BRG-6308-ZZ", "Deep groove ball bearing 6308-ZZ", "BEAR", "EA", 18.5m, "SKF", ["MECH", "PROD"], 20, 6, 12, 14, Mfr: "SKF"),
        new("BRG-22220-E", "Spherical roller bearing 22220 E", "BEAR", "EA", 245m, "SKF", ["MECH"], 4, 1, 2, 30, true, "SKF"),
        new("SEAL-MECH-35", "Mechanical seal 35 mm (pump)", "BEAR", "EA", 120m, "SKF", ["MECH", "FIRE"], 6, 2, 4, 21, true),
        new("BELT-SPB-1800", "V-belt SPB 1800", "MECH", "EA", 14.2m, "GRNG", ["MECH", "HVAC", "PROD"], 30, 8, 20, 7),
        new("BELT-XPA-1250", "Cogged V-belt XPA 1250", "MECH", "EA", 11.9m, "GRNG", ["HVAC", "PROD"], 24, 6, 12, 7),
        new("CPL-JAW-L100", "Jaw coupling element L100", "MECH", "EA", 9.5m, "GRNG", ["MECH", "PROD"], 12, 3, 8, 10),
        new("CHN-08B-1", "Roller chain 08B-1 (5 m)", "MECH", "ROLL", 38m, "GRNG", ["PROD", "MECH"], 8, 2, 4, 14),
        new("FLT-AHU-592", "Panel filter G4 592x592x48", "FILT", "EA", 16.5m, "COOL", ["HVAC"], 80, 24, 48, 10),
        new("FLT-BAG-F7", "Bag filter F7 592x592x600", "FILT", "EA", 42m, "COOL", ["HVAC"], 40, 12, 24, 14),
        new("FLT-OIL-GA37", "Oil filter element GA37", "FILT", "EA", 36m, "ATCO", ["MECH"], 10, 3, 6, 14, Mfr: "Atlas Copco"),
        new("FLT-AIR-GA37", "Air intake filter GA37", "FILT", "EA", 52m, "ATCO", ["MECH"], 10, 3, 6, 14, Mfr: "Atlas Copco"),
        new("SEP-GA37", "Oil separator element GA37", "FILT", "EA", 189m, "ATCO", ["MECH"], 4, 1, 2, 21, true, "Atlas Copco"),
        new("FLT-FUEL-C18", "Fuel filter C18 generator", "FILT", "EA", 48m, "GRNG", ["ELEC"], 8, 2, 4, 10, Mfr: "Caterpillar"),
        new("LUB-GRS-EP2", "Grease EP2 lithium (18 kg pail)", "LUBE", "PAIL", 96m, "GULF", ["MECH", "PROD", "VEH"], 10, 3, 6, 7),
        new("LUB-ISO68", "Hydraulic oil ISO VG 68 (20 L)", "LUBE", "DRUM", 74m, "GULF", ["PROD", "MECH"], 12, 4, 8, 7),
        new("LUB-COMP-46", "Compressor oil Roto-Inject 46 (20 L)", "LUBE", "DRUM", 210m, "ATCO", ["MECH"], 6, 2, 4, 14, Mfr: "Atlas Copco"),
        new("LUB-15W40", "Engine oil 15W-40 (20 L)", "LUBE", "DRUM", 82m, "GULF", ["ELEC", "VEH"], 8, 2, 4, 7),
        new("ELC-CONT-25A", "Contactor 25 A, 230 V coil", "ELEC", "EA", 44m, "SCHN", ["ELEC", "HVAC", "PROD", "MECH"], 16, 4, 10, 10, Mfr: "Schneider Electric"),
        new("ELC-OLR-1218", "Thermal overload relay 12–18 A", "ELEC", "EA", 39m, "SCHN", ["ELEC", "MECH", "PROD"], 12, 3, 6, 10, Mfr: "Schneider Electric"),
        new("ELC-MCB-C16", "MCB 1P C16", "ELEC", "EA", 7.4m, "SCHN", ["ELEC", "IT"], 40, 10, 20, 7, Mfr: "Schneider Electric"),
        new("ELC-FUSE-63", "HRC fuse NH00 63 A", "ELEC", "EA", 12.6m, "SCHN", ["ELEC", "FIRE"], 30, 8, 20, 7),
        new("ELC-RELAY-24", "Control relay 24 VDC, 2 C/O", "ELEC", "EA", 15.8m, "SCHN", ["PROD", "FIRE", "IT"], 20, 5, 10, 7),
        new("ELC-PROX-M18", "Inductive proximity sensor M18", "ELEC", "EA", 58m, "SCHN", ["PROD"], 10, 3, 6, 14),
        new("ELC-MOT-5K5", "Motor 5.5 kW 4-pole IE3", "ELEC", "EA", 640m, "SCHN", ["MECH", "PROD", "HVAC"], 2, 1, 1, 28, true),
        new("ELC-BATT-12V", "VRLA battery 12 V 9 Ah (UPS)", "ELEC", "EA", 32m, "SCHN", ["ELEC", "FIRE", "IT"], 24, 6, 12, 14),
        new("HVAC-CAP-45", "Run capacitor 45 µF", "ELEC", "EA", 13.5m, "COOL", ["HVAC"], 20, 5, 10, 7),
        new("HVAC-R410A", "Refrigerant R410A (11.3 kg)", "CONS", "CYL", 165m, "COOL", ["HVAC"], 6, 2, 4, 10),
        new("HYD-HOSE-12", "Hydraulic hose 1/2\" 2SN (per m)", "HYD", "M", 8.9m, "GRNG", ["PROD", "VEH"], 40, 10, 20, 7),
        new("HYD-CYL-SEAL", "Cylinder seal kit 63 mm", "HYD", "SET", 54m, "GRNG", ["PROD"], 6, 2, 4, 14),
        new("CNS-CABLE-TIE", "Cable ties 300 mm (bag of 100)", "CONS", "BAG", 4.2m, "GRNG", ["ELEC", "IT", "PROD", "HVAC"], 30, 8, 20, 5),
        new("CNS-WD40", "Penetrating lubricant spray 400 ml", "CONS", "EA", 5.6m, "GRNG", ["MECH", "PROD", "VEH", "PLMB"], 36, 10, 24, 5),
        new("PLB-VALVE-25", "Ball valve 1\" brass", "MECH", "EA", 18m, "GRNG", ["PLMB", "FIRE"], 12, 3, 6, 7),
        new("SAF-GLOVE-CUT", "Cut-resistant gloves (pair)", "SAFE", "PR", 3.9m, "GRNG", ["MECH", "PROD", "ELEC", "HVAC", "PLMB", "VEH"], 60, 20, 48, 5),
    ];

    public static async Task<object> SeedAsync(CmmsDbContext db, int seed = 7, CancellationToken ct = default)
    {
        if (await db.Stores.AnyAsync(s => s.Code == "CAI-MAIN", ct)) return new { skipped = true };
        var rnd = new Random(seed);
        var tenantId = db.CurrentTenantId ?? throw new InvalidOperationException("No tenant.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddDays(-364);
        var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        if (sites.Count == 0) return new { skipped = true };
        var assets = await db.Assets.AsNoTracking().Where(a => a.Status != AssetStatus.Disposed).Select(a => new { a.Id, a.SiteId, a.LocationId, a.Name, Cat = a.Category!.Code, a.Criticality }).ToListAsync(ct);
        if (assets.Count == 0) return new { skipped = true };
        var trades = await db.Trades.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var failures = await db.FailureCodes.AsNoTracking().ToListAsync(ct);
        Guid? Trade(string cat) => trades.GetValueOrDefault(cat switch { "HVAC" => "HVAC", "ELEC" or "FIRE" or "IT" => "ELEC", "PLMB" => "PLMB", _ => "MECH" }) is var g && g != Guid.Empty ? g : null;

        // ---- people
        var employees = await db.Employees.Where(e => e.IsActive).ToListAsync(ct);
        if (employees.Count < 4)
        {
            var people = new (string Name, string Trade, decimal Rate, bool Contractor, string? Company)[]
            {
                ("Ahmed Hassan", "MECH", 22, false, null), ("Mona Adel", "ELEC", 26, false, null), ("Omar Khaled", "MECH", 20, false, null), ("Sara Nabil", "INST", 27, false, null),
                ("Youssef Ali", "HVAC", 24, false, null), ("Karim Mostafa", "ELEC", 23, false, null), ("Rania Fawzy", "MECH", 25, false, null), ("Tarek Samir", "PLMB", 19, false, null),
                ("Hany Ibrahim", "HVAC", 35, true, "CoolAir HVAC Services"),
            };
            var i = 0;
            foreach (var p in people)
            {
                var site = sites[i % sites.Count];
                var e = new Employee { Code = $"T{++i:000}", FullName = p.Name, JobTitle = p.Contractor ? "Contract technician" : "Maintenance technician", HourlyRate = p.Rate, OvertimeRate = Math.Round(p.Rate * 1.5m, 2),
                    IsContractor = p.Contractor, Company = p.Company, TradeIds = trades.TryGetValue(p.Trade, out var tid) ? [tid] : [], SiteIds = [site.Id], Email = $"{p.Name.Split(' ')[0].ToLowerInvariant()}@example.com" };
                db.Employees.Add(e); employees.Add(e);
            }
        }

        // ---- inventory master data
        var groups = await db.PartGroups.ToDictionaryAsync(g => g.Code, ct);
        foreach (var (code, name) in TenantProvisioning.DefaultPartGroups.Where(g => !groups.ContainsKey(g.Code)))
        { var g = new PartGroup { Code = code, Name = name }; db.PartGroups.Add(g); groups[code] = g; }
        var suppliers = new Dictionary<string, Supplier>();
        foreach (var s in Suppliers)
        {
            var x = await db.Suppliers.FirstOrDefaultAsync(y => y.Code == s.Code, ct) ?? new Supplier { Code = s.Code, Name = s.Name, ContactName = s.Contact, City = s.City, Country = s.Country, PaymentTerms = s.Terms, Email = s.Email, Phone = "+20 2 5555 0100" };
            if (db.Entry(x).State == EntityState.Detached) db.Suppliers.Add(x);
            suppliers[s.Code] = x;
        }
        var stores = new List<(Store Store, bool Production)>();
        foreach (var site in sites.Take(3))
        {
            var st = new Store { Code = $"{site.Code}-MAIN", Name = $"{site.Name.Split(' ')[0]} main store", SiteId = site.Id, Description = "Main spare parts store" };
            db.Stores.Add(st); stores.Add((st, !site.Name.Contains("Office")));
        }
        var parts = new Dictionary<string, (Part Part, PartSpec Spec)>();
        foreach (var p in Parts)
        {
            var part = new Part { PartNumber = p.No, Name = p.Name, UnitOfMeasure = p.Unit, GroupId = groups[p.Group].Id, StandardPrice = p.Price, AverageCost = p.Price, LeadTimeDays = p.Lead,
                IsCritical = p.Critical, Manufacturer = p.Mfr, PreferredSupplierId = suppliers[p.Supplier].Id, Specification = null };
            db.Parts.Add(part); parts[p.No] = (part, p);
        }
        var levels = new Dictionary<(Guid, Guid), StockLevel>();
        foreach (var (store, production) in stores)
            foreach (var (part, spec) in parts.Values)
            {
                if (!production && !spec.Cats.Any(c => c is "HVAC" or "ELEC" or "IT" or "FIRE" or "PLMB")) continue;
                var f = production ? 1m : 0.5m;
                var l = new StockLevel { TenantId = tenantId, PartId = part.Id, StoreId = store.Id, Bin = $"{(char)('A' + rnd.Next(6))}{rnd.Next(1, 9)}-{rnd.Next(1, 5)}",
                    MaxLevel = Math.Max(1, Math.Ceiling(spec.Max * f)), ReorderLevel = Math.Max(1, Math.Ceiling(spec.Reorder * f)), MinLevel = Math.Max(0, Math.Ceiling(spec.Reorder * f / 2)), ReorderQuantity = Math.Max(1, Math.Ceiling(spec.Qty * f)) };
                db.StockLevels.Add(l); levels[(part.Id, store.Id)] = l;
            }
        await db.SaveChangesAsync(ct);

        // ---- PM programme
        var pmTasks = new Dictionary<string, PmTask>();
        foreach (var (code, name, trade, hours, steps) in new[]
        {
            ("PM-AHU-M", "AHU monthly service", "HVAC", 2m, new[] { "Replace panel filters", "Check belt tension and wear", "Clean drain pan", "Record supply/return temperatures" }),
            ("PM-COMP-Q", "Compressor quarterly service", "MECH", 3m, new[] { "Change oil filter", "Replace air filter", "Check oil level and leaks", "Test safety valve" }),
            ("PM-GEN-M", "Generator monthly test run", "ELEC", 1.5m, new[] { "Run on load 30 min", "Check fuel, oil and coolant", "Inspect batteries" }),
            ("PM-PUMP-Q", "Pump quarterly inspection", "MECH", 1.5m, new[] { "Check vibration and noise", "Inspect mechanical seal", "Grease bearings" }),
            ("PM-FIRE-M", "Fire system monthly test", "ELEC", 1m, new[] { "Test alarm panel", "Run fire pump", "Log results" }),
            ("PM-LINE-M", "Production line monthly lubrication", "MECH", 2.5m, new[] { "Lubricate chains and bearings", "Inspect belts", "Check guards" }),
        })
        {
            var t = new PmTask { Code = code, Name = name, TradeId = trades.GetValueOrDefault(trade), EstimatedHours = hours };
            var n = 1; foreach (var s in steps) t.Steps.Add(new PmTaskStep { Sequence = n++, Description = s });
            db.PmTasks.Add(t); pmTasks[code] = t;
        }
        string TaskFor(string cat, string name) => cat switch
        {
            "HVAC" => "PM-AHU-M", "FIRE" => "PM-FIRE-M", "PROD" => "PM-LINE-M",
            _ when name.Contains("Compressor") => "PM-COMP-Q", _ when name.Contains("Generator") => "PM-GEN-M", _ => "PM-PUMP-Q",
        };
        var pmAssets = assets.Where(a => a.Cat is "HVAC" or "FIRE" or "PROD" || a.Name.Contains("Compressor") || a.Name.Contains("Generator") || a.Name.Contains("Pump"))
            .Where(a => a.Criticality is Criticality.High or Criticality.Critical || rnd.NextDouble() < 0.35).Take(36).ToList();
        var schedules = new List<(PmSchedule S, int Months, string Cat)>();
        var pmNo = await db.PmSchedules.CountAsync(ct);
        foreach (var a in pmAssets)
        {
            var task = pmTasks[TaskFor(a.Cat, a.Name)];
            var months = task.Code.EndsWith("-Q") ? 3 : 1;
            var s = new PmSchedule { Code = $"PM-{++pmNo:00000}", Name = $"{task.Name} — {a.Name}", SiteId = a.SiteId, LocationId = a.LocationId, AssetId = a.Id, TradeId = task.TradeId, PmTaskId = task.Id,
                Frequency = months, Unit = FrequencyUnit.Month, StartDate = start.AddDays(rnd.Next(0, 28)), LeadDays = 7, Priority = WorkPriority.Medium };
            db.PmSchedules.Add(s); schedules.Add((s, months, a.Cat));
        }
        await db.SaveChangesAsync(ct);

        // ---- simulation state
        var avg = parts.Values.ToDictionary(p => p.Part.Id, p => p.Spec.Price);
        var onHand = levels.Keys.ToDictionary(k => k, _ => 0m);
        var onOrder = levels.Keys.ToDictionary(k => k, _ => 0m);
        var pendingReceipts = new List<(DateOnly Date, PurchaseOrder Po, PurchaseOrderLine Line, Guid StoreId, decimal Qty, decimal Price)>();
        var txs = new List<StockTransaction>();
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct)).Settings;
        var woPrefix = settings.WorkOrderNumberPrefix ?? "WO-";
        var poPrefix = settings.PurchaseOrderNumberPrefix ?? "PO-";
        long woSeq = await MaxNumberAsync(db.WorkOrders.Select(w => w.Number), woPrefix, ct);
        long poSeq = await MaxNumberAsync(db.PurchaseOrders.Select(o => o.Number), poPrefix, ct);
        var storeBySite = stores.ToDictionary(s => s.Store.SiteId, s => s.Store);
        var badList = assets.Where(a => a.Cat is "MECH" or "PROD").OrderBy(_ => rnd.Next()).Take(8).ToList();
        var badActors = badList.Select(a => a.Id).ToHashSet();
        var woCount = 0; var poCount = 0;

        void Receive(DateOnly date, Guid partId, Guid storeId, decimal qty, decimal price, Guid? supplierId, PurchaseOrder? po, PurchaseOrderLine? line, string? note)
        {
            var key = (partId, storeId);
            var total = onHand.Where(k => k.Key.Item1 == partId).Sum(k => Math.Max(k.Value, 0));
            avg[partId] = Math.Round((avg[partId] * total + price * qty) / (total + qty), 4);
            onHand[key] += qty;
            txs.Add(new StockTransaction { TenantId = tenantId, Type = StockTransactionType.Receipt, PartId = partId, StoreId = storeId, Quantity = qty, UnitCost = price, Date = date, SupplierId = supplierId,
                PurchaseOrderId = po?.Id, PurchaseOrderLineId = line?.Id, Reference = po?.Number ?? note, CreatedAt = At(date, 10), CreatedByName = "Stores" });
            parts.Values.First(p => p.Part.Id == partId).Part.LastPurchasePrice = price;
        }
        bool Issue(DateOnly date, Guid partId, Guid storeId, decimal qty, Guid woId, string by)
        {
            var key = (partId, storeId);
            if (!onHand.TryGetValue(key, out var bal) || bal < qty) return false;
            onHand[key] = bal - qty;
            txs.Add(new StockTransaction { TenantId = tenantId, Type = StockTransactionType.Issue, PartId = partId, StoreId = storeId, Quantity = -qty, UnitCost = avg[partId], Date = date, WorkOrderId = woId,
                CreatedAt = At(date, 14), CreatedByName = by });
            return true;
        }
        DateTimeOffset At(DateOnly d, int hour) => new(d.ToDateTime(new TimeOnly(Math.Clamp(hour, 0, 23), rnd.Next(60))), TimeSpan.Zero);

        // Opening stock: counted in at the start of the period.
        foreach (var (key, l) in levels) Receive(start, key.Item1, key.Item2, l.MaxLevel, Math.Round(avg[key.Item1] * (decimal)(0.92 + rnd.NextDouble() * 0.06), 2), null, null, null, "Opening balance");

        var wos = new List<WorkOrder>();
        var titles = new Dictionary<string, string[]>
        {
            ["MECH"] = ["Abnormal noise from bearing", "Belt slipping", "Oil leak at gearbox", "Coupling worn", "Vibration high", "Pressure low"],
            ["PROD"] = ["Line stopped — jam at infeed", "Sensor not detecting", "Hydraulic leak on press", "Chain broken", "Guard interlock fault", "Motor overheating"],
            ["HVAC"] = ["Unit not cooling", "Water leaking from indoor unit", "Fan belt squealing", "High head pressure trip", "Thermostat fault"],
            ["ELEC"] = ["Breaker tripping", "Generator failed to start", "UPS alarm — battery", "Lighting circuit fault", "Panel hot spot found"],
            ["PLMB"] = ["Water heater not heating", "Leaking valve", "Blocked drain"], ["FIRE"] = ["Fire panel fault signal", "Fire pump pressure low"],
            ["VEH"] = ["Forklift hydraulic leak", "Forklift won't lift", "Brake inspection"], ["IT"] = ["Server room temperature alarm", "Rack PDU failure"],
        };
        Employee Tech(Guid siteId, Guid? tradeId)
        {
            var pool = employees.Where(e => (tradeId is null || e.TradeIds.Contains(tradeId.Value)) && e.SiteIds.Contains(siteId)).ToList();
            if (pool.Count == 0) pool = employees.Where(e => tradeId is null || e.TradeIds.Contains(tradeId.Value)).ToList();
            if (pool.Count == 0) pool = employees;
            return pool[rnd.Next(pool.Count)];
        }
        WorkOrder NewWo(DateOnly created, string title, WorkType type, WorkPriority pri, Guid siteId, Guid? locationId, Guid? assetId, Guid? tradeId)
        {
            var w = new WorkOrder { Number = $"{woPrefix}{++woSeq:00000}", Title = title, Type = type, Priority = pri, SiteId = siteId, LocationId = locationId, AssetId = assetId, TradeId = tradeId,
                CreatedAt = At(created, rnd.Next(7, 17)), DueDate = created.AddDays(pri switch { WorkPriority.Urgent => 1, WorkPriority.High => 3, WorkPriority.Medium => 7, _ => 14 }), RequestedBy = "Production supervisor" };
            db.WorkOrders.Add(w); wos.Add(w); woCount++;
            return w;
        }
        void Finish(WorkOrder w, DateTimeOffset startedAt, double repairHours, DateOnly today0, Employee tech, bool overtime)
        {
            w.StartedAt = startedAt;
            w.CompletedAt = startedAt.AddHours(repairHours);
            var doneDay = DateOnly.FromDateTime(w.CompletedAt.Value.UtcDateTime);
            if (doneDay > today0) { w.Status = WorkOrderStatus.InProgress; w.CompletedAt = null; }
            else
            {
                w.Status = doneDay < today0.AddDays(-14) && rnd.NextDouble() < 0.85 ? WorkOrderStatus.Closed : WorkOrderStatus.Completed;
                if (w.Status == WorkOrderStatus.Closed) w.ClosedAt = w.CompletedAt.Value.AddDays(rnd.Next(1, 6));
                w.CompletionNotes = "Work completed and equipment returned to service.";
            }
            w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = tech.Id });
            var hrs = (decimal)Math.Round(Math.Clamp(repairHours, 0.5, 10), 1);
            var ot = overtime ? Math.Round(hrs * 0.4m, 1) : 0;
            db.WorkOrderLabor.Add(new WorkOrderLabor { TenantId = tenantId, WorkOrderId = w.Id, EmployeeId = tech.Id, WorkDate = DateOnly.FromDateTime(startedAt.UtcDateTime), Hours = hrs - ot, OvertimeHours = ot,
                Rate = tech.HourlyRate ?? 20, OvertimeRate = tech.OvertimeRate ?? 30, CreatedAt = w.CompletedAt ?? startedAt });
        }

        // PM due dates for the year
        var pmDue = new List<(DateOnly Due, PmSchedule S, string Cat)>();
        foreach (var (s, months, cat) in schedules)
        {
            for (var d = s.StartDate; d <= today.AddDays(30); d = d.AddMonths(months)) pmDue.Add((d, s, cat));
        }

        for (var day = start; day <= today; day = day.AddDays(1))
        {
            // receipts arriving today
            foreach (var r in pendingReceipts.Where(r => r.Date == day).ToList())
            {
                Receive(day, r.Line.PartId!.Value, r.StoreId, r.Qty, r.Price, r.Po.SupplierId, r.Po, r.Line, null);
                r.Line.QuantityReceived += r.Qty; onOrder[(r.Line.PartId!.Value, r.StoreId)] -= r.Qty;
                r.Po.Status = r.Po.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Closed : PurchaseOrderStatus.PartiallyReceived;
                pendingReceipts.Remove(r);
            }
            var weekday = day.DayOfWeek;
            // corrective & breakdown work (fewer at weekends)
            var n = weekday is DayOfWeek.Friday or DayOfWeek.Saturday ? rnd.Next(0, 2) : rnd.Next(1, 4);
            for (var k = 0; k < n; k++)
            {
                var a = rnd.NextDouble() < 0.25 && badList.Count > 0 ? badList[rnd.Next(badList.Count)] : assets[rnd.Next(assets.Count)];
                var breakdown = rnd.NextDouble() < (badActors.Contains(a.Id) ? 0.55 : 0.25);
                var type = breakdown ? WorkType.Breakdown : rnd.NextDouble() < 0.15 ? WorkType.Inspection : rnd.NextDouble() < 0.05 ? WorkType.Safety : WorkType.Corrective;
                var pri = breakdown ? (a.Criticality >= Criticality.High ? WorkPriority.Urgent : WorkPriority.High) : rnd.NextDouble() < 0.6 ? WorkPriority.Medium : WorkPriority.Low;
                var list = titles.GetValueOrDefault(a.Cat) ?? titles["MECH"];
                var tradeId = Trade(a.Cat);
                var w = NewWo(day, $"{list[rnd.Next(list.Length)]} — {a.Name}", type, pri, a.SiteId, a.LocationId, a.Id, tradeId);
                var tech = Tech(a.SiteId, tradeId);
                var response = breakdown ? rnd.NextDouble() * 4 : rnd.NextDouble() * 72;
                var repair = breakdown ? 1 + rnd.NextDouble() * (badActors.Contains(a.Id) ? 9 : 5) : 0.5 + rnd.NextDouble() * 4;
                var startedAt = w.CreatedAt.AddHours(response);
                if (DateOnly.FromDateTime(startedAt.UtcDateTime) > today || (day > today.AddDays(-6) && rnd.NextDouble() < 0.5))
                {
                    w.Status = rnd.NextDouble() < 0.2 ? WorkOrderStatus.OnHold : WorkOrderStatus.Open;
                    if (w.Status == WorkOrderStatus.OnHold) w.HoldReason = HoldReason.WaitingForParts;
                    w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = tech.Id });
                    continue;
                }
                Finish(w, startedAt, repair, today, tech, breakdown && rnd.NextDouble() < 0.4);
                if (breakdown)
                {
                    w.AssetWasDown = true;
                    w.DowntimeHours = Math.Round((decimal)(response + repair), 1);
                    var fc = failures.Count == 0 ? null : failures[Math.Min(failures.Count - 1, (int)Math.Floor(Math.Pow(rnd.NextDouble(), 1.8) * failures.Count))];
                    w.FailureCodeId = rnd.NextDouble() < 0.9 ? fc?.Id : null;
                    w.CauseNotes = fc is null ? null : $"Root cause: {fc.Name.ToLowerInvariant()}.";
                }
                // parts
                if (w.CompletedAt is not null && (breakdown || rnd.NextDouble() < 0.4) && storeBySite.TryGetValue(a.SiteId, out var store))
                {
                    var candidates = parts.Values.Where(p => p.Spec.Cats.Contains(a.Cat) && levels.ContainsKey((p.Part.Id, store.Id))).ToList();
                    for (var j = rnd.Next(1, 3); j > 0 && candidates.Count > 0; j--)
                    {
                        var p = candidates[rnd.Next(candidates.Count)];
                        var qty = p.Spec.Price > 150 ? 1 : rnd.Next(1, 4);
                        Issue(DateOnly.FromDateTime(w.CompletedAt.Value.UtcDateTime), p.Part.Id, store.Id, qty, w.Id, tech.FullName);
                    }
                }
            }
            // PM work orders raised 7 days ahead of due
            foreach (var (due, s, cat) in pmDue.Where(x => x.Due.AddDays(-7) == day))
            {
                var task = pmTasks.Values.First(t => t.Id == s.PmTaskId);
                var w = NewWo(day, s.Name, WorkType.Preventive, WorkPriority.Medium, s.SiteId, s.LocationId, s.AssetId, s.TradeId);
                w.PmScheduleId = s.Id; w.PmTaskId = s.PmTaskId; w.PmDueDate = due; w.DueDate = due; w.EstimatedHours = task.EstimatedHours; w.RequestedBy = "PM scheduler";
                var seqNo = 1; foreach (var st in task.Steps) db.WorkOrderTasks.Add(new WorkOrderTask { TenantId = tenantId, WorkOrderId = w.Id, Sequence = seqNo++, Description = st.Description });
                s.LastGeneratedAt = w.CreatedAt; s.LastWorkOrderId = w.Id; s.GenerationCount++; s.LastDueDate = due;
                var roll = rnd.NextDouble();
                var doneOn = roll < 0.84 ? due.AddDays(-rnd.Next(0, 4)) : roll < 0.95 ? due.AddDays(rnd.Next(1, 12)) : (DateOnly?)null;
                var tech = Tech(s.SiteId, s.TradeId);
                if (doneOn is null || doneOn > today) { w.Status = WorkOrderStatus.Open; w.Assignees.Add(new WorkOrderAssignee { WorkOrderId = w.Id, EmployeeId = tech.Id }); continue; }
                Finish(w, At(doneOn.Value, 8), (double)(task.EstimatedHours ?? 1) * (0.8 + rnd.NextDouble() * 0.5), today, tech, false);
                if (w.CompletedAt is not null && storeBySite.TryGetValue(s.SiteId, out var store))
                {
                    var consumable = cat switch { "HVAC" => "FLT-AHU-592", "PROD" => "LUB-GRS-EP2", _ when task.Code == "PM-COMP-Q" => "FLT-OIL-GA37", _ when task.Code == "PM-GEN-M" => "FLT-FUEL-C18", _ => "LUB-GRS-EP2" };
                    var qty = consumable == "FLT-AHU-592" ? rnd.Next(2, 5) : 1;
                    if (cat != "FIRE") Issue(doneOn.Value, parts[consumable].Part.Id, store.Id, qty, w.Id, tech.FullName);
                }
            }
            // weekly replenishment run (Sunday): order what is at or below re-order, grouped by supplier and store
            if (weekday == DayOfWeek.Sunday && day > start && day < today.AddDays(-35)) // the last month is left for the re-order proposal
            {
                var due = levels.Where(l => onHand[l.Key] + onOrder[l.Key] <= l.Value.ReorderLevel).ToList();
                foreach (var grp in due.GroupBy(l => (parts.Values.First(p => p.Part.Id == l.Key.Item1).Spec.Supplier, l.Key.Item2)))
                {
                    var sup = suppliers[grp.Key.Supplier];
                    var store = stores.First(s => s.Store.Id == grp.Key.Item2).Store;
                    var lead = grp.Max(l => parts.Values.First(p => p.Part.Id == l.Key.Item1).Spec.Lead);
                    var po = new PurchaseOrder { Number = $"{poPrefix}{++poSeq:00000}", SiteId = store.SiteId, SupplierId = sup.Id, OrderDate = day, ExpectedDate = day.AddDays(lead), Currency = settings.Currency,
                        PaymentTerms = sup.PaymentTerms, RequestedBy = "Stores", TaxPercent = settings.DefaultTaxPercent, Status = PurchaseOrderStatus.Approved, SubmittedAt = At(day, 9), ApprovedAt = At(day, 11),
                        ApprovedByName = "Maintenance Manager", CreatedAt = At(day, 9), Notes = "Weekly replenishment." };
                    var lineNo = 1;
                    foreach (var l in grp)
                    {
                        var (part, spec) = parts.Values.First(p => p.Part.Id == l.Key.Item1);
                        var qty = Math.Max(l.Value.ReorderQuantity, l.Value.MaxLevel - onHand[l.Key] - onOrder[l.Key]);
                        var price = Math.Round(spec.Price * (decimal)(0.97 + rnd.NextDouble() * 0.1) * (1 + (decimal)(day.DayNumber - start.DayNumber) / 365m * 0.04m), 2);
                        var line = new PurchaseOrderLine { LineNumber = lineNo++, PartId = part.Id, Description = part.Name, UnitOfMeasure = part.UnitOfMeasure, Quantity = qty, UnitPrice = price, StoreId = store.Id };
                        po.Lines.Add(line); onOrder[l.Key] += qty;
                        var arrive = day.AddDays(lead + (rnd.NextDouble() < 0.7 ? rnd.Next(-3, 1) : rnd.Next(1, 9)));
                        var split = rnd.NextDouble() < 0.15 && qty > 1;
                        var first = split ? Math.Floor(qty / 2) : qty;
                        if (arrive <= today) pendingReceipts.Add((arrive, po, line, store.Id, first, price));
                        if (split && arrive.AddDays(7) <= today) pendingReceipts.Add((arrive.AddDays(7), po, line, store.Id, qty - first, price));
                    }
                    db.PurchaseOrders.Add(po); poCount++;
                }
            }
        }
        // A few orders waiting on someone: a draft and one awaiting approval.
        foreach (var (code, status) in new[] { ("SKF", PurchaseOrderStatus.Submitted), ("COOL", PurchaseOrderStatus.Draft) })
        {
            var sup = suppliers[code];
            var picks = parts.Values.Where(p => p.Spec.Supplier == code).Take(3).ToList();
            var store = stores[0].Store;
            var po = new PurchaseOrder { Number = $"{poPrefix}{++poSeq:00000}", SiteId = store.SiteId, SupplierId = sup.Id, OrderDate = today, ExpectedDate = today.AddDays(14), Currency = settings.Currency,
                PaymentTerms = sup.PaymentTerms, RequestedBy = "Maintenance planner", TaxPercent = settings.DefaultTaxPercent, Status = status, SubmittedAt = status == PurchaseOrderStatus.Submitted ? DateTimeOffset.UtcNow : null,
                Notes = status == PurchaseOrderStatus.Draft ? "Spares for the summer HVAC campaign." : "Critical spares for line 2 overhaul." };
            var i = 1;
            foreach (var (part, spec) in picks) po.Lines.Add(new PurchaseOrderLine { LineNumber = i++, PartId = part.Id, Description = part.Name, UnitOfMeasure = part.UnitOfMeasure, Quantity = spec.Qty, UnitPrice = spec.Price, StoreId = store.Id });
            db.PurchaseOrders.Add(po); poCount++;
        }

        // Write balances, costs and PM next-due dates.
        foreach (var (key, l) in levels) l.QuantityOnHand = onHand[key];
        foreach (var (part, _) in parts.Values) part.AverageCost = avg[part.Id];
        foreach (var (s, months, _) in schedules)
        {
            var next = s.StartDate; while (next <= (s.LastDueDate ?? s.StartDate.AddDays(-1))) next = next.AddMonths(months);
            s.NextDueDate = next;
        }
        db.StockTransactions.AddRange(txs);
        await db.SaveChangesAsync(ct);
        await db.SetSequenceAtLeastAsync("WorkOrder", woSeq + 1, ct);
        await db.SetSequenceAtLeastAsync("PurchaseOrder", poSeq + 1, ct);
        return new { workOrders = woCount, purchaseOrders = poCount, stockTransactions = txs.Count, parts = parts.Count, pmSchedules = schedules.Count };
    }

    private static async Task<long> MaxNumberAsync(IQueryable<string> numbers, string prefix, CancellationToken ct) =>
        (await numbers.Where(n => n.StartsWith(prefix)).ToListAsync(ct)).Select(n => long.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
}
