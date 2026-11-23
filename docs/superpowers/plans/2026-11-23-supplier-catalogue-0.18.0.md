# Supplier catalogue and purchase units (0.18.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A per-part supplier catalogue (supplier part number, pack, price, discount, lead time, minimum, last price paid) that fills order lines in whole packs, with receiving that books pieces, on the web and the phone.

**Architecture:**
- A `SupplierPart` table sits beside `Part`. Pure `PackMath` holds the pack arithmetic, and one server helper (`CatalogueEndpoints.FillAsync`) fills order lines for the order form, the re-order proposal and ordering from requests.
- `PurchaseOrderLine` gains `PackSize` and `SupplierPartNumber`. Its existing `UnitOfMeasure` becomes the order unit (the pack name), so documents, receiving and the phone show packs unchanged.
- `ReceiveCoreAsync` converts packs into pieces, and every "on order" or request amount multiplies by `PackSize`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server, named Tenant/Site filters), React 19 + Mantine 8 + TanStack Query + i18next (web and the tech app in `web/src/tech`), xUnit integration tests, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-supplier-catalogue-0.18.0-design.md`

## Global Constraints

- No new NuGet or npm packages.
- Arabic for every new screen text (web `en.json` + `ar.json` via `loc-add.py`, Arabic plurals `_zero/_one/_two/_few/_many/_other`). Every new server message goes through `Translator.T`, with an entry in `server/FourC.Cmms.Api/Localization/ar.json` (`ar-add.py`).
- Stock stays in the part's own unit. **Pieces = order quantity × `PackSize`.**
- Whole packs:
  - packs = pieces ÷ pieces per pack, **rounded up**, at least the minimum;
  - received stock = received × `PackSize`;
  - unit cost per piece = net price × exchange rate ÷ `PackSize`, rounded to 4 decimals.
- A receipt never changes a catalogue price. It records only the last price paid (gross price per pack, its discount, currency and date), converted through pieces when the packs differ.
- Each catalogue row keeps its **own currency**. A catalogue price is used only on orders in that currency. Nothing is converted between currencies.
- The preferred supplier stays `Part.PreferredSupplierId`. The star on a row sets it, and setting it (form or import) without a row creates one with:
  - a pack of 1 in the part's unit and the part's lead time;
  - currency = the company's currency, else the base currency;
  - price = last purchase price, else standard price, **only when that currency is the base currency**, otherwise empty.
- Requests keep 0.16.0's rule: a request line is ordered once it is on a live order line. Only the amounts shown convert to pieces.
- Existing orders are packs of 1 in the part's unit, and every existing test stays green unchanged.
- Commands (scratchpad `bin` on PATH, `export WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/catalogue-0.18.0`):
  - server tests: `wt-test [filter]`
  - web tests: `wt-vitest`
  - web build: `wt-web`
  - migration: `wt-ef <Name>`

## Review Focus

1. **Every quantity meant as pieces is pieces.** This covers stock after a receipt, "on order" (parts list, part page, re-order proposal, inventory report) and a request's ordered/received amounts, for packs of 1 and of 100. Tests: `Receiving_a_box_books_its_pieces_at_cost_per_piece` and `On_order_counts_pieces_everywhere` (Task 4).
2. **Cost per piece with discounts, exchange rates and part of a pack received.** Average cost must follow. Tests: `Half_a_box_received_books_half_its_pieces` and `Foreign_currency_pack_cost_per_piece_uses_the_rate` (Task 4).
3. **A catalogue change (pack size, price) never changes an existing order line.** Test: `Catalogue_change_leaves_existing_order_lines_alone` (Task 3).
4. **Supplier change on a draft keeps at least the pieces.** A currency mismatch prices from today's price per piece × pack. Tests: `Fill_keeps_the_pieces_when_the_pack_changes` and `Other_currency_takes_the_pack_at_todays_price` (Task 3).
5. **The phone receives in packs and books pieces.** Test: `Phone_receipt_of_a_pack_books_pieces` (Task 4).

---

### Task 1: Data, migration and pack arithmetic

**Files:**
- Create: `server/FourC.Cmms.Core/Purchasing/Catalogue.cs`
- Modify: `server/FourC.Cmms.Core/Purchasing/Purchasing.cs` (order line fields), `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`, new migration `SupplierCatalogue`
- Test: `server/FourC.Cmms.Tests/PackMathTests.cs` (pure), `server/FourC.Cmms.Tests/CatalogueSetupTests.cs`

**Interfaces:**
- Produces (Core, namespace `FourC.Cmms.Core.Purchasing`):
  - `class SupplierPart : TenantEntity` with these fields:
    - `PartId`, `Part?`, `SupplierId`, `Supplier?`, `SupplierPartNumber string?`
    - pack: `PackName string` (required), `PackSize decimal` (18,4), `PackPrice decimal?` (18,4), `Currency string` (3)
    - terms: `DiscountPercent decimal` (9,3), `LeadTimeDays int?`, `MinPacks int = 1`, `Notes string?`, `IsActive = true`
    - last paid: `LastPaidPrice decimal?` (18,4), `LastPaidDiscount decimal?` (9,3), `LastPaidCurrency string?` (3), `LastPaidOn DateOnly?`
  - Unique `(TenantId, PartId, SupplierId)` filtered `IsDeleted = 0`. FKs to `Part` and `Supplier` are `NoAction`: companies and parts are soft-deleted, so the database never cascades and Task 2 removes the rows itself.
  - `static class PackMath` with these methods:
    - `decimal Packs(decimal pieces, decimal packSize, int minPacks)`: whole packs, rounded up, at least `max(minPacks, 1)`; 0 pieces → `max(minPacks,1)`.
    - `decimal CostPerPiece(decimal netPricePerUnit, decimal exchangeRate, decimal packSize)`: rounded to 4 decimals.
    - `decimal PriceForPack(decimal price, decimal fromPackSize, decimal toPackSize)`: price per piece × `toPackSize`, rounded to 4 decimals.
  - `PurchaseOrderLine.PackSize decimal = 1` (18,4) and `SupplierPartNumber string?` (100). Ignored computed `Pieces => Quantity * PackSize` and `PiecesReceived => QuantityReceived * PackSize`.
  - DbSet `SupplierParts`. Site filter: none (tenant only, like `Part`).
- Migration data: seed a row for every part with `PreferredSupplierId`, per Global Constraints (pack name = the part's unit, `PackSize` 1, `MinPacks` 1).

- [ ] **Step 1: Write the failing tests**

```csharp
// PackMathTests (plain [Fact])
[Fact] public void Packs_round_up_to_whole_packs_and_the_minimum()
// Packs(20,100,1)=1; Packs(250,100,1)=3; Packs(100,100,1)=1; Packs(20,100,2)=2; Packs(5,2.5m,1)=2; Packs(0,100,1)=1; Packs(3,1,1)=3
[Fact] public void Cost_per_piece_divides_the_pack_price()
// CostPerPiece(50,1,100)=0.5m; CostPerPiece(45,48.5m,10)=218.25m; CostPerPiece(10,1,3)=3.3333m
[Fact] public void Price_converts_between_packs_through_pieces()
// PriceForPack(50,100,10)=5m; PriceForPack(5,10,100)=50m; PriceForPack(7,1,1)=7m
// CatalogueSetupTests
[SkippableFact] public async Task Order_lines_without_a_pack_are_packs_of_one()
// POST /api/purchase-orders with a part line (no packSize) → GET line packSize 1, unitOfMeasure "EA"
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test "PackMathTests|CatalogueSetupTests"`
Expected: FAIL (`PackMath` missing; `packSize` absent).

- [ ] **Step 3: Implement the entity, `PackMath`, order line fields and DbContext config**
  - The PO detail and list line projections in `PurchasingEndpoints` return `PackSize` and `SupplierPartNumber`.

- [ ] **Step 4: Generate the migration and add the seed SQL**

Run: `wt-ef SupplierCatalogue`. Then add the `INSERT … SELECT` for parts with a preferred supplier and no row. Currency = `ISNULL(NULLIF(s.Currency,''), <tenant base currency from Tenants.Settings JSON '$.currency'>)`. Price = `COALESCE(LastPurchasePrice, StandardPrice)` only when that currency equals the base currency, else NULL.

Expected: migration created, the solution builds, and the tests pass.

- [ ] **Step 5: Run the tests**

Run: `wt-test "PackMathTests|CatalogueSetupTests"` then `wt-test`
Expected: PASS; whole suite green.

- [ ] **Step 6: Commit** `feat(catalogue): supplier catalogue data and pack arithmetic`

---

### Task 2: Catalogue on the part and the company

**Files:**
- Create: `server/FourC.Cmms.Api/Features/CatalogueEndpoints.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs`, `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (part save: preferred supplier ensures a row), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/CatalogueTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `SupplierPartInput(Guid SupplierId, string? SupplierPartNumber, string? PackName, decimal PackSize, decimal? PackPrice, string? Currency, decimal DiscountPercent, int? LeadTimeDays, int MinPacks, string? Notes, bool IsActive = true, bool IsPreferred = false)`.
  - `GET /api/parts/{id}/suppliers` (`inventory.view`) returns rows with:
    - row: `{ id, supplierId, supplierCode, supplierName, supplierActive, supplierPartNumber }`
    - pack and terms: `packName, packSize, packPrice, currency, discountPercent, leadTimeDays, minPacks, notes, isActive, isPreferred`
    - last paid: `lastPaidPrice, lastPaidDiscount, lastPaidCurrency, lastPaidOn`
  - `POST` and `PUT /api/parts/{id}/suppliers/{rowId}`, and `DELETE /api/parts/{id}/suppliers/{rowId}`. These need `inventory.manage` **or** `suppliers.manage` (checked in the handler).
    - `PackName` defaults to the part's unit and `Currency` to the company's, else base.
    - `IsPreferred` true sets `Part.PreferredSupplierId`. False on the preferred row clears it.
    - Deleting the preferred row clears it too.
  - Validation messages:
    - "Choose a company with the Supplier role." (newly chosen)
    - "This supplier is already in the part's catalogue."
    - "Pieces per pack must be more than 0."
    - "The price can't be below 0."
    - "Discount must be between 0 and 100%."
    - "Minimum order must be at least 1 pack."
    - "Use a 3-letter currency code."
  - `POST /api/parts/{id}/suppliers/{rowId}/use-last-price` copies `LastPaidPrice` and `LastPaidDiscount` into `PackPrice` and `DiscountPercent`. Refusals:
    - 400 "Nothing has been received from this supplier yet." (no last price);
    - 400 "The last price was paid in another currency." (currency differs).
  - `GET /api/suppliers/{id}/parts` (`purchasing.view`) returns `{ partId, partNumber, partName, supplierPartNumber, packName, packSize, packPrice, currency, isActive }`.
  - The company `DELETE /api/suppliers/{id}` and the part delete remove that company's or part's catalogue rows (soft delete, as the record).
  - `internal static Task<SupplierPart> EnsureRowAsync(CmmsDbContext db, Part part, Guid supplierId, CancellationToken ct)` creates the row per Global Constraints when missing. The part form's POST and PUT call it when `PreferredSupplierId` is set.

