# Serial-numbered parts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CMX 0.24.0 — serial-tracked parts whose units are assets: serials at receipt, fit/remove on work orders (web and phone), unit history and hours, life limits, unit actions, outside repair.

**Architecture:** A unit is an `Asset` with `PartId` (and `StoreId` while in store). One server service (`SerialUnits`) performs every unit move, writing a `UnitMovement` and the matching stock ledger entry with a new `StockPost.UnitMove` flag; the ledger refuses any other post on a serial part, so all seven stock entry points stay consistent without touching each. Meter propagation (0.12.0) carries running hours while a unit is fitted.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit SkippableFact + ApiFixture; React 19 + Mantine 8 + TanStack Query + i18next, Vitest; the offline phone view in `web/src/tech`.

**Spec:** `docs/superpowers/specs/2026-11-24-serial-units-0.24.0-design.md`

## Global Constraints

- Every new text in en + ar (web via loc-add.py; server via ar-add.py; Arabic plurals complete).
- No new permissions (verbatim): *Execute work orders* + *Issue and move stock* to fit and remove; *Issue and move stock* for unit actions; *Manage inventory* to mark a part serial-tracked; *Create purchase orders* for repair lines; *Receive purchase orders* to receive them; *Edit assets* to edit a unit's asset record.
- A serial part's quantity in a store always equals its units in that store; every unit move writes its stock entry and its movement in one save.
- Removing a unit back to a store posts a return that names no work order.
- Units not fitted are left out of the asset list, tree and dashboard counts unless the "Show spare units" filter is on or a search text is given.
- Nothing changes on upgrade: no part becomes serial-tracked by itself; existing endpoints keep their answers for non-serial parts.
- Exact refusal messages are listed in Task 1 and the tasks that own them; use them verbatim.
- Gate with CI's exact build (`wt-ci`), plus `wt-vitest` and `wt-web`. Commands run with `WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/serial-units-0.24.0`.

## Review Focus

