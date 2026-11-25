# Changelog

All notable changes to CMX (formerly 4C CMMS) — the cloud app and the Android app share one version number.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); numbering follows the rules in
[docs/VERSIONING.md](docs/VERSIONING.md): a new feature raises the middle number, a fix or small task the last one.

## [0.28.0] - 2026-11-25

Bilingual data. Names, checklist steps and notes that technicians read can have an Arabic text beside the main one, and each person
sees their own language on the screens, the phone, prints and labels, reports, e-mails and phone notifications. Where there is no
Arabic text, everyone sees the main text. Arabic texts are typed on the forms, filled in from Excel, or drafted by the AI assistant
for someone to check.

### Added
- **Arabic texts** beside the main text of assets and locations (name and notes to technician), parts, asset categories, symptoms,
  causes, remedies, request types, cancel reasons, work templates (name, title, safety notes and each step), PM schedules and work
  orders (title, steps, safety notes). Each form shows the Arabic field next to the main one.
- **Each person's language everywhere:** lists, pickers, the asset, location and part pages, the work order page and job card,
  reports and their exports, labels, the phone, e-mails and phone notifications show the Arabic text to people reading in Arabic
  (their own choice, else the organization's language). Search finds a record by either text, and lists sort by the text shown.
- **Work orders from templates and PMs** copy the Arabic title, steps and safety notes; follow-up work gets an Arabic title too.
  Duplicating a work order keeps them. Changing a template later doesn't change existing work orders.
- **Safety notes** have their own place on work orders made from now on (both languages), shown as a Safety section on the work
  order page, the phone's job and the job card, and in assignment e-mails. Older work orders keep them in their description.
- **Excel:** Arabic columns ("Name (Arabic)", "Title (Arabic)", "Safety notes (Arabic)", "Description (Arabic)", "Notes to technician
  (Arabic)") right after their main column on the Locations, Categories, Symptoms, Causes, Remedies, Assets, Parts, Work templates,
  PM steps and PM schedules sheets, filled in the current-data download. A blank cell keeps what is there.
- **Fill in missing Arabic** on the assets, locations, parts, work templates and PM schedules lists and on the Settings lists: the AI
  assistant drafts a batch of missing Arabic texts (50 names, 100 steps or 20 notes), someone checks, edits or unticks them and
  saves, then drafts the next batch. Each press is one use of the month's AI allowance (shown as "Arabic drafts" in Settings › AI
  usage). Saving never overwrites an Arabic text someone else saved meanwhile. Needs the permission that edits that list.
- **API:** answers that show names follow the request's `Accept-Language`; a record's own detail and the Settings lists give both
  texts as stored. Leaving an Arabic field out keeps it; an empty string clears it. See docs/API.md.

### Changed
- Purchase orders, purchase requests, the re-order proposal, shortages and supplier papers keep the part's main name, whoever reads
  them.
- A follow-up's main title is worded in the organization's language, whoever answered the step.

## [0.26.1] - 2026-11-24

A fix release: 21 issues left by the reviews of 0.24.0–0.26.0, and three older places where people could see records outside their
organization or sites.

### Security
- **Meters (PM › Meters)** listed every organization's meters to anyone who could view PM (since 0.12.0). The list now shows only
  the organization's own meters.
- **Serial units** (Inventory › Units, a part's Units tab, a unit's page) showed every site's units to site-limited people (since
  0.24.0). They now follow site limits.
- **Purchase requests › lines to order** showed approved lines from every site to a site-limited buyer (since 0.16.0). They now
  follow site limits.
- An asset's fitted units, Settings › Storage's list of largest files and the activity log's record names now follow site and
  organization limits; the storage totals stay organization-wide.
- API keys can't reach people's devices (any request under `/api/users/{id}/devices`).

### Fixed
- **Serial-numbered parts:** a part can't be both serial-tracked and a shelf-life item ("A serial-tracked part can't also be a
  shelf-life item."); parts already both work as serial parts and no longer ask for a batch. A repair line is received whole
  ("Receive a repair line whole."). The units list applies its filters before the 500-unit limit and says "Showing the first 500
  units." when there are more. Units fitted to an asset move with it when it changes site, with or without its other child assets.
  Serial lines on a purchase order's receive form no longer say "then issue to WO-…". Fitting a unit and adding a repair line no
  longer need View inventory.
- **Activity log and privacy:** anonymising someone also replaces their address and name in Settings › E-mail log, and e-mails
  still waiting for them are not sent. Naming an employee to anonymise acts on that employee and their own sign-in only. The log
  finds Arabic values, and its From/To dates are the organization's days in its time zone. A wrong current password when renewing
  an expired one is logged as "Sign-in failed" (and the try that locks the account as "Locked out"). The log's person filter works
  for anyone who may open the log.
- **API keys and imports:** an import is marked done in the same save as its data, so a crash right after can't show "Nothing was
  saved" for an import that saved. Long errors are cut to fit, so a failed import can't stay stuck. Old import files are cleared
  from the history only once they are really deleted. PM schedule e-mail recipients and a contract's manager can't be service
  accounts, and the People page's sign-in list leaves them out. Service accounts can be limited to branches like people. The import
  history keeps itself up to date while a check or import runs, and opening a finished import no longer reloads every screen.

## [0.26.0] - 2026-11-24

API keys and background imports. Other systems (an ERP, a BI tool, a sensor gateway) can read and change CMX data with an API key
that acts as a service account, within its roles and sites. Every check and import now runs in the background with a progress bar,
a history and an e-mail when a long one finishes. See docs/API.md.

### Added
- **Service accounts** (Settings › Users, Manage users): accounts for systems, not people, with roles, sites and an active switch.
  They can't sign in, get no e-mail or push, and are never offered where a person is chosen.
- **API keys** (Settings › API keys, Manage users): made for a service account, shown once, optionally read-only and expiring, with
  the time each was last used and Revoke. Send `Authorization: Bearer cmx_…` (or `X-Api-Key`). A key sees and changes what its
  account's roles and sites allow, up to 120 requests a minute; changes show the source **API** in the activity log. Keys can't sign
  in, manage users, roles, keys or security settings, anonymise anyone or use the technician app's sync.
- **API documentation** at `/api/docs` describes the key scheme; it needs sign-in or a key. Guide: docs/API.md.
- **Background imports** (Settings › Import): checks and imports are queued and run one at a time per server, with progress
  ("Checking Assets… 1,240 of 3,000 rows"), a history of past runs with their results and problem rows, and an e-mail to the person
  who started one that took more than a minute. Leaving the page doesn't stop it. Imports by key answer with the job to follow.

### Changed
- Import limits: up to 20,000 rows in one file and 25 MB (was 5,000 rows a sheet and 10 MB, within 45 seconds). An import still
  saves everything or nothing; one that runs over 30 minutes or is stopped by a server restart saves nothing and says so.
- Asset numbers an import uses are taken at once, so other people can keep adding assets while a long import runs; a check only
  shows the numbers it would take. A failed import leaves a gap in the numbering.
- Site-limited administrators see only the imports they started. Uploaded files are kept 30 days after an import finishes.
- The API description (`/openapi/v1.json`) is no longer public.

## [0.25.0] - 2026-11-24

Security and the activity log. An organization can set its own password rules, allow one signed-in device per person, anonymise
people who have left, and see every change and sign-in in one activity log, with how long to keep it. Settings › Storage shows the
space files take. Nothing changes until an administrator sets these. See docs/SECURITY_AND_ACTIVITY.md.

### Added
- **Settings › Security** (Manage settings): minimum password length (8–64), expiry (never, or 30–730 days) and recent passwords
  that can't be reused (0–24). An expired password is renewed at sign-in, and expiry also reaches devices already signed in: the
  web returns to the page after the new password, the phone keeps its waiting changes. A password shorter than a raised minimum is
  changed at the next sign-in. "Last changed by" shows who set each rule.
- **One session per person** (off by default): signing in on a second device asks before ending the first one's session.
- **Anonymise a person who has left** (Manage users), for an inactive employee or sign-in: their name is replaced wherever it was
  copied (by person where CMX kept who it was, by exact name where only the name was typed, left alone when someone else has the
  same name), e-mail, phone and notes are cleared, the sign-in is closed for good and its e-mail freed; hours and costs stay.
- **Settings › Activity log:** every change (records and their lines, users, roles, settings key by key, the organization's name,
  list wording and logo) and security event (signed in, sign-in failed, locked out, signed out, device signed out, password
  changed or renewed, other sessions ended, anonymised), with the source (Web, Phone, Import, Automatic), one change number per
  action ("show everything this action changed"), filters, Excel/CSV export and masked secrets. Site-limited administrators see
  their sites' records and organization-wide changes. Optional retention of 12–120 months.
- **Settings › Storage:** space used, by kind and by record type, files kept with deleted records, and the largest files with a search.
- **History tabs** show their lines' changes (labour, parts, checklist steps, comments, files…) and where a change came from.

