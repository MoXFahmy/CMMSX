# Failure Codes and Maintenance Analysis (0.14.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Symptom / cause / remedy codes recorded on requests, work orders and the phone with smart ordering; failure tree, responsiveness, PM annual plan and asset work history reports; asset costs by year with downtime cost; an organization logo and print-to-PDF layouts for every report.

**Architecture:** Symptoms and remedies are lookups like `FailureCode` (which stays as the cause list), mapped with `PeopleEndpoints.MapLookup` and imported like `Trades`. Smart ordering comes from one pairs endpoint (and the phone bundle) fed to one pure web helper. Reports keep the uniform `ReportResult` shape, extended with three row conventions (drill-down link fields, a tree level, a month-plan cell type) and two parameter types (asset, year). The print layout is a web component shared by reports and the job card.

**Tech Stack:** ASP.NET Core 10, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19 + Mantine 8 (+ `@mantine/charts`, `mantine-datatable`) + TanStack Query + i18next, Vitest (static-render tests via `web/src/test/render.tsx`); all builds in Docker.

**Spec:** `docs/superpowers/specs/2026-11-22-failure-analysis-0.14.0-design.md`

## Global Constraints

- One migration, `FailureAnalysis`: new tables `Symptoms`, `Remedies`; nullable columns `WorkRequests.SymptomId`, `WorkOrders.SymptomId`, `WorkOrders.RemedyId`, `AssetCategories.DowntimeCostPerHour` (decimal(18,2)); no data rewrite. `FailureCode` keeps its table, entity and `/api/failure-codes` route; screens say *Cause*.
- Organization settings (JSON, no column): `LogoVersion` (long?, null = no logo).
- No new permission keys: code lists `settings.manage` to change (any signed-in user reads); reports keep their permission; Costs tab `workorders.view`; logo `settings.manage` to change, any signed-in user to view.
- Codes are optional everywhere. The existing setting `RequireFailureCodeForBreakdowns` is unchanged in behaviour; its message becomes "Choose a cause for breakdown work."
- A record keeps an inactive code it already has; setting a new code needs an active one ("Unknown symptom." / "Unknown cause." / "Unknown remedy." otherwise, field `symptomId` / `failureCodeId` / `remedyId`).
- Site access (0.9.0) applies to every new query, report, the Costs tab, pickers, the pairs counts and the phone bundle.
- Logo: PNG, JPEG or WebP, at most 1 MB ("Choose a PNG, JPEG or WebP image." / "The logo can be at most 1 MB."); stored with `IFileStorage` under `{tenant:N}/tenant/logo`.
- Smart ordering: pairs from work orders completed in the last 2 years, at most 20 per first code, most used first.
- Every new text has Arabic: web `en.json`/`ar.json` (the locale parity test) and server `Localization/ar.json` (a theory row per new message).
- EF lessons: new child rows added through a tracked parent are marked `Added`; never `IgnoreQueryFilters()` inside a subquery.
- Messages (exact English): "Unknown symptom.", "Unknown cause.", "Unknown remedy.", "Choose a cause for breakdown work.", "Choose an asset.", "Choose a year from 2000 to 2100.", the two logo messages above, report titles "Failure tree", "Responsiveness", "PM annual plan", "Asset work history", "Not recorded", "Round of {0} assets", "Group of {0} assets", "No trade".

## Review Focus

1. A user limited to one site runs the new reports and opens the Costs tab of an asset on another site — they see only their site's work, and the other asset answers not found. (Tests in Tasks 9, 10, 11.)
2. A code made inactive after it was recorded — editing the work order, completing a group, or the phone completing — keeps it; a new inactive code is refused. (Tests in Tasks 3, 4, 7.)
3. A failure-tree line's count equals the work order list opened from it, including *Not recorded* lines. (Test in Task 9.)
4. A phone from 0.13 (no `symptomId` / `remedyId` in its payloads) keeps working, and completing from it doesn't wipe codes set on the web. (Test in Task 7.)
5. PM annual plan around the current month and the year's edges: this month shows both what was done and what is still planned; an occurrence on 31 December stays in December in the organization's time zone. (Test in Task 10.)

