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

The import prints created/updated counts per record type and warnings (unknown locations, missing assets, cycles).

## Not yet imported (arrives with each module)

Employees/users, work requests, work orders, PM schedules & meters, checklists, stock & stores, suppliers,
purchase orders, attachments (`ExtFiles`), and reports.
