# Excel import and export (0.7.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An administrator loads and updates locations, assets, parts and stock, people, suppliers and PM from one Excel workbook through Settings › Import, with a preview first and a matching export.

**Architecture:** A hand-written `.xlsx`/`.csv` reader produces raw sheets. Sheet definitions map headings (English or Arabic) to typed cells. An import run processes the sheets in a fixed order inside one database transaction. Each row is laid over the record's current input and checked with the screens' own field rules plus in-memory reference checks, then saved through the screens' own create/update code. A check rolls back; an import commits. The export writes the same layout with the existing hand-written writer, extended to several sheets.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, xUnit (`SkippableFact` + `ApiFixture`); React 19, Mantine 8, TanStack Query, i18next, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-21-excel-import-0.7.0-design.md`

**Commands** (scratchpad helpers; `B=/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad/bin`, and `export DEVELOPER_DIR=/Library/Developer/CommandLineTools` for git):
- server tests: `$B/wt-test "FullyQualifiedName~<Class>"` (whole suite: `$B/wt-test`)
- web tests: `$B/wt-vitest`; web type-check and build: `$B/wt-web`

## Global Constraints

- No new server package and no new web package. The `.xlsx` reader is hand-written like `TableExport`.
- Limits: 10 MB per file, 5,000 rows per sheet, a 45-second run budget.
- Only `settings.manage` may see the tab or call any import endpoint.
- Add and update only. Nothing is deleted. A blank cell keeps the current value. Codes match ignoring case.
- The opening quantity is used only when the part has no stock level in that store yet.
- Every new text exists in English and Arabic: `web/src/locales/en.json` and `ar.json`, and server messages in `server/FourC.Cmms.Api/Localization/ar.json`.
- The screens behave exactly as before: every existing test stays green.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A workbook saved by Microsoft Excel uses shared strings, rich-text runs, custom date formats (`dd/mm/yyyy`) and formula cells (`t="str"`). Each must read as its plain value. Test: `Reads_an_excel_style_workbook` (Task 1).
2. Codes and headings with stray spaces or a different case (`" site code "`, `pump-01` against `PUMP-01`) must match. Test: `Codes_and_headings_match_ignoring_case_and_spaces` (Task 4).
3. A code typed as a number (cell `123`) reads as `"123"`, not `"123.0"`, and a text cell `"00123"` keeps its zeros. Test: `Numeric_cells_read_as_plain_text_where_text_is_expected` (Task 2).
4. Blank rows in the middle or at the end of a sheet, including formatted empty rows, are skipped, not reported. Test: `Blank_rows_are_skipped` (Task 4).
5. The same key twice in one sheet: the second row is a problem naming the first row. Test: `A_key_twice_in_a_sheet_is_a_problem` (Task 4).

---

### Task 1: Reading spreadsheets, and writing several sheets

**Files:**
- Create: `server/FourC.Cmms.Api/Features/Import/SpreadsheetReader.cs`
- Modify: `server/FourC.Cmms.Api/Infrastructure/TableExport.cs` (add a several-sheet `Xlsx`)
- Test: `server/FourC.Cmms.Tests/SpreadsheetReaderTests.cs` (plain `[Fact]`s, no database)

**Interfaces:**
- Produces:
  - `public sealed record SpreadsheetSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows)`. Rows include the heading row. A cell is `string`, `decimal`, `DateOnly`, `bool` or `null`. Trailing empty cells and fully empty rows are kept as they are; skipping them is Task 4's job.
  - `public sealed record SpreadsheetBook(IReadOnlyList<SpreadsheetSheet> Sheets)`.
  - `public static class SpreadsheetReader { public static SpreadsheetBook Read(Stream content, string fileName); }`
    - `.xlsx`: zip of XML.
    - `.csv`: one sheet named after the file without its extension.
    - `.xls`, or anything else: `DomainException("Save the file as .xlsx (Excel workbook) or .csv first.")`.
    - A broken file: `DomainException("This file can't be read as an Excel workbook.")`.
  - `public sealed record ExportSheet(string Name, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<object?[]> Rows)`.
  - `public static byte[] TableExport.Xlsx(IReadOnlyList<ExportSheet> sheets)`. The existing one-sheet `Xlsx` calls it.