---

### Task 1: Model and migration

**Files:**
- Modify: `server/FourC.Cmms.Core/Work/Work.cs` (add `Symptom`, `Remedy`; `WorkRequest.SymptomId/Symptom`; `WorkOrder.SymptomId/Symptom`, `RemedyId/Remedy`)
- Modify: `server/FourC.Cmms.Core/Assets/Assets.cs` (`AssetCategory.DowntimeCostPerHour`)
- Modify: `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`TenantSettings.LogoVersion`)
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (DbSets, config like `FailureCode`: unique `(TenantId, Code)`, FKs `Restrict`, decimal precision)
- Create: migration `FailureAnalysis` (`wt-ef FailureAnalysis`)
- Test: `server/FourC.Cmms.Tests/FailureCodeModelTests.cs`

**Interfaces:**
- Produces: `public class Symptom : TenantEntity, ICodeLookup { Code, Name, IsActive = true }`, `public class Remedy : TenantEntity, ICodeLookup` (same); `CmmsDbContext.Symptoms`, `.Remedies`; `Asset`-side helper `public static decimal? CostMath.DowntimeCostPerHour(decimal? asset, decimal? category) => asset ?? category` in `server/FourC.Cmms.Core/Assets/CostMath.cs`.

- [ ] **Step 1: Write the failing tests** — `Codes_save_and_link_to_requests_and_work_orders` (a symptom and a remedy saved with a tenant, a request and a work order pointing at them read back with the same ids); `Category_downtime_cost_saves`; `Asset_cost_wins_over_its_category` (`CostMath.DowntimeCostPerHour(50, 20) == 50`, `(null, 20) == 20`, `(null, null) == null`); `Logo_version_defaults_to_none` (`new TenantSettings().LogoVersion is null`).
- [ ] **Step 2: Run** `wt-test FailureCodeModelTests` — Expected: build errors (types missing).
- [ ] **Step 3: Implement** the entities, config and `CostMath`, then `wt-ef FailureAnalysis`; check the migration only creates the two tables and adds the four nullable columns.
- [ ] **Step 4: Run** `wt-test FailureCodeModelTests` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): symptom and remedy codes, category downtime cost, logo setting (model and migration)`.