1. **Two people act on one unit at once** (fit on the web while the phone's queued fit arrives): the second is refused ("This unit isn't in a store."), never two parents. Test: `A_unit_cannot_be_fitted_twice` (Task 3) and `Phone_fit_of_a_unit_fitted_meanwhile_is_refused` (Task 7).
2. **A unit removed on a work order of another site's asset or a closed work order**: removal is refused on a closed work order like other job changes; a unit at another site can't be fitted. Test: `Fit_and_remove_follow_work_order_rules` (Task 3).
3. **Pack sizes on purchase orders** (1 box of 2 pumps): the receipt asks for 2 serials. Test: `Po_receipt_asks_one_serial_per_piece_in_packs` (Task 2).
4. **The parent asset is disposed of or deleted while a unit is fitted**: the unit can still be removed outside a work order; deleting an asset with fitted units is refused with "Remove its fitted units first." Test: `Asset_with_fitted_units_cannot_be_deleted` (Task 4).
5. **Arabic/English serial input with spaces and blank lines**: serials are trimmed, blank lines ignored, case kept, duplicates refused. Test: Vitest `parseSerials trims and drops blank lines` (Task 8) and `Serials_are_trimmed_and_checked` (Task 2).

---

### Task 1: Model, migration and the ledger guard

**Files:**
- Create: `server/FourC.Cmms.Core/Inventory/SerialUnits.cs` (`UnitMovement`, `UnitMovementKind`, `UnitOutcome`, `UnitState`, `UnitRules`), migration `SerialUnits` (via `wt-ef SerialUnits`)
- Modify: `server/FourC.Cmms.Core/Inventory/Inventory.cs` (`Part.IsSerialTracked`, `Part.UnitLifeMonths`), `server/FourC.Cmms.Core/Assets/Assets.cs` (`Asset.PartId`, `Asset.StoreId`, `Asset.EndOfLifeOn`), `server/FourC.Cmms.Core/Purchasing/Purchasing.cs` (`PurchaseOrderLine.UnitId`), `server/FourC.Cmms.Core/Inventory/StockLedger.cs` (`StockPost.UnitMove`, guard), `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`, `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/SerialGuardTests.cs`, `server/FourC.Cmms.Tests/UnitRulesTests.cs` (plain Facts)

**Interfaces:**
- Produces:
  - `public enum UnitMovementKind { Received = 1, Registered = 2, Fitted = 3, Removed = 4, SentForRepair = 5, BackFromRepair = 6, Scrapped = 7, Transferred = 8 }`; `public enum UnitOutcome { Store = 1, Repair = 2, Scrap = 3 }`; `public enum UnitState { InStore = 1, Fitted = 2, AtRepair = 3, Scrapped = 4 }`.
  - `public class UnitMovement : Entity, ITenantOwned { Guid TenantId; Guid UnitId; UnitMovementKind Kind; DateTimeOffset At; Guid? StoreId; Guid? AssetId; Guid? WorkOrderId; Guid? PurchaseOrderId; Guid? PurchaseOrderLineId; UnitOutcome? Outcome; string? Note (≤500); decimal? Cost; Guid? ByUserId; string? ByName (≤200) }` with index (UnitId, At), and `DbSet<UnitMovement> UnitMovements`.
  - `Part.IsSerialTracked : bool`, `Part.UnitLifeMonths : int?`; `Asset.PartId : Guid?` (FK Part, Restrict), `Asset.StoreId : Guid?` (FK Store, Restrict), `Asset.EndOfLifeOn : DateOnly?`; filtered unique index (TenantId, PartId, SerialNumber) where `PartId IS NOT NULL AND IsDeleted = 0`; `PurchaseOrderLine.UnitId : Guid?` (FK Asset, Restrict).
  - `public static class UnitRules { UnitState StateOf(Asset unit); bool IsHoursUnit(string? meterUnit); DateOnly? EndOfLife(DateOnly from, int? months); IReadOnlyList<string> CleanSerials(IEnumerable<string?> raw); }` — `StateOf`: Disposed → Scrapped; UnderRepair → AtRepair; ParentAssetId set → Fitted; else InStore. `IsHoursUnit`: trimmed, case-insensitive in `h, hr, hrs, hour, hours, ساعة, ساعات`. `CleanSerials`: trim, drop empty, keep case and order.
  - `StockPost` gains `bool UnitMove = false` (last parameter). `StockLedger` refuses posts on serial parts unless `UnitMove` or `LegacyRef` is set, with: Issue/Return → "This part is serial-tracked: fit or remove a unit on the work order."; Adjustment → "This part is serial-tracked: scrap a unit or register a found one on the part's units."; Receipt → "Enter a serial number for each piece received."; TransferOut/TransferIn (and `TransferAsync`) → "This part is serial-tracked: transfer units by choosing them on the part's units."

- [ ] **Step 1: Write the failing tests.** `UnitRulesTests` (plain): `StateOf` for the four states; `IsHoursUnit("Hours")`, `("ساعة")` true, `("km")` false; `EndOfLife(2026-11-24, 12)` = 2027-11-24, `null` months → null; `CleanSerials([" SN1 ", "", "sn2", null])` = `["SN1", "sn2"]`. `SerialGuardTests` (integration, `PurchaseRequestTests.OrgAsync` org): mark the org's part serial-tracked through the DbContext (the endpoint comes in Task 2), then `POST /api/inventory/issue`, `/return`, `/adjust`, `/transfer` and `/receive` on it each answer 400 with the message above; the same calls on a normal part still succeed.
- [ ] **Step 2: Run** `wt-test "SerialGuardTests|UnitRulesTests"` — Expected: fail (types missing).
- [ ] **Step 3: Implement** the model, configuration, migration (check it adds only the listed columns, the table and indexes), the guard in `ValidateAsync` (and the transfer path), and the messages in ar.json.
- [ ] **Step 4: Run** `wt-test "SerialGuardTests|UnitRulesTests|Inventory|Stock"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): serial-unit model and the stock ledger guard`

### Task 2: Serial tracking on parts, registering units and receipts

**Files:**
- Create: `server/FourC.Cmms.Api/Features/SerialUnitsService.cs` (static `SerialUnits`), `server/FourC.Cmms.Api/Features/UnitEndpoints.cs`
- Modify: `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (`ReceiveInput.Serials`, `PartInput` create-time `IsSerialTracked`/`UnitLifeMonths`, part detail/list fields), `server/FourC.Cmms.Api/Features/PurchasingEndpoints.cs` (`ReceiveLine.Serials`, receipt of serial lines), `Program.cs`, ar.json
- Test: `server/FourC.Cmms.Tests/SerialReceiptTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces (`SerialUnits`, all write movements + ledger entries with `UnitMove: true`; the caller saves, inside its transaction):
  - `Task<List<Asset>> ReceiveAsync(CmmsDbContext db, ITenantContext ctx, Part part, Store store, IReadOnlyList<string> serials, DateOnly date, decimal unitCost, StockPost template, CancellationToken ct)` — one receipt entry of `serials.Count` (with the template's supplier/PO/line/reference), one asset per serial (number from `AssetEndpoints.NextAssetNumberAsync` at the store's site, name = part name, manufacturer/model from the part, `PurchasePrice = unitCost`, `Status = InStorage`, `StoreId`, `EndOfLifeOn = UnitRules.EndOfLife(date, part.UnitLifeMonths)`), movement `Received`.
  - `Task RegisterInStoreAsync(..., Part part, Store store, IReadOnlyList<string> serials, bool moveStock, string? note, CancellationToken ct)` — `moveStock: false` for switching on (no ledger), `true` for a found unit (adjustment +1 each); movement `Registered`.
  - `Task CheckNewSerialsAsync(CmmsDbContext db, Part part, IReadOnlyList<string> serials, int expected, CancellationToken ct)` — messages: "Enter {expected} serial numbers, one for each piece received." / "Serial numbers must be different." / "Serial number {s} is already used by a unit of this part.".
  - `POST /api/parts/{id}/serial-tracking` (inventory.manage) body `SerialTrackingInput(bool On, int? UnitLifeMonths, List<StoreSerials>? Stores)` where `StoreSerials(Guid StoreId, List<string>? Serials)`; turning on registers units matching each store's on-hand stock exactly ("Enter one serial number for each piece in stock: {store code} has {n}."), turning off is refused while units aren't all scrapped ("This part still has units that aren't scrapped."); `UnitLifeMonths` saved either way (1–600, "Enter the life in months, from 1 to 600.").
  - `ReceiveInput` gains `List<string>? Serials = null`; `ReceiveLine` gains `List<string>? Serials = null`. Serial parts need whole pieces ("Serial parts are received in whole pieces.") and pieces = quantity × pack size serials. A PO line for a serial part ordered for a work order is received into the store (no issue to the work order); the work order is told as today.
  - Part detail and list rows add `isSerialTracked`, `unitLifeMonths`.

- [ ] **Step 1: Write the failing tests** in `SerialReceiptTests.cs`:
  - `Switching_on_needs_serials_matching_the_stock`: a part with 2 in store MAIN; turn on with 1 serial → 400 with "MAIN has 2"; with `["P-1","P-2"]` → 200, two units InStorage in MAIN, stock still 2, no new ledger entry; turning off → 400 "This part still has units that aren't scrapped."
  - `Manual_receipt_creates_one_unit_per_serial`: receive 3 with serials → 3 assets with `partId`, the serials, `status = InStorage`, `storeId`, purchase price = unit cost, end of life = date + `unitLifeMonths`; stock +3; one receipt entry of 3; three `Received` movements.
  - `Serials_are_trimmed_and_checked`: `[" A ", "A"]` → "Serial numbers must be different."; 2 serials for quantity 3 → "Enter 3 serial numbers, one for each piece received."; an existing serial → "Serial number A is already used by a unit of this part."; quantity 1.5 → "Serial parts are received in whole pieces."
  - `Po_receipt_asks_one_serial_per_piece_in_packs`: a PO line of 1 pack of 2 for a serial part; receiving with 1 serial → 400 "Enter 2 serial numbers…"; with 2 → 2 units; a line ordered for a work order leaves both units in the store and issues nothing to the work order.
  - `New_parts_can_start_serial_tracked`: POST a part with `isSerialTracked: true, unitLifeMonths: 24` → detail shows both.
- [ ] **Step 2: Run** `wt-test "SerialReceiptTests"` — Expected: fail.
- [ ] **Step 3: Implement.** Messages to ar.json.
- [ ] **Step 4: Run** `wt-test "SerialReceiptTests|SerialGuardTests|Purchas|Inventory"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): serial tracking on parts and serial numbers at receipt`

### Task 3: Fit and remove on a work order

**Files:**
- Modify: `SerialUnitsService.cs`, `UnitEndpoints.cs`, ar.json
- Test: `server/FourC.Cmms.Tests/UnitFitTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - `Task FitAsync(CmmsDbContext db, ITenantContext ctx, Asset unit, Asset target, WorkOrder? wo, bool countHours, Guid? hoursMeterId, DateTimeOffset at, CancellationToken ct)` — refuses: not in store → "This unit isn't in a store."; store's site ≠ target's site → "Move the unit to a store at that site first."; target is the unit or one of its own fitted units → "A unit can't be fitted to itself or to one of its own units."; posts an issue of 1 to the work order from the unit's store (`UnitMove`), sets `ParentAssetId`, `SiteId`, `LocationId` = target's, `StoreId = null`, `Status = Active`; movement `Fitted` with the work order. Hours: when `countHours`, the meter = `hoursMeterId` (must belong to the target, "Choose a meter of that asset.") or the target's first active meter with `UnitRules.IsHoursUnit`; when one exists, the unit's meter with an hours unit (or a new meter `RH` "Running hours", unit `h`) gets `SourceMeterId` = it.
  - `Task RemoveAsync(CmmsDbContext db, ITenantContext ctx, Asset unit, WorkOrder? wo, UnitOutcome outcome, Guid? storeId, string? note, DateTimeOffset at, CancellationToken ct)` — refuses: not fitted → "This unit isn't fitted."; Store without a store → "Choose the store it goes back to."; the unit's hours meters stop following (`SourceMeterId = null`); Store → return of 1 with **no work order** (`UnitMove`), `StoreId`, `Status = InStorage`, site = store's, `ParentAssetId = null`, `LocationId = null`; Repair → `Status = UnderRepair`, no store, no parent; Scrap → `Status = Disposed`, `DisposedOn` = local date; movement `Removed` with the work order, the asset it came off, the outcome and the note.
  - `POST /api/work-orders/{id}/units/fit` body `FitInput(Guid UnitId, Guid AssetId, bool CountHours = true, Guid? HoursMeterId = null)` — needs workorders.execute and inventory.transact; the work order not closed (as other job changes); `AssetId` must be the work order's asset or one of its round's assets ("Choose an asset of this work order.").
  - `POST /api/units/{id}/remove` body `RemoveInput(Guid? WorkOrderId, string? Outcome, Guid? StoreId, string? Note)` — `Outcome` `Store|Repair|Scrap` ("Choose what happens to the unit."); with a work order: the same work order rules, and the unit must be fitted to one of its assets ("This unit isn't fitted to this work order's asset."); without: inventory.transact.
  - `GET /api/work-orders/{id}/units` → units fitted to its asset(s) `[{ id, assetNumber, serialNumber, partId, partNumber, partName, fittedToId, since }]`.

- [ ] **Step 1: Write the failing tests** in `UnitFitTests.cs` (receive 2 units through Task 2's endpoint; an asset "Chiller 2" with a meter in hours at the store's site; a work order on Chiller 2):
  - `Fitting_moves_the_unit_onto_the_asset`: fit → unit `parentAssetId` = Chiller 2, status Active, no store; stock −1; the work order's parts show the part issued; movement `Fitted`; the unit has a meter `RH` following Chiller 2's hours meter; a reading on Chiller 2 raising it by 10 raises the unit's meter by 10.
  - `A_unit_cannot_be_fitted_twice`: fitting the same unit again (same or another work order) → 400 "This unit isn't in a store."
  - `Fit_and_remove_follow_work_order_rules`: a unit in a store at another site → "Move the unit to a store at that site first."; an asset not on the work order → "Choose an asset of this work order."; a closed work order → refused like other changes; a Technician without inventory.transact → 403.
  - `Removing_back_to_store_credits_no_work_order`: fit on WO-1, remove on WO-2 to store MAIN → stock +1; the return names no work order; WO-2's parts show nothing; WO-1 still shows the issue; the unit's meter no longer follows; movement `Removed` with WO-2 and outcome Store.
  - `Remove_to_repair_and_scrap`: repair → UnderRepair, stock unchanged; scrap (another unit) → Disposed with `disposedOn`; "Choose the store it goes back to." for Store without a store; removing an unfitted unit → "This unit isn't fitted."
- [ ] **Step 2: Run** `wt-test "UnitFitTests"` — Expected: fail.
- [ ] **Step 3: Implement**, reusing `PmEndpoints.PropagateAsync` behaviour through `SourceMeterId` (no change to propagation itself).
- [ ] **Step 4: Run** `wt-test "UnitFitTests|SerialReceiptTests|WorkOrder|Meter"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): fit and remove units on work orders`

### Task 4: Unit actions, lists and history

**Files:**
- Modify: `SerialUnitsService.cs`, `UnitEndpoints.cs`, `server/FourC.Cmms.Api/Features/AssetEndpoints.cs` (unit lock, delete guard, make-unit, fitted units), ar.json
- Test: `server/FourC.Cmms.Tests/UnitActionTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `POST /api/units/{id}/transfer` `{ storeId, note? }` (in store only; stock out of one store, into the other; site follows the new store; movement `Transferred`).
  - `POST /api/units/{id}/send-for-repair` `{ note? }` (in store → adjustment −1, UnderRepair; `SentForRepair`).
  - `POST /api/units/{id}/back-from-repair` `{ storeId, cost?, note? }` (UnderRepair → adjustment +1 into the store, InStorage; `BackFromRepair` with the cost; "This unit isn't under repair.").
  - `POST /api/units/{id}/scrap` `{ note }` (in store → adjustment −1; under repair → no stock move; fitted → "Remove the unit first."; `Scrapped`).
  - `POST /api/parts/{id}/units/found` `{ storeId, serial, note? }` (serial part; adjustment +1; `Registered`).
  - `POST /api/parts/{id}/units/fitted` `{ assetId, serial, note? }` (creates a fitted unit, no stock move; `Registered` with the asset).
  - `POST /api/assets/{id}/make-unit` `{ partId }` (assets.edit; the asset must have a parent ("Only an asset fitted to another asset can be made a unit."), a serial ("Give the asset a serial number first."), not already a unit ("This asset is already a unit."), the part serial-tracked ("Choose a serial-tracked part."), the serial new for the part; sets `PartId`, `Registered`).
  - All actions need inventory.transact except make-unit.
  - `GET /api/units?partId=&state=&storeId=&siteId=&endingWithinDays=&search=` → `[{ id, assetNumber, serialNumber, partId, partNumber, partName, state, storeId, storeName, fittedToId, fittedToNumber, fittedToName, siteId, siteName, endOfLifeOn, repairCount, repairCost }]` (`repairCount` = `BackFromRepair` movements, `repairCost` = their cost sum).
  - `GET /api/units/{id}` → the row plus `movements: [{ kind, at, storeName, assetId, assetNumber, workOrderId, workOrderNumber, purchaseOrderId, purchaseOrderNumber, outcome, note, cost, byName }]` newest first.
  - `GET /api/assets/{id}/fitted-units` → `{ now: [{ unitId, assetNumber, serialNumber, partName, since }], before: [{ unitId, assetNumber, serialNumber, partName, from, to, outcome }] }`.
  - Asset PUT on a unit refuses changing status, parent, site, location or `partId` ("A unit's status and place change through fit, remove and the unit actions."); DELETE of a unit → "A unit can't be deleted; scrap it instead."; DELETE of an asset with fitted units → "Remove its fitted units first."
  - `AssetInput` gains `DateOnly? EndOfLifeOn = null, bool? ManagesEndOfLife = null` (older clients never clear it); asset detail adds `partId`, `partNumber`, `partName`, `storeId`, `storeName`, `endOfLifeOn`, `unitState`. Serial number and end of life stay editable on a unit's asset form.