- [ ] **Step 1: Write the failing tests** in `SpreadsheetReaderTests`:
  - `Reads_what_the_export_writes`. `TableExport.Xlsx` of two sheets ("Sites" and "Assets"), read back:
    - the sheet names are equal;
    - `"PUMP-01"` stays a string, `12.5m` a decimal, `new DateOnly(2026,11,21)` a `DateOnly`;
    - an empty cell is `null`.
  - `Reads_an_excel_style_workbook`. A workbook built in the test the way Excel saves one, with:
    - `xl/sharedStrings.xml` holding a rich-text run (`<si><r><t>Pu</t></r><r><t>mp</t></r></si>` reads `"Pump"`);
    - `styles.xml` with a custom `numFmt` id 165 `dd/mm/yyyy` on cell style 1, so `<c s="1"><v>46347</v></c>` reads `DateOnly(2026,11,21)`;
    - a `t="b"` cell with `1` reads `true`;
    - a formula cell `<c t="str"><f>A1</f><v>X</v></c>` reads `"X"`;
    - a gap in cell references (`A2`, then `C2`) leaves `B2` as `null`.
  - `Reads_csv_with_comma_semicolon_and_bom`. `"﻿Code;Name\nA1;\"x;y\"\n"` gives rows `[["Code","Name"],["A1","x;y"]]`, and `Assets.csv` gives sheet name `"Assets"`.
  - `Refuses_old_excel_files`. `Read(stream, "data.xls")` throws `DomainException` with the message above.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~SpreadsheetReaderTests"`. Expected: build errors (`SpreadsheetReader` not defined).
- [ ] **Step 3: Implement** `SpreadsheetReader` and the several-sheet `TableExport.Xlsx`.
  - The reader follows `xl/_rels/workbook.xml.rels` from each `<sheet r:id>` to its part.
  - A number cell is a date when its style's `numFmtId` is a built-in date (14–22, 45–47) or a custom format whose code contains `d`, `m` or `y` outside quotes and brackets.
  - The writer gets one `sheetN.xml` per sheet, with matching `[Content_Types]` and rels entries.
- [ ] **Step 4: Run** the same command. Expected: 4 passed. Then run `$B/wt-test "FullyQualifiedName~TableExportTests|FullyQualifiedName~ListExportTests"`. Expected: all pass (the one-sheet export is unchanged).
- [ ] **Step 5: Commit** `git add -A && git commit -m "Spreadsheet reader and several-sheet export"`.

### Task 2: Sheet definitions and reading cell values

**Files:**
- Create: `server/FourC.Cmms.Api/Features/Import/ImportSheets.cs` (all 15 sheet definitions, in processing order)
- Create: `server/FourC.Cmms.Api/Features/Import/ImportValues.cs`
- Modify: `server/FourC.Cmms.Api/Localization/ar.json` (sheet names, headings, value messages)
- Test: `server/FourC.Cmms.Tests/ImportValuesTests.cs`

**Interfaces:**
- Produces:
  - `public enum ValueKind { Text, Number, Integer, Date, YesNo, Choice, CodeList }`.
  - `public sealed record ColumnDef(string Key, string English, string Arabic, ValueKind Kind, bool RequiredOnNew = false, Type? Choices = null)`.
  - `public sealed record SheetDef(string Key, string English, string Arabic, IReadOnlyList<ColumnDef> Columns)`.
  - `public static class ImportSheets`:
    - `IReadOnlyList<SheetDef> All` (order: Sites, Locations, Categories, Trades, PartGroups, Suppliers, Stores, Assets, People, Parts, Stock, PmTasks, PmSteps, PmParts, PmSchedules);
    - `SheetDef? Find(string name)` (English or Arabic, case and space-insensitive).
    - Keys and English names are exactly the spec's tables. The Assets custom-field columns are added at run time (Task 5).
  - `public static class ImportValues`:
    - `bool TryRead(object? cell, ColumnDef col, out object? value, out string? problem)`. The value type per kind:
      - Text → `string` (trimmed). A `decimal` cell becomes its plain text (`123m` → `"123"`), a `DateOnly` → `yyyy-MM-dd`.
      - Number → `decimal`; Integer → `int`; Date → `DateOnly` (cell date or text `yyyy-MM-dd`); YesNo → `bool`.
      - Choice → the enum value; CodeList → `List<string>` split on commas, trimmed, empties dropped.
    - Blank (null or whitespace) → `value = null`, no problem.
    - `string Show(object? value)` gives the display text for preview changes: dates `yyyy-MM-dd`, numbers invariant without trailing zeros, enums as the words the screens show in the request language, booleans as Yes/No in the request language, lists joined with ", ".
  - Problem messages (English keys, Arabic in `ar.json`):
    - `"{0} must be a number."`, `"{0} must be a whole number."`;
    - `"{0} must be a date (yyyy-mm-dd)."`, `"{0} must be Yes or No."`;
    - `"{0} must be one of: {1}."`, where `{0}` is the column heading in the request language.
  - Choice words: the enum name, `TableExport.Words(name)` and `Translator.T(TableExport.Words(name), "ar")`, compared ignoring case. YesNo accepts Yes/No/True/False/1/0/نعم/لا.