### Changed
- Changes made in bulk reach the log: deleting a record's lines logs each line; merges and renames write one summary on the record.
- The history of a person's sign-in, of roles and of organization settings needs Manage users or Manage settings.
- Choosing a new password signs the person out on their other devices.

## [0.24.0] - 2026-11-24

Serial-numbered parts. Pumps, motors and other repairable spares can be tracked one by one: each piece of a serial-tracked part is
a unit with its own serial number, label, page and history, in a store, fitted to an asset, at repair or scrapped. The part's stock
in each store always equals its units there. Parts that aren't serial-tracked work as before. See docs/SERIAL_PARTS.md.

### Added
- **Serial tracking** on a part: tick it on a new part, or switch it on from the part page with a serial for each piece in stock
  (each becomes a unit, no stock movement). An optional unit life in months gives each unit its end of life.
- **Serials at receipt:** manual and purchase-order receipts ask for one serial per piece (packs count their pieces). A line
  ordered for a work order leaves the units in the store until they're fitted.
- **Fit and remove on a work order** (Parts tab), on the web and on the phone, offline too: fitting issues the unit to the job and
  makes it a component of the asset; its running hours can follow the asset's hours meter. Removing sends it back to a store
  (a return that names no work order), to repair, or to scrap, with a condition note.
- **Unit actions** on the part's Units tab and the unit's page: transfer, send for repair, back from repair (with a repair cost),
  scrap, remove without a work order, register a found unit or one already on a machine; "Make this asset a unit" for an existing
  component with a serial. Units print as asset labels.
- **Outside repair:** a purchase order repair line for a unit under repair; receiving it brings the unit back to a store with the
  repair cost on the unit (and on the line's work order, when it names one). Each unit shows its repair count and cost.
- **Unit and Fitted units cards** on asset pages, a units list, "Show spare units" on the asset list, and an alert for units
  reaching their end of life within 30 days.
- **Import:** the Parts sheet has "Serial tracked" and "Unit life (months)".

### Changed
- Issue, return, adjustment and transfer by quantity are refused on serial parts, with what to do instead. New stock counts and the
  stock take leave serial parts out; the Stock sheet refuses their opening quantities; the phone doesn't receive them.
- Units not fitted to an asset stay off the asset list, location tree counts, dashboard and asset pickers until searched for.
- A unit's status and place change only through fit, remove and the unit actions; a unit is scrapped, not deleted, and an asset
  with fitted units or a part with units can't be deleted.

## [0.23.0] - 2026-11-24

Print layouts. Each organization can make its job card, purchase order, request receipt and labels look like its own forms, in
English and Arabic, with several layouts chosen by site and type. With no layout, every paper prints exactly as before. See
docs/PRINT_LAYOUTS.md.

### Added
- **Settings › Print layouts:** a tab per paper (job card, purchase order, request receipt, labels), with today's paper as a
  read-only "Built-in" layout to copy. A layout chooses which blocks print and in what order, the fields each block shows, header,
  footer and terms text in English and Arabic, the paper size (A4, A5, Letter) and the logo; the job card can show asset custom
  fields. A live preview prints a real record with the layout before saving.
- **Label sizes:** today's A4 sheet (3 × 8), two other A4 sheets (3 × 7, 2 × 7) and one label per page for 50 × 25 mm and
  100 × 50 mm label printers, with the details each label shows.
- **Which layout prints:** the one for the record's site and type, then its site, then its type, then the default, then the
  built-in paper. Layouts with neither are picked by hand. Every print page has a "Layout" choice, kept in the page's address.
- **Request receipt:** "Print receipt" on a request: what was asked, by whom, when the work can be done, and the work order that
  followed.
- **Supplier e-mail:** "Send to supplier" follows the purchase order's layout (details, columns, totals, notes, terms, header and
  footer). With the discount column hidden, the unit price is the price after discount. With no layout the e-mail is unchanged.

## [0.22.0] - 2026-11-24

Home pages and roles. Each role can open CMX on what it needs, and everyone can pin the numbers they watch. A role with no home
page keeps today's dashboard, so nothing changes until an administrator sets one up. See docs/HOME_PAGES_AND_ROLES.md.

### Added
- **A home page per role** (Settings › Roles › Home page): the administrator picks and orders the role's cards from the pieces
  of today's dashboard and new cards: my work, unassigned work, late work, assets down now, parts below reorder level, waiting for
  my approval, report a problem, the location tree (report, create work or add an asset at a picked place) and shortcut tiles.
  A card someone's permissions don't cover is left out for them, and the role window says which cards the role can't see.
- **Shortcut tiles:** up to 12 coloured tiles per role, each with a label, an Arabic label and a CMX link.
- **Counters:** "Show on home page" on any saved view puts a tile under My counters with the number of records it matches right
  now (the same as its list), refreshed every minute; up to 24, in the person's order.
- **Menu per role** (Settings › Roles › Menu): hide menu items a role doesn't need. Tidying only: permissions still decide access.
  An item is hidden for someone when all of their roles that can open it hide it.
- **Default columns per role:** "Use these columns for a role…" in a list's column menu, optionally resetting the columns of
  that role's people who changed theirs; they apply until a person picks their own. **Reset columns** on every list.
- **Duplicate role:** copies permissions, home page, hidden menu items and default columns, without the people.
- **Unassigned** view on the work order list (open work with no person assigned).
- People with several roles see the cards of all their roles, in role-name order, without repeats.

### Changed
- People without dashboard permission and without a role home page now see a short message on the home page instead of an
  endless loader.

## [0.21.0] - 2026-11-24

Editable lists, brands and models, units, asset groups and yearly numbering. Each organization can give the fixed lists its own
words, keep one spelling per brand, model and unit, group assets across the tree, and restart numbers each year. Organizations
that change nothing see CMX as before. See docs/LISTS_GROUPS_AND_NUMBERING.md.

### Added
- **Your own words for the fixed lists** (Settings › Lists): names in English and Arabic, colours, order and hidden values for work
  types, priorities, asset statuses and criticality. Meanings stay fixed, so targets, rules and reports keep working. The names show
  on screens, the phone (also offline), the job card, reports, exports and e-mails; the import accepts them and the built-in names.
  A hidden value stays on old records and in list filters but isn't offered for new ones.
- **Brands and models** (Settings › Brands and models), picked on assets, with "Add …" from the picker; the asset keeps the names
  as text, renaming renames everywhere, and renaming onto another name merges the two. The asset list filters by brand and model.
- **Units** (Settings › Units), picked for parts, measurement points and template steps; case-sensitive symbols; a new symbol is
  written to everything using the old one (work orders keep theirs).
- **Starting lists from your data:** on first start, brands, models and units are built from what records already say, and assets
  are linked; the same runs after every import and at the end of a 4CSQL migration run.
- **Asset groups** (Settings › Asset groups): named sets of assets anywhere in the tree, for one site or all. Members from the
  group, the asset form or a bulk action on the asset list. Filters on the asset and work order lists and on seven reports, and a
  PM target that follows the group: an asset added later is included from the next visit.
- **Yearly numbering** (Settings › Organization): work orders, requests, orders, purchase requests, work groups and contracts can
  carry the year and restart at 1 each year (WO-2027-00001), in the organization's time zone.
- **Import sheets:** Brands and models, Asset groups, Asset group members, and an Asset group code column on PM schedules.

## [0.20.0] - 2026-11-23

Cost centres and budgets. Maintenance cost is charged to the organization's own cost centres, set once on locations and assets
and taken by every new work order, and compared with a monthly budget in the Budget against actual report. Organizations that
set up no cost centres see no change. See docs/COST_CENTRES_AND_BUDGETS.md.

### Added
- **Cost centres** (Settings › Cost centres): a tree ("Production › Line 1 › Packing"), each for one site or all; one in use
  can only be made inactive. Someone limited to some sites sees their sites' cost centres and the shared ones.
- **Where cost goes:** a cost centre on locations and assets. Every new work order takes the one chosen, the request's or PM's
  override, else its asset's (up the asset tree), else its location's (up the location tree); a PM round takes the one its
  assets share. Parts issued straight to a location take the location's, or one chosen when issuing.
- **Corrections:** Change cost centre on any work order, closed and cancelled included, one at a time or from the list; it shows
  in the work order's activity and its costs and purchase commitments move with it. A direct issue's cost centre is changed from
  Inventory › Stock movements. Fill in past work gives work and direct issues from a chosen date that have none the cost centre
  in force today; it counts first and never overwrites.
- **Budgets** (Reports › Budgets): one amount per cost centre per month in a year grid; a total typed in spreads evenly over the
  months.
