# Spare parts per asset and downtime tracking (0.8.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each asset gets a spare-parts list (where-used, picked first on its work orders) and a log of real stops (from requests, work orders and by hand), with availability measured against the asset's own running time.

**Architecture:**
- Two new tenant tables: `DowntimeEvents` and `AssetParts`.
- Two new asset columns: running hours a day and running days.
- Requests and work orders point at their stop through `DowntimeEventId`, so several records can share one stop.
- A pure `DowntimeMath` in Core turns stops into planned and downtime hours per period. The reliability report, the asset's Downtime tab and a new Downtime log report all use it.
- Asset parts reuse the import's list-sheet pattern. Its review logic moves into a generic base shared with PM steps and parts.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, xUnit (`SkippableFact` + `ApiFixture`); React 19, Mantine 8, TanStack Query, i18next, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-21-parts-downtime-0.8.0-design.md`

**Commands** (scratchpad helpers; `B=/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad/bin`, and `export DEVELOPER_DIR=/Library/Developer/CommandLineTools` for git):
- server tests: `$B/wt-test "FullyQualifiedName~<Class>"` (whole suite: `$B/wt-test`)
- web tests: `$B/wt-vitest`; build: `$B/wt-web`
- new migration: `$B/wt-ef migrations add PartsAndDowntime`

## Global Constraints

- No new package. Arabic for every new text: `web/src/locales/ar.json` and `server/FourC.Cmms.Api/Localization/ar.json`.
- Times are stored in UTC, and shown and split into days in the organization's time zone (`TenantSettings.TimeZone`).
- One migration, `PartsAndDowntime`. Existing assets get running hours 24 and all seven days.
- One open stop per asset, enforced by a filtered unique index (`EndedAt IS NULL AND IsDeleted = 0`).
- Permissions:
  - manual stops, stop edits and parts lists need `assets.edit`;
  - "Back running" needs `workorders.execute`;
  - the Downtime log report needs `workorders.view`;
  - the dashboard tile shows with `assets.view`.
- Rule limits: running hours 0.5–24 and at least one running day; stop reason up to 500 characters; parts quantity above 0 with three decimals; parts note up to 200 characters.
- Screens and the import share their rules, as in 0.7.0. The screens behave as before except for the new features. The import's existing sheets and messages stay word for word.
- **Ruling (plan):** CMX has no way to edit a request (no PUT; a request is approved, rejected or cancelled). So the tick box goes on the new-request form and on the new and edit work order forms. The spec's "request edit" and "change a request's asset" cases don't arise.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A stop crossing midnight in the organization's time zone (Africa/Cairo) is split by local days, not UTC days. Test: `Days_split_in_the_organizations_time_zone` (Task 1).
2. A stop still open at the end of a report period counts only up to the period end, or up to now if that is earlier. Test: `An_open_stop_counts_up_to_the_end_of_the_period` (Task 1).
3. A stop that began before the period counts only from the period start. Test: `A_stop_before_the_period_counts_only_inside_it` (Task 1).
4. "Back running" pressed when the asset isn't down, for example by a second person right after the first, is refused with "The asset isn't down." and changes nothing. Test: `Back_running_when_not_down_is_refused` (Task 2).
5. Running time with no days, or hours outside 0.5–24, is refused on the asset form and in the import. Test: `Running_time_must_make_sense` (Task 3).

---

### Task 1: Model, migration and DowntimeMath