- [ ] **Step 1: Write the failing tests** in `ImportValuesTests`:
  - `Numeric_cells_read_as_plain_text_where_text_is_expected`. `123m` read as Text gives `"123"`; `"00123"` gives `"00123"`; `12.50m` gives `"12.5"`.
  - `Dates_read_from_cells_and_text`. `DateOnly(2026,11,21)` and `"2026-11-21"` give the same date; `"21/11/2026"` is a problem with the date message.
  - `Choices_accept_english_and_arabic_words`. For `AssetStatus`: `"active"`, `"Active"` and `"نشط"` give `AssetStatus.Active`; `"InRepair"` and `"in repair"` give `InRepair`; `"broken"` is a problem listing the allowed words.
  - `Yes_no_and_lists`. `"نعم"` gives `true`, `0m` gives `false`; `" ELEC , MECH ,"` gives `["ELEC","MECH"]`.
  - `Sheets_are_found_by_english_or_arabic_name`. `ImportSheets.Find(" assets ")` and `Find(<Arabic name of Assets>)` both give Key `"Assets"`, and `ImportSheets.All.Count == 15`.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportValuesTests"`. Expected: build errors.
- [ ] **Step 3: Implement** `ImportSheets` and `ImportValues`, and add the Arabic sheet names, headings and messages to `ar.json`.
- [ ] **Step 4: Run** the same command. Expected: 5 passed.
- [ ] **Step 5: Commit** `"Import sheet definitions and cell values"`.

### Task 3: Share the screens' rules and save code

No behaviour changes. Every validator and save path the import needs is split into reusable pieces, and the endpoints call those pieces.

**Files (modify):**
- `Features/LocationEndpoints.cs` (sites, locations)
- `Features/SupportEndpoints.cs` (categories)
- `Features/PeopleEndpoints.cs` (trades, employees)
- `Features/InventoryEndpoints.cs` (part groups if there, stores, parts)
- `Features/PurchasingEndpoints.cs` (suppliers)
- `Features/AssetEndpoints.cs`
- `Features/PmEndpoints.cs` (tasks, schedules)

**Interfaces:**
- Produces, for each kind `K` in {Site, Location, Category, Lookup (trades, part groups), Store, Supplier, Asset, Employee, Part, PmTask, PmSchedule}:
  - `internal static Validator KRules(KInput i, Guid? id)`, named `SiteRules`, `LocationRules`, `CategoryRules`, `LookupRules`, `StoreRules`, `SupplierRules`, `AssetRules` (with an extra `bool requireNumber`), `EmployeeRules`, `PartRules`, `PmTaskRules` and `PmScheduleRules`. Only the checks that need no database: required, lengths, ranges, enum defined, date order, own-parent, frequency rules and so on. The existing `Validate…Async` starts from it, so the screens' messages and field keys stay the same.
  - The existing apply methods keep their names and become `internal`: `LocationEndpoints.Apply(Site, SiteInput)` and `Apply(Location, LocationInput)`, `AssetEndpoints.Apply`, `PeopleEndpoints.Apply(Employee, EmployeeInput)`, `InventoryEndpoints.ApplyPart`, `PurchasingEndpoints.ApplySupplier`, `PmEndpoints.ApplyTask` and `ApplySchedule`. Categories, lookups and stores get the same treatment where their endpoint sets fields inline.
  - Save pieces lifted out of the endpoint lambdas, without `SaveChangesAsync` unless stated:
    - `internal static Task<Location> CreateLocationAsync(CmmsDbContext db, LocationInput i, CancellationToken ct)`, which adds and assigns the path;
    - `internal static Task UpdateLocationAsync(CmmsDbContext db, Location loc, LocationInput i, CancellationToken ct)`. When it moves the location it saves, rewrites descendants and moves assets, as the PUT does now;
    - `internal static Task<Asset> CreateAssetAsync(CmmsDbContext db, AssetInput i, Dictionary<string,string?> custom, TenantSettings settings, CancellationToken ct)`, which numbers a blank asset number as now;
    - `internal static void UpdateAsset(CmmsDbContext db, Asset a, AssetInput i, Dictionary<string,string?> custom, ITenantContext ctx)`, which records a transfer when the site or location changes, exactly as the PUT does;
    - `internal static Dictionary<string,string?> CustomFieldValues(Validator v, IReadOnlyList<CustomFieldDefinition> defs, AssetInput i)`, the custom-field loop as a pure function. `ValidateAsync` loads `defs` and calls it;
    - `internal static PmSchedule CreateSchedule(CmmsDbContext db, ScheduleInput i, string code)`;
    - `internal static void UpdateSchedule(PmSchedule s, ScheduleInput i, DateOnly today)`, holding the next-due rules of POST and PUT;
    - `internal static Validator ScheduleChangeRules(PmSchedule s, ScheduleInput i)`, the "basis can't change after generation" rule;
    - `internal static void ApplyTaskLists(CmmsDbContext db, PmTask t, TaskInput i)`, which replaces the steps and parts as the PUT does;
    - `internal static void ApplyLevels(...)` is not needed. The Stock sheet works on `StockLevel` directly (Task 6).
  - Each endpoint's POST/PUT becomes: `Validate…Async` → piece → `SaveChangesAsync`, in the same order as today.
  - Every `KInput` record the import builds becomes `internal`-visible (they are already `public`).

