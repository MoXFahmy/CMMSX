# Supplier catalogue and purchase units (0.18.0)

A part is often bought from several suppliers, each with their own part number, pack, price and delivery time. The
**supplier catalogue** keeps one row per part and supplier, fills order lines from it in **whole packs**, and books
**pieces** into stock when a pack is received. Stock, issues, counts and reservations stay in the part's own unit.
Organizations that never fill in a catalogue see no change: every existing order line is a pack of 1 in the part's unit.

## Who may do what

| Permission | Can |
| --- | --- |
| *View inventory* (`inventory.view`) | See a part's Suppliers tab. |
| *Manage parts* (`inventory.manage`) or *Manage suppliers* (`suppliers.manage`) | Add, edit and delete catalogue rows, star the preferred supplier, use the last price. |
| *View purchasing* (`purchasing.view`) | See the parts a company supplies. |
| *Create purchase orders* (`purchasing.create`) | Fill order lines from the catalogue. |

## Catalogue rows

Part page › **Suppliers** lists one row per supplier company (Supplier role, active):

- **Supplier's part number** (optional), shown on order lines as "Their number".
- **Pack**: a name (e.g. "Box of 100"; empty = the part's unit) and **pieces per pack** in the part's own unit — more
  than 0, decimals allowed (a 2.5 L can of a part kept in litres).
- **Price per pack** (optional) in the row's **own currency**: the company's currency when the row is made, else the
  company currency of the organization. Changing the company's currency later doesn't change the row.
- **Discount %**, **lead time** in days (empty = the part's lead time), **minimum order** in packs (default 1), notes,
  active.
- **Last paid**: the gross price per pack, its discount, currency and date of the latest receipt from this supplier. It
  is set by receipts only, and a receipt **never changes the catalogue price**. **Use last price** copies the last price
  paid and its discount into the row (only when the currencies match, so the discount is never applied twice).
- The **star** marks the part's preferred supplier (the same field as on the part form). Choosing a preferred supplier on
  the part, in the form or the import, adds a row for it if there is none: a pack of 1, the part's lead time, and the
  part's last purchase price (else standard price) only when the company works in the organization's currency.

Purchasing › Companies › a company shows the **parts it supplies** with links to the parts.

## Order lines in packs

An order line's **unit** is the order unit: the pack name for catalogue lines, the part's unit otherwise. Quantity, price,
discount and quantity received are all in that unit, so the printed order, the e-mail to the supplier, the receive dialog
and the phone show packs. Each line also keeps **pieces per unit**, and screens show the pieces underneath ("= 100 EA").
A line copies its pack when it is filled; **editing the catalogue later never changes an order**.

### Filling lines

On the order form (picking a part, or changing the supplier of a draft), in the re-order proposal's *Create orders* and in
*Order from requests*:

- The supplier has an **active row** for the part: the line takes its pack, part number, discount and lead time, and its
  price per pack **when the row's currency is the order's currency**. Otherwise it still takes the pack (the supplier
  ships packs) at today's price per piece × pieces per pack, marked "Not a catalogue price".
- **Packs = pieces needed ÷ pieces per pack, rounded up, at least the minimum.** Need 20, sold in boxes of 100 → 1 box;
  need 250 → 3 boxes.
- **No row**: as before — the part's unit, last purchase price else average cost.
- Changing the supplier on a draft order refills its part lines from the new supplier's catalogue, keeping at least the
  pieces each line had.
- **Expected date**, when not typed: the order date + the longest lead time of the lines (row, else the part's).

## Receiving and costs

Received quantities are in the order unit, decimals allowed (half a box), on the web and the phone.

- **Stock + received × pieces per unit**: 1 box of 100 books 100 pieces; half a box books 50.
- **Cost per piece = net price per pack × exchange rate ÷ pieces per pack** (4 decimals). A box of 100 at 45 less 2% is
  44.10, so 0.441 a piece; a box of 10 at 45 EUR less 2% at 48.5 is 213.885 a piece. The part's average cost and last
  purchase price follow as before, per piece in the organization's currency.
- The supplier's row records the last price paid. When the line's pack differs from the row's (the row was edited since),
  the price is converted through pieces to the row's pack.

## Quantities in pieces

Everything that means the part's own unit counts pieces: **on order** on the parts list, the part page, the re-order
proposal and the inventory report, and the **ordered** and **received** amounts on a purchase request. A request is still
"ordered" when it is on a live order line, whatever the quantity (as in 0.16.0).

## Re-order proposal

- The suggested supplier is the part's preferred supplier, else its only catalogue supplier, else the supplier it was last
  received from. The supplier list shows catalogue suppliers first, with their pack and price (★ for the preferred one).
- The quantity stays in pieces; each line shows what it becomes ("→ 1 Box of 100") and its value at the catalogue price.
  *Create orders* turns it into whole packs.

## Currencies

Each row keeps its own currency, and its price is used only on orders in that currency. There is no exchange-rate table
and no price comparison across currencies.

## Import

New sheet **Supplier catalogue** (Settings › Import): part number*, supplier code*, supplier's part number, pack, pieces
per pack, price per pack, currency, discount %, lead time (days), minimum packs, notes. It adds and updates rows (one per
part and supplier) and never removes them; the "current data" export includes it. A preferred supplier code on the Parts
sheet adds a row as above. The 4CSQL migration adds a row (pack of 1, no price) for every part–supplier pair it reads.

## Upgrading

The migration adds a catalogue row for every part with a preferred supplier (pack of 1 in the part's unit, the part's lead
time, the company's currency; price as for a newly chosen preferred supplier). Every existing order line becomes a pack of
1, so existing orders, receipts and costs don't change.
