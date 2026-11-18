# Migrating from 4CSQL

The `FourC.Cmms.LegacyImport` tool copies a customer's 4CSQL database into an organization in the cloud edition.
It works with 4CSQL 6.6x – 6.108 schemas (it reads with `SELECT *` where it can, and optional tables and columns may be
missing). It has been run end to end against three customer databases on **4CSQL 6.108.1.1** — see *Results* below.

## Steps

1. Restore the customer's `.bak` on any SQL Server 2019+ (SQL Server upgrades 2008 R2 – 2014 backups automatically).
   If `RESTORE` reports the media as incomplete/incorrectly formed, the file was copied before the backup finished;
   ask the customer for a fresh copy (or see `repair_bak_trailer.py` in the analysis notes for the trailer-only case).
2. Run a dry run into an existing organization (everything is rolled back):
   ```bash
   dotnet run --project server/FourC.Cmms.LegacyImport -- \
     --source "Server=...;Database=CustomerDb;User Id=...;Password=...;TrustServerCertificate=True" \
     --target "<cloud connection string>" --tenant <organization-slug> --dry-run
   ```
3. Run for real — either into an existing organization (`--tenant`) or create one:
   `--create-tenant "Customer name" --admin-email admin@customer.com --admin-password '...'`.
4. Read the report: created/updated counts per record type and the warnings (see *Warnings worth reading*).

Connection strings and passwords can be passed as environment variables (`CMMS_IMPORT_SOURCE`, `CMMS_IMPORT_TARGET`,
`CMMS_IMPORT_ADMIN_PASSWORD`) to keep them out of shell history.

**Re-running.** After a run that *completed*, re-running into the same organization is safe: records are matched by
their legacy keys and updated, never duplicated. A run that *failed part-way* is different: assignees, labour, costs,
notes and status history are only added for work orders created in the same run, so re-running would leave the work
orders saved before the failure without them. Delete that organization and import into a new one instead.

### Running it with Docker (no .NET on the machine)

With the local stack from the README running (`docker compose up`), restore and import entirely in containers:

```bash
# 1. Copy the backup into the SQL Server container, then RESTORE DATABASE ... WITH MOVE for each file
#    (RESTORE FILELISTONLY FROM DISK = '/var/opt/mssql/backup/Customer.bak' lists the logical file names)
docker cp Customer.bak cmmsx-sql-1:/var/opt/mssql/backup/Customer.bak

# 2. Build the importer once
docker run --rm -v "$PWD/server":/repo:ro -v cmmsx-importer:/out mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'cp -r /repo /src && dotnet publish /src/FourC.Cmms.LegacyImport -c Release -o /out'

# 3. Import (CMMS_IMPORT_SOURCE / _TARGET / _ADMIN_PASSWORD set in the shell)
docker run --rm --init --memory 4g --network cmmsx_default -v cmmsx-importer:/out \
  -e CMMS_IMPORT_SOURCE -e CMMS_IMPORT_TARGET -e CMMS_IMPORT_ADMIN_PASSWORD \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet /out/FourC.Cmms.LegacyImport.dll --create-tenant "Customer" --admin-email admin@customer.com
```

- `--init` matters: without it the .NET process is PID 1 and, if it crashes, it spins at 100 % CPU instead of exiting.
- `--memory 4g` keeps a large import from starving SQL Server. The importer saves in batches and streams the PM step
  table, so MEG (290,000 work orders, 3.7 million step rows) fits in 4 GB. Keep SQL Server capped too
  (`SQL_MEMORY_MB` in docker-compose, default 3 GB): uncapped, it is the process the kernel kills when memory runs out.

## Mapping

