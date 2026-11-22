# Smart Checklists and Follow-up Work (0.13.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Typed checklist steps (Check, Number, Meter reading, Text; photo required; safety before starting), shared measurement points with five levels, follow-up work raised in the same save, All OK, completion rules, trends and an out-of-limits report — on the web, the phone and the import.

**Architecture:** Step definitions live on `PmTaskStep` and are copied onto `WorkOrderTask` by one helper (`StepCopy`) used by every creation path; answering goes through one server function (`Checklists.AnswerAsync`) used by the web PUT, *All OK* and the phone's `task.update`, which validates, computes the level from the `MeasurementPoint`, writes the `MeasurementReading` or meter reading and raises the follow-up in the same `SaveChanges`. Safety and completion checks are pure functions over a work order's steps, called from `ChangeStatusAsync` (the single status path).

**Tech Stack:** ASP.NET Core 10, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19 + Mantine 8 (+ `@mantine/charts`) + TanStack Query + i18next, Vitest; all builds in Docker.

**Spec:** `docs/superpowers/specs/2026-11-22-smart-checklists-0.13.0-design.md`

## Global Constraints

- One migration, `SmartChecklists`; only new tables and nullable/defaulted columns — existing rows keep every value; existing steps are Check steps (`StepType.Check = 0`).
- Enums: `StepType { Check = 0, Number = 1, Meter = 2, Text = 3 }`, `StepAnswer { Ok = 1, NotOk = 2, NotApplicable = 3 }`, `FollowUpKind { Default = 0, None = 1, WorkOrder = 2, Request = 3 }`, `MeasureLevel { Normal = 0, AlertHigh = 1, AlarmHigh = 2, AlertLow = 3, AlarmLow = 4 }`, `MeasurementKind { Temperature = 1, Pressure = 2, Current = 3, Voltage = 4, Level = 5, Consumption = 6, Other = 7 }`.
- Limits: step unit ≤ 20; decimals 0–4 (null = 2); text answer ≤ 1000; point code ≤ 50, name ≤ 200; one follow-up per step.
- Organization settings (JSON, no column): `FollowUpDefault` = `Request`, `FollowUpTemplateId` = null, `RequireReadingSteps` = false.
- No new permission keys: points `pm.view` / `pm.manage`; readings `meters.record`; answering `workorders.execute`; report `workorders.view`.
- Messages (exact English, all with Arabic):
  - "Answer the safety steps first: {0}."
  - "A safety step isn't OK: {0}."
  - "Enter a value from {0} to {1} {2}."
  - "The valid range's minimum must be below its maximum."
  - "Levels must go up: alarm low, alert low, target, alert high, alarm high."
  - "Levels must be inside the valid range."
  - "Another point uses this code."
  - "Unknown measurement point."
  - "In use by {0} work template(s)."
  - "Choose a work template for this follow-up."
  - "Choose the meter this reading is for."
  - "This step needs a different kind of answer."
  - "Take a photo for: {0}."
  - "Answer these steps first: {0}."
  - "This template is used for follow-up work. Mark it inactive instead."
  - Follow-up title "{0} — from step {1} of {2}"; description lines "Answer: {0}" and "Value: {0} {1} ({2})".
- Every user-visible string in English and Arabic (web `en.json`/`ar.json`; server text through `Localization/ar.json`, formatted before `Translator.T`).
- 0.9.0 access on every new list, picker, download and action; organization-wide reads that decide or write use `AllSites()`.
- Helpers: `wt-test <filter>` (→ `last-test.log`), `wt-vitest`, `wt-web`, `wt-ef <Name>`; debug APK `mobile/build-apk.sh debug` at the end.

## Review Focus

1. Correcting an answer (Not OK → OK → Not OK; 120 → 80 → 125 °C) raises exactly one follow-up, and the reading history keeps one reading per step — Task 6 `Changing_the_answer_raises_one_follow_up`, Task 5 `Changing_a_value_replaces_its_reading`.
2. Rounds: a Not OK on asset B's step raises the follow-up on asset B, and asset A's unanswered safety steps don't block asset B — Task 6 `Round_follow_up_is_for_the_steps_asset`, Task 7 `Round_safety_is_per_asset`.
3. Upgrading: an old phone or page sending `isDone: true/false` on an existing step still ticks and unticks it, as OK and cleared, and raises nothing — Task 5 `Old_tick_still_works`.
4. A template used by a point level, a step's follow-up or the organization default can't be deleted (follow-ups would silently fall back) — Task 4 `Templates_used_for_follow_ups_cannot_be_deleted`.
5. A phone answer that is rejected (outside the valid range, or a conflict) raises no follow-up and writes no reading — Task 6 `Rejected_phone_answer_raises_nothing`.

