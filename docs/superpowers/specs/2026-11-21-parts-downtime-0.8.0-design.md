# CMX 0.8.0 — Spare parts per asset and downtime tracking (AS-2, AS-4)

## Why

Two items from the DimoMaint comparison that maintenance teams use every day:

- **Spare parts per asset.** The planner or technician should see straight away which parts a machine takes. Today a
  part only knows its alternates, so people search the whole parts list each time.
- **Downtime.** Today downtime is a number of hours typed when a work order is completed, and the reliability report
  assumes every asset runs 24 hours a day, every day. A machine that runs one shift looks far more available than it
  is, and stops that never become a work order are not recorded at all.

## Decisions taken with the user

- **Downtime comes from requests and work orders, plus a manual log.** A request or work order can say the asset is
  down; that starts a stop at that moment. The stop ends when the work order is completed, or earlier with "Back
  running". People can also log a stop by hand.
- The rest of the shape below was proposed and accepted as a whole ("Looks right"):
  - running time per asset;
  - a Downtime tab;
  - the parts list with where-used and copy-from;
  - the picker showing the asset's parts first;
  - an import sheet for asset parts.

## Approaches considered

1. **Records of their own (chosen).** Each stop is a record: asset, start, end, reason, and the request or work order
   it came from. Each asset's parts list is a small list of its own. Running time is two fields on the asset. Reports,
   the picker and the import read from these.
2. **A "down since" mark on the asset.** It keeps no history of stops, so there is no manual log, no reasons, and no
   availability over a period.
3. **Stops worked out from status changes ("Under repair") in the change history.** That status also means storage and
   disposal, it carries no reason, and reading the history back is fragile.

## Goals

- A stop's real start time is kept, even when the work order comes hours later.
- Availability counts only the time an asset was meant to be running.
- When planning or issuing parts on a work order, the asset's own parts come first.
- Old data keeps working: work orders completed before 0.8.0 still count, using their typed downtime hours.

## Non-goals (0.8.0)

- Shift calendars (start and end times per shift), holidays, and production counts (OEE).
- Downtime categories as an editable list. The reason is free text in 0.8.0.
- Parts lists inherited from a parent asset or a category.
- Turning a stop into a work order automatically.

## Anti-goals