### Task 2: Code lists — API, settings tabs, import

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/PeopleEndpoints.cs` (map `/symptoms`, `/remedies`; in use = any request or work order (all sites) using it)
- Modify: `server/FourC.Cmms.Api/Features/Import/ImportSheets.cs` (sheets `Symptoms`, `Causes`, `Remedies`: Code, Name, Active; `Categories` gains `N("downtimeCostPerHour", "Downtime cost per hour", "تكلفة ساعة التوقف")`)
- Modify: `server/FourC.Cmms.Api/Features/Import/SheetImporters.cs` (`SymptomImporter`, `CauseImporter` (FailureCodes), `RemedyImporter` — copies of `TradeImporter` without colour; category importer reads/writes the new column)
- Modify: `ImportRun` / `ImportWorkbook` registration and export of the three sheets; `SupportEndpoints.CategoryInput` gains `decimal? DowntimeCostPerHour = null` (≥ 0, "Downtime cost can't be negative.")
- Modify: `web/src/pages/settings/SettingsPage.tsx` (tabs *Symptoms*, *Causes* (the failure-codes tab relabelled), *Remedies*, via `LookupTable`); category form field; locales
- Modify: `server/FourC.Cmms.Tests/ImportExportTests.cs`, `ImportValuesTests.cs` (sheet counts 17→20, 18→21 with Help)
- Test: `server/FourC.Cmms.Tests/CodeListTests.cs`, `server/FourC.Cmms.Tests/ImportCodeTests.cs`

**Interfaces:**
- Consumes: Task 1 entities.
- Produces: routes `GET/POST/PUT/DELETE /api/symptoms`, `/api/remedies` (same shapes as `/api/failure-codes`); import context dictionaries `c.Symptoms`, `c.Causes`, `c.Remedies` (code → id).

- [ ] **Step 1: Write the failing tests** — `Symptoms_and_remedies_are_lists_like_causes` (create, list sorted, rename, deactivate; duplicate code refused with the lookup message); `A_code_in_use_cannot_be_deleted` (a symptom on a request and a remedy on a work order → delete refused, inactive allowed); `Lists_need_settings_manage` (a technician gets 403 on POST); `Import_adds_and_updates_codes_by_code` (Symptoms/Causes/Remedies rows → added, a second import with a changed name → updated); `Categories_import_downtime_cost`.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~CodeListTests|FullyQualifiedName~ImportCodeTests"` — Expected: FAIL (404s, unknown sheets).
- [ ] **Step 3: Implement** routes, importers, sheets, category input; update the two sheet-count tests (ledger it as deliberate).
- [ ] **Step 4: Run** the same filter plus `ImportExportTests|ImportValuesTests` — Expected: PASS. Run `wt-vitest` (locale parity) and `wt-web`.
- [ ] **Step 5: Commit** `feat(0.14.0): symptom, cause and remedy lists in settings and the import`.

### Task 3: Symptom on requests and work orders (server)

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/WorkRequestEndpoints.cs` (`RequestInput` + `Guid? SymptomId = null`; validate; copy to the work order in approve and `AutoApproveAsync`; detail and list output `symptom {id, code, name}`; list column/export)
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (`WorkOrderInput` + `Guid? SymptomId = null` at the end; create and update; detail output `symptom`, `failureCode` (unchanged name), `remedy`; `WorkOrderFilter` + `string? SymptomId`, `string? CauseId`, `string? RemedyId` (a Guid or `none`), `DateOnly? CompletedTo`; list columns and export `symptom`, `cause`, `remedy`)
- Create: `server/FourC.Cmms.Api/Features/Codes.cs` (shared checks)
- Test: `server/FourC.Cmms.Tests/SymptomTests.cs`

**Interfaces:**
- Consumes: Task 1, Task 2 routes for test setup.
- Produces: `internal static class Codes { Task<string?> CheckAsync<T>(DbSet<T> set, Guid? newId, Guid? currentId, string message, CancellationToken ct) where T : TenantEntity, ICodeLookup }` — null when `newId` is null, equals `currentId`, or names an active code; otherwise `message`. Constants `UnknownSymptom = "Unknown symptom."`, `UnknownCause = "Unknown cause."`, `UnknownRemedy = "Unknown remedy."`. Filter semantics: `none` = the field is null.

- [ ] **Step 1: Write the failing tests** — `Request_symptom_reaches_its_work_order` (request with symptom → approve → work order has it; auto-approve type → same); `Work_order_symptom_is_set_and_changed`; `Inactive_symptom_is_kept_but_not_newly_set` (record with S1, S1 made inactive → PUT with S1 OK, PUT with inactive S2 → 400 "Unknown symptom."); `Work_order_list_filters_by_codes` (three work orders: symptom S1, symptom none, cause C1 + remedy R1 completed on two dates → `symptomId=S1`, `symptomId=none`, `causeId=C1&remedyId=R1`, `completedFrom/To` return exactly the expected ids); `List_and_export_show_codes`.
- [ ] **Step 2: Run** `wt-test SymptomTests` — Expected: FAIL.
- [ ] **Step 3: Implement** as above; one `Codes.CheckAsync` call per field.
- [ ] **Step 4: Run** `wt-test SymptomTests` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): symptoms on requests and work orders, code filters on the work order list`.

### Task 4: Cause and remedy on completion (server)