- [ ] **Step 1: Write the failing tests** in `CatalogueTests`. Setup:
  - `PurchaseRequestTests.OrgAsync` (base currency from settings);
  - `CompanyTests.ContractorAsync` / a supplier company POST.

```csharp
[SkippableFact] public async Task Part_has_several_suppliers_with_packs_and_prices()
// two rows: SKF (Box of 100, 100, 45, 2%) and LOCAL (EA, 1, 0.6) → GET lists both with currency = company or base; second row for SKF → 400 "This supplier is already in the part's catalogue."
[SkippableFact] public async Task Catalogue_rows_are_checked()
// packSize 0, price -1, discount 120, minPacks 0, a contractor-only company → each 400 with the message above
[SkippableFact] public async Task Star_sets_the_preferred_supplier_and_choosing_one_on_the_part_adds_a_row()
// PUT row isPreferred true → GET /api/parts/{id} preferredSupplierId = that company; PUT part with preferredSupplierId of a company without a row → a row with packSize 1, the part's unit, price = last purchase or standard price (company in base currency)
[SkippableFact] public async Task Foreign_currency_supplier_row_starts_without_a_price()
// company currency USD, base EGP: choose it as preferred on the part → row currency USD, packPrice null
[SkippableFact] public async Task Use_last_price_copies_price_and_discount_in_the_same_currency()
// set lastPaid fields through the DbContext (Task 4 records them) → POST use-last-price → packPrice and discountPercent updated; lastPaidCurrency differs → 400; none → 400
[SkippableFact] public async Task Company_lists_the_parts_it_supplies_and_deleting_it_removes_its_rows()
// GET /api/suppliers/{id}/parts → the part; DELETE company (no history) → 204 and the part's catalogue has no row for it
[SkippableFact] public async Task Viewer_reads_the_catalogue_but_cannot_change_it()
// Viewer GET → 200; POST → 403
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test CatalogueTests`
Expected: FAIL (404).

