# Multi-asset PM schedules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A PM schedule can cover an ordered list of assets ("round"); each cycle generates one work order whose checklist repeats the task's steps per asset, every listed asset's history shows it, and the 4CSQL importer brings MEG's rounds and step results across.

**Architecture:** Two join tables (`PmScheduleAsset`, `WorkOrderAsset`) plus `AssetId`/`Reading`/`Remarks` on `WorkOrderTask`. Round planning (which assets, which steps, common location) is a pure static class unit-tested without a database; `PmGenerator` calls it. API changes are additive. The importer reads `PMScheduleAssets` and streams `workorderelements`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit + `SkippableFact` integration tests against a throwaway database, React 19 + Mantine 8 + TanStack Query, i18next.

**Spec:** `docs/superpowers/specs/2026-11-18-multi-asset-pm-design.md` — read it first; this plan does not repeat its reasoning.

## Global Constraints

- Existing single-asset and location schedules and their work orders behave exactly as before (anti-goal).
- One work order per cycle for a round — never one per asset.
- Labour, parts, costs and downtime stay on the work order; nothing is split across assets.
- All listed assets belong to the schedule's site; no duplicates; at most **100** assets per list; type must not be `Meter`.
- `WorkOrderAsset` rows exist **only for rounds**; single-asset work orders keep `WorkOrder.AssetId`.
- `Reading` nvarchar(**100**) free text; `Remarks` nvarchar(**2000**).
- API changes are additive (new optional fields); every new query stays under the tenant filter (join tables are reached through `PmSchedules` / `WorkOrders`).
- All UI text in `web/src/locales/en.json`.

## Commands (no .NET or Node on the Mac — everything runs in containers)

The local stack must be up (`docker compose up -d` in the repo root; `.env` holds `SA_PASSWORD`).

- **Build + tests (server)** — `DT <filter>`:
  ```bash
  docker run --rm --network cmmsx_default -v "$HOME/Projects/CMMSX/server":/repo:ro -v cmmsx-nuget:/root/.nuget/packages \
    -e CMMS_TEST_SQL='Server=sql,1433;User Id=sa;Password=Local_Test_2026!;TrustServerCertificate=True;Encrypt=False' \
    mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'cp -r /repo /src && cd /src && rm -rf */bin */obj && dotnet test FourC.Cmms.slnx --filter "<filter>"'
  ```
  `DT ''` runs everything. Integration tests skip silently without `CMMS_TEST_SQL`, so check the summary says *Passed*, not *Skipped*.
- **Add a migration** (writes into the repo):
  ```bash
  docker run --rm --network cmmsx_default -v "$HOME/Projects/CMMSX/server":/src -v cmmsx-nuget:/root/.nuget/packages -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
    sh -c 'dotnet tool install -g dotnet-ef --version "10.*" >/dev/null && ~/.dotnet/tools/dotnet-ef migrations add <Name> --project FourC.Cmms.Core'
  ```
- **Web type-check + build**: `docker run --rm -v "$HOME/Projects/CMMSX/web":/repo:ro node:22-alpine sh -c 'cp -r /repo /w && cd /w && npm ci --no-audit --no-fund >/dev/null && npm run build'`
- **Run the app with the change**: `docker compose up -d --build app`, then http://localhost:8080 (local test login in `.env`).
- **Importer**: build and run as in Task 6.

## Review Focus

1. A listed asset later **moved to another site** — expected: left out of generated work orders (not a cross-site work order). Test in Task 3.
2. A listed asset later **deleted** (soft delete) or **disposed** — expected: left out; if none remain, the cycle is skipped with "No active assets in the list" and the schedule still advances only when work is created. Test in Task 3.
3. **Editing a round back to a single asset** (`assetIds: []`, `assetId` set) — expected: list cleared, behaves as a normal schedule. Test in Task 2.
4. **Remarks longer than 2000 / reading longer than 100 characters** (Arabic included) — expected: 400 with a field message, nothing truncated silently. Test in Task 4.
5. **Asset filter on the work order list** for an asset that is both the single asset of some work orders and covered by rounds — expected: both kinds listed once each, no duplicates. Test in Task 4.

