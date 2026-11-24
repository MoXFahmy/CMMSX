# Print layouts (0.23.0)

Roadmap item from the DimoMaint comparison: editable job card, purchase order, request and label layouts in Arabic and
English. Choice picked in the conversation: **A — layouts apply wherever CMX prints in the browser, and to the body of the
purchase order e-mail sent to suppliers; no server-made PDF.**

## Intent

Every customer wants the job card and the purchase order to look like their own form: their logo, their fields, their
wording, sometimes a different paper for each work type or site. Today CMX prints one fixed job card, one fixed purchase
order and one fixed label sheet, and a request has no printout at all. An administrator should be able to shape these papers
by ticking and ordering things, without a report designer.

Success looks like: a hotel's job card shows only location, asset, checklist and a signature box on A5; a factory's shows parts
and labour with its ISO form number in the footer; the purchase order carries the company's terms in Arabic and English and the
supplier gets the same content by e-mail; asset labels come out on the sticker sheets or label printer the customer already has.

## Goals

1. **Layouts for four papers:** the job card, the purchase order, a new request receipt, and labels.
2. **What a layout controls:** which blocks show and in what order; which fields each block shows; header text, footer text
   and terms in English and Arabic; paper size; logo on or off. For labels: the label size and which details show.
3. **Several layouts per paper,** chosen automatically by site and by type (work type for the job card, order type for the
   purchase order, request type for the receipt, record kind for labels), with a default for the organization, and a
   "Print with another layout" choice when printing.
4. **A preview with a real record** while editing, before saving.
5. **The purchase order e-mail** to a supplier uses the purchase order's layout: the same header, fields, lines, terms and
   footer in the e-mail body.
6. **Nothing changes until someone sets up a layout:** with no layout, every paper prints exactly as today.

## Non-goals

- A free-form designer, formulas, data sources or custom HTML.
- Server-made PDFs or PDF attachments (choice B). "Save as PDF" stays the browser's print dialog.
- A per-layout logo; layouts use the organization's logo (Settings › Organization) or none.
- Printing in a language other than the person's; layouts carry English and Arabic texts and show the one for the language
  being printed (see Language).
- New papers beyond the four (reports, stock-take sheets, work groups keep their prints).
- The phone; it doesn't print.

## Anti-goals

- **A changed paper on upgrade.** The built-in layout of each paper is today's print, block for block and field for field.
- **A layout that loses information silently.** Hiding a block or field is the administrator's choice in the editor, which
  shows what will print; nothing else hides data.
- **Two looks for one order.** The purchase order printed from CMX and the one e-mailed to the supplier come from the same
  layout and show the same fields, terms and texts.
- **Unsafe text.** Header, footer and terms are plain text (line breaks kept); they are never read as HTML, in the browser or
  in the e-mail.

## Approaches considered

- **A. One layout, printed from the browser, also shaping the e-mail body** (picked). One renderer per paper (the web print
  components already there), no new dependencies, Medium size.
- **B. PDFs made on the server** from the same layout, attached to the supplier e-mail. Needs a PDF library (QuestPDF is free
  only under US$1M yearly revenue) and a second drawing of every layout kept in step with the web one. Large. Left for later.
- **For the editor:** blocks and fields with tick boxes and up/down ordering (picked, as the roadmap set: for administrators,
  not report developers), rather than editable HTML templates or a drag-and-drop canvas, which need design skills and break on
  Arabic right-to-left.

## Design

### 1. The papers, their blocks and fields

Each paper has a fixed catalogue of blocks, each with a fixed list of fields (where a block has fields). The **built-in
layout** of each paper is today's print, and where this section and today's print differ, today's print wins: the blocks below in
this order, the fields today's print shows, A4, the logo where today's print has one (the job card, not the purchase order), no
header, footer or terms text. Fields marked new start off in the built-in layout. (My call, from the builder check.) Block and field keys are fixed in Core and mirrored on the web; tests on both sides pin them to one list (as for the
menu modules in 0.22.0).

**Job card** (`job-card`):