| 4CSQL | Cloud edition |
|---|---|
| `site` | Site (code, name) |
| `department` (shown as Department / Section / Sub-site) | Top-level **Area** location under its site. 4CSQL lets a section and a location share a code (4 in MEG, e.g. `MGM_GAR`); the cloud edition needs codes unique per site, so the location gets a suffix (`MGM_GAR-2`) and a warning |
| `location` | Location under its department area (or the site root) |
| `assetcategories` | Asset category |
| `assets` | Asset — number, description (first line → name), site/location, category, status, criticality, model, serial, manufacturer, Op No, dates, prices, estimated life, downtime cost, warranty/contract, notes |
| `assets.NameplateInfo1..30` | Custom fields (only those with data). Labels come from the customer's own captions (`ObjectCaptions`, e.g. "Additional Info 1" renamed "Rated Power") |
| `sibling` (AssetNo = parent, Sibling = child) | Asset parent/component link (cycles are skipped and reported) |
| `assetstransfer` | Transfer history (from/to reconstructed in date order); transfers of assets deleted in 4CSQL are skipped and counted in one warning |
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
| `WOStatusAudit` | Status history (who and when). Consecutive 4CSQL statuses that map to the same cloud status are folded into one change |
| `pmchecklist` + `checklistelements` | PM tasks with ordered sub-tasks |
| `pmschedule` | PM schedules. `TypePMgen` 1/2 → fixed interval by scheduled/actual date; 3 → nth weekday (`WeekofMonth` 0–4 → 1st–last, `DayofWeek` VB 1 = Sunday); 4 → fixed date; 5 → fixed date with whole-month work period. `TypePM` 1 → the asset, 2 → the location, **3 → the section** (`SectionNo`, i.e. the department's Area location; a `PhyLocationNo`, when also set, wins). Interval = `FreqUnits` × `Frequency` days (weeks and years kept as such; 4CSQL "months" of 28 days are kept as exact days). `PeriodDays` → work period, `NextDate` → next due, `PMCounter` → generation count |
| `pmschedule.IsMultipleAsset` + `PMScheduleAssets` | **PM round** (multi-asset PM): one schedule covering the listed assets in `SeqNo` order; each cycle generates one work order with the task's steps repeated per asset. A list spanning several sites is split into one round per site, coded `<PM no>-<site>` (4 in MEG), and each historical work order links to the part for its site. Leftover `PMScheduleAssets` rows on schedules without the flag are ignored |
| `multiplepm` | Multiple-PM rules (task + every Nth) |
| `pmmeters` | A meter on the asset **and** a meter-based schedule (incremental: interval/next due; threshold: limits with lower/upper tasks; time-based → whichever comes first) |
| `readings` | Meter reading history; the latest becomes the current reading |
| `workorders.PMNo` / `TaskNo` / `PMTarStartDate` | Link from historical work orders to their PM schedule |
| `workorderelements` | PM checklist steps recorded on work orders (per asset on rounds). **Step results** are imported: steps with a reading, finding or remark (`readings` → Reading; `findings` and `Remarks` → Remarks, joined with " · "), plus every step of work orders that are still open so they can be finished after cut-over; a step counts as done when its work order is completed. The rest is only a copy of the PM task and is not copied. For round work orders, the assets their steps were recorded for become the work order's asset list (else the schedule's list) |
| `suppliers` | Suppliers (code, name, contact, address, phone, e-mail, active; services, account no. and fax go to notes) |
| `substore` | Stores (code, description, site); the `isDefault` store receives records with no store |
| `stockcode` | Parts — number, description, unit, attribute group → part group, standard price (`Price2`), average cost (`AvgPrice`), lead time, critical, shelf item, allow negative balance, manufacturer, barcode, specification, preferred supplier; `Status` other than `A` → inactive; SAP no. → notes |
| `stockcode_levels` | Store levels (min, re-order level, max, re-order qty); parts without store levels get one in the default store from the part's min/max |
| `stock_alternative`, `stock_code_supplier` | Alternate parts; preferred supplier when the part has none |
| `receive` / `issue` / `return` / `adjustment` | The stock ledger, with dates, prices, references (MRN / IWT), batches and expiry. `Transfer` rows become transfer out / transfer in between stores. Issues and returns are linked to their work orders. Balances per store are recomputed from the ledger; a part with a 4CSQL balance but no history gets an opening-balance adjustment |
| `purchase_req_master` + `purchase_req_details` | Purchase orders (PO number, or `PR-<no>` when no PO was raised). Status: delivery status 3/2 → Received / Partly received, cancelled (status 4, authorisation 3 or a cancel date) → Cancelled, authorised → Approved, verified / partly authorised / numbered → Awaiting approval, else Draft. Currency from `currency_rate`, `conv_rate` → exchange rate (international → local, as in the User's Guide), GST % + Tax % → tax, freight + delivery + misc → shipping, lump discount folded into each line's discount; lines keep part, store, work order and received quantity |

## Results (2026-11-18, local Docker, 4CSQL 6.108.1.1)

| | EGA | KandilGlass | MEG |
|---|---|---|---|
| Backup | 239 MB | 301 MB | 2.6 GB |
| Run time | 65 s | 56 s | 13 min |
| Sites · areas · locations · assets | 3 · 8 · 269 · 1,409 | 7 · 48 · 59 · 1,592 | 4 · 72 · 169 · 1,675 |
| Work orders · labour · status changes | 37,950 · 26,403 · 76,434 | 29,536 · 1,673 · 70,876 | 290,225 · 324,755 · 651,362 |
| Work requests | 4,367 | 7,555 | 42,053 |
| PM schedules (of which rounds) | 1,089 (1) | 146 (9) | 2,352 (298, 4 split by site) |
| Round work orders with their assets | 4 | 130 | 86,338 |
| Step results | 1,547 | 14,774 | 367,127 |
| Parts · stock movements · suppliers | — | 1,869 · 0 · 0 | 24,368 · 37,742 · 229 |
| Purchase orders | 0 | 0 | 0 |

Arabic text (EGA uses the Arabic code page 1256; MEG stores Arabic in Unicode columns) comes across intact.
None of the three databases has purchase orders, so that part of the importer is still verified only against a
synthetic 4CSQL database (transfers, returns, shelf-life batches, foreign currency, cancelled and partly received
orders, unknown parts and work orders).

### Warnings worth reading

- *Location X: code already used in site S; imported as X-2* — a section and a location shared a code.
- *PM X: its assets are in N sites; imported as …* — a round was split per site.
- *Part X: 4CSQL balance A but its transactions add up to B* — the stored balance and the ledger disagree. **MEG:**
  `stockcode.StockBalance` is 0 for all but 492 of 24,370 parts, while the receive/issue/return/adjustment history leaves
  stock on 18,518 of them. The importer keeps the ledger's result. Confirm with the customer which figure is right
  before cut-over (e.g. open part 1202678 in 4CSQL: its history adds up to 102).
- *Legacy query skipped, treated as empty: …* — an optional table or column is missing in this 4CSQL version, or a
  query failed (a timeout would show here too). Check it is expected.

## Not yet imported

User logins (invite users afresh), PM materials, attachments (`ExtFiles`: 60,741 rows in MEG), purchase-order receiving
log (`Trans`; receipts come from `receive`), cost centres, per-step acceptable limits (`workorderelements.aLimits`), and
the legacy report definitions.
