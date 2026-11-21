# Downtime polish (0.8.1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 0.8.0's downtime gaps: the work order's own stop moves with its asset, the phone app can report a stop and shows a linked stop on completion, and four small review fixes.

**Architecture:**
- The work order update moves or unlinks its stop through a new `Downtime.FollowAssetAsync`.
- The technician app's `request.create` change and its job bundle gain one field each.
- The other fixes extend the existing rule functions shared by the screens and the import.

**Tech Stack:** ASP.NET Core 10, EF Core 10 / SQL Server, xUnit (`SkippableFact` + `ApiFixture`); React 19, Mantine 8, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-21-downtime-polish-0.8.1-design.md` (and 0.8.0's `docs/superpowers/specs/2026-11-21-parts-downtime-0.8.0-design.md`)

**Commands:** `B=<scratchpad>/bin`; `$B/wt-test "FullyQualifiedName~<Class>"`, `$B/wt-test`, `$B/wt-vitest`, `$B/wt-web` (all point at `.worktrees/polish-0.8.1`).

## Global Constraints

- No new package, no migration. Every new text in `web/src/locales/{en,ar}.json` or `server/FourC.Cmms.Api/Localization/ar.json`.
- Older technician app versions (no `assetIsDown` in `request.create`, no `downtime` in the bundle) keep working.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Moving a stop to an asset that is already down joins that stop and removes the work order's own stop, with no unique-index error. Test: `Moving_onto_a_down_asset_joins_its_stop` (Task 1).
2. A moved stop keeps its exact start. Test: `Changing_the_asset_moves_the_stop_this_work_order_started` (Task 1).
3. A stop that came from a request is not moved. Test: `A_requests_stop_stays_when_the_work_order_moves` (Task 1).
4. A phone request queued offline with an old `at` starts its stop at `at`, not at sync time. Test: `A_phone_request_can_say_the_asset_is_down` (Task 2).
5. An older phone app's `request.create` (no `assetIsDown`) starts no stop. Same test (Task 2).

---

### Task 1: The work order's own stop follows its asset

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Features/DowntimeEndpoints.cs` (`Downtime.FollowAssetAsync`);
  - `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (PUT uses it);
  - `web/src/pages/work/WorkOrderFormPage.tsx` (hint);
  - `web/src/locales/en.json`, `ar.json`.
- Test: `server/FourC.Cmms.Tests/DowntimeTests.cs`. Replace `Changing_the_asset_leaves_the_stop_on_the_old_one` and `Changing_the_asset_with_the_box_ticked_starts_a_stop_on_the_new_one`.

**Interfaces:**
- Produces `internal static async Task FollowAssetAsync(CmmsDbContext db, WorkOrder w, Guid? oldAssetId, CancellationToken ct)`, called after `Apply` when `w.AssetId != oldAssetId` and `w.DowntimeEventId != null`. Not saved. It:
  - moves the stop (`AssetId = w.AssetId`) when the stop is open, `Source == WorkOrder`, `SourceNumber == w.Number`, nothing else points at it, `w.AssetId` is set, and the new asset has no open stop;
  - when the new asset has an open stop: links `w` to it and removes its own stop (own stops only);
  - otherwise sets `w.DowntimeEventId = null` (the 0.8.0 behaviour).
- PUT order: `FollowAssetAsync` (asset changed), then `UntickAsync` (box false), then `StartOrJoinAsync(now)` (box true, no stop).
- Web hint key `downtime.assetMovesHint`: "The stop this work order started moves to this asset and keeps its start." Shown when `wo.downtime.source === 'WorkOrder' && wo.downtime.sourceNumber === wo.number` and the asset differs. Otherwise the existing `downtime.assetChangedHint`.

- [ ] **Step 1: Write the failing tests:**
  - `Changing_the_asset_moves_the_stop_this_work_order_started`. Work order down on Asset; PUT with Asset2 (`down: null`). Expected:
    - Asset has no stops;
    - Asset2's single stop has the same id and the same `StartedAt` as before;
    - the work order's `downtime.id` is that stop.
    - The same holds with `down: true` (the web's payload).
  - `Moving_onto_a_down_asset_joins_its_stop`. Asset2 has an open manual stop; a work order down on Asset; PUT with Asset2. Expected: Asset has no stops; the work order's `downtime.id` is Asset2's manual stop; Asset2 has one stop.
  - `A_requests_stop_stays_when_the_work_order_moves`. A request down on Asset is approved; PUT the work order with Asset2 (`down: null`). Expected: the request's stop stays open on Asset, and the work order's `downtime` is null.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeTests"`. Expected: the first two fail (the stop stays on Asset).