**Files:**
- Modify: `WorkOrderEndpoints.cs` (`StatusInput` + `Guid? RemedyId = null`; `ChangeStatusAsync` checks cause/remedy with `Codes.CheckAsync`; message "Choose a cause for breakdown work."; completion section editable `remedyId`)
- Modify: `server/FourC.Cmms.Api/Features/WorkGroupEndpoints.cs` (`ReportInput` + `Guid? RemedyId = null`; set like the failure code: only where empty)
- Modify: existing tests that assert "Choose a failure code for breakdown work." (ledger the wording change)
- Test: `server/FourC.Cmms.Tests/RemedyTests.cs`

**Interfaces:**
- Consumes: Task 3 `Codes`.
- Produces: `StatusInput.RemedyId`; `WorkGroupEndpoints.ReportInput.RemedyId`.

- [ ] **Step 1: Write the failing tests** — `Completing_records_cause_and_remedy`; `Breakdown_rule_asks_for_a_cause` (setting on, breakdown completed without cause → 400 "Choose a cause for breakdown work."; remedy never required); `Inactive_cause_and_remedy_are_kept_on_completion` (codes set, made inactive, complete sending the same ids → OK); `Group_report_sets_remedy_where_empty`.
- [ ] **Step 2: Run** `wt-test RemedyTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "FullyQualifiedName~RemedyTests|FullyQualifiedName~WorkGroup|FullyQualifiedName~CompletionRule"` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): remedy on completion and group reports; cause wording`.

### Task 5: Smart ordering

**Files:**
- Create: endpoint `GET /api/code-pairs` in `Codes.cs` (mapped from `Program.cs` or the work order group) — `workorders.view`
- Create: `web/src/components/codes.ts` + `codes.test.ts`
- Test: `server/FourC.Cmms.Tests/CodePairTests.cs`

**Interfaces:**
- Produces (server): `record CodePairs(List<CodePair> SymptomCause, List<CodePair> CauseRemedy)`, `record CodePair(Guid First, Guid Second, int Count)`; `internal static Task<CodePairs> Codes.PairsAsync(CmmsDbContext db, DateOnly since, CancellationToken ct)` — work orders completed since `since` (site filter applies), grouped, top 20 per `First`, most used first.
- Produces (web): `export type CodePair = { first: string; second: string; count: number }`; `export function orderCodes<T extends { id: string; code: string }>(codes: T[], pairs: CodePair[], first?: string | null): { top: T[]; rest: T[] }` — `top` = codes paired with `first`, by count; `rest` = the others by code; inactive codes are the caller's to filter. `export function codeOptions(...)` returning Mantine grouped `data` (`[{ group: t('codes.mostUsed'), items }, { group: t('codes.all'), items }]`, no empty groups).

- [ ] **Step 1: Write the failing tests** — server `Pairs_count_completed_work_of_two_years_per_site` (S1+C1 twice, S1+C2 once, one older than two years, one on a site the user can't see → `[S1,C1,2],[S1,C2,1]` for a site-limited user); `Pairs_keep_twenty_per_code`. Web `orderCodes` puts paired codes first by count, keeps the rest by code, returns everything in `rest` when `first` is empty.
- [ ] **Step 2: Run** `wt-test CodePairTests` and `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** both — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): causes and remedies most used together come first`.

### Task 6: Codes on the web

**Files:**
- Create: `web/src/components/CodeSelect.tsx` (a `Select` over a lookup path with `orderCodes` grouping; shows an inactive current value)
- Modify: `web/src/pages/work/WorkRequestsPage.tsx` (form symptom; detail; list column), `WorkOrderFormPage.tsx` (symptom), `WorkOrderDetailPage.tsx` (fields Symptom / Cause / Remedy; completion dialog cause + remedy; the failure-code labels become *Cause*), `WorkGroupPages.tsx` (report remedy), `WorkOrdersPage.tsx` (columns, filters from the URL: `symptomId`, `causeId`, `remedyId`, `completedFrom`, `completedTo`), `WorkOrderPrintPage.tsx` (three codes), `SettingsPage.tsx` (the completion rule reads *Require a cause on breakdown work*); `api/types.ts`; locales
- Test: `web/src/components/CodeSelect.test.tsx` (static render)

**Interfaces:**
- Consumes: Task 5 `orderCodes`, `codeOptions`; Task 3/4 fields.
- Produces: `<CodeSelect path="/symptoms" | "/failure-codes" | "/remedies" label value onChange pairedWith?={id} pairs?={CodePair[]} />`.

- [ ] **Step 1: Write the failing test** — `CodeSelect` renders the current inactive code's label, and (with pairs) the *Most used* group before *All*.
- [ ] **Step 2: Run** `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement** the component and wire every screen listed.
- [ ] **Step 4: Run** `wt-vitest` and `wt-web` — Expected: PASS, build clean.
- [ ] **Step 5: Commit** `feat(0.14.0): web — symptoms, causes and remedies on requests, work orders, groups, lists and the job card`.

