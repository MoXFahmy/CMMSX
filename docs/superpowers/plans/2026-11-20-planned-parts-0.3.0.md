# 0.3.0 Planned Parts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan parts and hours on work orders and PM tasks, reserve stock for open work, show on hand / reserved /
available, warn before reserved stock is taken, and compare planned with actual cost.

**Architecture:** Planned lines are stored (`WorkOrderPart`, `PmTaskPart`, `WorkOrderAssignee.PlannedHours`); reserved
stock is never stored. A SQL view `StockReservations` sums still-reserved quantities per part and store for SQL-side
filters and columns; a pure C# helper (`Reservations`) holds the per-line rules (still reserved, shortage allocation in
order of need, the issue warning). Endpoints and pages read both.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit + SkippableFact with `ApiFixture`;
React 19, Mantine 8, TanStack Query 5, i18next.

**Spec:** `docs/superpowers/specs/2026-11-20-planned-parts-0.3.0-design.md`

## Global Constraints

- Reserved stock is never stored; it must always equal open work orders' planned lines less net issues (spec *Anti-goals*).
- "Open" = status Open, InProgress or OnHold (`WorkOrderWorkflow.IsActive`).
- A reservation never blocks: the warning is one confirmation (`confirmReserved: true`) away.
- With no planned lines, every existing number (lists, proposal, alert bell, report, issue flow) is unchanged.
- Re-order quantity 0 still means "never proposed".
- English strings in `web/src/locales/en.json`; no new packages; Android shell untouched.
- Server tests: `WT=<worktree> <scratchpad>/bin/wt-test [filter]` (helper defaults to this worktree). Web build:
  `<scratchpad>/bin/wt-web`. Migrations: `<scratchpad>/bin/wt-ef <Name>`. `<scratchpad>` =
  `/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Fractional quantities and negative on hand** (0.5 L of oil; a part allowed to go negative): allocation treats
   on hand below 0 as 0 and handles decimals exactly → unit tests in Task 1.
2. **Reopening a completed work order** brings its reservation back; cancelling releases it → integration test in Task 2.
3. **Issuing from a store other than the planned one** counts against the plan and releases the planned store's
   reservation → integration test in Task 2.
4. **Deleting a part that is planned** on a work order or PM task is refused with a clear message (not a 500 or an
   orphan line) → integration test in Task 2 (work order) and Task 5 (PM task).
5. **Adding a part that is already planned** (double click, two planners) answers 400 "already planned", never the
   generic duplicate-code 409 → integration test in Task 2.

---

### Task 1: Planned-parts model, migration, reservation view and rules

**Files:**
- Modify: `server/FourC.Cmms.Core/Work/Work.cs` (add `WorkOrderPart`; `WorkOrderAssignee.PlannedHours`)
- Modify: `server/FourC.Cmms.Core/Pm/Pm.cs` (add `PmTaskPart`; `PmTask.Parts`)
- Create: `server/FourC.Cmms.Core/Inventory/Reservations.cs` (pure rules + `StockReservation` view row)
- Modify: `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`
- Create: migration `PlannedParts` (via `wt-ef PlannedParts`), then add the view SQL to its `Up`/`Down`
- Test: `server/FourC.Cmms.Tests/ReservationTests.cs`

**Interfaces:**
- Produces:
  - `class WorkOrderPart : Entity, ITenantOwned, IAuditable { Guid TenantId; Guid WorkOrderId; Guid PartId; Part? Part; Guid? StoreId; Store? Store; decimal Quantity; string? Notes; audit fields }`
    — unique index (`WorkOrderId`, `PartId`); index (`TenantId`, `PartId`, `StoreId`); `Quantity` precision as other quantities.
  - `class PmTaskPart : Entity { Guid PmTaskId; Guid PartId; Part? Part; decimal Quantity }` — unique (`PmTaskId`, `PartId`); `PmTask.Parts : List<PmTaskPart>`.
  - `WorkOrderAssignee.PlannedHours : decimal?`
  - `class StockReservation : ITenantOwned { Guid TenantId; Guid PartId; Guid StoreId; decimal Reserved }` — keyless, `ToView("StockReservations")`; DbSet `StockReservations`.
  - `sealed record PlanLine(Guid LineId, Guid WorkOrderId, string WorkOrderNumber, DateOnly? DueDate, DateOnly? PlannedStart, decimal StillReserved)`
  - `sealed record ShortfallChange(Guid WorkOrderId, string WorkOrderNumber, decimal MoreShort)`
  - `static class Reservations`:
    - `decimal StillReserved(decimal planned, decimal netIssued, WorkOrderStatus status)` — `max(planned − netIssued, 0)` when open, else 0.
    - `IReadOnlyDictionary<Guid, decimal> Shortages(decimal onHand, IEnumerable<PlanLine> lines)` — by line id.
    - `IReadOnlyList<ShortfallChange> WouldShort(decimal onHand, IReadOnlyList<PlanLine> lines, decimal quantity, Guid? toWorkOrderId)`
    - `const` view name `"StockReservations"`.

- [ ] **Step 1: Write the failing unit tests** (`[Fact]`, no database)

```csharp
[Fact] public void Still_reserved_never_goes_below_zero_and_ends_when_the_work_order_is_not_open()
{
    Assert.Equal(2.5m, Reservations.StillReserved(4m, 1.5m, WorkOrderStatus.OnHold));
    Assert.Equal(0m, Reservations.StillReserved(4m, 6m, WorkOrderStatus.Open));
    Assert.Equal(0m, Reservations.StillReserved(4m, 0m, WorkOrderStatus.Completed));
}
[Fact] public void Stock_goes_to_the_earliest_due_job_first()
{
    var a = Line("WO-2", due: D(10), reserved: 10); var b = Line("WO-1", due: D(1), reserved: 5);
    var s = Reservations.Shortages(10m, [a, b]);
    Assert.Equal(0m, s[b.LineId]); Assert.Equal(5m, s[a.LineId]);
}
[Fact] public void Ties_go_by_planned_start_then_number_and_undated_jobs_come_last()
{
    var late = Line("WO-1", due: null, reserved: 3); var b = Line("WO-3", due: D(2), start: D(1), reserved: 3); var a = Line("WO-2", due: D(2), start: D(1), reserved: 3);
    var s = Reservations.Shortages(6m, [late, b, a]);
    Assert.Equal((0m, 0m, 3m), (s[a.LineId], s[b.LineId], s[late.LineId]));
}
[Fact] public void Negative_on_hand_counts_as_none_and_fractions_are_exact()
{
    var a = Line("WO-1", due: D(1), reserved: 0.5m);
    Assert.Equal(0.5m, Reservations.Shortages(-3m, [a])[a.LineId]);
    Assert.Equal(0.25m, Reservations.Shortages(0.25m, [a])[a.LineId]);
}
[Fact] public void Issuing_its_own_parts_never_warns_the_most_urgent_job()
{
    var b = Line("WO-B", due: D(1), reserved: 5); var a = Line("WO-A", due: D(30), reserved: 10);
    Assert.Empty(Reservations.WouldShort(10m, [b, a], 5m, b.WorkOrderId)); // WO-A is 5 short before and after
}
[Fact] public void Taking_reserved_stock_lists_the_jobs_it_shorts_and_by_how_much()
{
    var a = Line("WO-A", due: D(5), reserved: 4);
    var c = Assert.Single(Reservations.WouldShort(6m, [a], 3m, null));
    Assert.Equal(("WO-A", 1m), (c.WorkOrderNumber, c.MoreShort));
}
```
(`Line(...)`/`D(...)` are local helpers building `PlanLine` and `DateOnly.FromDayNumber`.)

- [ ] **Step 2: Run to verify they fail** — `wt-test ReservationTests` → Expected: build error, `Reservations` not defined.
- [ ] **Step 3: Implement the entities, `Reservations` and DbContext config.** Order of need = `DueDate ?? DateOnly.MaxValue`,
  then `PlannedStart ?? DateOnly.MaxValue`, then `WorkOrderNumber` (ordinal). `WouldShort`: shortages before; after =
  on hand − quantity, and the target work order's lines lose up to `quantity` of still-reserved first; return other
  work orders whose total shortage grew, with the growth.
- [ ] **Step 4: Run to verify they pass** — `wt-test ReservationTests` → Expected: PASS (6).
- [ ] **Step 5: Add the migration** — `wt-ef PlannedParts`; then append to `Up` (and `DROP VIEW` in `Down`):

```sql
CREATE VIEW StockReservations AS
SELECT wp.TenantId, wp.PartId, wp.StoreId,
       SUM(CASE WHEN wp.Quantity > ISNULL(i.Net, 0) THEN wp.Quantity - ISNULL(i.Net, 0) ELSE 0 END) AS Reserved