- [ ] **Step 1: Write the failing tests** in `UnitActionTests.cs`:
  - `Stock_follows_every_unit_action`: receive 3 in MAIN; transfer 1 to STORE2; send 1 for repair; back from repair into MAIN with cost 120; register a found unit in MAIN; scrap 1 in MAIN. After each, each store's stock equals its units in store (query `/api/units?storeId=`), and the unit's movements list each step in order.
  - `Units_under_repair_and_fitted_units_have_a_way_out`: scrap a unit under repair (no stock move); scrap a fitted unit → "Remove the unit first."; remove a fitted unit with no work order to store → stock +1, return with no work order.
  - `Units_already_on_machines_can_be_registered`: `units/fitted` creates a fitted child of the asset with no stock change; `make-unit` on an existing child asset with serial "X9" makes it a unit; the four make-unit refusals.
  - `Asset_with_fitted_units_cannot_be_deleted` and `A_unit_cannot_be_deleted` and `A_units_place_is_locked_on_the_asset_form`.
  - `Lists_and_history`: `/api/units?partId=` shows states and repair count/cost; `/api/units/{id}` movements newest first; `/api/assets/{id}/fitted-units` shows the unit now, and after removal under `before` with from/to/outcome.
- [ ] **Step 2: Run** `wt-test "UnitActionTests"` — Expected: fail.
- [ ] **Step 3: Implement.** Messages to ar.json.
- [ ] **Step 4: Run** `wt-test "UnitActionTests|UnitFitTests|SerialReceiptTests|Asset"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): unit actions, lists and history`

