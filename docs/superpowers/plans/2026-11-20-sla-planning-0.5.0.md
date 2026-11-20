# SLA Targets and Planning Board (0.5.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Response and completion targets per priority that set deadlines on requests and work orders and show what is late, plus a drag-and-drop planning board by technician and by asset.

**Architecture:** Targets live in `TenantSettings` (JSON, no migration). Deadline rules are pure functions in `FourC.Cmms.Core/Work/Sla.cs`; every path that creates or changes work calls them. Work orders and requests gain date-time deadlines beside the existing `DueDate`, which stays the deadline's local day so everything date-based keeps working. The board is one read endpoint and one move endpoint, drawn by a Mantine grid with `@dnd-kit/core`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19, Mantine 8, TanStack Query 5, i18next, Vitest; `@dnd-kit/core`.

**Spec:** `docs/superpowers/specs/2026-11-20-sla-planning-0.5.0-design.md`

## Global Constraints

- "Nothing changes for an organization until someone sets targets", apart from due dates now carrying an end-of-day time.
- Targets count **every hour** (no working-hours calendar); the clock does **not** pause while on hold.
- A due date/time typed by hand always wins over the target. Editing targets never moves deadlines already set.
- **Date-only** due dates mean "by the end of that day" in the organization's time zone.
- **PM-generated work orders keep their schedule's due date** (`DueAt` = end of that day), no `StartBy`, never target-derived.
- A request's *Needed by* (`RequiredBy`) is the requester's wish; it is the deadline only when that priority has no completion target.
- A priority change (on edit or at approval) recomputes target-derived deadlines **from when the work was raised** (the request's creation time, else the work order's).
- Responded: request = `DecidedAt`; work order = first time it leaves Open, a step is ticked or hours are logged — at the moment recorded, or the phone's time for offline work.
- Existing permissions only. One EF migration for the release. Every string through `web/src/locales/en.json`.
- Phones on 0.4.x keep working: new fields are additive; the tech bundle keeps `dueDate`.
- Commands (from the worktree): `export WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/sla-planning-0.5.0; B=/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad/bin` then `$B/wt-test [filter]`, `$B/wt-vitest`, `$B/wt-web`, `$B/wt-ef <Name>`.

## Review Focus