- **Budget against actual** report: budget, committed (approved orders for work orders, not yet received), labour, parts,
  contractors, other costs, parts issued straight to locations, actual and variance, by cost centre (children indented under
  their parents, which add them up, and a "No cost centre" row) or by month, with a cost-centre filter. Opens on this year to
  date.
- **Maintenance costs** report: a cost-centre filter (with the ones under it) and a "Cost centre" grouping; its totals don't change.
- **Import and export:** Cost centres and Budgets sheets, and a Cost centre code column on the Locations, Assets and PM schedules
  sheets.

## [0.19.0] - 2026-11-23

Calendars, availability and the daily timesheet. Planning stays by the day on the planning board, which now counts time away
and extra time in each person's day, warns when work lands on a day someone is away or over their hours, and has a contractor
view. Technicians and supervisors get a daily timesheet on the web and My day on the phone, over the labour already logged on
work orders. Organizations that enter no availability see the board as before. See docs/CALENDARS_AND_TIMESHEET.md.

### Added
- **Availability** (Maintenance › Availability): time away (holiday, sick leave, training, a meeting) and extra time (an extra
  shift) for a person or a team, for a day or a date range, on chosen weekdays, for the whole day or some hours. Types are
  edited in Settings › Availability types (every organization starts with five).
- **Planning board:**
  - Each person's day shows planned against available hours (working pattern − time away + extra time), tinted when they
    are away. Planned hours count all of the person's open work you can see, whatever the board's filters.
  - A warning, never a block, when work is moved onto a day someone is away or past their hours.
  - "Assign to me" on unassigned work for people linked to an employee; two people pressing at once can't both get it.
  - Asset rows show the expected stop from the work template.
- **Contractor visits:** a visit date on contractor lines (Contractors tab), and a "By contractor" board view where visits
  are dragged to another day and recorded (hours done, date, person).
- **Timesheet** (Maintenance › Timesheet): one person's day with hours and overtime per work order, edited in place
  through the same labour entries as each work order, against the hours available; a week view for supervisors.
- **My day** on the phone: a day of the last 30 with my hours per job (including jobs completed since), added offline,
  against the hours I have that day.

## [0.18.1] - 2026-11-23

### Fixed
- **0.18.0 never went live:** the 4CSQL migration tool didn't compile (its supplier catalogue step declared the
  organization's currency twice), so the server build failed and the deploy was skipped. It builds again; 0.18.1 carries
  everything listed under 0.18.0.

## [0.18.0] - 2026-11-23

Supplier catalogue and purchase units. A part can be bought from several suppliers, each with their own part number,
pack, price, discount, lead time and minimum order. Order lines are filled from the catalogue in whole packs, and
receiving a pack books its pieces at cost per piece. Stock stays in the part's own unit; existing orders don't change
(every existing line is a pack of 1). See docs/SUPPLIER_CATALOGUE.md.

### Added
- **Part page › Suppliers:** one row per supplier with their part number, pack (name and pieces per pack), price per pack
  in the row's own currency, discount, lead time, minimum packs and notes. The star sets the preferred supplier; choosing a
  preferred supplier on the part adds a row for it. Each row shows the **last price paid** (set by receipts, which never
  change the catalogue price) with **Use last price** to copy it in.
- **Orders in packs:** picking a part or changing the supplier on the order form fills lines from the catalogue — the
  supplier's pack rounded up to whole packs (at least the minimum), their price and discount when the row is in the
  order's currency, their part number, and the expected date from the longest lead time. Without a catalogue price the
  line keeps its own price per piece × pack. The pieces show under each line, and the supplier's part number is printed
  and e-mailed on the order.
- **Receiving packs** (web and phone) books quantity × pieces per pack at net price × exchange rate ÷ pieces per pack.
- **Re-order proposal:** suggests the preferred supplier, else the only catalogue supplier, else the last one received
  from; lists catalogue suppliers first with their pack and price, and shows what each line becomes ("→ 1 Box of 100").
  *Create orders* and *Order from requests* turn pieces into whole packs at the catalogue price.
- A company shows the **parts it supplies**.
- **Import:** new *Supplier catalogue* sheet (adds and updates rows; blank cells keep what is there) and in the current
  data export. The 4CSQL migration adds a row for every part–supplier pair.

### Changed
- **On order** (parts list, part page, re-order proposal, inventory report) and a purchase request's **ordered** and
  **received** amounts count pieces.
- The upgrade adds a catalogue row for every part with a preferred supplier (pack of 1; its price only in the base
  currency and when more than 0).

## [0.17.0] - 2026-11-23

Contracts and contractors. Suppliers become companies that can supply, contract and/or manufacture; contractor work is
recorded on work orders at the company's rates and counted once in every cost report; service contracts track their
terms, renewals, covered assets and the work done under them, with a warning before a renewal or end date passes.
Organizations that use none of this see no change. See docs/CONTRACTS.md.

### Added
- **Companies** (Purchasing › Companies, was Suppliers): roles *Supplier*, *Contractor* and *Manufacturer*, other contacts
  and addresses, and a contractor's own hourly rate per category. Orders and requests pick suppliers only.
- **Contractor categories** (Settings) with a default hourly rate. People can be linked to a contractor company.
- **Contractor lines on work orders** (Contractors tab): company, category, person, planned hours and hours done. Planners
  add and change lines; technicians record the hours done, the date and the person. The rate is copied onto the line, so
  later rate changes never alter it. The job card prints the lines.
- **Contractor cost** in work order totals, plan vs actual, work groups, Maintenance costs (new column and chart series),
  the asset's Costs tab, the failure tree and asset work history. Labour of people linked to a company counts there too,
  not in Labour, so nothing is counted twice.
- **Service contracts** (Maintenance › Contracts, numbered `CON-00001`): company, manager, type, trade, site, value, payment
  terms, expected work orders, covered assets with a yearly cost, files, amendments and termination.
  - **Automatic renewal** adds a period each time the end passes, catching up if the server was off for a while.
  - **Alerts:** in the alert window before the last day to stop a renewal (or before the end), the contract shows in the
    bell and one e-mail goes to its manager and the people named in Settings › Contracts (new switch *Contract needs
    attention*).
  - **Links:** a contract field on work orders and PMs (a PM passes it to the work it creates), "Covered by" on work orders
    and requests whose asset is under contract, and a contracts card on the asset page.
  - **Reports › Contracts:** value, spend, work completed and expected per contract.
- Permissions *View contracts* (Technician, Viewer, Maintenance Manager, Administrator) and *Create and edit contracts*
  (Maintenance Manager, Administrator).

## [0.16.2] - 2026-11-23

### Fixed
- **Re-order proposal:** people who may raise purchase requests but not orders can now tick lines and request them.
  Before, they saw the "Request purchase" button but no tick boxes. The hint above the list now says the lines become
  draft purchase requests, one per site.

## [0.16.1] - 2026-11-23

Ships 0.16.0 (purchase requests and approval levels), which never went live: its build check failed on the fix below.

### Fixed
- **Two people signing at the same moment:** with two approvers on an "any one" approval level, the second could be told
  "This record was changed by someone else" instead of "This step is already signed off". It happened when they signed
  at the same moment as the first. No order was approved twice, but the message was wrong.

## [0.16.0] - 2026-11-23

Purchase requests and approval levels. Anyone allowed asks for parts or services with a purchase request; one person
approves it; buyers turn approved lines into draft orders by supplier. Orders can be signed by approval levels that add
up with the amount. Organizations that set no levels approve orders exactly as before. See docs/PURCHASE_REQUESTS.md.

### Added
- **Purchase requests** (Purchasing › Requests, numbered `PR-00001`):
  - **Lines:** a part with the store it should arrive in, or free text for services; quantity, needed-by date, work order
    or asset, estimated price and reason.
  - **Approval:** one person with the new *Approve purchase requests* permission, never the requester. Returned requests go
    back to be fixed and submitted again.
  - **Comments, files** and e-mails (submitted, approved, returned, ordered, arrived).
  - **Raised from** a work order's short planned parts, Inventory › Short for work and the Re-order tab, as well as from
    the Requests tab.
- **Requests into orders:** approved lines from one request, or from several at once (**Lines to order**), become draft
  orders grouped by supplier and site. Each order line keeps its request line, and the request follows its orders,
  including when an order is deleted or cancelled.
- **Order types:** Parts, Services and Capital as an editable list; each order has a type, shown and filterable.
- **Approval levels** (Settings › Purchasing):
  - Each level has a minimum amount, up to five approvers, and whether all of them or any one must sign.
  - They can be set for the organization, for a site, or per order type; the most specific levels apply.
  - Orders are signed lowest level first. A return goes back to Draft; earlier rounds stay visible.
  - Restart approval is there for when an approver leaves. Signatures print on the order.
- **My approvals:** a page and a bell count for requests and orders waiting for your signature, and what was sent back to
  you.
