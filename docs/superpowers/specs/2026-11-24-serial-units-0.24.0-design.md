# Serial-numbered parts (0.24.0)

Roadmap item from the DimoMaint comparison: units with serial numbers, fit and remove on work orders, unit history and hours,
life limits, work on a unit, and outside repair. Choice picked in the conversation: **A — a unit is an asset linked to its part.**

## Intent

Pumps, motors, gearboxes and other repairable spares are worth tracking one by one: which unit is where, how long it ran, how
often it came back from repair and what that cost. Today CMX counts a part's stock as a quantity only, so a pump taken off a
machine, repaired and refitted elsewhere leaves no trace. DimoMaint has "traceable parts"; CMX should cover the same ground,
avoiding DimoMaint's weak spots (units created after the receipt and only up to the free stock; receipts silently ignored).

Success looks like: a planner receives 3 pumps and types their 3 serial numbers; a technician fits one to Chiller 2 on a work order
and the other two stay in the store; months later the pump is removed to repair, sent to a supplier on an order line, comes back
with its repair cost, and is refitted to Chiller 5, and its page shows all of it, with its running hours and its repair count.

## Goals

1. **Serial-tracked parts.** A part can be marked serial-tracked. Each of its units is an asset record linked to the part, with a
   serial number, a status (in store, fitted, at repair, scrapped), and either its store or the asset it is fitted to.
