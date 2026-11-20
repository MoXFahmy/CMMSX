# 0.2.0 List toolkit (UX-1) — design

Source: DimoMaint report item UX-1 ("List toolkit for every list", High, minor release). The user asked to start 0.2.0
and to "just dive in", so every decision below is the builder's call, written down so it can be checked.

## Why

Planners, supervisors and storekeepers live in lists. DimoMaint gives every list the same tools (columns, saved views,
filter chips, Excel export, bulk actions); 4C CMMS has them only on the asset list, and only partly (column choice is
kept in one browser). 0.2.0 makes the six main lists work the same way, so a person can set a list up once, come back
to it from any device, take it to Excel, and act on many rows at once.

## Scope

Lists: **work orders, work requests, PM schedules, parts** (Inventory › Parts), **purchase orders, assets**.

On each list:

1. **Column chooser.** Show or hide columns; the choice is saved on the server per person and list, so it follows the
   person to any browser or phone. Each list keeps its current default set. The asset list's old browser-saved choice
   is carried over once (first visit after the upgrade, when the server has none).
2. **Saved views.** Named, personal views saved on the server. A view holds everything in the address except the page
   number (filters, tab, quick view, sort, page size) plus the column choice. The menu can save the current list as a
   new view, update a view with the current list, rename, delete, and mark one view per list as the default. Opening a
   list with nothing in its address applies the default view. The view that matches the current list is ticked.
   Limits: name 1–60 characters, unique per person and list (ignoring case); 50 views per person per list; the stored
   query is at most 2,000 characters.
3. **Filter chips.** A row of chips under the filters, one per active filter, showing its value in words
   ("Priority: High, Urgent", "Site: Cairo plant", "Due: 1 Nov 2026 – 30 Nov 2026"); the × removes that filter.
   "Clear all" stays.
4. **Export.** A menu with **Excel (.xlsx)** and **CSV**. It exports every row that matches the current filters and sort
   (not just the page on screen), with the visible columns, in screen order, under the screen's column titles.
   - At most 10,000 rows. When more match, the menu says so and the file holds the first 10,000.
   - Values read like the screen: words for statuses and other codes ("In progress"), "Yes"/"No", lists joined with
     ", ". In Excel, dates are real dates and numbers are numbers; date-times are in the organization's time zone.
   - A column on screen may export as several columns (the work-order "Asset" column exports asset number and asset
     name); columns with nothing to export (pictures, buttons) are left out.
   - The header row is bold and frozen, with filters switched on (xlsx). CSV is UTF-8 with a byte-order mark (Arabic
     opens correctly in Excel), and text starting with `=`, `+`, `-` or `@` gets a leading `'` so Excel won't run it.
   - An export shows only rows the person may see in the list (e.g. a requester exports only their own requests).
5. **Bulk actions.** Ticking rows shows a bar with the count and the actions below. Up to 500 rows per action; "select
   all" covers the page on screen. The answer says how many changed and lists the rows that were skipped, with why.
   - **Work orders:** *Change status* (the same rules as changing one work order: allowed moves, the hold reason, the
     0.0.2 completion rules, the close permission; shared completion notes can be given), *Set priority*,
     *Add technicians* (added to those already assigned; they get the usual "work assigned" e-mail). Closed and
     cancelled work orders are skipped for priority and technicians ("Reopen it first"), as the edit form does.
     Permissions: status needs workorders.execute (closing also workorders.close); priority and technicians need
     workorders.edit.
   - **PM schedules:** *Pause* and *Resume* (resuming sets the next due date the same way the edit form does).
     Needs pm.manage.
   - **Parts:** *Mark active* and *Mark inactive*. Needs inventory.manage.
   - **Assets:** the existing bulk edit (status, criticality, category) moves onto the shared bar.
   - **Work requests and purchase orders:** no bulk actions. Approving work or money stays one at a time.

The PM schedule list keeps its filters in the address from now on (the other five already do), so it can be saved as
a view and shared as a link.

## Non-goals

Views shared with the team; reordering or resizing columns; selecting every row across pages; bulk delete; bulk
actions on requests and purchase orders; the People, Locations and stock-transaction lists; import (AD-1);
printing lists.

## Anti-goals

- A bulk action that lets through something the one-at-a-time action refuses.
- An export that contains rows or columns the person can't see on screen.
- One person's views or columns changing what another person sees.

## How it is built

**Server**

- One export writer (`TableExport`): CSV and a small built-in XLSX writer (SpreadsheetML in a zip; no new package).
- Each list's query moves out of its endpoint into a function both the list and its `/export` endpoint call, so the
  filters, visibility and sort can't drift apart. Export reads each row as JSON (the same JSON the list returns) and
  picks the requested properties by key; dotted keys reach into objects (`customFields.colour`). Unknown keys → 400.
  The request carries repeated `c` (key) and `t` (title) parameters in screen order plus the list's own filters; with
  no `c`, every non-id property is exported under its key.
  `GET /api/{work-orders|work-requests|pm-schedules|parts|purchase-orders|assets}/export?format=xlsx|csv&c=…&t=…&<filters>`
  (the asset export URL stays, now with these options).
- Saved views: a `SavedView` table (tenant, user, list key, name, query, hidden columns, is-default, timestamps).
  Column choice: a `UserListSetting` table (tenant, user, list key, hidden columns). Both personal; queries are always
  scoped to the signed-in user.
  `GET/POST /api/views?list=…`, `PUT/DELETE /api/views/{id}`, `GET /api/me/list-settings`,
  `PUT /api/me/list-settings/{list}`.
- Bulk: `POST /api/work-orders/bulk`, `POST /api/pm-schedules/bulk`, `POST /api/parts/bulk`; the single work-order status
  change moves into a shared function that both the single and the bulk endpoint call, so the rules live in one place.

**Web**

- Shared pieces: `useListParams` (address-bar filters, the debounced search, page and sort), `useListColumns`
  (server-saved hidden columns), `ListTools` (views, columns and export menus), `FilterChips`, `BulkBar`.
- Each list declares its columns once (title, cell, and what it exports) and its filters once (key, label, how to show
  the value as a chip).

## Left to the builder

Exact wording of labels; icons; which columns each list hides by default (keep today's); column widths in the xlsx
(estimated from the text length); the order of menu items.