- [ ] **Step 3: Implement** `CatalogueEndpoints` and `EnsureRowAsync` as above. Translate the messages.

- [ ] **Step 4: Run the tests**

Run: `wt-test CatalogueTests` then `wt-test Inventory` and `wt-test Company`
Expected: PASS.

- [ ] **Step 5: Commit** `feat(catalogue): suppliers per part with packs, prices and the preferred star`

---

### Task 3: Filling order lines from the catalogue

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/CatalogueEndpoints.cs` (fill helper and endpoint), `server/FourC.Cmms.Api/Features/PurchasingEndpoints.cs` (`LineInput` + apply, from-proposal, expected date), `server/FourC.Cmms.Api/Features/PurchaseRequestEndpoints.cs` (order from requests), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/CatalogueOrderTests.cs`

**Interfaces:**
- Consumes: Task 1 `PackMath`; Task 2 rows.
- Produces:
  - `LineInput` adds `decimal? PackSize = null, string? SupplierPartNumber = null`. Null keeps the line's pack on edit, and is 1 on a new line.
  - `public sealed record FilledLine(Guid PartId, string UnitOfMeasure, decimal PackSize, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, string? SupplierPartNumber, int? LeadTimeDays, bool CataloguePrice)`.
  - `internal static Task<FilledLine> FillAsync(CmmsDbContext db, Part part, Guid supplierId, string orderCurrency, decimal pieces, decimal? pricePerPiece, CancellationToken ct)` works as follows:
    - **Active row:** pack = row (`UnitOfMeasure` = `PackName`), quantity = `PackMath.Packs(pieces, PackSize, MinPacks)`, supplier part number.
    - **Price:** `PackPrice` + row discount when `PackPrice != null && Currency == orderCurrency && pricePerPiece == null`. Otherwise `(pricePerPiece ?? part.LastPurchasePrice ?? part.AverageCost) × PackSize` with discount 0 and `CataloguePrice = false`.
    - **No row:** the part's unit, `PackSize` 1, quantity = pieces, price as today, `LeadTimeDays` = the part's.
  - `POST /api/purchase-orders/fill` (`purchasing.create`) takes `FillInput(Guid SupplierId, string? Currency, List<FillRequest> Lines)` with `FillRequest(Guid PartId, decimal Pieces)`, and returns `FilledLine[]`. A null `Currency` means the company's, else base.
  - `internal static DateOnly? ExpectedFrom(DateOnly orderDate, IEnumerable<int?> leadTimes)`: the order date + the longest lead time, null when none.
  - From-proposal and order-from-requests take request or proposal quantities as **pieces**. They build lines through `FillAsync`, using the typed price as `pricePerPiece`, and set `ExpectedDate` from `ExpectedFrom` when none is given.

