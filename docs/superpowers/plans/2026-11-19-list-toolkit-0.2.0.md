# List toolkit 0.2.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Column chooser, server-saved views, filter chips, xlsx/CSV export and bulk actions on the six main lists.

**Architecture:** Server: one export writer, list queries moved into functions shared by each list endpoint and its
`/export`, two small per-user tables (views, column settings), three bulk endpoints reusing the single-row rules.
Web: shared hooks and components around `mantine-datatable`, then each list adopts them.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, xUnit (SkippableFact + `ApiFixture`),
React 19, Mantine 8, mantine-datatable 8, TanStack Query 5, i18next.

**Spec:** `docs/superpowers/specs/2026-11-19-list-toolkit-0.2.0-design.md`

## Global Constraints

- No new NuGet or npm packages (xlsx is written by hand with `System.IO.Compression`).
- Export cap 10,000 rows; bulk cap 500 ids; view name 1–60 chars, unique per user+list ignoring case; 50 views per
  user+list; view query ≤ 2,000 chars; hidden-columns list ≤ 100 entries of ≤ 80 chars.
- List keys: `work-orders`, `work-requests`, `pm-schedules`, `parts`, `purchase-orders`, `assets` (anything else → 400).
- Views and list settings are always read and written for the signed-in user only (never by id across users).
- CSV: UTF-8 BOM, CRLF, RFC 4180 quoting; cells starting with `=`, `+`, `-`, `@` get a leading `'`.
- Enum values export in words: PascalCase split into words, first letter capital, rest lower ("InProgress" → "In progress").
- Date-times export in the tenant's `Settings.TimeZone` (fallback UTC), formatted `yyyy-MM-dd HH:mm` in CSV.
- Every user-visible string goes through i18n (`web/src/i18n/en.json`).
- Server tests via `scratchpad/bin/wt-test`, web build via `wt-web`, migration via `wt-ef`.

## Review Focus

1. A bulk status change must refuse exactly what the single change refuses (completion rules, hold reason, close
   permission, invalid moves) — per row, without failing the rows that are fine.