FROM WorkOrderParts wp
JOIN WorkOrders w ON w.Id = wp.WorkOrderId AND w.IsDeleted = 0 AND w.Status IN (1, 2, 3)
OUTER APPLY (SELECT -SUM(t.Quantity) AS Net FROM StockTransactions t
             WHERE t.WorkOrderId = wp.WorkOrderId AND t.PartId = wp.PartId AND t.Type IN (2, 3)) i
WHERE wp.StoreId IS NOT NULL
GROUP BY wp.TenantId, wp.PartId, wp.StoreId
```
- [ ] **Step 6: Full suite** — `wt-test` → Expected: all pass (143 + 6), 2 skipped.
- [ ] **Step 7: Commit** — `feat(planned parts): model, migration, reservation view and rules`.

### Task 2: Planned parts and hours on work orders

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PlanningEndpoints.cs` (routes below; `MapPlanningEndpoints` in `Program.cs`)
- Create: `server/FourC.Cmms.Api/Features/Planning.cs` (queries shared by Tasks 2–5)
- Modify: `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (detail fields; `WorkOrderInput.CopyPlanFromId`)
- Modify: `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (part delete guard)
- Test: `server/FourC.Cmms.Tests/PlannedPartsTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `Reservations`, `StockReservations`.
- Produces (`static class Planning`):
  - `Task<Guid?> DefaultStoreAsync(CmmsDbContext db, Guid partId, Guid siteId, CancellationToken ct)` — spec §1 rule.
  - `Task<List<PlanLine>> OpenLinesAsync(CmmsDbContext db, Guid partId, Guid storeId, CancellationToken ct)`
  - `Task<Dictionary<Guid, decimal>> NetIssuedAsync(CmmsDbContext db, Guid workOrderId, CancellationToken ct)` — by part.
  - `Task<PlannedLabour> LabourAsync(CmmsDbContext db, WorkOrder w, CancellationToken ct)`; `record PlannedLabour(decimal Hours, decimal Cost)`.
  - `Task CopyPlanAsync(CmmsDbContext db, Guid fromWorkOrderId, WorkOrder to, CancellationToken ct)`.
- Routes (all `RequirePermission(WorkOrdersEdit)` plus an in-handler `InventoryView` check; refused with
  `DomainException` when the work order is Completed, Closed or Cancelled):
  - `POST /api/work-orders/{id}/planned-parts` `{ partId, storeId?, quantity, notes? }` → `{ id }`; `storeId` omitted → `DefaultStoreAsync`.
  - `PUT /api/work-orders/{id}/planned-parts/{lineId}` `{ storeId?, quantity, notes? }`; `DELETE` same path → 204.
  - `PUT /api/work-orders/{id}/planned-hours` `{ lines: [{ employeeId, plannedHours }] }` (assignees only; null clears).
- Detail additions (`GET /api/work-orders/{id}`): `plannedParts[]` `{ id, partId, partNumber, partName, unitOfMeasure,
  storeId, storeCode, quantity, issued, reserved, short, unitCost, plannedCost, notes }`; `assignees[].plannedHours`;
  `planned { laborHours, laborCost, partsCost, totalCost }`.

- [ ] **Step 1: Write the failing integration tests** (`[SkippableFact]`, setup copied from `InventoryTests`)

```csharp
Planned_parts_reserve_stock_until_issued_and_show_shortages_by_due_date()
  // stock 10; WO-1 due +1 plans 6; WO-2 due +5 plans 6 → WO-1 { reserved 6, short 0 }, WO-2 { reserved 6, short 2 }
  // issue 4 to WO-1 → WO-1 { issued 4, reserved 2, short 0 }
