# CMX 0.18.0 — Supplier catalogue and purchase units

Roadmap release 0.18.0: the supplier catalogue, split off 0.17.0 at the user's request and released right after it. The
roadmap's later releases move up one number (calendars 0.19.0, cost centres 0.20.0, …).
Status: design for approval.

## Why

A part is often bought from several suppliers, each with their own part number, price, pack and delivery time. Today a
CMX part has one preferred supplier, one last price and one lead time, and orders are always in the part's own unit. So
the buyer retypes prices and converts "20 pieces" into "1 box of 100" by hand, and the order sent to the supplier doesn't
match their price list. In DimoMaint, picking a supplier on an order line brings in that supplier's price, discount,
currency, pack and lead time.

## Decisions taken with the user

- **Whole packs** (A): an order line is in the supplier's pack, rounded **up** to whole packs (need 20 pieces, supplier
  sells boxes of 100 → 1 box). Receiving books pieces (1 box → 100 pieces) at pack price ÷ pieces per pack.
- **Catalogue prices stay as entered** (B): a receipt never changes the catalogue price. Each catalogue row shows the
  last price paid and its date, and the buyer can copy it into the catalogue with one click.
- Everything else: "go with your picks" — the calls below marked *(my call)*.

## Goals

1. A **catalogue per part**: one row per supplier with their part number, pack, price, discount, lead time and minimum order.
2. **Order lines in packs**, filled from the catalogue, and **receiving that converts packs into pieces** (web and phone).
3. The **re-order proposal** and **ordering from purchase requests** use the catalogue: supplier, price and whole packs.

## Non-goals (0.18.0)

- Exchange-rate tables or comparing prices across currencies.
- Price breaks by quantity, price validity dates, or contract prices.
- A unit-of-measure master with conversions (parts keep one stock unit).
- Supplier portal, price-list e-mails, or electronic orders.
- Changing how stock, issues, counts or reservations work: stock stays in the part's own unit.

## Anti-goals

- **Stock, on-order or request quantities counted in packs** where pieces are meant (100 pieces booked as 1, or 1 box
  shown as 1 piece on order).
- **Existing orders, receipts or costs that change**: every existing order line is a pack of 1 in the part's unit.
- **A price that changes on its own**: a receipt never rewrites the catalogue.
- **Silent no-ops**: a refused action says why.

## Approaches considered

1. **A catalogue table beside the part (chosen)**: `SupplierParts` rows (part × supplier) with pack, price, discount,
   lead time, minimum and last price paid; order lines copy the pack (unit name + pieces per pack). Existing screens keep
   working because the part's preferred supplier stays where it is.
2. **A general units list with conversions (ERP style)**: more general, but heavy, and buyers think in "this supplier's
   box of 100", not unit tables.
3. **One pack size on the part**: simplest, but different suppliers sell different packs, which is half the point.

## What CMX has today (and keeps)

- `Part`: unit of measure, standard price, average cost, last purchase price (per unit, base currency), lead time days,
  **preferred supplier** (`PreferredSupplierId`), alternates.