### Task 5: Outside repair on purchase orders

**Files:**
- Modify: `PurchasingEndpoints.cs` (line input `UnitId`, validation, receipt of repair lines), `SerialUnitsService.cs`, ar.json
- Test: `server/FourC.Cmms.Tests/UnitRepairTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4 (`BackFromRepair` path).
- Produces: PO line input gains `Guid? UnitId = null` (older clients send nothing and keep the line's unit). With a unit: the unit must be under repair ("Only units under repair can go on a repair line."), on no other open repair line ("This unit is already on an open repair line ({PO number})."); the line has no part, quantity 1, description defaulting to "Repair of {part name} {serial}". Receiving the line needs a store ("Choose the store the unit comes back to."); the unit comes back (adjustment +1, InStorage), `BackFromRepair` with the line's net cost × exchange rate and the PO and line; when the line names a work order, the work order cost is added as today. PO detail lines add `unitId`, `unitSerial`, `unitAssetNumber`.

- [ ] **Step 1: Write the failing tests** in `UnitRepairTests.cs`: `Repair_line_needs_a_unit_under_repair` (a unit in store → refused; one under repair → saved with the default description; a second open line for it → refused with the first PO's number); `Receiving_a_repair_line_brings_the_unit_back_with_its_cost` (receive into MAIN → unit InStorage in MAIN, stock +1, `repairCount` 1, `repairCost` = line net × rate; with a work order on the line, the work order's costs include it); `Cancelling_leaves_the_unit_under_repair`.
- [ ] **Step 2: Run** `wt-test "UnitRepairTests"` — Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "UnitRepairTests|Purchas|UnitActionTests"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): outside repair on purchase order lines`