- [ ] **Step 1: Write the failing tests** in `CatalogueOrderTests`. Setup: part BRG (unit EA), and supplier SKF (base currency) with row Box of 100, `PackSize` 100, `PackPrice` 45, 2% discount, 10 days lead time, min 1.

```csharp
[SkippableFact] public async Task Fill_turns_pieces_into_whole_packs_at_the_catalogue_price()
// POST /api/purchase-orders/fill {supplierId SKF, lines [{partId BRG, pieces 20}]} → unitOfMeasure "Box of 100", packSize 100, quantity 1, unitPrice 45, discountPercent 2, cataloguePrice true
[SkippableFact] public async Task Fill_keeps_the_pieces_when_the_pack_changes()
// pieces 250 → quantity 3 (300 pieces ≥ 250); row minPacks 5 → 5
[SkippableFact] public async Task Other_currency_takes_the_pack_at_todays_price()
// order currency USD (row EGP), part lastPurchasePrice 0.5 → packSize 100, unitPrice 50, discount 0, cataloguePrice false
[SkippableFact] public async Task Proposal_orders_come_in_whole_packs_with_the_expected_date_from_lead_time()
// POST /api/purchase-orders/from-proposal {partId BRG, storeId, quantity 20, supplierId SKF} → the draft's line: quantity 1, packSize 100, unitOfMeasure "Box of 100"; expectedDate = today + 10
[SkippableFact] public async Task Requested_pieces_become_whole_packs_on_the_order()
// approved request line 20 EA of BRG → POST /api/purchase-requests/order with supplier SKF → order line 1 Box of 100; GET request: line ordered 100 (pieces), status Ordered
[SkippableFact] public async Task Catalogue_change_leaves_existing_order_lines_alone()
// order line made at Box of 100 / 45 → PUT row packSize 50, price 30 → GET order: line still packSize 100, unitPrice 45; PUT the draft unchanged (lines without packSize) → still 100
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test CatalogueOrderTests`
Expected: FAIL.