---

### Task 0: Branch and commit the importer fixes already in the working tree

**Files:** already modified — `server/FourC.Cmms.LegacyImport/{Program,Legacy,PmImport}.cs`, `README.md`, `docker-compose.yml`; new `docs/superpowers/specs/2026-11-18-multi-asset-pm-design.md`, this plan.

- [ ] **Step 1:** `git switch -c multi-asset-pm` (from `main` at `c6a5cb4`).
- [ ] **Step 2:** `DT ''` — expected: build succeeds, all tests pass (no test covers the importer; this guards the build).
- [ ] **Step 3: Commit** (two commits):
  ```bash
  git add server/FourC.Cmms.LegacyImport README.md docker-compose.yml
  git commit -m "Importer: fix duplicate section/location codes, import section PMs, report skipped legacy reads; cap local SQL memory"
  git add docs/superpowers
  git commit -m "Design and plan: multi-asset PM schedules"
  ```

### Task 1: Data model and migration

**Files:**
- Modify: `server/FourC.Cmms.Core/Pm/Pm.cs` (add `PmScheduleAsset`, `PmSchedule.Assets`)
- Modify: `server/FourC.Cmms.Core/Work/Work.cs` (add `WorkOrderAsset`, `WorkOrder.Assets`; `WorkOrderTask.AssetId/Reading/Remarks`)
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (DbSets + configuration next to `PmScheduleTask`, line ~301, and `WorkOrderTask`, line ~256)
- Create: `server/FourC.Cmms.Core/Data/Migrations/<timestamp>_MultiAssetPm.cs` (generated)

**Interfaces:**
- Produces:
  - `public class PmScheduleAsset { Guid PmScheduleId; Guid AssetId; Asset? Asset; int Sequence; }` — key (`PmScheduleId`, `AssetId`), index `AssetId`, cascade from schedule, restrict from asset. `PmSchedule.Assets : List<PmScheduleAsset>`.
  - `public class WorkOrderAsset { Guid WorkOrderId; Guid AssetId; Asset? Asset; int Sequence; }` — key (`WorkOrderId`, `AssetId`), index (`AssetId`, `WorkOrderId`), cascade from work order, restrict from asset. `WorkOrder.Assets : List<WorkOrderAsset>`.
  - `WorkOrderTask.AssetId : Guid?` (FK restrict), `Reading : string?` (100), `Remarks : string?` (2000).
  - DbSets `PmScheduleAssets`, `WorkOrderAssets`.

- [ ] **Step 1:** Add the entities and configuration (follow `PmScheduleTask`: plain class, no `TenantId`, reached through its tenant-filtered parent).
- [ ] **Step 2:** Generate the migration `MultiAssetPm` (command above). Expected: one migration creating two tables and adding three nullable columns to `WorkOrderTasks`; no other diff (if the snapshot shows unrelated changes, stop and investigate).
- [ ] **Step 3:** `DT ''` — expected: all pass (the fixture migrates a fresh database, proving the migration applies).
- [ ] **Step 4: Commit** `git commit -m "PM rounds: data model and migration"`.