**Files:**
- Create: `server/FourC.Cmms.Core/Assets/Downtime.cs` (entities, `DowntimeMath`)
- Modify:
  - `server/FourC.Cmms.Core/Assets/Assets.cs` (asset running time; `AssetPart`);
  - `server/FourC.Cmms.Core/Work/Work.cs` (`DowntimeEventId` on `WorkRequest` and `WorkOrder`);
  - `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (sets, mapping, indexes).
- Create: the migration `PartsAndDowntime`
- Test: `server/FourC.Cmms.Tests/DowntimeMathTests.cs` (plain `[Fact]`s)

**Interfaces:**
- Produces:
  - `public enum DowntimeSource { Request = 1, WorkOrder = 2, Manual = 3 }`.
  - `public class DowntimeEvent : TenantEntity`, with:
    - `Guid AssetId`, `Asset? Asset`;
    - `DateTimeOffset StartedAt`, `DateTimeOffset? EndedAt`;
    - `string? Reason` (500);
    - `DowntimeSource Source`, `string? SourceNumber` (50, the request or work order number that started it);
    - `Guid? EndedById`;
    - "who logged it" is `CreatedById`, which `TenantEntity` already has.
  - `public class AssetPart : TenantEntity { Guid AssetId; Asset? Asset; Guid PartId; Part? Part; decimal Quantity; string? Note; }`, with a unique (TenantId, AssetId, PartId) where not deleted, and decimal(18,3).
  - On `Asset`: `decimal RunsHoursPerDay = 24` (decimal(4,2)) and `List<DayOfWeek> RunsOn` (all seven by default, stored as JSON like `TenantSettings.PmNonWorkingDays`).
  - `Guid? DowntimeEventId` on `WorkRequest` and `WorkOrder`.
  - `public sealed record StopSpan(DateTimeOffset Start, DateTimeOffset End)`.
  - `public sealed record DowntimeMeasure(decimal PlannedHours, decimal DownHours)`.
  - `public static class DowntimeMath`, with
    `static DowntimeMeasure Measure(IEnumerable<StopSpan> stops, IEnumerable<(DateOnly Day, decimal Hours)> typedHours, DateOnly from, DateOnly to, DateTimeOffset now, decimal hoursPerDay, IReadOnlyCollection<DayOfWeek> runsOn, TimeZoneInfo tz)`.
    Its steps:
    1. Clip each stop to [the start of `from`, the end of `to` or `now`, whichever is earlier] in `tz`.
    2. Merge overlapping stops.
    3. Split the stopped time by local day.
    4. For each running day, add any typed hours for that day, then cap at `hoursPerDay`.
    5. Planned hours = running days × `hoursPerDay`.
    6. Round both to 2 decimals.

- [ ] **Step 1: Write the failing tests** in `DowntimeMathTests` (UTC unless named):
  - `Only_running_days_count`. 16 h a day, Sunday–Thursday, a stop from Thursday 2026-11-19 20:00 to Sunday 2026-11-22 08:00, period 2026-11-15 → 2026-11-21. Expected: planned = 5 × 16 = 80, down = 4 (Thursday only; Friday and Saturday aren't running days, and Sunday the 22nd is outside the period).
  - `Overlapping_stops_count_once`. Stops 10:00–14:00 and 12:00–16:00 on one running day: down = 6.
  - `Typed_hours_count_on_their_day_and_are_capped`. Typed (2026-11-19, 30 h), 24 h a day: down = 24.
  - `Days_split_in_the_organizations_time_zone`. Africa/Cairo (UTC+2). A stop from 2026-11-18 21:00Z to 2026-11-19 03:00Z is 23:00–05:00 local. Period: just the 19th. Expected: down = 5.
  - `An_open_stop_counts_up_to_the_end_of_the_period`. A stop from 2026-11-20 10:00 with no end, `now` = 2026-11-20 12:00, period to the 20th: down = 2.
  - `A_stop_before_the_period_counts_only_inside_it`. A stop from 2026-11-01 00:00 to 2026-11-16 06:00, period from the 16th: down = 6.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeMathTests"`. Expected: build errors.
- [ ] **Step 3: Implement** the entities, mapping and `DowntimeMath`, then add the migration with `$B/wt-ef migrations add PartsAndDowntime`. It holds two tables, the asset columns with defaults, the two FKs and the filtered unique index on open stops.
- [ ] **Step 4: Run** the same command. Expected: 6 passed. Then `$B/wt-test`. Expected: the existing suite is green with the migration applied.
- [ ] **Step 5: Commit** `"Downtime and asset parts model, DowntimeMath"`.

### Task 2: Stops from requests and work orders, and "Back running"

**Files:**
- Create: `server/FourC.Cmms.Api/Features/DowntimeEndpoints.cs` (the shared `Downtime` helpers and the asset downtime endpoints)
- Modify:
  - `WorkRequestEndpoints.cs` (`RequestInput.AssetIsDown`; create; approve links the work order);
  - `WorkOrderEndpoints.cs` (`WorkOrderInput.AssetIsDown`; create and update tick/untick and asset change; `ChangeStatusAsync` ends the stop on completion and fills the completion fields; the detail DTO gains `downtime`);
  - `Program.cs` (map);
  - `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/DowntimeTests.cs`

