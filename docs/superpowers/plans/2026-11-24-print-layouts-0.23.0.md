# Print layouts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CMX 0.23.0 — administrator-edited layouts for the job card, purchase order, a new request receipt and labels, chosen by site and type, also shaping the purchase order e-mail.

**Architecture:** A `PrintLayout` table holds each layout (paper, conditions, texts, ordered blocks with shown fields as JSON). A fixed catalogue of papers, blocks and fields lives in Core and is mirrored on the web (both pinned to one literal by tests); the built-in layout of each paper equals today's print. The server picks a record's layout (site + kind precedence, like approval levels); the web print pages render a pure "body" component from the record and the layout, which the settings preview reuses.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit SkippableFact + ApiFixture; React 19 + Mantine 8 + TanStack Query + i18next, Vitest (`renderHtml` static markup helper in `web/src/test/render.tsx`).

**Spec:** `docs/superpowers/specs/2026-11-24-print-layouts-0.23.0-design.md`

## Global Constraints

- Every new text in en + ar (web via loc-add.py; server messages via ar-add.py; Arabic plurals complete).
- No new permissions: layouts are written with *Manage settings* (`settings.manage`); reading layouts and printing need only what printing the record already needs.
- Nothing changes until an organization sets up a layout: every built-in layout prints today's paper, and the supplier e-mail with no layout is today's e-mail.
- Header, footer and terms are plain text, escaped everywhere (React text nodes; `EmailText.Html` in e-mails), line breaks kept.
- Limits (verbatim): name up to 60, unique per paper; up to 20 layouts per paper; header and footer up to 500 characters each, terms up to 4,000 (each language); one default per paper, with no site and no kind; paper sizes `A4` | `A5` | `Letter`; label sizes `a4-3x8` (built-in), `a4-3x7`, `a4-2x7`, `roll-50x25`, `roll-100x50`.
- Precedence (verbatim): site + kind → site → kind → default → built-in. Layouts with no site, no kind and not default never apply by themselves.
- Fixed fields: purchase order `lines.description`, `lines.lineTotal`, `totals.total` can't be hidden.
- Discount hidden → unit price is the price after discount.
- Gate with CI's exact build (`wt-ci`), plus `wt-vitest` and `wt-web`. Commands run with `WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/print-layouts-0.23.0`.

## Review Focus

1. **A layout deleted or edited while a print address carries `?layout=<id>`**: the print falls back to the record's matched layout (a deleted id answers 404 and the page uses the match), never a blank page. Test: Vitest `chooseLayout falls back when the chosen layout is missing` (Task 6).
2. **A record whose site has no layout but whose kind does, at a site-limited organization**: the kind layout applies; a layout for a site the printer can't see never applies. Test: `Pick_skips_layouts_for_other_sites_and_kinds` (Task 1) and `For_endpoint_applies_precedence` (Task 2).
3. **Arabic page with only English texts filled (or the reverse)**: the filled language prints; nothing prints empty boxes. Test: Vitest `layoutText falls back to English` (Task 5).
4. **Long terms (4,000 characters) and a 500-character footer on A5**: they wrap and print, nothing is cut off on screen. Test: `Texts_at_their_limits_save_and_one_more_is_refused` (Task 2); visual wrap left to the builder (pre-wrap).
5. **Purchase order e-mail with every optional column hidden**: still number, description, line total and total, with quantity × price consistent when price shown. Test: `Po_email_with_minimal_layout_keeps_fixed_columns` (Task 3).

---

### Task 1: Core — layout model, catalogue and choice rule