- [ ] **Step 3: Implement** `FillAsync`, the fill endpoint, `ExpectedFrom`, `LineInput` fields and their apply (pack kept when null). Wire them into from-proposal and order-from-requests.

- [ ] **Step 4: Run the tests**

Run: `wt-test CatalogueOrderTests` then `wt-test Purchas`
Expected: PASS.

- [ ] **Step 5: Commit** `feat(catalogue): order lines filled from the catalogue in whole packs`

---

### Task 4: Receiving in packs and quantities in pieces

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/PurchasingEndpoints.cs` (`ReceiveCoreAsync`), `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (on order: parts list, part page, proposal), `server/FourC.Cmms.Api/Features/ReportEndpoints.cs` (on-order report), `server/FourC.Cmms.Api/Features/PurchaseRequestEndpoints.cs` (ordered/received shown), `server/FourC.Cmms.Api/Features/TechEndpoints.cs` (phone lines gain `PackSize`)
- Test: `server/FourC.Cmms.Tests/CatalogueReceiptTests.cs`

**Interfaces:**
- Consumes: Task 1 `PackMath.CostPerPiece`, `PriceForPack`; Task 3 orders in packs.
- Produces:
  - `ReceiveCoreAsync` for a part line:
    - stock and the issue to the work order get `received × PackSize`;
    - unit cost is `PackMath.CostPerPiece(NetUnitPrice, ExchangeRate, PackSize)`;
    - the order supplier's catalogue row gets `LastPaidPrice = PriceForPack(UnitPrice, line.PackSize, row.PackSize)`, plus `LastPaidDiscount = line.DiscountPercent`, `LastPaidCurrency = order currency` and `LastPaidOn = receipt date`.
  - Free-text lines stay unchanged.
  - "On order" = Σ `(Quantity − QuantityReceived) × PackSize` in every reader listed in Files.
  - Request detail `ordered` and `received` = order line quantity × `PackSize`.
  - Phone (`/api/tech/...` body) lines gain `PackSize`; outstanding stays in packs.

- [ ] **Step 1: Write the failing tests** in `CatalogueReceiptTests`. Setup: the approved order for 1 Box of 100 BRG at 45 with 2% discount, rate 1, store MAIN.