**Interfaces:**
- Consumes: Task 1 entities.
- Produces:
  - `internal static class Downtime`, with:
    - `Task<DowntimeEvent> StartOrJoinAsync(CmmsDbContext db, Guid assetId, DateTimeOffset at, string? reason, DowntimeSource source, string sourceNumber, CancellationToken ct)`, which returns the asset's open stop, or a new one;
    - `Task<bool> EndOpenAsync(CmmsDbContext db, ITenantContext ctx, Guid assetId, DateTimeOffset at, CancellationToken ct)`, which returns false when nothing is open;
    - `Task UnlinkAsync(CmmsDbContext db, Guid stopId, string sourceNumber, CancellationToken ct)`, which deletes the stop when it is open, its `SourceNumber` is this record's number, and no other request or work order points at it.
  - `RequestInput` and `WorkOrderInput` gain a trailing `bool AssetIsDown = false`.
  - The work order detail gains `downtime: { id, startedAt, endedAt, source, sourceNumber } | null` and `assetDownSince: DateTimeOffset | null`.
  - `POST /api/assets/{id}/back-running` needs `workorders.execute`. It answers 204, or 400 "The asset isn't down."
- Rules:
  - **Creation:** the stop starts at the record's `CreatedAt`, and its reason is the record's title.
  - **Ticking on a work order edit:** the stop starts at the time of the edit.
  - **Approve:** the new work order gets the request's `DowntimeEventId`.
  - **Completion in `ChangeStatusAsync`:**
    - it ends the linked stop if it's open, at `w.CompletedAt` as that method sets it (`ActualEnd ?? now`), but never before the stop's start;
    - it sets `AssetWasDown = true` and `DowntimeHours` = the stop's hours (2 decimals), ignoring the typed values;
    - this also covers the technician app's completion, which goes through the same method.
  - **Untick, or asset changed on update:** `UnlinkAsync` with the work order's number, then clear `DowntimeEventId`.

- [ ] **Step 1: Write the failing tests** in `DowntimeTests`:
  - `A_request_that_says_down_starts_a_stop_and_its_work_order_ends_it`.
    - Create a request with `assetIsDown = true`: the asset's downtime has an open stop started at the request's `createdAt`, source Request, number REQ-….
    - Approve it: the work order's `downtime.id` is the same stop.
    - Complete it: the stop has `endedAt`, and the work order's `assetWasDown` is true with `downtimeHours` equal to the stop's hours.
  - `A_second_down_report_joins_the_open_stop`. Two requests on one asset with `assetIsDown`: one stop, and both requests carry its id.
  - `Back_running_ends_the_stop_early`. `POST back-running` gives 204; the stop is ended, and the work order completed later doesn't change the end.
  - `Back_running_when_not_down_is_refused`. Gives 400 "The asset isn't down."
  - `Paperwork_does_not_end_a_stop`. Rejecting the request, or cancelling its work order, leaves the stop open.
  - `Unticking_a_mistake_removes_its_stop`. A work order created with `assetIsDown = true`, then updated with `false`: the stop is gone. When a request also points at the stop, it stays.
  - `Changing_the_asset_leaves_the_stop_on_the_old_one`. The work order's asset changes: the stop stays open on the old asset, and the work order's `downtime` is null.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeTests"`. Expected: failures (`assetIsDown` is ignored).
- [ ] **Step 3: Implement** the helpers, the endpoint changes and the back-running endpoint; add "The asset isn't down." to `ar.json`.
- [ ] **Step 4: Run** the same command. Expected: 7 passed. Then `$B/wt-test`. Expected: green.
- [ ] **Step 5: Commit** `"Stops from requests and work orders, Back running"`.

### Task 3: Manual stops, the Downtime tab's data, running time, list and dashboard