Completing_releases_and_reopening_restores_the_reservation()
  // plan 3; complete → plannedParts[0].reserved == 0; reopen (InProgress) → 3; cancel → 0
Issuing_from_another_store_counts_against_the_plan()
  // plan 2 from MAIN; issue 2 from SAT to the WO → line issued 2, reserved 0; MAIN reserved 0
Default_store_prefers_the_sites_store_then_a_central_store()
  // part stocked only in a store on site B; WO on site A → line.storeId == site B store; no stock level anywhere → storeId null, reserved 0
Planning_the_same_part_twice_is_refused_with_a_clear_message()   // 400, message contains "already planned"
Planned_hours_and_parts_give_the_planned_cost()
  // tech rate 20, planned 3h; part avg cost 5 × qty 2 → planned { laborHours 3, laborCost 60, partsCost 10, totalCost 70 }
  // no planned hours, estimatedHours 4, two techs at 10 and 30 → laborCost 80
A_part_planned_on_a_work_order_cannot_be_deleted()               // DELETE /api/parts/{id} → 400
Plans_are_locked_once_the_work_order_is_completed()              // POST planned-parts on Completed → 400
Duplicating_copies_planned_parts_and_hours()                     // POST /api/work-orders with copyPlanFromId → same lines, hours for kept assignees
```
- [ ] **Step 2: Run to verify they fail** — `wt-test PlannedPartsTests` → Expected: FAIL (404 on the new routes).
- [ ] **Step 3: Implement `Planning`, `PlanningEndpoints` and the detail fields.** Short per line = `Reservations.Shortages`
  over `OpenLinesAsync` for that line's part and store, using the level's on hand. Unique-index race: catch
  `DbUpdateException` on insert → `DomainException("{partNumber} is already planned on this work order.")`. Part
  delete: refuse when any `WorkOrderParts` or `PmTaskParts` row uses it ("This part is planned on work orders or PM tasks…").
- [ ] **Step 4: Run to verify they pass** — `wt-test PlannedPartsTests` → Expected: PASS (9).
- [ ] **Step 5: Full suite** — `wt-test` → all pass.
- [ ] **Step 6: Commit** — `feat(planned parts): planned parts and hours on work orders`.

### Task 3: The "Issue anyway?" warning

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (`IssueInput`, `TransferInput` gain `bool ConfirmReserved = false`)
- Test: `server/FourC.Cmms.Tests/PlannedPartsTests.cs`

**Interfaces:**
- Consumes: `Planning.OpenLinesAsync`, `Reservations.WouldShort`.
- Produces: 409 body `{ title: "This takes stock reserved for other work.", reserved: [{ workOrderId, workOrderNumber, title, moreShort }] }`.

- [ ] **Step 1: Write the failing tests**

```csharp
Taking_reserved_stock_asks_first_and_goes_through_when_confirmed()
  // stock 5; WO-A plans 4; issue 3 with no WO → 409, reserved[0] { workOrderNumber = WO-A, moreShort = 2 }; on hand still 5
  // same issue with confirmReserved = true → 200; WO-A short 2
