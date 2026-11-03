# 4C CMMS — cloud edition

The web-based, multi-tenant successor to **4CSQL** (4C Systems LLC's Windows CMMS).
One cloud service shared by all customers; each customer is an isolated *organization* (tenant).

| Layer | Technology |
|---|---|
| API | ASP.NET Core 10 (minimal APIs), EF Core 10, SQL Server / Azure SQL |
| Web | React 19, TypeScript, Vite, Mantine 8, TanStack Query, i18next |
| Auth | ASP.NET Identity (secure cookie), tenant-scoped roles & permissions |
| Ops | Docker image (API serves the web app), GitHub Actions CI |

## Screenshots

| Dashboard | Locations |
|---|---|
| ![Dashboard](docs/screenshots/dashboard.png) | ![Locations](docs/screenshots/locations.png) |
| **Assets** | **Asset detail** |
| ![Assets](docs/screenshots/assets.png) | ![Asset detail](docs/screenshots/asset-detail.png) |
| **Work orders** | **Work order** |
| ![Work orders](docs/screenshots/work-orders.png) | ![Work order](docs/screenshots/work-order.png) |
| **PM schedules** | **PM generation** |
| ![PM schedules](docs/screenshots/pm-schedules.png) | ![PM generation](docs/screenshots/pm-generate.png) |

## Modules

| Module | Status |
|---|---|
| Platform: organizations, users, roles/permissions, audit trail, files, settings, terminology | ✅ |
| **Locations**: sites → unlimited-depth location tree (areas, buildings, floors, rooms, lines) | ✅ |
| **Assets**: hierarchy (components), categories, custom fields, warranty, financials, transfers, photos/documents, bulk edit, CSV export | ✅ |
| **People**: employees/contractors with trades, rates and sites (optionally linked to a login); trades; failure codes | ✅ |
| **Work requests**: anyone can request work; triage → approve (creates a work order) / reject; duplicate & warranty check | ✅ |
| **Work orders**: workflow (Open → In progress ⇄ On hold → Completed → Closed), assignees, task checklist, labour & other costs, completion details (action/cause/prevention, failure code, downtime), activity feed, files | ✅ |
| **Preventive maintenance**: PM tasks (checklists) and schedules on assets or locations — fixed interval (by scheduled or actual date), nth weekday of month, fixed date of month, meter-based (incremental or threshold, optionally whichever-comes-first with time); multiple PM (heavier task every Nth time); preview & generate, automatic generation with lead time, forecast; meters and readings | ✅ |
| **4CSQL importer** (locations, assets, people, trades, failure codes, work requests, work orders with labour, costs, tasks, notes and status history, PM tasks, schedules, meters and readings) | ✅ |
| Inventory → Purchasing → Reports | Next — see [docs/ROADMAP.md](docs/ROADMAP.md) |

## Run locally

Prerequisites: .NET 10 SDK, Node 22, a SQL Server (the compose file starts one).

```bash
# 1. Database
SA_PASSWORD='YourStr0ng!Pass' docker compose up -d sql

# 2. API (applies migrations on start) — http://127.0.0.1:5080
export ConnectionStrings__Cmms="Server=localhost,1433;Database=Cmms;User Id=sa;Password=YourStr0ng!Pass;TrustServerCertificate=True"
dotnet run --project server/FourC.Cmms.Api --urls http://127.0.0.1:5080

# 3. Web (hot reload, proxies /api) — http://localhost:5173
cd web && npm install && npm run dev
```

Open the app, choose **Create an organization**, then *Settings → Organization → Load sample data* to explore with ~230 demo assets.
API reference: `http://127.0.0.1:5080/api/docs`.

Full stack in containers: `SA_PASSWORD='YourStr0ng!Pass' docker compose up --build` → http://localhost:8080

## Tests

```bash
export CMMS_TEST_SQL="Server=localhost,1433;User Id=sa;Password=YourStr0ng!Pass;TrustServerCertificate=True"
dotnet test server/FourC.Cmms.sln      # unit tests + integration tests (they create and drop their own database)
cd web && npm run build                 # type-check + production build
```

## Migrating a 4CSQL customer

See [docs/LEGACY_MIGRATION.md](docs/LEGACY_MIGRATION.md).

## Project layout

```
server/
  FourC.Cmms.Core/          domain model, EF Core DbContext + migrations, tenant provisioning, location tree
  FourC.Cmms.Api/           HTTP API (Features/*Endpoints.cs), auth, permissions, file storage
  FourC.Cmms.LegacyImport/  4CSQL → cloud importer (console)
  FourC.Cmms.Tests/         API integration tests
web/src/
  pages/                    dashboard, locations, assets, settings
  components/               layout, pickers, attachments, audit timeline
  locales/en.json           all UI text (add ar.json etc. for more languages)
```

## Design notes

- **Tenant isolation**: every business table has `TenantId`; EF Core global query filters scope all reads, and
  `SaveChanges` stamps/guards writes (cross-tenant writes throw). Integration tests verify isolation.
- **Audit**: every create/update/delete on business records is recorded with field-level before/after values.
- **Concurrency**: records carry a `RowVersion`; stale edits return HTTP 409 instead of silently overwriting.
- **Hierarchy**: locations store a materialized path, so "all assets under Building A" is a single indexed query.
- **Customer-specific needs** are handled with configuration, not code forks: terminology (rename Site/Location/Asset),
  custom fields per asset category, and roles — replacing 4CSQL's 30 `NameplateInfo` columns and 300+ per-client feature flags.