### Task 7: Codes on the phone

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/TechEndpoints.cs` (bundle `symptoms`, `remedies` (active), `codePairs` (Task 5, for the person's sites); job `symptomId`, `failureCodeId`, `remedyId`)
- Modify: `server/FourC.Cmms.Api/Features/TechActions.cs` (`RequestPayload` + `Guid? SymptomId = null`; `WorkOrderPayload` + `Guid? SymptomId = null`; `StatusPayload` + `Guid? RemedyId = null`; a payload without them leaves the record's codes as they are)
- Modify: `web/src/tech/types.ts`, `JobPage.tsx` (completion remedy select; cause and remedy ordered with `orderCodes`; job shows the three codes), `PlacePage.tsx` (request symptom), `JobExtras.tsx` (`NewWorkOrderPage` symptom), `overlay.ts` (queued status shows the codes); locales
- Test: `server/FourC.Cmms.Tests/TechCodeTests.cs`; `web/src/tech/overlay.test.ts` (codes on a queued completion)

**Interfaces:**
- Consumes: Tasks 3–5.
- Produces: bundle fields `symptoms: Lookup[]`, `remedies: Lookup[]`, `codePairs: { symptomCause: CodePair[]; causeRemedy: CodePair[] }`.

- [ ] **Step 1: Write the failing tests** — `Phone_request_and_work_order_take_a_symptom`; `Phone_completion_records_cause_and_remedy`; `Old_phone_completion_keeps_codes_set_on_the_web` (web sets cause + remedy; a 0.13 `status.change` without them completes and keeps both); `Phone_inactive_code_already_on_the_job_is_accepted`; `Bundle_has_codes_and_pairs_for_my_sites`; vitest: overlay shows a queued completion's cause and remedy.
- [ ] **Step 2: Run** `wt-test TechCodeTests` and `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** both and `wt-web` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): phone — symptom on new requests and work orders, cause and remedy on completion, offline ordering`.

### Task 8: Report infrastructure — asset and year parameters, links, tree levels, plan cells

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/ReportEndpoints.cs` (`ReportArgs` + `Guid? AssetId`, `int Year`, `bool Components`; query params `assetId`, `year`, `components`; `Param` types `"asset"`, `"year"`, `"bool"`; a report with an `asset` param and no asset → 400 "Choose an asset."; `year` outside 2000–2100 → 400 "Choose a year from 2000 to 2100."; column type `"plan"`)
- Modify: `web/src/api/types.ts` (`ReportParam.type` adds `'asset' | 'year' | 'bool'`; `ReportColumnType` adds `'plan'`)
- Modify: `web/src/pages/reports/ReportsPage.tsx` (asset picker, year input, switch; row links: `workOrderId` → `/work-orders/{id}`, `requestId` → `/work-requests/{id}`, `listQuery` → `/work-orders?{listQuery}`; rows with `level` indent the first column, bold levels 0–1, sorting off when any row has a level, the chart uses level-0 rows; `plan` cells render `{done, late, missed, cancelled, planned}` as small coloured counts; CSV exports a plan cell as `done 2 · late 1 · planned 1`)
- Create: `web/src/pages/reports/reportCells.ts` + `.test.ts`