### Task 6: Visibility, alerts, counts, import and phone receiving

**Files:**
- Modify: `AssetEndpoints.cs` (list filter `spareUnits`), `AdminEndpoints.cs` (`/dashboard` totals), `AlertEndpoints.cs` (`UnitsEndingLife`), `StockCountEndpoints.cs` (leave serial parts out, note), the one-step stock take in `InventoryEndpoints.cs`, the Import feature's Parts and Stock sheets (find them with grep for the Parts sheet's columns), `PurchasingEndpoints.ReceiveCoreAsync` / `TechActions.cs` (`po.receive` on serial lines), ar.json
- Test: `server/FourC.Cmms.Tests/UnitVisibilityTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/assets` leaves out assets with `PartId != null && ParentAssetId == null` unless `spareUnits=true` or `search` is given; `/api/dashboard` totals and status/category/site breakdowns leave them out.
  - `/api/alerts` adds `UnitsEndingLife` (inventory.view): units not scrapped with `EndOfLifeOn` ≤ today + 30 days.
  - New stock counts leave out serial parts and carry `serialPartsLeftOut: number`; the one-step stock take refuses serial parts with the adjustment message.
  - Parts sheet: columns "Serial tracked" (yes/no) and "Unit life (months)"; turning it on for a part with stock and no units → row error "Register its units on the part page first."; Stock sheet: an opening quantity for a serial part → "Serial parts' stock comes from their units: register them on the part page."
  - Phone `po.receive` (and a web receipt without serials) on a serial line → "Receive serial parts on the web, where their serial numbers are entered."