**Files:**
- Create: `server/FourC.Cmms.Core/Settings/PrintLayouts.cs`, migration `PrintLayouts` (via `wt-ef PrintLayouts`)
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`
- Test: `server/FourC.Cmms.Tests/PrintLayoutRulesTests.cs` (plain `[Fact]` tests; put them in a class without `IClassFixture<ApiFixture>`)

**Interfaces:**
- Produces:
  - `public sealed class PrintBlock { public required string Key { get; set; } public bool Shown { get; set; } = true; public List<string> Fields { get; set; } = []; }`
  - `public class PrintLayout : TenantEntity { string Paper (≤20); string Name (≤60); bool IsDefault; Guid? SiteId; string? KindKey (≤60); string? PaperSize (≤10); bool ShowLogo = true; string? HeaderText, HeaderTextAr, FooterText, FooterTextAr (≤500); string? Terms, TermsAr (≤4000); List<PrintBlock> Blocks (JSON, default "[]"); string? LabelSize (≤20); List<string> LabelFields (JSON, default "[]") }`. Site filter like teams (`SiteId == null || CurrentSiteIds.Contains(SiteId)`). Index (TenantId, Paper, Name) unique, filtered `IsDeleted = 0`.
  - `public static class PrintPapers` with `const string JobCard = "job-card", PurchaseOrder = "purchase-order", Request = "request", Labels = "labels"`; `IReadOnlyList<string> All`; `IReadOnlyList<PaperBlock> Blocks(string paper)` where `record PaperBlock(string Key, IReadOnlyList<string> Fields, IReadOnlyList<string> BuiltInFields, bool BuiltInShown)`; `IReadOnlySet<string> FixedFields(string paper)` (as `"block.field"`); `IReadOnlyList<string> LabelFields = ["number", "name", "detail", "site"]`, `BuiltInLabelFields = ["number", "name", "detail"]`; `IReadOnlyList<string> PaperSizes = ["A4", "A5", "Letter"]`; `IReadOnlyList<string> LabelSizes = ["a4-3x8", "a4-3x7", "a4-2x7", "roll-50x25", "roll-100x50"]`; `PrintLayout BuiltIn(string paper)` (Id = Guid.Empty, Name "Built-in"). Block and field keys exactly the spec's tables, camelCase field keys (e.g. `details`: `status, priority, workType, trade, team, site, location, asset, serialNumber, plannedStart, due, assignees, requestedBy, costCentre`). Built-in: every block of today's print shown; `asset-fields` and `terms` hidden; new fields (`details.team`, `details.costCentre` on the job card; `details.orderType` on the PO) not in `BuiltInFields`; the PO `header.logo` not in `BuiltInFields`; the job card's built-in `ShowLogo` true, the PO's built-in `ShowLogo` false.
  - `public static class PrintLayoutRules { PrintLayout Pick(IEnumerable<PrintLayout> layouts, Guid? siteId, string? kindKey, string paper); }`

- [ ] **Step 1: Write the failing tests** in `PrintLayoutRulesTests.cs`:
  - `Pick_follows_site_and_kind_precedence`: layouts L1 (site S, kind K), L2 (site S), L3 (kind K), L4 (default), L5 (no conditions, not default). For (S, K) → L1; remove L1 → L2; remove L2 → L3; remove L3 → L4; remove L4 → built-in (`Id == Guid.Empty`). L5 is never picked.
  - `Pick_skips_layouts_for_other_sites_and_kinds`: only (site T, kind K) and (site S, kind J) layouts; for (S, K) → built-in. Kind keys compare case-insensitively for work type names.
  - `Built_in_layouts_match_todays_prints`: job card blocks in order `header, details, description, technician-notes, warranty, asset-fields, checklist, contractors, labour, parts, completion, signatures, terms`, with `asset-fields` and `terms` hidden, and `details` built-in fields not containing `team` or `costCentre`; PO blocks `header, supplier, details, lines, totals, notes, approvals, terms`, `terms` hidden, PO `ShowLogo` false, `details` built-in without `orderType`; request blocks `header, details, description, access-windows, progress, terms`; labels built-in fields `number, name, detail` and size `a4-3x8`.
  - `Catalogue_matches_the_web_list`: `PrintPapers` blocks and fields equal a literal list in the test (the same literal the web test uses in Task 5).
- [ ] **Step 2: Run** `wt-test "PrintLayoutRulesTests"` — Expected: fail (types missing).
- [ ] **Step 3: Implement** the model, catalogue, rule, configuration and migration (`wt-ef PrintLayouts`; check it adds only the PrintLayouts table and its indexes). `Pick` ignores layouts of other papers and soft-deleted ones (the caller passes filtered rows).
- [ ] **Step 4: Run** `wt-test "PrintLayoutRulesTests"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(print): layout model, catalogue and choice rule`

### Task 2: Layout endpoints

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PrintLayoutEndpoints.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (map), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/PrintLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `PrintLayoutInput(string? Paper, string? Name, bool IsDefault, Guid? SiteId, string? KindKey, string? PaperSize, bool ShowLogo, string? HeaderText, string? HeaderTextAr, string? FooterText, string? FooterTextAr, string? Terms, string? TermsAr, List<PrintBlock>? Blocks, string? LabelSize, List<string>? LabelFields)`.
  - `GET /api/print-layouts?paper=` (signed in) → layouts of that paper `[{ id, name, isDefault, siteId, siteName, kindKey, kindName, paperSize, … all fields }]`, built-in not included.
  - `GET /api/print-layouts/built-in?paper=` → the built-in layout.
  - `GET /api/print-layouts/{id}` (signed in) → one layout (404 for another organization's or a site the person can't see).
  - `GET /api/print-layouts/for?paper=&id=` (signed in) → the layout that applies to that record: loads the record through the filtered DbSets (work order, purchase order, work request; for labels `id` is a single asset, location or part and `kind` comes from `paper=labels&labelKind=`) — 404 when the person can't see it — then `PrintLayoutRules.Pick`. Kind: work order `Type.ToString()`, PO `OrderTypeId`, request `RequestTypeId`, labels the label kind.
  - `POST /api/print-layouts`, `PUT /api/print-layouts/{id}`, `DELETE /api/print-layouts/{id}` (settings.manage).
  - Validation messages (field `blocks` unless noted): "Choose a paper from the list." (`paper`); "Give the layout a name." / "Keep the name to 60 characters." / "Another layout of this paper has this name." (`name`); "A paper can have up to 20 layouts." ; "The default layout applies when no other does, so it can't have a site or type." (`isDefault`); "Another layout already has this site and type." (`siteId`); "Choose a site you can see." (`siteId`); "Choose a type from the list." (`kindKey`); "Choose a paper size from the list." (`paperSize`); "Choose a label size from the list." (`labelSize`); "Choose blocks from the list." ; "Choose fields from the list."; "This field always prints."; "Choose an asset field from the list."; "Keep the header to 500 characters." (`headerText`), same for footer (`footerText`); "Keep the terms to 4,000 characters." (`terms`). Saving a default clears the paper's other default.

- [ ] **Step 1: Write the failing tests** in `PrintLayoutTests.cs` (register-org pattern of `ViewTests`):
  - `Layout_round_trip`: POST a job-card layout (site S, kind "Corrective", A5, header/footer/terms in both languages, blocks with `parts` hidden and `details` fields `[status, asset]`), GET list and by id return it exactly; PUT changes the name; DELETE removes it from the list.
  - `For_endpoint_applies_precedence`: a work order at site S of type Corrective; layouts (S, Corrective), (S), (Corrective), default; `GET /for?paper=job-card&id=` returns each in turn as the more specific ones are deleted, then the built-in (`id` = empty Guid). A PO with order type T and a request with request type R pick their kind layouts the same way. Labels: `for?paper=labels&labelKind=asset&id=<asset>` picks an `asset` kind layout.
  - `Layout_validation`: each message above for: unknown paper, empty and 61-character name, a duplicate name, a 21st layout, a default with a site, two layouts with the same site and kind (and a second hand-picked layout with neither is allowed), a site of another organization, an unknown work type, `paperSize: "A3"`, `labelSize: "big"`, unknown block, unknown field, hiding `lines.description` on a PO, an unknown asset field key on `asset-fields`, a 501-character header and Arabic footer, 4,001-character terms.
  - `Texts_at_their_limits_save_and_one_more_is_refused`: 500/500/4,000 save; 501/4,001 are refused.
  - `Layouts_are_per_organization`: another org's layout id → 404 on GET, PUT and as `?layout` for `/for` reads; a work order of another org → 404 on `/for`.
  - `Writing_needs_manage_settings`: a Technician gets 403 on POST and 200 on GET list.
- [ ] **Step 2: Run** `wt-test "PrintLayoutTests"` — Expected: fail (404s).
- [ ] **Step 3: Implement** the endpoints and validation; add the messages to ar.json.
- [ ] **Step 4: Run** `wt-test "PrintLayoutTests|PrintLayoutRulesTests"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(print): layout endpoints and validation`

### Task 3: Purchase order e-mail follows the layout; label site; request's work order due

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/EmailOutbox.cs` (`PoToSupplierAsync`), `server/FourC.Cmms.Api/Features/EmailTemplates.cs` (`PurchaseOrder`), `server/FourC.Cmms.Api/Features/LabelEndpoints.cs` (`Label` gains `string? Site`), `server/FourC.Cmms.Api/Features/WorkRequestEndpoints.cs` (detail's `workOrder` gains `dueDate`, `dueAt`)
- Test: `server/FourC.Cmms.Tests/PoEmailLayoutTests.cs`, add `Labels_carry_their_site` and `Request_detail_shows_its_work_orders_due` to it (one file for this task)

**Interfaces:**
- Consumes: Tasks 1–2 (`PrintLayoutRules.Pick`, `PrintPapers.BuiltIn`).
- Produces: `EmailTemplates.PurchaseOrder(string org, PoDocument po, PoEmailLayout? layout = null)` where `PoEmailLayout` carries the shown facts (order date, expected date, delivery address, payment terms, quote ref, shipping method, requested by, currency, order type, supplier fields), shown line columns, shown totals, notes and the header, footer and terms text already chosen for the e-mail's language. Null = today's e-mail.

- [ ] **Step 1: Write the failing tests** (read queued e-mails as `ContractAlertTests.MailsAsync` does; e-mail enabled with `TestSettings.SetSettingsAsync`):
  - `Po_email_without_layout_is_unchanged`: send a PO with no layouts; the HTML has the columns "Item", "Qty", "Unit price", "Total", the facts "Order date", "Payment terms", "Your quote", and no "Discount" or "Terms" text — the same assertions the existing PO e-mail test makes (find it with grep "PoToSupplier\|po-sent" and reuse its expectations).
  - `Po_email_follows_the_layout`: a default PO layout hiding `details.paymentTerms`, `lines.unit`, `lines.discount`, showing `details.shippingMethod`, with header "Ref <b>X</b>", terms "Pay in 30 days" and Arabic terms "الدفع خلال 30 يومًا"; organization language English → body contains "Ref &lt;b&gt;X&lt;/b&gt;", "Pay in 30 days", the shipping method, no payment terms value; unit price equals the price after discount; with organization language Arabic → the Arabic terms.
  - `Po_email_with_minimal_layout_keeps_fixed_columns`: a layout hiding every optional line column and total → the body still shows the description, line total and total.
  - `Labels_carry_their_site`: `POST /api/labels` for an asset returns `site` = its site name; for a part, `site` is null.
  - `Request_detail_shows_its_work_orders_due`: approve a request into a work order with a due date; the request detail's `workOrder.dueDate` equals it.
- [ ] **Step 2: Run** `wt-test "PoEmailLayoutTests"` — Expected: fail.
- [ ] **Step 3: Implement.** `PoToSupplierAsync` loads the paper's layouts (site and order type of the PO), picks one, and when it isn't the built-in builds `PoEmailLayout` (texts in the e-mail language: `Translator.CurrentLanguage == "ar"` and Arabic text filled → Arabic). The template renders the facts, supplier lines, the shown columns (unit price after discount when discount is hidden), the shown totals, notes, header text above the facts, terms under the table and footer text before the usual e-mail footer, all through `EmailText.Html` with line breaks as `<br>`.
- [ ] **Step 4: Run** `wt-test "PoEmailLayoutTests|Email|Label|WorkRequest"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(print): purchase order e-mail follows its layout; label site; request work order due`

### Task 4: Web — catalogue mirror and layout helpers

**Files:**
- Create: `web/src/print/layouts.ts`, `web/src/print/layouts.test.ts`
- Modify: `web/src/api/types.ts` (`PrintLayout` type)

**Interfaces:**
- Produces:
  - `export type Paper = 'job-card' | 'purchase-order' | 'request' | 'labels'`; `export type PrintBlock = { key: string; shown: boolean; fields: string[] }`; `export type PrintLayout = { id: string; paper: Paper; name: string; isDefault: boolean; siteId?: string | null; siteName?: string | null; kindKey?: string | null; kindName?: string | null; paperSize?: 'A4' | 'A5' | 'Letter' | null; showLogo: boolean; headerText?: string | null; headerTextAr?: string | null; footerText?: string | null; footerTextAr?: string | null; terms?: string | null; termsAr?: string | null; blocks: PrintBlock[]; labelSize?: LabelSize | null; labelFields: string[] }`.
  - `export const PAPER_BLOCKS: Record<Paper, { key: string; fields: string[]; builtInFields: string[]; builtInShown: boolean }[]>`, `FIXED_FIELDS: Record<Paper, string[]>`, `LABEL_FIELDS`, `LABEL_SIZES`, `PAPER_SIZES` (the Core literal).
  - `export function builtInLayout(paper: Paper): PrintLayout`.
  - `export function blocksInOrder(layout: PrintLayout): PrintBlock[]` (shown blocks in layout order, plus any catalogue block missing from the stored list, hidden).
  - `export function shows(layout: PrintLayout, block: string, field?: string): boolean` (fixed fields always true).
  - `export function layoutText(layout: PrintLayout, which: 'header' | 'footer' | 'terms', language: string): string` (Arabic when `language === 'ar'` and filled, else English, else '').
  - `export function poUnitPrice(line: { unitPrice: number; discountPercent?: number | null; quantity: number; lineTotal: number }, discountShown: boolean): number` (after discount when hidden: `lineTotal / quantity` when quantity ≠ 0, else `unitPrice × (1 − discount/100)`).
- [ ] **Step 1: Write the failing tests** in `layouts.test.ts`: the catalogue equals the literal (same as Task 1's); `builtInLayout('job-card')` hides `asset-fields` and `terms` and has no `team`/`costCentre` fields; `builtInLayout('purchase-order').showLogo === false`; `blocksInOrder` follows the stored order and appends missing catalogue blocks hidden; `shows(po, 'lines', 'description')` true even when not in fields; `layoutText falls back to English` (Arabic page, Arabic empty → English; Arabic filled → Arabic; both empty → ''); `poUnitPrice` → 90 for 100 at 10% with discount hidden, 100 with it shown.
- [ ] **Step 2: Run** `wt-vitest` — Expected: the new file fails.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-vitest` — Expected: pass.
- [ ] **Step 5: Commit** `feat(web): print layout catalogue and helpers`

### Task 5: Web — job card and purchase order follow the layout

**Files:**
- Create: `web/src/print/JobCardBody.tsx`, `web/src/print/PoBody.tsx`, `web/src/print/PrintTexts.tsx` (header text, terms and footer; `@page` size), `web/src/print/usePrintLayout.ts` (+ `LayoutChooser`), `web/src/print/bodies.test.tsx`
- Modify: `web/src/pages/work/WorkOrderPrintPage.tsx` (renders `JobCardBody`), `web/src/pages/purchasing/PoDetailPage.tsx` (`PoPrintPage` renders `PoBody`), locales

**Interfaces:**
- Consumes: Task 4; `GET /print-layouts/for`, `GET /print-layouts?paper=`, `GET /print-layouts/{id}` (Task 2).
- Produces:
  - `export function JobCardBody({ w, layout, orgName, terms, language, assetFields }: { w: WorkOrderDetail; layout: PrintLayout; orgName: string; terms: Terminology; language: string; assetFields?: Record<string, string | null> | null })` — today's job card content split into the blocks, rendered by `blocksInOrder` and `shows`; no hooks that need the auth context (the page passes `orgName`, `terms`, `language`).
  - `export function PoBody({ o, layout, orgName, language }: { o: PurchaseOrderDetail; layout: PrintLayout; orgName: string; language: string })`.
  - `export function usePrintLayout(paper: Paper, recordId: string | undefined, extra?: Record<string, string>): { layout: PrintLayout | undefined; layouts: PrintLayout[]; matchedId: string; choose: (id: string) => void }` — reads `?layout=` from the address; when the chosen id answers 404 it uses the matched layout.
  - `export function chooseLayout(chosen: PrintLayout | null | undefined, matched: PrintLayout): PrintLayout`.
  - `export function LayoutChooser(props)` — a Select (hidden when printing) listing the matched layout first, then the others and "Built-in".

- [ ] **Step 1: Write the failing tests** in `bodies.test.tsx` with `renderHtml` and a fixture work order and purchase order:
  - `the built-in job card shows today's sections in order`: titles appear in the order Tasks, Labour, Parts, Completion, and the details labels Status, Priority, Due appear; no "Terms".
  - `a job card layout hides blocks and fields and follows its order`: blocks `[parts, checklist]` first with `labour` hidden and `details.fields = ['status']` → "Parts" before "Tasks", no "Labour" heading, no "Priority" label; terms block shown with terms "Wear gloves" → "Wear gloves" present.
  - `texts are escaped`: header "<b>x</b>" renders as `&lt;b&gt;x&lt;/b&gt;`.
  - `the purchase order hides columns and shows price after discount`: discount hidden on a line 2 × 100 at 10% → "Discount" header absent, unit price cell shows 90.00, total 180.00; `lines.description` can't be hidden.
  - `chooseLayout falls back when the chosen layout is missing` (pure).
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement.** Move the current markup into the bodies block by block without changing the built-in output; the pages fetch the record, the layout (`usePrintLayout`) and, when `asset-fields` is shown, the asset (`GET /assets/{id}` for `customFields` and their labels from `/custom-fields?entityType=Asset`). `PrintTexts` adds `@page { size: A5 }` (or Letter) and tighter spacing on A5. Labels for the new fields (team, cost centre, order type, terms) in en and ar.
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass; build clean.
- [ ] **Step 5: Commit** `feat(web): job card and purchase order print with layouts`

### Task 6: Web — request receipt and labels

**Files:**
- Create: `web/src/print/RequestBody.tsx`, `web/src/pages/work/RequestPrintPage.tsx`
- Modify: `web/src/App.tsx` (route `work-requests/:id/print` beside the other print routes), `web/src/pages/work/WorkRequestsPage.tsx` (Print button on the request), `web/src/api/types.ts` (request `workOrder.dueDate`, `dueAt`; label `site`), `web/src/pages/labels/LabelsPage.tsx`, `web/src/pages/labels/sheet.ts`, `web/src/print/bodies.test.tsx`, `web/src/pages/labels/sheet.test.ts` (create if absent), locales

**Interfaces:**
- Consumes: Tasks 4–5 (`usePrintLayout`, `LayoutChooser`, `PrintTexts`).
- Produces:
  - `export function RequestBody({ r, layout, orgName, terms, language }: { r: WorkRequestDetail; …same as JobCardBody })`.
  - `export const SHEETS: Record<LabelSize, { pageWidthMm: number; pageHeightMm: number; columns: number; rows: number; labelWidthMm: number; labelHeightMm: number; marginTopMm: number; qrMm: number }>` with today's `a4-3x8` values unchanged and the four new sizes; `export function sheetFor(size?: LabelSize | null)` (built-in `a4-3x8`).

- [ ] **Step 1: Write the failing tests:**
  - `bodies.test.tsx`: `the request receipt shows its blocks` (number, requester, description, access window, the linked work order number and due date) and hides `details.phone` when the layout drops it.
  - `sheet.test.ts`: `a4-3x8` equals today's geometry (3 columns, 8 rows, 70 × 37 mm); `a4-2x7` 2 × 7 of 99.1 × 38.1; `roll-50x25` one label of 50 × 25 on a 50 × 25 page; every sheet's columns × label width fits its page width.
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement.** The labels page asks `/print-layouts/for?paper=labels&labelKind=<type>&id=<first id>` (and offers the chooser), uses the layout's size and fields (`site` from the label's new field), and when the sheet's records span sites with different layouts says "These labels use the layout of the first one." (decided on the client from each record's site vs the layouts' sites).
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass.
- [ ] **Step 5: Commit** `feat(web): request receipt and label layouts`

### Task 7: Web — Settings › Print layouts with preview

**Files:**
- Create: `web/src/pages/settings/PrintLayoutsSettings.tsx`, `web/src/pages/settings/PrintLayoutEditor.tsx`, `web/src/print/editor.ts` + `editor.test.ts`
- Modify: `web/src/pages/settings/SettingsPage.tsx` (tab `print-layouts`, `show: can(P.settingsManage)`), locales

**Interfaces:**
- Consumes: Tasks 4–6 (bodies, `builtInLayout`, `PAPER_BLOCKS`, `SHEETS`); layout endpoints (Task 2).
- Produces:
  - `export function copyLayout(from: PrintLayout, name: string): PrintLayout` (new, not default, no conditions).
  - `export function moveBlock(layout: PrintLayout, key: string, by: -1 | 1): PrintLayout`.
  - `export function toggleBlock(layout: PrintLayout, key: string): PrintLayout`, `toggleField(layout: PrintLayout, block: string, field: string): PrintLayout` (fixed fields unchanged).
  - `export function kindOptions(paper, data)` (work types; order types; request types; asset/location/part).

- [ ] **Step 1: Write the failing tests** in `editor.test.ts`: `copyLayout` of the built-in keeps its blocks and clears default and conditions; `moveBlock` moves and clamps; `toggleField` can't untick a fixed field; `toggleBlock` hides and shows.
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement** the tab: a sub-tab per paper; a table of layouts with "Built-in" first (read-only, "Copy to a new layout"); the editor (name, site, kind, default, paper size, logo, header/footer/terms in English and Arabic side by side, blocks with show/hide, up/down and field ticks, asset custom fields for `asset-fields`, label size and fields for labels) and beside it (under it on narrow screens) the preview: the paper's body component rendering the latest record (`GET /work-orders?pageSize=1&sort=createdAt&dir=desc`, the same for POs and requests, a few assets/locations/parts for labels) with a number picker to choose another; Save, Delete (confirm).
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass.
- [ ] **Step 5: Commit** `feat(web): Settings › Print layouts with live preview`

### Task 8: Guide and full verification

**Files:**
- Create: `docs/PRINT_LAYOUTS.md` (style of `docs/HOME_PAGES_AND_ROLES.md`)

- [ ] **Step 1: Write the guide.**
- [ ] **Step 2: Run** `wt-ci`, `wt-vitest`, `wt-web` — Expected: all pass.
- [ ] **Step 3: Commit** `docs: print layouts guide`