- The same minutes counted twice (two requests about one stop, or a manual stop overlapping a request's).
- A stop that silently stays open forever. An open stop is always visible: on the asset, on its work order, and as
  a count on the dashboard.
- Changing how the 0.7.x import behaves for the sheets it already has.

## Downtime

**A stop** has: asset; start; end (empty while the asset is down); reason (free text, up to 500 characters); where it
came from (a request, a work order, or manual); and who logged it.

**Starting a stop**
- Requests (new and edit) and work orders (new and edit) get a tick box: "The asset is down". It shows only when an
  asset is chosen.
- Ticking it starts a stop at the record's creation time, or at the moment it is ticked on an edit. The record's title
  becomes the stop's reason.
- **One open stop per asset.** If the asset already has an open stop, the request or work order is linked to that stop
  instead of starting another.
- Approving a request into a work order links the request's stop to the new work order.
- *(Builder check, my call)* Unticking the box on an edit unlinks the record. If the stop is still open, came from this
  record and has no other link, it is deleted, because it was ticked by mistake. Otherwise the stop stays as it is.
- *(My call)* Changing a work order's or request's asset unlinks its stop. The stop stays on the old asset, still open
  and visible there.

**Ending a stop**
- Completing a work order ends its linked open stop, at the completion time.
- "Back running" ends the asset's open stop now. It is on the asset page, on the work order page, and on the
  technician app's job page.
- Rejecting or cancelling a request, or cancelling a work order, leaves the stop open. The asset still shows as down
  until someone presses "Back running", so a real stop is never ended by paperwork.

**Manual stops**
- On the asset's Downtime tab, people with `assets.edit` can add a stop (start, end or "still down", reason) and change
  the start, end and reason of any stop. The change history records each edit.
- Rules: the end is after the start; the start is not in the future; a new stop may not overlap another stop of the
  same asset.

**The completion fields on a work order**
- When a work order has a linked stop, "Asset was down" is ticked and "Downtime (h)" shows the stop's duration: from
  its start to its end, or to the completion time when the completion ends it. These become read-only on the completion form, with a note "from the asset's downtime log".
- Without a linked stop, the fields work as today.

**Running time per asset**
- Two new fields: "Runs … hours a day" (0.5–24, default 24) and "Runs on" (weekdays, default all seven).
- Both are on the asset form, in the asset import (two new columns), and in the export.

**Availability** for a period (the reliability report and the asset's Downtime tab):
- **Planned time:** for each running day in the period (in the organization's time zone), the asset's hours a day.
- **Downtime:** for each running day, the stopped time that falls on that day, capped at that day's planned hours.
  Overlapping stops are merged first, so no minute counts twice.
- **Availability** = (planned − downtime) ÷ planned. MTBF uses the same planned time.
- *(My call)* **A breakdown**, for the stop counts, MTBF and MTTR, is a stop that starts in the period, plus each
  old-data work order. MTTR is the average downtime per breakdown.
- **Old data:**
  - work orders before 0.8.0 (no linked stop, "Asset was down" ticked) add their typed hours;
  - those hours fall on the work order's completion day;
  - they are capped the same way.

**Where it shows**
- **Asset page, Downtime tab:**
  - "Down since … (duration)" with "Back running" when the asset is down;
  - the running time, "Add a stop", and the list of stops, newest first;
  - each stop shows its source, linked to the request or work order;
  - availability for the last 30 days.
- **Asset list:** a "Down" badge; filter "Down now".
- **Dashboard:** "Assets down now" tile with the count, linking to the filtered list. *(My call)* It shows to people
  who can see assets.
- **Reports:**
  - the reliability report uses the new rules (per asset: stops, downtime, MTTR, MTBF, availability, downtime cost);
  - a new "Downtime log" report lists the stops in a period (asset, start, end, duration within running time, reason,
    source).

## Spare parts per asset

- **The list:** each asset has a Parts list. Each line has a part, a quantity (above zero, three decimals) and a note
  (up to 200 characters, for example "drive-end bearing"). A part appears at most once per asset.
- **Editing:** on the asset page, a "Parts" tab, for people with `assets.edit`.
- **Copy from another asset:** adds the other asset's lines. A part already on the list keeps its quantity and note.
- **Where-used:** the part page gets a "Used on" list (asset number, name, quantity, note), linked to the assets.
- **Picker:** wherever parts are planned or issued for a work order on an asset (planned parts, Issue), the part picker
  lists "Parts of this asset" first, with the quantity from the list, then the other matches. Searching filters both
  groups.
- **Deletions:**
  - deleting a part that is on an asset's list is refused, as for parts used elsewhere ("used on assets");
  - deleting an asset deletes its list (soft delete, as the asset).

## Import and export (0.7.x)

- A new sheet, **Asset parts** (Arabic «قطع الأصول»), placed after Parts. Columns: Asset number*, Part number*,
  Quantity*, Note.
- It works like PM parts:
  - the rows given for an asset become that asset's whole list;
  - an asset with a problem row keeps its list ("Not saved: fix row N of this asset first");
  - removed parts show the asset as changed, with its old and new list;
  - the export never splits an asset's list.
- The Assets sheet gains "Runs hours a day" and "Runs on" (weekdays, comma-separated, English or Arabic names).

## Constraints

- No new package. Arabic texts for everything new (web, server messages, reports).
- The screens' rules stay shared between the screens and the import, as in 0.7.0.
- Database: two new tables and two asset columns, in one migration. Existing rows get the defaults (24 h, all days).
- Times are stored in UTC and shown, and split into days, in the organization's time zone.

## Tests the builder adds

- **Stops:**
  - a request ticked "down" starts a stop at its creation time, and approving it links the work order;
  - completing the work order ends the stop;
  - "Back running" ends it early;
  - one open stop per asset (a second "down" request links to it);
  - reject and cancel leave it open.
- **Manual stops:** overlap refused; end after start; edits recorded in history.
- **Availability:**
  - 16 h a day, Sunday–Thursday, a stop across a weekend counts only the running days;
  - overlapping stops count once;
  - old typed hours still count.
- **Completion fields:** filled from the stop and read-only.
- **Parts:**
  - list add, edit and remove;
  - copy from another asset;
  - where-used;
  - the picker returns the asset's parts first;
  - deleting a listed part is refused.
- **Import:**
  - the Asset parts sheet (replace per asset, problem row keeps the list, removed parts shown);
  - running-time columns;
  - export round trip with no changes.
- **Technician app:** "Back running" on the job page.

## Left to the builder

- Endpoint names and shapes.
- Where exactly the tick box and buttons sit on each form.
- The badge and tile styling.