- `PurchaseOrder`: one supplier, currency (the web form switches to the supplier's currency) and exchange rate, order and
  expected date. `PurchaseOrderLine`: part or free text, **unit of measure** (copied from the part), quantity, unit price,
  discount %, quantity received, store, work order.
- **Receiving** (web and phone): quantity per line in the line's unit; stock + quantity; unit cost = net line price ×
  exchange rate; the part's average cost and last purchase price follow.
- Re-order proposal and "order from requests" make draft orders grouped by supplier, priced from the part's last purchase
  price (else average cost). Request lines are in pieces; a request follows its order lines (ordered, received).
- "On order" quantities (part list and page, proposal, an inventory report) sum `Quantity − QuantityReceived` of open
  order lines.

## Design

### Catalogue rows

New `SupplierPart` per part and supplier company (Supplier role) — one row per pair:

- **Supplier's part number** (optional), **pack**: a name (e.g. "Box of 100", defaults to the part's unit) and **pieces
  per pack** (the part's own unit; more than 0, decimals allowed, e.g. a 2.5 L can of a part kept in litres; default 1).
- **Price per pack** in the company's currency (the company's currency, else the base currency), **discount %**,
  **lead time** in days (optional, else the part's), **minimum order** in packs (default 1), notes, active.
- **Last price paid** per pack (net of discount), its currency and date — set by receipts, never typed.
- The **preferred supplier** stays the part's `PreferredSupplierId` *(my call)*: the star on a catalogue row sets it, and
  choosing a preferred supplier on the part (form or import) without a row for it creates a row with a pack of 1 at the
  part's last purchase price (else standard price) and its lead time.
- "**Use last price**" copies the last price paid into the price (same currency only).
- Edited with *Manage parts* (`inventory.manage`) or *Manage suppliers* (`suppliers.manage`); seen with *View inventory*.

### Order lines in packs

- `PurchaseOrderLine` gains **pieces per unit** (`PackSize`, default 1) and **supplier part number**. The line's existing
  **unit of measure becomes the order unit**: the pack name for catalogue lines, the part's unit otherwise. Quantity, unit
  price, discount and quantity received are all in the order unit, so the order document, the e-mail to the supplier, the
  receiving screens and the phone show packs without further change.
- **Pieces** = quantity × pieces per unit; shown beside packs on order screens ("1 Box of 100 = 100 EA").
- A line's pack is **copied** when the line is filled; changing the catalogue later never changes an order.

### Filling order lines

On the order form, in the re-order proposal's "Create orders" and in "order from requests":

- A part line on an order whose supplier has an **active catalogue row** for the part takes its pack, supplier part
  number, discount and lead time, and its **price per pack when the order's currency is the company's currency** (another
  currency: price left as before, with a hint). The quantity becomes **packs = pieces needed ÷ pieces per pack, rounded up,
  at least the minimum**.
- **No catalogue row**: as today (pack of 1 in the part's unit, last purchase price else average cost).
- **Changing the supplier on a draft order** refills its part lines from the new supplier's catalogue, keeping at least
  the pieces each line had *(my call; the web form does it and shows what changed)*.
- **Expected date**: when not typed, the order date + the longest lead time of its lines (catalogue row, else the part's).

### Receiving

- Received quantity is in the line's order unit (decimals allowed, e.g. half a box) on the web and the phone.
- **Stock + received × pieces per unit**, at **unit cost = net price × exchange rate ÷ pieces per unit** (4 decimals);
  average cost and the part's last purchase price (per piece, base currency) follow as today.
- The catalogue row of the order's supplier records the **last price paid** (net price per pack, order currency) and the
  receipt date. No catalogue row: nothing more is recorded.

### Quantities that must be pieces

Every reader of order quantities that means the part's own unit multiplies by pieces per unit:

- "On order" on the parts list, the part page, the re-order proposal and the inventory report;
- a purchase request line's **ordered** and **received** amounts and the request status that follows them;
- the request's "lines to order" (what is still to order, in pieces).

### Re-order proposal and requests

- The proposal's supplier is the part's preferred supplier, else the only catalogue supplier, else the supplier the part
  was last received from *(my call; no price comparison across currencies)*. The supplier list for a line shows catalogue
  suppliers first with their pack and price.
- The suggested quantity stays in pieces (needs); each line shows what it becomes ("→ 1 Box of 100") and the value at the
  catalogue price. Creating orders converts to whole packs.
- Order from requests: request lines (pieces) become order lines in whole packs of the chosen supplier; a request line
  ordered beyond its quantity (pack rounding) counts as fully ordered.

### Screens

- **Part page › Suppliers** tab: catalogue rows (supplier, their part number, pack, price, discount, lead time, minimum,
  last paid and date, preferred star), add / edit / delete, "use last price".
- **Companies**: the company form lists the parts it supplies (read-only, links to the part).
- **Order form**: pack column with pieces, supplier part number; supplier change refills lines. **Receive** dialog and
  **phone**: quantities in packs with the pieces shown.
- **Re-order proposal**: pack and value per line, catalogue suppliers first.
- **Import**: new sheet **Supplier catalogue** (part number, supplier code, supplier part number, pack, pieces per pack,
  price, discount, lead time, minimum packs, notes) — import and "current data" export.
- Arabic for every new text.

### Data (one migration, `SupplierCatalogue`)

- New `SupplierParts` (unique part × supplier); `PurchaseOrderLines` + `PackSize` (default 1) and `SupplierPartNumber`.
- Data: a catalogue row for every part with a preferred supplier (pack of 1 in the part's unit, price = last purchase
  price else standard price, the part's lead time).

### Builder's questions, settled *(my calls)*

- **Fractions**: pieces per pack may be a decimal; order quantities are whole packs when filled but stay editable
  (decimals allowed, as today); receipts may be fractions of a pack.
- **Inactive** rows and inactive or non-Supplier companies are not offered; existing orders keep what they copied.
- **Deleting a company** with catalogue rows but no purchasing history deletes its rows; a part with orders can't be
  deleted as today.
- **Free-text lines** have no catalogue: pieces per unit 1.
- **Currency**: the catalogue price is in the company's currency; no conversion anywhere.

## Review focus (most likely to bite)

1. Every quantity meant as pieces is pieces: stock after a receipt, on order everywhere, request ordered/received and
   status, lines to order — for packs of 1 (unchanged) and of 100.
2. Cost per piece on receipt with packs, discounts and exchange rates; partial receipts of a fraction of a pack; average
   cost.
3. A catalogue change (pack, price) never changes an existing order line.
4. Supplier change on a draft order: lines refill without losing pieces; currency mismatch leaves prices alone.
5. The phone receives in packs and books pieces.

## Left to the builder

Endpoint shapes, component structure, query details, test layout, exact wording beyond what's above.
