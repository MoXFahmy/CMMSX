# CMX 0.8.1 — Downtime polish (short design)

## Why

0.8.0 shipped downtime and spare parts. Its final review left a few gaps. The biggest is on the phone: technicians on
the floor often see a machine stop first, but the technician app can't say "the asset is down". Its completion form
also still asks for downtime hours that the server replaces when a stop is linked.

## Decisions taken with the user

- **Scope:** the four items offered as "0.8.1 polish" (phone request box, phone completion note, cheap review fixes,
  the asset-change question).
- **Asset change: move the stop.** This changes the 0.8.0 design's "stays on the old asset". When a work order's
  asset changes, the stop *this work order started* moves to the new asset and keeps its original start:
  - The stop moves only when it is open, was started by this work order, and no request or other work order points
    at it.
  - If the new asset is already down, the work order joins that stop, and its own stop is removed (it was ticked on
    the wrong asset).
  - Any other stop (from a request, shared, ended, or entered by hand) stays where it is, as in 0.8.0.

## Approaches

1. **Move only the work order's own stop (chosen).** It follows the "ticked by mistake" rule 0.8.0 already uses
   for unticking. A stop that other records point at, or that came from a request, describes what someone else
   reported, so it isn't moved.
2. **Move any linked stop.** Rejected: it would move a requester's report, and other records' stops, without them
   knowing.
3. **Keep 0.8.0's behaviour.** Rejected by the user: the wrong machine stays down until someone presses Back running.

## What changes

- **Phone request form** (opened after scanning an asset or location):
  - a "The asset is down" box, shown when the request is about an asset;
  - it goes through the offline queue, and the stop starts at the time the request was made on the phone;
  - older app versions that don't send the box create requests as before.
- **Phone completion form:** when the job has a stop, a note "from the asset's downtime log" replaces the
  "Asset was down" switch and hours. The phone's job data carries the stop's start.
- **Work order form hint:** it says whether the stop will move with the asset or stay on the old one.
- **Cheap fixes from the 0.8.0 review:**
  - running hours are rounded to 2 decimals on the screen and in the import, so a re-import of 8.333 shows no change;
  - a manual stop without a start is refused ("Choose when the asset stopped.");
  - the asset parts rules shared by the screen and the import refuse more than 500 parts on one asset and a
    quantity above 1,000,000,000;
  - the Downtime log report says when it shows only the first 5,000 stops.

## Non-goals

- "Back running" offline (it stays online only: the stop ends at the moment it is pressed).
- The other deferred 0.8.0 minors (race messages, reopening a stop, the Parts tab picker's inventory permission…).

## Constraints

- No new package and no migration. Arabic for every new text.
- Older phone app versions keep working against the new server.

## Left to the builder

Exact placement of the box and note; message wording beyond the ones above.