---

### Task 1: Model and migration

**Files:** create `server/FourC.Cmms.Core/Pm/Checklists.cs` (the five enums), `server/FourC.Cmms.Core/Pm/Measurement.cs` (`MeasurementPoint`, `MeasurementPointAsset`, `MeasurementReading`); modify `Pm/Pm.cs` (`PmTaskStep` fields), `Work/Work.cs` (`WorkOrderTask` fields; `WorkOrder.SourceWorkOrderId?`, `SourceTaskId?`), `Work/WorkRequest*.cs` (`SourceWorkOrderId?`, `SourceTaskId?`), `Files/Attachment.cs` (`WorkOrderTaskId?`), `Tenancy/Tenant.cs` (settings), `Data/CmmsDbContext.cs` (DbSets, lengths, indexes: unique point code per tenant filtered not deleted, readings `(PointId, AssetId, ReadAt)`, site filter on readings, `MeasurementPointAsset` composite key, decimals `(18,4)`); migration `SmartChecklists` via `wt-ef SmartChecklists`; test `ChecklistModelTests.cs`.

**Interfaces:**
- Produces: `PmTaskStep { Type, Unit, Decimals, MeasurementPointId, PhotoRequired, BeforeStart, FollowUp, FollowUpTemplateId }`; `WorkOrderTask` the same plus `MeterId, Answer, Value, TextValue, Level, FollowUpWorkOrderId, FollowUpRequestId`; `MeasurementPoint { Code, Name, Kind, Unit, Decimals, ValidMin, ValidMax, AlarmLow, AlertLow, Target, AlertHigh, AlarmHigh, AlarmLowTemplateId, AlertLowTemplateId, AlertHighTemplateId, AlarmHighTemplateId, IsActive, RowVersion, Assets }`; `MeasurementReading { PointId, AssetId, SiteId, Value, ReadAt, Level, WorkOrderId, WorkOrderTaskId, Note, ById, ByName, FollowUpWorkOrderId, FollowUpRequestId }`; `TenantSettings { FollowUpDefault, FollowUpTemplateId, RequireReadingSteps }`.

- [ ] Failing tests: `Existing_steps_are_check_steps` (a step saved without the new fields reads back `Type == Check`, `FollowUp == Default`, `Answer == null`); `Points_and_readings_round_trip` (point with 5 levels and 2 assets; a reading with `SiteId`); `Readings_follow_site_access` (a user limited to site A doesn't see site B's readings through the DbSet); `Settings_default_to_request_follow_ups` (`new TenantSettings().FollowUpDefault == Request`, `RequireReadingSteps == false`).
- [ ] Implement; generate the migration; PASS; full suite; commit.

### Task 2: Pure rules — levels, answers, safety, completion

**Files:** create `server/FourC.Cmms.Core/Pm/MeasureLevels.cs`, `server/FourC.Cmms.Core/Pm/StepRules.cs`; test `ChecklistPureTests.cs` (plain `[Fact]`s).

**Interfaces:**
- Produces: `MeasureLevels.LevelOf(MeasurementPoint p, decimal value) → MeasureLevel` (≥ alarm high → AlarmHigh; ≥ alert high → AlertHigh; ≤ alarm low → AlarmLow; ≤ alert low → AlertLow; else Normal; unset levels skipped); `MeasureLevels.OutOfRange(MeasurementPoint p, decimal value) → bool`; `MeasureLevels.Problems(MeasurementPoint p) → IReadOnlyList<(string Field, string Message)>` (the three point messages); `MeasureLevels.TemplateFor(MeasurementPoint p, MeasureLevel l) → Guid?`.
- `StepRules.Answered(WorkOrderTask t) → bool` (Check: Answer; Number/Meter: Value; Text: TextValue); `StepRules.Failed(WorkOrderTask t) → bool` (NotOk or Level ≠ Normal); `StepRules.SafetyBlock(IReadOnlyList<WorkOrderTask> steps, Guid? assetId, bool forStart) → string?` (the formatted English message naming unanswered safety steps "#{seq} {description}" or the first Not OK one; `forStart` checks only steps without an asset when the work order is a round, else all; a step answer checks steps of `assetId` plus those without an asset); `StepRules.CompletionProblems(IReadOnlyList<WorkOrderTask> steps, IReadOnlySet<Guid> stepsWithPhotos, bool requireReadings) → IReadOnlyList<string>` (photo and readings messages); `StepRules.RoundProgress(IEnumerable<WorkOrderTask> assetSteps) → "green" | "red" | null`.

