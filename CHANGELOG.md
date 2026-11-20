# Changelog

All notable changes to 4C CMMS — the cloud app and the Android app share one version number.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); numbering follows the rules in
[docs/VERSIONING.md](docs/VERSIONING.md): a new feature raises the middle number, a fix or small task the last one.

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