- **E-mails and settings:**
  - New e-mail switches: purchase request submitted, approved, returned, ordered, arrived, and order waiting for my
    signature.
  - Per-site and organization purchase-request recipients and number prefix.

### Changed
- *Approve purchase orders* now approves by approval levels when they apply, and within the person's limit otherwise.
- New permission *Raise purchase requests*. Technicians get it; Maintenance Managers and Administrators get both new
  permissions.

### Fixed
- Adding a line to a saved draft purchase order failed with "This record was changed by someone else".

## [0.15.0] - 2026-11-23

AI assistant. Claude (Anthropic's AI) turns a technician's notes into the completion report and diagnoses a fault from
the asset's own history, citing the work orders it relied on. cworks holds one Claude key for the server; every use is
counted against a monthly limit and logged with its estimated cost. Off by default, and off everywhere until the server
has a key. See docs/AI_ASSISTANT.md.

### Added
- Settings › Organization › **AI assistant**: the switch and the monthly limit (AI actions a month; empty means the
  server's ceiling, 300 unless set otherwise). The month runs in the organization's time zone.
- Phone completion sheet: a **microphone** next to *Work done* dictates in Arabic or English with the phone's own speech
  recognition (Android app 0.15.0 and later; the audio stays on the phone), and **Fill with AI** fills work done, cause
  details, symptom, cause and remedy from the notes. Suggested hours and parts become ticked *Log N h for me* and
  *Use N × part* lines, sent on completion as the usual changes; hours already logged or parts already issued start
  unticked.
- Web completion dialog: **Fill from notes** fills work done, cause details and the three codes; suggested hours and
  parts are listed to add by hand.
- **Diagnose** on a work order and in a request's panel: the probable cause with a confidence and up to five ranked
  actions (why, parts with quantities, estimated hours), each citing the past work orders it relied on, with links.
  History comes from the asset, then assets of the same category (same manufacturer and model first), only work the
  person may see; with fewer than three, CMX says so without asking Claude. Diagnoses are kept (*Earlier diagnoses*,
  *Run again*), take a thumbs up or down from everyone, and an action's parts can be added to the planned parts.
- Settings › **AI usage**: this month against the limit, the last 12 months, by feature and by person, and the call log
  (who, feature, record, model, tokens, estimated cost, result) with filters and export to Excel or CSV.
- A symptom field in the web completion dialog and on the phone completion sheet.
- AWS: the stack takes the Claude key from a Secrets Manager secret named by the GitHub variable
  `CMMS_ANTHROPIC_SECRET_ARN`, and an `AiMonthlyCeiling` parameter (default 300).

### Changed
- Android app: microphone permission (asked only when the microphone is first used) for dictation.

## [0.14.0] - 2026-11-23

Failure codes and maintenance analysis. What people saw, what was wrong and what was done are recorded separately, with
the codes most often used together offered first; new reports show the failure tree, responsiveness, the PM year and
an asset's history; assets show their costs by year including downtime; and every report prints, or saves as a PDF,
with the organization's logo. See docs/FAILURE_ANALYSIS.md.

### Added
- Symptoms and remedies (Settings): a symptom on requests (web, phone) and work orders, copied to the work order when a
  request is approved; a remedy next to the cause on completion (web, work group reports, phone). Columns and filters on
  the work order list (*Not recorded* finds work without one), on the job card and in exports.
- Smart ordering: the causes most often recorded with the chosen symptom, and the remedies most often used for the
  chosen cause, come first — also on the phone offline.
- Reports: **Failure tree** (symptom → cause → remedy with work orders, labour hours, downtime and cost; each line opens
  those work orders), **Responsiveness** (each request and work order against its targets; averages by priority or
  trade), **PM annual plan** (a year per PM, month by month: done, late, missed, cancelled and planned) and **Asset work
  history** (one asset, optionally with its components).
- PM compliance by asset category; Maintenance costs with labour hours and by technician.
- Downtime cost per hour on asset categories, used for assets without their own; the asset's **Costs** tab: labour,
  parts, other costs and downtime cost per year, with its components if asked.
- Organization logo (Settings › Organization) on printed reports and job cards.
- Every report prints, or saves as a PDF, on a landscape page with the logo, its period and filters, and who printed it.
- Import: Symptoms, Causes and Remedies sheets; downtime cost per hour on Categories.

### Changed
- Failure codes are called causes on screens; the free-text cause is "Cause details". *Require a failure code on
  breakdown work* reads *Require a cause on breakdown work* and works as before.

## [0.13.0] - 2026-11-22

Smart checklists and follow-up work. A checklist step says what it asks for (OK / Not OK / N/A, a number, a meter
reading or a text), numbers can be judged against shared measurement points with alert and alarm levels, a failed step
raises follow-up work in the same save, and safety steps must be answered before work starts. See
docs/SMART_CHECKLISTS.md.

### Added
- Step types on work templates: OK / Not OK / N/A, Number (unit and decimals, or a measurement point), Meter reading
  (saved on the asset's meter, meter PMs follow) and Text. Any step can be Photo required or Before starting (safety).
- PM › Measurement points: kind, unit, decimals, lowest and highest possible values, up to five levels (alarm low, alert
  low, target, alert high, alarm high), a work template per alert or alarm level, and the assets it is measured on.
- Follow-up work: a Not OK answer or a reading at an alert or alarm level raises a request for triage or a work order
  from a template, once per step, linked back to the step with its answer, value, remarks and photos. Settings ›
  Organization › *A failed step raises* sets the default; each step can choose its own or none.
- Safety steps: answered OK or N/A before the work can start or be completed and before other steps can be answered,
  on the web, in bulk, for groups and on the phone; automatic start waits for them. On a round, per asset.
- On the work order and the phone: an answer that fits each step, a reading's level, a photo on a step, All OK (plain
  OK / Not OK / N/A steps, whole job or one round asset), each round asset in green or red, and the follow-up raised.
- Settings › Organization › *Require every number, meter and text step answered before completing*.
- Asset › Measurements: a chart of each point's readings with its levels, the latest readings and Add reading.
- Reports › Out of limits: readings at an alert or alarm level with the work they raised.
- Import: a Measurement points sheet, and Type, Unit, Decimals, Measurement point, Photo required, Before starting,
  Follow-up and Follow-up template columns on PM steps.

### Changed
- Steps from before 0.13.0 are OK / Not OK / N/A steps; a ticked step counts as OK. Reading and remarks stay on every
  step.
- The printed work order shows each step's result (☑ answered, ☒ Not OK or out of limits, ☐ open) with its answer,
  value, text, reading and follow-up.
- A template used for follow-up work can't be deleted (mark it inactive instead).

## [0.12.1] - 2026-11-22

### Fixed
- Adding a material to a saved work template failed with "This record was changed by someone else".

## [0.12.0] - 2026-11-22

Work templates, triggers and group work. PM tasks become work templates with documents and recipients; PMs can share
calendars, add extra meters and spread their assets over the year; a PM on several assets can make a work group (one
work order per asset, handled together); and checklists can be attached to an asset or a category. See
docs/PM_TEMPLATES_AND_GROUPS.md.

### Added
- Work templates (PM › Work templates, formerly PM tasks): work order title, work type, priority, recipients, expected
  stop hours and documents, with where each template is used. A work order can be made from a template (Template on
  the form), and a PM can make its own copy of a shared template.
- PM calendars (PM › Calendars): every N days, weeks, months or years from a date, or chosen dates (up to 500), shared
  by many PMs. New PM type Calendar; other PMs can add calendars and up to 5 extra meters — whichever comes first.
  Editing a calendar moves the next date of every PM using it.
- Recipients on templates and PMs, and a Work recipients column in Settings › Notifications.
- A PM's own generation rules: skipped asset statuses, non-working days and waiting for the previous work order.
- On a PM with several assets: a template per asset, Round or Group mode, Copy or Split of the plan, and Spread over N
  parts (one block of assets per part, e.g. one asset a month).
- Occurrences on a PM: the next 24 dates or meter readings, and Generate this one now.
- Work › Work groups: assign, write one report, change status, set the due date and comment for all of a group's work
  orders (or the ticked ones); group column and filter on the work order list; the phone shows a job's group and its
  comments.
- Checklists on an asset (Maintenance plan tab) or a category (Settings › Asset categories): their steps are added to
  every new work order on the asset.
- Meters that follow another meter: increases recorded on the source are added to the follower and run its PMs.
- The phone job lists its work template's documents (opened online).

### Changed
- The import's *PM tasks* sheet is called *Work templates* (the old name still imports), with optional Title, Work
  type, Priority and Expected stop hours columns; *PM schedules* has an optional Multi-asset mode column.
- Deleting a template is refused while a PM uses it for an asset or it is a checklist on an asset or category.

## [0.11.0] - 2026-11-22

Phone app: parts, stock and stops. Technicians use and give back parts, look up stock, count stores and receive
deliveries from the phone, offline too; the Android app gets fingerprint unlock and push notifications. See
docs/TECHNICIAN_APP.md.

### Added
- Parts on a job from the phone: *Used* on a planned part or *Add a part*. What the store has is issued at once (with a
  note when it takes parts reserved for another job); what it can't cover is added to the job and shows as short on
  the store keeper's list. *Give back* returns parts to the store. People without permission to issue stock ask for the
  parts instead, and shelf-life parts go on the store keeper's list to be issued with their batch.
- Stock on the phone: search or scan a part to see stock per store and bin; store keepers issue to any open job.
- Stock counts (Inventory › Stock counts): the office opens a count, people count on the phone (offline too, the latest
  count of a part wins), and the office reviews the differences and posts them. Stock moved after a part was counted
  is kept when posting.
- Inventory › Short for work: planned parts on open work that their store can't cover.
- Receiving purchase order deliveries on the phone, for people who may receive.
- On a phone job: mark the asset stopped or back running with the time it happened, add a PM checklist, attach videos
  and documents up to 50 MB, log hours for the crew (new permission "Log hours for others", given to Maintenance
  Managers in new organizations), cancel the job with a reason, and create work orders.
- Android app: unlock with fingerprint or face (Sync page), and notifications when a job is assigned or a request is
  approved. Push needs the server's Firebase key on AWS (docs/DEPLOY_AWS.md › Push notifications) and the 0.11.0 app.

### Changed
- The phone's Sync page names every kind of change in English and Arabic, and a video or document that doesn't get
  through on a slow connection after 3 tries is set aside (Retry or Discard) so the changes after it still go.
- The phone's job page shows *Asset stopped* / *Back running* offline instead of the online-only button, and its *New
  work order* link opens the phone form (also under *My jobs*).

## [0.10.0] - 2026-11-22

Requests that close the loop: requests go to the right people, the requester accepts or refuses finished work, and an
administrator chooses which actions need a comment and who gets which e-mail. See docs/REQUESTS_AND_NOTIFICATIONS.md.

### Added
- Request types (Settings › Request types): a default trade and priority, people told about new requests, and optional
  automatic approval into a work order for the default trade or a team (also for requests raised on the phone).
- "Keep informed" on a request: up to 10 extra people who can see the place. The site's, the type's and the request's
  own recipients are all told about a new request.
- The requester accepts or refuses finished work with a 1–5 rating and a comment. Accepting closes the request (new
  status Closed); refusing reopens the work order with the reason. A requester with a CMX login answers on the request;
  anyone else gets a single-use link that works without signing in and expires after 30 days.
- Comments on requests, e-mailed to the other side; the work order shows them read-only.
- Required comments (Settings › Organization): reject a request, refuse finished work, put on hold, reopen, cancel and
  complete, on the web, for many work orders at once and on the phone.
- Cancel reasons (Settings › Cancel reasons): cancelling a work order asks for one; the reason shows on the work order,
  in the list (filter) and in the new Cancelled work report.
- Access windows on requests and work orders, shown on the work order, the phone job and the planning board, with a
  warning when work is planned outside every window.
- "Maintenance only while stopped" on assets, copied to their requests, work orders and PM schedules, with a badge and a
  filter on the work order list and the planning board.
- Settings › Notifications is a grid: for each work and request e-mail, choose the requester, request recipients,
  technicians, contractors and issuer. New e-mails: comments on requests, finished work refused, work cancelled and work
  overdue (once per due time; old backlog is never e-mailed).
- Reports: Requester satisfaction and Cancelled work.

### Changed
- Cancelling a work order needs a reason when the list has an active one (four are provided).
- When work raised from a request is completed, the requester is asked to accept or refuse it.
- Every e-mail that existed before still goes to the same people by default.

## [0.9.1] - 2026-11-22

Small fixes to sites and access, from the 0.9.0 review.

### Fixed
- A user whose places were all deleted can be edited again (they keep seeing nothing until given new places); the user
  form shows such a place as "(deleted place)" so it can be taken off.
- A limited administrator can't list or sign out the devices of people who see more than they do.
- The change history of a work order's labour, parts, costs and other rows answers "not found" outside the person's sites.
- Saving a work order or PM schedule from a page opened before 0.9.0 no longer clears its team.
- A team's site can't be changed away from its open work and schedules; people with limited access give their teams a
  site.
- The planning board shows people limited to some sites only the technicians who work there (or everywhere).
- A site's request recipients must be able to see all of the site.
- The import's automatic asset numbers skip numbers used in sites the person can't see (no more "already exists").
- The dashboard's "Completed (30 days)" opens the work completed in the last 30 days (new "Completed since" filter);
  the More filters badge counts the team.
- Saving a PM round keeps its assets in parts of the site the person can't see, and generating it covers them; only
  assets the person can see can be added, and such a round can't be turned into a single-asset schedule by them.
- An older page that moves work to another site drops a team that doesn't fit there; a team it sends is kept.

## [0.9.0] - 2026-11-22

People can be limited to their own sites or parts of the location tree, each site can have settings of its own, and
work can be given to teams. Nothing changes until you set something. See [docs/SITES_AND_ACCESS.md](docs/SITES_AND_ACCESS.md).

### Added
- **Sites per person** (Settings › Users): a person sees only their sites, everywhere — lists, records, pickers,
  reports, exports, the import, the dashboard, the alert bell, e-mails and the technician app. A record from another
  site answers "not found". No sites = every site, as before.
- **Limits by part of the location tree**: a person can be limited to places (a building, a floor, a line) and
  everything under them, including places added later. They must choose a location when they add work, requests,
  assets or PM schedules.
- **Site switch** in the header for people who see more than one site, and **Work by site** on the dashboard (open,
  late, completed in 30 days, requests waiting — each number opens that site's list).
- **A site's own settings**: number prefixes (each with its own count), default store, who is told about new requests,
  e-mail sender name and working hours. Empty = the organization's.
- **Teams** (People › Teams): give work orders and PM schedules to a team; members get the e-mail and see the work on
  their phones ("Team: …"); team filter and column on work orders, team filter on the planning board, group by team in
  the Labour hours and Maintenance costs reports.
- **Working hours** for the organization, each site and each person; the planning board measures each person's load
  against their hours for the day (a day off counts as 0).

### Changed
- Settings › Organization: **Working hours** (a weekly pattern) replaces "Hours per day"; until a pattern is saved the
  old number still applies to every day.
- E-mails go only to people who can see the record's site.
- Codes and numbers stay unique across the organization even when the clash is in a site the person can't see (the
  message says so, and the import makes that row a problem instead of failing).
- People whose own access is limited can't add sites, can only give access inside their own, and can only change people
  whose access is inside theirs. People limited to parts of the tree add places only under what they see and can't change
  the site record.
- A part's average cost and the "is it still in use?" check before deleting a shared record (part, supplier, PM task,
  trade, category, person) always count every site.

## [0.8.1] - 2026-11-21

### Added
- **Technician app: "The asset is down"** on the request form (after scanning an asset). It works offline: the stop
  starts at the moment the request was made on the phone, even if it is sent hours later. A late report about a stop
  that has since ended joins that stop instead of overlapping it.
- **Technician app: completing a job with a stop** shows "from the asset's downtime log" instead of asking whether
  the asset was down and for how long.

### Changed
- **Changing a work order's asset moves its stop.** The stop the work order started (still open, nothing else on it)
  goes to the new asset and keeps its start (or starts where the new asset's last stop ended, so stops never
  overlap), so a machine picked by mistake doesn't stay down. If the new asset is already down, the work order joins
  that stop; if the asset is cleared, the stop is removed. A stop that came from a request, or that other records
  share, stays where it is. The work order form says which will happen.

### Fixed
- Running hours are kept to two decimals, so an import of 8.333 doesn't show as a change every time.
- A stop added without a start is refused ("Choose when the asset stopped.").
- An asset's spare-parts list holds at most 500 parts with quantities up to 1,000,000,000, on the screen as in the
  import (a huge quantity used to fail with a server error).
- The Downtime log report says when it shows only the first 5,000 stops.

## [0.8.0] - 2026-11-21

Each asset gets its own spare-parts list and a log of its real stops, and availability counts only the time it is
meant to run.

### Added
- **"The asset is down"** on new requests and on work orders (new and edit), shown once an asset is chosen. It starts
  a stop on the asset's downtime log at the moment the request or work order was raised, so the real start is kept
  even when the work order comes hours later. One open stop per asset: a second report joins it. Approving the
  request carries the stop to its work order.
- **Ending a stop**: completing the work order ends it at the completion's actual end; **Back running** ends it
  earlier, from the asset page, the work order page or the technician app's job page (needs a connection).
  Rejecting or cancelling the paperwork leaves the asset down until someone presses Back running.
- **Asset › Downtime tab**: down since (and for how long), Back running, availability and downtime over the last 30
  days, the running time, and every stop with its reason and the request or work order it came from. People who
  can edit assets add stops by hand and correct a stop's start, end or reason (each change is kept in the history;
  stops may not overlap), or delete a stop that never happened (its requests and work orders let go of it).
