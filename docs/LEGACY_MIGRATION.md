# Migrating from 4CSQL

The `FourC.Cmms.LegacyImport` tool copies a customer's 4CSQL database into an organization in the cloud edition.
It works with 4CSQL 6.6x – 6.9x schemas (it reads with `SELECT *`, so missing/extra columns are tolerated).

## Steps

1. Restore the customer's `.bak` on any SQL Server 2019+ (SQL Server upgrades 2008 R2 backups automatically).
   If `RESTORE` reports the media as incomplete/incorrectly formed, the file was copied before the backup finished;
   ask the customer for a fresh copy (or see `repair_bak_trailer.py` in the analysis notes for the trailer-only case).
2. Run a dry run (everything is rolled back):
   ```bash
   dotnet run --project server/FourC.Cmms.LegacyImport -- \
     --source "Server=...;Database=CustomerDb;User Id=...;Password=...;TrustServerCertificate=True" \
     --target "<cloud connection string>" --tenant <organization-slug> --dry-run
   ```
3. Run for real — either into an existing organization (`--tenant`) or create one:
   `--create-tenant "Customer name" --admin-email admin@customer.com --admin-password '...'`.
4. Re-running is safe: records are matched by their legacy keys and updated, never duplicated.

Connection strings and passwords can be passed as environment variables (`CMMS_IMPORT_SOURCE`, `CMMS_IMPORT_TARGET`,
`CMMS_IMPORT_ADMIN_PASSWORD`) to keep them out of shell history.

## Mapping

| 4CSQL | Cloud edition |
|---|---|
| `site` | Site (code, name) |
| `department` (shown as Department / Section / Sub-site) | Top-level **Area** location under its site |
| `location` | Location under its department area (or the site root) |
| `assetcategories` | Asset category |
| `assets` | Asset — number, description (first line → name), site/location, category, status, criticality, model, serial, manufacturer, Op No, dates, prices, estimated life, downtime cost, warranty/contract, notes |
| `assets.NameplateInfo1..30` | Custom fields (only those with data). Labels come from the customer's own captions (`ObjectCaptions`, e.g. "Additional Info 1" renamed "Rated Power") |
| `sibling` (AssetNo = parent, Sibling = child) | Asset parent/component link (cycles are skipped and reported) |
| `assetstransfer` | Transfer history (from/to reconstructed in date order) |
| `assetstatus` | Active, Inactive, In storage, Under repair (Sent for repair), Disposed |
| `relable` Site/Location/Asset | Organization terminology |
| `worktrade` | Trades |
| `failurecause` | Failure codes |
| `employees` | People (workforce records: number, name, position, contact, trade, site, hourly/overtime rate, contractor flag). Logins and passwords are **not** migrated — invite users afresh. |
| `worequest` | Work requests (status: Open → New, WO raised/Closed → Approved and linked to the work order, Cancelled) |
| `workorders` | Work orders — number kept as-is; first line of the problem description becomes the title; type/priority/status mapped by their descriptions (Waiting for parts/approval/access → On hold with that reason, Acknowledged → In progress, WO Raised → Open); received/required/actual dates, action taken, cause, prevention, failure code, downtime, requester |
| `assigntoworkorder` | Assignees |
| `workorderlabor` | Labour (normal + OT1–OT4 hours; the rate is derived from the legacy total cost so historical costs are preserved) |
| `direct` | Other costs (description, qty, price, supplier, PR/invoice reference) |
| `jobtask` | Work order tasks |
| `workorders.Notes` | A comment on the work order |
| `WOStatusAudit` | Status history (who and when) |
| `pmchecklist` + `checklistelements` | PM tasks with ordered sub-tasks |
| `pmschedule` | PM schedules. `TypePMgen` 1/2 → fixed interval by scheduled/actual date; 3 → nth weekday (`WeekofMonth` 0–4 → 1st–last, `DayofWeek` VB 1 = Sunday); 4 → fixed date; 5 → fixed date with whole-month work period. `TypePM` 1/2 → asset/location. Interval = `FreqUnits` × `Frequency` days (weeks and years kept as such; 4CSQL "months" of 28 days are kept as exact days). `PeriodDays` → work period, `NextDate` → next due, `PMCounter` → generation count |
| `multiplepm` | Multiple-PM rules (task + every Nth) |
| `pmmeters` | A meter on the asset **and** a meter-based schedule (incremental: interval/next due; threshold: limits with lower/upper tasks; time-based → whichever comes first) |
| `readings` | Meter reading history; the latest becomes the current reading |
| `workorders.PMNo` / `TaskNo` / `PMTarStartDate` | Link from historical work orders to their PM schedule |
| `suppliers` | Suppliers (code, name, contact, address, phone, e-mail, active; services, account no. and fax go to notes) |
| `substore` | Stores (code, description, site); the `isDefault` store receives records with no store |
| `stockcode` | Parts — number, description, unit, attribute group → part group, standard price (`Price2`), average cost (`AvgPrice`), lead time, critical, shelf item, allow negative balance, manufacturer, barcode, specification, preferred supplier; `Status` other than `A` → inactive; SAP no. → notes |
| `stockcode_levels` | Store levels (min, re-order level, max, re-order qty); parts without store levels get one in the default store from the part's min/max |
| `stock_alternative`, `stock_code_supplier` | Alternate parts; preferred supplier when the part has none |
| `receive` / `issue` / `return` / `adjustment` | The stock ledger, with dates, prices, references (MRN / IWT), batches and expiry. `Transfer` rows become transfer out / transfer in between stores. Issues and returns are linked to their work orders. Balances per store are recomputed from the ledger; a part with a 4CSQL balance but no history gets an opening-balance adjustment |
| `purchase_req_master` + `purchase_req_details` | Purchase orders (PO number, or `PR-<no>` when no PO was raised). Status: delivery status 3/2 → Received / Partly received, cancelled (status 4, authorisation 3 or a cancel date) → Cancelled, authorised → Approved, verified / partly authorised / numbered → Awaiting approval, else Draft. Currency from `currency_rate`, `conv_rate` → exchange rate (international → local, as in the User's Guide), GST % + Tax % → tax, freight + delivery + misc → shipping, lump discount folded into each line's discount; lines keep part, store, work order and received quantity |

The import prints created/updated counts per record type and warnings (unknown locations, missing assets, cycles,
ledger rows for unknown parts, and parts whose 4CSQL balance differs from the sum of their transactions).

Note: the two customer databases supplied so far have no materials or purchasing data, so that part of the importer was
verified against a synthetic 4CSQL database covering every case above (transfers, returns, shelf-life batches, foreign
currency, cancelled and partly received orders, unknown parts and work orders).

## Not yet imported

User logins (invite users afresh), PM materials, attachments (`ExtFiles`), purchase-order receiving log (`Trans`; receipts
come from `receive`), cost centres, and the legacy report definitions.