| Block | Fields |
|---|---|
| `header` | logo, organization name, site, paper title and number, printed date |
| `details` | status, priority, work type, trade, team, site, location, asset, serial number, planned start, due, assignees, requested by, cost centre |
| `description` | — |
| `technician-notes` | — (asset and location notes for the technician) |
| `warranty` | — (shown only when the asset is under warranty, as today) |
| `asset-fields` | the asset's custom fields, each chosen by key (none chosen by default; this block is new and off in the built-in layout) |
| `checklist` | — (the tasks and checklist steps as today) |
| `contractors` | — |
| `labour` | — |
| `parts` | — |
| `completion` | completion notes, cause notes, prevention notes, symptom, cause, remedy, asset was down, downtime hours |
| `signatures` | — |
| `terms` | — (the layout's terms text; new, empty by default) |

Team and cost centre are new details fields, off in the built-in layout (today's card doesn't print them).

**Purchase order** (`purchase-order`):

| Block | Fields |
|---|---|
| `header` | logo, organization name, site, delivery address, paper title and number, order date, expected date |
| `supplier` | name, contact, address, e-mail, phone, tax number |
| `details` | payment terms, quote reference, shipping method, requested by, currency, order type |
| `lines` | line number, part number, description, supplier's part number, quantity, unit, unit price, discount, line total |
| `totals` | subtotal, tax, shipping, total |
| `notes` | — |
| `approvals` | — (the signatures table, or the "approved by" line, as today) |
| `terms` | — |

Order type is a new details field, off in the built-in layout. Purchase orders have no cost centre of their own (0.20.0 left
them on the lines' work orders), so there is no cost-centre field. (My call.) Description and line total can't be hidden in
`lines`, and total can't be hidden in `totals` (an order without them is not an order); the editor shows them ticked and
disabled.

**Prices when the discount is hidden:** with the discount column shown, unit price is the price before discount, as today's
print; with it hidden, unit price is the price after discount, so quantity × unit price = line total on any paper the supplier
sees (today's e-mail already does this). (My call.)

**Request receipt** (`request`), new: printed from the request page ("Print" next to the request's actions) for the requester
or the noticeboard.

| Block | Fields |
|---|---|
| `header` | logo, organization name, site, paper title and number, printed date |
| `details` | status, submitted, requester, phone, request type, priority, work type, site, location, asset, required by, symptom |
| `description` | — |
| `access-windows` | — |
| `progress` | the linked work order's number, status and due date when there is one |
| `terms` | — (free text such as "Keep this receipt; quote its number when you call") |

**Labels** (`labels`): a label always carries its QR code. Fields: number, name, `detail` (today's third line: the location path
for assets, the parent path for locations, the manufacturer or group for parts) and site (new, off in the built-in layout; assets
and locations only). Label sizes (`labelSize`):

| Key | Paper | Labels |
|---|---|---|
| `a4-3x8` | A4 sheet, 3 × 8, 70 × 37 mm (today's sheet, built-in) | 24 |
| `a4-3x7` | A4 sheet, 3 × 7, 63.5 × 38.1 mm | 21 |
| `a4-2x7` | A4 sheet, 2 × 7, 99.1 × 38.1 mm | 14 |
| `roll-50x25` | one 50 × 25 mm label per page (label printers) | 1 |
| `roll-100x50` | one 100 × 50 mm label per page | 1 |

Labels have no header, footer, terms or paper size of their own; the size sets the paper.

### 2. The layout

`PrintLayout` (tenant entity, soft-deleted like other settings rows): paper (`job-card` | `purchase-order` | `request` |
`labels`), name (up to 60, unique per paper), `IsDefault`, optional `SiteId`, optional `KindKey` (work type name for the job
card; order type id for the purchase order; request type id for the receipt; `asset` | `location` | `part` for labels), paper
size (`A4` | `A5` | `Letter`; not for labels), `ShowLogo`, header, footer and terms text in English and Arabic (header and
footer up to 500 characters each, terms up to 4,000), and `Blocks`: the ordered blocks with shown or hidden and the shown fields,
stored as JSON. Labels store `LabelSize` and the shown fields instead of blocks.

- Up to 20 layouts per paper. The default can't have a site or kind (it is what applies when no other layout matches); at most
  one default per paper. Layouts with no site, no kind and not the default are allowed, as many as wanted: they never apply by
  themselves and are there to pick by hand ("Short A5 card", "Contractor card"). (My call.)
- Validation: known paper, block keys and field keys for that paper (asset custom-field keys must exist for assets); the
  fixed fields above can't be hidden; known site (and one the person can see), work type, order type, request type, label kind,
  paper size and label size; text lengths.
- Plain text only: stored as typed, shown with line breaks, escaped everywhere.

**Which layout prints** for a record, most specific first, the same order the approval levels use:

1. a layout with the record's site and the record's kind;
2. a layout with the record's site and no kind;
3. a layout with no site and the record's kind;
4. the paper's default layout;
5. the built-in layout.

Two layouts with the same site and kind aren't allowed when at least one of the two is set (layouts with neither are the
default or hand-picked ones above). A label sheet is always for one kind of record (assets, locations or parts, as today); when
its records are at several sites whose layouts differ, one sheet still needs one size, so the first record's layout decides the
size and the fields, and the print page says so. (My call.)

### 3. Settings › Print layouts

- A tab per paper listing its layouts (name, site, kind, paper size, default) and "Built-in" as the first row, read-only, with
  "Copy to a new layout".
- **The editor:** name; site and kind; default; paper size; logo; header, footer and terms (English and Arabic side by side);
  the blocks with show/hide, move up/down and their field tick boxes; for labels, the label size and fields.
- **Preview:** beside the editor, the real print component renders a chosen record with the unsaved layout. The record is the
  latest one by default, with a picker to choose another (by number). For labels, a few records of the chosen kind.
- Permission: *Manage settings*, like other settings. Anyone who can open a record's print can choose any layout of that paper.

### 4. Printing

- The job card, purchase order print, request receipt and label sheet ask the server which layout applies to the record
  (`GET /api/print-layouts/for?paper=…&id=…`, which applies the order above and returns the layout, or the built-in one) and
  render with it.
- Each print page has a "Layout" choice listing the paper's layouts (the matched one first). Picking another re-renders at
  once and is remembered in the address (`?layout=<id>`), so a bookmarked or reopened print keeps it.
- Paper size sets the page's `@page` size; A5 tightens spacing so the card still fits.
- The print components keep their current look; they only skip hidden blocks and fields, follow the block order, and add the
  header text (under the title area), the terms block and the footer (at the bottom of the last page, and on each printed page
  where the browser supports a running footer; the builder decides).

### 5. The purchase order e-mail

"Send to supplier" picks the order's layout by the same rule and builds the e-mail body from it.

- **With an organization layout:** header text, the supplier and details fields shown, the lines columns shown, the totals
  shown, notes, terms and footer text, in the organization's language (as today). The e-mail never contains more than the printed
  order with that layout. The logo and approvals blocks don't go in the e-mail.
- **With no layout (built-in):** today's e-mail exactly (number, item with the supplier's part number, quantity with unit, unit
  price after discount, line total, totals). One e-mail builder does both: the built-in case is a fixed set of e-mail fields.
  (My call: goal 6, nothing changes on upgrade, wins over matching the print, which already differs from today's e-mail.)

### 6. Language

A layout's texts are printed in the language of the page: Arabic text when the page is in Arabic and the Arabic text is filled
in, otherwise the English text. Built-in words (block titles, field labels) follow the page's language as today. The supplier
e-mail uses the organization's language.

## What's left to the builder

- The JSON shape of `Blocks` and how the catalogue is shared between server validation, the e-mail and the web.
- Whether `/print-layouts/for` is one call per print or folded into each record's existing detail call.
- The editor's look; how the preview fits on narrow screens (it may sit under the editor).
- How A5 spacing is tightened; running footers per printed page where the browser supports them.
- The label CSS for each size (margins, QR size per size).
- Migration name and the soft-delete details.
- A short user guide, `docs/PRINT_LAYOUTS.md`.

## Tests that matter most

- The built-in layouts reproduce today's prints: with no layouts set up, each print page shows the same blocks and fields in
  the same order as before.
- Layout choice: site + kind beats site, which beats kind, which beats default, which beats built-in; a layout for another
  site or kind never applies; the default can't have conditions; two layouts with the same site and kind are refused.
- Validation: unknown block or field, hiding a fixed field, an unknown custom-field key, a site the person can't see, text over
  its limit, a 21st layout, a second default: each refused with a field message.
- Hidden blocks and fields don't print, and the order is followed (job card, purchase order, receipt).
- The purchase order e-mail with a layout: hidden fields and columns absent, terms, header and footer present, plain text
  escaped (`<b>` shows as text). With no layout: the e-mail is unchanged from today.
- Prices: discount hidden → unit price after discount, and quantity × unit price = line total.
- Hand-picked layouts: several with no site and no kind can exist besides the default, and none of them applies by itself.
- Language: Arabic text on an Arabic page when filled in, English otherwise.
- Labels: each size's sheet geometry; the `detail` line per kind; a sheet whose records' sites have different layouts uses the
  first record's layout and says so.
- `?layout=<id>` keeps the chosen layout; a layout of another paper or another organization answers 404.