- **Running time per asset**: "Runs … hours a day" (0.5–24) and "Runs on" (weekdays), on the asset form and in the
  import. Existing assets run 24 hours, every day, until changed.
- **"Down" badge** on the asset page and list, a **Down now** filter, and an **Assets down now** tile on the dashboard.
- **Downtime log report**: every stop in a period with the hours stopped during the asset's running time.
- **Asset › Parts tab**: the asset's spare parts with quantity and a note, edited by people who can edit assets;
  **Copy from another asset** adds the other asset's parts (a part already listed keeps its quantity).
- **Part › Used on**: the assets whose spare-parts list has the part. A part on such a list can't be deleted.
- **Parts of this asset first**: planning or issuing parts on a work order lists the asset's spare parts first, with
  the quantity from its list.
- **Import**: a new **Asset parts** sheet (asset number, part number, quantity, note); the rows given for an asset
  become its whole list, as with PM parts. The Assets sheet gains "Runs hours a day" and "Runs on".

### Changed
- **Asset reliability report**: breakdowns are the stops that began in the period; downtime, MTBF and availability
  count only each asset's running time, in the organization's time zone. Work completed the old way ("Asset was
  down" with hours typed and no stop) still counts on its completion day, unless it was done while the asset had a
  stop, which already counts those hours. Breakdown work orders where the asset was not down no longer count as
  breakdowns. MTBF now shows one decimal.
- **Failure analysis report**: work orders that share one stop count its downtime once.
- **Completing a work order with a stop**: "Asset was down" and the downtime hours come from the downtime log and
  can't be typed over.

## [0.7.2] - 2026-11-21

0.7.1 was built but never went live: its automatic checks failed on a slow test machine (5,000 locations took 32 s
against a 20-second target). 0.7.2 brings 0.7.1's change together with the fix.

### Fixed
- **Import speed**: a sheet of new locations no longer slows down with the square of its size (5,000 locations: about
  10 s → under 2 s on the test machine), and automatic asset numbers for an import are taken in one go (5,000 assets:
  about 5 s → 3 s). Numbers set aside for rows that turn out to be problems are given back, so numbering has no gaps.

## [0.7.1] - 2026-11-21

### Changed
- **Nobody waits for someone else's unsaved work.** When the app starts, it makes sure the database reads the last
  saved version of a record instead of waiting while another person's change, or an import check, is still in
  progress (SQL Server's READ_COMMITTED_SNAPSHOT). Databases CMX created already work this way; a database that
  doesn't is switched over once, which ends other open connections for a moment (anyone using CMX right then
  retries once).

## [0.7.0] - 2026-11-21

Customers who never used 4CSQL can load their own data from Excel.

### Added
- **Settings › Import** (administrators): load or update locations, assets, parts and stock, people, suppliers and PM
  from one Excel workbook (or a CSV file for one sheet).
  - **Download empty template**: every sheet with its headings, plus a Help sheet listing the columns, which are
    required and the allowed values. In each person's language.
  - **Download current data**: the organization's records in the same layout, with codes. Fix them in Excel and
    load the file again.
  - **Check file** shows what an import would do before anything is saved: new rows, changed rows (each change
    field by field), unchanged rows and problems with their reasons. **Import** then saves the good rows and skips
    the rest; **Download problem rows** gives those rows back with the reason beside each.
- Sheets: sites, locations, categories, trades, part groups, suppliers, stores, assets (custom fields too), people,
  parts, stock, PM tasks, PM steps, PM parts and PM schedules. Headings and fixed values work in English or Arabic.

### How it behaves
- Rows are matched by code: a new code adds a record, a known code updates it. Nothing is deleted (set Active to
  No instead), and a blank cell keeps the current value.
- An imported record is the same as one made on screen: asset numbering, location paths, PM due dates, stock
  ledger entries and "created/updated by".
- Unknown category, trade and part group codes are added and listed in the preview; any other unknown code makes the
  row a problem.
- The opening stock quantity is used only for stock that is new; after that, quantities change through a stock take.
- Steps and parts listed for a PM task replace that task's list. Meter-based PM, PM rounds and "every Nth" tasks
  are still set up on screen.
- Limits: 10 MB per file and 5,000 rows per sheet; a bigger load is split over several files. Old `.xls` files must
  be saved as `.xlsx` first.

## [0.6.0] - 2026-11-21

CMX speaks Arabic, with screens that read right to left.

### Added
- **Arabic**: every screen of the cloud app and the technician app in Arabic, laid out right to left (menus, tables,
  forms, the planning board, dialogs, arrows). Dates use Arabic month names; digits stay 0–9 and codes stay as they are.
- **Each person picks their language**: from the menu under their name, on the sign-in page, or on the technician app's
  Sync page. Whoever hasn't picked one uses the organization's language.
- **Organization language** (Settings › Organization): the language for people who haven't picked one and for orders sent
  to suppliers. An organization set up in Arabic starts in Arabic; someone who signs in in Arabic keeps it.
- **Arabic names for renamed terms** (Settings › Terminology): the site, location and asset levels have an Arabic name
  beside the English one.
- **In Arabic from the server too**: error and check messages, report titles, columns and totals, PM schedule wording
  ("كل 3 أشهر"), and e-mails, which each person gets in their own language (a supplier gets the order in the
  organization's language).
- **Android app**: the "no connection" page is in English and Arabic.

### Notes
- The Arabic wording is formal and follows the terms 4CSQL used. A native speaker should look it over; the text lives
  in `web/src/locales/ar.json` and `server/FourC.Cmms.Api/Localization/ar.json`.
- In Arabic, amounts show the currency code when its sign mixes Latin letters ("USD" rather than "US$"); Arabic signs
  such as "ج.م." stay. Charts keep their usual direction.

## [0.5.6] - 2026-11-21

Clean-ups under the hood, and one real fix found by the new tests.

### Fixed
- **PM tasks**: saving a task that has parts again (for example after renaming it) no longer fails with "changed by
  someone else".
- **Devices**: an administrator looking at their own devices under Settings › Users sees which one is "This device".
- **Files**: a photo or attachment whose record couldn't be saved is removed again instead of being left in storage; an
  upload with one file over 25 MB stores none of them.
- **Deadlines**: the end of a day is also right in time zones whose clocks jump forward over midnight.

### Changed
- Records of handled phone changes are kept for 90 days, then removed.
- The work order page loads planned parts' stock with two queries in all, instead of two per part.
- Android: test builds have their own app id (they install beside the real app); the print and save helpers only work
  on the cloud app's own pages; releases check the APK is signed with the release certificate.
- CI also runs the web app's unit tests and the Android app's checks on every push.
- The deploy summary shows both web addresses and whether e-mail is on; the server builds without warnings.

## [0.5.5] - 2026-11-21

Small fixes people notice, left over from earlier releases.

### Fixed
- **Technician app**:
  - The job list is only downloaded again when something changed (less data on weak signal).
  - "Log hours before completing" is met by anyone's hours on the job, as in the office (it used to want your own).
  - The app asks the phone's browser to keep its saved jobs and waiting changes, so they aren't cleared after a long
    break (Safari clears site data after 7 days otherwise).
- **Android app**: pinch to zoom; downloads and saved files are filed under the right kind of file.
- **Parts**: a quantity that would be saved as 0 (more than three decimals) is refused. When issuing or transferring, an
  expired or short batch is said before the "Issue anyway?" question about reserved stock.
- **Importer**: repeated PM parts rows that are added together are no longer reported as skipped.
- **E-mail**: a pause between batches when many e-mails are waiting; addresses that can't be sent to (for example two
  addresses in one field) are left out instead of failing again and again, and "Send to supplier" says so.
- **PM**: a due date no longer lands on a non-working day (a "whole month" ends on the month's last working day).
- **Work orders**: two checklist steps ticked at the same moment no longer clash when work starts automatically.

## [0.5.4] - 2026-11-21

Twelve more small fixes left over from earlier releases.

### Fixed
- **Targets met report**: finished work imported without a completion or decision time is no longer counted as missed.
- **Technician app**:
  - Hours can only be logged for the last 30 days (the date picker and the server agree).
  - The 30-day sign-in now counts from the last time the app was used (it used to renew only every 15 days or so).
  - Scanning a label of an asset on another site opens it in the full app when there is signal, instead of saying it
    isn't on the phone. A part's number or barcode opens the part. Leaving the scan page while the phone asks for camera
    permission no longer leaves the camera on.
  - A change that has just been sent no longer looks undone until the next refresh.
  - When the jobs can't be refreshed for a reason other than missing signal (for example, the account isn't linked to an
    employee), the app says why.
  - An empty photo is called empty, not "larger than 10 MB".
  - The Android app's "Can't reach CMX" page also covers the server being briefly unavailable.
- **Printing a work order** no longer waits forever when an image doesn't load.
- **Parts**: a part used only on deleted work orders or PM tasks can now be deleted.
- **Exports** no longer repeat a field when two shown columns both carry it (parts, purchase orders).
- **PM**: "Generate next now" waits until schedule changes are saved, so they aren't lost; the PM rules hint says that
  work started by a meter reading doesn't follow the rules.

## [0.5.3] - 2026-11-21

### Changed
- **The app is now called CMX** (was 4C CMMS): the web app's title and header, the sign-in page, the technician view,
  the Android app's name under its icon, and e-mails. E-mails come from "*organization* via CMX" (just "CMX" when the
  organization has that name). New APKs are named `CMX-x.y.z.apk`.