**Interfaces:**
- Produces (server): `public sealed record PlanCell(int Done, int Late, int Missed, int Cancelled, int Planned)`; row fields `workOrderId`, `requestId`, `listQuery` (string), `level` (0, 1, 2).
- Produces (web): `planText(c: PlanCell, t: TFunction): string`; `rowLink(r: Record<string, unknown>, key: string): string | null` (extends today's `link`: part, asset, purchase order, work order, request, list query); `treeRows(rows)` (chart rows = level 0 or no level).

- [ ] **Step 1: Write the failing tests** — web `planText`, `rowLink` (all six link kinds), `treeRows`. (The server checks for a missing asset and a bad year need a report that uses them; they are tested in Task 10.)
- [ ] **Step 2: Run** `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement** the server plumbing and the web rendering.
- [ ] **Step 4: Run** `wt-vitest`, `wt-web`, `wt-test ReportTests` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): report parameters for an asset and a year, drill-down links, tree rows and month-plan cells`.

### Task 9: Failure tree and Responsiveness

**Files:**
- Modify: `ReportEndpoints.cs` (catalog entries `failure-tree` (Reliability, `workorders.view`, Range + Site) and `responsiveness` (Work, `workorders.view`, Range + Site + GroupBy `order` (default) / `priority` / `trade`); `FailureTreeAsync`, `ResponsivenessAsync`)
- Test: `server/FourC.Cmms.Tests/FailureTreeTests.cs`, `server/FourC.Cmms.Tests/ResponsivenessTests.cs`

**Interfaces:**
- Consumes: Task 8 row conventions; the existing `Outcome` and cost queries (extract `CostLinesAsync(db, IQueryable<WorkOrder>, from?, to?)` from `CostsAsync` and reuse it here and in Tasks 10–11).
- Produces: `internal static Task<List<CostLine>> ReportEndpoints.CostLinesAsync(CmmsDbContext db, IQueryable<WorkOrder> wos, DateOnly? from, DateOnly? to, CancellationToken ct)` (null bounds = all dates); `CostLine` becomes `internal` (Task 11 uses it from `AssetEndpoints`).

- [ ] **Step 1: Write the failing tests** —
  - `Tree_has_symptom_cause_remedy_levels_with_totals` (S1/C1/R1 ×2, S1/C1/R2, S1/C2/none, a breakdown with no codes, a PM work order with no codes (left out), one completed outside the period (left out) → level-0 S1 = 4 work orders, its C1 = 3 with R1 = 2 and R2 = 1, C2 = 1 with "Not recorded" = 1, a level-0 "Not recorded" = 1; hours, downtime and cost add up).
  - `Tree_line_opens_the_same_work_orders` (for every row, `GET /api/work-orders?{listQuery}` count equals the row's work orders, *Not recorded* included).
  - `Tree_follows_site_access`.
  - `Responsiveness_lists_each_order_with_its_outcomes` (a request decided in time, a work order responded late, one pending, one imported with no time → its rows and outcomes; `requestId` / `workOrderId` set).
  - `Responsiveness_groups_average_by_priority_and_trade` (trade of a request = its work order's, else its type's, else "No trade").
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~FailureTreeTests|FullyQualifiedName~ResponsivenessTests"` — Expected: FAIL (404 report).
- [ ] **Step 3: Implement** the two reports and extract `CostLinesAsync` (Maintenance costs keeps its results — its tests must stay green).
- [ ] **Step 4: Run** the filter plus `ReportTests` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): failure tree and responsiveness reports`.

### Task 10: PM annual plan, asset work history, PM compliance by category, costs by technician

**Files:**
- Modify: `ReportEndpoints.cs` (entries `pm-annual-plan` (Work, `pm.view`, Year + Site) and `asset-work-history` (Assets, `workorders.view`, Asset + Range + `components` bool); `PmAnnualPlanAsync` (past months from generated work orders with `PmDueDate ?? DueDate` in the month, outcomes as `PmComplianceAsync`; this and later months add `PmEndpoints.ForecastAsync` rows not yet generated); `AssetHistoryAsync`; `pm-compliance` group-by `category`; `maintenance-costs` column `hours` and group-by `technician`)
- Test: `server/FourC.Cmms.Tests/AnnualPlanTests.cs`, `server/FourC.Cmms.Tests/AssetHistoryReportTests.cs`, additions to existing PM compliance and costs report tests

**Interfaces:**
- Consumes: Task 8 (`PlanCell`, params), Task 9 `CostLinesAsync`, `PmEndpoints.ForecastAsync(db, from, to, siteId, ct)`.

- [ ] **Step 1: Write the failing tests** —
  - `Annual_plan_shows_what_happened_and_what_is_planned` (a monthly PM with January done on time, February late, March missed, the current month done and one more planned this month, later months planned → cells match; a PM with nothing in the year is not listed; a round PM's asset column is "Round of 2 assets").
  - `Annual_plan_keeps_year_edges_in_the_organizations_time_zone` (an occurrence due 31 December → December cell).
  - `Year_is_checked` (`year=1999` → 400 "Choose a year from 2000 to 2100.").
  - `Asset_history_lists_its_work_with_codes_and_costs` (two work orders on the asset, one on a component (only with `components=true`), one round listing it in steps (left out)).
  - `Asset_report_needs_an_asset` (no `assetId` → 400 "Choose an asset."); `Asset_history_of_an_asset_I_cannot_see_is_not_found`.
  - `Pm_compliance_groups_by_category`; `Costs_by_technician_show_their_labour_and_hours` (parts and other blank).
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~AnnualPlanTests|FullyQualifiedName~AssetHistoryReportTests|FullyQualifiedName~ReportTests"` — Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): PM annual plan and asset work history reports; PM compliance by category; costs by technician`.

### Task 11: Asset costs by year with downtime cost

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/AssetEndpoints.cs` (`GET /api/assets/{id}/costs?components=` — `workorders.view`; years = the last five and the current one plus any older year with costs; per year `labor`, `parts`, `other`, `downtimeHours`, `downtimeCost` (null without a cost per hour), `total` = labour + parts + other + downtime cost; the effective cost per hour via `CostMath`)
- Modify: `ReportEndpoints.cs` reliability (`downtimeCost` uses the effective cost)
- Create: `web/src/pages/assets/AssetCosts.tsx` (tab *Costs* on `AssetDetailPage.tsx`: *Include components* switch, stacked `BarChart` per year, table with totals); asset detail shows "from its category" when the cost per hour is the category's; category form already has the field (Task 2); locales
- Test: `server/FourC.Cmms.Tests/AssetCostTests.cs`