### Task 2: Schedule API accepts an asset list

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/PmEndpoints.cs` (`ScheduleInput`, `ValidateScheduleAsync` ~292, `ApplySchedule` ~323, GET list ~87, GET detail ~112, PUT ~157 must `Include(x => x.Assets)`)
- Test: `server/FourC.Cmms.Tests/PmRoundTests.cs` (new; copy the `Ok`/`Iso`/setup helpers pattern from `PmTests.cs`)

**Interfaces:**
- Consumes: Task 1 entities.
- Produces:
  - `ScheduleInput` gains trailing optional `List<Guid>? AssetIds = null` (order = sequence).
  - Detail JSON adds `assets: [{ id, assetNumber, name }]` (ordered; `[]` for non-rounds).
  - List JSON adds `assetCount: int` (0 for non-rounds); `assetId` filter also matches `x.Assets.Any(a => a.AssetId == assetId)`.
  - Validation keys/messages: `assetIds` — "Assets must belong to the selected site." / "An asset is listed twice." / "List at most 100 assets." / "Meter schedules cannot cover several assets."; with a non-empty list `AssetId` must be null — `assetId`: "Choose either one asset or several assets." The existing "Choose the asset or location to maintain." accepts a non-empty list as a target.

- [ ] **Step 1: Write the failing tests** in `PmRoundTests`:
  - `Round_schedule_saves_its_ordered_asset_list` — create site, assets A, B, C (site S), task; POST schedule with `assetIds = [C, A, B]`, no `assetId`; GET detail → `assets` numbers in order C, A, B; GET `/api/pm-schedules?assetId={A}` → contains the schedule with `assetCount == 3`.
  - `Round_schedule_rejects_bad_lists` — each returns 400 with key `assetIds` (or `assetId`): asset from another site; `[A, A]`; 101 random `Guid.NewGuid()` values (the count check runs before existence checks, so this gives "List at most 100 assets."); `type = "Meter"` with a list; list plus `assetId`.
  - `Round_can_be_turned_back_into_a_single_asset_schedule` — PUT with `assetIds = []`, `assetId = A` → detail `assets` empty, `assetId == A`.
- [ ] **Step 2:** `DT 'FullyQualifiedName~PmRoundTests'` — expected: FAIL (unknown property / empty `assets`).
- [ ] **Step 3:** Implement: input record, validation (count check first), `ApplySchedule` syncs `s.Assets` to the input order (remove missing, add new, renumber `Sequence` 1..n), list projection `AssetCount = x.Assets.Count`, detail projection ordered by `Sequence`.
- [ ] **Step 4:** `DT 'FullyQualifiedName~Pm'` — expected: PASS (new and existing PM tests).
- [ ] **Step 5: Commit** `git commit -m "PM rounds: schedules accept an ordered asset list"`.

### Task 3: Generation creates one work order per round

**Files:**
- Create: `server/FourC.Cmms.Core/Pm/PmRound.cs` (pure, no EF)
- Modify: `server/FourC.Cmms.Api/Features/PmGenerator.cs` (`CreateWorkOrderAsync` ~84, the loads at ~36 and ~112 must `Include(s => s.Assets)`)
- Test: `server/FourC.Cmms.Tests/PmCalendarTests.cs` (unit), `server/FourC.Cmms.Tests/PmRoundTests.cs` (integration)

**Interfaces:**
- Produces (in `FourC.Cmms.Core.Pm`):
  - `public sealed record RoundAsset(Guid Id, int Sequence, Guid SiteId, string? LocationPath, Guid? LocationId, bool Available);` — `Available` = active, not disposed, not deleted.
  - `public sealed record RoundStep(Guid AssetId, int Sequence, string Description);`
  - `public static class PmRound`
    - `IReadOnlyList<RoundAsset> Covered(IEnumerable<RoundAsset> listed, Guid siteId)` — available assets of `siteId`, ordered by `Sequence`.
    - `IReadOnlyList<RoundStep> Steps(IReadOnlyList<RoundAsset> covered, IReadOnlyList<string> taskSteps)` — for each covered asset in order, every task step; `Sequence` continuous from 1.
    - `Guid? CommonLocationId(IReadOnlyList<RoundAsset> covered)` — deepest location whose `Path` prefixes every covered asset's `LocationPath` (paths are `/<guid>/<guid>/…/`; the id is the last segment of the common prefix); null if any asset has no location or they share none.
- Consumes: `PmRound` from `PmGenerator`; `WorkOrderEndpoints.CreateAsync(..., configure)` unchanged.
- Skip reason string (exact): `"No active assets in the list"`.

- [ ] **Step 1: Write failing unit tests** (`PmCalendarTests.cs`, plain `[Fact]`):
  - `Round_covers_available_assets_of_the_site_in_list_order` — inputs with sequences 3,1,2, one unavailable, one of another site → ids in sequence order, excluding both.
  - `Round_steps_repeat_per_asset_with_continuous_sequence` — 2 assets × steps ["Look","Listen"] → 4 steps, sequences 1..4, asset ids A,A,B,B.
  - `Round_common_location_is_the_deepest_shared_path` — paths `/x/y/a/` and `/x/y/b/` → `y`; `/x/` and `/z/` → null; one null path → null.
- [ ] **Step 2:** `DT 'FullyQualifiedName~PmCalendarTests'` — expected: FAIL (`PmRound` missing).
- [ ] **Step 3:** Implement `PmRound`.
- [ ] **Step 4:** `DT 'FullyQualifiedName~PmCalendarTests'` — expected: PASS.
- [ ] **Step 5: Write failing integration tests** (`PmRoundTests`):
  - `Round_generates_one_work_order_with_steps_per_asset` — round [A, B], task with steps ["Look","Listen"], start today, lead 0 → generate until today: `created == 1`; WO detail: `assetId` null, `assets` = [A, B], 4 tasks whose `assetId` sequence is A,A,B,B.
  - `Round_leaves_out_unavailable_and_moved_assets` — round [A, B, C]; dispose B (PUT asset status `Disposed`), move C to another site → WO `assets` = [A]. Then round [D] with D deleted → generate result for it has `skipped == "No active assets in the list"` and no work order.
  - `Round_rotates_the_heavier_task_like_single_asset_schedules` — extra task every 2nd → second generated WO's tasks use the monthly task's steps per asset.
- [ ] **Step 6:** `DT 'FullyQualifiedName~PmRoundTests'` — expected: FAIL.
- [ ] **Step 7:** Implement. In `GenerateForAsync`, for a round load its `RoundAsset`s (no `IgnoreQueryFilters` — deleted assets must drop out); if `PmRound.Covered` is empty, add a `Result` with the skip reason and stop for this schedule without advancing `NextDueDate` (same handling as "Previous PM work order is still open"). In `CreateWorkOrderAsync` for a round: call `CreateAsync` with `AssetId = null`, `LocationId = s.LocationId ?? PmRound.CommonLocationId(covered)`, `Tasks = null`, and in `configure` add the `WorkOrderAsset` rows and the `WorkOrderTask` rows from `PmRound.Steps` (with `AssetId`, `Sequence`). Single-asset path unchanged.
- [ ] **Step 8:** `DT 'FullyQualifiedName~Pm'` — expected: PASS.
- [ ] **Step 9: Commit** `git commit -m "PM rounds: generate one work order with steps per asset"`.

### Task 4: Work order API — covered assets, step readings and remarks, asset filters

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (`TaskInput` line 20, detail ~98–125, task PUT ~252, open-work ~224, list filter ~368)
- Test: `server/FourC.Cmms.Tests/PmRoundTests.cs`

**Interfaces:**
- Produces:
  - `TaskInput(string? Description, bool? IsDone, string? Reading = null, string? Remarks = null)`; PUT sets each field when non-null (empty string clears it); 400 keys `reading` ("Reading can be at most 100 characters.") and `remarks` ("Remarks can be at most 2000 characters.").
  - Detail JSON: `assets: [{ id, assetNumber, name }]` ordered by `Sequence`; each task adds `assetId`, `reading`, `remarks`.
  - List and open-work: asset match = `w.AssetId == X || w.Assets.Any(a => a.AssetId == X)` (list keeps its existing child-asset clause).

- [ ] **Step 1: Write failing tests:**
  - `Round_work_order_shows_in_each_assets_history_and_open_work` — after generating a round [A, B]: `/api/work-orders?assetId={B}` contains it; `/api/assets/{B}/open-work` lists it; a separate single-asset WO on A plus the round → `/api/work-orders?assetId={A}` returns both, each once (Review Focus 5).
  - `Steps_record_readings_and_remarks` — PUT task with `reading = "72 °C"`, `remarks = "تسريب بسيط"` → detail echoes both; `reading` of 101 chars → 400 key `reading`; `remarks` of 2001 chars → 400 key `remarks` (Review Focus 4).
- [ ] **Step 2:** `DT 'FullyQualifiedName~PmRoundTests'` — expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `DT ''` — expected: all PASS (including `WorkTests`, proving single-asset behaviour unchanged).
- [ ] **Step 5: Commit** `git commit -m "PM rounds: work order assets, step readings and remarks, asset filters"`.

### Task 5: Web — schedule form, PM list, work order checklist, asset page

**Files:**
- Modify: `web/src/api/types.ts` (`PmScheduleDetail.assets`, `PmScheduleListItem.assetCount`, `WorkOrderDetail.assets`, task `assetId/reading/remarks`)
- Modify: `web/src/pages/pm/PmScheduleFormPage.tsx` (Target section ~125: segmented control *Asset · Location · Several assets*; form value `assetIds: string[]`; hide meter type when several)
- Create: `web/src/components/AssetListEditor.tsx` — `AssetListEditor({ value: AssetRef[], onChange(next: AssetRef[]), siteId: string | null, error?: string })` where `AssetRef = { id: string; assetNumber: string; name: string }`; uses `AssetPicker` to add, buttons to move up/down/remove.
- Modify: `web/src/pages/pm/PmPage.tsx` (asset column shows `t('pm.assetCount', { count })` when `assetCount > 0`)
- Modify: `web/src/pages/work/WorkOrderDetailPage.tsx` (`TasksPanel` ~178: group by `assetId` in `w.assets` order with per-asset progress; per-step inline *Reading* and *Remarks* that save on blur via `PUT /work-orders/{id}/tasks/{taskId}`; header shows the asset list when `w.assets.length > 0`)
- Modify: `web/src/pages/assets/AssetDetailPage.tsx` (badge `t('pm.round')` on work orders without `assetId` and on schedules with `assetCount > 0`)
- Modify: `web/src/locales/en.json` — keys: `pm.targetAsset` "Asset", `pm.targetLocation` "Location", `pm.targetSeveral` "Several assets", `pm.addAsset` "Add asset", `pm.assetCount` "{{count}} assets", `pm.round` "Round", `pm.noAssets` "Add at least one asset.", `wo.reading` "Reading", `wo.remarks` "Remarks", `wo.assets` "Assets", `wo.assetProgress` "{{done}}/{{total}}". Change `pm.target` to "Target".

- [ ] **Step 1:** Implement types, editor, form, list, checklist, asset page, locale keys (the sketches in the spec's *Web* section are the layout).
- [ ] **Step 2:** Web type-check + build (command above) — expected: `vite build` completes with no TypeScript errors.
- [ ] **Step 3:** `docker compose up -d --build app`; in the browser (Local Test organization): create a round of 3 sample assets, generate, open the work order, tick a step, enter a reading and remark, reload — values persist; open one of the assets — the work order and schedule appear with the *Round* badge; check the work order at phone width (375 px) — steps and inputs stack without horizontal scroll.
- [ ] **Step 4: Commit** `git commit -m "PM rounds: schedule form, grouped checklist with readings, asset history"`.

### Task 6: Importer — rounds, covered assets, step results

**Files:**
- Modify: `server/FourC.Cmms.LegacyImport/Legacy.cs` (load `PMScheduleAssets`, `IsMultipleAsset`; streaming reader for `workorderelements`)
- Create: `server/FourC.Cmms.LegacyImport/RoundImport.cs` (pure helpers + the step-results pass)
- Modify: `server/FourC.Cmms.LegacyImport/PmImport.cs` (rounds at ~119; WO→schedule link at ~185 picks the split by site)
- Modify: `server/FourC.Cmms.LegacyImport/Program.cs` (call the step-results pass after `PmImport`)
- Modify: `server/FourC.Cmms.LegacyImport/FourC.Cmms.LegacyImport.csproj` only if a test project reference is needed (see Step 1)
- Test: `server/FourC.Cmms.Tests/RoundImportTests.cs` (add a project reference from Tests to LegacyImport)

**Interfaces:**
- Produces (static class `RoundImport`, pure):
  - `IReadOnlyList<(string SiteCode, string Code, IReadOnlyList<string> AssetNos)> SplitBySite(string pmNo, IReadOnlyList<(string AssetNo, string SiteCode)> orderedAssets)` — one entry per site in first-appearance order; `Code = pmNo` when a single site, else `$"{pmNo}-{siteCode}"`.
  - `IReadOnlyList<string> CoveredAssets(IReadOnlyList<(string AssetNo, int AssetSeqNo)> elements, IReadOnlyList<string> scheduleList)` — distinct element asset numbers ordered by `AssetSeqNo`, or `scheduleList` when there are none.
  - `string? JoinRemarks(string? findings, string? remarks)` — trimmed, joined with `" · "`, null when both empty.
- `LegacyWork` gains `List<LPmScheduleAsset> PmScheduleAssets` (`PMNo`, `SeqNo`, `AssetNo`) and `HashSet<string> MultiAssetPmNos` (from `IsMultipleAsset = 1`), both via `Try(...)` so older 4CSQL versions load.

- [ ] **Step 1: Write failing unit tests** `RoundImportTests` (`[Fact]`): `SplitBySite` — single site keeps the code; two sites give `PM-1-MGM`, `PM-1-MEGS` with each site's assets in order. `CoveredAssets` — elements `[(1120,2),(1110,1),(1110,1)]` → `[1110,1120]`; no elements → schedule list. `JoinRemarks` — `("leak", "")` → `"leak"`; `("a","b")` → `"a · b"`; blanks → null.
- [ ] **Step 2:** `DT 'FullyQualifiedName~RoundImportTests'` — expected: FAIL.
- [ ] **Step 3:** Implement the helpers; then wire the import:
  - Rounds: a schedule whose `PMNo` is in `MultiAssetPmNos` becomes a round per `SplitBySite` (assets resolved via `assetByNumber`; unknown assets warned once per schedule). Non-flagged schedules ignore leftover `PMScheduleAssets` rows.
  - Historical WOs linked to a round get `WorkOrderAsset` rows from `CoveredAssets` (elements of that WO), and link to the split matching the WO's site.
  - Step results: stream `workorderelements` (Dapper `buffered: false`, ordered by `WorkorderNo, AssetSeqNo, SeqNo`); import a row when it has a reading/finding/remark, or when its work order is not completed/closed/cancelled; map per the spec (*Importer* section); insert in batches of 5,000 with `db.ChangeTracker.Clear()` after each `SaveChanges`; idempotent via `LegacyRef = $"step:{Woelementid}"` (skip existing refs).
- [ ] **Step 4:** `DT ''` — expected: all PASS.
- [ ] **Step 5: Verify against the customer databases** — rebuild the importer, then import each into a fresh organization (`--create-tenant`, `docker run --init --memory 4g`, as used for the earlier MEG test). Expected in the printed report:
  - EGA: no "neither asset … nor location" warning for its 1 multi-asset PM; KandilGlass: none for its 9.
  - MEG: the 298 multi-asset PMs (plus extra rows for cross-site splits) appear as *PM schedule* created; *Work order asset* ≈ number of round work orders × assets; *Step result* ≈ 265,000 plus open work orders' steps; run completes within the 4 GB limit.
  - Spot check in SQL: MEG WO `PMMEGS26024569` covers assets 1110, 1120, 1130 in that order.
- [ ] **Step 6: Commit** `git commit -m "Importer: multi-asset PMs, covered assets and PM step results"`.

### Task 7: Documentation

**Files:**
- Modify: `docs/LEGACY_MIGRATION.md` — supported versions (verified on 6.108.1.1); mapping rows for section PMs (TypePM 3 → the section's Area), multi-asset PMs (rounds, split per site), `workorderelements` (step results rule); department/location code clash suffix rule; the measured results for EGA, KandilGlass and MEG (counts and run times); how to run the importer in Docker (`--init`, memory limit); "Not yet imported" list updated; re-run caveat (status history is only added for newly created work orders — re-run into a fresh organization after a failed first run).
- Modify: `README.md` — Preventive maintenance module line mentions multi-asset rounds and step readings.

- [ ] **Step 1:** Write the changes.
- [ ] **Step 2: Commit** `git commit -m "Docs: 4CSQL migration findings and multi-asset PMs"`.