- [ ] **Step 1: Run** the suite before touching anything: `$B/wt-test`. Expected: 296 passed, 2 skipped (the baseline).
- [ ] **Step 2: Refactor** each endpoint as listed. Messages, field keys, status codes and the order of side effects stay the same.
- [ ] **Step 3: Run** `$B/wt-test`. Expected: the same 296 passed, 2 skipped. No new test: this task is a refactor, and the existing endpoint tests are its proof.
- [ ] **Step 4: Commit** `"Share the screens' rules and save code for the import"`.

### Task 4: The import run, the endpoints, and the simple sheets

**Files:**
- Create: `Features/Import/ImportRun.cs` (run, results, the row accessor, the context)
- Create: `Features/Import/SheetImporters.cs` (the `ISheetImporter` contract and the importers for Sites, Locations, Categories, Trades, PartGroups, Suppliers, Stores)
- Create: `Features/ImportEndpoints.cs`; modify `Program.cs` (map it)
- Modify: `Localization/ar.json` (run messages)
- Test: `server/FourC.Cmms.Tests/ImportTests.cs`, plus `server/FourC.Cmms.Tests/ImportFiles.cs`: a test helper that builds a workbook with `TableExport.Xlsx(sheets)` from `(sheet, headings, rows)` and posts it.

**Interfaces:**
- Consumes: Task 1 `SpreadsheetReader`/`SpreadsheetBook`; Task 2 `ImportSheets`/`ImportValues`; Task 3 rules and save pieces.
- Produces:
  - `public enum RowOutcome { New, Changed, Unchanged, Problem }`.
  - `public sealed record FieldChange(string Column, string? Old, string? New)`.
  - `public sealed record RowResult(int Row, string Key, RowOutcome Outcome, IReadOnlyList<FieldChange> Changes, IReadOnlyList<string> Problems)`.
  - `public sealed record SheetResult(string Sheet, int New, int Changed, int Unchanged, int Problems, IReadOnlyList<RowResult> Rows, IReadOnlyList<string> IgnoredColumns)`. `Rows` omits unchanged rows; `Sheet` is the sheet key.
  - `public sealed record ImportResult(bool Saved, IReadOnlyList<SheetResult> Sheets, IReadOnlyList<string> NewLookups, IReadOnlyList<string> Notes, string? Stopped)`.
  - `public sealed class ImportRow`:
    - `int Number`;
    - `bool Has(string key)`;
    - `T? Get<T>(string key)` (the value already read by `ImportValues`);
    - `string? Raw(string key)`;
    - `void Problem(string message)`;
    - `bool HasProblems`.
  - `internal interface ISheetImporter`:
    - `string Sheet { get; }`;
    - `Task LoadAsync(ImportContext c, IReadOnlyList<ImportRow> rows, CancellationToken ct)`;
    - `string KeyOf(ImportRow row)`;
    - `Task<RowResult> ImportAsync(ImportContext c, ImportRow row, CancellationToken ct)`;
    - `Task FinishAsync(ImportContext c, CancellationToken ct)`.
  - `public sealed class ImportContext`:
    - db, tenant context and tenant settings, `Today`;
    - code maps per kind: `Dictionary<string, Guid>` with `StringComparer.OrdinalIgnoreCase`, kept up to date as rows are added;
    - `List<string> NewLookups`;
    - `Guid EnsureLookup<T>(string code)`, which adds a category, trade or part group named after its code and records `"{sheet} {code}"`.
  - `public static class ImportRun { public static Task<ImportResult> RunAsync(CmmsDbContext db, ITenantContext ctx, SpreadsheetBook book, bool save, TimeSpan budget, CancellationToken ct); }`. Behaviour:
    - A sheet over 5,000 data rows: `DomainException("{0} has {1} rows; the limit is 5,000 per sheet. Split it into smaller files.")` before anything runs.
    - Unknown headings are listed in `IgnoredColumns`. A missing required heading on a sheet with rows is a problem on every row of that sheet: `"{0} column is missing."`.
    - Fully blank rows are skipped.
    - A key seen earlier in the same sheet: `"{0} appears twice in this sheet (row {1})."`.
    - Each row's outcome comes from its importer.
    - After each sheet: `SaveChangesAsync`.
    - Everything runs in one transaction under the execution strategy. `save=false` rolls back.
    - Over `budget` (checked between rows): roll back and return `Saved=false, Stopped="This file is too big to import in one go. Split it into smaller files."`.
    - Any other exception rolls back and propagates; the error handler answers 500 or 400 as usual.
  - Endpoints (all `.RequirePermission(Permissions.SettingsManage)`, under `/api/import`):
    - `POST /check` and `POST /run` take multipart `file` and return `ImportResult`, with a 45 s budget.
    - A missing file or one over 10 MB answers 400 `"Choose a file of up to 10 MB."`.
