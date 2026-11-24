# Serial-numbered parts (0.24.0)

Pumps, motors, gearboxes and other repairable spares can be tracked one by one. A part marked **serial-tracked** keeps its
stock as **units**: each piece has its own serial number, its own page and history, and knows where it is:

- **In store**: in one store, counted in that store's stock;
- **Fitted**: on an asset (a machine), as one of its components;
- **At repair**: out for repair, in no store;
- **Scrapped**: finished with.

A unit is an asset linked to its part, so it has an asset number, a label with a QR code, meters, files and work orders like any
asset. The part's quantity in each store always equals its units in that store.

Parts that aren't serial-tracked work exactly as before.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage inventory* | Mark a part serial-tracked (and switch it off), and set the unit life. |
| *Receive* (stock or purchase orders) | Receive serial parts, entering a serial number for each piece. |
| *Execute work orders* + *Issue and move stock* | Fit and remove units on a work order, on the web and on the phone. |
| *Issue and move stock* | Unit actions: transfer, send for repair, back from repair, scrap, remove without a work order, register found or fitted units. |
| *Create purchase orders* | Add a repair line for a unit under repair. |
| *Edit assets* | Edit a unit's serial number and end of life, and make an existing component a unit. |

There are no new permissions.

## Marking a part serial-tracked

- **A new part**: tick **Serial-tracked** on the part form, and optionally the **Unit life (months)**.
- **An existing part**: on the part page, **⋯ › Serial tracking**. If the part has stock, enter a serial number for each piece
  in each store (one per line); each becomes a unit, with no stock movement. It is refused while the serials don't match the
  stock exactly (for example "Enter one serial number for each piece in stock: MAIN has 2.").
- **Switching off** is possible once every unit is scrapped.
- **Unit life**: each unit's end of life is its receipt (or registration) date plus this many months. A unit's end of life can be
  changed on its asset form.

A serial-tracked part's page has a **Units** tab and offers only **Receive** by quantity: issue, return, adjust and transfer go
through its units.

## Receiving

Receiving a serial part, by hand or on a purchase order, asks for a serial number for each piece, one per line ("3 of 3
entered"). A pack counts its pieces: 1 box of 2 needs 2 serials. Serials are trimmed; they must be different from each other and
from the part's other units (ignoring case). Each serial becomes a unit in the receiving store, at the piece's cost.

A purchase order line ordered for a work order leaves the units in the store (they are not issued to the job): fit them on the
job when they go on the machine. The work order is still told the parts arrived.

The phone doesn't receive serial parts: "Receive serial parts on the web, where their serial numbers are entered."

## Fitting and removing on a work order

A work order's **Parts** tab shows the **Units** fitted to its asset (or its round's assets).

- **Fit a unit**: choose the part, the unit (in a store at the job's site) and the asset. The unit leaves the store's stock and is
  issued to the work order at its cost; it becomes a component of the asset. **Count its running hours** (on by default) makes
  the unit's own running-hours meter follow the asset's meter in hours, so the unit's hours add up across every machine it runs on.
- **Remove**: back to a store (the site's default store first), to repair, or scrap, with a condition note. Back to a store is a
  return that names no work order: the job that fitted the unit keeps its cost. The unit's hours stop following the asset.

Refusals say what to do: "This unit isn't in a store.", "Move the unit to a store at that site first.", "Choose an asset of this
work order.", "This work order is closed. Reopen it to make changes."

### On the phone

A job shows its fitted units with **Remove** (to the job site's default store, to repair, or scrap) and **Fit a unit**: scan the
unit's label (or type its serial or asset number), or pick from the units in store at the job's site. Both work offline and are
checked when they arrive; a unit fitted meanwhile by someone else is refused with a note. Serial parts can't be marked used by
quantity on the phone ("Serial part: fit a unit on the job instead.").

## Unit actions

From the part's **Units** tab or the unit's own page:

| The unit is | Actions |
| --- | --- |
| In store | **Transfer** to another store, **Send for repair** (out of stock), **Scrap** (with a reason; out of stock). |
| At repair | **Back from repair** into a store (stock +1), with an optional repair cost; **Scrap** (no stock move). |
| Fitted | **Remove** without a work order (same outcomes as on a work order). |

**Register a found unit** adds a piece found in a store (stock +1). **Register a fitted unit** records a unit already on a
machine at go-live, with no stock move. On an asset's page, **⋯ › Make this asset a unit** links an existing component with a
serial number to a serial-tracked part.

A unit's status, store and place change only through these actions: its asset form locks them, and a unit can't be deleted
("A unit can't be deleted; scrap it instead."). An asset with units fitted can't be deleted until they're removed.

## Outside repair

On a purchase order, **Add repair line** picks a unit under repair: the line is a service line for that unit (no part, quantity 1,
"Repair of ‹part› ‹serial›"). A unit can be on one open repair line at a time. Receiving the line asks for the store it comes back
to: the unit returns to stock with the line's net cost in the organization's currency as its repair cost. When the line also names
a work order, the cost goes to that work order as for any service line. Cancelling the order leaves the unit under repair.

Each unit shows its **repair count** and **total repair cost**.

## Pages and lists

- **Unit page** (its asset page): a **Unit** card with the part, serial, status, store or asset, end of life, repairs and its full
  history (each move with its work order or order).
- **Any asset's page**: a **Fitted units** card with the units on it now (since when) and before (from, to, what happened).
- **Asset list and dashboard** leave out units that aren't fitted; **Show spare units** in the list's filters shows them. Pickers
  still find them by number or serial. Fitted units show as components of their asset.
- **Alert bell**: "Units reaching end of life" (within 30 days, not scrapped) for people who view inventory; it opens the units list.
- **Labels**: tick units on the Units tab and **Print labels** (they print as asset labels).

## Counts and import

- New stock counts leave serial parts out; the count says how many it left out. Their units are checked on the part's Units tab.
  The one-step stock take refuses them.
- The **Parts** sheet has **Serial tracked** (yes/no) and **Unit life (months)**. Switching it on is refused while the part has
  stock ("Register its units on the part page first."); switching off is refused while it has units that aren't scrapped.
- The **Stock** sheet refuses an opening quantity for a serial part ("Serial parts' stock comes from their units: register them on
  the part page."). The 4CSQL importer is unchanged.

## Plain stock moves on serial parts

| Move | Answer |
| --- | --- |
| Issue or return by quantity (web or phone) | "This part is serial-tracked: fit or remove a unit on the work order." |
| Adjustment | "This part is serial-tracked: scrap a unit or register a found one on the part's units." |
| Transfer | "This part is serial-tracked: transfer units by choosing them on the part's units." |
| Receipt without serials | "Enter a serial number for each piece received." |
