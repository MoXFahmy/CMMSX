# CMX 0.12.0 — Templates, triggers and group work

Roadmap release 0.12.0 (DimoMaint roadmap, version 5). The user said "go ahead" with the approach and calls proposed in
the conversation; every call below marked **(call)** is the builder's, recorded so it can be checked.

## Why

Large sites repeat the same job on many assets. A change to the job should be made once; one PM sometimes needs both a
calendar and a meter; many PMs share the same dates (a hotel's low-season weeks); a job on twelve assets should be
manageable as one piece of work without losing each asset's own history.

## Goals

1. **Work templates.** Reusable jobs used by PMs and by work orders created by hand.
2. **Shared PM calendars and several triggers per PM**, whichever comes first.
3. **PM recipients**, carried to the work orders the PM generates.
4. **Per-PM generation rules** that override the organization's.
5. **A different template per asset** on a multi-asset PM.
6. **Meter propagation** from a parent meter to linked meters.
7. **Occurrence list** on each PM: the next 24, each can be generated now.
8. **Group work orders**: one child work order per asset under a group, managed from the group.
9. **Even spread** of a multi-asset PM's assets over its period.
10. **Checklists on an asset or asset category**, added to every new work order on it.

## Non-goals (0.12.0)

- Planned hours per trade on templates (CMX plans hours per person; a template keeps one trade and estimated hours).
- Editing recipients on an individual work order (shown read-only; edited on the PM or template).
- Step types, measurement points, follow-up work (0.13.0).
- Import/export of calendars, triggers, per-PM rule overrides, recipients, meter links and asset checklists (screens only
  in 0.12.0; the import keeps working unchanged for everything it reads today).

## Anti-goals

- A group counted as extra work anywhere (lists, dashboard, KPIs, reports, overdue e-mails, planning board, phone).
- Existing PMs changing their next dates when the server is upgraded.
- Silent no-ops: an action that skips some children, triggers or meters says which and why.
- Follow-up work appearing later from a background job when it can be created in the same save.

## Constraints (unchanged house rules)

- Arabic for every new text (web locales and server messages through the Translator catalogue), right to left.
- The same rules on screens and in the import where the import reads a field.
- Tests first (xUnit integration tests against SQL Server, vitest for web logic); everything built in Docker.
- Site and branch access from 0.9.0 applies to every new list, picker and action.
- Times stored in UTC, shown in the organization's zone; no form defaults to a time later than now.

## Approaches considered

**Templates.** (a) Grow PM tasks into work templates — one list, renamed *Work templates*, every existing PM task becomes
a template; (b) a new Templates list wrapping PM tasks. **Chosen: (a)** — PMs already read their task when they generate,
so "PMs follow template changes" is already true, nothing to migrate, one list to explain. Cost: the words "PM task"
leave the screens; the Excel sheet is renamed (the old name still imports).

**Group parent.** (a) The parent is a work order with an "is group" flag; (b) the parent is its own record (a *work
group*) and its children are ordinary work orders. **Chosen: (b) (call)** — about 20 places count or list work orders
(dashboard, reports, planning board, overdue e-mails, alerts, phone bundle, people/team pages); a parent work order would
have to be excluded in every one, and one miss double-counts work. A work group is invisible to all of them by
construction.

**Several triggers.** (a) Move every PM's rule into a list of trigger rows; (b) keep the PM's own rule as its first
trigger and add optional extra triggers (shared calendars, extra meters). **Chosen: (b) (call)** — existing PMs, the
import and every screen keep working unchanged and keep their dates; extra triggers are additive.

## Design

### 1. Work templates (PM tasks grown)

- Entity stays `PmTask` in code and `/api/pm-tasks` in the API; screens and docs say *Work template(s)*.
- New fields: `Title` (work order title; empty = the template's name), `WorkType` (default Preventive), `Priority`
  (nullable: the PM's or form's), `RecipientIds` (users), `ExpectedStopHours` (decimal, nullable, shown on the work order).
  Existing: code, name, description, trade, estimated hours, safety notes, steps, parts, active.
- **Documents:** attachments on the template (`EntityType = "PmTask"`, same permissions as PM view/manage). A work order
  linked to a template (its `PmTaskId`) shows them read-only under "From the template" — not copied, so they follow
  changes. **(call)**
- **New work order from a template:** the work order form gets a *Template* picker that fills title, description (with
  safety notes), type, priority, trade, estimated hours and steps; on save (`TemplateId` in the input) the server links
  `PmTaskId`, adds the template's planned parts (default store per part, as PM generation does) and its recipients.
- **Where used:** the template page lists the PMs using it (routine, every-Nth, threshold low/high, per-asset overrides),
  the asset checklists that use it, and its count of open work orders.
- **Unlink (make a copy for this PM):** on a PM, copies the routine template to a new template (code `<code>-<pm code>`,
  made unique) and points the PM at the copy, so later edits to the shared template no longer reach it.
- The template list moves from the PM page's "Tasks" tab to a *Work templates* tab with a full-page editor
  (`/pm/templates/:id`) because it now has documents and where-used.

### 2. Shared calendars and several triggers

- **Calendar** (new entity, UI *PM calendars* tab): code, name, kind **Interval** (every N days/weeks/months/years from an
  anchor date) or **Dates** (a list of chosen dates, up to 500), active.
- A PM can use **one or more calendars** (`PmScheduleCalendar` link rows). New schedule type **Calendar** = the PM's dates
  come only from its calendars. Any other calendar-using type (fixed interval, nth weekday, day of month, meter with
  time) may also add calendars as extra triggers.
- A PM can add **extra meters** (`PmScheduleMeter`: meter, interval, next due reading) on top of its own meter.
- **Whichever comes first:** the next date is the earliest of the own rule's next date and each calendar's first date after
  the last occurrence. A meter trigger fires when its reading reaches its next due reading. When any trigger generates
  work, every incremental meter trigger restarts at its current reading + interval, the own time rule restarts from the
  generation day (as "meter or time" does today), and calendar dates stay fixed (the next calendar date after today).
- Calendars need the **Scheduled** basis (validation). An empty or inactive calendar contributes no dates; a PM whose
  only triggers give no future date shows "No next date" instead of generating.
- One pure function computes the next date from the own rule and calendars, used by generation, the forecast, the
  occurrence list and spread; unit-tested without a database.

### 3. PM recipients

- `PmSchedule.RecipientIds` (users who can see the PM's site). On generation the work order's new `RecipientIds` =
  template recipients ∪ PM recipients (deduplicated; inactive users dropped).
- E-mail: the grid column *Request recipients* becomes *Recipients* (request recipients, plus the work order's own
  recipients). The *PM work generated* event (`pm.assigned`) allows it and has it on by default; work order events
  that already allow request recipients (completed, cancelled, overdue, refused) reach the work order's recipients too.
  Access rules from 0.9.0 still filter every recipient.

### 4. Per-PM generation rules

- Nullable overrides on the PM: skipped asset statuses, non-working days, "wait for the previous work order". Null = the
  organization's setting. The PM form shows "Organization's rule (…)" until changed, with a *Use the organization's*
  reset.

### 5. A different template per asset

- `PmScheduleAsset.PmTaskId` (nullable). On a round, each covered asset's steps and parts come from its own template, or
  from the PM's template for this generation when empty. An every-Nth generation uses the Nth template for every asset.
- Saving a multi-asset PM **warns** (does not refuse) when any template it would use has no steps, naming the assets.

### 6. Meter propagation

- `Meter.SourceMeterId` (another meter with the same unit). A reading recorded on a source meter, higher than its
  previous reading, adds the **same increase** to each linked meter as its own reading (source *Propagated*, note "From
  <code>", same time and work order) and runs that meter's PMs. **(call: copy the increase, not the value — linked
  meters keep their own starting values, e.g. a replaced gearbox.)** Decreases (resets) are not propagated.
- Chains propagate (vehicle → engine → pump); a link that would make a loop is refused on save.
- All readings go through the existing `RecordReadingAsync` (web, phone, work order completion); the import's reading
  path calls it too or is left as is if it already does (builder checks).

### 7. Occurrence list

- `GET /api/pm-schedules/{id}/occurrences`: the next 24 date occurrences (target start, due date, the template that
  generation would use, status *Planned* or *Generated* with the work order number/status when a work order already
  exists for that date). Meter PMs: the next 24 projected readings with an estimated date from the last 90 days of
  readings (none when there is too little history).
- *Generate this one now* on a date row (`POST …/occurrences/generate { date }`): creates that occurrence's work order (or
  group) with `PmDueDate` = that date; if it is the next one, the PM moves on as *Generate now* does today; a later one is
  generated out of order and is skipped as "Already generated" when its turn comes. The forecast page links to it.

### 8. Group work orders

- **Per multi-asset PM:** *Round* (one work order with a checklist per asset, as today) or *Group*. Changeable until the
  PM generates its first work order; then fixed (message on change).
- **WorkOrderGroup** (new): number (own sequence, prefix "GRP-" **(call)**), title, site, PM, template, `PmDueDate`, due
  date/time, planned start, created. **WorkOrder.GroupId** links each child. A group generation creates the group and one
  child per covered asset (each through the normal work order creation, so numbering, deadlines, e-mails, asset
  checklists and access all apply), each with its asset's template steps and parts.
- **Plan to children:** per PM, *Copy to each* (each child gets the template's hours and parts) or *Split evenly* (hours
  and part quantities divided by the number of children, rounded to 2 and 3 decimals).
- **Group status** is computed from the children (no stored status): Open, In progress, Completed (all done: completed,
  closed or cancelled with at least one completed), Closed, Cancelled.
- **Group page** (`/work-groups/:id`, list at `/work-groups`): children with asset, status, assignees, steps done/total,
  hours and cost; totals (labour, parts, other) rolled up; progress bar.
- **Assign to children** (all or selected): adds the chosen technicians/contractors to each child; children that are
  finished or already have hours logged are left alone and listed in the answer. **(call: "already worked on" = finished
  or hours logged.)**
- **Report for all:** completion notes, cause, prevention and failure code entered once, written to children not yet
  finished; fields a child already has are kept. **(call)**
- **Change the status of all** (or selected): runs each child's normal status change with its rules (required comments,
  cancel reasons, completion rules); the answer lists children that refused and why.
- **Due date flows down:** changing the group's due date updates every unfinished child; a child's due date can't be
  edited while it is in a group ("The due date comes from group GRP-00012.").
- **Comments on the group** show on each child (web and phone, read-only, marked "From the group"). Not copied.
- Children appear everywhere like any work order (lists show "Group GRP-00012" on them, filter by group); the group itself
  appears only on its own pages and on the PM.

### 9. Even spread

- On a multi-asset PM with a fixed interval and the Scheduled basis, and no calendars or meters: *Spread over N parts*
  (2 … number of assets). Each period is split into N occurrences; occurrence k of a period starts at period start +
  ⌊k × days in period ÷ N⌋ and covers the k-th block of assets in list order (blocks as equal as possible). Each
  occurrence is a round or a group for its block. Every-Nth templates count periods, not parts. **(call: "parts" instead
  of always one asset per occurrence — a yearly PM on 12 assets with 12 parts gives one asset a month.)**

### 10. Checklists on an asset or category

- **AssetChecklist** (new): a template attached to an asset or to an asset category. Every new work order (any path:
  form, request approval, PM, phone, group child) on that asset gets the steps of the asset's and its category's
  attached templates appended after its own steps; a template the work order already uses is not added twice. On a
  round, they are added per covered asset with that asset's id. Existing work orders are not changed.
- Managed on the asset page (*Checklists* panel) and on the category (Settings › Asset categories).

### Phone

- Group children are ordinary jobs; the job shows "Group GRP-00012" and the group's comments (read-only). A group is never
  in the phone's download. Template documents appear with the job's files when online (read-only link list).

### Import

- The *PM tasks* sheet is called *Work templates* (the old name still imports) with new optional columns Title, Work type,
  Priority, Expected stop hours. The *PM schedules* sheet gets an optional *Multi-asset mode* column (Round / Group),
  refused once the PM has generated work, exactly as on the screen.

## Left to the builder

- Exact migration names, index choices, page layout details within the existing Mantine patterns.
- Whether the work group detail lives in one endpoint or a few; the occurrence list's page size beyond 24.
- Arabic wording for new texts (consistent with existing terminology in `ar.json` and the server catalogue).