The_most_urgent_job_takes_its_own_parts_without_a_question()      // spec §7 example: WO-B (due first) issue 5 → 200 without confirm
Transfers_out_warn_too_and_adjustments_never_do()                 // transfer 3 → 409; adjust −3 with notes → 200
Without_planned_parts_issuing_asks_nothing_new()                  // no plans: issue → 200; "not enough stock" still 400
```
- [ ] **Step 2: Run to verify they fail** — `wt-test "FullyQualifiedName~PlannedPartsTests"` → Expected: the 409 assertions fail.
- [ ] **Step 3: Implement** inside the existing transaction, before `StockLedger.PostAsync`: when not confirmed and
  `WouldShort` is non-empty, return `Results.Json(body, statusCode: 409)`. Existing stock rules still run first for
  a move the ledger would refuse anyway (call order: validate target → reserved check → post).
- [ ] **Step 4: Run to verify they pass**; **Step 5: full suite**; **Step 6: Commit** — `feat(planned parts): warn before reserved stock is taken`.

### Task 4: On hand, reserved and available in inventory

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/InventoryEndpoints.cs` (part detail, `PartListItem`, `ListAsync`, `/inventory/proposal`)
- Modify: `server/FourC.Cmms.Api/Features/AlertEndpoints.cs`, `server/FourC.Cmms.Api/Features/ReportEndpoints.cs` (`ReorderAsync`)
- Test: `server/FourC.Cmms.Tests/PlannedPartsTests.cs`

**Interfaces:**
- Consumes: `StockReservations`, `Planning.OpenLinesAsync`, `Reservations.Shortages`.
- Produces:
  - Part detail: `levels[].reserved`, `levels[].available`; `reserved`, `available` totals; `reservedFor[]`
    `{ workOrderId, workOrderNumber, title, storeCode, reserved, short, dueDate }` ordered by need.
  - `PartListItem` gains `decimal Reserved, decimal Available` (after `OnHand`); export keys `reserved`, `available`.
  - Proposal rows gain `reserved`, `available`; suggested = `max(reorderQty, max > 0 ? max − available − onOrder : 0, reserved − max(onHand, 0) − onOrder)`, ≥ 0.

- [ ] **Step 1: Write the failing tests**

