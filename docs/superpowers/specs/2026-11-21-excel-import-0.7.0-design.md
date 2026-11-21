# CMX 0.7.0 — Excel import and export (AD-1)

## Why

A new customer who never used 4CSQL has no way to bring their data into CMX. Today the only import is the 4CSQL
command-line tool, which we run for the customer. 0.7.0 lets an administrator load their own locations, assets, parts
and stock, people, suppliers and PM from one Excel workbook. They can also take the same data out, fix it in Excel, and
load it again. This is the main thing between CMX and customers who don't come from 4CSQL.

## Decisions taken with the user

- **Add and update.** A row whose code is new adds a record, and a row whose code exists updates that record.
  Nothing is ever deleted.
- **One Import page** under Settings, taking one workbook with a sheet per kind. Any sheet may be missing, empty or
  partly filled.
- **Check, then send again (approach 1).** "Check file" reads the workbook and shows a preview without saving anything.
  "Import" sends the same file again; the server checks it once more and saves. Nothing is stored between the two.

## Approaches considered

1. **Check, then send again (chosen).** Stateless: nothing to clean up when a preview is abandoned. Changes made by
   someone else between preview and import are caught by the second check. Each record's own history already shows
   who changed it.
2. **Server keeps the preview and an import history.** This gives a list of past imports and room for background
   jobs, but it needs a new table, clean-up of abandoned previews and a background worker. That's more than a first
   load needs.
3. **The browser reads the file and sends rows.** It needs a large spreadsheet library in the web app, and the server
   must check every row anyway.

## Goals

- An administrator can fill a template, see exactly what will happen, and load it, without our help.
- "Download current data" followed straight away by "Import", with nothing edited, changes nothing.
- An imported record is the same as one made on screen. Asset numbering, location paths, PM next due dates, stock
  ledger entries and "created/updated by" all come out the same.
- It works in English and Arabic.

## Non-goals (0.7.0)

- Work orders, requests, purchase orders, meter readings, files and photos, user logins.
- Meter-based PM schedules, PM rounds (one schedule over several assets) and "every Nth" extra tasks. Rows for such
  schedules can still update their plain fields; the meter, round and extra-task settings are left as they are.
- Deleting or clearing values through import.
- The old `.xls` format, background jobs, an import history, connectors to other systems.

## Anti-goals (a failure even if it "works")

- Saving anything the preview didn't show.
- An old export loaded later quietly moving stock back to old quantities.
- A half-saved import after a server error. The good rows go in together, or nothing does.

## The page (Settings › Import)

```
Import data
 1. Get a workbook   [Download empty template]  [Download current data]
 2. Choose the file  [ Choose .xlsx or .csv … ]   [Check file]
 3. Preview
    Sheet        New  Changed  Unchanged  Problems
    Sites          2        0          0         0
    Locations     14        3          0         1   ▸ (rows: what changes, field by field / why it's a problem)
    Assets       230       12          4         5   ▸
    New lookups: category PUMPS, trade WELD
    [Import 261 rows]   [Download problem rows]
 4. Result: "Imported 261 rows. 6 rows had problems and were skipped."   [Download problem rows]
```

- Only people with `settings.manage` see the tab and can call its endpoints.
- "Changed" lists each field with its old and new value. "Problems" gives the sheet, row number, column and reason.
- "Download problem rows" gives an `.xlsx` with the problem rows as they were in the file, plus a "Problem" column.
  Fixing those rows and loading that file works like any other file.
- The Import button only shows after a check of the same file, and only when at least one row would add or change
  something.

## The workbook

- One sheet per kind, named as below. Sheets are found by name; columns by their heading on row 1. Headings and
  sheet names are matched in English or Arabic, ignoring case and surrounding spaces. Column order doesn't matter, and
  unknown columns are ignored and listed in the preview as "ignored column".
- The template and "current data" come in the person's language and carry a **Help** sheet. It lists each sheet's
  columns, which are required, and the allowed values.
- A `.csv` file is one sheet. Its name must start with the sheet's name (`Assets.csv`, `assets-november.csv`). UTF-8
  with or without BOM, comma or semicolon separated.