2. A requester exporting work requests must get only their own requests (the list's `VisibleAsync` rule).
3. Export with a filter that matches more than 10,000 rows returns 10,000 and says so (`X-Export-Truncated: true`).
4. Two users in one organization never see or change each other's views or column settings, including by guessing ids.
5. Opening a list whose address already has filters must not be overwritten by the default view.

---

### Task 1: Export writer

**Files:**
- Create: `server/FourC.Cmms.Api/Infrastructure/TableExport.cs`
- Test: `server/FourC.Cmms.Tests/TableExportTests.cs`

**Interfaces:**
- Produces: `public sealed record ExportColumn(string Key, string Title);`
  `public static class TableExport` with
  `IReadOnlyList<object?[]> Rows(IEnumerable<object> items, IReadOnlyList<ExportColumn> cols, TimeZoneInfo tz)` (values:
  string, decimal/double/int/long, DateOnly, DateTime (already converted to tz), bool → "Yes"/"No", null),
  `byte[] Csv(IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)`,
  `byte[] Xlsx(string sheetName, IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)`,
  `IReadOnlyList<ExportColumn> DefaultColumns(object sample)` (every non-id property, title = key),
  `string Words(string enumValue)`.
  `Rows` serialises each item with the API's JSON options (camelCase, enums as strings) and reads `Key` (dotted path);
  JSON arrays of scalars join with ", "; strings that parse as ISO date (`yyyy-MM-dd`) become `DateOnly`, ISO date-times
  become tz-local `DateTime`; strings matching an enum-like PascalCase word with no spaces are NOT converted (only values
  of properties whose CLR type is an enum get `Words`, detected from the item's type via reflection on the key's first
  segment). Unknown key → `ArgumentException("Unknown column: {key}")`.

- [ ] **Step 1: Write the failing tests** — `Csv_quotes_and_neutralises_formulas` (`a,b` → `"a,b"`; `say "hi"` → `"say ""hi"""`;
  `=1+1` → `'=1+1`; first bytes are EF BB BF; lines end CRLF), `Rows_reads_paths_enums_dates_lists` (record with
  `WorkOrderStatus Status=InProgress`, `DateOnly Due`, `DateTimeOffset At` = 2026-11-19T10:00Z with tz Africa/Cairo →
  `2026-11-19 12:00`, `List<string> Names` → "A, B", `Dictionary<string,string?> CustomFields` read via
  `customFields.colour`, bool → "Yes"), `Rows_rejects_unknown_key`, `Xlsx_is_a_valid_workbook` (open with
  `ZipArchive`; `xl/worksheets/sheet1.xml` contains the titles, a numeric cell `<v>12.5</v>`, a date cell with style
  index of the date format, `<autoFilter`, `<pane` frozen at A2; `[Content_Types].xml` lists the sheet),
  `Words_splits_pascal_case` ("InProgress" → "In progress", "OnHold" → "On hold", "PM" → "PM").
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~TableExportTests"` — expect compile failure (types missing).
- [ ] **Step 3: Implement** `TableExport`. XLSX parts: `[Content_Types].xml`, `_rels/.rels`, `xl/workbook.xml`,
  `xl/_rels/workbook.xml.rels`, `xl/styles.xml` (numFmt 164 `yyyy-mm-dd hh:mm`; cellXfs: 0 default, 1 bold, 2 date
  numFmt 14, 3 datetime 164), `xl/worksheets/sheet1.xml` (inline strings `t="inlineStr"`, XML-escaped, invalid XML
  chars stripped; dates as OADate serials; `<cols>` widths from max text length clamped 8–60).
- [ ] **Step 4: Run** the same filter — PASS.
- [ ] **Step 5: Commit** `feat(export): CSV and xlsx table writer`.

### Task 2: Export endpoints on the six lists

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/{WorkOrder,WorkRequest,Pm,Inventory,Purchasing,Asset}Endpoints.cs`
- Create: `server/FourC.Cmms.Api/Infrastructure/ListExportEndpoint.cs`
- Modify: `server/FourC.Cmms.Api/Infrastructure/Infrastructure.cs` (`ToPagedAsync` gets an optional `maxPageSize = 500`)
- Test: `server/FourC.Cmms.Tests/ListExportTests.cs`

**Interfaces:**
- Consumes: Task 1 `TableExport`, `ExportColumn`.
- Produces: `static class ListExport { Task<IResult> FileAsync<T>(HttpContext http, CmmsDbContext db, string list, Func<int,int,Task<PagedResult<T>>> page, CancellationToken ct) }`
  — reads `format` (xlsx default, csv), `c`/`t` (titles default to keys; counts must match or titles are ignored),
  calls `page(1, 10000)` (paging helper allows 10,000 for export), resolves the tenant time zone, returns
  `Results.File(bytes, mime, "{list}-{yyyyMMdd-HHmm}.{ext}")` and sets `X-Export-Truncated: true` when total > 10,000.
  `ArgumentException` → 400 problem.
- Each list endpoint's body becomes `static Task<PagedResult<…>> ListAsync(CmmsDbContext db, <Filter> f, …, int page, int pageSize, CancellationToken ct)`;
  filters bound with `[AsParameters]` classes: `WorkOrderFilter` (exists), new `RequestFilter`, `PmScheduleFilter`,
  `PartFilter`, `PoFilter`, existing `AssetFilter`. Export endpoints: `GET /{list}/export` with the list's permission
  (requests: `RequireAuthorization` + `VisibleAsync`, as the list).

- [ ] **Step 1: Write the failing tests** — `Work_order_export_uses_filters_columns_and_titles` (two WOs, priority
  filter matches one; xlsx sheet contains title "Pri" and value "High", not the other WO's number),
  `Csv_export_of_work_orders` (`format=csv`, header line `Number,Title`), `Request_export_shows_a_requester_only_their_own`,
  `Export_rejects_an_unknown_column` (400), `Export_needs_the_list_permission` (requester → 403 on work-orders export),
  `Every_list_exports` (one call per list with `format=csv` and no `c` → 200 and `text/csv`),
  `Asset_export_reads_custom_fields` (`c=customFields.<key>`), `Export_caps_rows` (call `ListExport` helper unit-style
  with a fake page function returning total 10,001 → header `X-Export-Truncated`).
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~ListExportTests"` — FAIL (404s).
- [ ] **Step 3: Implement** the refactor and endpoints; the list endpoints return exactly what they returned before.
- [ ] **Step 4: Run** the filter, then the whole suite — PASS (existing list tests prove the refactor kept behaviour).
- [ ] **Step 5: Commit** `feat(export): xlsx/CSV export for the six lists`.

### Task 3: Saved views and column settings

**Files:**
- Create: `server/FourC.Cmms.Core/Users/SavedView.cs` (`SavedView`, `UserListSetting`, `ListKeys`)
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`; migration `ListToolkit` via `wt-ef ListToolkit`
- Create: `server/FourC.Cmms.Api/Features/ViewEndpoints.cs`; Modify `Program.cs` (`api.MapViewEndpoints()`)
- Test: `server/FourC.Cmms.Tests/ViewTests.cs`

**Interfaces:**
- `SavedView : Entity, ITenantOwned { Guid TenantId; Guid UserId; string List(40); string Name(60); string Query(2000); List<string> HiddenColumns; bool IsDefault; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }`,
  unique index (UserId, List, Name). `UserListSetting : ITenantOwned { Guid TenantId; Guid UserId; string List; List<string> HiddenColumns }`, key (UserId, List).
  `ListKeys.All` (the six keys), `ListKeys.IsValid(string)`.
- `GET /api/views?list=` → `[{id,name,query,hiddenColumns,isDefault}]` ordered by name;
  `POST /api/views` `{list,name,query,hiddenColumns,isDefault}` → `{id}`; `PUT /api/views/{id}` `{name,query,hiddenColumns,isDefault}`;
  `DELETE /api/views/{id}`; setting `isDefault` clears the user's other default for that list.
  `GET /api/me/list-settings` → `{ "<list>": { hiddenColumns: [] } }`; `PUT /api/me/list-settings/{list}` `{hiddenColumns}`.
  Another user's view id → 404. All need only sign-in.

- [ ] **Step 1: Write the failing tests** — `Views_are_personal` (user B can't list/PUT/DELETE A's view: empty / 404),
  `View_names_are_unique_per_list_ignoring_case` (second "Mine" vs "mine" → 400; same name on another list → OK),
  `One_default_per_list`, `View_limits` (61-char name, 2,001-char query, unknown list → 400; 51st view → 400),
  `List_settings_round_trip` (PUT then GET; other user sees none).
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~ViewTests"` — FAIL.
- [ ] **Step 3: Implement** entities, context config (`HiddenColumns` primitive collection with default `'[]'`),
  migration, endpoints.
- [ ] **Step 4: Run** — PASS; whole suite PASS.
- [ ] **Step 5: Commit** `feat(views): saved views and column settings per user`.

### Task 4: Work-order bulk actions

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs`
- Test: `server/FourC.Cmms.Tests/BulkTests.cs`

**Interfaces:**
- `static Task<Validator> ChangeStatusAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, TenantSettings settings, bool canClose, WorkOrder w, StatusInput input, CancellationToken ct)`
  (everything the single endpoint does after loading the WO, without `SaveChanges`; an invalid move adds a validator
  error instead of throwing). The single endpoint calls it and keeps its responses (DomainException → 400 as today,
  missing close permission → 403).
- `POST /api/work-orders/bulk` `{ ids: Guid[], status?: StatusInput, priority?: WorkPriority, addAssigneeIds?: Guid[] }`
  (exactly one of the three) → `{ updated, skipped: [{ id, number, reason }] }`. Permission: status → execute (closing
  rows without workorders.close are skipped "You can't close work orders"), priority/assignees → edit (checked in the
  handler via `PermissionService`; missing → 403). Unknown employee id → 400. Rows are saved together at the end.

- [ ] **Step 1: Write the failing tests** — `Bulk_status_applies_the_completion_rules_per_row` (RequireLaborToComplete on;
  two WOs, one with labour → updated 1, skipped 1 with reason containing "labour"), `Bulk_hold_needs_a_reason`,
  `Bulk_close_needs_the_close_permission` (technician role), `Bulk_priority_skips_closed_work`,
  `Bulk_add_technicians_adds_and_emails` (existing assignee kept; new one queued an outbox row `workorder.assigned`),
  `Bulk_rejects_more_than_500_ids`, `Single_status_change_still_behaves` (existing tests cover it — run them).
- [ ] **Step 2: Run** `wt-test "FullyQualifiedName~BulkTests"` — FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** — PASS; whole suite PASS.
- [ ] **Step 5: Commit** `feat(bulk): change status, priority and technicians on many work orders`.

### Task 5: PM schedule and part bulk actions

**Files:**
- Modify: `PmEndpoints.cs`, `InventoryEndpoints.cs`; Test: `BulkTests.cs`

**Interfaces:**
- `POST /api/pm-schedules/bulk` `{ ids, active: bool }` → `{ updated, skipped: [] }` (pm.manage); resuming a calendar
  schedule sets `NextDueDate = PmCalendar.FirstOnOrAfter(s, Today)` exactly when the edit form would (shared helper
  `SetActive(PmSchedule s, bool active)` used by PUT too).
- `POST /api/parts/bulk` `{ ids, active: bool }` → `{ updated, skipped: [] }` (inventory.manage).

- [ ] **Step 1: Tests** — `Bulk_resume_sets_the_next_due_date`, `Bulk_pause_stops_generation` (paused schedule not in
  `dueWithinDays` list), `Bulk_parts_active_flag`, permission 403 for a requester on both.
- [ ] **Step 2–4:** run (FAIL), implement, run (PASS + suite).
- [ ] **Step 5: Commit** `feat(bulk): pause/resume PM schedules, (de)activate parts`.

### Task 6: Web toolkit

**Files:**
- Create: `web/src/components/list/useListParams.ts`, `useListColumns.ts`, `ListTools.tsx`, `FilterChips.tsx`,
  `BulkBar.tsx`, `types.ts`
- Modify: `web/src/api/types.ts` (`SavedView`, `BulkResult`), `web/src/i18n/en.json` (`list.*`)

**Interfaces:**
- `useListParams(defaults?: Record<string,string>)` → `{ params, p(k), set(patch), search, setSearch, page, pageSize, sort, setSort, clearAll(keep?: string[]) }`
  (same behaviour as the copies in the pages today: changing anything but `page` resets `page`).
- `type ListColumn<T> = DataTableColumn<T> & { export?: false | { key: string; title?: string }[] }` (default export
  = `[{ key: accessor, title }]`).
- `useListColumns<T>(list: ListKey, all: ListColumn<T>[], defaultHidden: string[], legacyLocalKey?: string)` →
  `{ columns, hidden, setHidden, toggle(accessor), chooser: {accessor,title,shown}[] }` (server-saved, optimistic).
- `<ListTools list exportPath query columns total hidden onApplyView />`: views menu (save new, update, rename,
  delete, set/unset default, apply), columns menu, export menu (xlsx/csv; warns over 10,000). Default view applied
  by `useDefaultView(list, isAddressEmpty, apply)`.
- `type FilterDef = { key: string; label: string; format?: (value: string) => string }`; `<FilterChips defs params onRemove />`.
- `<BulkBar count actions onClear />` and `showBulkResult(t, result)` (notification with skipped rows, max 10 listed).

- [ ] **Step 1:** implement; **Step 2:** `wt-web` builds; **Step 3:** commit `feat(web): list toolkit components`.

### Task 7: Work-order list on the toolkit

**Files:** Modify `web/src/pages/work/WorkOrdersPage.tsx` (+ i18n)

- [ ] Columns declared once with `export` (asset → `assetNumber`, `assetName`; location → `locationPath`; site →
  `siteName`; assignees → `assignees`; trade column added, hidden by default; completedAt column hidden by default);
  filter defs for every filter incl. "More filters"; selection + BulkBar (status modal with hold reason and completion
  notes; priority menu; add-technicians modal with EmployeePicker multi), result notification; export path
  `/work-orders/export` with the tab's statuses.
- [ ] `wt-web` builds; commit `feat(web): toolkit on the work-order list`.

### Task 8: The other five lists

**Files:** `WorkRequestsPage.tsx`, `PmPage.tsx` (schedules tab: filters into the address), `InventoryPage.tsx`
(parts tab), `PurchasingPage.tsx` (orders tab), `AssetsPage.tsx` (replace its own column/bulk/export code; legacy
local key `assets.hiddenColumns`).

- [ ] One list at a time: columns + export, filter chips, views, bulk (PM, parts, assets); `wt-web` after each;
  commit `feat(web): toolkit on <list>` each.

### Task 9: Docs and hands-on check

- [ ] `CHANGELOG.md` `## [0.2.0] - <date>` (Added: per list tools; Changed: asset export options, PM filters in the
  address). Hands-on in the local stack: save a view, set default, reload; export xlsx opens (unzip + check sheet);
  bulk status with a rule-blocked row; chips remove filters. Commit `docs: 0.2.0 changelog`.