```csharp
[SkippableFact] public async Task Receiving_a_box_books_its_pieces_at_cost_per_piece()
// receive quantity 1 → part on hand +100; receipt transaction unitCost 0.441 (45×0.98÷100); part averageCost 0.441 (from 0 stock); lastPurchasePrice 0.441; catalogue row lastPaidPrice 45, lastPaidDiscount 2, lastPaidOn today
[SkippableFact] public async Task Half_a_box_received_books_half_its_pieces()
// receive 0.5 → +50 on hand; order PartiallyReceived; receive 0.5 → Received, +100 total
[SkippableFact] public async Task Foreign_currency_pack_cost_per_piece_uses_the_rate()
// order currency USD, rate 48.5, line 1 × Box of 10 at 45 → unitCost per piece 218.25
[SkippableFact] public async Task On_order_counts_pieces_everywhere()
// approved order 2 boxes of 100 not received → GET /api/parts (onOrder 200), /api/parts/{id} (onOrder 200), /api/inventory/proposal row onOrder 200
[SkippableFact] public async Task Request_shows_ordered_and_received_in_pieces()
// request 20 EA ordered as 1 box → GET request line ordered 100; after receipt received 100
[SkippableFact] public async Task Phone_receipt_of_a_pack_books_pieces()
// tech sync body lists the line with packSize 100 and outstanding 1; phone receive (the 0.11.0 phone receipt route) quantity 1 → on hand +100
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test CatalogueReceiptTests`
Expected: FAIL (100 booked as 1, on order 2).

- [ ] **Step 3: Implement** as above.

- [ ] **Step 4: Run the tests**

Run: `wt-test CatalogueReceiptTests` then `wt-test` (whole suite)
Expected: PASS; whole suite green.

- [ ] **Step 5: Commit** `feat(catalogue): receiving books pieces; on order and requests in pieces`

---