2. **Serial numbers at receipt,** on purchase order receipts and manual receipts, so every unit in stock has its record.
3. **Fit and remove on a work order,** on the web and on the phone (by scanning the unit's label): fit a unit in store to the
   work order's asset; remove a fitted unit back to a store, to repair, or to scrap, with a condition note.
4. **History:** where each unit has been and its work orders; the asset page shows the units fitted now and before.
5. **Running hours while fitted,** taken from the asset's meter, plus the unit's own meters (units are assets).
6. **Life limits:** an operating life per part (months), an end-of-life date per unit, and an alert before it ends.
7. **Work orders and PMs on a unit,** as on any asset.
8. **Outside repair:** a unit under repair goes on a purchase order line to a supplier and comes back to a store on receipt, with
   the repair cost; each unit shows its repair count and total repair cost.

## Non-goals

- Importing units (the Parts sheet gains only the "serial tracked" column); the 4CSQL importer is unchanged.
- E-mails for end of life (the alert bell carries it).
- Unit-level costing in the part's average cost: stock value stays at the part's average cost.
- Serial numbers for consumables used up on a job; serial tracking is for units that are fitted and removed.
- A new report; repair counts and costs show on the part's units list and the unit's page.

## Anti-goals

- **Stock that disagrees with its units.** A serial part's quantity in a store always equals its units in that store.
- **A unit in two places,** or fitted twice.
- **Changing anyone's data on upgrade.** No part becomes serial-tracked by itself.
- **An asset list flooded with spares.** Units that aren't fitted are left out of the asset list, the asset tree and the dashboard
  counts by default.
- **Silent no-ops.** Every refused stock action on a serial part says what to do instead.

## Approaches considered

- **A. A unit is an asset linked to its part** (picked). Assets already have serial numbers, the statuses In storage, Under repair
  and Disposed, a parent asset, meters, work orders, PMs, history, costs, documents and QR labels. Medium-large.
- **B. A separate unit record.** Keeps spares out of the asset list, but needs its own history, meters, labels and pages, and work
  orders and PMs on a unit built anew. About twice the size.

## Design

### 1. Parts and units

- `Part.IsSerialTracked` (bool) and `Part.UnitLifeMonths` (int?, the default operating life of its units).
- A unit is an `Asset` with `PartId` set (new, nullable), its `SerialNumber` (required for units, unique per part among units not
  deleted), and:
  - **In store:** status In storage, `StoreId` set (new, nullable), no parent, site = the store's site.
  - **Fitted:** status Active, `ParentAssetId` = the asset it is fitted to, location and site = the parent's, no store.
  - **At repair:** status Under repair, no store, no parent.
  - **Scrapped:** status Disposed, `DisposedOn` set.
  - Name = the part's name; number from the asset numbering of its site; manufacturer and model from the part; purchase price =
    the receipt's cost per piece; `EndOfLifeOn` (new, DateOnly?) = receipt date + the part's life when it has one (editable).
- **Unit movements** (`UnitMovement`, new): unit, kind (`Received`, `Registered`, `Fitted`, `Removed`, `SentForRepair`,
  `BackFromRepair`, `Scrapped`, `Transferred`), time, store, asset (fitted to / removed from), work order, purchase order and line,
  outcome and condition note (removals), repair cost (back from repair), by whom. Written in the same save as the change.
- **Stock stays in step:** every move in or out of a store posts the usual stock ledger entry at the part's average cost, so stock
  values, work order part costs and reports keep working: a receipt (+1 per unit), fitting (an issue to the work order, −1),
  removing back to a store (a return, +1), sending from a store to repair or scrapping from a store (an adjustment, −1),
  back from repair (an adjustment, +1, the repair cost going to the unit, not the stock value), a transfer (out and in).
- **Whose cost a unit is:** fitting charges the fitting work order with the unit (an issue, as for any part). Removing it back to a
  store later is a stock movement only: the return names **no work order**, so no work order, asset or cost centre is credited
  and the removing work order shows no negative part cost; the removing work order is kept on the unit's movement. (My call, from
  the builder check: ledger entries can't be changed, and crediting either the old or the removing work order misstates one.)
- **Switching serial tracking on** for a part with stock: the part page asks for a serial number for each piece in each store
  ("Register units") and creates the units without moving stock (movement `Registered`). Switching on is refused while the
  serials given don't match the stock exactly. Switching off is refused while the part has units not scrapped.
- **Units already on machines at go-live** (my call, from the builder check):
  - **Register a fitted unit** (part's Units tab): a serial and the asset it is on; creates the unit already fitted, as a child of
    that asset, with no stock move (movement `Registered`).
  - **Make this asset a unit** (an asset's page, *Edit assets*): links an existing asset that has a parent and a serial number to a
    serial-tracked part, so it becomes a fitted unit, with no stock move. Its serial must be new for the part. Assets without a
    parent can't be linked this way (a spare in a store is registered on the part page, where the stock is).

### 2. Receiving

- On a purchase order receipt and a manual receipt, a serial part's line asks for one serial number per piece received
  (pieces = quantity × pack size). Quantities must be whole. Serials must be filled, distinct and new for the part.
- A line ordered for a work order (today received and issued straight to it): serial units are received into the store and stay
  there for fitting; the work order is told as today.

### 3. Fit and remove on a work order (web)

- The work order page gets a **Units** panel (shown when the work order has an asset, or a PM round's assets) listing the units
  fitted to its asset(s), each with **Remove**, and **Fit a unit**.
- **Fit a unit:** pick a serial-tracked part, then one of its units in a store at the work order's site (store, serial, end of life);
  choose the asset (the work order's, or one of the round's); optionally "count running hours from" one of that asset's meters
  (the first meter in hours by default). Fitting posts the issue to the work order, sets the unit fitted, writes the movement, and,
  when hours are counted, links the unit's running-hours meter to the asset's meter (creating the unit's meter "Running hours" if
  it has none) so the asset's increases are added to it while fitted (0.12.0's meter propagation).
- **Remove:** outcome (back to a store: choose the store, default the work order's site's default store; to repair; scrap) and a
  condition note. Removing unlinks the running-hours meter, posts the return when back to a store, sets the status, writes the
  movement.
- Permission: fitting and removing need *Execute work orders* and *Issue and move stock* (`inventory.transact`), like issuing
  parts. Units at another site can't be fitted ("Move the unit to a store at that site first.").

### 4. On the phone

- A job shows the units fitted to its asset(s), with **Remove** (back to the job's site's default store, to repair, or scrap,
  with a note), and **Fit a unit**: scan the unit's label (it is an asset label) or pick from the units in store at the job's site.
- Offline, these queue like parts used and are checked when they arrive: a unit no longer in store, or already removed, is
  refused with a note for the technician; nothing half-happens.
- The phone's data carries the units fitted to its jobs' assets and the units in store at its sites (for the serial-tracked parts
  only).
- **Receiving on the phone:** purchase order lines for serial parts are refused there with "Receive serial parts on the web, where
  their serial numbers are entered." (My call.) Repair lines have no part and don't show on the phone, as today.

### 5. Plain stock actions on serial parts

- **Issue** and **return** by quantity, on the web and the phone: refused, "This part is serial-tracked: fit or remove a unit on
  the work order."
- **Adjustment:** refused, "Scrap a unit or register a found one on the part's units."
- **Transfer:** by choosing units (serials), not a quantity.
- **Stock counts:** serial parts are left out of new counts, with a note on the count; their units are checked on the part's units list.

### 6. Unit actions outside a work order (part page, Units tab)

- The units list: serial, status, where (store or asset), end of life, repair count, total repair cost; filters by status and store;
  Print labels for the selected units.
- Actions on units in store: **Transfer** to another store, **Send for repair** (out of stock, to Under repair), **Scrap** (with a
  reason). **Register a found unit** (a serial for a piece found in a store: stock +1, movement `Registered`).
- Actions on units under repair (my call, from the builder check, so no unit is stuck): **Back from repair** into a store (stock
  +1, with an optional repair cost, for workshop or warranty repairs; movement `BackFromRepair`) and **Scrap** (no stock move).
- Actions on fitted units: **Remove** outside a work order too (same outcomes as on a work order; the return names no work order),
  for a unit on a machine that was disposed of, or removed without a job.
- Editing a unit's serial number and end of life happens on its asset page (*Edit assets*); a unit's status, store, parent and site
  change only through these actions, and units can't be deleted (they are scrapped).
- Permissions: *Issue and move stock* for all of these.

### 7. Outside repair

- A purchase order line can name a unit under repair (`PurchaseOrderLine.UnitId`, new). The line is a service line: its
  description defaults to "Repair of ‹part› ‹serial›", quantity 1, no part. Only units Under repair can be chosen, and a unit can
  be on one open repair line at a time.
- Receiving that line asks for the store it comes back to; the unit returns to the store (stock +1), the movement
  `BackFromRepair` records the line's net cost in the organization's currency, and the unit's repair count and repair cost go up.
- **Where the repair cost counts** (my call): on the unit's card always. When the repair line also names a work order (as any
  service line can today, for example a work order on the unit itself), the cost goes to that work order as today, and so into the
  cost reports and budgets. A repair line without a work order counts only on the unit.
- Removing the line or cancelling the order before receipt leaves the unit Under repair.

### 8. Pages

- **Unit's asset page:** a Unit card with the part (link), serial, status, store or fitted-to asset, end of life, repair count and
  cost, and its movement history (each with its work order or order).
- **Any asset's page:** a Fitted units card: units fitted now (with since when), and units fitted before (from, to, removal
  outcome), from the movements.
- **Asset list, tree and dashboard:** units not fitted are left out unless the filter "Show spare units" is on; pickers still find
  them by number or serial.
- **Alerts:** "Units reaching end of life" (within 30 days, not scrapped) in the alert bell for *View inventory*, opening the
  units list filtered.

### 9. Import

- The Parts sheet gains "Serial tracked" (yes/no) and "Unit life (months)". Turning it on through the import follows the same
  rule as the part page: refused while the part has stock without registered units.

### Permissions

No new permissions. As listed above: *Execute work orders* + *Issue and move stock* to fit and remove; *Issue and move stock* for
unit actions; *Manage inventory* to mark a part serial-tracked; *Create purchase orders* for repair lines; *Receive purchase
orders* to receive them; *Edit assets* to edit a unit's asset record.

### Arabic

Every new label, message and alert in English and Arabic.

## What's left to the builder

- Migration name; indexes (serial unique per part among live units).
- Exact endpoint shapes; how the receipt forms collect serials (a text area of one serial per line is fine).
- The phone's screens within its current style; how many in-store units the bundle may carry (cap it, newest first).
- The Units panel and Fitted units card visuals.
- A short user guide, `docs/SERIAL_PARTS.md`.

## Tests that matter most

- Receipt of a serial part creates one unit per piece with the serials given, in the store, with the ledger receipt; blank,
  repeated or existing serials and fractional quantities are refused.
- Fit: the unit leaves the store (issue to the work order, stock −1), becomes a child of the asset at its site, and its hours meter
  follows the asset's; a unit at another site or not in store is refused; fitting the same unit twice is refused.
- Remove: back to store (return, stock +1), to repair (no stock move), scrap; the hours meter stops following; history shows each.
- Quantity in each store equals the units in store after receipts, fits, removals, transfers, repairs, scraps and registrations.
- Plain issue, return and adjustment on a serial part are refused with the messages above; counts and the stock import leave
  serial parts out.
- A removal back to store names no work order: the removing work order's parts cost doesn't go negative.
- Register a fitted unit and Make this asset a unit create fitted units with no stock move; back from repair and scrap work for
  units under repair; remove works outside a work order.
- The phone refuses receiving serial-part lines with the message above.
- Switching serial tracking on needs serials matching the stock; off is refused with live units.
- Outside repair: only units under repair; one open line per unit; receipt returns the unit with the cost; repair count and cost.
- Phone: fit and remove offline, sent later; a unit fitted meanwhile by someone else is refused with a note.
- Asset list and dashboard leave out spare units by default; the filter shows them; fitted units show as children.
- End-of-life alert counts units within 30 days.