- [ ] Failing tests: `Levels_from_alarm_to_alarm` (point 0–60, alarm low 5, alert low 10, target 22, alert high 28, alarm high 32: 4→AlarmLow, 10→AlertLow, 22→Normal, 28→AlertHigh, 33→AlarmHigh); `Unset_levels_are_skipped`; `Out_of_range_is_detected` (61 and -1); `Point_problems` (min ≥ max; alert high 40 > alarm high 32; alarm high 70 outside 0–60 — each exact message); `Answered_per_type`; `Safety_block_names_the_steps` ("Answer the safety steps first: #1 Lock out."); `Safety_not_ok_blocks` ("A safety step isn't OK: #1 Lock out."); `Round_safety_only_blocks_its_asset`; `Completion_problems` ("Take a photo for: #3 Belt." and "Answer these steps first: #2 Temperature."); `Round_progress_colours`.
- [ ] Implement; PASS; commit.

### Task 3: Measurement points API and direct readings

**Files:** create `server/FourC.Cmms.Api/Features/MeasurementPointEndpoints.cs` (`/api/measurement-points`); modify `Program.cs`; test `MeasurementPointTests.cs`; helpers in `Ck13.cs` (`PointAsync(c, code, extra?)`, `TypedTemplateAsync(c, code, steps)`, `WoWithStepsAsync(...)`).

**Interfaces:**
- Consumes: `MeasureLevels.*` (Task 2).
- Produces: `GET /` (list: code, name, kind, unit, decimals, levels, assetCount, readingCount, isActive; `?assetId=` filters points listing that asset or with readings on it); `GET /{id}` (detail + assets + `usedBy` templates + rowVersion); `POST`, `PUT /{id}` (row version), `DELETE /{id}` (refused "In use by {0} work template(s)." when steps use it); `GET /{id}/readings?assetId&from&to` (newest first, ≤ 1000, site-filtered); `POST /{id}/readings { assetId, value, readAt?, note? }` (`meters.record`; out of range refused; level stored; follow-ups wired in Task 6). `CheckPointInput` validations: code unique ("Another point uses this code."), lengths, `MeasureLevels.Problems`, level templates exist ("Unknown work template."), assets exist and visible.

- [ ] Failing tests: `Points_can_be_made_listed_changed_and_deleted`; `Level_order_and_range_are_checked` (exact messages); `A_point_used_by_a_template_cannot_be_deleted`; `Direct_reading_stores_its_level` (31 °C at alert high 28 → `AlertHigh`); `Reading_outside_the_valid_range_is_refused` ("Enter a value from 0 to 60 °C."); `Readings_follow_site_access`; `Points_need_pm_manage_and_readings_meters_record`.
- [ ] Implement; PASS; full suite; commit.

### Task 4: Typed template steps and copying them everywhere

**Files:** modify `PmEndpoints.cs` (`TaskInput.StepDetails`, `StepInput`, `ApplyTask` typed, `GET /pm-tasks/{id}` → `stepDetails`, own-template copy typed, template delete guard), create `server/FourC.Cmms.Core/Pm/StepCopy.cs`, modify `PmRound.cs` (typed overload), `PmGenerator.cs` (single, round, every-Nth, spread), `WorkGroups.cs`, `WorkOrderEndpoints.cs` (`CreateAsync` typed steps + template matching + meter resolution), `AssetChecklistEndpoints.cs` (`AppendAsync` typed), `TechActions.cs` (`tasks.fromPm` typed); create `server/FourC.Cmms.Api/Features/Checklists.cs` (`ResolveMetersAsync`); test `TypedStepTests.cs`.