```csharp
Part_page_and_list_show_reserved_and_available_and_who_holds_the_stock()
  // stock 10, plans 6 + 6 → level { reserved 12, available −2 }; reservedFor = [WO-1 short 0, WO-2 short 2]; list row { reserved 12, available −2 }
Reorder_checks_use_available_stock()
  // reorder level 5, qty 10, on hand 8, planned 6 → list belowReorder includes it, alerts.partsBelowReorder == 1, proposal suggests 10
Proposal_covers_planned_need_where_no_max_level_is_set()
  // reorder level 0, qty 1, max 0, on hand 2, planned 7 → suggested 5
Nothing_changes_without_plans()
  // same part, no plans: list available == onHand, proposal and report rows equal today's values
```
- [ ] **Step 2: Run to verify they fail**; **Step 3: Implement** — SQL-side filters and columns read
  `db.StockReservations.Where(r => r.PartId == … && r.StoreId == …).Sum(r => (decimal?)r.Reserved) ?? 0`; the report
  adds `reserved` and `available` columns and uses the same suggestion formula (extract it as
  `internal static decimal SuggestedQuantity(decimal onHand, decimal reserved, decimal onOrder, decimal reorderQty, decimal max)` in `InventoryEndpoints`, used by both).
- [ ] **Step 4: Run to verify they pass**; **Step 5: full suite**; **Step 6: Commit** — `feat(planned parts): reserved and available stock`.

### Task 5: PM task parts, generation and cost per visit

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/PmEndpoints.cs` (`TaskInput.Parts`, task detail/list, schedule detail, forecast)
- Modify: `server/FourC.Cmms.Api/Features/PmGenerator.cs` (`CreateWorkOrderAsync` configure step)
- Test: `server/FourC.Cmms.Tests/PlannedPartsTests.cs`

**Interfaces:**
- Consumes: `Planning.DefaultStoreAsync`.
- Produces:
  - `record TaskPartInput(Guid PartId, decimal Quantity)`; `TaskInput` gains trailing `List<TaskPartInput>? Parts = null` (null on update = keep, list = replace).
  - Task detail: `parts[] { partId, partNumber, partName, unitOfMeasure, quantity, unitCost }`, `partsCost`.
  - `static class PmCost { Task<VisitCost> ForScheduleAsync(CmmsDbContext db, PmSchedule s, Guid taskId, CancellationToken ct) }`, `record VisitCost(decimal Parts, decimal Labour, decimal Total)`.
  - Schedule detail: `visitCost`; forecast rows: `cost` (computed once per schedule and task, not per row).

- [ ] **Step 1: Write the failing tests**

```csharp
Generated_pm_work_orders_arrive_with_their_parts_planned()
  // task parts: P1 × 2; generate → WO plannedParts == [P1 × 2] with the site's store; part reserved 2
A_round_plans_parts_for_every_covered_asset()                     // 3 covered assets, task P1 × 2 → planned 6
Multiple_pm_plans_the_parts_of_the_task_used_that_time()          // every-2nd task has P2 × 1 → 2nd generation plans P2 only
Visit_cost_adds_parts_and_labour_for_schedule_and_forecast()
  // P1 avg 5 × 2 = 10; est. 3h, assignees at 10 and 30 → labour 60; visitCost.total 70; forecast rows cost 70
