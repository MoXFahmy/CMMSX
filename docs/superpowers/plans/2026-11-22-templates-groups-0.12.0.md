# Templates, Triggers and Group Work (0.12.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Work templates (PM tasks grown), shared PM calendars and extra meter triggers, PM recipients and rule overrides, per-asset templates, meter propagation, occurrence lists, group work orders, even spread, and checklists on assets and categories.

**Architecture:** `PmTask` gains template fields and documents; a pure `PmTriggers` computes next dates from a PM's own rule plus shared `ScheduleCalendar`s; `PmGenerator` learns extra meters, per-asset templates, spread and a *Group* mode that creates a `WorkOrderGroup` with ordinary child work orders (`WorkOrder.GroupId`), so no existing count changes; `AssetChecklist` steps are appended inside `WorkOrderEndpoints.CreateAsync`, the single creation path.

**Tech Stack:** ASP.NET Core 10, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19 + Mantine 8 + TanStack Query + i18next, Vitest; all builds in Docker.

**Spec:** `docs/superpowers/specs/2026-11-22-templates-groups-0.12.0-design.md`

## Global Constraints

- One migration, `TemplatesGroups`; only new tables and nullable/defaulted columns — existing rows keep every value (existing PMs keep `NextDueDate`).
- Code names stay (`PmTask`, `/api/pm-tasks`); every screen, e-mail and doc says *Work template(s)* / *قالب العمل* (*قوالب العمل*).
- New permission keys: none. Templates and calendars: `pm.view` / `pm.manage`; groups: `workorders.view` / `workorders.edit` / `workorders.execute` (status), as for work orders; asset checklists: `assets.edit` (asset) / `settings.manage` (category).
- Group numbers: own sequence `WorkGroup`, prefix `GRP-`, 5 digits (`GRP-00001`).
- Limits: calendar dates ≤ 500; extra meters ≤ 5 per PM; calendars ≤ 10 per PM; recipients ≤ 20; spread parts 2 … number of assets; occurrences shown 24.
- Messages (exact English, all with Arabic):
  - "The due date comes from group {0}."
  - "The multi-asset mode can't change after the PM has generated work."
  - "Shared calendars need the Scheduled basis."
  - "A meter can only follow a meter with the same unit."
  - "Meters can't follow each other in a loop."
  - "Spread needs a fixed interval, the Scheduled basis, and no calendars or extra meters."
  - "These assets get no checklist steps: {0}." (save warning, not an error)
  - "Left alone (finished or hours logged): {0}."
  - "No next date: its calendars have no dates after {0}."
  - "This occurrence was already generated as {0}."
- Every user-visible string in English and Arabic (web `en.json`/`ar.json`; server text through `Localization/ar.json`).
- 0.9.0 access applies to every new list, picker, download and action; organization-wide lookups use `AllSites()` exactly where the existing code does.
- Helpers: `wt-test <filter>` (→ `last-test.log`), `wt-vitest`, `wt-web`, `wt-ef <Name>`; debug APK `mobile/build-apk.sh debug` (unchanged mobile code; build only at the end).

## Review Focus

1. Upgrading: an existing PM (no calendars, no extra meters, no overrides) generates on exactly the same dates and with the same work order as before — Task 6 `Existing_schedules_generate_as_before`.
2. A group child's due date changed directly (edit, bulk) is refused with the message, and a group due change skips finished children — Task 10 `Child_due_date_follows_the_group`.
3. A source meter reading that is lower (reset) or older than its latest reading propagates nothing — Task 7 `Resets_and_late_readings_are_not_propagated`.
4. An occurrence generated out of order is not generated again when normal generation reaches it, and the PM moves past it — Task 8 `Out_of_order_occurrence_is_not_generated_twice`.
5. A person limited to some sites sees only groups of their sites and only children they may see; assigning across sites is refused — Task 10 `Groups_follow_site_access`.

---

### Task 1: Model and migration

**Files:** Modify `server/FourC.Cmms.Core/Pm/Pm.cs`, `Work/Work.cs`, `Assets/Assets.cs` (or new `Assets/AssetChecklist.cs`), `Data/CmmsDbContext.cs`, `Services/Numbering.cs` (`NumberKind.WorkGroup`); create `Core/Work/WorkOrderGroup.cs`, `Core/Pm/ScheduleCalendar.cs`; migration `TemplatesGroups`; test `server/FourC.Cmms.Tests/TemplatesModelTests.cs`.