- Nothing else changes: the Android app updates over the old one as before and keeps you signed in.

## [0.5.2] - 2026-11-20

### Changed
- **New web address: https://cmx.cworks.com.eg.** E-mail links and newly printed QR labels use it. The old
  `dxvjhwlc6echh.cloudfront.net` address keeps working, so bookmarks and labels already printed still open.
- **Android app** opens the new address. Before updating, technicians should let the app finish sending any changes
  still waiting; after updating, they sign in once more.
- Deployment: the AWS stack takes the app's own domain and its certificate from the repository variables
  `CMMS_DOMAIN` and `CMMS_DOMAIN_CERTIFICATE_ARN` (see docs/DEPLOY_AWS.md).

## [0.5.1] - 2026-11-20

Small fixes left over from 0.0.2–0.5.0.

### Fixed
- **Planning board**: Undo after moving a person off a job puts back their planned hours, and the Undo button can only
  be clicked once. Cards show the job's status.
- **Targets editor**: choosing days before typing a number no longer jumps back to hours, and decimals such as 1.5 days
  can be typed (1.5 days used to show as 36 h).
- **Exports**: the work order and request deadline columns come out as separately named columns (Due, Due date and
  time, Completion, Response) instead of several columns all called "Due".
- **Technician app**: a hold note sent for a job that is already on hold is kept as a comment. Completing a job that was
  already completed no longer claims notes were added, and completing again after a refusal doesn't send the same
  signatures twice.