**Files:**
- Modify:
  - `DowntimeEndpoints.cs`;
  - `AssetEndpoints.cs`:
    - `AssetInput` gains trailing `decimal? RunsHoursPerDay = null, List<DayOfWeek>? RunsOn = null` (null keeps the current value, or the default on create);
    - `AssetRules` adds the running-time rules;
    - `AssetFilter.DownNow`;
    - list items gain `downSince`;
  - `AdminEndpoints.cs` (dashboard `assetsDown`);
  - `TechEndpoints.cs` (the job's asset gains `downSince`);
  - `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/DowntimeTests.cs` (more tests)

**Interfaces:**
- Consumes: Task 1 `DowntimeMath`, Task 2 `Downtime`.
- Produces:
  - `GET /api/assets/{id}/downtime` (`assets.view`) returns `{ downSince, runsHoursPerDay, runsOn, availability30, plannedHours30, downHours30, stops: [{ id, startedAt, endedAt, hours, reason, source, sourceNumber, links: [{ kind: "request" | "workOrder", id, number }] }] }`, newest first.
    - `links` are the requests and work orders pointing at the stop, so the tab can link to them.
    - The 30 days are counted the way the reliability report counts them, so old work orders' typed hours are included.
    - Availability is a percentage with 1 decimal, or null when no planned hours.
  - The technician job's `asset` gains `downSince`.
  - `POST /api/assets/{id}/downtime` and `PUT /api/downtime/{id}` (`assets.edit`) take `{ startedAt, endedAt?, reason }`. Messages:
    - "The end must be after the start.";
    - "The start can't be in the future.";
    - "This stop overlaps another stop of this asset.";
    - "The asset is already down." (a second open stop).
  - Asset list `?downNow=true`; dashboard `assetsDown: number`.
  - Running-time messages: "Running hours must be between 0.5 and 24." and "Choose at least one running day."

- [ ] **Step 1: Write the failing tests:**
  - `Manual_stops_are_checked_and_listed`. Add a closed stop, add an overlapping one (400 overlap), add one with end before start (400), then edit the first one's reason. The list shows the stops newest first, and the change history (`/api/audit?entityType=DowntimeEvent&entityId=…`) has the edit.
  - `Availability_uses_the_assets_running_time`. Set the asset to 8 h a day on all days and add a manual stop of 4 h yesterday: `plannedHours30` = 240, `downHours30` = 4, `availability30` = 98.3.
  - `Running_time_must_make_sense`. `runsHoursPerDay = 25` gives 400 with the hours message; `runsOn = []` gives 400 with the days message.
  - `Down_assets_show_in_the_list_and_on_the_dashboard`. One asset down: `?downNow=true` returns it with `downSince`, the dashboard's `assetsDown` = 1, and its job in the technician bundle (`GET /api/tech/bundle`) shows `asset.downSince`.
  - `A_stop_links_to_its_records`. A stop started by a request and joined by its work order lists both in `links`.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeTests"`. Expected: the new tests fail.
- [ ] **Step 3: Implement** the endpoints and the asset changes, with the messages in `ar.json`.
- [ ] **Step 4: Run** the same command. Expected: all pass. Then `$B/wt-test`. Expected: green.
- [ ] **Step 5: Commit** `"Manual stops, availability, running time, down list and tile"`.

### Task 4: Reports: reliability on stops and the Downtime log

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/ReportEndpoints.cs`, `Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/DowntimeReportTests.cs`

**Interfaces:**
- Consumes: Task 1 `DowntimeMath`.
- The **reliability report**:
  - per asset: breakdowns (stops starting in the period, plus old work orders with `AssetWasDown`, no `DowntimeEventId` and completed in the period);
  - downtime and planned hours from `DowntimeMath`;
  - MTTR = downtime ÷ breakdowns; MTBF = (planned − downtime) ÷ breakdowns; availability; downtime cost;
  - the columns stay as they are.
- The new report `"downtime"`, titled "Downtime log": "Every stop in the period with how long the asset was stopped during its running time." Columns: Asset, Start, End, Hours (within running time), Reason, From. Parameters: the date range and site.

- [ ] **Step 1: Write the failing tests:**
  - `Reliability_counts_stops_and_running_time`. An asset at 12 h a day with a 6 h stop and an old-style completed work order (`assetWasDown`, 3 h typed, no stop): breakdowns 2, downtime 9, MTTR 4.5.
  - `The_downtime_log_lists_each_stop`. Two stops: two rows, with reason and source number.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeReportTests"`. Expected: failures.
- [ ] **Step 3: Implement**, and add the new report texts to `ar.json`.
- [ ] **Step 4: Run** the same command. Expected: 2 passed. Then `$B/wt-test "FullyQualifiedName~Report"`. Expected: green.
- [ ] **Step 5: Commit** `"Reliability on stops and running time, Downtime log report"`.

### Task 5: Spare parts per asset

**Files:**
- Modify:
  - `AssetEndpoints.cs` (parts list endpoints; asset delete removes its list);
  - `InventoryEndpoints.cs` (lookup `assetId`; part detail `usedOn`; delete guard);
  - `Localization/ar.json`.
- Test: `server/FourC.Cmms.Tests/AssetPartTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/assets/{id}/parts` (`assets.view`) returns `[{ partId, partNumber, name, unitOfMeasure, quantity, note }]`, ordered by part number.
  - `PUT /api/assets/{id}/parts` (`assets.edit`) takes `[{ partId, quantity, note }]` and makes that the whole list. Messages:
    - "List each part once.";
    - "Each part needs a quantity above zero.";
    - "Part not found.";
    - a note over 200 characters.
  - `public record AssetPartInput(Guid PartId, decimal Quantity, string? Note)`.
  - `internal static Validator AssetPartRules(IReadOnlyList<AssetPartInput> lines)`, shared with the import.
  - `POST /api/assets/{id}/parts/copy-from/{sourceId}` (`assets.edit`) adds the source's lines whose part isn't already listed.
  - `GET /api/parts/{id}` gains `usedOn: [{ assetId, assetNumber, assetName, quantity, note }]`.
  - `GET /api/parts/lookup?assetId=` returns the asset's parts first (each with `onAsset: number`), then the other matches; `search` filters both.
  - Deleting a listed part: "This part is on assets' spare-parts lists. Remove it from them, or mark it inactive instead."

- [ ] **Step 1: Write the failing tests:**
  - `An_asset_gets_its_parts_list`. Put [P1×2, P2×1]: get returns 2 lines. Put [P1×3]: get returns only P1×3 (edit and removal). Put a duplicate: 400 "List each part once.".
  - `Copy_adds_the_other_assets_parts`. A has [P1×2], B has [P1×5, P2×1]. Copy B into A: A has P1×2 (kept) and P2×1.
  - `Parts_show_where_they_are_used`. P1's `usedOn` lists A and B.
  - `The_picker_offers_the_assets_parts_first`. A lookup with `assetId = A` and no search returns P1 first, with `onAsset = 2`.
  - `A_listed_part_cannot_be_deleted`. Gives 400 with the message.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~AssetPartTests"`. Expected: failures.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the same command. Expected: 5 passed. Then `$B/wt-test`. Expected: green.
- [ ] **Step 5: Commit** `"Spare parts per asset"`.

### Task 6: Import and export: running time and the Asset parts sheet

**Files:**
- Modify:
  - `Features/Import/ImportSheets.cs`:
    - Assets gains `N("runsHoursPerDay", "Runs hours a day", "ساعات التشغيل في اليوم")` and `L("runsOn", "Runs on", "يعمل في")`;
    - a new sheet `AssetParts` ("Asset parts", «قطع الأصول»), placed right after `Parts`, with columns Asset number*, Part number*, Quantity*, Note;
  - `Features/Import/SheetImporters.cs`:
    - extract `ListSheetImporter` from `PmTaskListImporter` (Review, block on problem rows, removed-items change);
    - `PmTaskListImporter` derives from it;
    - add `AssetPartImporter`;
  - `Features/Import/ImportWorkbook.cs`:
    - Asset parts rows ordered by asset number then part number;
    - `WholeTasks` becomes `WholeLists(rows, max, ownerKey)`, used for PM steps, PM parts and Asset parts;
    - assets export the two new columns;
  - `web/src/locales/en.json` and `ar.json` (`import.sheets.AssetParts`).
- Test: `server/FourC.Cmms.Tests/ImportAssetPartTests.cs`

**Interfaces:**
- Consumes: Task 5 `AssetPartRules`, Task 3 running-time rules (through `AssetRules`).
- Produces:
  - `internal abstract class ListSheetImporter : ISheetImporter` with:
    - `abstract string OwnerColumn`;
    - `abstract Guid? OwnerOf(ImportContext c, ImportRow row)`;
    - `abstract void Forget(Guid ownerId)`;
    - `abstract IEnumerable<(Guid OwnerId, FieldChange Change)> ListChanges(ImportContext c)`;
    - `abstract string BlockedMessage` (a format with `{0}` for the row);
    - the shared `Review`.
  - `PmTaskListImporter` keeps "Not saved: fix row {0} of this task first." word for word. `AssetPartImporter` uses "Not saved: fix row {0} of this asset first.", added to `ar.json`. Both go through `Translator.T` as today.
  - `runsOn` cells are weekday words (English or Arabic), read through the `DayOfWeek` choice words. An unknown word gives the usual "must be one of" message.

- [ ] **Step 1: Write the failing tests:**
  - `Asset_parts_load_and_replace_per_asset`. Asset A with P1×2 and P2×1. A second file with only P1×3: A has [P1×3], and the preview shows A changed with "Parts: P1 × 2; P2 × 1 → P1 × 3".
  - `A_problem_row_keeps_the_assets_list`. An unknown part for A: A keeps its list, and its other rows say "Not saved: fix row N of this asset first.".
  - `Running_time_imports_and_exports`. Assets row with "Runs hours a day" 16 and "Runs on" "Sunday, Monday, الثلاثاء": saved as 16 h on 3 days. Current data with an Asset parts list imports back with no changes.
  - The existing `ImportPmTests` stay unchanged and must still pass.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportAssetPartTests|FullyQualifiedName~ImportPmTests"`. Expected: failures.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the same command. Expected: all pass. Then `$B/wt-test "FullyQualifiedName~Import"`. Expected: green (round trip included).
- [ ] **Step 5: Commit** `"Import: running time and Asset parts sheet"`.

### Task 7: Web screens and the technician app

**Files:**
- Create:
  - `web/src/pages/assets/AssetDowntime.tsx` (the Downtime tab);
  - `web/src/pages/assets/AssetParts.tsx` (the Parts tab with copy-from);
  - `web/src/components/downtime.ts` and `web/src/components/downtime.test.ts`.
- Modify:
  - `AssetDetailPage.tsx` (two tabs, a "Down" badge in the header);
  - `AssetFormPage.tsx` (running time);
  - `AssetsPage.tsx` (badge; "Down now" filter);
  - `DashboardPage.tsx` (tile);
  - the request form in `WorkRequestsPage.tsx` and `WorkOrderFormPage.tsx` (tick box, shown when an asset is chosen);
  - `WorkOrderDetailPage.tsx` ("Back running" when the asset is down; completion fields read-only with "From the asset's downtime log" when `downtime` is set);
  - `PartDetailPage.tsx` ("Used on");
  - `components/PartPicker.tsx` (`assetId` prop; groups "Parts of this asset" first);
  - `components/StockTxModal.tsx` and `pages/work/WorkOrderPlanning.tsx` (pass the work order's asset);
  - `tech/JobPage.tsx` ("Back running" when the job's asset is down; online only, disabled offline with a hint);
  - `api/types.ts`;
  - `locales/en.json` and `ar.json`.
- Test: `web/src/components/downtime.test.ts`

**Interfaces:**
- Consumes: Tasks 2–5 endpoints.
- Produces:
  - in `downtime.ts`:
    - `export function downFor(since: string, now: Date): string`, which uses `span` from `sla.ts`;
    - `export function groupPicked<T extends { onAsset?: number | null }>(items: T[]): { onAsset: T[]; others: T[] }`.

- [ ] **Step 1: Write the failing tests** in `downtime.test.ts`:
  - `downFor says how long in plain units`: since 2 h 15 min ago gives `"2 h 15 min"` (English), and the Arabic unit letters after `applyLanguage('ar')`.
  - `groupPicked puts the asset's parts first`: `[{id:'a'},{id:'b',onAsset:2}]` gives `onAsset` = [b] and `others` = [a].
- [ ] **Step 2: Run** `$B/wt-vitest`. Expected: the new file fails.
- [ ] **Step 3: Implement** the helpers, the screens and both locale files (every key in both).
- [ ] **Step 4: Run** `$B/wt-vitest`, then `$B/wt-web`. Expected: green and `✓ built`.
- [ ] **Step 5: Check in the browser.** Run a local preview (port 8096, its own database, sample data).
  - Tick "The asset is down" on a request and approve it: the asset shows "Down since…".
  - Complete the work order: the stop ends.
  - Add a manual stop; set running time; check availability.
  - Edit a parts list and copy from another asset; issue a part from a work order and see "Parts of this asset" first.
  - Do the same in Arabic.
  - Then remove the preview.
- [ ] **Step 6: Commit** `"Downtime and spare parts screens, technician Back running"`.

### Task 8: Docs and the whole branch

**Files:**
- Modify: `CHANGELOG.md` (`## [0.8.0] - <date>`, Added: spare parts per asset, downtime log, running time, availability, Downtime log report, Asset parts import sheet), `README.md` (a feature row)

- [ ] **Step 1: Write** the CHANGELOG and README entries.
- [ ] **Step 2: Run** `$B/wt-test`, `$B/wt-vitest` and `$B/wt-web`. Expected: all green.
- [ ] **Step 3: Commit** `"Docs for 0.8.0"`.