**Produces:**
- `PmTask` + `string? Title`, `WorkType WorkType = Preventive`, `WorkPriority? Priority`, `List<Guid> RecipientIds`, `decimal? ExpectedStopHours`.
- `ScheduleCalendar : TenantEntity { Code, Name, CalendarKind Kind (Interval=1, Dates=2), int Frequency=1, FrequencyUnit Unit=Month, DateOnly? AnchorDate, List<DateOnly> Dates, bool IsActive=true }` (organization-wide like `PmTask`).
- `PmScheduleType.Calendar = 5`; `enum MultiAssetMode { Round = 1, Group = 2 }`; `enum GroupPlan { Copy = 1, Split = 2 }`; `ReadingSource.Propagated = 4`.
- `PmSchedule` + `List<Guid> RecipientIds`, `List<AssetStatus>? SkipStatusesOverride`, `List<DayOfWeek>? NonWorkingDaysOverride`, `bool? NoStackingOverride`, `MultiAssetMode MultiAssetMode = Round`, `GroupPlan GroupPlan = Copy`, `int? SpreadParts`, `int SpreadIndex`, `DateOnly? CycleStart`, `List<PmScheduleCalendar> Calendars`, `List<PmScheduleMeter> ExtraMeters`.
- `PmScheduleCalendar { PmScheduleId, ScheduleCalendarId }` (key both); `PmScheduleMeter { PmScheduleId, MeterId, decimal Interval, decimal? NextDueReading }` (key both); `PmScheduleAsset` + `Guid? PmTaskId`; `Meter` + `Guid? SourceMeterId`.
- `WorkOrder` + `List<Guid> RecipientIds`, `Guid? GroupId`.
- `WorkOrderGroup : TenantEntity { Number, Title, SiteId, Site, Guid? PmScheduleId, Guid? PmTaskId, DateOnly? PmDueDate, DateOnly? PlannedStart, DateOnly? DueDate, DateTimeOffset? DueAt }` (unique Number; site filter by `SiteId`).
- `WorkOrderGroupComment : Entity, ITenantOwned { GroupId, Body (4000), Guid? UserId, string? UserName }` (site filter through its group).
- `AssetChecklist : TenantEntity { Guid? AssetId, Guid? AssetCategoryId, Guid PmTaskId }` (exactly one of asset/category; unique per target+template).
- Lists of Guid/enum/DateOnly stored as JSON like existing `AssigneeIds`/`PmSkipAssetStatuses`.

- [ ] **Step 1:** Failing test `Templates_model_round_trips` (saves a template with the new fields, a calendar with dates, a schedule with a calendar link, an extra meter, overrides, Group mode, a group with a comment, an asset checklist; reads them back equal) and `Group_numbers_use_their_own_sequence` (`Numbering.For(..., NumberKind.WorkGroup)` → sequence `"WorkGroup"`, prefix `"GRP-"`).
- [ ] **Step 2:** `wt-test TemplatesModelTests` → FAIL (types missing).
- [ ] **Step 3:** Implement the model, DbSets, filters, `wt-ef TemplatesGroups`; check the migration only adds.
- [ ] **Step 4:** `wt-test TemplatesModelTests` → PASS; full `wt-test` green.
- [ ] **Step 5:** Commit `feat(0.12.0): model for templates, calendars, groups and asset checklists`.

### Task 2: Work templates and work orders from a template

**Files:** `PmEndpoints.cs` (TaskInput + fields, detail + where-used, `POST /api/pm-schedules/{id}/own-template`), `SupportEndpoints.cs` (attachment kind `"PmTask"` → `pm.view`/`pm.manage`), `WorkOrderEndpoints.cs` (`WorkOrderInput.TemplateId`, detail: `template { id, code, name, expectedStopHours, documents[] }`, `recipients[]`), `PmGenerator.CreateWorkOrderAsync` (title from `task.Title`, type from `task.WorkType`, priority `task.Priority ?? s.Priority`); test `TemplateTests.cs`.