1. **A deadline near midnight in the organization's zone** (Africa/Cairo, due 23:30 local = 21:30 UTC) lands on the right `DueDate` day and is not late before 23:30 local → test in Task 2.
2. **Offline phone work that arrives after a deadline but was done before it** counts as responded/met on time (the phone's time) → test in Task 2.
3. **An organization with no targets**: creating, editing and approving work behaves as before (no `StartBy`, `DueAt` only from a typed date, `RequiredBy` still pre-fills) → test in Task 2.
4. **A board drag that must be refused** — no edit permission, another organization's work order, completed/closed work — answers with a reason and changes nothing → test in Task 5.
5. **Saving a work order without touching its due fields** keeps a target-derived deadline target-derived and unmoved → test in Task 2.

---

### Task 1: Targets in settings and the deadline rules

**Files:**
- Modify: `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`TenantSettings`)
- Create: `server/FourC.Cmms.Core/Work/Sla.cs`
- Modify: `server/FourC.Cmms.Api/Features/AdminEndpoints.cs:133-150` (settings validation)
- Test: `server/FourC.Cmms.Tests/SlaTests.cs` (pure), `server/FourC.Cmms.Tests/TenantSettingsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class SlaTarget { WorkPriority Priority; int? RespondMinutes; int? CompleteMinutes; }`; `TenantSettings.SlaTargets : List<SlaTarget>` (default empty); `TenantSettings.DailyCapacityHours : decimal` (default 8).
  - `public enum SlaState { None, OnTrack, DueSoon, Late, Met, Missed }`
  - `static class Sla`:
    - `TimeZoneInfo Zone(string? id)` — valid zone or UTC.
    - `DateTimeOffset EndOfDay(DateOnly day, TimeZoneInfo zone)` — 23:59:59 local, as an offset in UTC.
    - `DateOnly LocalDay(DateTimeOffset at, TimeZoneInfo zone)`.
    - `SlaTarget? For(TenantSettings s, WorkPriority p)`.
    - `DateTimeOffset? RespondBy(TenantSettings s, WorkPriority p, DateTimeOffset raisedAt)`; `DateTimeOffset? CompleteBy(TenantSettings s, WorkPriority p, DateTimeOffset raisedAt)`.
    - `SlaState State(DateTimeOffset? deadline, DateTimeOffset? doneAt, DateTimeOffset now, TimeSpan? window)` — None without a deadline; Met/Missed once done (done ≤ deadline is Met); else Late if now > deadline; DueSoon if remaining < window/4, or < 2 h when `window` is null; else OnTrack.

- [ ] **Step 1: Write the failing tests** — `SlaTests` (plain `[Fact]`, no database):
  - `End_of_day_is_the_last_second_of_the_local_day`: `EndOfDay(2026-11-20, Africa/Cairo)` == `2026-11-20T21:59:59Z`; `LocalDay(2026-11-20T22:30:00Z, Cairo)` == 2026-11-21.
  - `Unknown_zone_falls_back_to_UTC`: `Zone("Mars/Base")` == `TimeZoneInfo.Utc`.
  - `Targets_count_every_hour_from_when_work_was_raised`: Urgent respond 60, complete 240; raised Thursday 16:00Z → `RespondBy` 17:00Z, `CompleteBy` 20:00Z; Low with no target → both null.
  - `States`: with deadline 12:00 and window 4 h: now 08:30 → OnTrack; 11:30 → DueSoon; 12:01 → Late; done 11:59 → Met; done 12:01 → Missed; no window, deadline in 90 min → DueSoon; no deadline → None.
  - `TenantSettingsTests.Targets_and_time_zone_are_validated`: PUT settings with `timeZone = "Nowhere/City"` → 400 on `settings.timeZone`; two targets for Urgent → 400; `respondMinutes = 0` → 400; `dailyCapacityHours = 30` → 400; a valid set (Urgent 60/240, Cairo, 8) → 200 and GET returns it.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SlaTests|FullyQualifiedName~TenantSettingsTests"` — Expected: compile errors / FAIL (no `Sla`, no validation).
- [ ] **Step 3: Implement** `SlaTarget`, `SlaState`, `Sla` and the settings validation (targets: 1 minute … 365 days, one row per priority; `TimeZone` must satisfy `TimeZoneInfo.TryFindSystemTimeZoneById`; `DailyCapacityHours` 1–24; a null `SlaTargets` becomes empty).
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit** `feat(sla): targets per priority and deadline rules`.

### Task 2: Deadlines on requests and work orders

**Files:**
- Modify: `server/FourC.Cmms.Core/Work/Work.cs` — `WorkOrder`: `DueAt`, `StartBy`, `RespondedAt` (all `DateTimeOffset?`), `DueFromTarget` (bool); `WorkRequest`: `RespondBy`, `DueAt` (`DateTimeOffset?`), `DueFromTarget` (bool).
- Create: migration `SlaPlanning` (via `$B/wt-ef SlaPlanning`).
- Create: `server/FourC.Cmms.Api/Infrastructure/DueBackfill.cs`; Modify: `server/FourC.Cmms.Api/Program.cs` (run it after migrations).
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (`WorkOrderInput`, `StatusInput`, `CreateAsync`, `Apply`, PUT, `ChangeStatusAsync`, `AddLaborAsync`, `ApplyTaskChangeAsync`), `WorkRequestEndpoints.cs` (`CreateAsync`, approve, `ApproveInput`), `PmGenerator.cs:151`, `TechActions.cs` (`ChangeStatusAsync` passes `At`), `server/FourC.Cmms.LegacyImport/WorkImport.cs:144` (set `DueAt` = end of the imported day), `server/FourC.Cmms.Core/Services/SampleOperations.cs:236` (same).
- Test: `server/FourC.Cmms.Tests/SlaWorkTests.cs`

**Interfaces:**
- Consumes: Task 1's `Sla.*`, `TenantSettings.SlaTargets`.
- Produces:
  - `WorkOrderInput(..., DateTimeOffset? DueAt = null)` — date+time from the form; `DueDate` stays accepted (date only).
  - `StatusInput(..., DateTimeOffset? At = null)` — when the change happened (phone); null = now.
  - `ApproveInput(..., DateTimeOffset? DueAt = null)`.
  - `WorkOrderEndpoints.CreateAsync(..., bool applyTargets = true)` — `PmGenerator` passes `false`.
  - `internal static async Task MarkRespondedAsync(CmmsDbContext db, Guid workOrderId, DateTimeOffset? at, CancellationToken ct)` — sets `RespondedAt ??= at ?? now` (no save).
  - `DueBackfill.RunAsync(CmmsDbContext db, CancellationToken ct)` — idempotent; fills null `DueAt` from `DueDate`/`RequiredBy` with `Sla.EndOfDay` in the tenant's zone (fallback: all the tenant's sites share one valid zone → that one; else UTC).