- [ ] **Step 3: Implement** the server change, the hint and both locales.
- [ ] **Step 4: Run** the same command. Expected: all pass. Then `$B/wt-web`. Expected: `✓ built`.
- [ ] **Step 5: Commit** `"A work order's own stop moves with its asset"`.

### Task 2: The phone reports a stop and shows a linked one

**Files:**
- Modify:
  - `server/FourC.Cmms.Api/Features/TechActions.cs` (`RequestPayload` gains `bool? AssetIsDown`; passed as `AssetIsDown: p.AssetIsDown == true`);
  - `server/FourC.Cmms.Api/Features/TechEndpoints.cs` (bundle job gains `downtime: { startedAt, endedAt } | null`, the job's own linked stop);
  - `web/src/tech/types.ts`, `web/src/tech/PlacePage.tsx` (RequestModal box when an asset is chosen; payload `assetIsDown`), `web/src/tech/JobPage.tsx` (CompleteModal note instead of the switch when `job.downtime`; sends no `assetWasDown`/`downtimeHours` then);
  - `web/src/locales/en.json`, `ar.json`.
- Test: `server/FourC.Cmms.Tests/TechTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `A_phone_request_can_say_the_asset_is_down`:
    - `request.create` with `assetIsDown = true` and `at` = 2 hours ago starts one open stop on the asset, with `StartedAt` within a second of `at`;
    - a second asset with `request.create` and no `assetIsDown` gets no stop.
  - `The_phone_sees_its_jobs_stop`. A work order assigned to me, down on an asset: the bundle job's `downtime.startedAt` is set. For a work order without a stop it is null.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~TechTests"`. Expected: the two new tests fail.
- [ ] **Step 3: Implement** server and web.
- [ ] **Step 4: Run** the same command. Expected: all pass. Then `$B/wt-vitest` and `$B/wt-web`. Expected: green and `✓ built`.
- [ ] **Step 5: Commit** `"Phone: report that the asset is down, show a job's stop on completion"`.

### Task 3: Small fixes from the 0.8.0 review

**Files:**
- Modify:
  - `AssetEndpoints.cs` (`Apply` rounds `RunsHoursPerDay` to 2 decimals);
  - `Import/SheetImporters.cs` (`AssetImporter.ImportAsync` rounds the row's `runsHoursPerDay` before the comparison; `AssetPartImporter` uses `AssetPartEndpoints.MaxLines`);
  - `DowntimeEndpoints.cs` (`CheckAsync` refuses `StartedAt == default`);
  - `AssetPartEndpoints.cs`:
    - `public const int MaxLines = 500`;
    - `AssetPartRules` adds "At most 500 parts." (more than `MaxLines` lines) and "A quantity can be at most 1,000,000,000.";
  - `ReportEndpoints.cs`:
    - the Downtime log takes 5,001 rows; past 5,000 it keeps 5,000;
    - its subtitle then reads "{0} · hours count each asset's running time only · the first 5,000 stops";
  - `Localization/ar.json`.
- Test: `DowntimeTests.cs`, `AssetPartTests.cs`, `ImportAssetPartTests.cs`, `DowntimeReportTests.cs`

- [ ] **Step 1: Write the failing tests:**
  - `A_stop_needs_a_start` (DowntimeTests). POST `{ endedAt: null, reason: "x" }` with no `startedAt` gives 400 "Choose when the asset stopped.".
  - `Parts_lists_have_limits` (AssetPartTests). PUT 501 lines gives 400 "At most 500 parts."; PUT one line with quantity 2,000,000,000 gives 400 "A quantity can be at most 1,000,000,000.".
  - `Running_hours_are_kept_to_two_decimals` (ImportAssetPartTests):
    - import "Runs hours a day" 8.333: stored as 8.33;
    - the same file again: the Assets sheet shows 0 changed.
  - `The_downtime_log_says_when_it_is_cut_off` (DowntimeReportTests). 5,001 stops inserted through the database: 5,000 rows, and a subtitle ending "the first 5,000 stops".
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~DowntimeTests|FullyQualifiedName~AssetPartTests|FullyQualifiedName~ImportAssetPartTests|FullyQualifiedName~DowntimeReportTests"`. Expected: the four new tests fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the same command. Expected: all pass.
- [ ] **Step 5: Commit** `"Small downtime fixes: rounding, stop start, parts limits, log cut-off note"`.

### Task 4: Docs and the whole branch

- [ ] **Step 1:** `CHANGELOG.md` `## [0.8.1] - 2026-11-21` (Added: phone box and note; Changed: stop moves with the work order's asset; Fixed: the four small fixes).
- [ ] **Step 2: Run** `$B/wt-test`, `$B/wt-vitest`, `$B/wt-web`. Expected: all green.
- [ ] **Step 3: Commit** `"Docs for 0.8.1"`.