**Interfaces:**
- Produces: `PmEndpoints.StepInput(string Description, StepType Type = Check, string? Unit = null, int? Decimals = null, Guid? MeasurementPointId = null, bool PhotoRequired = false, bool BeforeStart = false, FollowUpKind FollowUp = Default, Guid? FollowUpTemplateId = null)`; `TaskInput.StepDetails: List<StepInput>?` (wins over `Steps`; `Steps` alone = Check steps); validation: point exists and Number only, unit ≤ 20, decimals 0–4, WorkOrder follow-up needs a template ("Choose a work template for this follow-up."), template exists.
- `StepCopy.ToWorkOrder(PmTaskStep s, Guid workOrderId, int sequence, Guid? assetId) → WorkOrderTask` (copies the definition; Number with a point takes the point's unit/decimals at answer time, not copy time); `StepCopy.Copy(PmTaskStep s, Guid pmTaskId) → PmTaskStep`.
- `PmRound.TypedSteps(IReadOnlyList<RoundAsset> covered, Func<Guid, IReadOnlyList<PmTaskStep>> stepsFor) → IReadOnlyList<(Guid AssetId, int Sequence, PmTaskStep Step)>`.
- `WorkOrderEndpoints.CreateAsync(..., IReadOnlyList<PmTaskStep>? typedSteps = null)` — typed steps win over `input.Tasks`; with `TemplateId` and no typed steps, each `input.Tasks` text equal to an unused template step description takes that step's definition, in order. `Checklists.ResolveMetersAsync(db, w, ct)` sets `MeterId` per Meter step (step asset's active meter with the step's unit, or its only active meter), called before save.
- Template delete refused with "This template is used for follow-up work. Mark it inactive instead." when a point level, any step's `FollowUpTemplateId` or the organization's `FollowUpTemplateId` names it.

- [ ] Failing tests: `Template_steps_keep_their_types` (save 4 typed steps, read `stepDetails`); `Pm_work_copies_step_types` (single PM); `Round_and_group_children_copy_their_assets_template_types`; `Work_order_from_template_matches_steps_by_text` (form sends the template's 3 texts plus 1 extra: first 3 typed, extra Check); `Asset_checklist_and_phone_add_copy_types`; `Own_template_copies_types`; `Meter_step_finds_the_assets_meter` (asset with one "h" meter → `MeterId` set; two meters and no unit → null); `Templates_used_for_follow_ups_cannot_be_deleted` (point level, step follow-up, org default — exact message); `Follow_up_kind_needs_a_template`.
- [ ] Implement; PASS; full suite; commit.

### Task 5: Answering typed steps

**Files:** modify `server/FourC.Cmms.Api/Features/Checklists.cs` (answering); modify `WorkOrderEndpoints.cs` (`TaskInput` + `Answer?`, `Value?`, `Text?`, `MeterId?`, `Confirm`; PUT calls `Checklists.AnswerAsync`; response adds `level`, `followUp`), `TechActions.cs` (`TaskPayload` same fields; `task.update` calls `Checklists.AnswerAsync` with `Confirm = true`), `PmEndpoints.cs` (`RecordReadingAsync` optional `Guid? workOrderId`); test `StepAnswerTests.cs`.

**Interfaces:**
- Produces: `Checklists.StepChange(bool? IsDone, StepAnswer? Answer, decimal? Value, string? Text, Guid? MeterId, string? Reading, string? Remarks, bool Confirm)`; `Checklists.AnswerAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, TenantSettings settings, WorkOrder w, WorkOrderTask t, StepChange c, DateTimeOffset at, CancellationToken ct) → Task<Validator>` (wraps today's `ApplyTaskChangeAsync` behaviour: reading/remarks, done stamps, auto-start, responded); type rules: Check takes `Answer` (legacy `IsDone` true → Ok, false → clears), Number/Meter take `Value`, Text takes `Text` — anything else "This step needs a different kind of answer."; Number with point: range check (message with the point's unit), rounding, `Level`, one `MeasurementReading` per step (replaced when the value changes, removed when cleared); Meter: `RecordReadingAsync` with the step's or given meter (none → "Choose the meter this reading is for."), validator problems returned as they are.

- [ ] Failing tests: `Check_step_takes_ok_not_ok_and_na`; `Number_step_with_point_gets_its_level` (31 → AlertHigh, reading written with work order and step); `Changing_a_value_replaces_its_reading`; `Value_outside_the_valid_range_is_refused`; `Meter_step_records_a_meter_reading` (meter current reading updated, meter PM generates); `Meter_step_without_meter_needs_one`; `Text_step_keeps_its_text`; `Wrong_answer_kind_is_refused`; `Old_tick_still_works` (`isDone: true` → Answer Ok, no follow-up request; `isDone: false` clears); `Phone_answers_typed_steps` (`task.update` with value); `Phone_value_outside_valid_range_is_rejected`.
- [ ] Implement; PASS; full suite; commit.

### Task 6: Follow-up work

**Files:** modify `Checklists.cs` (`RaiseFollowUpAsync`), `MeasurementPointEndpoints.cs` (direct readings raise), `WorkOrderEndpoints.cs` (detail: `source { workOrderId, number, stepSequence, stepDescription, answer, value, unit, level, photos[] }`, step `followUp { kind, id, number }`), `WorkRequestEndpoints.cs` (request detail `source`), settings endpoint (follow-up default validation "Choose a work template for this follow-up."); test `FollowUpTests.cs`.

**Interfaces:**
- Produces: `Checklists.RaiseFollowUpAsync(db, ctx, mail, settings, WorkOrder? source, WorkOrderTask? step, MeasurementPoint? point, MeasureLevel level, Guid? assetId, Guid siteId, Guid? locationId, string what, CancellationToken ct) → Task<(Guid? WorkOrderId, Guid? RequestId)>` — precedence per spec (step None disables; point level template; step/org WorkOrder template; Request); work order through `WorkOrderEndpoints.CreateAsync` with `TemplateId` (typed steps), title `string.Format("{0} — from step {1} of {2}", ...)` translated, description lines, priority template's or source's, `SourceWorkOrderId`/`SourceTaskId`; request through `WorkRequestEndpoints.CreateAsync` (title "{step} — from step {n} of {WO}", requester = answering user). Called from `AnswerAsync` when the step becomes failed and has no follow-up yet, and from direct readings at a non-normal level.

- [ ] Failing tests: `Not_ok_raises_a_request_by_default` (title, description, asset, source link); `Step_set_to_work_order_raises_one_from_its_template` (typed steps of the template copied; priority; title "Belt replacement — from step 2 of WO-00001"); `Point_level_template_wins` (alarm template used at 33, alert template at 29); `None_raises_nothing`; `Org_default_work_order_uses_its_template`; `Changing_the_answer_raises_one_follow_up`; `Round_follow_up_is_for_the_steps_asset`; `Phone_answer_raises_the_follow_up_in_the_same_save`; `Rejected_phone_answer_raises_nothing`; `Direct_reading_at_alarm_raises_its_follow_up`; `Follow_up_shows_its_source` (detail `source.number`, step photos listed).
- [ ] Implement; PASS; full suite; commit.

### Task 7: Safety steps before starting

**Files:** modify `WorkOrderEndpoints.cs` (`ChangeStatusAsync`: In progress and Completed call `StepRules.SafetyBlock(..., forStart: true)`; `AutoStartAsync` skips while blocked), `Checklists.cs` (answering a non-safety step calls `SafetyBlock(steps, t.AssetId, forStart: false)`); test `SafetyStepTests.cs`.

**Interfaces:**
- Consumes: `StepRules.SafetyBlock` (Task 2), `Checklists.AnswerAsync` (Task 5).

- [ ] Failing tests: `Work_cant_start_before_safety_steps` (page status change refused with "Answer the safety steps first: #1 Lock out."; then answered OK → starts); `Safety_not_ok_blocks_start`; `Other_steps_wait_for_safety_steps`; `Bulk_group_and_phone_status_changes_check_safety` (bulk answer lists the refusal; group status refusal; phone `status.change` rejected); `Auto_start_waits_for_safety` (AutoStartWork on, labour logged → stays Open, no error); `Round_safety_is_per_asset`; `Completing_needs_safety_steps_too`.
- [ ] Implement; PASS; full suite; commit.

### Task 8: All OK, step photos and completion rules

**Files:** modify `WorkOrderEndpoints.cs` (`POST /{id}/tasks/all-ok { assetId? }` → `{ answered }`; completion check uses `StepRules.CompletionProblems`), `SupportEndpoints.cs` (upload form `stepId` → `Attachment.WorkOrderTaskId`, must belong to the work order), `TechActions.cs` (`/tech/files` form `taskId` same), settings input (`RequireReadingSteps`); test `StepCompletionTests.cs`.

**Interfaces:**
- Produces: `POST /api/work-orders/{id}/tasks/all-ok`; attachment list rows carry `workOrderTaskId`.

- [ ] Failing tests: `All_ok_answers_plain_check_steps` (skips safety, photo-required, Number, answered ones; count); `All_ok_on_a_round_asset`; `All_ok_respects_safety_steps` (refused with the safety message while unanswered); `Photo_required_step_needs_a_photo_to_complete` ("Take a photo for: #2 Belt."; upload with `stepId` → completes); `Phone_photo_on_a_step`; `Readings_needed_to_complete_when_set`.
- [ ] Implement; PASS; full suite; commit.

### Task 9: Outputs — work order detail, phone bundle, settings

**Files:** modify `WorkOrderEndpoints.cs` (detail tasks: all new fields, `photos` per step, `followUp`, point limits for Number steps, `meterName`), `TechEndpoints.cs` (bundle job tasks the same; `points` used by the jobs: id, code, name, unit, decimals, validMin/Max and levels; `settings.requireReadingSteps`; files carry `workOrderTaskId`), settings GET/PUT (`followUpDefault`, `followUpTemplateId`, `requireReadingSteps`); test `ChecklistOutputTests.cs`.

- [ ] Failing tests: `Detail_lists_typed_steps_with_photos_and_follow_ups`; `Bundle_carries_typed_steps_and_their_points` (a point used by a job is in `points` with its levels; another isn't); `Settings_save_follow_up_default`.
- [ ] Implement; PASS; full suite; commit.

### Task 10: Out-of-limits report and the asset's measurements

**Files:** modify `ReportEndpoints.cs` (`out-of-limits` in Reliability: params range + site + groupBy point/asset/level; columns date, asset, point, value, unit, level, work order, follow-up; KPIs alarms, alerts, follow-ups raised), `MeasurementPointEndpoints.cs` (`GET /api/assets/{id}/measurements` → points with their last 200 readings per point in the period and their levels); test `OutOfLimitsTests.cs`.

- [ ] Failing tests: `Out_of_limits_lists_alert_and_alarm_readings` (normal readings left out; KPI counts); `Out_of_limits_follows_site_access`; `Asset_measurements_return_series_with_levels`.
- [ ] Implement; PASS; full suite; commit.

### Task 11: Import

**Files:** modify `Import/ImportSheets.cs` (*Work template steps* columns `type`, `unit`, `decimals`, `measurementPoint`, `photoRequired`, `beforeStart`, `followUp`, `followUpTemplate`; new *Measurement points* sheet with code, name, kind, unit, decimals, validMin, validMax, alarmLow, alertLow, target, alertHigh, alarmHigh, alarmLowTemplate, alertLowTemplate, alertHighTemplate, alarmHighTemplate, assets, active — Arabic headings), `Import/SheetImporters.cs` (steps importer builds `StepInput`s; points importer uses the same validation as the API), `ImportWorkbook.cs` (export both); test `ImportChecklistTests.cs`.

- [ ] Failing tests: `Steps_sheet_imports_step_types` (point code and follow-up template code resolved); `Unknown_point_code_is_refused_per_row`; `Points_sheet_adds_and_updates_by_code` (levels, templates, assets); `Point_level_order_is_checked_in_the_import`; `Export_round_trips_points_and_typed_steps`.
- [ ] Implement; PASS; full suite; commit.

### Task 12: Web — measurement points, template steps, settings

**Files:** modify `web/src/api/types.ts` (step and point types, `PmTaskDetail.stepDetails`, settings fields); create `web/src/components/measure.ts` (+ `measure.test.ts`: `levelOf`, `outOfRange`, `roundProgress`, `answered`, `formatValue` — mirroring Task 2), `web/src/pages/pm/MeasurementPointsTab.tsx`, `web/src/pages/pm/TemplateSteps.tsx` (step editor rows: type, unit or point, decimals, photo required, before starting, follow-up + template); modify `PmPage.tsx` (tab `points`), `TemplatePage.tsx` (uses `TemplateSteps`, sends `stepDetails`), `pages/settings/SettingsPage.tsx` (Organization › Checklists: follow-up default + template, readings to complete); locales en + ar.

- [ ] `measure.test.ts` failing first (same cases as `ChecklistPureTests` levels/progress); implement; `wt-vitest` and `wt-web` clean; commit.

### Task 13: Web — answering on the work order, asset measurements, report

**Files:** create `web/src/pages/work/ChecklistPanel.tsx` (replaces the Tasks tab body: safety group first with a lock note; per-type controls — OK / Not OK / N/A segmented buttons, number input with unit and level badge, meter select when `meterId` is empty, text; remarks; photo button per step (upload with `stepId`, thumbnails); *All OK* for the work order and per round asset; round asset headers with green/red; follow-up links; meter confirmation re-send), `web/src/pages/work/SourceBanner.tsx` (follow-up's source with step photos); modify `WorkOrderDetailPage.tsx` (use both), `WorkRequestDetail` page (source banner); create `web/src/pages/assets/AssetMeasurements.tsx` (tab: `@mantine/charts` LineChart per point with reference lines for set levels, recent readings table with work order/follow-up links, *Add reading* modal); modify `AssetDetailPage.tsx` (tab `measurements`); the report renders through the existing reports page (labels only); locales en + ar.

- [ ] Implement; `wt-vitest` and `wt-web` clean; commit.

### Task 14: Phone — typed checklist

**Files:** create `web/src/tech/Checklist.tsx` (from the job page's checklist: per-type answers offline as `task.update` changes with `answer`/`value`/`text`/`meterId`; level badge via `measure.ts` using the bundle's `points`; out-of-range refused locally with the server's message; safety steps first and other steps disabled until they are answered; *All OK* as one change per eligible step; camera button per step queuing a photo with `taskId`; meter select from the bundle's meters of the step's asset; follow-up number shown after refresh; round asset headers green/red); modify `web/src/tech/JobPage.tsx` (uses it; completion checks photos and readings with the bundle's settings), `web/src/tech/types.ts`, `web/src/tech/api.ts` (`taskId` on file changes), `web/src/tech/complete.ts` (+ test: photo-required and readings rules); locales en + ar.

- [ ] `complete.test.ts` additions failing first; implement; `wt-vitest` and `wt-web` clean; commit.

### Task 15: Arabic, docs, verification

**Files:** `server/FourC.Cmms.Api/Localization/ar.json`; `ChecklistModelTests.cs` (Arabic theory over every Global Constraints message, formatted with sample values, plus any other new server message); `docs/SMART_CHECKLISTS.md` (new); README module rows (work orders, PM, reports); `docs/TECHNICIAN_APP.md` (typed steps, safety, photos per step); `CHANGELOG.md` left for release.

- [ ] Failing Arabic theory; add translations; PASS.
- [ ] Docs; full `wt-test`, `wt-vitest`, `wt-web`, debug APK; commit.

---

## Deferred minors (final review)

Found by the final review and left for a later release:

- M5 a follow-up on an asset with no location gets none even when the work order has one (checked: a work order with no asset already passes its location on; re-grade to Important withdrawn).
- M6 phone photo taken on a step removed meanwhile is stored then rejected (orphan file, photo lost).
- M8 answer accepts undefined enum integers; Number/Meter values without a range can overflow decimal(18,4) (web 500, phone change set aside after retries).
- M9 All OK runs ~5 queries per step (slow on 50-asset rounds).
- M10 template ids nothing uses (org default switched away from WorkOrder, step kind Default/Request) still block deleting the template.
- M11 before-starting and photo-required steps can be deleted by anyone who executes work, bypassing the gate.
- M12 Not OK then OK queued offline sends both, so the first raises a follow-up.
- M13 values round half-to-even (27.85 → 27.8).
- M14 the phone shows a reading's level on blur, not while typing.