**Interfaces:**
- Consumes: Task 1 `CostMath`, Task 9 `CostLinesAsync`, `DowntimeMath.Measure` (as the Downtime log uses it).

- [ ] **Step 1: Write the failing tests** — `Costs_per_year_add_labour_parts_other_and_downtime` (costs in two years; a stop of 10 running hours; category cost 20/h, asset none → downtime cost 200; asset 50/h → 500); `Components_are_included_when_asked` (child and grandchild costs); `Costs_of_an_asset_on_another_site_are_not_found`; `Reliability_uses_the_category_cost`.
- [ ] **Step 2: Run** `wt-test AssetCostTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "FullyQualifiedName~AssetCostTests|FullyQualifiedName~ReportTests"`, `wt-web` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): asset costs by year with downtime cost; category downtime cost`.

### Task 12: Logo and print layouts

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/AdminEndpoints.cs` (`POST /api/settings/logo` (multipart, one file), `DELETE /api/settings/logo` — `settings.manage`; `GET /api/settings/logo` — any signed-in user, `Cache-Control: private, max-age=86400`, 404 when none; upload sets `LogoVersion` = Unix ms)
- Create: `web/src/components/PrintHeader.tsx` (logo `?v={logoVersion}`, organization name, title, subtitle lines, "Printed {date} by {name}"), `web/src/pages/reports/printFilters.ts` + `.test.ts`
- Modify: `ReportsPage.tsx` (print header with filters; button *Print or save as PDF*; print CSS: A4 landscape, `thead { display: table-header-group }`, chart at a fixed print width), `WorkOrderPrintPage.tsx` (logo via `PrintHeader`), `SettingsPage.tsx` organization logo upload/remove; `api/types.ts` (`settings.logoVersion`); locales
- Test: `server/FourC.Cmms.Tests/LogoTests.cs`