- [ ] **Step 1: Write the failing tests** in `UnitVisibilityTests.cs`: `Spare_units_are_left_out_by_default` (asset list total and dashboard totals unchanged by receiving units; `spareUnits=true` and `search=<serial>` find them; a fitted unit shows as a child); `End_of_life_alert_counts_units_within_30_days`; `Counts_leave_serial_parts_out`; `Import_turns_serial_tracking_on_only_without_stock` and `Import_refuses_opening_stock_for_serial_parts`; `Phone_cannot_receive_serial_parts` (a `po.receive` phone change on a serial line is rejected with the message).
- [ ] **Step 2: Run** `wt-test "UnitVisibilityTests"` — Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "UnitVisibilityTests|Dashboard|Alert|StockCount|Import|Tech"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): spare units out of lists; end-of-life alert; counts, import and phone receiving rules`

### Task 7: Phone — fit and remove (server)

**Files:**
- Modify: `TechEndpoints.cs` (bundle `jobs[].units`, stock `units`, `parts[].isSerialTracked`), `TechActions.cs` (`unit.fit`, `unit.remove`), ar.json
- Test: `server/FourC.Cmms.Tests/PhoneUnitTests.cs`

**Interfaces:**
- Consumes: Tasks 3–4 (`FitAsync`, `RemoveAsync`).
- Produces: bundle jobs carry `units: [{ id, assetNumber, serialNumber, partId, partName, fittedToId }]` (units fitted to the job's asset(s)); `/api/tech/stock` carries `units: [{ id, assetNumber, serialNumber, partId, storeId }]` (in store at my sites, serial parts only, newest 500) and `parts[].isSerialTracked`. Actions: `unit.fit` payload `{ unitId, assetId }` and `unit.remove` payload `{ unitId, outcome, note? }` (Store goes to the job's site's default store; "This job's site has no default store." when none); both need workorders.execute and inventory.transact and the job not closed; refusals become rejected changes with the service's message.

- [ ] **Step 1: Write the failing tests** in `PhoneUnitTests.cs` (the tech changes endpoint used by existing phone tests — find it with grep for `"part.use"` in the tests): `Phone_fits_and_removes_units` (fit → unit fitted, stock −1; remove to store → back in the default store); `Phone_fit_of_a_unit_fitted_meanwhile_is_refused` (fit on the web first, then the phone change → rejected with "This unit isn't in a store."); `Bundle_carries_units` (job units and stock units with `isSerialTracked`).
- [ ] **Step 2: Run** `wt-test "PhoneUnitTests"` — Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `wt-test "PhoneUnitTests|Tech|Phone"` — Expected: pass.
- [ ] **Step 5: Commit** `feat(units): fit and remove units from the phone (server)`

### Task 8: Web — parts, receipts and the units list

**Files:**
- Create: `web/src/pages/inventory/PartUnits.tsx` (Units tab, serial-tracking dialog, unit action dialogs), `web/src/pages/inventory/units.ts` + `units.test.ts`
- Modify: `web/src/pages/inventory/PartDetailPage.tsx` (Units tab when serial-tracked; "Serial tracking" action for inventory.manage), `web/src/pages/inventory/PartFormPage.tsx` (create-time serial tracking and unit life), the manual receipt form (`web/src/components/StockTxModal.tsx`) and the PO receive form (`web/src/pages/purchasing/PoDetailPage.tsx`) — a serials text area for serial parts, `web/src/api/types.ts`, locales

**Interfaces:**
- Consumes: Tasks 2–5 endpoints.
- Produces:
  - `export function parseSerials(text: string): string[]` (split on new lines and commas, trim, drop empty, keep case and order).
  - `export function serialProblem(serials: string[], needed: number): 'count' | 'duplicate' | null`.
  - `export type UnitState = 'InStore' | 'Fitted' | 'AtRepair' | 'Scrapped'`; `export const UNIT_STATE_COLOR: Record<UnitState, string>`.
  - `export type UnitRow` (the `/api/units` row) in `api/types.ts`.

- [ ] **Step 1: Write the failing tests** in `units.test.ts`: `parseSerials trims and drops blank lines` (`" A1 \n\n a2, A3 "` → `['A1','a2','A3']`); `serialProblem` (`['A','A']`, 2 → 'duplicate'; `['A']`, 2 → 'count'; `['A','B']`, 2 → null).
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement** the Units tab (list with state badges, store/fitted-to links, end of life, repairs, filters, Print labels for selected units via `openLabels('asset', ids)`; actions per state: transfer, send for repair, scrap, back from repair, remove (fitted, outside a work order), register found / register fitted), the serial-tracking dialog (on: a serials box per store with stock; off), and the serials box on both receipt forms (count shown as "n of m"), all with en/ar labels.
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass.
- [ ] **Step 5: Commit** `feat(web): serial tracking, serials at receipt and the units list`

### Task 9: Web — work orders, assets, alerts and repair lines

**Files:**
- Create: `web/src/pages/work/WorkOrderUnits.tsx`, `web/src/pages/assets/AssetUnits.tsx` (Unit card, Fitted units card, Make this asset a unit)
- Modify: `web/src/pages/work/WorkOrderDetailPage.tsx`, `web/src/pages/assets/AssetDetailPage.tsx`, `web/src/pages/assets/AssetsPage.tsx` ("Show spare units" filter), `web/src/components/AlertBell.tsx` (units ending life), `web/src/pages/purchasing/PoFormPage.tsx` (a repair line picking a unit under repair), `web/src/pages/inventory/units.ts` (+ test), locales

**Interfaces:**
- Consumes: Tasks 3–6 endpoints; Task 8 helpers.
- Produces: `export function defaultHoursMeter(meters: { id: string; unit: string; isActive: boolean }[]): string | null` (first active meter whose unit is an hours unit: `h, hr, hrs, hour, hours, ساعة, ساعات`, case-insensitive).

- [ ] **Step 1: Write the failing test** in `units.test.ts`: `defaultHoursMeter picks the first active hours meter` (`[{km}, {Hours inactive}, {h}]` → the `h` one; none → null).
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement** the work order Units panel (shown with an asset or round assets; fitted units with Remove: outcome, store, note; Fit a unit: part → units in store at the site → asset → "count running hours from" meter, default `defaultHoursMeter`), the asset page cards (Unit card with part, serial, state, store or fitted-to, end of life, repairs and the movement history; Fitted units now and before; "Make this asset a unit" for a child asset with a serial), the asset list filter, the alert bell entry linking to `/inventory/units?ending=30` (a simple page listing `/api/units?endingWithinDays=30`, or the part list filtered — builder's choice), and the PO form's repair line (choose a unit under repair).
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass.
- [ ] **Step 5: Commit** `feat(web): units on work orders and assets; spare units filter; end-of-life alert; repair lines`

### Task 10: Phone — fit and remove (web)

**Files:**
- Modify: `web/src/tech/types.ts`, `web/src/tech/JobPage.tsx` or `JobExtras.tsx` (a Units section), `web/src/tech/overlay.ts` (+ `overlay.test.ts`), `web/src/tech/stock.ts` (+ `stock.test.ts`: serial parts can't be marked used by quantity), `web/src/tech/changeLabel.ts`, locales

**Interfaces:**
- Consumes: Task 7 bundle and actions.
- Produces: overlay handling of `unit.fit` (the unit leaves `stock.units`, joins the job's `units`) and `unit.remove` (leaves the job's `units`; back in `stock.units` at the job's default store when the outcome is Store); `canUseByQuantity(part: StockPart): boolean` (false for serial parts).

- [ ] **Step 1: Write the failing tests:** `overlay.test.ts` — `a queued fit moves the unit onto the job` and `a queued removal to store puts it back in stock`; `stock.test.ts` — `serial parts can't be used by quantity`.
- [ ] **Step 2: Run** `wt-vitest` — Expected: fail.
- [ ] **Step 3: Implement** the job's Units section (fitted units with Remove → outcome and note; Fit a unit → scan the unit's label (it is an asset label: the scanned asset id must be a unit in `stock.units` of a part for this job's site) or pick from the units in store at the job's site, then the asset when the job has round assets), hiding "used" quantity controls for serial parts with a hint "Fit a unit instead.", and the change labels.
- [ ] **Step 4: Run** `wt-vitest`, `wt-web` — Expected: pass.
- [ ] **Step 5: Commit** `feat(tech): fit and remove units on the phone`

### Task 11: Guide and full verification

**Files:**
- Create: `docs/SERIAL_PARTS.md` (style of `docs/PRINT_LAYOUTS.md`)

- [ ] **Step 1: Write the guide.**
- [ ] **Step 2: Run** `wt-ci`, `wt-vitest`, `wt-web` — Expected: all pass.
- [ ] **Step 3: Commit** `docs: serial-numbered parts guide`