- Importer rules for these sheets:
  - Each importer builds the current `KInput` from the stored record, lays the row's non-blank cells over it, then runs `KRules` plus in-memory reference and uniqueness checks against the context maps.
  - Problems leave the record untouched. With no differences the outcome is Unchanged; otherwise New or Changed, through the Task 3 pieces.
  - The Locations importer orders its rows so parents come first. It reports a parent in another site (`"Parent location must belong to the selected site."`) and a loop (`"{0} can't be under itself."`).
  - Unknown site, store or supplier codes: `"{0} {1} not found."`, for example "Site CAI not found."

- [ ] **Step 1: Write the failing tests** in `ImportTests` (all `SkippableFact` with `Skip.IfNot(fx.Enabled, …)`; each test registers its own organization):
  - `A_check_saves_nothing_and_an_import_saves`.
    - Sites `[["CAI","Cairo Plant"]]` plus Locations `[["CAI","B1","Building 1",""],["CAI","R1","Room 1","B1"]]`.
    - `/check`: Sites New=1, Locations New=2, `Saved=false`, and `/api/sites` is unchanged.
    - `/run`: `Saved=true`. Room 1's path name reads "Building 1 › Room 1".
  - `A_known_code_updates_and_a_blank_keeps`. After the above, importing Sites `[["cai","Cairo Main",""]]` with an empty City column gives Changed=1 with change `Name: Cairo Plant → Cairo Main`. City stays as it was.
  - `Codes_and_headings_match_ignoring_case_and_spaces`. The headings `" site code "` and `"CODE"` are found, and `"b1"` updates `B1`.
  - `Blank_rows_are_skipped`. Rows 3 and 5 blank: row numbers in results are 2 and 4, with no problems.
  - `A_key_twice_in_a_sheet_is_a_problem`. Two rows with `CAI`: the second has the problem "CAI appears twice in this sheet (row 2).".
  - `Rows_depending_on_a_problem_row_are_problems`. A location under a parent row that has a problem gets "Location B9 not found." (or the parent's code).
  - `An_unknown_site_on_a_store_is_a_problem`. Stores `[["ST1","Main store","XX"]]` gives "Site XX not found."; with `CAI` the row is New.
  - `Unchanged_rows_are_not_written`. Importing the same Sites file twice: the second run is Unchanged, and the site's `updatedAt` (from `/api/sites/{id}`) is the same as after the first.
  - `A_stopped_run_saves_nothing`. Call `ImportRun.RunAsync` directly with a `CmmsDbContext` for the test organization (`new CmmsDbContext(options, new FixedTenantContext(tenantId))`, options from `fx.Services`), `save: true` and `budget: TimeSpan.Zero`. Expect `Saved=false`, `Stopped` set, and `/api/sites` unchanged.
  - `Only_administrators_can_import`. A user without `settings.manage` gets 403 on `/check`.
  - `Limits_are_enforced`. 5,001 rows in Sites gives 400 with the limit message; an 11 MB body gives 400.
  - `Arabic_headings_work`. Sheet and headings in Arabic import the same rows.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportTests"`. Expected: build errors or failures.
- [ ] **Step 3: Implement** `ImportRun`, the contract, the context, the seven importers and the endpoints.
- [ ] **Step 4: Run** the same command. Expected: all pass. Then `$B/wt-test`. Expected: green.
- [ ] **Step 5: Commit** `"Import run with sites, locations, lookups, suppliers and stores"`.

### Task 5: Assets and people

**Files:**
- Modify: `Features/Import/SheetImporters.cs` (Assets and People importers; custom-field columns)
- Modify: `Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/ImportAssetTests.cs`

**Interfaces:**
- Consumes: Task 3 `AssetRules`, `CreateAssetAsync`, `UpdateAsset`, `CustomFieldValues`, `EmployeeRules`, `PeopleEndpoints.Apply(Employee, EmployeeInput)`; Task 4 contract.
- Produces: none for later tasks, except that `ImportSheets` gains the Assets custom-field columns at run time: `ColumnDef(Key: "cf:" + def.Key, English: def.Label, Arabic: def.Label, Kind: Text)`.

- [ ] **Step 1: Write the failing tests:**
  - `Assets_are_added_with_their_place_parent_and_category`.
    - Sites and Locations from Task 4, plus Assets rows `PUMP-01` (location B1, category `PUMPS`, unknown) and `MOTOR-01` (parent `PUMP-01`, listed first).
    - Both are New. `NewLookups` contains `"Categories PUMPS"`. MOTOR-01's parent is PUMP-01.
  - `A_blank_asset_number_is_numbered_and_always_new`. With numbering on, two runs of the same row give 2 assets, numbered `AST-00001` and `AST-00002`.
  - `Moving_an_asset_records_a_transfer`. Changing its Site code adds an `AssetTransfer` row, as the screen does.
  - `Custom_fields_are_read_and_checked`.
    - A required number field "Power (kW)": a new asset without it gets "Power (kW) is required.".
    - With `"7.5"` it saves `"7.5"`.
  - `People_get_trades_and_sites_by_code`. `Trades="ELEC, MECH"` (ELEC known, MECH unknown and added) and `Sites="CAI"` link the employee. An unknown site gets "Site XX not found.".
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportAssetTests"`. Expected: failures.
- [ ] **Step 3: Implement** the two importers.
- [ ] **Step 4: Run** the same command. Expected: 5 passed.
- [ ] **Step 5: Commit** `"Import assets and people"`.

### Task 6: Parts and stock

**Files:**
- Modify: `Features/Import/SheetImporters.cs` (Parts and Stock importers)
- Test: `server/FourC.Cmms.Tests/ImportStockTests.cs`

**Interfaces:**
- Consumes: Task 3 `PartRules`, `ApplyPart`; `StockLedger.PostAsync(db, ctx, new StockPost(StockTransactionType.Adjustment, partId, storeId, qty, today, BatchNumber, ExpiryDate, Notes: "Opening balance (import)"))`.
- Parts: the part number is the key and is permanent. A new part's `AverageCost` is its standard price, as on screen. An unknown group is added; an unknown preferred supplier is a problem.
- Stock:
  - The key is part number + store code. Bin and levels are set on the `StockLevel`.
  - The opening quantity applies only when no level existed before this run. Those posts happen in `FinishAsync` after the levels are saved.
  - On an existing level a non-blank opening quantity adds the note "Opening quantity ignored for existing stock: use a stock take to change quantities." to `ImportResult.Notes`.
  - For shelf-life parts the opening quantity needs Batch and Expiry: `"{0} is a shelf-life item: enter the batch and expiry."`.

- [ ] **Step 1: Write the failing tests:**
  - `Opening_stock_is_posted_once`. A new part `BRG-6205` at standard price 6.8, with stock `ST1` opening 24: on hand is 24, and one ledger adjustment carries the note "Opening balance (import)". Importing the same file again leaves on hand at 24, adds a note, and gives Stock outcome Unchanged.
  - `Levels_update_on_existing_stock`. Re-order level 5 → 8 is Changed, and on hand stays the same.
  - `Shelf_life_parts_need_batch_and_expiry`. Without them the row is a problem; with them it posts that batch.
  - `Unknown_part_or_store_is_a_problem`. "Part XX not found." and "Store YY not found.".
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportStockTests"`. Expected: failures.
- [ ] **Step 3: Implement** the importers.
- [ ] **Step 4: Run** the same command. Expected: 4 passed.
- [ ] **Step 5: Commit** `"Import parts and stock"`.

### Task 7: PM tasks, steps, parts and schedules

**Files:**
- Modify: `Features/Import/SheetImporters.cs` (PmTasks, PmSteps, PmParts, PmSchedules importers)
- Test: `server/FourC.Cmms.Tests/ImportPmTests.cs`

**Interfaces:**
- Consumes: Task 3 `PmTaskRules`, `ApplyTask`, `ApplyTaskLists`, `PmScheduleRules`, `ApplySchedule`, `CreateSchedule`, `UpdateSchedule`, `ScheduleChangeRules`.
- PmSteps and PmParts collect their rows per task code. In `FinishAsync` each task that appears gets its whole list replaced through `ApplyTaskLists`, and the task counts as Changed when the list differs.
  - The results of those two sheets report per row: a step row is New when the task had no such step text at that number, and otherwise Unchanged or Changed.
  - Unknown task: `"PM task {0} not found."`.
- PmSchedules:
  - Schedule words map Every → FixedInterval, "Weekday of month" → DayOfWeekInMonth, "Day of month" → DateOfMonth. Week of month accepts 1–4 or "Last" (5).
  - A new row whose Schedule is blank, or names a meter schedule, gets `"Set up meter-based schedules on screen."`.
  - Updating an existing meter or round schedule keeps its meter fields, assets and extra tasks, because they are part of the current input the row is laid over.
  - Assignees are people codes.

- [ ] **Step 1: Write the failing tests:**
  - `A_task_gets_its_steps_and_parts`. PmTasks `LUB-M`, PmSteps rows (1 "Grease bearings", 2 "Check belts"), PmParts (`BRG-6205`, 2): the task has 2 steps in order and 1 part.
  - `Steps_listed_again_replace_the_old_list`. A second file with only step 1 "Grease all bearings" leaves exactly that one step. A task not on the sheet keeps its steps.
  - `A_schedule_comes_out_as_on_screen`. Code `PM-LUB-01`, Every 3 months from 2026-12-01 on asset `PUMP-01`: next due is 2026-12-01, and Describe in English reads "Every 3 months".
  - `A_meter_schedule_is_not_made_by_import`. Gives the problem above.
  - `Basis_cannot_change_after_generation`. A schedule with `GenerationCount > 0` and Count from = Completion is a problem with the screen's message.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportPmTests"`. Expected: failures.
- [ ] **Step 3: Implement** the four importers.
- [ ] **Step 4: Run** the same command. Expected: 5 passed.
- [ ] **Step 5: Commit** `"Import PM tasks, steps, parts and schedules"`.

### Task 8: Template, current data, help and problem rows

**Files:**
- Create: `Features/Import/ImportWorkbook.cs`
- Modify: `Features/ImportEndpoints.cs`
- Test: `server/FourC.Cmms.Tests/ImportExportTests.cs`

**Interfaces:**
- Produces:
  - `public static class ImportWorkbook`:
    - `static Task<byte[]> TemplateAsync(CmmsDbContext db, CancellationToken ct)` gives all sheets with headings only, in the request language, custom-field columns included, plus a Help sheet;
    - `static Task<byte[]> CurrentAsync(CmmsDbContext db, CancellationToken ct)` gives all sheets filled with codes, choices as words and Yes/No in the request language;
      - deleted records are left out and inactive ones included;
      - up to 5,000 rows per sheet, with the Help sheet's first line naming any sheet that was cut off;
      - meter-based or round schedules have a blank Schedule cell;
    - `static byte[] Problems(SpreadsheetBook book, ImportResult result)` gives, for each sheet with problems, its heading row and the problem rows as they were in the file, plus a last column "Problem" with the reasons joined by " ".
  - Endpoints:
    - `GET /api/import/template` gives file `CMX-import-template.xlsx`;
    - `GET /api/import/current` gives `CMX-data-{yyyy-MM-dd}.xlsx`;
    - `POST /api/import/problems` (multipart `file`) runs a check and returns the problem workbook `CMX-import-problems.xlsx`;
    - all of them need `settings.manage`.
  - Help sheet: for each sheet and column, the heading, whether it is required on new rows, and the allowed values for choices, in the request language.

- [ ] **Step 1: Write the failing tests:**
  - `Current_data_imports_back_with_no_changes`. Load a full set (sites, locations, assets with a custom field, parts with stock, people, suppliers, a PM task with steps and a schedule) through the import. Then `/current`, then `/check` with that file: every sheet has New=0, Changed=0, Problems=0.
  - `The_template_has_every_sheet_and_heading`. Reading `/template` gives the 15 sheets plus Help, and Assets' headings include the custom field's label.
  - `The_template_follows_the_persons_language`. With `Accept-Language: ar` the sheet names and headings are Arabic.
  - `Problem_rows_come_back_with_their_reason`. A file with one bad Sites row: `/problems` returns a workbook whose Sites sheet has 2 rows (heading and the bad row) and a "Problem" cell with the reason.
- [ ] **Step 2: Run** `$B/wt-test "FullyQualifiedName~ImportExportTests"`. Expected: failures.
- [ ] **Step 3: Implement** `ImportWorkbook` and the three endpoints.
- [ ] **Step 4: Run** the same command. Expected: 4 passed. Then the whole suite: `$B/wt-test`. Expected: green.
- [ ] **Step 5: Commit** `"Import template, current data, help and problem rows"`.

### Task 9: Settings › Import page

**Files:**
- Create: `web/src/pages/settings/ImportSettings.tsx`, `web/src/pages/settings/importView.ts`
- Test: `web/src/pages/settings/importView.test.ts`
- Modify:
  - `web/src/pages/settings/SettingsPage.tsx` (an `import` tab shown with `can(P.settingsManage)`);
  - `web/src/api/client.ts` (`apiBlob`);
  - `web/src/api/types.ts`;
  - `web/src/locales/en.json` and `ar.json` (`import.*`).

**Interfaces:**
- Consumes: Task 4/8 endpoints and the `ImportResult` shape (camelCase JSON). Outcomes come as strings `"New" | "Changed" | "Unchanged" | "Problem"`. Make sure the enum serializes as a string: add `[JsonConverter(typeof(JsonStringEnumConverter))]` on `RowOutcome` if the app doesn't already serialize enums as strings.
- Produces:
  - `export async function apiBlob(path: string, init?: RequestInit): Promise<Blob>` in `client.ts`, with the same headers and error handling as `api`;
  - `export type ImportResult`, `SheetResult`, `RowResult`, `FieldChange` in `types.ts`;
  - in `importView.ts`:
    - `export function fileKey(f: File): string` (`name|size|lastModified`);
    - `export function canImport(result: ImportResult | null, checkedKey: string | null, file: File | null): boolean`, true only when a check of this same file found at least one New or Changed row and `result.saved === false`;
    - `export function totals(result: ImportResult): { new: number; changed: number; unchanged: number; problems: number }`.
- The page is the spec's four steps:
  - two download links (`/api/import/template`, `/api/import/current`);
  - a `FileInput` that accepts `.xlsx,.csv`;
  - a Check button, then a summary table per sheet, with each sheet expanding to its changed and problem rows (up to 200 rows shown, then "…and N more");
  - New lookups and Notes;
  - "Import N rows" (enabled by `canImport`) and "Download problem rows" (when problems > 0, via `apiBlob` and the `downloadCsv`-style anchor download);
  - after Import, a success alert with the counts, or the Stopped message as a warning.
- Sheet names in the table come from `t('import.sheets.<Key>')`.

- [ ] **Step 1: Write the failing tests** in `importView.test.ts`:
  - `canImport needs a check of the same file with something to do`:
    - false with no result;
    - false when the key differs from the file's;
    - false when all rows are unchanged or problems;
    - false after `saved: true`;
    - true with one Changed row.
  - `totals adds the sheets up`: two sheets `{new:1,changed:2,unchanged:3,problems:0}` and `{new:4,changed:0,unchanged:0,problems:5}` give `{new:5,changed:2,unchanged:3,problems:5}`.
- [ ] **Step 2: Run** `$B/wt-vitest`. Expected: the new file fails (module not found).
- [ ] **Step 3: Implement** `importView.ts`, `apiBlob`, the types, `ImportSettings.tsx`, the tab and both locale files (every key in both, so `locales.test.ts` stays green).
- [ ] **Step 4: Run** `$B/wt-vitest`. Expected: all pass. Then `$B/wt-web`. Expected: `✓ built`.
- [ ] **Step 5: Check in the browser.** Run a local preview (as for 0.6.0: `docker build -t cmx-import-preview .`, run on port 8095 with its own database).
  - Download the template, fill Sites, Locations and Assets, check, import, then download current data and check it: zero changes.
  - Do the same in Arabic.
  - Fix what looks wrong, then remove the preview container, database and image.
- [ ] **Step 6: Commit** `"Settings › Import page"`.

### Task 10: Docs and the whole branch

**Files:**
- Modify: `CHANGELOG.md` (`## [0.7.0] - <date>` with Added: Import page, template, current data, problem rows; the rules users meet: blank keeps, nothing deleted, opening stock once, limits, `.xls` not accepted)
- Modify: `README.md` (a feature row), `docs/ROADMAP.md` if it lists data import

- [ ] **Step 1: Write** the CHANGELOG, README and roadmap entries.
- [ ] **Step 2: Run** `$B/wt-test`, `$B/wt-vitest` and `$B/wt-web`. Expected: all green. Server count: 296 plus the new tests, 2 skipped.
- [ ] **Step 3: Commit** `"Docs for 0.7.0"`.
