using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Imports the 4CSQL materials and purchasing modules: suppliers, sub-stores, attribute groups, stock codes with store levels and
/// alternates, purchase requisitions/orders with their lines, and the receive / issue / return / adjustment ledger (transfers included).
/// Re-runnable: everything is matched on its legacy key, and ledger rows that were already imported are skipped.
/// </summary>
static class InventoryImport
{
    public static async Task RunAsync(CmmsDbContext db, Guid tenantId, LegacyMaterials m, Report report, Func<string?, Site> siteFor, string baseCurrency)
    {
        var ci = StringComparer.OrdinalIgnoreCase;
        static string K(string? s) => (s ?? "").Trim();
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
        static DateOnly? Day(DateTime? d) => d is { Year: > 1900 } v ? DateOnly.FromDateTime(v) : null;
        static decimal Num(double? v, int scale = 3) => v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value) || Math.Abs(v.Value) > 1e12 ? 0 : Math.Round((decimal)v.Value, scale);
        static DateTimeOffset At(DateTime? d) => d is { Year: > 1900 } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : DateTimeOffset.UtcNow;

        // ---------- Suppliers ----------
        var suppliers = (await db.Suppliers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync()).GroupBy(x => x.Code, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        foreach (var s in m.Suppliers.Where(s => !string.IsNullOrWhiteSpace(s.SupplierNo)))
        {
            var code = Trunc(K(s.SupplierNo), 50)!;
            if (!suppliers.TryGetValue(code, out var x)) { x = new Supplier { TenantId = tenantId, Code = code, Name = "" }; db.Suppliers.Add(x); suppliers[code] = x; report.Created("Supplier"); }
            else report.Updated("Supplier");
            x.Name = Trunc(Clean(s.SupplierName) ?? code, 200)!; x.ContactName = Trunc(Clean(s.ContactName), 200); x.Email = Trunc(Clean(s.EmailAddress), 200);
            x.Phone = Trunc(Clean(s.PhoneNumber), 50); x.City = Trunc(Clean(s.City), 100); x.Country = Trunc(Clean(s.Country), 100);
            x.Address = Trunc(string.Join("\n", new[] { s.Address, s.StateOrProvince, s.PostalCode }.Select(Clean).Where(v => v != null)), 1000) is { Length: > 0 } a ? a : null;
            x.Notes = Trunc(string.Join("\n", new[] { Clean(s.Services) is { } sv ? $"Services: {sv}" : null, Clean(s.AccountNo) is { } ac ? $"Account no.: {ac}" : null, Clean(s.FaxNumber) is { } fx ? $"Fax: {fx}" : null }.Where(v => v != null)), 4000) is { Length: > 0 } n ? n : null;
            x.IsActive = s.Active is null || s.Active != 0; x.LegacyRef = $"supplier:{code}";
        }
        await db.SaveChangesAsync();

        // ---------- Stores (sub-stores) ----------
        var stores = (await db.Stores.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync()).GroupBy(x => x.Code, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        foreach (var s in m.Stores.Where(s => !string.IsNullOrWhiteSpace(s.SubStoreCode)))
        {
            var code = Trunc(K(s.SubStoreCode), 50)!;
            if (!stores.TryGetValue(code, out var x)) { x = new Store { TenantId = tenantId, Code = code, Name = "" }; db.Stores.Add(x); stores[code] = x; report.Created("Store"); }
            else report.Updated("Store");
            x.Name = Trunc(Clean(s.SubStoreDesc) ?? code, 200)!; x.SiteId = siteFor(s.SiteCode).Id; x.LegacyRef = $"store:{code}";
        }
        var defaultStoreCode = m.Stores.Where(s => s.isDefault is not null and not 0).Select(s => K(s.SubStoreCode)).FirstOrDefault() ?? m.Stores.Select(s => K(s.SubStoreCode)).FirstOrDefault();
        Store StoreFor(string? code, string? siteCode)
        {
            if (!string.IsNullOrWhiteSpace(code) && stores.TryGetValue(K(code), out var s)) return s;
            if (!string.IsNullOrWhiteSpace(code))
            {   // referenced by the ledger but missing from the sub-store list
                s = new Store { TenantId = tenantId, Code = Trunc(K(code), 50)!, Name = K(code), SiteId = siteFor(siteCode).Id, LegacyRef = $"store:{K(code)}" };
                db.Stores.Add(s); stores[s.Code] = s; report.Created("Store"); report.Warn($"Store '{code}' is used by transactions but not defined in 4CSQL; created it.");
                return s;
            }
            if (defaultStoreCode is not null && stores.TryGetValue(defaultStoreCode, out var d)) return d;
            return StoreFor("MAIN", siteCode);
        }
        await db.SaveChangesAsync();

        // ---------- Part groups (attribute groups) ----------
        var groups = (await db.PartGroups.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync()).GroupBy(x => x.Name, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        var groupCodes = groups.Values.Select(g => g.Code).ToHashSet(ci);
        foreach (var name in m.Parts.Select(p => Clean(p.AttrGroup)).Where(n => n != null).Distinct(ci))
        {
            if (groups.ContainsKey(name!)) continue;
            var code = Trunc(new string(name!.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()), 20) is { Length: > 0 } c ? c : "GRP";
            for (var i = 2; groupCodes.Contains(code); i++) code = $"{Trunc(code, 17)}{i}";
            var g = new PartGroup { TenantId = tenantId, Code = code, Name = Trunc(name, 200)! };
            db.PartGroups.Add(g); groups[name] = g; groupCodes.Add(code); report.Created("Part group");
        }

        // ---------- Parts (stock codes) ----------
        var parts = (await db.Parts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync()).GroupBy(x => x.PartNumber, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        var preferred = m.PartSuppliers.GroupBy(x => K(x.Part), ci).ToDictionary(g => g.Key, g => K(g.First().Supplier), ci);
        foreach (var s in m.Parts.Where(s => !string.IsNullOrWhiteSpace(s.StockNo)))
        {
            var no = Trunc(K(s.StockNo), 100)!;
            if (!parts.TryGetValue(no, out var p)) { p = new Part { TenantId = tenantId, PartNumber = no, Name = "", UnitOfMeasure = "" }; db.Parts.Add(p); parts[no] = p; report.Created("Part"); }
            else report.Updated("Part");
            p.Name = Trunc(Clean(s.StockDescription) ?? no, 300)!; p.UnitOfMeasure = Trunc(Clean(s.UOM) ?? "EA", 30)!;
            p.GroupId = Clean(s.AttrGroup) is { } gname && groups.TryGetValue(gname, out var g) ? g.Id : null;
            p.Specification = Trunc(Clean(s.Specification), 4000); p.Manufacturer = Trunc(Clean(s.Manufacturer), 200); p.Barcode = Trunc(Clean(s.BarcodeId), 100);
            p.StandardPrice = Math.Max(0, Num(s.Price2, 4)); p.AverageCost = Num(s.AvgPrice, 4) is > 0 and var avg ? avg : p.StandardPrice;
            p.LeadTimeDays = s.LeadTime is > 0 and < 10000 ? s.LeadTime : null; p.IsCritical = s.Criticality is not null and not 0; p.IsShelfItem = s.ShelfItem is not null and not 0;
            p.AllowNegativeStock = s.allow_negative_value == true;
            var supplierNo = Clean(s.supplier_no) ?? preferred.GetValueOrDefault(no);
            p.PreferredSupplierId = supplierNo is not null && suppliers.TryGetValue(supplierNo, out var sup) ? sup.Id : null;
            p.Notes = Trunc(string.Join("\n", new[] { Clean(s.Notes), Clean(s.SAPNo) is { } sap ? $"SAP no.: {sap}" : null }.Where(v => v != null)), 4000) is { Length: > 0 } n ? n : null;
            p.IsActive = Clean(s.Status) is not { } st || st.StartsWith('A') || st.StartsWith('a'); // 4CSQL: 'A' = active
            p.LegacyRef = $"stock:{no}";
        }
        foreach (var grp in m.Alternates.GroupBy(a => K(a.Part), ci))
            if (parts.TryGetValue(grp.Key, out var p))
                p.AlternatePartIds = p.AlternatePartIds.Union(grp.Select(a => parts.GetValueOrDefault(K(a.Alternate))?.Id).OfType<Guid>().Where(id => id != p.Id)).ToList();
        await db.SaveChangesAsync();

        // ---------- Store levels ----------
        var levels = (await db.StockLevels.IgnoreQueryFilters().Where(l => l.TenantId == tenantId).ToListAsync()).ToDictionary(l => (l.PartId, l.StoreId));
        StockLevel Level(Part p, Store s)
        {
            if (!levels.TryGetValue((p.Id, s.Id), out var l)) { l = new StockLevel { TenantId = tenantId, PartId = p.Id, StoreId = s.Id }; db.StockLevels.Add(l); levels[(p.Id, s.Id)] = l; report.Created("Stock level"); }
            return l;
        }
        foreach (var l in m.Levels.Where(l => !string.IsNullOrWhiteSpace(l.stock_no)))
        {
            if (!parts.TryGetValue(K(l.stock_no), out var p)) { report.Warn($"Store level for unknown part '{l.stock_no}' skipped."); continue; }
            var lvl = Level(p, StoreFor(l.sub_store_code, null));
            lvl.MinLevel = Math.Max(0, Num(l.min_level)); lvl.ReorderLevel = Math.Max(0, Num(l.re_order_level)); lvl.MaxLevel = Math.Max(0, Num(l.max_level)); lvl.ReorderQuantity = Math.Max(0, Num(l.reorder_qty));
            lvl.LegacyRef = $"level:{K(l.stock_no)}:{K(l.sub_store_code)}";
        }
        // Parts without store levels get one in the default store, from the part-level min/max (older 4CSQL versions).
        var withLevels = m.Levels.Select(l => K(l.stock_no)).ToHashSet(ci);
        foreach (var s in m.Parts.Where(s => !string.IsNullOrWhiteSpace(s.StockNo) && !withLevels.Contains(K(s.StockNo))))
        {
            var p = parts[Trunc(K(s.StockNo), 100)!];
            var lvl = Level(p, StoreFor(null, s.SiteCode));
            lvl.MinLevel = Math.Max(0, Num(s.MinLevel)); lvl.MaxLevel = Math.Max(lvl.MinLevel, Num(s.MaxLevel)); lvl.ReorderLevel = lvl.MinLevel;
            lvl.Bin ??= Trunc(Clean(s.Location), 50);
        }
        await db.SaveChangesAsync();

        // ---------- Purchase orders (purchase requisitions) ----------
        var wos = await db.WorkOrders.IgnoreQueryFilters().Where(w => w.TenantId == tenantId && !w.IsDeleted).Select(w => new { w.Id, w.Number }).ToListAsync();
        var woByNo = wos.GroupBy(w => w.Number, ci).ToDictionary(g => g.Key, g => g.First().Id, ci);
        var pos = (await db.PurchaseOrders.IgnoreQueryFilters().Include(o => o.Lines).Where(o => o.TenantId == tenantId && !o.IsDeleted).ToListAsync());
        var poByRef = pos.Where(o => o.LegacyRef != null).GroupBy(o => o.LegacyRef!, ci).ToDictionary(g => g.Key, g => g.First(), ci);
        var poNumbers = pos.Select(o => o.Number).ToHashSet(ci);
        var poByLegacyNo = new Dictionary<string, PurchaseOrder>(ci);
        var lines = m.OrderLines.GroupBy(l => K(l.po_req_no), ci).ToDictionary(g => g.Key, g => g.ToList(), ci);
        foreach (var r in m.Orders.Where(r => !string.IsNullOrWhiteSpace(r.pr_no)))
        {
            var key = $"pr:{K(r.pr_no)}";
            if (Clean(r.supplier_no) is not { } sno || !suppliers.TryGetValue(sno, out var sup)) { report.Warn($"PR {r.pr_no}: supplier '{r.supplier_no}' not found, skipped."); continue; }
            if (!poByRef.TryGetValue(key, out var o))
            {
                var number = Trunc(Clean(r.po_no) ?? $"PR-{K(r.pr_no)}", 50)!;
                if (poNumbers.Contains(number)) number = Trunc($"{number}-{K(r.pr_no)}", 50)!;
                o = new PurchaseOrder { TenantId = tenantId, Number = number, LegacyRef = key }; db.PurchaseOrders.Add(o); poByRef[key] = o; poNumbers.Add(number); report.Created("Purchase order");
            }
            else report.Updated("Purchase order");
            o.SiteId = siteFor(r.sitecode).Id; o.SupplierId = sup.Id;
            o.OrderDate = Day(r.order_date) ?? Day(r.pr_date) ?? DateOnly.FromDateTime(DateTime.UtcNow); o.ExpectedDate = Day(r.exp_del_date);
            o.RequestedBy = Trunc(Clean(r.req_by) ?? Clean(r.order_by), 200); o.DeliverTo = Trunc(Clean(r.del_to), 1000); o.SupplierQuoteRef = Trunc(Clean(r.quote_ref), 100);
            o.PaymentTerms = r.pay_term_id is { } pt ? Trunc(m.PaymentTerms.GetValueOrDefault(pt), 100) : null;
            o.ShippingMethod = r.ship_id is { } sh ? Trunc(m.ShipVia.GetValueOrDefault((int)sh), 100) : null;
            var cur = r.currency_id is { } cid ? m.Currencies.GetValueOrDefault((int)cid) : null;
            o.Currency = cur is { Length: 3 } ? cur.ToUpperInvariant() : baseCurrency;
            o.ExchangeRate = Num(r.conv_rate, 6) is > 0 and var rate ? rate : 1; // 4CSQL: international → local currency
            o.TaxPercent = Math.Clamp(Num(r.gst_percent) + Num(r.TaxRate), 0, 100);
            o.ShippingCost = Math.Max(0, Math.Round((r.delivery_charges ?? 0) + Num(r.freight, 2) + Num(r.MiscValue, 2), 2));
            o.Notes = Trunc(Clean(r.order_notes), 4000); o.CreatedAt = At(r.pr_date ?? r.order_date);
            // Status: delivery status first, then authorisation (1 not authorised, 2 authorised, 3 cancelled, 4 partly authorised, 5 verified).
            o.Status = r.cancel_date is not null || r.status_id == 4 || r.auth_status == 3 ? PurchaseOrderStatus.Cancelled
                : r.status_id == 3 ? PurchaseOrderStatus.Received
                : r.status_id == 2 ? PurchaseOrderStatus.PartiallyReceived
                : r.auth_status == 2 ? PurchaseOrderStatus.Approved
                : r.auth_status is 4 or 5 || Clean(r.po_no) is not null ? PurchaseOrderStatus.Submitted : PurchaseOrderStatus.Draft;
            if (o.Status == PurchaseOrderStatus.Cancelled) { o.CancelledAt = At(r.cancel_date ?? r.order_date); o.CancelReason = Trunc(Clean(r.cancel_notes) ?? "Cancelled in 4CSQL", 1000); }
            if (o.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received) { o.ApprovedAt ??= At(r.order_date ?? r.pr_date); o.ApprovedByName ??= Trunc(Clean(r.order_by), 200); }
            if (Clean(r.po_no) is { } legacyPo) poByLegacyNo.TryAdd(legacyPo, o);
            poByLegacyNo.TryAdd(K(r.pr_no), o);
            var lump = Num(r.LumpDiscount);
            var n = 0;
            foreach (var d in lines.GetValueOrDefault(K(r.pr_no)) ?? [])
            {
                n++;
                var part = Clean(d.part_no) is { } pn && parts.TryGetValue(pn, out var pp) ? pp : null;
                var line = o.Lines.FirstOrDefault(l => l.LineNumber == n && db.Entry(l).State != EntityState.Added);
                if (line is null) { line = new PurchaseOrderLine { Description = "" }; o.Lines.Add(line); report.Created("PO line"); }
                var disc = Math.Clamp(d.disc ?? 0, 0, 100);
                line.LineNumber = n; line.PartId = part?.Id; line.Description = Trunc(Clean(d.part_desc) ?? part?.Name ?? Clean(d.part_no) ?? "Item", 500)!;
                line.UnitOfMeasure = Trunc(Clean(d.uom) ?? part?.UnitOfMeasure, 30); line.Quantity = Math.Max(0, d.qty ?? 0); line.UnitPrice = Math.Max(0, d.unit_price ?? 0);
                line.DiscountPercent = Math.Round(100 - (100 - disc) * (100 - lump) / 100, 4);
                line.QuantityReceived = Math.Max(0, d.received_qty ?? 0);
                line.StoreId = part is null ? null : StoreFor(d.SubStoreCode, r.sitecode).Id;
                line.WorkOrderId = Clean(d.wo_no) is { } wn && woByNo.TryGetValue(wn, out var wid) ? wid : null;
            }
        }
        await db.SaveChangesAsync();

        // ---------- Stock ledger ----------
        var done = (await db.StockTransactions.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && t.LegacyRef != null).Select(t => t.LegacyRef!).ToListAsync()).ToHashSet(ci);
        var pending = new List<StockTransaction>();
        void Post(string key, string? stockNo, StockTransactionType type, decimal qty, decimal cost, DateTime? date, string? storeCode, string? siteCode, Action<StockTransaction>? more = null)
        {
            if (done.Contains(key)) { report.Updated("Stock movement"); return; }
            if (string.IsNullOrWhiteSpace(stockNo) || !parts.TryGetValue(K(stockNo), out var part)) { report.Warn($"{key}: unknown part '{stockNo}', skipped."); return; }
            if (qty == 0) return;
            var store = StoreFor(storeCode, siteCode);
            Level(part, store);
            var t = new StockTransaction { TenantId = tenantId, Type = type, PartId = part.Id, StoreId = store.Id, Quantity = qty, UnitCost = Math.Max(0, cost), Date = Day(date) ?? DateOnly.FromDateTime(DateTime.UtcNow),
                CreatedAt = At(date), CreatedByName = "4CSQL import", LegacyRef = key };
            more?.Invoke(t);
            pending.Add(t); done.Add(key); report.Created("Stock movement");
        }
        Guid? Wo(string? no) => Clean(no) is { } n && woByNo.TryGetValue(n, out var id) ? id : null;
        foreach (var r in m.Receipts)
            Post($"rcv:{r.ReceiveID}", r.Stockcode, r.Transfer is not null and not 0 ? StockTransactionType.TransferIn : StockTransactionType.Receipt, Num(r.QtyRec), Num(r.Price1, 4), r.Date, r.SubStoreCode, r.SiteCode, t =>
            {
                var po = Clean(r.PONo) is { } pn && poByLegacyNo.TryGetValue(pn, out var o) ? o : null;
                t.PurchaseOrderId = po?.Id; t.SupplierId = po?.SupplierId ?? (Clean(r.Vendor) is { } v && suppliers.TryGetValue(v, out var s) ? s.Id : suppliers.Values.FirstOrDefault(x => string.Equals(x.Name, Clean(r.Vendor), StringComparison.OrdinalIgnoreCase))?.Id);
                t.PurchaseOrderLineId = po?.Lines.FirstOrDefault(l => l.PartId == t.PartId)?.Id;
                t.Reference = Trunc(Clean(r.MRNNo) ?? Clean(r.PONo), 100); t.BatchNumber = Trunc(Clean(r.BatchNo), 100); t.ExpiryDate = Day(r.BatchExpiryDate);
                t.Notes = Trunc(string.Join(" · ", new[] { Clean(r.Comments), r.Transfer is not null and not 0 && Clean(r.FromSubStore) is { } f ? $"from {f}" : null }.Where(x => x != null)), 1000) is { Length: > 0 } nn ? nn : null;
            });
        foreach (var r in m.Issues)
            Post($"iss:{r.IssueID}", r.StockNo, r.Transfer is not null and not 0 ? StockTransactionType.TransferOut : StockTransactionType.Issue, -Num(r.QtyIssue), Num(r.Price3, 4), r.DateIssue, r.SubStoreCode, r.SiteCode, t =>
            {
                t.WorkOrderId = r.Transfer is not null and not 0 ? null : Wo(r.WONo); t.Reference = Trunc(Clean(r.IWTNo), 100); t.BatchNumber = Trunc(Clean(r.BatchNo), 100);
                t.Notes = Trunc(string.Join(" · ", new[] { Clean(r.Comments), t.WorkOrderId is null && Clean(r.WONo) is { } wn ? $"WO {wn}" : null, r.Transfer is not null and not 0 && Clean(r.ToSubStoreCode) is { } to ? $"to {to}" : null }.Where(x => x != null)), 1000) is { Length: > 0 } nn ? nn : null;
                t.CreatedByName = Trunc(Clean(r.CreatedByEmpNo), 200) ?? t.CreatedByName;
            });
        foreach (var r in m.Returns)
            Post($"ret:{r.ReturnID}", r.StockNo, StockTransactionType.Return, Num(r.Qty), Num(r.RetPrice, 4), r.Date, r.SubStoreCode, r.SiteCode, t =>
            {
                t.WorkOrderId = Wo(r.WONo); t.Reference = Trunc(Clean(r.IWTNo), 100); t.BatchNumber = Trunc(Clean(r.BatchNo), 100); t.Notes = Trunc(Clean(r.Comment), 1000);
                t.CreatedByName = Trunc(Clean(r.Person), 200) ?? t.CreatedByName;
            });
        foreach (var r in m.Adjustments)
            Post($"adj:{r.AdjustmentID}", r.StockNo, StockTransactionType.Adjustment, Math.Round(r.QtyAdj ?? 0, 3), Num(r.Price4, 4), r.Date, r.SubStoreCode, r.SiteCode, t =>
            {
                t.BatchNumber = Trunc(Clean(r.BatchNo), 100); t.Notes = Trunc(Clean(r.Comment) ?? "Adjustment", 1000); t.CreatedByName = Trunc(Clean(r.Person), 200) ?? t.CreatedByName;
            });
        // Parts with a legacy balance but no history (purged ledgers) get an opening balance so stock on hand matches 4CSQL.
        var withHistory = pending.Select(t => t.PartId).Concat(await db.StockTransactions.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.PartId).Distinct().ToListAsync()).ToHashSet();
        foreach (var s in m.Parts.Where(s => !string.IsNullOrWhiteSpace(s.StockNo) && Num(s.StockBalance) != 0))
        {
            var part = parts[Trunc(K(s.StockNo), 100)!];
            if (withHistory.Contains(part.Id)) continue;
            var store = levels.Values.FirstOrDefault(l => l.PartId == part.Id) is { } l ? stores.Values.First(x => x.Id == l.StoreId) : StoreFor(null, s.SiteCode);
            Post($"open:{K(s.StockNo)}", s.StockNo, StockTransactionType.Adjustment, Num(s.StockBalance), part.AverageCost, DateTime.UtcNow, store.Code, s.SiteCode, t => t.Notes = "Opening balance from 4CSQL");
        }
        foreach (var chunk in pending.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).Chunk(5000))
        {
            db.StockTransactions.AddRange(chunk);
            await db.SaveChangesAsync();
            foreach (var t in chunk) db.Entry(t).State = EntityState.Detached;
        }

        // ---------- Balances and costs ----------
        var balances = await db.StockTransactions.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).GroupBy(t => new { t.PartId, t.StoreId })
            .Select(g => new { g.Key.PartId, g.Key.StoreId, Qty = g.Sum(t => t.Quantity) }).ToListAsync();
        foreach (var b in balances)
            if (levels.TryGetValue((b.PartId, b.StoreId), out var l)) l.QuantityOnHand = b.Qty;
        var lastPrice = await db.StockTransactions.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && t.Type == StockTransactionType.Receipt && t.UnitCost > 0)
            .GroupBy(t => t.PartId).Select(g => new { g.Key, Price = g.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).Select(t => t.UnitCost).First() }).ToListAsync();
        foreach (var lp in lastPrice) if (parts.Values.FirstOrDefault(p => p.Id == lp.Key) is { } p) p.LastPurchasePrice = lp.Price;
        await db.SaveChangesAsync();
        var totals = balances.GroupBy(b => b.PartId).ToDictionary(g => g.Key, g => g.Sum(b => b.Qty));
        var mismatched = m.Parts.Where(s => !string.IsNullOrWhiteSpace(s.StockNo)).Select(s => (s.StockNo, Legacy: Num(s.StockBalance), Now: totals.GetValueOrDefault(parts[Trunc(K(s.StockNo), 100)!].Id)))
            .Where(x => Math.Abs(x.Legacy - x.Now) > 0.001m).ToList();
        foreach (var x in mismatched.Take(20)) report.Warn($"Part {x.StockNo}: 4CSQL balance {x.Legacy:0.###} but its transactions add up to {x.Now:0.###}.");
        if (mismatched.Count > 20) report.Warn($"… {mismatched.Count - 20} more parts whose 4CSQL balance differs from their transaction history.");
    }
}