**Interfaces:**
- `TaskInput(..., string? Title = null, WorkType? WorkType = null, WorkPriority? Priority = null, List<Guid>? RecipientIds = null, decimal? ExpectedStopHours = null)`.
- `GET /api/pm-tasks/{id}` adds `usedBy { schedules: [{id, code, name, role: "routine"|"everyNth"|"lower"|"upper"|"asset"}], checklists: [{assetId?, categoryId?, name}], openWorkOrders: int }`.
- `POST /api/pm-schedules/{id}/own-template` → `{ pmTaskId, code }` (`pm.manage`).
- `WorkOrderEndpoints.WorkOrderInput(..., Guid? TemplateId = null)`; `CreateAsync` with a template: `PmTaskId = TemplateId`, planned parts via `Planning.DefaultStoreAsync`, `RecipientIds = template.RecipientIds`.

- [ ] Failing tests: `Template_fields_save_and_show`; `Work_order_from_template_brings_parts_recipients_and_documents` (template with a part, a recipient, an attachment → new WO has planned part, recipients, `template.documents` lists the attachment); `Pm_work_takes_the_template_title_type_and_priority` (template Title/Type Inspection/Priority High → generated WO uses them; template without priority → PM's); `Where_used_lists_pms_and_checklists`; `Own_template_copies_and_relinks` (PM points to the copy; editing the original no longer changes what the PM generates).
- [ ] Implement; PASS; full suite; commit.

### Task 3: PM recipients and the e-mail grid

**Files:** `PmEndpoints.cs` (`ScheduleInput.RecipientIds`, detail), `PmGenerator.CreateWorkOrderAsync` (`w.RecipientIds = template ∪ PM`, active users only), `Core/Notifications/OutboundEmail.cs` (`PmAssigned` allows + defaults `RequestRecipients`), `EmailOutbox.cs` (request-recipients kind adds the work order's `RecipientIds`); web label key `notify.kinds.requestRecipients` → "Recipients"; test `PmRecipientsTests.cs`.

- [ ] Failing tests: `Pm_recipients_reach_generated_work` (WO `recipients` = template + PM users, no duplicates, inactive dropped); `Pm_recipients_get_the_generated_email` (an `OutboundEmails` row for `pm.assigned` to the recipient); `Work_order_recipients_get_completed_email`; `Recipients_without_access_get_nothing` (a recipient limited to another site gets no row).
- [ ] Implement; PASS; full suite; commit.

### Task 4: Per-PM rules and a template per asset

**Files:** `PmEndpoints.cs` (ScheduleInput: `SkipStatusesOverride`, `NonWorkingDaysOverride`, `NoStackingOverride`, `AssetTemplates: [{assetId, pmTaskId?}]` alongside `AssetIds`; save answer `warnings: string[]`), `PmGenerator.cs` (effective rules: override ?? organization; round steps/parts per asset template); `Core/Pm/PmRound.cs` (`Steps(covered, stepsFor: Func<Guid, IReadOnlyList<string>>)` overload); test `PmRulesTests.cs`, `PmRoundTests.cs` (pure).

- [ ] Failing tests: `Pm_override_skips_statuses_the_org_does_not` and `Pm_override_clears_an_org_rule` (override `[]` generates where the org would skip); `Pm_non_working_days_override`; `Pm_no_stacking_override`; `Round_uses_each_assets_template` (steps and parts per asset; asset without override uses the PM's); `Every_nth_template_applies_to_every_asset`; `Saving_warns_when_an_asset_gets_no_steps` (warning text exact, save succeeds); pure `Round_steps_per_asset_template`.
- [ ] Implement; PASS; full suite; commit.

### Task 5: Shared calendars and the next-date function

**Files:** Create `server/FourC.Cmms.Core/Pm/PmTriggers.cs`, `server/FourC.Cmms.Api/Features/PmCalendarEndpoints.cs`; register in `Program.cs`; tests `PmTriggersTests.cs` (pure), `PmCalendarApiTests.cs`.

**Produces:**
- `PmTriggers.CalendarNext(ScheduleCalendar c, DateOnly after) → DateOnly?` (Interval: first anchor + k·interval > after; Dates: first listed date > after; inactive → null).
- `PmTriggers.Next(PmSchedule s, DateOnly after, IReadOnlyList<ScheduleCalendar> calendars) → DateOnly?` = earliest of own rule (`PmCalendar.Next(s, after)` when `s.Type != Calendar && PmCalendar.UsesCalendar(s)`) and each calendar's `CalendarNext`.
- `PmTriggers.First(PmSchedule s, DateOnly start, IReadOnlyList<ScheduleCalendar> calendars) → DateOnly?` (first on or after start).
- `/api/pm-calendars`: GET list (with `scheduleCount`), GET `{id}` (+ next 12 dates, used by), POST/PUT (`pm.manage`), DELETE (refused while used: "In use by {0} PM(s).").

- [ ] Failing pure tests: interval calendar from an anchor; dates calendar skips past dates; earliest of own monthly rule and a dates calendar; Calendar-type uses calendars only; empty calendars → null; inactive calendar ignored. API tests: CRUD, 500-date limit, delete refused while used.
- [ ] Implement; PASS; commit.

### Task 6: Generation with calendars and extra meters

**Files:** `PmEndpoints.cs` (ScheduleInput: `CalendarIds`, `ExtraMeters: [{meterId, interval, nextDueReading?}]`; validation: calendars need Scheduled; Calendar type needs ≥ 1 calendar; meters on the PM's asset(s) as for the own meter; initial `NextDueDate` from `PmTriggers.First`), `PmGenerator.cs` (all `PmCalendar.Next` calls → `PmTriggers.Next` with the PM's calendars; `OnReadingAsync` also selects PMs with an extra meter on the meter; after any generation reset every incremental meter trigger to current + interval; meter-fired generation restarts own rule from today and takes the next calendar date after today; "No next date" result), forecast endpoint; test `PmTriggerGenerationTests.cs`.

- [ ] Failing tests: `Existing_schedules_generate_as_before` (a fixed monthly PM and a meter-or-time PM created through the old inputs generate the same dates/WOs as the current release: compare against fixed expected dates); `Calendar_pm_generates_on_listed_dates`; `Own_rule_or_calendar_whichever_first`; `Extra_meter_triggers_work_and_resets_all_meters`; `Calendar_needs_scheduled_basis` (message exact); `No_next_date_when_calendars_run_out` (result text exact); `Forecast_includes_calendar_dates`.
- [ ] Implement; PASS; full suite; commit.

### Task 7: Meter propagation

**Files:** `PmEndpoints.cs` (`MeterInput.SourceMeterId`; validation unit/loop/self; `RecordReadingAsync` propagates; meter list/detail shows source and followers); test `MeterPropagationTests.cs`.

**Interfaces:** `PmEndpoints.RecordReadingAsync(db, ctx, mail, meter, input, ct)` unchanged signature; propagated readings `Source = Propagated`, `Note = "From {code}"`, same `ReadAt` and `WorkOrderId`; each linked meter runs `PmGenerator.OnReadingAsync`; the generated list returned includes linked meters' work.

- [ ] Failing tests: `Reading_adds_the_increase_to_linked_meters` (parent 100→150, child 40 → 90, reading note); `Chain_propagates_and_runs_pms` (grandchild PM due at 95 generates); `Resets_and_late_readings_are_not_propagated`; `Different_unit_is_refused` and `Loop_is_refused` (messages exact).
- [ ] Implement; PASS; full suite; commit.

### Task 8: Occurrence list

**Files:** `PmEndpoints.cs` (`GET /api/pm-schedules/{id}/occurrences`, `POST /api/pm-schedules/{id}/occurrences/generate {date}` (`pm.generate`)), `PmGenerator.cs` (`GenerateOccurrenceAsync(db, ctx, s, date, ct, mail) → Result`, reusing `CreateWorkOrderAsync`/group creation; advances the PM only when `date == NextDueDate`); test `PmOccurrenceTests.cs`.

**Produces:** occurrences `[{ date, dueDate, templateName, status: "Planned"|"Generated", workOrderId?, workOrderNumber?, groupId?, groupNumber? }]` (24), meter PMs `[{ reading, estimatedDate? }]` (24; estimate from readings in the last 90 days; none with < 2 readings).

- [ ] Failing tests: `Lists_next_24_with_generated_marked`; `Generate_a_later_occurrence_now`; `Out_of_order_occurrence_is_not_generated_twice`; `Generating_an_existing_occurrence_says_so` (message exact); `Meter_projection_estimates_dates`.
- [ ] Implement; PASS; full suite; commit.

### Task 9: Group generation

**Files:** `PmEndpoints.cs` (ScheduleInput `MultiAssetMode`, `GroupPlan`; refused change after first generation), create `server/FourC.Cmms.Api/Features/WorkGroups.cs` (`CreateGroupAsync`), `PmGenerator.cs` (Group mode: group + one child per covered asset; hours/parts copy or split; children `GroupId`, `PmScheduleId`, `PmDueDate`, due from group), duplicate check covers groups (`WorkOrderGroups.Any(PmScheduleId, PmDueDate)`); test `GroupGenerationTests.cs`.

**Produces:** `WorkGroups.CreateGroupAsync(db, ctx, PmSchedule s, PmTask task, DateOnly? due, IReadOnlyList<RoundAsset> covered, IReadOnlyDictionary<Guid, PmTask> assetTemplates, IReadOnlyCollection<DayOfWeek> nonWorkingDays, ct, EmailOutbox? mail) → Task<WorkOrderGroup>`; generator `Result` gains `GroupId?`, `GroupNumber?`.

- [ ] Failing tests: `Group_mode_makes_a_group_with_a_child_per_asset` (numbers `GRP-00001`; children with their asset, steps, due = group due); `Split_divides_hours_and_parts`; `Copy_gives_each_child_the_plan`; `Mode_locks_after_first_generation` (message exact); `Groups_are_not_generated_twice`; `Children_count_as_work_and_groups_do_not` (dashboard/summary counts = children).
- [ ] Implement; PASS; full suite; commit.

### Task 10: Working with a group

**Files:** `WorkGroups.cs` (endpoints `/api/work-groups`: GET list (site filter, search, status), GET `{id}` (children, totals, computed status, comments), POST `{id}/assign {employeeIds, childIds?}`, POST `{id}/report {completionNotes?, causeNotes?, preventionNotes?, failureCodeId?, childIds?}`, POST `{id}/status {status input as work orders, childIds?}`, PUT `{id}/due {dueDate?, dueAt?}`, POST `{id}/comments {body}`); `WorkOrderEndpoints.cs` (PUT and bulk refuse a due change on a child; list item + filter `groupId`, `groupNumber`; detail `group { id, number, comments[] }`); `TechEndpoints.cs` (bundle job `group { number, comments[] }`); test `GroupActionTests.cs`.

**Produces:** `WorkGroups.Status(IEnumerable<WorkOrderStatus> children) → string` ("Open"/"InProgress"/"Completed"/"Closed"/"Cancelled"), pure, unit-tested.

- [ ] Failing tests: `Assign_skips_finished_or_worked_children` (answer lists them with the exact message); `Report_for_all_keeps_existing_fields`; `Status_of_all_reports_refusals` (a required-comment rule makes each child refuse; answer lists them); `Child_due_date_follows_the_group`; `Group_comments_show_on_children_and_phone`; `Totals_roll_up`; `Groups_follow_site_access`; pure `Group_status_from_children`.
- [ ] Implement; PASS; full suite; commit.

### Task 11: Even spread

**Files:** `Core/Pm/PmTriggers.cs` (`Spread(int assetCount, int parts, int k) → (int Start, int Count)`, `SpreadDate(DateOnly cycleStart, DateOnly nextCycle, int parts, int k) → DateOnly`), `PmEndpoints.cs` (ScheduleInput `SpreadParts`; validation message exact), `PmGenerator.cs` (spread: next date steps through parts; `SpreadIndex`, `CycleStart`; every-Nth counts cycles), occurrences/forecast; tests `PmTriggersTests.cs` (pure), `PmSpreadTests.cs`.

- [ ] Failing tests: pure `Spread_blocks_are_even` (10 assets / 3 parts → 4,3,3) and `Spread_dates_split_the_period`; `Yearly_pm_on_12_assets_in_12_parts_does_one_a_month`; `Spread_with_group_mode_makes_a_group_per_part`; `Every_nth_counts_periods_with_spread`; `Spread_is_refused_with_calendars` (message exact).
- [ ] Implement; PASS; full suite; commit.

### Task 12: Checklists on assets and categories

**Files:** Create `server/FourC.Cmms.Api/Features/AssetChecklistEndpoints.cs` (`GET /api/asset-checklists?assetId=|categoryId=`, POST `{assetId?|assetCategoryId?, pmTaskId}`, DELETE `{id}`); `WorkOrderEndpoints.CreateAsync` (append after own steps: templates on `w.AssetId`'s asset and category, or per covered asset on rounds with `AssetId`; skip `w.PmTaskId`); register in `Program.cs`; test `AssetChecklistTests.cs`.

**Produces:** `AssetChecklists.AppendAsync(CmmsDbContext db, WorkOrder w, IReadOnlyList<Guid> assetIds, CancellationToken ct) → Task` (called inside `CreateAsync` before save).

- [ ] Failing tests: `Asset_checklist_is_added_to_new_work`; `Category_checklist_is_added`; `Own_template_is_not_added_twice`; `Round_gets_checklists_per_asset`; `Phone_and_request_work_get_them_too` (request approval and `workorder.create` from the phone); `Asset_checklists_need_asset_edit`.
- [ ] Implement; PASS; full suite; commit.

### Task 13: Import

**Files:** `server/FourC.Cmms.Api/Features/Import/*` (sheet name "Work templates", old "PM tasks" accepted; optional columns Title, Work type, Priority, Expected stop hours; PM schedules optional "Multi-asset mode"); export writes the new names/columns; test `ImportPmTests.cs`.

- [ ] Failing tests: `Imports_work_templates_with_new_columns`; `Old_sheet_name_still_imports`; `Multi_asset_mode_imports_and_locks_like_the_screen` (refused with the exact message after generation); `Export_has_the_new_columns`.
- [ ] Implement; PASS; full suite; commit.

### Task 14: Web — templates, calendars and the PM form

**Files:** `web/src/pages/pm/PmPage.tsx` (tabs: schedules, templates, calendars, meters, generate, forecast), create `web/src/pages/pm/TemplatePage.tsx` (`/pm/templates/:id`, documents via the attachments component, where-used), `web/src/pages/pm/CalendarsTab.tsx`, `web/src/pages/pm/OccurrencesDrawer.tsx`, `PmScheduleFormPage.tsx` (calendars, extra meters, recipients, rule overrides, asset templates, mode/plan/spread, *Make a copy for this PM*, warnings), meter form source meter; `web/src/pages/work/WorkOrderFormPage.tsx` (template picker); locales; vitest for pure helpers in `web/src/pages/pm/spread.ts` (`spreadBlocks(count, parts)`).

- [ ] Failing vitest `spread.test.ts` (same blocks as the server); implement pages; `wt-vitest` and `wt-web` clean; commit.

### Task 15: Web — groups, work orders, assets and the phone

**Files:** create `web/src/pages/work/WorkGroupsPage.tsx`, `WorkGroupDetailPage.tsx` (children table, totals, assign/report/status/due/comments); `WorkOrderDetailPage.tsx` (template documents, expected stop, recipients, group link and comments, due locked note); `WorkOrdersPage.tsx` (group column + filter); asset page *Checklists* panel; Settings › Asset categories checklists; `web/src/tech/JobPage.tsx` (group number and comments); routes and menu; locales; vitest for `groupStatusColor` helper if any logic.

- [ ] Implement; `wt-vitest` and `wt-web` clean; commit.

### Task 16: Arabic, docs, verification

**Files:** `server/FourC.Cmms.Api/Localization/ar.json`, `server/FourC.Cmms.Tests/TemplatesModelTests.cs` (Arabic theory over every Global Constraints message), `docs/` (new `docs/PM_TEMPLATES_AND_GROUPS.md`; README module rows for PM and work orders; `docs/TECHNICIAN_APP.md` group jobs), `CHANGELOG.md` left for release.

- [ ] Failing Arabic theory; add translations; PASS.
- [ ] Docs; full `wt-test`, `wt-vitest`, `wt-web`, debug APK; commit.
