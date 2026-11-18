# Multi-asset PM schedules — design

Status: draft for approval · 2026-11-18

## Why

4CSQL lets one PM schedule cover an ordered **list of assets** (a "round"). Each cycle produces **one** work order whose
checklist repeats the PM task's steps for every asset in the list. Technicians do the round on one work order.

The cloud edition can only schedule a PM on one asset or one location, so the importer currently **drops** these
schedules. In the three customer databases supplied:

| Customer | Multi-asset PMs (active) | Assets per list | Work orders generated from them |
|---|---|---|---|
| MEG | 298 (221) | 1–36, avg 5 | 86,338 of 290,225 (30%) |
| KandilGlass | 9 | — | — |
| EGA | 1 | — | — |

For MEG this is a core way of working, not an edge case. We want the cloud edition to support it natively, so MEG's
schedules and history migrate faithfully and customers can keep running rounds after the move.

## Decisions taken

- **One work order per cycle** covering all listed assets (same as 4CSQL). Not one work order per asset, and not
  parent/child work orders.
- **Per-asset visibility** (better than 4CSQL): each listed asset's own history shows the round's work orders, and its
  "open work" check sees them. In 4CSQL these work orders carry no asset and are invisible from the asset side.
- **Labour and costs stay on the work order**; they are not split across the assets. Cost and reliability reports by
  asset therefore do not include round work orders (same as 4CSQL today).
- **Approach A** (below): an asset list on the schedule and on the work order, and checklist steps that carry their asset.

## Goals

1. A PM schedule can target *several assets* (ordered list) as an alternative to one asset or one location.
2. Generation creates one work order per cycle; its checklist has the task's steps once per asset, grouped by asset in
   list order. Multiple PM ("every Nth time use the heavier task") works unchanged.
3. Each checklist step can record a **reading** and **remarks** in addition to done / by / when (all work orders, not
   only rounds — 4CSQL records these per step and customers use them).
4. An asset's work history, open-work check and PM schedule list include rounds that cover it.
5. The importer brings across multi-asset schedules, which assets each historical round covered, and step results.

## Non-goals

- Splitting labour, parts or downtime across assets.
- Per-asset compliance reporting (the PM compliance report stays per schedule; rounds count there already because
  their work orders carry `PmScheduleId`).
- Meter-based rounds. A meter belongs to one asset; a several-assets schedule is calendar-based only.
- Editing which assets a generated work order covers (technicians can still add free-text steps).
- Importing 4CSQL's per-step "acceptable limits" (`aLimits`) as a structured field.

## Anti-goals (would make this a failure even if it works)

- Any change in behaviour for existing single-asset and location schedules, or their work orders.
- A round turning into many work orders for the technician.
- Losing MEG's history of which assets each past round covered.

## Constraints

- ASP.NET Core 10 minimal APIs, EF Core 10 on SQL Server with migrations; React 19 + Mantine; all UI text in
  `web/src/locales/en.json` (Arabic follows later).
- Tenant isolation: every query must stay under the tenant filter; new join tables are reached through tenant-filtered
  parents (`PmSchedule`, `WorkOrder`), like the existing `PmScheduleTask`.
- Audit trail and `RowVersion` concurrency apply to schedules and work orders as today.
- API changes are additive (new optional fields), so the current web client keeps working during deployment.

## Approaches considered

| | Approach | Trade-off | Verdict |
|---|---|---|---|
| **A** | Ordered asset list on the schedule **and** on each generated work order; checklist steps carry `AssetId` | Two small join tables; asset-history queries gain one join | **Chosen** — keeps per-asset history complete even when old steps aren't imported, one work order per cycle |
| B | Only checklist steps carry `AssetId`; "covered assets" = assets with a step | Fewer tables | Rejected — a task with no steps covers nothing, and importing only result-bearing steps would erase most of MEG's coverage history |
| C | Parent work order with one child work order per asset | Reports work unchanged | Rejected — technicians get 26 work orders for one round; the opposite of what was asked |

## Design

### Data model (Core)

- `PmScheduleAsset` (new, join): `PmScheduleId`, `AssetId`, `Sequence`. Key (`PmScheduleId`, `AssetId`); index on
  `AssetId`. `PmSchedule.Assets` collection.
- `WorkOrderAsset` (new, join): `WorkOrderId`, `AssetId`, `Sequence`. Key (`WorkOrderId`, `AssetId`); index
  (`AssetId`, `WorkOrderId`). `WorkOrder.Assets` collection.
- `WorkOrderTask`: add `AssetId` (nullable FK → Asset), `Reading` (nvarchar(100)), `Remarks` (nvarchar(2000)).
- A schedule with a non-empty `Assets` list is a **round**: its `AssetId` is null. `LocationId` is optional and, if
  set, is only used to place the generated work order (see below).
- `WorkOrderAsset` is filled **only for rounds**. Single-asset work orders keep using `WorkOrder.AssetId`, so "work for
  asset X" means `AssetId == X` or `Assets` contains X. *(Design call: avoids rewriting every existing work order.)*
