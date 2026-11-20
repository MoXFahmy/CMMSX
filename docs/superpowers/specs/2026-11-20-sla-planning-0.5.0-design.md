# 0.5.0 — Response and completion targets (SLA) and planning boards

Backlog: WO-2 (targets per priority) and PM-3 (planning boards) from the DimoMaint review. One release, built in that
order: targets first, then the board, which colours its cards with them.

## Why

- **Targets.** The people waiting on maintenance (requesters, and customers where the organization runs contracts)
  need urgent work handled within hours and nothing quietly running late. Today due dates are typed by hand, carry no
  time, and "overdue" only means "the due day has passed". *(Reading proposed to the user; they did not correct it.)*
- **Board.** A supervisor plans who does what each day or week without opening work orders one by one, and sees at a
  glance who is overloaded and what has nobody on it.

Success: a supervisor sets targets once; every new request and job gets its deadlines by itself; lists, the dashboard
and the alert bell show what is about to be late or already is; the week is planned by dragging cards.

## Decisions the user made

1. **Due date and time.** Requests and work orders get a due *time*, not just a date. Targets can be hours (Urgent:
   4 h) or days (Low: 7 days).
2. **Every hour counts.** The clock runs around the clock: no working-hours calendar. An urgent job raised Thursday
   16:00 with a 4 h target is due Thursday 20:00.
3. **Response targets too.** Each priority has "respond within" and "complete within" (either may be left empty). A
   **request** is responded to when it is approved or rejected. A **work order** is responded to when work first starts:
   it moves to *In progress*, or a checklist step is ticked, or hours are logged (including from the phone, at the time
   the technician did it). A work order created from a request was already responded to when the request was approved.
4. **Board: technician and asset views, with drag.** Week and month, rows per technician with an unassigned lane, and
   rows per asset; drag a card to another day or person; upcoming PMs shown.
5. **Due time stored beside the due date** (approach A below).
6. **Board built in the app's own style** with a small drag-and-drop library (approach A below).
7. **"Needed by" stays the requester's wish.** The priority target sets a request's deadline; the requester's *Needed by*
   date is shown beside it, and the approver can choose to use it when approving. With no target for that priority,
   *Needed by* (end of that day) is the deadline, as today.
8. **Unfinished work from earlier days** sits in a **From earlier days** tray beside the board, to be dragged onto a day;
   it counts in nobody's hours until re-planned.
9. **A priority change counts from when the work was raised.** If the approver turns a Medium request into Urgent, the
   Urgent targets count from when the request was raised (an Urgent job approved 10 h later starts 6 h late), unless
   the approver sets a deadline by hand.

## Approaches considered

**Where the due time lives**
- **A — chosen:** add a due time (`DueAt`) next to today's `DueDate` and keep `DueDate` as the day of `DueAt` in the
  organization's time zone. Everything that works by date keeps working: list filters, reports, stock reservations
  (earliest due gets stock first), the PM generator, the technician app's "Today" grouping, the 4CSQL importer.
- B: turn `DueDate` into a date-time everywhere. Tidier model, but it touches every report, filter, the importer and the
  phone app at once.
- Rejected: work the due time out on the fly from the creation time and the target. It can't be overridden per job, and
  changing a target later would move old deadlines.

**How the board is built**
- **A — chosen:** a grid of days × technicians (or assets) in Mantine, with a small drag-and-drop library
  (`@dnd-kit/core` is the builder's default). Matches the app, shows planned hours against each person's day.
- B: a ready-made calendar component. FullCalendar's per-person ("resource") views need a paid licence; the free ones
  handle month-by-person poorly.

## Part 1 — Targets (WO-2)

### Settings
- Settings › Organization gets a **Targets by priority** table: for Low, Medium, High, Urgent — *Respond within* and
  *Complete within*, each a number plus unit (hours or days), or empty.
- **Default: all empty.** Nothing changes for an organization until someone sets targets (same rule as earlier
  releases). Stored in `TenantSettings` as minutes.
- Editing targets never moves deadlines already set.

### What each record carries
- **Request:** `RespondBy` (created + respond target), `DueAt` (created + complete target; with no completion target,
  the end of the *Needed by* day, as today). Responded = `DecidedAt` (exists). When approved into a work order, the work
  order's due time is the request's `DueAt` (recomputed from the request's creation time if the approver changed the
  priority), unless the approver sets one by hand: the clock started when the requester asked. The approve dialog
  pre-fills that deadline and shows *Needed by* beside it, so the approver can choose it instead.
- **Work order:**
  - `DueAt` (date-time) with `DueDate` kept as its day in the organization's time zone.
  - `StartBy` (response deadline) and `RespondedAt`: the first time anyone acts on it — it leaves *Open* (In progress,
    On hold or straight to Completed), a checklist step is ticked, or hours are logged. The time is when that was
    recorded, or the phone's time for work done offline (as the phone already sends). Hours' work dates and a typed
    actual start don't move it. *(My call: putting a job on hold or completing it is clearly a response.)* A work order
    from a request has no `StartBy`; its response is the request's decision time, shown as such.
  - Whether the due time came from a target, so that changing the **priority** recomputes target-derived deadlines (from
    the original creation time) but leaves hand-set ones alone.
- **Who sets targets on creation:** every path that creates work — the work order form, approving a request, requests
  from the full app and from the phone. **PM-generated work orders keep their schedule's due date** (their window comes
  from the PM schedule, not from a priority target), with `DueAt` at the end of that day, and no `StartBy`.
- **Date-only due dates** (typed as a date, imported from 4CSQL, PM, existing rows) mean "by the end of that day" in the
  organization's time zone. Existing rows are back-filled that way.