- **Fixed values** (status, criticality, type, priority, Yes/No, units, weekdays) are accepted as the English or Arabic
  word the screens show, ignoring case.
- **Dates**: real Excel dates, or text `yyyy-mm-dd`. **Numbers**: Excel numbers, or text with `.` as the decimal mark.
  **Lists** (trades, sites, assignees): codes separated by commas.
- **Blank cells keep the current value** on update. On a new row, a blank cell takes the same default a new record gets
  on screen.
- **Limits**: 10 MB per file and 5,000 rows per sheet. A bigger load is split over several files; that is safe because
  rows are matched by code. The limit keeps a check and an import well inside the 60-second request limit in front of
  the app.

### Sheets, keys and columns (processed in this order)

| Sheet | Matched by | Columns (* required on new rows) |
|---|---|---|
| Sites | Code | Code*, Name*, Address, City, Country, Time zone, Active |
| Locations | Site code + Code | Site code*, Code*, Name*, Parent code, Type, Criticality, Description, Notes to technician, Active |
| Categories | Code | Code*, Name*, Color, Active |
| Trades | Code | Code*, Name*, Color, Active |
| Part groups | Code | Code*, Name*, Active |
| Suppliers | Code | Code*, Name*, Contact, Email, Phone, Address, City, Country, Tax number, Payment terms, Currency, Contractor, Notes, Active |
| Stores | Code | Code*, Name*, Site code*, Description, Active |
| Assets | Asset number | Asset number (blank = numbered automatically when the organization numbers assets, else required), Name*, Site code*, Location code, Parent asset number, Category code, Status, Criticality, Manufacturer, Model, Serial number, Barcode, Operation number, Commissioned on, Disposed on, Purchase price, Current value, Salvage value, Expected life (years), Downtime cost per hour, Warranty type, Warranty expires on, Warranty notes, Description, Notes, Notes to technician, then one column per custom field (heading = the field's name) |
| People | Code | Code*, Full name*, Job title, Email, Phone, Trades, Sites, Hourly rate, Overtime rate, Contractor, Company, Notes, Active |
| Parts | Part number | Part number*, Name*, Unit*, Group code, Specification, Manufacturer, Manufacturer part number, Standard price, Lead time (days), Critical, Shelf-life item, Allow negative stock, Preferred supplier code, Barcode, Notes, Active |
| Stock | Part number + Store code | Part number*, Store code*, Bin, Min, Re-order level, Max, Re-order quantity, Opening quantity, Batch, Expiry |
| PM tasks | Code | Code*, Name*, Description, Trade code, Estimated hours, Safety notes, Active |
| PM steps | Task code + Step | Task code*, Step*, Description* |
| PM parts | Task code + Part number | Task code*, Part number*, Quantity* |
| PM schedules | Code | Code*, Name*, Task code*, Site code*, Location code, Asset number, Trade code, Priority, Schedule (Every / Weekday of month / Day of month), Every, Unit (Day / Week / Month / Year), Count from (Due date / Completion), Week of month (1–4 / Last), Weekday, Day of month, Start date*, Lead days, Work period days, Assignees, Description, Active |

Rules per sheet:

- **References** are by code, and may point at rows earlier in the same workbook (a location's parent, an asset's
  location and parent asset, a store's site). Inside Locations and Assets, parents may come after their children in the
  sheet. A location's parent must be in the same site, and a loop (A under B under A) is a problem.
- **Time zone** takes the same values as the screen's time zone list (for example `Africa/Cairo`).
- **Lookups**: an unknown category, trade or part group code is added, named after its code, and listed under "New
  lookups" in the preview. Every other unknown reference (site, location, parent asset, store, supplier, part, task,
  person) makes the row a problem.
- **Asset numbers**: a row with a blank number is always a new asset. The preview says so, so loading the same
  template twice visibly adds the assets twice.
- **Changing an asset's site or location** works as it does when editing the asset on screen.
- **Stock** — the opening quantity is used only when the part has no stock in that store yet. It is posted to the stock
  ledger as an opening balance at the part's average cost (for a new part, its standard price), with the note "Opening
  balance (import)". On existing stock the opening quantity is ignored and the preview says "use a stock take to change
  quantities". This keeps an old export from moving stock back. A shelf-life part's opening stock needs Batch and
  Expiry. Bin and levels update like any other field.
- **PM steps / PM parts**: when a task appears on these sheets, the rows given become that task's whole list of steps
  (or parts), replacing the old list. A task that doesn't appear keeps its list. The preview shows such a task as
  "changed" with the old and new lists.
- **PM schedules**: next due date, lead time and the rest come out as when the schedule is saved on screen. Updating a
  meter-based or round schedule changes only the columns above, and its Schedule cell is left blank in the export. A new
  row can't make a meter-based schedule ("set up meter-based schedules on screen").

## Behaviour of a check and an import

- **One run, two endings.** Check and Import do the same thing, inside one database transaction:
  - read every sheet in the order above;
  - turn each row into the same input the screen sends, laid over the record's current values (blank = keep);
  - run the screen's own checks, and save the good rows;
  - collect per-row results.
  A check then rolls the transaction back; an import commits it. So the preview is exactly what an import does. Rows
  that depend on a skipped row become problems too ("location L-12 not found"), in both.
- **All or nothing on failure.** An unexpected error rolls everything back, and the page says nothing was saved.
- **Speed.** Codes are resolved and uniqueness is checked against data loaded once per run (and kept up to date as
  rows are saved), not with a query per row. Target: a 5,000-row assets sheet checks in under 20 seconds on
  production.
- **Time budget.** A run that reaches 45 seconds stops, rolls back, and says the file is too big for one go and should be
  split. Only then does the page show the result, so an import can never save after the page has already shown a
  time-out.
- **Last write wins.** An import doesn't use the records' change stamps: what the file says (and the second check
  confirms) is saved, as when someone saves a form.
- **Codes match ignoring case.** A row that differs from the stored code only in case updates the record and leaves
  the stored code as it is.
- Rows that change nothing are not written, so their "updated" time and author stay as they were.

## Export ("Download current data")

- All sheets above, in the import layout, with every record that isn't deleted (inactive ones included, with Active =
  No). It is filled with codes, not names, so it imports back with no changes.