**Interfaces:**
- Produces (web): `printFilters(def: ReportDef, q: Record<string, string | undefined>, names: { site?: string; asset?: string }, t: TFunction): string[]` — one line per chosen filter (period, site, group by label, year, asset and "with components").

- [ ] **Step 1: Write the failing tests** — `Logo_upload_replace_and_remove` (PNG saved, version set; a second upload replaces it; delete → 404 on GET, version null); `Logo_type_and_size_are_checked` (a `.gif` → "Choose a PNG, JPEG or WebP image."; 1 MB + 1 byte → "The logo can be at most 1 MB."); `Logo_needs_settings_manage_to_change` (technician POST → 403, GET → 200); `Another_organizations_logo_is_not_served`; vitest `printFilters` for a report with every parameter kind.
- [ ] **Step 2: Run** `wt-test LogoTests`, `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** both and `wt-web` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): organization logo; print or save every report as a PDF with logo and filters`.

### Task 13: Arabic, guide and docs

**Files:**
- Modify: `server/FourC.Cmms.Api/Localization/ar.json` (every new server message, report title, column, KPI and group label)
- Modify: `server/FourC.Cmms.Tests/FailureCodeModelTests.cs` (theory `New_messages_have_Arabic` with one row per new text)
- Create: `docs/FAILURE_ANALYSIS.md` (codes and ordering, the reports, asset costs, logo and printing, import)
- Modify: `README.md` (feature rows), `docs/TECHNICIAN_APP.md` (codes on the phone)

- [ ] **Step 1: Write the failing test** — the theory rows (each English text translates to something different that keeps its placeholders).
- [ ] **Step 2: Run** `wt-test FailureCodeModelTests` — Expected: FAIL for missing translations.
- [ ] **Step 3: Add** the Arabic and the docs.
- [ ] **Step 4: Run** the full suites: `wt-test`, `wt-vitest`, `wt-web` — Expected: all PASS.
- [ ] **Step 5: Commit** `feat(0.14.0): Arabic for the new texts; failure analysis guide, README and technician app docs`.

---

## Deferred minors (final review)

Found by the final review and left for a later release:

- M5 logo GET sends no nosniff / CSP sandbox headers (type already checked by first bytes).
- M6 the phone job can't name a code made inactive (the bundle lists active codes only).
- M7 treeRows drops rows whose level is text (no chart uses such rows today).
- M8 the print header repeats the period (and the asset on Asset work history).
- M9 the report year box sends a request per keystroke and flashes the year error while typing.
- M11 no site-limited user tests for Responsiveness and the PM annual plan (they rely on the global filters).
- M12 Asset work history adds each work order's downtime hours, so a stop shared by two work orders counts twice.
- M13 the Failure analysis report still says "Failure code" and "Not coded".