- `Reading` is free text, like 4CSQL's. Numeric trending of readings stays with meters. *(Design call.)*
- One migration for all of the above.

### Rules

- All listed assets must belong to the schedule's site. Duplicates are not allowed. Maximum 100 assets per list.
- Schedule type must not be `Meter` when the list is non-empty.
- Changing the list affects future work orders only.
- A covered asset stays in the work order's `Assets` (and so in its history) even if some of its steps are left
  unticked; the work order shows that asset's own progress ("5/7"). There is no separate per-asset "skipped" state.
  *(Design call.)*
- Reading, remarks and ticking steps use the existing permission for executing work orders.

### Generation (`PmGenerator`)

For a round, `CreateWorkOrderAsync` creates **one** work order:

- `AssetId` = null; `LocationId` = the schedule's `LocationId` if set, otherwise the **lowest common location** of the
  covered assets (null if they share none below the site).
- `Assets` = the listed assets that are still active (not disposed / inactive), in list order.
- Checklist: for each covered asset, in order, the chosen task's steps with `AssetId` set; `Sequence` runs continuously
  across the whole checklist.
- If no listed asset is active, skip with the reason "No active assets in the list" (shown in preview/generate results
  like the existing "Previous PM work order is still open").
- Everything else (title, due date, trade, assignees, priority, multiple-PM task choice, actual-basis waiting for the
  open work order) is unchanged.

### API

- `ScheduleInput` and schedule detail: add `AssetIds: Guid[]?` (ordered). Detail returns the assets (id, number, name).
  Schedule list filter `assetId` also matches schedules whose list contains the asset; list rows show "7 assets" for rounds.
- Work order detail: add `assets` (ordered id, number, name) and per task `assetId`, `reading`, `remarks`.
- `PUT /work-orders/{id}/tasks/{taskId}` accepts `reading` and `remarks` alongside `isDone`.
- Work order list filter `assetId` and `GET /assets/{id}/open-work` also match work orders whose `Assets` contain the asset.

### Web

- **PM schedule form**, Target section: a choice *Asset · Location · Several assets*. "Several assets" shows an ordered
  list (add with the existing `AssetPicker`, remove, move up/down) and hides the meter option.

  ```
  Target   ( ) Asset   ( ) Location   (•) Several assets
           1. CH-1110  Feeder 1 — Line 7          ↑ ↓ ✕
           2. CH-1120  Feeder 2 — Line 7          ↑ ↓ ✕
           3. CH-1130  Feeder 3 — Line 7          ↑ ↓ ✕
           [ + Add asset ]
  ```
- **Work order detail**, Tasks tab: for rounds, steps grouped under one heading per asset with its own progress
  ("CH-1110 Feeder 1 · 5/7"). Every step (rounds and normal work orders) can show/edit an optional *Reading* and
  *Remarks* inline. The header shows "Assets: 3" with links instead of the single asset field.

  ```
  ▾ CH-1110  Feeder 1                                   5/7
    ☑ Check all feeder oil connections      Reading [      ]  Remarks [            ]
    ☐ Check cooling connections             …
  ▾ CH-1120  Feeder 2                                   0/7
  ```
- **Asset detail**: the existing work-order and PM-schedule lists include rounds (they use the filters above); a small
  "Round" badge marks them.
- **PM page list**: rounds show "n assets" where the asset column is.

### Importer

- **Schedules**: `pmschedule.IsMultipleAsset = 1` with assets from `PMScheduleAssets` (ordered by `SeqNo`) become rounds.
  The flag decides: a few single-asset/location schedules also have leftover `PMScheduleAssets` rows (5 in MEG); they
  stay single. *(Design call.)*
  Lists spanning several sites (4 in MEG) are split into one schedule per site, coded `<PMNo>-<SiteCode>`; historical
  work orders link to the split matching their site.
- **Which assets each historical round covered**: distinct `workorderelements.assetno` per work order (ordered by
  `AssetSeqNo`); if a work order has no elements, the schedule's list.
- **Step results** (`workorderelements`, all PM work orders, not only rounds): import steps that have a reading,
  finding or remark (≈265,000 of 3.7 million rows in MEG), plus *all* steps of work orders that are still open, so they
  can be finished after cut-over. `readings` → `Reading`; `findings` and `Remarks` → `Remarks` (joined with " · " when
  both are present); `assetno` → `AssetId`; a step counts as done when its work order is completed.
- Volume: read `workorderelements` streamed (unbuffered) and insert in batches, clearing the change tracker between
  batches, so MEG's 3.7 million rows don't sit in memory.

## Left to the builder

- Exact UI layout, wording and empty states (within the sketches above).
- Lowest-common-location computation (the materialized `Path` makes it a string-prefix problem).
- Batch sizes and the bulk-insert technique for the importer.
- Test coverage: generation for rounds (including inactive assets and multiple PM), the new filters, tenant isolation of
  the join tables, and the importer mapping against the synthetic 4CSQL database.