- Up to 5,000 rows per sheet. The first line of the Help sheet names any sheet that was cut off. Loading a cut-off
  export back is harmless: it only updates the rows it holds, and nothing is deleted. The existing list exports stay
  as they are.

## Constraints

- No new server package. The `.xlsx` reader is written like the existing writer (`TableExport`): a zip of XML, shared
  and inline strings, numbers, booleans, and dates told apart by their number format. It must read files saved by
  Excel, LibreOffice and Google Sheets.
- The screens' rules are shared with the import, not copied, so they can't drift apart. That covers the field rules
  in the endpoints' `Validate…Async` (required, lengths, ranges, allowed combinations, custom-field types, a required
  custom field on a new asset) and the save code with its side effects: asset numbering, location path and depth, PM
  next due date, and stock ledger posts. The existence and uniqueness checks those validators make with a query are
  done by the import against the run's loaded data instead (see Speed). The screens must behave exactly as before.
- English and Arabic texts for the page, the Help sheet, the headings and the problem messages.

## Tests the builder adds

- **Reader**: shared and inline strings, numbers, booleans, dates, empty cells, a file from LibreOffice, CSV with
  comma, semicolon and BOM.
- **Each sheet**: new, changed, unchanged, a blank keeps its value, problems.
- **Order**: references inside one workbook; parents after their children; a row depending on a problem row.
- **Round trip**: "current data" imports back with zero changes.
- **Stock**: opening quantity on new stock only, a shelf-life batch is needed.
- **PM**: steps replaced per task; next due date as on screen.
- **Language**: Arabic headings and values.
- **Limits**: size and row limits; only `settings.manage` may use it.
- **Check versus import**: a check saves nothing; an import commits; a server error saves nothing.

## Left to the builder

- Endpoint names and the shape of the preview response.
- How "Download problem rows" gets its rows: built in the browser from the preview, or sent again to the server.
- The exact Help sheet wording, and the Arabic sheet names.
- How the endpoints' validation and save code is pulled out for sharing, as long as the screens behave exactly as
  before.