- **Assets**: bulk changes use the same bar as the other lists, with the same limit of 500.
- **Settings › Notifications**: quick clicks on several switches no longer undo each other, saving the Organization tab
  no longer puts back old e-mail settings, and Cancel in your own e-mail settings throws away unsaved changes.
- **Alert bell**: the low-stock count leaves out inactive parts.
- **Work order filters**: changing the site clears the asset and person filters, and an asset in the page address
  shows its name in the asset filter.
- "1 work order created" (was "1 work orders created").
- A very far "created to" date in the work order filter no longer gives a server error.

## [0.5.0] - 2026-11-20

Response and completion targets per priority, and a planning board. Nothing changes for an organization until someone sets
targets (Settings › Organization), apart from due dates now meaning "by the end of that day". See
[docs/PLANNING_AND_TARGETS.md](docs/PLANNING_AND_TARGETS.md).

### Added
- **Targets by priority** (Settings › Organization): *respond within* and *complete within*, in hours or days, for
  each priority. New requests and work orders get their deadlines from them, counting every hour. A deadline typed on
  a job always wins; changing a target never moves deadlines already set.
- **Due date and time** on work orders. Lists show "due in 3 h", "2 h late" or "done in time", in orange when due soon
  and red when late; a *Late* filter on work orders and a *Late* tab on requests.
- **Responses**: a request is answered when it is approved or rejected; a work order when work first starts (In
  progress, On hold, a ticked step or logged hours — the phone's time for offline work). Work orders and requests show
  their response deadline and whether it was met.
- **Dashboard**: late work, due soon, and waiting for a response. The alert bell and the backlog report use the due
  time.
- **Report "Targets met"** (Reports › Work): responses and completions met per month or per priority.
- **Planning board** (Maintenance › Planning): week or month, by technician or by asset. Drag a card to another day or
  person (with Undo); each person's day shows planned hours against their working hours (red when over); trays for
  work from earlier days and work without a date; upcoming PMs as dashed cards with *Generate now*.

### Changed
- A date-only due date means the end of that day in the organization's time zone; existing due dates were filled in
  that way. **Check Settings › Organization › Time zone** (now a list): if it was left on UTC, deadlines are an hour
  or two off.
- Approving a request: leave the due time empty to use the priority's target (counted from when the request was
  raised, even if you change the priority); *Use the requester's date* fills in their "Needed by".
- The job card prints the due date and time; the phone app shows the time when a job is due at a time of day.

## [0.4.1] - 2026-11-20

Found in the 0.4.0 phone check: with no signal, the full app's work order page showed "Failed to fetch".

### Fixed
- **No connection is now said plainly.** When the full app can't reach the server it says "No connection. Check your
  signal and try again." instead of the browser's "Failed to fetch", and an orange *You're offline* bar explains that
  the full app needs a connection (with a link to the technician view, which keeps working offline).
- **The technician view notices at once when the phone loses its network** (airplane mode): it shows *Offline* and
  hides the links into the full app (*Full app*, *Issue parts*, *New work order*) instead of waiting for the next sync
  attempt. The Android app now has permission to see the network status, which it needs for this.

## [0.4.0] - 2026-11-20

The technician app. Technicians on plant floors, in basements and at remote sites can do their jobs on a phone with no
signal; everything they do is sent when the phone is back online. See [docs/TECHNICIAN_APP.md](docs/TECHNICIAN_APP.md).

### Added
- **Technician view** at `/tech` (the Android app opens it). *My jobs* shows the open work orders assigned to me, kept
  on the phone. On a job: tick checklist steps and enter readings, log hours, add photos (camera or gallery) and
  comments, start, put on hold, and complete it with my signature and, if wanted, the customer's or supervisor's.
  Works the same with or without a connection.
- **Sync** page: what is waiting to be sent, what was refused and why, *Refresh now*, and *Apply mine anyway* when an
  office user changed the same checklist step in the meantime. A change is never sent twice, and a phone that stays
  offline keeps its changes (even if the app is closed) until it reconnects.
- **QR scanning** in the technician view: scan an asset or location label (or type its number) to see its open jobs,
  enter a meter reading or raise a request, also offline.
- **QR labels** for assets, locations and parts: *Print labels* on the asset and parts lists (select rows first; needs
  edit permission) and on a location (with or without everything below it). An A4 sheet of 3 × 8 labels with the QR
  code, number and name. A label scanned with any phone camera opens the record after signing in.
- **Signed-in devices**: *Signed-in devices* in the user menu lists my phones and browsers, with *Sign out* and *Sign
  out all other devices*; administrators can do the same for any user (Settings › Users). A signed-out device stops
  working within a minute.
- **Signatures** captured on completion are shown on the work order and printed on the job card.
- *Technician view* in the user menu for accounts linked to an employee.
- **Sign out** in the technician view. Signing out (there or in the full app) removes that person's jobs from the
  phone; their unsent changes stay on it until they sign in there again, and are never sent under someone else's name.

### Changed
- On the Android app and in the technician view, *Remember me* keeps you signed in for 30 days, so a phone that is
  offline for days can still send its changes. Office browsers are unchanged.
- A technician view that has had no contact with the server for 30 days locks until you sign in again.
- Phones signed in before 0.4.0 keep their 12-hour sign-in: sign out and in again once to get 30 days.

## [0.3.1] - 2026-11-20

The first step of the technician app (0.4.0): the Android app can open without a connection.

### Changed
- **The Android app opens through a technician start page** that is kept on the phone. With a connection it goes
  straight on to the full app, as before. With no connection it opens from the phone's copy and says
  "Offline check passed" instead of "You're offline". Office browsers are not affected: only pages under `/tech` are
  kept on the device.
- **The Android app can use the camera** (asked the first time it's needed), for QR scanning and photos in 0.4.0. The
  start page has a *Test the camera* button.

### How to check on a phone
1. Install this APK and open it once with a connection (it goes on to the full app).
2. Turn on airplane mode, close the app completely and open it again: it should show *Offline check passed* within a
   few seconds.
3. Tap *Test the camera* and allow it: the camera picture should appear.

## [0.3.0] - 2026-11-20

Planned parts. List the parts a job needs before anyone goes to the store, keep that stock for the job, and see what is
really available. Nothing changes for an organization until someone plans parts.

### Added
- **Planned parts on work orders** (Parts tab): part, store and quantity, with what has been issued, what is still
  reserved and any shortage. The store defaults to the one on the work order's site with the most available stock (or a
  central store on another site). When stock can't cover every job, the job due earliest gets it first; later jobs
  show "short". Completing, closing or cancelling a work order releases what it still reserves; reopening brings it back.
