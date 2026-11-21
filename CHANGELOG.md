# Changelog

All notable changes to CMX (formerly 4C CMMS) — the cloud app and the Android app share one version number.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); numbering follows the rules in
[docs/VERSIONING.md](docs/VERSIONING.md): a new feature raises the middle number, a fix or small task the last one.

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
