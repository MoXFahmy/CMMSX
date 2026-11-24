# Bilingual Data (0.28.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Records a technician reads (asset/location/part names, codes and lists, templates, steps, PM names, work order titles and
steps, notes to technician, safety notes) get an optional Arabic text; each person sees their own language everywhere; Arabic
texts come from forms, Excel, and AI drafts.

**Architecture:** Nullable `…Ar` columns beside each main text. The server picks the text by the request's language
(`Translator.CurrentLanguage`) in every payload that only displays it; payloads that feed edit forms keep the stored pair raw and
the web picks for display there. Work orders copy both texts from their template/PM when made. AI drafts run through the existing
`AiService` as a new feature `Translation`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, xUnit (SkippableFact, ApiFixture), React 19 + Mantine 8 +
TanStack Query, Vitest, Capacitor phone app (same web code).

**Spec:** `docs/superpowers/specs/2026-11-24-bilingual-data-0.28.0-design.md`

## Global Constraints

- Arabic for every new label, button, message, column heading (web `locales/en` + `locales/ar`, server `Localization/ar.json`, import column Arabic names).
- Tests first, watched failing; server tests are `[SkippableFact]` with `Skip.IfNot(fx.Enabled)` on `ApiFixture`, organizations from `OrgAsync(fx)`.
- Never write test files with shell redirection (`cat >`); use the Write/Edit tools.
- No `AllSites()` / `IgnoreQueryFilters` inside a query that must stay filtered; look extra names up in a second query (0.26.1 lesson).
- Blank Arabic input is stored as null (`.Clean()`); max length of each `…Ar` column equals its main column's.
- **The rule (R/S):**
  - **S — shown:** any payload that only displays a text (list rows of assets, locations, parts, PM schedules, templates and work
    orders; every reference to another record; pickers' own endpoints; phone; reports and exports; labels; e-mails; push; activity
    labels; AI pages' shown names) carries the **chosen** text in its existing field.
  - **R — raw:** endpoints that feed edit forms carry the stored main text in its existing field plus the `…Ar` field, unchanged
    by language: `GET /assets/{id}`, `/locations/{id}`, `/parts/{id}`, `/pm-tasks/{id}`, `/pm-schedules/{id}`, `/work-orders/{id}`
    (its own title, safety notes and steps), and the Settings lists `/asset-categories`, `/symptoms`, `/failure-codes`,
    `/remedies`, `/cancel-reasons`, `/request-types`. The web shows R texts with `pick()`.
  - In EF queries the chosen text is `ar && x.NameAr != null ? x.NameAr : x.Name` with `var ar = Bilingual.Arabic;` captured before
    the query (one SQL `CASE`, no second load).
- Purchasing (purchase orders, purchase requests, catalogue, supplier e-mails, purchase papers) keeps the part's **main** name.
- AI prompts (diagnosis, report) keep main texts; only names shown on AI pages follow the reader.
- Scripts: `WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/bilingual-0.28.0`; server tests `wt-test "FullyQualifiedName~X"`,
  migrations `wt-ef Name`, web `wt-vitest`, `wt-web`, CI gate `wt-ci` (all in the scratchpad `bin`).

## Review Focus

1. An Arabic reader opens any edit form and saves without touching names → the main text is unchanged (anti-goal 1). Pinned in Task 1 (server) and Task 9 (web form bodies).
2. Arabic text blank or whitespace → stored null, and every surface falls back to the main text, never blank. Pinned in Task 1 and Task 2.
3. A site-limited person reading in Arabic sees location paths, asset names and lists only from their sites; the new name lookups lift no filter. Pinned in Task 2.
4. AI save after someone else filled the same Arabic text meanwhile → skipped, not overwritten; an AI answer with unknown ids, missing ids or over-long text drafts only the valid ones and reports how many it couldn't. Pinned in Task 8.
5. PM generation or any background creation running inside an Arabic request still stores the main title/steps in the main fields and the Arabic in the Arabic fields. Pinned in Task 3.

---

### Task 1: Storage, the picking helper, and editing (server)

**Files:**
- Modify entities: `server/FourC.Cmms.Core/Assets/Assets.cs` (Asset, AssetCategory), `Locations/Locations.cs` (Location), `Inventory/Inventory.cs` (Part), `Work/Work.cs` (FailureCode, Symptom, Remedy, WorkOrder, WorkOrderTask), `Work/Requests.cs` (RequestType, CancelReason), `Pm/Pm.cs` (PmTask, PmTaskStep, PmSchedule)
- Create: `server/FourC.Cmms.Core/Common/IBilingualName.cs`, `server/FourC.Cmms.Api/Infrastructure/Bilingual.cs`
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (max lengths), migration `BilingualData` (generated)
- Modify inputs/validators/edit payloads: `Features/AssetEndpoints.cs`, `LocationEndpoints.cs`, `InventoryEndpoints.cs`, `SupportEndpoints.cs` (categories), `PeopleEndpoints.cs` (`LookupInput`, `MapLookup`, `ApplyLookup`, `LookupRules`), `RequestTypeEndpoints.cs`, `PmEndpoints.cs` (`TaskInput`, `StepInput`, `ScheduleInput`), `WorkOrderEndpoints.cs` (`WorkOrderInput`, `TaskInput`)
- Test: `server/FourC.Cmms.Tests/BilingualTests.cs` (pure), `server/FourC.Cmms.Tests/BilingualEditTests.cs`

**Interfaces:**
- Produces (Core): new nullable properties `NameAr` (Asset, AssetCategory, Location, Part, FailureCode, Symptom, Remedy, RequestType, CancelReason, PmTask, PmSchedule), `NotesToTechnicianAr` (Asset, Location), `TitleAr` (PmTask, WorkOrder), `SafetyNotesAr` (PmTask), `SafetyNotes` + `SafetyNotesAr` (WorkOrder, same max length as `PmTask.SafetyNotes`), `DescriptionAr` (PmTaskStep, WorkOrderTask).
- Produces: `public interface IBilingualName { string? NameAr { get; set; } }` implemented by FailureCode, Symptom, Remedy, CancelReason (not by Trade, OrderType, ContractType).
- Produces (Api): `public static class Bilingual` with `bool Arabic` (=`Translator.CurrentLanguage == Languages.Arabic`), `string Pick(string main, string? ar)`, `string? PickNote(string? main, string? ar)`, `string? Other(string main, string? ar)` (the text not chosen: under Arabic the main text when an Arabic text exists, else null; under English the Arabic text).
- Produces (inputs, all optional trailing `= null`): `AssetInput.NameAr, NotesToTechnicianAr`; `LocationInput.NameAr, NotesToTechnicianAr`; `PartInput.NameAr`; `CategoryInput.NameAr`; `LookupInput.NameAr` (applied only when the entity is `IBilingualName`); `RequestTypeInput.NameAr`; `PmEndpoints.TaskInput.NameAr, TitleAr, SafetyNotesAr`; `StepInput.DescriptionAr`; `ScheduleInput.NameAr`; `WorkOrderInput.TitleAr, SafetyNotes, SafetyNotesAr`; `WorkOrderEndpoints.TaskInput.DescriptionAr`.
- Produces (R payloads): the endpoints listed under the rule return the raw `…Ar` fields (camelCase: `nameAr`, `notesToTechnicianAr`, `titleAr`, `safetyNotes`, `safetyNotesAr`, `descriptionAr` on steps).

- [ ] **Step 1: Write the failing pure tests** in `BilingualTests.cs`:

```csharp
[Fact] public void Pick_takes_arabic_for_arabic_readers() { using (Translator.Use("ar")) Assert.Equal("مضخة", Bilingual.Pick("Pump", "مضخة")); }
[Fact] public void Pick_takes_main_for_english_readers() { using (Translator.Use("en")) Assert.Equal("Pump", Bilingual.Pick("Pump", "مضخة")); }
[Fact] public void Pick_falls_back_to_main_when_arabic_is_blank() { using (Translator.Use("ar")) { Assert.Equal("Pump", Bilingual.Pick("Pump", null)); Assert.Equal("Pump", Bilingual.Pick("Pump", "  ")); } }
[Fact] public void PickNote_handles_missing_main() { using (Translator.Use("ar")) Assert.Null(Bilingual.PickNote(null, null)); }
[Fact] public void Other_is_the_text_not_shown() { using (Translator.Use("ar")) { Assert.Equal("Pump", Bilingual.Other("Pump", "مضخة")); Assert.Null(Bilingual.Other("Pump", null)); } using (Translator.Use("en")) Assert.Equal("مضخة", Bilingual.Other("Pump", "مضخة")); }
```

- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualTests"` — Expected: build error, `Bilingual` does not exist.
- [ ] **Step 3: Implement** `IBilingualName`, `Bilingual`, the entity properties and their max lengths in `CmmsDbContext`; generate the migration with `WT=… wt-ef BilingualData`.
- [ ] **Step 4: Run** the pure tests — Expected: 5 passed.
- [ ] **Step 5: Write the failing API tests** in `BilingualEditTests.cs` (each sends `Accept-Language: ar` on reads and writes unless stated):
  - `Asset_keeps_both_texts_raw_in_its_edit_data` — POST asset with `name="Chiller pump 2"`, `nameAr="مضخة التبريد 2"`, `notesToTechnician="Isolate first"`, `notesToTechnicianAr="افصل أولاً"`; `GET /assets/{id}` under `ar` returns `name == "Chiller pump 2"` and `nameAr == "مضخة التبريد 2"`, same for the notes.
  - `Saving_the_edit_data_under_arabic_keeps_the_main_text` — GET the asset detail under `ar`, PUT it back unchanged under `ar` (rowVersion as the form does), GET under `en`: `name == "Chiller pump 2"`.
  - `Location_and_part_keep_both_texts` — the same pair round trip for `/locations` (name + notes) and `/parts` (name).
  - `Settings_lists_keep_both_texts` — `/asset-categories`, `/symptoms`, `/failure-codes`, `/remedies`, `/cancel-reasons`, `/request-types`: POST with `nameAr`, list GET under `ar` returns the row with raw `name` and `nameAr`; `/trades` rows have no `nameAr` property.
  - `Template_steps_and_schedule_keep_both_texts` — PmTask with `nameAr`, `titleAr`, `safetyNotes`+`safetyNotesAr`, a step with `descriptionAr`; PmSchedule with `nameAr`; both detail GETs under `ar` return the raw pairs.
  - `Work_order_keeps_title_safety_and_steps_in_both_texts` — POST work order with `titleAr`, `safetyNotes`, `safetyNotesAr`; add a step with `descriptionAr`; `GET /work-orders/{id}` under `ar` returns raw `title`, `titleAr`, `safetyNotes`, `safetyNotesAr`, and the step's `description` + `descriptionAr`.
  - `Blank_arabic_text_is_stored_as_empty` — `nameAr = "   "` → detail returns `nameAr == null`.
  - `Too_long_arabic_text_is_refused` — asset `nameAr` one character over the asset name's limit → 400 naming `nameAr`.
  - `Arabic_text_changes_show_in_history` — change an asset's `nameAr`; the asset's history (the endpoint the asset page's history uses) has an entry for the change.
- [ ] **Step 6: Run** `wt-test "FullyQualifiedName~BilingualEditTests"` — Expected: FAIL (fields missing / null).
- [ ] **Step 7: Implement** the inputs, validators (`MaxLength("nameAr", …, "Name (Arabic)", <main limit>)` etc.), apply methods (`x.NameAr = i.NameAr.Clean()`), and the R payload fields. For `MapLookup`: list rows add `NameAr = (x as IBilingualName)?.NameAr` only when `typeof(IBilingualName).IsAssignableFrom(typeof(T))` (so trades have no property); `ApplyLookup` sets it when the item is `IBilingualName`.
- [ ] **Step 8: Run** the Task 1 tests, then the whole suite `wt-test` — Expected: all pass (update any existing test whose anonymous payload shape broke, by adding the new fields, never by weakening an assertion).
- [ ] **Step 9: Commit** `feat(0.28.0): Arabic texts stored beside main texts, kept raw in edit data`.

### Task 2: Shown texts for places, parts and lists (server)

**Files:**
- Modify (S rule for asset, location, location path, part, category, symptom, cause, remedy, cancel reason, request type names): `Features/AssetEndpoints.cs` (list, export, lookup/picker), `LocationEndpoints.cs` (tree/list, `PathText` name dictionaries), `InventoryEndpoints.cs` (parts list/export/lookup, stock levels, transactions), `WorkOrderEndpoints.cs` and `WorkRequestEndpoints.cs` (references, list/export), `PlanningBoardEndpoints.cs`, `Planning.cs`, `PlanningEndpoints.cs`, `WorkGroupEndpoints.cs`, `WorkGroups.cs`, `ContractEndpoints.cs`, `AssetPartEndpoints.cs`, `AssetChecklistEndpoints.cs`, `MeasurementPointEndpoints.cs`, `DowntimeEndpoints.cs`, `SerialUnitEndpoints.cs`, `StockCountEndpoints.cs`, `TimesheetEndpoints.cs`, `ActivityEndpoints.cs` (`ActivityLabels`), `PmEndpoints.cs` (asset/location names in PM lists), `PmCalendarEndpoints.cs`, `AlertEndpoints.cs`
- Leave on main names: `PurchasingEndpoints.cs`, `PurchaseRequestEndpoints.cs`, `CatalogueEndpoints.cs`, `PurchaseApprovals.cs`
- Test: `server/FourC.Cmms.Tests/BilingualDisplayTests.cs`

**Interfaces:**
- Consumes: Task 1 properties and `Bilingual`.
- Produces: every S payload above carries the chosen text in its existing field; search filters that match a `Name` also match `NameAr`; name sorts use the chosen expression.

- [ ] **Step 1: Write the failing tests** in `BilingualDisplayTests.cs`:
  - `Asset_list_and_export_show_the_readers_language` — asset A `name "Chiller pump 2"`/`nameAr "مضخة التبريد 2"`, asset B with no Arabic; `GET /assets` under `ar`: A's row `name == "مضخة التبريد 2"`, B's row its main name; under `en`: A `"Chiller pump 2"`; `GET /assets/export?format=csv&c=name&t=Name` under `ar` contains `مضخة التبريد 2`.
  - `Work_order_references_follow_the_reader` — location "Plant room"/"غرفة المعدات" under building "Block A"/"المبنى أ", asset A there, symptom "Leak"/"تسريب", category "Pumps"/"مضخات"; work order on A with the symptom; `GET /work-orders/{id}` under `ar`: asset name `مضخة التبريد 2`, location path contains `المبنى أ` and `غرفة المعدات`, symptom name `تسريب`; `GET /assets/{id}` under `ar`: category name `مضخات` (reference), own `name` raw.
  - `Search_finds_records_by_either_text` — `GET /assets?search=التبريد` under `en` finds A; `GET /parts?search=<arabic>` finds the part; `GET /locations?search=<arabic>` (or the tree's search) finds the location.
  - `Sorting_follows_the_text_the_reader_sees` — assets "Alpha"/"ي" and "Beta"/"أ": `GET /assets?sort=name` under `ar` lists Beta's row (`أ`) first; under `en` Alpha first.
  - `Location_paths_and_names_keep_site_filters` (Review Focus 3) — a person limited to site 1; a site-2 location with an Arabic name; under `ar` none of the person's payloads (`/locations`, `/assets`, a site-1 work order's path) contain the site-2 Arabic name.
  - `Phone_and_screens_fall_back_when_arabic_is_missing` (Review Focus 2) — asset with `nameAr = null`: `/assets` and a work order reference under `ar` show the main name, never empty.
  - `Purchasing_keeps_the_parts_main_name` — part "Bearing 6204"/"رولمان 6204" on a purchase order line; `GET /purchase-orders/{id}` under `ar` shows `Bearing 6204`.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualDisplayTests"` — Expected: FAIL on the Arabic assertions.
- [ ] **Step 3: Implement** the S rule across the files listed: capture `var ar = Bilingual.Arabic;` at the top of each handler/query method and replace each displayed `x.Name` (and location-name dictionaries feeding `PathText`) with the chosen expression; add `|| x.NameAr.Contains(term)` (or the equivalent `EF.Functions.Like`) to name searches; switch name sorts to the chosen expression. Leave R endpoints from Task 1 untouched.
- [ ] **Step 4: Run** the Task 2 tests, then `wt-test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(0.28.0): names of places, parts and lists shown in the reader's language`.

### Task 3: Templates, PMs and work orders (server)

**Files:**
- Modify: `server/FourC.Cmms.Core/Pm/StepCopy.cs` (`ToWorkOrder`, `Copy`, `Match` keep `DescriptionAr`), `Features/PmGenerator.cs` (title + `TitleAr` from the PM; safety notes into `SafetyNotes`/`SafetyNotesAr`, no longer appended to the description), `Features/WorkGroups.cs` (same), `Features/WorkOrderEndpoints.cs` (create from template: `TitleAr` from input else template; template safety notes copied), `Features/Checklists.cs` (follow-up work: `TitleAr`), `Features/AssetChecklistEndpoints.cs`, `Features/TechActions.cs` (phone-made work from a template), `Features/PmEndpoints.cs` (template copy keeps `NameAr`/`TitleAr`/`SafetyNotesAr`; template and schedule list rows S), S rule for work order titles in `WorkOrderEndpoints.cs` (list/export), `PlanningBoardEndpoints.cs`, `PmCalendarEndpoints.cs`, `WorkGroupEndpoints.cs`, `WorkRequestEndpoints.cs` (linked work), `AssetEndpoints.cs` (asset history/work tab), `TimesheetEndpoints.cs`, `ContractEndpoints.cs`, `ActivityEndpoints.cs`, `AlertEndpoints.cs`; S rule for template/PM names wherever shown as references
- Test: `server/FourC.Cmms.Tests/BilingualWorkTests.cs`

**Interfaces:**
- Consumes: Task 1 properties, `Bilingual`.
- Produces: `StepCopy.ToWorkOrder(PmTaskStep s, Guid workOrderId, int sequence, Guid? assetId)` copies `DescriptionAr` (cut to 1000 like the main text); work orders from templates/PMs carry `TitleAr`, `SafetyNotes`, `SafetyNotesAr`.
- Title sources (both texts from the same source): PM work → `PmSchedule.Name`/`NameAr`; template work → `PmTask.Title ?? PmTask.Name` and `PmTask.TitleAr ?? PmTask.NameAr`; follow-up → main title as today, `TitleAr = string.Format(Translator.T(FromStep, "ar"), <template Arabic title>, step.Sequence, source.Number)` when the template has an Arabic title or name, else null.

- [ ] **Step 1: Write the failing tests** in `BilingualWorkTests.cs`:
  - `Pm_generation_copies_arabic_title_and_steps` — template with step "Check coupling"/"افحص الوصلة", PM "Monthly chiller PM"/"صيانة المبرد الشهرية"; generate; the work order detail has `title == "Monthly chiller PM"`, `titleAr == "صيانة المبرد الشهرية"`, step `descriptionAr == "افحص الوصلة"`.
  - `Generation_inside_an_arabic_request_keeps_main_texts_in_main_fields` (Review Focus 5) — the generate call sent with `Accept-Language: ar`; detail under `en` has the English title and step text in `title`/`description`.
  - `Safety_notes_get_their_own_fields_not_the_description` — template `safetyNotes "Lock out the pump"`/`"اقفل المضخة"`; generated work order: `safetyNotes`, `safetyNotesAr` set; `description` does not contain `Lock out`.
  - `New_from_template_copies_arabic_title_steps_and_safety_notes` — `POST /work-orders` with `pmTaskId` (no `titleAr` sent): `titleAr` = template's Arabic title, steps' Arabic texts and safety notes copied.
  - `Changing_the_template_later_leaves_the_work_order` — edit the template step's Arabic text after creation; the work order keeps the old one.
  - `Follow_up_work_carries_the_arabic_title` — failed step with a follow-up template that has an Arabic title; the follow-up work order's `titleAr` contains that Arabic title.
  - `Work_order_titles_follow_the_reader_where_shown` — `GET /work-orders` rows and `GET /planning/board` (or the board endpoint) under `ar` show `صيانة المبرد الشهرية`; under `en` the main title.
  - `Template_copy_keeps_arabic_texts` — copy a template; the copy keeps `nameAr`, `titleAr`, `safetyNotesAr` and step `descriptionAr`.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualWorkTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** the copies, the safety-notes move, and the S rule for titles and template/PM names in the files listed.
- [ ] **Step 4: Run** Task 3 tests, then `wt-test` — Expected: all pass (existing tests that asserted "Safety:" inside a generated description are updated to assert the safety fields instead, with a ledger ruling).
- [ ] **Step 5: Commit** `feat(0.28.0): work orders carry Arabic titles, steps and safety notes from their source`.

### Task 4: Reports, labels and AI pages (server)

**Files:**
- Modify: `Features/ReportEndpoints.cs` (every asset/location/part/category/code/template/PM name and work order title in report rows; their Excel/CSV exports follow because they read the same rows), `Features/BudgetReport.cs`, `Features/LabelEndpoints.cs` (label name lines), `Features/Ai/AiDiagnose.cs` and `Features/Ai/AiReport.cs` (names returned for display only; prompts unchanged), `Features/SlaQueries.cs`, `Features/CostCentreQueries.cs`, `Features/AvailabilityQueries.cs` where they return names
- Test: `server/FourC.Cmms.Tests/BilingualReportTests.cs`

**Interfaces:** Consumes Task 1–3. Produces S texts in report rows, labels and AI page payloads.

- [ ] **Step 1: Write the failing tests:**
  - `Reports_show_names_in_the_readers_language` — a work order on asset A with symptom "Leak"/"تسريب"; `GET /api/reports/wo-backlog` under `ar` has A's Arabic name in its rows; a failure report (symptom/cause/remedy breakdown) under `ar` shows `تسريب`; under `en` main names.
  - `Report_export_follows_the_language` — the same report's xlsx/csv download under `ar` contains the Arabic name.
  - `Labels_print_names_in_the_printers_language` — `POST /labels` for asset A under `ar` returns its Arabic name; under `en` the main name.
  - `Ai_prompts_keep_main_names` — with the fake AI client, a diagnosis for a work order on A under `ar`: the prompt the fake received contains `Chiller pump 2`, not the Arabic name.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualReportTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** the S rule in the files listed.
- [ ] **Step 4: Run** Task 4 tests, then `wt-test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(0.28.0): reports, labels and AI pages show names in the reader's language`.

### Task 5: The phone

**Files:**
- Modify: `Features/TechEndpoints.cs` (jobs, job detail, bundle assets/locations, pickers: chosen names, titles, steps, notes to technician; safety notes; bundle rows gain `otherName`), `Features/PhoneStock.cs` (part names chosen), `Features/TechActions.cs` (answers that echo names)
- Modify web: `web/src/tech/types.ts` (`otherName?: string | null` on bundle assets/locations; `safetyNotes?: string | null` on job), `web/src/tech/` search helper used by the asset/location pickers (match `name` or `otherName`), `web/src/tech/JobPage.tsx` (Safety alert), `web/src/tech/TechApp.tsx` or `useTech.tsx` (language switch reloads the stored copy)
- Test: `server/FourC.Cmms.Tests/BilingualPhoneTests.cs`; Vitest `web/src/tech/bilingualSearch.test.ts`

**Interfaces:**
- Consumes: `Bilingual.Pick`, `Bilingual.PickNote`, `Bilingual.Other`.
- Produces: tech bundle `assets[]` and `locations[]` rows `{ …, name (chosen), otherName (Bilingual.Other) }`; job payload `safetyNotes` (chosen); web `matchesPlace(row: { name: string; otherName?: string | null }, term: string): boolean` in `web/src/tech/bilingualSearch.ts`.

- [ ] **Step 1: Write the failing server tests:**
  - `Phone_jobs_show_names_steps_and_notes_in_the_phones_language` — a work order from the Arabic template on asset A (with notes to technician in both languages); `GET /tech/jobs` and the job detail under `ar`: title, asset name, step text, notes to technician and safety notes in Arabic; under `en` main texts.
  - `Phone_bundle_carries_the_other_text_for_search` — bundle under `ar`: A's row `name == "مضخة التبريد 2"`, `otherName == "Chiller pump 2"`; under `en`: `otherName == "مضخة التبريد 2"`; an asset without Arabic has `otherName == null`.
- [ ] **Step 2: Write the failing Vitest** `bilingualSearch.test.ts`: `matchesPlace({ name: 'مضخة التبريد 2', otherName: 'Chiller pump 2' }, 'chiller')` is true; `matchesPlace({ name: 'Pump' }, 'مض')` is false; match is case-insensitive.
- [ ] **Step 3: Run** `wt-test "FullyQualifiedName~BilingualPhoneTests"` and `wt-vitest` — Expected: FAIL.
- [ ] **Step 4: Implement** the server changes, `matchesPlace`, the pickers using it, the Safety alert on the job page (same style as the notes alert, `t('tech.safety')`), and the language switch calling the existing stored-copy refresh after `i18n.changeLanguage`.
- [ ] **Step 5: Run** Task 5 tests, `wt-test`, `wt-vitest` — Expected: all pass.
- [ ] **Step 6: Commit** `feat(0.28.0): the phone shows names, steps and notes in its language`.

### Task 6: E-mails and push

**Files:**
- Modify: `Features/EmailTemplates.cs`, `Features/EmailOutbox.cs`, `Features/OverdueNotifier.cs`, `Features/ContractNotifier.cs`, `Features/RequestFeedback.cs`, `Features/Push.cs`, and the callers that build e-mail/push content with work order titles or asset/location names (`WorkOrderEndpoints.cs`, `WorkRequestEndpoints.cs`, `PmGenerator.cs` notifications)
- Test: `server/FourC.Cmms.Tests/BilingualMailTests.cs`

**Interfaces:** Names go into e-mail content as `LocalWords(main, ar ?? main)` (existing type in `EmailTemplates.cs`) or are picked inside the recipient's `Translator.Use` scope; supplier e-mails unchanged (organization language, main names).

- [ ] **Step 1: Write the failing tests:**
  - `Email_to_an_arabic_recipient_uses_arabic_names` — recipient user with `language = "ar"`, another with `"en"`; assign both to a work order made from the Arabic template on asset A (or trigger the assignment e-mail); the queued `OutboundEmails` body for the Arabic recipient contains `مضخة التبريد 2` and the Arabic title; the English one contains `Chiller pump 2` and the main title.
  - `Push_to_an_arabic_recipient_uses_the_arabic_title` — with the test push sender, the Arabic recipient's message contains the Arabic title.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualMailTests"` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** Task 6 tests, then `wt-test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(0.28.0): e-mails and push messages name things in each recipient's language`.

### Task 7: Excel import and download

**Files:**
- Modify: `Features/Import/ImportSheets.cs` (columns), `Features/Import/SheetImporters.cs` (read/apply/current values), `server/FourC.Cmms.Api/Localization/ar.json` if headings need entries
- Test: `server/FourC.Cmms.Tests/BilingualImportTests.cs`

**Interfaces:** New columns right after their main column — key, English heading, Arabic heading:
- `nameAr`, "Name (Arabic)", "الاسم (عربي)": Locations, Categories, Symptoms, Causes, Remedies, Assets, Parts, PmTasks ("Work templates"), PmSchedules
- `titleAr`, "Title (Arabic)", "العنوان (عربي)": PmTasks
- `safetyNotesAr`, "Safety notes (Arabic)", "ملاحظات السلامة (عربي)": PmTasks
- `notesToTechnicianAr`, "Notes to technician (Arabic)", "ملاحظات للفني (عربي)": Assets, Locations
- `descriptionAr`, "Description (Arabic)", "الوصف (عربي)": PmSteps
Blank cell keeps the current value; a value replaces it; same max lengths and messages as the forms.

- [ ] **Step 1: Write the failing tests** (using `ImportFiles` helpers `Book`, `Sheet`, `RunAsync`):
  - `Import_fills_arabic_columns` — Assets sheet `["Asset number","Name","Name (Arabic)","Notes to technician (Arabic)"]` row `["A1","Chiller pump 2","مضخة التبريد 2","افصل أولاً"]`; PmSteps row with `Description (Arabic)`; detail GETs return the Arabic texts.
  - `Arabic_headings_are_read_too` — the same Assets sheet with Arabic headings (`"الاسم (عربي)"`) fills `nameAr`.
  - `Blank_arabic_cell_keeps_the_current_text` — re-import with the column present and the cell blank: `nameAr` unchanged; re-import without the column: unchanged.
  - `Current_data_download_includes_arabic_columns` — `GET /api/import/current` workbook's Assets sheet has the `Name (Arabic)` heading and A1's Arabic name; re-importing that file unchanged leaves `name` as the main text (Review Focus 1 for Excel).
  - `Too_long_arabic_cell_is_a_problem_row` — over-length `Name (Arabic)` reports the row with the same message as the form.
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BilingualImportTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** the columns and the importers (lookup sheets pass `NameAr` through `LookupInput`).
- [ ] **Step 4: Run** Task 7 tests, then `wt-test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(0.28.0): Arabic columns in the Excel import and download`.

### Task 8: AI drafts

**Files:**
- Modify: `server/FourC.Cmms.Core/Ai/Ai.cs` (`AiFeature.Translation`)
- Create: `server/FourC.Cmms.Api/Features/Ai/AiTranslate.cs` (endpoints + list definitions), map it where `AiEndpoints` are mapped
- Create web: `web/src/components/TranslateButton.tsx` (button + review modal), `web/src/components/translations.ts` (pure helpers), `web/src/components/translations.test.ts`
- Modify web pages to place the button: `pages/assets/AssetsPage.tsx`, `pages/locations/LocationsPage.tsx`, `pages/inventory/InventoryPage.tsx` (parts), `pages/pm/PmPage.tsx` (templates tab and schedules tab), `pages/settings/SettingsPage.tsx` (categories, symptoms, causes, remedies, cancel reasons), `pages/settings/RequestTypesSettings.tsx`; AI usage page shows the new feature's name (`pages/settings/AiUsageSettings.tsx` / locales)
- Test: `server/FourC.Cmms.Tests/AiTranslateTests.cs` (with `FakeAiClient`)

**Interfaces:**
- `POST /api/translations/draft` body `{ list: string }` → `200 { field: string | null, items: [{ id: Guid, text: string, draft: string }], couldNot: int, remaining: int }`. `field` null and empty items when nothing is missing.
- `POST /api/translations/save` body `{ list: string, field: string, items: [{ id: Guid, ar: string }] }` → `200 { saved: int, skipped: int }`; an item whose Arabic text is no longer empty is skipped; blank `ar` skipped.
- Lists → fields in order → permission:
  - `assets` → `name`, `notesToTechnician` → `AssetsEdit`
  - `locations` → `name`, `notesToTechnician` → `LocationsManage`
  - `parts` → `name` → `InventoryManage`
  - `categories`, `symptoms`, `causes` (failure codes), `remedies`, `cancel-reasons`, `request-types` → `name` → `SettingsManage`
  - `templates` → `name`, `title`, `safetyNotes`, `steps` (PmTaskStep.Description) → `PmManage`
  - `pm-schedules` → `name` → `PmManage`
- One press drafts the first field (in that order) that has texts with a main text and no Arabic text, among records the person can see (normal query filters), ordered by code/number (steps: template code, then sequence); batch size `name`/`title` 50, `steps` 100, `notesToTechnician`/`safetyNotes` 20. `remaining` = missing texts of that field after this batch.
- `AiAsk(AiFeature.Translation, Instructions, SharedContext, Prompt, Schema, AiEffort.Low, MaxTokens: 8000)` via `AiService.RunAsync(AiFeature.Translation, null, null, ask, ct)` (status, allowance and call log as for reports).
  - Instructions (exact): "You translate short texts from a maintenance management system into Arabic for maintenance technicians in Egypt. Use clear Modern Standard Arabic with the technical words technicians use. Keep codes, numbers, units, model numbers, part numbers and brand names exactly as written. Translate each item on its own; never add or drop information. Answer with one Arabic text for every input id."
  - SharedContext: "Existing translations in this organization (keep the same words):" then up to 200 lines `main ⇒ arabic` of the same list and field, most recently changed first; empty line list when none.
  - Prompt: JSON array `[{ "id": "…", "text": "…" }]`.
  - Schema: `{ "type":"object", "properties": { "items": { "type":"array", "items": { "type":"object", "properties": { "id": {"type":"string"}, "ar": {"type":"string"} }, "required": ["id","ar"], "additionalProperties": false } } }, "required": ["items"], "additionalProperties": false }`.
  - Answer handling: drafts kept only for ids that were sent, non-blank, within the field's max length; the rest count in `couldNot`. A refusal returns all as `couldNot`.
- Web: `TranslateButton({ list }: { list: TranslationList })` shown when `useAiStatus()` says enabled and the person has the list's permission; `translations.ts` exports `type TranslationList`, `toRows(result): ReviewRow[]` (all ticked) and `toSaveBody(list, field, rows): { list, field, items }` (ticked, trimmed, non-blank only).

- [ ] **Step 1: Write the failing server tests** in `AiTranslateTests.cs`:
  - `Draft_sends_only_texts_missing_arabic` — 3 assets, one already with Arabic; fake answers per id; result `field == "name"`, 2 items, the fake's prompt has exactly those 2 ids.
  - `Draft_takes_at_most_the_batch` — 55 assets without Arabic → 50 items, `remaining == 5`.
  - `Draft_moves_to_the_next_field_when_names_are_done` — all asset names have Arabic, two have notes to technician → `field == "notesToTechnician"`.
  - `Template_steps_are_drafted_after_names_titles_and_safety_notes`.
  - `Save_skips_texts_filled_meanwhile` (Review Focus 4) — draft, then set one asset's Arabic name through the form, then save both → `saved == 1, skipped == 1`, the form's text kept.
  - `Bad_answers_draft_only_valid_items` (Review Focus 4) — fake answers with an unknown id, a missing id and an over-long text → only valid drafts, `couldNot == 2` (missing + over-long; the unknown id ignored).
  - `Drafting_counts_one_ai_use_logged_as_translation` — one `AiCalls` row with feature `Translation`.
  - `Drafting_is_refused_when_ai_is_off_or_used_up` — the existing assistant messages and status codes.
  - `Drafting_and_saving_need_the_lists_edit_permission` — a person without `AssetsEdit` gets 403 on both.
  - `Shared_context_carries_existing_pairs` — an existing translated asset appears in the fake's shared context as `Pump ⇒ مضخة`.
- [ ] **Step 2: Write the failing Vitest** `translations.test.ts`: `toRows` ticks all rows and keeps `id/text/draft`; `toSaveBody` drops unticked and blank rows and trims drafts.
- [ ] **Step 3: Run** `wt-test "FullyQualifiedName~AiTranslateTests"` and `wt-vitest` — Expected: FAIL.
- [ ] **Step 4: Implement** the endpoints, the feature value (AI usage labels in both locales), the web helpers, `TranslateButton` (modal: table of main text, editable Arabic draft `dir="rtl"`, tick; "Save" then "Draft the next batch" when `remaining > 0`; "Every text here has its Arabic" when nothing is missing; `couldNot` shown as a note), and the button on each page.
- [ ] **Step 5: Run** Task 8 tests, `wt-test`, `wt-vitest` — Expected: all pass.
- [ ] **Step 6: Commit** `feat(0.28.0): AI drafts for missing Arabic texts`.

### Task 9: Web forms, pages and docs

**Files:**
- Create: `web/src/components/bilingual.ts`, `web/src/components/bilingual.test.ts`
- Modify forms (Arabic field beside each main text, `dir="rtl"`, sent in the body): `pages/assets/AssetFormPage.tsx` (name, notes to technician), `pages/locations/LocationForms.tsx` (name, notes to technician), `pages/inventory/PartFormPage.tsx`, `pages/settings/SettingsPage.tsx` (categories and the symptoms/causes/remedies/cancel-reasons lookup editors: an Arabic column like `UnitsSettings`), `pages/settings/RequestTypesSettings.tsx`, `pages/pm/TemplatePage.tsx` (name, title, safety notes) and `pages/pm/TemplateSteps.tsx` (each step's Arabic text), `pages/pm/PmScheduleFormPage.tsx`, `pages/work/WorkOrderFormPage.tsx` (Arabic title, safety notes in both languages; when a template is chosen, prefill `titleAr` as the title is prefilled), `pages/work/WorkOrderDetailPage.tsx` and `pages/work/ChecklistPanel.tsx` (steps editor: Arabic text per step; show texts with `pick`)
- Modify display of R payloads with `pick`: `pages/assets/AssetDetailPage.tsx`, `pages/locations/LocationsPage.tsx` (location detail), `pages/inventory/PartDetailPage.tsx`, `pages/pm/TemplatePage.tsx` (view mode), `pages/pm/PmScheduleFormPage.tsx` (view header if any), `pages/work/WorkOrderDetailPage.tsx` (title, steps, a Safety section), `pages/work/WorkOrderPrintPage.tsx` (title, steps, Safety section inside the description block), `components/CodeSelect.tsx` and the category/request-type/cancel-reason pickers that read R lists
- Modify: `web/src/api/types.ts` (new optional fields), `web/src/locales/en/*.json`, `web/src/locales/ar/*.json`, `docs/API.md`
- Test: `web/src/components/bilingual.test.ts`, plus form-body tests where a form already has a test file (e.g. a `*.test.ts` beside the form's pure helpers); otherwise a pure `toBody` helper extracted for the asset form and tested

**Interfaces:**
- Produces: `pick(main: string, ar?: string | null, lang: string = i18n.language): string` and `pickNote(main?: string | null, ar?: string | null, lang?: string): string | null` in `web/src/components/bilingual.ts` (Arabic when `lang` starts with `ar` and `ar` is non-blank, else main).
- i18n keys (en / ar): `bilingual.nameAr` "Name (Arabic)" / "الاسم (عربي)"; `bilingual.titleAr` "Title (Arabic)" / "العنوان (عربي)"; `bilingual.notesAr` "Notes to technician (Arabic)" / "ملاحظات للفني (عربي)"; `bilingual.safetyAr` "Safety notes (Arabic)" / "ملاحظات السلامة (عربي)"; `bilingual.stepAr` "Step (Arabic)" / "الخطوة (عربي)"; `bilingual.fill` "Fill in missing Arabic" / "إكمال النصوص العربية الناقصة"; `bilingual.save` "Save" / "حفظ"; `bilingual.next` "Draft the next batch" / "صياغة الدفعة التالية"; `bilingual.none` "Every text here has its Arabic" / "كل النصوص هنا لها نص عربي"; `bilingual.couldNot` "{{count}} couldn't be drafted" / "تعذرت صياغة {{count}}"; `workOrders.safety` "Safety" / "السلامة"; `tech.safety` "Safety" / "السلامة".

- [ ] **Step 1: Write the failing Vitest** `bilingual.test.ts`: `pick('Pump','مضخة','ar') === 'مضخة'`; `pick('Pump','مضخة','en') === 'Pump'`; `pick('Pump','  ','ar') === 'Pump'`; `pick('Pump', null, 'ar-EG') === 'Pump'`; `pickNote(null, null, 'ar') === null`. And for the asset form's body helper: a form opened under Arabic with `name: 'Chiller pump 2', nameAr: 'مضخة التبريد 2'` produces a body with `name === 'Chiller pump 2'` (Review Focus 1).
- [ ] **Step 2: Run** `wt-vitest` — Expected: FAIL.
- [ ] **Step 3: Implement** the helper, forms, pages, pickers, types, locales and `docs/API.md` (names follow `Accept-Language`; edit endpoints return `…Ar` fields raw; list the new fields and the two translation endpoints).
- [ ] **Step 4: Run** `wt-vitest` and `wt-web` — Expected: all pass, build clean.
- [ ] **Step 5: Run** the whole server suite `wt-test` once more — Expected: all pass.
- [ ] **Step 6: Commit** `feat(0.28.0): Arabic fields on forms and texts in the reader's language on pages`.
