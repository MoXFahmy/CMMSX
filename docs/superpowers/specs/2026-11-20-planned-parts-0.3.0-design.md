# 0.3.0 Planned parts (PM-1, IN-1, WO-3) — design

Source: DimoMaint report items PM-1 (PM materials and planned labour), IN-1 (reservations and available stock) and WO-3
(planned labour and parts, planned vs actual cost), ranked 4th and planned as release 0.3. The user agreed the
behaviour below on 2026-11-20. Lines marked *(builder's call)* were settled by the builder and can be checked.

## Why

Today a part reaches a job only when someone issues it. Nobody can see what a job will need before it starts, stock
already promised to planned work looks free (so the re-order proposal orders too late), and PM costs are known only
after the work is done. DimoMaint plans parts and labour on every work order and PM. 0.3.0 lets a planner list the
parts a job needs, holds that stock for the job, shows storekeepers what is really available, and compares planned with
actual cost. PM tasks carry their parts list, so every PM work order arrives planned.

## Decisions made with the user

- **A reservation warns, never blocks.** Issuing stock that is reserved for other jobs asks "Issue anyway?" and goes
  through on confirmation; the other jobs then show a shortage.
- The behaviour in *Scope* below, as played back and accepted ("Looks right").
- Reserved stock is **worked out from the planned lines**, never stored (see *Approaches*). The user left the building
  approach to the builder.

## Scope

### 1. Planned parts on a work order

- The work order's **Parts** tab gets a **Planned parts** table above the issued parts: part, store, planned quantity,
  issued so far, still reserved, short, planned cost. Lines can be added, changed and removed.
- One line per part per work order (adding a part that is already planned is refused with a message pointing at the
  existing line) *(builder's call: keeps "issued against plan" unambiguous)*.
- **Store:** the default comes from the active stores that have a stock level for the part, at any quantity (zero or
  below counts: out-of-stock parts are the ones planning matters most for). Stores on the work order's site come first;
  when none there has the part, any site's store is used (a central store). Among them: most available, then most on
  hand, then store code. With no stock level anywhere the line has no store. The planner can pick any active store.
  *(Builder's call, from the test data: Kandil Glass has 7 sites and 2 stores, EGA 3 sites and 1 store, so most of their
  work orders are on sites without a store of their own.)* A line with no store reserves nothing and shows
  "Choose a store".
- **Issued against plan** = net quantity of that part issued to this work order from any store (issues minus returns),
  taken from the stock ledger. Issuing from a different store than planned still counts.
- **Still reserved** = planned − issued, never below 0, and only while the work order is Open, In progress or On hold.
  Completing, closing or cancelling releases what is left; reopening brings it back; deleting the work order (or the
  line) removes it.
- **Short:** a store's on-hand stock goes to its open jobs in order of need: work order due date, then planned start,
  then work order number (no date sorts last). A line is short by the part of its still-reserved quantity that the
  stock left after earlier jobs can't cover. A line with no store is not "short", it shows "Choose a store".
- Unplanned issues and issuing more than planned stay allowed; they count as actual only.
- Who may change the plan: people with **Edit work orders** *and* **View inventory**; only while the work order is not
  Completed, Closed or Cancelled. The Parts tab stays visible only with **View inventory**, as today.
- **Duplicate** copies the planned parts (with their stores re-picked by the default rule) and planned hours, like it
  copies the checklist and technicians.

### 2. Planned hours per technician

- Each assigned technician can be given **planned hours** (optional) — on the Labour tab, a small "Planned" table listing
  the assignees with an editable hours field *(builder's call on placement)*. Same permission as editing the work order.
- **Planned labour:** the sum of the technicians' planned hours, each costed at that technician's current hourly rate
  (no rate → 0). When no technician has planned hours, planned labour falls back to the work order's existing
  *Estimated hours*, costed at the average hourly rate of the assigned technicians (none or no rates → 0).
- PM generation does not fill per-technician hours; the task's estimated hours land in *Estimated hours* as today.

### 3. Planned vs actual cost

- The **Costs** tab gets a table: rows *Labour hours*, *Labour cost*, *Parts*, *Other costs*, *Total*; columns
  *Planned*, *Actual*, *Difference*. Other costs have no planned value (shown as "—" and left out of the planned total).
- Planned parts cost = planned quantity × the part's **current** average cost *(builder's call: no snapshot)*.
- The work order list and its export are unchanged.

### 4. Parts lists on PM tasks

- A PM task gets a **Parts** list: part and quantity, one line per part, edited on the PM task form (permission
  **Manage PM**).
- A work order generated from a PM task (on schedule, by "Generate now", or by a meter reading) gets one planned line per
  task part, store picked by the default rule against the schedule's site. Multiple PM uses the parts of the task chosen
  for that occurrence.
- **Rounds** (several assets): planned quantity = task quantity × number of assets the work order covers, the way
  checklist steps repeat per asset.

### 5. PM cost per visit

- **Estimated cost per visit** = task parts (quantity × current average cost, × covered assets on a round) + task
  estimated hours × the average hourly rate of the schedule's active assignees (none or no rates → 0).
- Shown on the PM schedule page (with its parts and labour split) and on each row of the PM forecast, with the total of
  the rows listed. The PM task page shows its parts cost.

### 6. Stock: on hand, reserved, available

- **Available = on hand − reserved** per store level; it can go negative (shown in red).
- **Part page:** the store table adds *Reserved* and *Available*, and a **Reserved for** list shows the open work
  orders holding the part: work order, store, still reserved, short, due date.
- **Parts list:** new columns *Reserved* (hidden by default) and *Available* (shown), summed over all stores or for the
  store filter; both export like the other columns.
- **Re-order level checks use available stock** wherever on hand was compared with the re-order level: the
  *below re-order* filter and "low" flag on the parts list, and the alert bell count.
- **Re-order proposal:** a store level is proposed when available + on order ≤ re-order level. Re-order quantity 0 still
  means "never proposed", as in 4CSQL; such parts are bought per job with *Order for job*. Suggested quantity =
  max(re-order quantity, *max level − available − on order* when a max level is set, *reserved − on hand (if above 0) −
  on order*), never below 0. The last term covers planned need beyond stock and open orders even where no max level is
  set; with nothing reserved it is never positive, so today's numbers don't change *(builder's call)*. The proposal shows
  on hand, reserved, available and on order.
- The **"Stock below re-order level" report** uses the same rule and adds *Reserved* and *Available* columns.

### 7. The issue warning

- Applies to **issues** and **transfers out**. It does not apply to adjustments, stock takes or returns.
- The warning follows the same order-of-need rule as "short": the shortages of the store's other open work orders are
  worked out before and after the move. It fires only when the move makes at least one **other** work order short, or
  more short *(builder's call)*. So the most urgent job never gets asked when issuing its own parts, even if a later job
  is already short; an issue to a work order counts against that work order's own still-reserved quantity first.
- When it fires, the server refuses with a "reserved" answer listing those work orders (number, title, and how much
  more short each becomes). The web shows them and asks **Issue anyway?**; confirming resends the move with a
  confirmation flag, and it goes through. Any client (the Android app included) gets the same check.
- The existing rules (not enough stock, negative stock not allowed, batches, expiry) are unchanged and apply first.

### 8. Job card

- The printed job card lists the planned parts (part, planned quantity, an empty "used" column) before its blank rows.

### 9. Import from 4CSQL

- The importer reads `checklistInv` (`ChecklistNo`, `PartNo`, `Qty`) into PM task parts: task by checklist number, part
  by part number. Rows whose task or part isn't found, or with a quantity ≤ 0, are skipped and counted in the import
  report. Re-running the import updates quantities and doesn't duplicate lines. (Local test data: 166 lines on 57 MEG
  tasks, 69 on 17 Kandil Glass tasks, none at EGA.)
- `docs/LEGACY_MIGRATION.md` moves PM materials from "Not yet imported" to the imported tables.

## Non-goals

- A "short of parts" filter on the work order list; automatic status changes (e.g. On hold, waiting for parts);
  e-mails about shortages.
- Reserving stock for forecast occurrences that haven't been generated yet.
- Reservation history, planned-cost snapshots, planned subcontractor lines.
- Several suppliers per part, purchase units, serial-numbered parts (IN-2, IN-3, IN-4).

## Anti-goals

- **A reservation never stops anyone.** Nothing blocks an urgent issue beyond one confirmation.
- **Nothing changes for organizations that don't plan.** With no planned lines, reserved is 0, available equals on hand,
  issuing asks nothing new, and every list, proposal and alert gives today's numbers.
- **The reserved figure can't drift.** It must always equal what the open work orders' planned lines and the ledger say.

## Approaches

1. **Compute reserved from planned lines (chosen).** Store only the planned lines. Reserved for a store level is
   summed at query time over planned lines of open work orders, less net issues from the ledger. Status changes,
   reopening, bulk actions, deletes, issues and returns need no extra bookkeeping. Cost: stock screens run an extra
   aggregate; fine at current sizes (largest customer ≈ 24,000 parts), with an index on planned lines by part and store.
2. **A stored reserved counter on each store level,** updated on every event. Fastest reads, but a dozen code paths
   must keep it right and one miss leaves it wrong until a rebuild. Rejected: works against "can't drift".
3. **A reservation ledger** (reserve/release entries like stock transactions). Full history, heaviest to build, history
   nobody asked for. Rejected.

## How it is built *(builder's calls unless noted)*

- **Core entities** (tenant-owned, soft-delete like their neighbours):
  - `WorkOrderPart`: `WorkOrderId`, `PartId`, `StoreId?`, `Quantity` (planned, > 0), `Notes?`, audit fields; unique
    (`WorkOrderId`, `PartId`) among non-deleted lines; index (`PartId`, `StoreId`).
  - `PmTaskPart`: `PmTaskId`, `PartId`, `Quantity` (> 0); unique (`PmTaskId`, `PartId`).
  - `WorkOrderAssignee.PlannedHours` (decimal?, ≥ 0).
- **One place that computes reservations** (a static helper next to `StockLedger`): still-reserved per line, reserved per
  store level, allocation of on hand to lines in order of need (shortage), and "free for this move". Used by the work
  order detail, part page, parts list, proposal, alert bell and the issue/transfer endpoints. The shortage allocation is
  a pure function with unit tests.
- **Store default rule** in one function, used by manual add, PM generation and duplicate.
- **PM generation** adds planned lines inside `PmGenerator.CreateWorkOrderAsync`'s configure step.
- **Issue/transfer endpoints** take an optional `confirmReserved` flag; without it, a move that dips into other jobs'
  reservations returns 409 with the work orders it would short.
- **Migration** `PlannedParts`; CHANGELOG `[0.3.0]`; release via `scripts/release.sh 0.3.0` (APK versionCode 3001).

## Constraints

- Multi-tenant query filters on every new table; tests use the existing `ApiFixture` and SkippableFact pattern.
- English strings in `web/src/locales/en.json`; no new dependencies; the Android shell needs no change.
- All existing tests keep passing; the default experience for an organization with no planned lines is unchanged.

## Left to the builder

Endpoint shapes and routes, exact UI layout and wording, query shapes and indexes beyond the one named, how the web
shows the "Issue anyway?" list, and how the PM forecast row cost is laid out.