Task_parts_reject_duplicates_and_non_positive_quantities()        // 400
A_part_on_a_pm_task_cannot_be_deleted()                           // DELETE /api/parts/{id} → 400
```
- [ ] **Step 2: Run to verify they fail**; **Step 3: Implement**; **Step 4: Run to verify they pass**; **Step 5: full suite**;
  **Step 6: Commit** — `feat(planned parts): PM task parts, generation and visit cost`.

### Task 6: Import PM parts lists from 4CSQL

**Files:**
- Modify: `server/FourC.Cmms.LegacyImport/Legacy.cs` (`LChecklistPart { ChecklistNo, PartNo, Qty }`; `LegacyWork.ChecklistParts`, read optionally: `SELECT ChecklistNo, PartNo, Qty FROM checklistInv`)
- Create: `server/FourC.Cmms.LegacyImport/PmPartsImport.cs`; call it in `Program.cs` **after** `InventoryImport.RunAsync`
- Modify: `docs/LEGACY_MIGRATION.md`
- Test: `server/FourC.Cmms.Tests/RoundImportTests.cs` (pure function tests live beside the other importer tests)

**Interfaces:**
- Produces: `static IReadOnlyList<(string TaskCode, string PartNo, decimal Quantity)> PmPartsImport.Lines(IEnumerable<LChecklistPart> rows)`
  — trims keys, drops blanks and quantities ≤ 0, merges repeats of the same task + part by summing (case-insensitive);
  `RunAsync(CmmsDbContext db, Guid tenantId, LegacyWork w, Report report)` upserts `PmTaskPart` by (task LegacyRef `pmtask:{code}`, part number), counts skipped rows in the report.

- [ ] **Step 1: Failing test** `Pm_parts_rows_are_cleaned_and_merged` — rows `(" T1 ","P1",1)`, `("t1","P1",2)`, `("T1","",1)`, `("T2","P2",0)` → `[("T1","P1",3)]`.
- [ ] **Step 2: Run → fail**; **Step 3: Implement**; **Step 4: Run → pass**; **Step 5: full suite**.
- [ ] **Step 6: Local import check** — run the importer against `Legacy_MEG` into a scratch tenant (as in
  `docs/LEGACY_MIGRATION.md`) → Expected: report shows PM task parts created ≈ 166 minus skipped, and a re-run creates 0.
- [ ] **Step 7: Commit** — `feat(import): PM parts lists from 4CSQL`.

### Task 7: Web — work order planning, cost table, issue warning, job card

**Files:**
- Modify: `web/src/api/client.ts` (`ApiError.body?: unknown`, set from the parsed error body)
- Modify: `web/src/api/types.ts` (`WorkOrderDetail.plannedParts`, `planned`, `assignees[].plannedHours`; `PlannedPart`)
- Modify: `web/src/pages/work/WorkOrderDetailPage.tsx` (Parts tab "Planned parts" card above issued parts; Labour tab "Planned" table; Costs tab planned-vs-actual table)
- Modify: `web/src/components/StockTxModal.tsx` (409 `reserved` → confirm modal listing work orders → resend with `confirmReserved: true`)
- Modify: `web/src/pages/work/WorkOrderFormPage.tsx` (duplicate sends `copyPlanFromId`)
- Modify: `web/src/pages/work/WorkOrderPrintPage.tsx` (planned parts rows before blanks)
- Modify: `web/src/locales/en.json` (`plan.*` keys)

**Interfaces:**
- Consumes: Task 2 and 3 routes and shapes.

- [ ] **Step 1: Implement.** Planned parts card: columns Part, Store, Planned, Issued, Reserved, Short (red when > 0;
  "Choose a store" when no store), Planned cost, actions (edit, delete); "Plan a part" opens a modal with `PartPicker`,
  store `Select` (empty = default rule) and quantity. Shown edit controls only with Edit work orders + View inventory and
  an unlocked work order. "Issue" on a planned row opens `StockTxModal` prefilled with part, planned store and remaining quantity.
- [ ] **Step 2: Build** — `wt-web` → Expected: `✓ built`, exit 0.
- [ ] **Step 3: Commit** — `feat(planned parts): work order planning on the web`.

### Task 8: Web — inventory and PM; changelog

**Files:**
- Modify: `web/src/pages/inventory/PartDetailPage.tsx` (levels Reserved/Available; "Reserved for" card)
- Modify: `web/src/pages/inventory/InventoryPage.tsx` (PartsTab columns `reserved` hidden by default via `+reserved` token rules, `available` shown)
- Modify: `web/src/pages/purchasing/PurchasingPage.tsx` (proposal columns Reserved, Available)
- Modify: `web/src/pages/pm/PmPage.tsx` (TasksTab modal: parts editor with `PartPicker` + quantity and the task's parts cost; ForecastTab cost column and total)
- Modify: `web/src/pages/pm/PmScheduleFormPage.tsx` (visit cost card: parts, labour, total)
- Modify: `web/src/locales/en.json`, `CHANGELOG.md` (`## [0.3.0] - <date>` with Added/Changed, incl. "re-order checks now use available stock")

- [ ] **Step 1: Implement.** Negative available shows red.
- [ ] **Step 2: Build** — `wt-web` → exit 0.
- [ ] **Step 3: Hands-on check** in a local container built from the worktree (as for 0.2.0, port 8091): plan parts on
  two work orders, see shortages, the warning on issue, part page, parts list columns, proposal, a PM task with parts →
  generate → planned lines, forecast cost, job card. Remove the container and scratch database afterwards.
- [ ] **Step 4: Commit** — `feat(planned parts): inventory and PM on the web; changelog`.
