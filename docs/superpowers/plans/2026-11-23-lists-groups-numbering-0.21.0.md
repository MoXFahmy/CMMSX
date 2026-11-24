# Editable lists, brands and models, units, asset groups, yearly numbering — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CMX 0.21.0 — organization names/colours/order/hidden for four fixed lists, brand/model and unit lists, asset groups (filters, report filters, PM target that follows the group), and optional yearly numbering.

**Architecture:** List labels live in `TenantSettings.ListLabels` (no schema change); one server helper (`Lists`) names and parses values, and the web/phone override their i18n keys from the same settings. Brands, models, units and asset groups are new tenant tables in one migration (`ListsAndGroups`); records keep their existing text columns as the display copy and a reconcile routine links texts to list rows. Yearly numbering is a change inside `Numbering.For`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server, named filters Tenant/Site), xUnit SkippableFact + ApiFixture; React 19 + Mantine 8 + TanStack Query + i18next, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-lists-groups-numbering-0.21.0-design.md`

## Global Constraints

- Every new text in en + ar (web `web/src/locales/{en,ar}.json` via loc-add.py; server `server/FourC.Cmms.Api/Localization/ar.json` via ar-add.py; Arabic plurals need _zero/_one/_two/_few/_many/_other).
- No new permissions: lists, units, numbering → *Manage settings* (`settings.manage`); brands, models, asset groups → *Edit assets* (`assets.edit`); reading groups/brands → *View assets*.
- Older clients never clear new fields: new optional inputs use the `Guid? X = null, bool? ManagesX = null` pattern (apply only when `ManagesX == true`, or on create).
- Organization-wide reads that decide or write (uniqueness, in-use, rename propagation, reconcile, numbering) use `AllSites()`.
- Meanings stay the enums: `WorkType`, `WorkPriority`, `AssetStatus`, `Criticality` are not changed.
- Gate with CI's exact build (`wt-ci`), plus `wt-vitest` and `wt-web`.

## Review Focus

1. **A renamed priority clashing with a built-in name** ("High" renamed "Urgent"): saving refuses; an import cell always maps to one value. Test: `List_names_cannot_clash_and_import_reads_both` (Task 1).
2. **Hidden value on an existing record**: editing that record keeps it, and a list filter still finds it. Test: `Hidden_value_stays_on_existing_records` (Task 1) + Vitest `listOptions keeps the current hidden value` (Task 7).
3. **Brand rename/merge propagates and re-running reconcile changes nothing.** Test: `Renaming_and_merging_brands_updates_assets` and `Reconcile_is_idempotent` (Tasks 2–3).
4. **Group PM membership changes between generations; empty and inactive groups.** Test: `Group_pm_follows_membership` (Task 5).
5. **Yearly numbering across 31 Dec → 1 Jan in the organization's time zone, with a site prefix.** Test: `Yearly_numbers_restart_in_the_organizations_year` (Task 6).

---

### Task 1: Editable lists on the server

**Files:**
- Create: `server/FourC.Cmms.Core/Tenancy/ListLabels.cs` (`ListLabel` class), `server/FourC.Cmms.Api/Infrastructure/Lists.cs`
- Modify: `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (`TenantSettings.ListLabels`), `server/FourC.Cmms.Api/Features/AdminEndpoints.cs` (`PUT /api/settings/lists`; general PUT keeps stored labels when input's are null), `server/FourC.Cmms.Api/Infrastructure/TableExport.cs` (enum cells of the four lists), `server/FourC.Cmms.Api/Features/ReportEndpoints.cs` (rows/groups naming types, priorities, statuses, criticality), `server/FourC.Cmms.Api/Features/EmailOutbox.cs` (priority at ~552/555), `server/FourC.Cmms.Api/Features/Import/ImportSheets.cs` + `ImportValues.cs` (choice text and parse through `Lists`), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/ListLabelTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ListLabel { public string? Name; public string? NameAr; public string? Color; public int? Order; public bool Hidden; }` (properties).
  - `TenantSettings.ListLabels : Dictionary<string, Dictionary<string, ListLabel>>` keyed `"workType" | "priority" | "assetStatus" | "criticality"`, then the enum name.
  - `Lists.Key(Type enumType) : string?` (null for other enums); `Lists.Name(TenantSettings? s, Enum value, string? language = null) : string` (organization name in the language, else `Translator.T(TableExport.Words(value.ToString()), language)`); `Lists.Parse(TenantSettings? s, Type enumType, string text) : Enum?` (built-in English, built-in Arabic, organization en/ar; case/space-insensitive via `ImportSheets.Normal`); `Lists.Problem(Dictionary<string, Dictionary<string, ListLabel>> labels) : string?`; `Lists.Palette : string[]` = Mantine colours `gray, red, pink, grape, violet, indigo, blue, cyan, teal, green, lime, yellow, orange`.
  - `PUT /api/settings/lists` body `{ listLabels }` → 200 / 400.

- [ ] **Step 1: Write the failing tests** in `ListLabelTests.cs`:
  - `List_labels_save_and_show_in_reports_and_exports`: PUT labels `priority.Urgent = { name: "Emergency", nameAr: "طارئ", color: "red", order: 0 }`; a work order with priority Urgent; `wo-backlog` report row group reads "Emergency"; the work order list export (xlsx endpoint used by the list toolkit) cell reads "Emergency"; with `Accept-Language: ar` the report reads "طارئ".
  - `List_names_cannot_clash_and_import_reads_both`: PUT `priority.High = { name: "Urgent" }` → 400 containing "This name is already used by Urgent."; PUT `priority.Urgent = { name: "Emergency" }` → 200; an import check of the PM schedules sheet (it has a `priority` column) reads both "Emergency" and "Urgent" as Urgent.
  - `Hidden_value_stays_on_existing_records`: PUT `workType.Project.hidden = true`; an existing Project work order still saves (PUT with type Project) and the list filter `type=Project` finds it.
  - `Every_list_keeps_a_visible_value`: hiding all four priorities → 400 "Leave at least one priority showing."; colour "purple" → 400 "Choose a colour from the list."; a 51-character name → 400.
- [ ] **Step 2: Run** `wt-test "ListLabelTests"` — Expected: fail (endpoint missing / built-in names).
- [ ] **Step 3: Implement.** `Lists.Problem` messages (exact): "Leave at least one {list word} showing." (`work type`, `priority`, `asset status`, `criticality level`), "Choose a colour from the list.", "A name can be at most 50 characters.", "This name is already used by {built-in English name}." Clash = equal (normalized) to another value's built-in English, built-in Arabic or organization name in either language. `TableExport.Rows` gains `TenantSettings? settings = null` and formats the four enums with `Lists.Name`; callers of the list exports pass settings. Report `Localized`/row builders call `Lists.Name` for type/priority/status/criticality labels (load settings once per report). Import `ChoiceText`/`Choice` get the settings from `ImportContext` (pass through) and use `Lists.Name` / `Lists.Parse` for the four enums. Arabic: add the four messages and any built-in names missing from ar.json.
- [ ] **Step 4: Run** `wt-test "ListLabelTests|Import|Report"` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(lists): organization names, colours, order and hidden values for the fixed lists`

### Task 2: Schema, brands and models

**Files:**
- Create: `server/FourC.Cmms.Core/Assets/Brands.cs` (`Brand`, `BrandModel`), `server/FourC.Cmms.Core/Assets/AssetGroups.cs` (`AssetGroup`, `AssetGroupMember`), `server/FourC.Cmms.Core/Inventory/Units.cs` (`Unit`), `server/FourC.Cmms.Api/Features/BrandEndpoints.cs`, migration `ListsAndGroups`
- Modify: `Core/Assets/Assets.cs` (`Asset.BrandId`, `Asset.ModelId`), `Core/Pm/Pm.cs` (`PmSchedule.AssetGroupId`), `CmmsDbContext.cs` (DbSets, config, filters, `NumberSequence.Name` max length 50 → 100), `AssetEndpoints.cs` (input, detail, list filters), `Program.cs`
- Test: `server/FourC.Cmms.Tests/BrandTests.cs`

**Interfaces:**
- Produces:
  - `Brand : TenantEntity { string Name; bool IsActive = true; List<BrandModel> Models }`, `BrandModel : TenantEntity { Guid BrandId; string Name; bool IsActive = true }`; unique (TenantId, Name) and (TenantId, BrandId, Name), filtered `IsDeleted = 0`. SQL Server's default collation is case-insensitive — brand/model names rely on it.
  - `Unit : TenantEntity { string Symbol (≤30); string Name; string? NameAr; bool IsActive = true }`; unique (TenantId, Symbol) with a **case-sensitive** collation on `Symbol` (`Latin1_General_100_CS_AS`).
  - `AssetGroup : TenantEntity, ICodeLookup { string Code; string Name; Guid? SiteId; bool IsActive = true; List<AssetGroupMember> Members }` with the Team-style site filter (`SiteId == null || CurrentSiteIds.Contains(SiteId)`); `AssetGroupMember { Guid AssetGroupId; Guid AssetId }` composite key.
  - `Asset.BrandId`, `Asset.ModelId`, `PmSchedule.AssetGroupId` (Guid?, NoAction FKs).
  - `GET /api/brands` (assets.view) → `[{ id, name, isActive, models: [{ id, name, isActive }] }]`; `POST /api/brands`, `PUT /api/brands/{id}`, `DELETE /api/brands/{id}`, `POST /api/brands/{id}/models`, `PUT /api/brand-models/{id}`, `DELETE /api/brand-models/{id}` (assets.edit). PUT bodies carry `merge: bool` — renaming onto an existing name returns 409 `{ mergeWith: id }` unless `merge = true`, which moves assets to the kept row and deletes the other.
  - `AssetInput` gains `Guid? BrandId = null, Guid? ModelId = null, bool? ManagesBrand = null`; `GET /api/assets` gains `brandId`, `modelId` filters; asset detail returns `brandId`, `modelId`.
  - `internal static Task SyncBrandTextAsync(CmmsDbContext db, Guid brandId, CancellationToken ct)` / `SyncModelTextAsync` — set every asset's `Manufacturer`/`Model` text from the row's name (ExecuteUpdate, AllSites).

- [ ] **Step 1: Write the failing tests** in `BrandTests.cs`:
  - `Asset_picks_a_brand_and_model_and_keeps_the_text`: create brand "Grundfos" + model "CR 10"; create asset with `brandId, modelId, managesBrand: true` → detail `manufacturer = "Grundfos"`, `model = "CR 10"`; a model of another brand → 400 "Choose a model of the chosen brand."; `GET /api/assets?brandId=` returns it.
  - `Renaming_and_merging_brands_updates_assets`: rename brand → asset text follows; rename "Grundfos Pumps" onto "Grundfos" → 409 with `mergeWith`; again with `merge: true` → assets now on "Grundfos", the other brand gone.
  - `Brands_in_use_cannot_be_deleted`: DELETE a brand used by an asset → 400 "This brand is in use. Make it inactive instead."
  - `Old_clients_keep_the_brand`: PUT an asset without `managesBrand` → ids unchanged.
- [ ] **Step 2: Run** `wt-test "BrandTests"` — Expected: fail.
- [ ] **Step 3: Implement** entities, configuration, migration (`wt-ef ListsAndGroups`), endpoints, `AssetInput` rules: when `ManagesBrand == true` (or on create), set ids (model requires brand, model must belong to brand, inactive refused unless unchanged — "Choose an active brand.") and copy names into the text columns; when not managed and the `Manufacturer` text changes, clear both ids (the reconcile re-links). In-use check covers assets (AllSites).
- [ ] **Step 4: Run** `wt-test "BrandTests|Asset"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(assets): brands and models`

### Task 3: Units and the reconcile routine

**Files:**
- Create: `server/FourC.Cmms.Api/Features/UnitEndpoints.cs`, `server/FourC.Cmms.Core/Services/ReferenceLists.cs`
- Modify: `Program.cs` (startup run after `DueBackfill`), `Features/Import/ImportRun.cs` (after a saving import), `server/FourC.Cmms.LegacyImport/Program.cs` (end of a run), part / measurement point / PM task step / template step endpoints (unit must be a listed symbol — or added, see below)
- Test: `server/FourC.Cmms.Tests/UnitTests.cs`, `server/FourC.Cmms.Tests/ReconcileTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/units` (signed in) → `[{ id, symbol, name, nameAr, isActive }]`; `POST/PUT/DELETE /api/units[/{id}]` (settings.manage); PUT with a new symbol updates `Part.UnitOfMeasure`, `MeasurementPoint.Unit`, PM task / template step `Unit` (ExecuteUpdate, AllSites, exact match) — work-order steps are left alone; onto an existing symbol → 409 `{ mergeWith }` unless `merge = true`.
  - `ReferenceLists.ReconcileAsync(CmmsDbContext db, Guid tenantId, CancellationToken ct) : Task<(int Brands, int Models, int Units)>` — creates missing brands (distinct trimmed `Asset.Manufacturer`, case-insensitive, first spelling met), models (distinct brand + trimmed `Asset.Model`), units (distinct trimmed unit texts on parts, measurement points, PM task and template steps; case kept), and sets missing `Asset.BrandId/ModelId`. Idempotent. Plus `ReconcileAllAsync(CmmsDbContext db, CancellationToken ct)` over tenants that have unlinked texts.
  - New organizations get the starter units `pcs, m, L, kg, h, °C, bar, kWh` (where tenants are created).

- [ ] **Step 1: Write the failing tests:**
  - `UnitTests.Renaming_a_unit_updates_parts_points_and_template_steps_not_work_orders`: unit "PCS" used by a part, a point, a template step and a generated work order's step; rename to "pcs" → the first three read "pcs", the work-order step still "PCS".
  - `UnitTests.Units_are_case_sensitive`: "mW" and "MW" both save; a second "mW" → 400 "This symbol is already used."
  - `ReconcileTests.Reconcile_links_texts_and_is_idempotent`: assets with manufacturer " grundfos", "Grundfos", "GRUNDFOS  " and models "CR 10"/"cr 10" written straight to the DB (`Loop.DbAsync`); parts with "pcs" and "PCS" → one brand, one model, units "pcs" and "PCS", every asset linked; second run returns `(0, 0, 0)`.
  - `ReconcileTests.Import_adds_unknown_brands_and_units`: import an Assets sheet with Manufacturer "Daikin", Model "FTX" and a Parts sheet with unit "box" → brand, model and unit exist and the asset is linked; the check result counts "New brands: 1".
- [ ] **Step 2: Run** `wt-test "UnitTests|ReconcileTests"` — Expected: fail.
- [ ] **Step 3: Implement.** Reconcile with set-based reads (load distinct texts once per tenant) and inserts; link with `ExecuteUpdateAsync` grouped by target. Run at startup (`ReconcileAllAsync`), at the end of a saving import (and include the counts in the import result as "New brands/models/units"), and at the end of the LegacyImport run for its tenant. Pickers enforce the list on the web; the server keeps accepting any unit text (old clients, import) and the next reconcile adds it.
- [ ] **Step 4: Run** `wt-test "UnitTests|ReconcileTests|Import"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(lists): units and the reconcile routine for brands, models and units`

### Task 4: Import sheets for brands and models

**Files:**
- Modify: `Features/Import/ImportSheets.cs` (sheet `BrandsAndModels`: `brand*`, `model`, `active`), `SheetImporters.cs` (`BrandModelImporter` — creates/updates brand, then model when given), `ImportWorkbook.cs` (export), counts in the sheet-count tests (+1 here; +2 in Task 5)
- Test: `server/FourC.Cmms.Tests/BrandImportTests.cs`

- [ ] **Step 1: Failing test** `Brand_sheet_imports_and_round_trips`: rows (Grundfos, CR 10, Yes), (Grundfos, CR 15, Yes), (Daikin, , No) → 2 brands, 2 models, Daikin inactive; the current-data export checks back with no changes.
- [ ] **Step 2: Run** — Expected: fail. **Step 3: Implement.** **Step 4: Run** `wt-test "BrandImportTests|Import"` — pass.
- [ ] **Step 5: Commit** `feat(import): brands and models sheet`

### Task 5: Asset groups

**Files:**
- Create: `server/FourC.Cmms.Api/Features/AssetGroupEndpoints.cs`
- Modify: `AssetEndpoints.cs` (input `List<Guid>? GroupIds = null`; list filter `assetGroupId`; detail `groupIds`), `WorkOrderEndpoints.cs` (list filter `assetGroupId`: own asset in group or a covered asset in group), `ReportEndpoints.cs` (param `AssetGroupP = new("assetGroupId", "assetGroup")` on maintenance-costs, reliability, downtime, failure-pareto, failure-tree, pm-compliance, assets-by-category; filter by the row's asset), `PmEndpoints.cs` (`ScheduleInput` gains `Guid? AssetGroupId = null, bool? ManagesAssetGroup = null`; detail returns `assetGroupId` and `groupAssets` = today's members), `PmGenerator.cs` (`RoundAssetsAsync` draws from the group; every `s.Assets.Count > 0` round check uses `PmRound.IsRound(s)`), `Features/Import/*` (sheets `AssetGroups`, `AssetGroupMembers`; PM schedules column `assetGroupCode`), `ImportRun.cs` (`ImportContext.AssetGroups` code map)
- Test: `server/FourC.Cmms.Tests/AssetGroupTests.cs`, `AssetGroupPmTests.cs`, `AssetGroupImportTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/asset-groups` (assets.view) → `[{ id, code, name, siteId, siteName, isActive, memberCount }]`; `GET /api/asset-groups/{id}` → `+ members: [{ id, assetNumber, name, siteId }]` (visible members only); `POST/PUT/DELETE` (assets.edit); `POST /api/asset-groups/{id}/members { add: Guid[], remove: Guid[] }` → `{ added, removed }` (assets.edit; a site group refuses another site's asset: "This group is for {site} only."; never touches members the person can't see).
  - `PmRound.IsRound(PmSchedule s) : bool` = `s.Assets.Count > 0 || s.AssetGroupId != null`.
  - Group PM reason when inactive: "The asset group is inactive." (else the existing `NoActiveAssets`).

- [ ] **Step 1: Failing tests:**
  - `AssetGroupTests.Groups_have_members_across_the_tree_and_filter_lists`: group with two assets at different locations; `GET /api/assets?assetGroupId=` → both; a work order on one → `GET /api/work-orders?assetGroupId=` finds it; Maintenance costs `&assetGroupId=` counts only its work.
  - `AssetGroupTests.Site_limited_people_see_and_change_only_their_members`: shared group with a site-A and a site-B asset; a site-A editor sees one member; removing all they see leaves the site-B member.
  - `AssetGroupTests.Asset_form_sets_groups_and_old_clients_keep_them`: asset PUT with `groupIds` → membership; PUT without `groupIds` → unchanged.
  - `AssetGroupPmTests.Group_pm_follows_membership`: round PM on a group of 2 → work order covers 2; add a third asset to the group → next generation covers 3; remove all → result "No active assets…" and `nextDueDate` unchanged; make the group inactive → "The asset group is inactive."; all members Under repair (a skip status) → skipped and moved on.
  - `AssetGroupPmTests.Group_mode_pm_makes_one_work_order_per_member`.
  - `AssetGroupImportTests.Group_sheets_and_pm_column_import`: Asset groups + members sheets; PM schedules row with `Asset group code` → the PM targets the group; re-import of members adds, never removes.
- [ ] **Step 2: Run** `wt-test "AssetGroup"` — Expected: fail.
- [ ] **Step 3: Implement.** Group members for a PM: active group, assets not Disposed/Inactive, `SiteId == s.SiteId`, ordered by asset number (sequence = index). Per-asset templates and spread are refused for group targets ("Per-asset templates and spread aren't available for a PM on an asset group."). Delete is refused while a PM uses the group ("This asset group is used by a PM. Make it inactive instead."); otherwise its members go with it.
- [ ] **Step 4: Run** `wt-test "AssetGroup|Pm|Import"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(assets): asset groups as filters, report filters and PM targets`

### Task 6: Yearly numbering

**Files:**
- Modify: `Core/Tenancy/Tenant.cs` (`TenantSettings.RestartNumbersYearly`), `Core/Services/Numbering.cs`, `Features/WorkGroups.cs` (number through `Numbering.For(…, NumberKind.WorkGroup, …)`), every `Numbering.For` caller (pass now)
- Test: `server/FourC.Cmms.Tests/YearlyNumberingTests.cs`

**Interfaces:**
- Produces: `Numbering.For(TenantSettings org, Site? site, NumberKind kind, DateTimeOffset now) : (string Sequence, string Prefix)` — when `org.RestartNumbersYearly` and `kind != Asset`: year `y` = `now` in `Sla.Zone(org.TimeZone)`; sequence `+ ":" + y`; prefix `+ y + "-"`.

- [ ] **Step 1: Failing tests:**
  - `Yearly_numbers_restart_in_the_organizations_year`: unit test of `Numbering.For` — org prefix "WO-", time zone "Africa/Cairo", `now` = 2026-12-31T22:30Z (already 2027 in Cairo) → (`"WorkOrder:2027"`, `"WO-2027-"`); a site prefix "CAI-" → (`"WorkOrder:<site id N>:2027"`, `"CAI-2027-"`); option off → unchanged; Asset kind → unchanged.
  - `New_work_orders_get_yearly_numbers`: option on → a new work order's number matches `^WO-\d{4}-00001$`, the next `-00002`; a request `^WR-\d{4}-00001$`.
- [ ] **Step 2: Run** `wt-test "YearlyNumberingTests"` — Expected: fail.
- [ ] **Step 3: Implement**; the organization settings PUT accepts the flag.
- [ ] **Step 4: Run** `wt-test "YearlyNumberingTests|Numbering|WorkOrder"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(numbering): optional yearly numbering`

### Task 7: Web — editable lists

**Files:**
- Create: `web/src/components/listLabels.ts` (+ `listLabels.test.ts`), `web/src/pages/settings/ListsSettings.tsx`
- Modify: `web/src/api/types.ts` (`ListLabel`, `TenantSettings.listLabels`, `restartNumbersYearly`), `web/src/auth/AuthContext.tsx` (apply labels when `me` loads/changes), `web/src/tech/*` (apply the bundle's labels; server `TechEndpoints` bundle gains `listLabels`), the 13 files using `WORK_TYPES`/`WORK_PRIORITIES`/`ASSET_STATUSES`/`CRITICALITIES` for pickers, `web/src/components/WorkBadges.tsx` + `Badges.tsx` (colours), settings tabs, locales

**Interfaces:**
- Produces (`listLabels.ts`):
  - `type ListKey = 'workType' | 'priority' | 'assetStatus' | 'criticality'`; i18n key prefix per list: `workType`, `priority`, `status`, `criticality`.
  - `applyListLabels(i18n: i18n, labels: TenantSettings['listLabels'] | undefined): void` — adds/overrides `prefix.Value` in en (name) and ar (nameAr); removes overrides no longer set (restores bundled text).
  - `listOptions(list: ListKey, values: readonly string[], labels, t, current?: string | null): { value: string; label: string }[]` — organization order (then built-in order), hidden left out unless `=== current`.
  - `listColor(list: ListKey, value: string, labels, fallback: string): string`.
  - `firstVisible(list, values, labels, preferred): string` (form defaults).
- [ ] **Step 1: Failing Vitest** `listLabels.test.ts`: order (`order` then built-in), hidden left out, `listOptions keeps the current hidden value`, colour fallback, `firstVisible` falls back when "Medium" is hidden, `applyListLabels` overrides then restores.
- [ ] **Step 2: Run** `wt-vitest` — Expected: new tests fail.
- [ ] **Step 3: Implement** helpers, Settings › Lists (four tables: built-in name, English name, Arabic name, colour swatch select, hidden switch, up/down, Reset; saves `PUT /settings/lists`, then refetches `me`), pickers and badges, phone bundle labels; locales.
- [ ] **Step 4: Run** `wt-vitest` and `wt-web` — Expected: pass; build clean.
- [ ] **Step 5: Commit** `feat(web): editable lists`

### Task 8: Web — brands, models, units, asset groups, numbering

**Files:**
- Create: `web/src/pages/settings/BrandsSettings.tsx`, `UnitsSettings.tsx`, `AssetGroupsSettings.tsx`, `web/src/components/UnitSelect.tsx`, `web/src/components/merge.ts` (+ test), `web/src/pages/settings/numbering.ts` (+ test)
- Modify: asset form (Brand/Model pickers with "Add “X”", Groups multi-select), asset list (brand, model, group filters; bulk Add to group / Remove from group), work order list (group filter), part form, measurement point form, template / PM task step editors (unit picker), PM form (target "Asset group" with the members it would use today; per-asset templates/spread hidden), reports page (`assetGroup` param type), organization settings (Restart numbers each year + next number preview), settings tabs, `api/types.ts`, locales
- Test: `web/src/components/merge.test.ts`, `web/src/pages/settings/numbering.test.ts`

**Interfaces:**
- Produces: `isMergeConflict(e: unknown): { mergeWith: string } | null` in `web/src/components/merge.ts` (+ `merge.test.ts`), used by the brands and units screens to ask "Merge into …?" and resend with `merge: true`; `previewNumber(prefix: string, padding: number, yearly: boolean, now: Date): string` in `web/src/pages/settings/numbering.ts` (+ `numbering.test.ts`: `WO-2027-00001` when yearly in 2027, `WO-00001` when not).
- [ ] **Step 1: Failing Vitest** for `isMergeConflict` and `previewNumber`.
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement** screens and pickers; locales.
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass; build clean.
- [ ] **Step 5: Commit** `feat(web): brands, models, units, asset groups and yearly numbering`

### Task 9: Docs and full verification

**Files:** Create `docs/LISTS_GROUPS_AND_NUMBERING.md`; Modify `README.md` (feature line).

- [ ] **Step 1: Write the docs** (who may do what, each feature, import sheets, upgrade note: the starting brand/model/unit lists are built from existing texts on first start).
- [ ] **Step 2: Run** `wt-ci`, `wt-vitest`, `wt-web` — Expected: Release build succeeds; server green; web green; build clean.
- [ ] **Step 3: Commit** `docs(0.21.0): lists, brands, groups and numbering`