### Task 5: Re-order proposal suppliers and the import sheets

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (proposal supplier rule and catalogue options), `server/FourC.Cmms.Api/Features/Import/ImportSheets.cs` and `SheetImporters.cs` (new sheet; Parts sheet's preferred supplier calls `EnsureRowAsync`), `server/FourC.Cmms.LegacyImport/InventoryImport.cs` (rows for every `stock_code_supplier` pair)
- Test: `server/FourC.Cmms.Tests/CatalogueProposalImportTests.cs`

**Interfaces:**
- Consumes: Task 2 `EnsureRowAsync`.
- Produces:
  - Proposal rows gain `Catalogue: [{ supplierId, supplierName, packName, packSize, packPrice, currency, minPacks, isPreferred }]`, active rows of active Supplier-role companies only, preferred first.
  - The suggested supplier is chosen in this order: `PreferredSupplierId`, else the only catalogue supplier, else the supplier of the part's latest receipt.
  - Sheet `SupplierCatalogue` ("Supplier catalogue" / "كتالوج الموردين"), a list sheet keyed by part, following `AssetPartImporter`. Columns:
    - `partNumber`* and `supplierCode`*
    - `supplierPartNumber`, `packName`, `packSize`, `packPrice`, `currency`
    - `discountPercent`, `leadTimeDays`, `minPacks`, `notes`
  - The sheet takes part in the template and current-data export.
  - The 4CSQL importer creates rows (pack of 1, no price, currency = company else base) for every pair, without duplicates.

- [ ] **Step 1: Write the failing tests**:

```csharp
[SkippableFact] public async Task Proposal_suggests_the_preferred_else_only_else_last_supplier_with_catalogue_options()
// part A preferred SKF → supplier SKF; part B one row LOCAL, no preferred → LOCAL; part C no rows, last receipt from ACME → ACME; catalogue lists rows preferred first
[SkippableFact] public async Task Supplier_catalogue_sheet_imports_and_exports()
// ImportFiles.Book(new Sheet("SupplierCatalogue", headings…, ["BRG","SKF","6205-2RS","Box of 100",100,45,null,2,10,1,null])) → RunAsync → GET part suppliers has the row; GET /api/import/current then CheckAsync → SupplierCatalogue sheet 0 new, 0 changed
[SkippableFact] public async Task Parts_sheet_preferred_supplier_adds_a_catalogue_row()
// Parts sheet row with "Preferred supplier code" SKF → the part has a SKF row (pack 1)
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test CatalogueProposalImportTests`
Expected: FAIL.

- [ ] **Step 3: Implement** as above. The legacy importer change has no automated test, because it needs a restored 4CSQL database; record that in the ledger.

- [ ] **Step 4: Run the tests**

Run: `wt-test CatalogueProposalImportTests` then `wt-test Import`
Expected: PASS.

- [ ] **Step 5: Commit** `feat(catalogue): proposal suppliers from the catalogue and the supplier catalogue sheet`

---

### Task 6: Web — catalogue, orders in packs, receiving, proposal

**Files:**
- Create:
  - `web/src/pages/inventory/PartSuppliers.tsx` (Suppliers tab)
  - `web/src/pages/purchasing/packs.ts`, `web/src/pages/purchasing/packs.test.ts`
- Modify:
  - `web/src/pages/inventory/PartDetailPage.tsx` (tab)
  - `web/src/pages/purchasing/CompaniesTab.tsx` (parts supplied, read-only)
  - `web/src/pages/purchasing/PoFormPage.tsx` (pack column with pieces, supplier part number, fill on part add and supplier change via `/purchase-orders/fill`, expected date from lead time when empty)
  - `web/src/pages/purchasing/PoDetailPage.tsx` (lines and Receive dialog show pieces)
  - `web/src/pages/purchasing/PurchasingPage.tsx` (proposal: supplier options from `catalogue`, pack and value)
  - `web/src/tech/StockPages.tsx` (receive shows pieces)
  - `web/src/api/types.ts`, `web/src/tech/types.ts`, `web/src/locales/en.json`, `web/src/locales/ar.json`

**Interfaces:**
- Consumes: Tasks 2–5 routes.
- Produces `packs.ts`:
  - `packsFor(pieces: number, packSize: number, minPacks: number): number`, the same rule as `PackMath.Packs`;
  - `piecesOf(quantity: number, packSize: number): number`;
  - `packText(quantity: number, unit: string, packSize: number, partUnit: string): string`, e.g. "1 Box of 100 = 100 EA". The ` = …` part is left out when `packSize` is 1.

- [ ] **Step 1: Write the failing test** `packs.test.ts`:

```ts
it('rounds up to whole packs and the minimum', () => { expect(packsFor(20, 100, 1)).toBe(1); expect(packsFor(250, 100, 1)).toBe(3); expect(packsFor(20, 100, 2)).toBe(2); expect(packsFor(5, 2.5, 1)).toBe(2); });
it('counts pieces', () => expect(piecesOf(1.5, 100)).toBe(150));
it('writes the pack with its pieces', () => { expect(packText(1, 'Box of 100', 100, 'EA')).toBe('1 Box of 100 = 100 EA'); expect(packText(3, 'EA', 1, 'EA')).toBe('3 EA'); });
```

- [ ] **Step 2: Run it to see it fail**

Run: `wt-vitest`
Expected: FAIL (module missing).

- [ ] **Step 3: Implement** the helpers and screens. Arabic for every text.

- [ ] **Step 4: Run tests and build**

Run: `wt-vitest` then `wt-web`
Expected: all pass; build succeeds.

- [ ] **Step 5: Commit** `feat(web): supplier catalogue, orders in packs and receiving in pieces`

---

### Task 7: Docs and full verification

**Files:**
- Create: `docs/SUPPLIER_CATALOGUE.md` (catalogue rows, packs and pieces, filling orders, receiving and costs, prices and the last price paid, currencies, proposal and requests, the import sheet)
- Modify: `README.md` (feature line)

- [ ] **Step 1: Write the docs.**

- [ ] **Step 2: Run everything**

Run: `wt-test`, `wt-vitest`, `wt-web`
Expected: server suite green (1120 + new tests, 2 skipped as before), web tests green, build succeeds.

- [ ] **Step 3: Commit** `docs(0.18.0): supplier catalogue and purchase units`