- **Issue anyway?** Issuing or transferring stock that another job has reserved asks first and lists the work orders
  that would go short. The job due first never gets asked when it takes its own parts.
- **Planned hours per technician** (Labour tab) and a **planned against actual cost** table (Costs tab) for labour,
  parts and the total. Without planned hours, planned labour uses the work order's estimate.
- **Parts lists on PM tasks.** Every work order generated from the task arrives with its parts planned and reserved
  (multiplied by the number of assets on a round).
- **Estimated cost per visit** on PM schedules, and per row on the PM forecast with the total.
- **Reserved and available stock** on the part page, which also lists the open work orders holding the part; the parts
  list has an *Available* column (and *Reserved*, hidden by default).
- The job card prints the planned parts with a column for what was used.
- Duplicating a work order copies its planned parts and planned hours.
- The 4CSQL importer brings in PM parts lists (`checklistInv`).

### Changed
- **Re-order checks use available stock** (on hand less what open work has reserved): the parts list's *below re-order
  level* filter, the alert bell, the purchase proposal and the *Stock below re-order level* report. The proposal also
  orders enough to cover planned work beyond stock and open orders, even where no maximum level is set. With nothing
  planned, every figure is as before.
- A part that is planned on a work order or PM task can't be deleted; remove it from them or mark it inactive.

## [0.2.0] - 2026-11-20

The list toolkit: work orders, work requests, PM schedules, parts, purchase orders and assets now share the same tools.

### Added
- **Columns:** show or hide columns on every one of these lists. Your choice is saved with your account, so it follows
  you to any browser or phone.
- **Saved views:** save a list as a named view (its filters, tab, sort, page size and columns), update, rename or
  delete it, and mark one view per list to open by default when you open the list without filters. Views are personal.
- **Filter chips** under the filters show each active filter in words; the × removes it.
- **Export to Excel (.xlsx) or CSV** from every list: all rows that match the filters (up to 10,000), with the columns
  you show, in the same order and with the same titles. In Excel, dates are dates and numbers are numbers.
- **Bulk actions** on ticked rows (up to 500 at a time). Rows that can't change are listed afterwards with the reason:
  - work orders: change status (with the same rules as one at a time, including the completion rules), set priority,
    add technicians (they get the usual e-mail);
  - PM schedules: pause and resume (resuming starts the schedule again from today);
  - parts: mark active or inactive;
  - assets: status, criticality and category, as before.
- New columns you can switch on: work order type, trade, site, location and completed date; request type, site,
  location, asset and needed-by date; PM site, last generated and open work orders; part manufacturer, group,
  critical and status; purchase order site, requester and number of lines.
- The work request list has a search box, a site filter and sortable columns.

### Changed
- The PM schedule list keeps its filters in the address, like the other lists, so it can be bookmarked and saved.
- The asset export follows the columns on screen. Called without columns (for example from a script), it now lists
  every field under its field name instead of the old fixed headings.
- Exports hold up to 10,000 rows; the asset export used to go up to 50,000. The export menu says when more rows match.
- The asset list's column choice moves from the browser to your account (carried over automatically the first time).

## [0.1.0] - 2026-11-19

E-mail notifications. They are **off** for every organization until an administrator switches them on in
Settings › Notifications, and the server needs a sending address first (see *Sending e-mail* in
[docs/DEPLOY_AWS.md](docs/DEPLOY_AWS.md)).

### Added
- **E-mail notifications** for the moments that matter:
  - new work requests to the people who review them;
  - the requester when a request is approved (with the work order number), not approved (with the reason) or done;
  - technicians when work or PM work is assigned to them, and when parts ordered for their job arrive;
  - whoever created a work order when it is completed;
  - approvers when an order within their approval limit is submitted, and the buyer when it is approved or returned.
  Nobody gets an e-mail about something they did themselves, and nobody gets the same e-mail twice. Replies go to the
  person who acted.
- **Send to supplier** on approved purchase orders: e-mails the whole order, with every line and the totals.
- **Settings › Notifications**: switch e-mail on, choose which e-mails go out, send yourself a test e-mail and read the
  e-mail log (sent, waiting or failed, with the reason).
- **Your own e-mail settings** in the user menu: each person can switch off the e-mails they don't want.
- E-mails are queued with the change that causes them and sent in the background within about a minute; a failed
  e-mail is retried up to five times and never holds up anyone's work.
- **Amazon SES** on AWS (the deployment creates the sending identity for your domain and shows its DNS records), or any
  SMTP server. Local Docker runs include a test mailbox at http://localhost:8025.
- While public sign-up is open, only organizations the server's operator lists may send e-mail, so a stranger can't
  send mail from your domain.

## [0.0.2] - 2026-11-19

Quick wins from the DimoMaint comparison. New rules are **off** until an administrator switches them on in
Settings › Organization, so nothing changes for existing organizations on its own.

### Added
- **PM generation rules:** don't generate PM work for assets that are under repair, inactive or in storage (you
  choose which); move PM work that falls on a non-working day to the next working day while the schedule keeps its
  dates; don't generate new PM work while the previous work order is still open. An occurrence is only skipped once it
  falls due: work generated ahead (lead time, Generate now) waits until then. Skipped and waiting occurrences are listed
  with the reason in the PM preview.
- **Completion rules:** require labour, every checklist step ticked, or a failure code on breakdown work before a work
  order can be completed.
- **Work starts automatically:** logging labour or ticking a checklist step on an Open work order moves it to In
  progress, with the reason in its history.
- **Job card:** print any work order as a job card with its checklist, blank rows for labour and parts, completion
  lines and signature lines.
- **Duplicate a work order** with its checklist, assignees and asset (dates, status and bookings are not copied).
- **More work order filters:** location (with sub-locations), asset, assignee, created and due date ranges, and
  source (from PM, from a request, raised directly).
- **Alert bell** in the header: overdue work orders (and how many are yours), requests waiting for triage and parts at
  or below re-order level, each opening the filtered list.
- **Generate now:** create the next PM work order straight from the forecast or the schedule page.

## [0.0.1] - 2026-11-19

### Security
- **Attachments can no longer run script in the app.** An uploaded SVG (or other file a browser treats as a page)
  opened from an attachment ran its script as the signed-in user, in the web app and in the Android app. Only photos
  (PNG, JPEG, GIF, WebP, BMP, AVIF) and PDFs now open in place; every other file, SVG included, is downloaded. All
  attachment responses forbid script (`Content-Security-Policy`), and everything except PDFs shown in place is also
  sandboxed. Files uploaded before this fix are covered too.
- The stored type of an uploaded file now follows its file extension instead of what the browser reported.

### Changed
- **Release tags come from GitHub.** After a release deploys, the deploy workflow tags the commit `vX.Y.Z` itself,
  so a release needs only a push of `main`; `scripts/release.sh` no longer tags and refuses a version already tagged
  on GitHub.

## [0.0.0] - 2026-11-19

The first numbered release: everything built so far.

### Added
- **Platform:** organizations (tenants), users, roles and permissions, audit trail, file attachments, settings and
  terminology.
- **Locations:** sites and an unlimited-depth location tree.
- **Assets:** hierarchy, categories, custom fields, warranty, financials, transfers, photos and documents, bulk edit,
  CSV export.
- **People:** employees and contractors with trades, rates and sites; trades; failure codes.
- **Work requests:** request, triage, approve into a work order or reject; duplicate and warranty checks.
- **Work orders:** full workflow, assignees, task checklists, labour and other costs, completion details, downtime,
  activity feed and files.
- **Preventive maintenance:** PM tasks and schedules on an asset, a location or a **round of several assets** (one
  work order per cycle with the checklist per asset); fixed interval, nth weekday, fixed date and meter-based
  schedules; multiple PM; preview, automatic generation and forecast; meters and readings. Checklist steps record a
  reading and remarks.
- **Inventory:** stores, part groups, parts with store levels, alternates, shelf-life batches; receive, issue, return,
  adjust, transfer and stock take; moving average cost.
- **Purchasing:** suppliers, purchase orders with approval limits, currencies, tax, partial receipts, re-order
  proposal, printable PO.
- **Reports:** 14 reports with KPIs, charts, CSV export and print.
- **4CSQL importer:** locations, assets, people, work history, PM (including multi-asset and section PMs), PM step
  results, meters, inventory and purchasing; verified on 4CSQL 6.108.1.1 with three customer databases.
- **Deployment:** Docker image, GitHub Actions CI, AWS (ECS Fargate, RDS for SQL Server, S3, CloudFront) live in
  eu-west-2, Azure templates prepared.
- **Android app:** an app shell that opens the cloud app, with downloads, file exports, printing, the back button and
  an offline message.
- **Versioning:** this changelog, the `VERSION` file and `scripts/release.sh`.