- Deadline rule on create (work order): `DueAt` given → hand-set, `DueDate = LocalDay(DueAt)`; else `DueDate` given → hand-set, `DueAt = EndOfDay(DueDate)`; else (applyTargets) `DueAt = CompleteBy(priority, raisedAt)`, `DueFromTarget = true`, `DueDate = LocalDay`. `StartBy = RespondBy(priority, raisedAt)` only when `applyTargets` and no request. `raisedAt` = request's `CreatedAt` when created from a request, else now.
- On PUT: a due field differing from what is stored → hand-set (`DueFromTarget = false`). Otherwise, if the priority changed and `DueFromTarget` → recompute `DueAt`/`DueDate` (and `StartBy` when no request) from `raisedAt` with the new priority. Otherwise untouched.
- Request create: `RespondBy`/`DueAt` from targets with `raisedAt = CreatedAt`; no completion target → `DueAt = EndOfDay(RequiredBy)` (hand-set) when given.
- Approve: `input.DueAt` → hand-set; else `input.DueDate` → end of that day, hand-set; else if the priority changed → `CompleteBy(newPriority, request.CreatedAt)` (or the request's `DueAt` when the new priority has no target); else the request's `DueAt` and `DueFromTarget`. The web pre-fill stops sending `RequiredBy` as `DueDate` (Task 4).
- Responded: `ChangeStatusAsync` leaving Open (any target) → `RespondedAt ??= input.At ?? now`; `ApplyTaskChangeAsync` when a step becomes done and `AddLaborAsync` → `MarkRespondedAsync(at)`.

- [ ] **Step 1: Write the failing tests** in `SlaWorkTests` (each sets targets via PUT `/api/settings` first: Urgent respond 60 / complete 240, Medium complete 3 days; zone `Africa/Cairo` unless stated):
  - `Targets_set_deadlines_on_new_work`: POST an Urgent work order → `startBy` ≈ now+1 h, `dueAt` ≈ now+4 h, `dueDate` == Cairo day of `dueAt`, `dueFromTarget` true.
  - `A_date_typed_by_hand_wins_and_means_end_of_day`: Urgent with `dueDate` 2026-12-01 → `dueAt` == `2026-12-01T21:59:59Z`, `dueFromTarget` false.
  - `A_deadline_near_midnight_lands_on_the_local_day` (Review Focus 1): created with `dueAt = 2026-12-01T21:30:00Z` (23:30 in Cairo, UTC+2 in December) → `dueDate` == 2026-12-01, not 2026-12-02; and a job with `dueAt` = now + 30 min is not in the late list.
  - `Saving_without_touching_due_fields_keeps_the_target_deadline` (Review Focus 5): PUT the same values with a new title → `dueAt` unchanged, `dueFromTarget` still true.
  - `Changing_priority_recomputes_target_deadlines_but_not_hand_set_ones`: target-derived Medium → PUT Urgent → `dueAt` ≈ created+4 h; a hand-set one → unchanged.
  - `Requests_get_deadlines_and_approval_with_a_new_priority_counts_from_when_raised`: request Medium created; force its `CreatedAt` back 10 h through the DbContext; approve as Urgent → work order `dueAt` ≈ created+4 h (already past), no `startBy`; the request's `respondedAt`/`decidedAt` set.
  - `Needed_by_is_the_deadline_only_without_a_target`: Low (no target) request with `requiredBy` → `dueAt` = end of that day; Medium with `requiredBy` → `dueAt` = created+3 days.
  - `PM_work_keeps_its_schedule_date`: generate a PM work order → `dueAt` = end of its `dueDate`, `startBy` null, `dueFromTarget` false.
  - `The_first_action_is_the_response`: Open → OnHold sets `respondedAt`; a separate job: tick a step → set; a third: phone `status.change` InProgress with `at` = now−2 h → `respondedAt` ≈ now−2 h (Review Focus 2); a later action never moves it.
  - `Without_targets_nothing_changes` (Review Focus 3): no targets → POST without due → `dueAt`, `startBy` null; with `dueDate` → end of day; approve keeps working with `dueDate`.
  - `Existing_due_dates_are_back_filled_in_the_organizations_time_zone`: insert a work order and a request with `DueDate`/`RequiredBy` and null `DueAt` through the DbContext; `DueBackfill.RunAsync` → `DueAt` = Cairo end of day; running again changes nothing.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SlaWorkTests"` — Expected: FAIL (fields and rules missing).
- [ ] **Step 3: Implement** the entity fields, then `$B/wt-ef SlaPlanning`, then the rules above, `DueBackfill` and its call in `Program.cs`, importer and sample data.
- [ ] **Step 4: Run** `$B/wt-test` (full) — Expected: all pass (existing tests unchanged).
- [ ] **Step 5: Commit** `feat(sla): deadlines on requests and work orders`.

### Task 3: Late and due soon everywhere, and the "Targets met" report

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (list item, detail, `Overdue` filter → `Late`, summary), `WorkRequestEndpoints.cs` (list item, detail, `Late` filter), `AlertEndpoints.cs`, `AdminEndpoints.cs:215` (dashboard), `ReportEndpoints.cs` (backlog overdue rule; new report), `TechEndpoints.cs` (bundle `dueAt`).
- Create: `server/FourC.Cmms.Api/Features/SlaQueries.cs`
- Test: `server/FourC.Cmms.Tests/SlaWorkTests.cs` (more), `AlertTests.cs`

**Interfaces:**
- Consumes: Task 2 fields, `Sla.State`.
- Produces:
  - `record SlaView(SlaState Response, SlaState Completion, DateTimeOffset? RespondBy, DateTimeOffset? DueAt, DateTimeOffset? RespondedAt)` on work order and request list items and details (`sla`).
  - `SlaQueries.LateWork(IQueryable<WorkOrder> q, DateTimeOffset now)` — active and (`DueAt < now` or (`StartBy < now` and `RespondedAt == null`)); `SlaQueries.LateRequests(IQueryable<WorkRequest>, now)` — New and (`RespondBy < now` or `DueAt < now`).
  - Filters: `WorkOrderFilter.Late` (the old `Overdue=true` keeps working as an alias), `RequestFilter.Late`.
  - Dashboard `work`: `Late`, `DueSoon`, `ResponseLate` (replacing `Overdue`, which stays as `Late` for old clients); alert counts use `LateWork`.
  - Report `targets-met` (Work group, `WorkOrdersView`, site and date-range params): rows per priority × month with response met/total/% and completion met/total/%; response = requests (decision vs `RespondBy`) + work orders without a request (`RespondedAt` vs `StartBy`); completion = non-PM work orders with `DueAt` (`CompletedAt` vs `DueAt`).
- The "due soon" window: the target's length (`CompleteBy − raisedAt`, `RespondBy − raisedAt`) when the deadline came from a target; `null` (the 2-hour rule) for hand-set and PM deadlines.
- Approved requests' list state is their work order's state.

- [ ] **Step 1: Write the failing tests**:
  - `Late_means_past_the_due_time_not_past_the_due_day`: Urgent target-derived job created with `CreatedAt` forced back 5 h → list `late=true` contains it, `sla.completion` == Late, while a job due later today is not; dashboard `late` == 1.
  - `Response_late_counts_until_someone_acts`: job with `StartBy` past and no response → in `late`; put on hold → leaves `late` (still completion-on-track).
  - `Requests_are_late_while_waiting`: New request past `RespondBy` → request list `late=true` contains it; after approval → not.
  - `AlertTests.Overdue_counts_use_the_due_time`: bell `overdueWorkOrders` follows `LateWork`.
  - `Targets_met_report_counts_each_thing_once`: one request answered in time, one late; one job completed in time, one late; one PM job → response 1/2, completion 1/2, PM not counted.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SlaWorkTests|FullyQualifiedName~AlertTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** `SlaQueries`, the `sla` fields, filters, dashboard, alerts, backlog rule, report, bundle `dueAt`.
- [ ] **Step 4: Run** `$B/wt-test` (full) — Expected: all pass.
- [ ] **Step 5: Commit** `feat(sla): late and due soon on lists, dashboard, alerts; targets met report`.

### Task 4: Targets in the web app

**Files:**
- Create: `web/src/components/sla.ts` (+ `sla.test.ts`), `web/src/components/SlaBadge.tsx`
- Modify: `web/src/api/types.ts`; `web/src/pages/settings/SettingsPage.tsx` (Targets by priority table, time-zone `Select` of `Intl.supportedValuesOf('timeZone')`, daily capacity); `web/src/pages/work/WorkOrderFormPage.tsx` (due date **and time**: `DateTimePicker`, sends `dueAt`); `WorkOrdersPage.tsx` and `WorkRequestsPage.tsx` (deadline column, *Late* filter chip replacing *Overdue*, approve dialog pre-fills the target deadline and shows *Needed by* with a "use this" link); `WorkOrderDetailPage.tsx` (both deadlines and their state); `web/src/pages/DashboardPage.tsx` (late / due soon / response late cards linking to filtered lists); `WorkOrderPrintPage.tsx` (due date and time); `web/src/tech/JobPage.tsx`, `web/src/tech/TechApp.tsx`, `web/src/tech/types.ts` (show "due 14:00" when `dueAt` is not end of day); `web/src/locales/en.json`.

**Interfaces:**
- Consumes: Task 3's `sla` view, filters, dashboard fields, settings fields.
- Produces: `deadlineText(deadline: string | null | undefined, state: SlaState, now: Date, t: TFunction): string` and `slaColor(state): string | undefined` in `components/sla.ts`; `<SlaBadge sla={...} kind="completion"|"response" />`.

- [ ] **Step 1: Write the failing tests** — `sla.test.ts` (with a stub `t` returning key + values):
  - due in 3 h → `sla.dueIn` with "3 h"; 2 h late → `sla.late` with "2 h"; due in 26 h → `sla.dueIn` with "1 d 2 h"; more than 7 days away → the date; Met → `sla.met`; no deadline → `''`.
  - `slaColor`: Late/Missed → red, DueSoon → orange, else undefined.
- [ ] **Step 2: Run** `$B/wt-vitest` — Expected: FAIL (no module).
- [ ] **Step 3: Implement** `sla.ts`, `SlaBadge`, then the screens above.
- [ ] **Step 4: Run** `$B/wt-vitest` and `$B/wt-web` — Expected: PASS and "✓ built".
- [ ] **Step 5: Hands-on** (local test container as in 0.4.0): set targets; create an Urgent job → "due in 4 h"; force one late → red, dashboard card links to the filtered list; approve a request with a priority change; settings time-zone list saves.
- [ ] **Step 6: Commit** `feat(sla): targets, deadlines and late work in the web app`.

### Task 5: Planning board server

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PlanningBoardEndpoints.cs`; Modify: `Program.cs` (map), `PmEndpoints.cs` (extract the forecast query into `internal static Task<List<ForecastRow>> ForecastAsync(CmmsDbContext db, DateOnly from, DateOnly to, Guid? siteId, CancellationToken ct)`, used by `/pm/forecast` and the board).
- Test: `server/FourC.Cmms.Tests/PlanningBoardTests.cs`

**Interfaces:**
- Consumes: Task 3's `SlaView` for card colours; `TenantSettings.DailyCapacityHours`.
- Produces:
  - `GET /api/planning/board?from&to&siteId&tradeId` (`WorkOrdersView`; range ≤ 42 days) → `{ capacityHours, people: [{id, name, tradeIds}], jobs: [BoardJob], earlier: [BoardJob], unscheduled: [BoardJob], pms: [ForecastRow] }`.
  - `BoardJob { id, number, title, priority, status, siteId, assetId, assetNumber, assetName, day (DateOnly?, planned start else due day), dueAt, sla, estimatedHours, assignees: [{employeeId, hours}] }` — `hours` = assignee's planned hours, else estimate ÷ number of assignees, else null.
  - Placement: active work orders (Open, In progress, On hold); `day` in range → `jobs`; `day` before `from` and before today → `earlier` (newest first); no day → `unscheduled`. `people` = active employees on the site (or with no sites) and trade filter.
  - `POST /api/planning/move` (`WorkOrdersEdit`) `{ workOrderId, day?: DateOnly, fromEmployeeId?: Guid, toEmployeeId?: Guid }` → `{ warning?: string }`: `day` sets `PlannedStart` (warning when after the due day; the form's "due before planned start" check does not apply here); `from`→`to` moves that assignment (keeps planned hours); `to` alone adds; `from` alone removes. Refused (400 with reason) for Completed/Closed/Cancelled work and for an employee who doesn't exist; other organizations' ids → 404.

- [ ] **Step 1: Write the failing tests** in `PlanningBoardTests`:
  - `Jobs_sit_on_their_planned_start_else_their_due_day`; `Earlier_and_unscheduled_trays`; `Hours_are_planned_hours_else_the_estimate_split`.
  - `Upcoming_PMs_are_on_the_board`: a schedule due in range → one `pms` row.
  - `Moving_changes_the_day_and_the_person`: move day → `plannedStart`; move from A to B → assignees [B, others]; from A only → removed; move past the due day → 200 with `warning`.
  - `Moves_that_must_be_refused_change_nothing` (Review Focus 4): a technician role without edit → 403; another organization's work order → 404; a completed job → 400 with reason; the job is unchanged after each.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~PlanningBoardTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** the endpoints and `ForecastAsync`.
- [ ] **Step 4: Run** `$B/wt-test` (full) — Expected: all pass.
- [ ] **Step 5: Commit** `feat(planning): board and move endpoints`.

### Task 6: Planning board page

**Files:**
- Modify: `web/package.json` (add `@dnd-kit/core`); Create: `web/src/pages/planning/PlanningPage.tsx`, `web/src/pages/planning/board.ts` (+ `board.test.ts`); Modify: `web/src/App.tsx` (route `/planning`), `web/src/components/AppLayout.tsx` (menu item under Maintenance, `IconCalendarWeek`, `P.woView`), `web/src/locales/en.json`.

**Interfaces:**
- Consumes: Task 5's board and move endpoints; Task 4's `slaColor`/`SlaBadge`; `generateNow` from `web/src/pages/pm/generateNow.tsx`.
- Produces: pure helpers in `board.ts`:
  - `days(from: string, to: string): string[]`;
  - `cellsByPerson(data: Board): Map<string, Map<string, BoardJob[]>>` (key `'unassigned'` for jobs without assignees; a job with two assignees is in both rows);
  - `cellsByAsset(data: Board): Map<string, Map<string, (BoardJob | ForecastRow)[]>>`;
  - `load(data: Board): Map<string, Map<string, number>>` (person → day → hours; jobs in trays not counted).

- [ ] **Step 1: Write the failing tests** — `board.test.ts`: a job with two assignees lands in both rows on its day; unassigned key; `load` sums assignee hours and ignores `earlier`/`unscheduled`; asset rows include PM rows; `days` for a week is 7 dates.
- [ ] **Step 2: Run** `$B/wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement** `board.ts`, then `PlanningPage`: week/month switch with previous/today/next; site and trade filters; *By technician* (Unassigned lane on top, each cell shows cards and `hours / capacity`, red when over) and *By asset*; *From earlier days* and *Unscheduled* trays; `DndContext` drop → `POST /planning/move` → refetch, toast with *Undo* (sends the inverse move); dashed PM cards with *Generate now*; card click opens `/work-orders/:id`; drag disabled without the work-order edit permission (the `P` key for `workorders.edit`).
- [ ] **Step 4: Run** `$B/wt-vitest` and `$B/wt-web` — Expected: PASS and "✓ built".
- [ ] **Step 5: Hands-on**: drag a job to another day and person; undo; drag from *From earlier days*; over-capacity cell turns red; asset view; *Generate now* on a PM card.
- [ ] **Step 6: Commit** `feat(planning): planning board page`.

### Task 7: Changelog and docs

**Files:** Modify `CHANGELOG.md` (`## [0.5.0] - <date>`: Added targets, deadlines, late work, report, planning board; Changed: due dates mean end of day, time-zone list, "check your time zone"); Create `docs/PLANNING_AND_TARGETS.md` (how targets work, what counts as a response, the board, the trays); Modify `docs/ROADMAP.md` (calendar view done).

- [ ] **Step 1:** Write the changelog entry and the guide (wording from the spec).
- [ ] **Step 2: Run** `$B/wt-test`, `$B/wt-vitest`, `$B/wt-web` — Expected: all pass, built.
- [ ] **Step 3: Commit** `docs: 0.5.0 changelog and planning/targets guide`.