- A due date/time typed by hand always wins over the target.

### Late, due soon, met
- **Late (completion):** active (Open, In progress, On hold) and `DueAt` is past. This replaces today's
  "due date < today" in the work order list filter, the alert bell, the dashboard and the backlog report.
- **Late (response):** `StartBy` is past and not yet responded; for requests, `RespondBy` past and still New.
- **Due soon:** less than a quarter of the target's time is left (or, for hand-set deadlines, less than 2 hours).
  *(Builder's call on the exact rule; this is the default.)*
- The clock does **not** pause while on hold. *(My call, consistent with "every hour counts"; the hold reason is shown
  beside the deadline.)*
- **Met:** completed (or responded) at or before the deadline in force at that moment. Completion uses the work order's
  completion time as recorded today (the actual end typed in the completion dialog, or the phone's time). *(My call:
  it is the real finish time; when it was saved stays visible in the status history.)*
- **Requests:** late (response) while still New after `RespondBy`; late (completion) while still New after `DueAt`.
  Once approved, the request list shows its work order's deadline state; a rejected request has no completion.

### Where it shows
- **Work order and request lists:** a deadline column — "due in 3 h", "2 h late", or the date for far-off ones — in
  orange when due soon and red when late; a "late" filter that covers both response and completion.
- **Work order and request pages:** both deadlines with their state ("responded 10:20, 40 min inside target").
- **Dashboard:** late and due-soon counts (response and completion), linking to the filtered lists.
- **Alert bell:** its overdue counts use the new rule.
- **Technician app:** shows the due time where there is one ("due 14:00"); grouping stays by day.
- **Job card:** prints the due date and time.
- **New report "Targets met"** (Reports › Work): per priority and month, how many met their targets and the percentage.
  *(My proposal — it is how the organization will see whether targets work.)* What counts, so nothing is counted twice:
  - **Response:** requests (decision vs `RespondBy`) and work orders not from a request (`RespondedAt` vs `StartBy`).
  - **Completion:** work orders with a deadline, not generated by PM (PM compliance already has its own report).

## Part 2 — Planning board (PM-3)

- New page **Planning** under Maintenance in the menu. Viewing needs work-order view permission; dragging needs edit.
- **Range:** week (7 days) or month, with previous / today / next. Filters: site and trade.
- **By technician:** one row per active employee (filtered by site and trade), plus an **Unassigned** lane at the top.
  - A job appears on its **planned start** day, or its due day if it has no planned start, in the row of each assignee.
  - Jobs with neither date sit in an **Unscheduled** tray beside the board, which can be dragged onto a day.
  - Open jobs whose planned start (or due day, if no planned start) is before the shown range — and before today — sit
    in a **From earlier days** tray, newest first, to be dragged onto a day. They count in nobody's hours until
    re-planned.
  - Each person's day shows planned hours against capacity (assignee's planned hours, else the job's estimate split
    between assignees; capacity 8 h a day by default, an organization setting). Over capacity shows red.
  - **Dragging:** to another day sets the planned start (a warning if that is after the due time — the work order rule
    that refuses a planned start after the due date becomes a warning for board drags); to another person
    moves that assignment to them (other assignees stay); to Unassigned removes that assignment. Each drop saves at
    once, with an *Undo* toast.
- **By asset:** one row per asset with work or upcoming PMs in the range (filtered by site), same day columns; dragging
  only changes the day.
- **Cards:** number, title, priority colour, status, and the target state (late / due soon); click opens the work order
  (in a side panel or the page — builder's call).
- **Upcoming PMs** (not yet generated) appear as dashed cards from the existing PM forecast, with **Generate now** (the
  existing action); they can't be dragged.
- Completed, closed and cancelled work is not shown.
- Desktop and tablet first; on a phone the board scrolls sideways. The technician view is unchanged.

## Non-goals

- Working-hours calendars and holidays for targets (the user chose every hour).
- Pausing the clock while on hold.
- E-mail alerts for late work (the bell and dashboard show it; e-mails can come later).
- Targets per site, asset, customer or contract — one set per organization.
- Subcontractor schedule and the general calendar from DimoMaint's four boards.
- Hour-level planning (time slots within a day): the board plans by day.

## Anti-goals

- Changing anything for an organization that sets no targets, other than due dates now carrying an end-of-day time.
- Moving deadlines that already exist when a target is edited.
- Losing a technician's offline work time: responses recorded from the phone use the time the work was done.

## Constraints

- One EF migration for the release; the back-fill of `DueAt` for existing work orders and requests uses each
  organization's time zone.
- **Time zone setting:** Settings › Organization's time zone becomes a list of valid zones (it is free text today,
  default "UTC"). Back-fill: the organization's zone; if it is "UTC" or not a valid zone and all its sites share one
  valid zone, that one; otherwise UTC. *(My call.)* Live organizations should check their setting after the update.
- Existing permissions only; no new roles.
- Every new screen and string through i18next (`en.json`); tests: xUnit `SkippableFact` with `ApiFixture` for the
  server, Vitest for pure web logic.
- The technician app's offline bundle and queued changes keep working with phones on 0.4.x until they update.

## Left to the builder

- Exact "due soon" rule (default above), column formats, card layout, side panel vs page.
- Drag-and-drop library choice (default `@dnd-kit/core`).
- How the board fetches (one endpoint returning jobs, assignees, planned hours and forecast PMs for the range is the
  expected shape).
- Back-fill mechanics (in the migration or a one-off startup step), as long as it uses each organization's time zone.
