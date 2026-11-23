# Cost centres and budgets (0.20.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A cost-centre tree set on locations and assets and given to all work, budgets per cost centre per month, and a report
of budget against committed and actual cost.

**Architecture:** `CostCentreId` sits on the work order (and on direct issues); every cost of a work order follows it, and
purchase commitments follow the work order of their line. One resolver gives the cost centre in force for an asset or location;
the single work-order create routine applies it. The budget report reuses the Maintenance costs report's cost lines.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), React 19 + Mantine 8 + TanStack Query + i18next, xUnit
(`SkippableFact`, `ApiFixture`), Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-cost-centres-0.20.0-design.md`

## Global Constraints

- Cost centre on the work order: labour, parts, contractors, other costs and purchase commitments of a work order follow its
  cost centre; direct issues and returns to a location carry their own.
- The Maintenance costs report's totals never change; cost centres only filter and group it.
- Changing an asset's or location's cost centre never changes existing work or issues.
- Budget = one amount per cost centre per month, in the organization's currency.
- No new permissions: cost centres, budgets and "Fill in past work" with *Manage settings*; budgets and the budget report seen
  with *View reports*; changing a work order's cost centre with *Edit work orders*; a direct issue's with *Manage inventory*.
- Every new text in English and Arabic (server `Localization/ar.json` via `ar-add.py`; web locales via `loc-add.py`).
- Gate with CI's exact commands (`wt-ci`: Release build of every project, then tests).

## Review Focus

1. **Actual by cost centre (with "No cost centre") equals the Maintenance costs report** for the same period. Test: `Actual_on_work_orders_matches_maintenance_costs` (Task 5).
2. **The cost centre in force through both trees, given to every work order however made.** Test: `Every_way_of_creating_work_gets_the_cost_centre` (Task 2).
3. **Fill in past work never overwrites, is repeatable, counts first.** Test: `Fill_in_never_overwrites_counts_first_and_repeats` (Task 3).
4. **Committed: partly received, foreign currency, draft/cancelled left out, moves with the work order.** Test: `Committed_is_the_open_part_of_approved_work_order_lines` (Task 5).
5. **A site-limited person sees and picks only their sites' and shared cost centres.** Test: `Site_limited_people_see_their_sites_and_shared_cost_centres` (Task 1).

---

### Task 1: Cost-centre tree (data, migration, API)

**Files:**
- Create: `server/FourC.Cmms.Core/Costs/CostCentres.cs`, `server/FourC.Cmms.Api/Features/CostCentreEndpoints.cs`
- Modify: `CmmsDbContext.cs` (sets, config, site filter like `Team`), `Locations.cs`, `Assets.cs`, `Work.cs` (`WorkRequest`, `WorkOrder`), `Pm.cs` (`PmSchedule`), `Inventory.cs` (`StockTransaction`) — `CostCentreId` (Guid?) on each; `Program.cs`; `ar.json`
- Create: migration `CostCentres` (`wt-ef CostCentres`)
- Test: `server/FourC.Cmms.Tests/CostCentreTests.cs`

**Interfaces:**
- Produces:
  - `class CostCentre : TenantEntity` — `Code`, `Name`, `ParentId?`, `SiteId?`, `IsActive`; `class Budget : TenantEntity` — `CostCentreId`, `Year`, `Month`, `Amount` (18,2), unique (tenant, cost centre, year, month) filtered `IsDeleted = 0`.
  - Routes: `GET /api/cost-centres` (signed in) → `[{ id, code, name, parentId, siteId, siteName, isActive, path }]` where `path` = "Production › Line 1"; `POST/PUT/DELETE /api/cost-centres[/{id}]` (`settings.manage`).
  - `record CostCentreInput(string Code, string Name, Guid? ParentId, Guid? SiteId, bool IsActive = true)`.
  - Messages: "This code is already used." (existing), "A cost centre can't be its own parent or under one of its own.", "Choose an active parent.", "This cost centre is in use. Make it inactive instead.", "A site's cost centre can only sit under a cost centre of the same site or for all sites."

- [ ] **Step 1: Write the failing tests**
  - `Tree_has_parents_and_no_cycles`: create PROD "Production", LINE1 "Line 1" (parent PROD), PACK "Packing" (parent LINE1) → GET gives PACK `path == "Production › Line 1 › Packing"`; PUT PROD with parent PACK → 400 cycle message.
  - `Cost_centre_in_use_cannot_be_deleted`: set PACK on a location; DELETE PACK → 400 in-use message; an unused one deletes.
  - `Site_limited_people_see_their_sites_and_shared_cost_centres`: SHARED (no site), A (site A), B (site B); a user limited to site A lists SHARED and A, not B; GET/PUT of B by them → 404.
- [ ] **Step 2: Run** `wt-test CostCentreTests` — Expected: FAIL (types/routes missing).
- [ ] **Step 3: Implement** entities, config (NoAction FKs; `CostCentre` gets the site query filter as `Team` does: `SiteId == null || in CurrentSiteIds`), migration, endpoints; in use = referenced by any location, asset, request, PM, work order, stock transaction, budget, or child.
- [ ] **Step 4: Run** `wt-test CostCentreTests` — Expected: 3 passed.
- [ ] **Step 5: Commit** `feat(costs): cost-centre tree`

### Task 2: Cost centre in force, and on every new work order

**Files:**
- Create: `server/FourC.Cmms.Api/Features/CostCentreQueries.cs`
- Modify: `LocationEndpoints.cs`, `AssetEndpoints.cs` (inputs + DTOs: `costCentreId`), `WorkRequestEndpoints.cs` (`RequestInput.CostCentreId`, passed on conversion), `PmEndpoints.cs` (`ScheduleInput.CostCentreId`), `PmGenerator.cs`, `WorkGroups.cs`, `WorkOrderEndpoints.cs` (`WorkOrderInput.CostCentreId`, `ManagesCostCentre`, `CreateAsync`, DTOs return `costCentreId` and `costCentrePath`)
- Test: `server/FourC.Cmms.Tests/CostCentreWorkTests.cs`

**Interfaces:**
- Produces:
  - `CostCentreQueries.InForceAsync(CmmsDbContext db, Guid? assetId, Guid? locationId, CancellationToken ct) : Task<Guid?>` — asset's own, else up its parent assets, else its location up the location tree; reads across all sites.
  - `WorkOrderInput` gains `Guid? CostCentreId = null, bool? ManagesCostCentre = null` (last); `CreateAsync` sets `CostCentreId = input.CostCentreId ?? InForceAsync(asset, location)`; update applies it only when `ManagesCostCentre == true`.
  - PM generation passes the schedule's `CostCentreId` (override; null = in force per work order, so a group PM's work orders each take their own asset's); request conversion passes the request's.
  - Cost-centre ids checked: active, visible ("Choose an active cost centre.").

- [ ] **Step 1: Write the failing tests**
  - `In_force_follows_the_asset_then_its_parents_then_its_location`: location L (cost centre FAC) with child location L2; asset P at L2 with no cost centre, child asset C of P; P gets PROD → a work order on C gets PROD; clear P's → FAC (from L via L2).
  - `Every_way_of_creating_work_gets_the_cost_centre`: asset with PROD → work order from the form, from a converted request, from a generated PM, from each asset of a group PM (second asset with PACK gets PACK), and from the phone's `wo.create` action all have PROD (resp. PACK).
  - `PM_and_request_overrides_win`: PM with MAINT → generated work gets MAINT; request with MAINT → converted work gets MAINT.
  - `Changing_an_assets_cost_centre_leaves_existing_work`: work on asset (PROD), then asset → PACK: the work order still PROD; a new one PACK.
- [ ] **Step 2: Run** `wt-test CostCentreWorkTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "CostCentreWorkTests|CostCentreTests"` — Expected: all passed.
- [ ] **Step 5: Commit** `feat(costs): cost centre in force, given to all new work`

### Task 3: Corrections — change on any work order, direct issues, fill in past work

**Files:**
- Modify: `WorkOrderEndpoints.cs`, `InventoryEndpoints.cs` (`IssueInput.CostCentreId`; issue/return to a location default the location's in force; transactions DTO `costCentreId`), `CostCentreEndpoints.cs`
- Test: `server/FourC.Cmms.Tests/CostCentreCorrectionTests.cs`

**Interfaces:**
- Produces:
  - `POST /api/work-orders/cost-centre { ids: Guid[], costCentreId: Guid? }` (`workorders.edit`): any status; returns `{ changed }`; each change audited (work-order history).
  - `PUT /api/inventory/transactions/{id}/cost-centre { costCentreId }` (`inventory.manage`): only issues and returns without a work order, else 400 "Only parts issued to a location have their own cost centre."
  - `GET /api/cost-centres/fill-in?from=` → `{ workOrders, issues }` counts; `POST /api/cost-centres/fill-in { from }` (`settings.manage`) → same counts, applied: rows dated (work orders by `CreatedAt`, issues by `Date`) on/after `from` with no cost centre get the one in force today; never overwrites.

- [ ] **Step 1: Write the failing tests**
  - `Change_works_on_closed_work_and_is_logged`: a closed work order with labour; POST cost-centre to MAINT → 200, work order MAINT, its history has an entry naming CostCentreId; a technician (execute only) → 403.
  - `Direct_issue_takes_the_locations_cost_centre_and_can_change`: issue 2 parts to location L (FAC) without work order → transaction FAC; with `costCentreId` PROD → PROD; PUT to MAINT works; PUT on a work-order issue → 400 message.
  - `Fill_in_never_overwrites_counts_first_and_repeats`: three work orders since date D without cost centre (assets with PROD), one with PACK, one before D → GET counts 3; POST → 3 changed, PACK kept, older one untouched; POST again → 0.
- [ ] **Step 2: Run** `wt-test CostCentreCorrectionTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: 3 passed.
- [ ] **Step 5: Commit** `feat(costs): corrections, direct issues and filling in past work`

### Task 4: Budgets

**Files:**
- Create: `server/FourC.Cmms.Api/Features/BudgetEndpoints.cs`
- Test: `server/FourC.Cmms.Tests/BudgetTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/budgets?year=` (`reports.view`) → `{ year, rows: [{ costCentreId, path, months: decimal[12], total }] }` for visible cost centres (rows with no budget have zeros).
  - `PUT /api/budgets { year, rows: [{ costCentreId, months: decimal?[12] }] }` (`settings.manage`): sets each month (null or 0 removes it); amounts ≥ 0 ("A budget can't be below 0."); year 2000–2100.

- [ ] **Step 1: Write the failing tests**
  - `Budget_grid_saves_months_per_cost_centre`: PUT 2027 PROD months [100…] → GET 2027 PROD months and total 1200; PUT again with month 3 = null → month 3 is 0, total 1100.
  - `Budgets_need_settings_rights_and_no_negatives`: a Viewer with reports.view reads, PUT → 403; −5 → 400 message.
- [ ] **Step 2: Run** `wt-test BudgetTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: 2 passed.
- [ ] **Step 5: Commit** `feat(costs): budgets per cost centre per month`

### Task 5: Budget report and the Maintenance costs report by cost centre

**Files:**
- Modify: `ReportEndpoints.cs` (`ReportArgs` + `CostCentreId`; GET `/{key}` takes `costCentreId`; `CostLinesAsync` keeps its totals; `maintenance-costs` gains grouping `costCentre` and the filter with children; new report `budget-vs-actual`)
- Create: `server/FourC.Cmms.Api/Features/BudgetReport.cs`
- Test: `server/FourC.Cmms.Tests/BudgetReportTests.cs`

**Interfaces:**
- Consumes: `CostLinesAsync`, budgets, `CostCentreQueries`.
- Produces: report `budget-vs-actual` (`reports.view`; From/To, optional `costCentreId` with children; group by `costCentre` (default) or `month`): rows `{ group, budget, committed, labor, parts, contractor, other, directIssues, actual, variance }` with `actual = labor + parts + contractor + other + directIssues`, `variance = budget − actual − committed`; cost-centre rows indented by depth (field `depth`), parents adding their children (hidden children included, not listed); a "No cost centre" row; whole figures across sites (`AllSites()`).
  - Committed: lines of `Approved`/`PartiallyReceived` orders with a work order, `max(Quantity − QuantityReceived, 0) × NetUnitPrice × ExchangeRate`, dated by the order's `ExpectedDate ?? OrderDate`, to the work order's cost centre.
  - Direct issues: issues less returns with no work order, by date, to the transaction's cost centre.

- [ ] **Step 1: Write the failing tests**
  - `Actual_on_work_orders_matches_maintenance_costs`: work orders on PROD and none, with labour, parts, contractor and other costs → `budget-vs-actual` labor+parts+contractor+other over all rows (incl. "No cost centre") equals `maintenance-costs` total for the same period.
  - `Committed_is_the_open_part_of_approved_work_order_lines`: approved EUR order (rate 50) for a PROD work order, 10 × 2 EUR, 4 received → committed 600; a draft and a cancelled order add nothing; a stock line without work order adds nothing; change the work order to PACK → committed moves to PACK.
  - `Parents_add_up_their_children_and_direct_issues_have_a_column`: budget PROD 1000 and LINE1 (child) 500; labour 100 on LINE1 work and a direct issue of 30 to PROD → PROD row budget 1500, actual 130 with directIssues 30; LINE1 row depth 1.
  - `Maintenance_costs_by_cost_centre_keeps_its_totals`: grouping `costCentre` total equals the ungrouped total; filter PROD (with LINE1) total = their work only.
- [ ] **Step 2: Run** `wt-test BudgetReportTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "BudgetReportTests|Report"` — Expected: all passed.
- [ ] **Step 5: Commit** `feat(costs): budget against actual and costs by cost centre`

### Task 6: Import and export

**Files:**
- Modify: `Import/ImportSheets.cs`, `Import/SheetImporters.cs`, `Import/ImportWorkbook.cs`; sheet-count tests
- Test: `server/FourC.Cmms.Tests/CostCentreImportTests.cs`

**Interfaces:**
- Produces: sheets **CostCentres** (code*, name*, parent code, site code, active) — parents before children within a sheet; **Budgets** (cost centre code*, year*, month*, amount) — replaces that month; Locations, Assets and PmSchedules gain "Cost centre code" (blank keeps). All in "current data" export.

- [ ] **Step 1: Write the failing tests**
  - `Cost_centre_and_budget_sheets_import_and_export`: CostCentres sheet with PROD and LINE1 (parent PROD) and a Budgets row LINE1 2027-01 500 → tree and budget exist; current data round-trips with 0 new/changed/problems.
  - `Location_asset_and_pm_sheets_set_cost_centres`: each sheet with "Cost centre code" PROD sets it; an unknown code is a problem "Unknown cost centre".
- [ ] **Step 2: Run** `wt-test CostCentreImportTests` — Expected: FAIL.
- [ ] **Step 3: Implement**; update sheet counts in ImportExportTests/ImportValuesTests (ruling expected).
- [ ] **Step 4: Run** `wt-test "CostCentreImportTests|Import"` — Expected: all passed.
- [ ] **Step 5: Commit** `feat(import): cost centres, budgets and cost-centre columns`

### Task 7: Web

**Files:**
- Create: `web/src/pages/settings/CostCentresSettings.tsx`, `web/src/pages/reports/BudgetsPage.tsx`, `web/src/components/costCentres.ts` (+ test), `web/src/components/CostCentrePicker.tsx`
- Modify: settings tab, `App.tsx` (`reports/budgets`), menu; location, asset, request, work order and PM forms (picker; work order form resolves the in-force one by leaving it empty); work order detail ("Change cost centre"); work orders list bulk action; stock issue/return modal (location issues); stock movements list (change); reports page (cost-centre filter on Maintenance costs and Budget against actual, grouping); `api/types.ts`; locales
- Test: `web/src/components/costCentres.test.ts`

**Interfaces:**
- Produces (`costCentres.ts`): `treeRows(list: CostCentre[]): (CostCentre & { depth: number })[]` (parents first, children under them, by code); `spread(total: number, months = 12): number[]` (2 dp, the rest on the last month: `spread(1000)` → eleven 83.33 and 83.37).

- [ ] **Step 1: Write the failing Vitest tests** for `treeRows` (order and depth, orphans of hidden parents at depth 0) and `spread` (1000 → 83.33×11 + 83.37; 0 → zeros; sums to the total).
- [ ] **Step 2: Run** `wt-vitest` — Expected: new tests fail.
- [ ] **Step 3: Implement** helpers and screens; locales en + ar.
- [ ] **Step 4: Run** `wt-vitest` and `wt-web` — Expected: all passed; build clean.
- [ ] **Step 5: Commit** `feat(web): cost centres, budgets and the budget report`

### Task 8: Docs and full verification

**Files:**
- Create: `docs/COST_CENTRES_AND_BUDGETS.md`; Modify: `README.md` (feature line)

- [ ] **Step 1: Write the docs.**
- [ ] **Step 2: Run** `wt-ci`, `wt-vitest`, `wt-web` — Expected: build succeeded; server green (1179 + new, 2 skipped); web green; build clean.
- [ ] **Step 3: Commit** `docs(0.20.0): cost centres and budgets`
