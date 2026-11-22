# CMX 0.11.0 — Phone app: parts, stock and stops (design)

Status: approved as the roadmap describes it, with the four findings the user saw before saying "go ahead" (push needs
Firebase — now created; fingerprint and push need the new app; stock counts need a saved count the office approves;
videos and documents up to 50 MB). Calls I made are marked **[call]**.

## Why

On the phone a technician can see planned parts but not use them, can't add a part they found they needed, can't count
stock, and can't end a stop without a signal. Store keepers can't work from the shelf. Phones get no alerts.

## Goals

1. **Parts used on a job** — mark planned parts used or change the quantity; add an unplanned part (scan or search) from
   the job's store or another; offline; issued against the job's reservation when sent, with the web's stock rules; a part
   short in the store shows a warning on the phone and appears in the store keeper's list.
2. **Stock on the phone** — scan or search a part: photo, stock in each store, bin. Issue to a work order and receive
   against a purchase order line by scanning. Stock counts: the office opens a count, people count on the phone (offline
   too), the office reviews the differences and posts them.
3. **Stops from a job** — start or end the asset's stop from the job with an editable time; offline uses the phone's time.
4. **More on a job** — add a checklist from a PM task; attach videos and documents (up to 50 MB); a crew leader logs hours
   for other technicians (new permission); cancel a job with a reason; create a work order on the phone (with the
   permission).
5. **Unlock and alerts** — fingerprint/face unlock after the first sign-in (the 30-day sign-in stays); push notifications
   on Android when a job is assigned or a request is approved.

## Non-goals

- iOS (no iPhone app exists). Push to the web browser.
- Changing the office's one-step stock-take (it stays; counts are an addition).
- Templates for checklists (0.12.0). Editing a part, store or PO on the phone.
- Push for other events (the e-mail grid stays the place for those) **[call]**.

## Anti-goals

- Stock that goes negative or differs from what the web would allow, because a change came from a phone.
- A part the technician physically took that the system silently forgets.
- A phone change applied twice.
- Push or fingerprint breaking older installed apps (they keep working without those).
- The Firebase server key in the repository, logs or answers.

## How the phone works today (relevant facts)

- The Android app is a Capacitor shell that opens the live `/tech/` web app; changes are queued offline (IndexedDB outbox)
  and sent to `POST /api/tech/actions` as typed changes applied once per (user, client id); files go to `/api/tech/files`.
- The job bundle (`/api/tech/bundle`) and places (`/api/tech/places`) are versioned downloads.
- Stock moves go through `StockLedger` (issue, return, receipt, adjustment); planned parts reserve stock
  (`Reservations`); the web asks "Issue anyway?" before taking stock reserved for other work.
- Native features (fingerprint, push) need plugins compiled into the APK; the web app reaches them through the Capacitor
  bridge (`window.Capacitor`) only when running inside the new app.

## Design

### Parts used on a job

- New phone download `/api/tech/stock` (versioned like places): active parts of the stores of the person's sites
  (id, number, name, unit, has photo, per store: on hand, reserved for other work, bin), the stores, the cancel reasons,
  PM tasks (code, name, steps), and — for people with `purchasing.receive` — open purchase orders with their lines.
  Shared by the job page, the part page, counts and receiving **[call]**.
- Bundle jobs gain planned lines with ids, part, store, planned and issued quantities, and the job's default store (the
  site's default store, 0.9.0).
- Phone changes (all need `workorders.execute` and a job that is open):
  - `part.use` {partId, storeId, quantity, lineId?}: issue to the job. Issued first against the job's own reservation.
    If it would take stock reserved for other work it is still issued (the part is in the technician's hand) and the
    answer says which work orders are now short **[call]**. If the store's stock is less than the quantity, what is
    there is issued and the rest becomes a planned line on the job (so it is short and on the store keeper's list); the
    answer says so **[call]**. Batch/shelf-life parts: the server picks batches the way the web does (first to expire).
  - `part.return` {partId, storeId, quantity}: return to the store, never more than was issued to the job.
- "Mark used" on a planned line sends `part.use` for the line's remaining quantity (editable).
- The phone warns before sending when its copy shows the store can't cover the quantity.
- **Store keeper's list:** Inventory › Short for work — planned lines on open work orders that their store can't cover
  (from `Reservations.Shortages`), with work order, part, store, short quantity and due date; filter by store. **[call]**

### Stock on the phone

- Part page: scan a part label or search the copy; shows photo (online), stock per store, bin, and buttons for what the
  person may do.
- `stock.issue` {partId, storeId, quantity, workOrderId}: needs `inventory.transact`; same rules as `part.use` (any open
  work order the person can see; offline only to jobs in their bundle).
- `po.receive` {purchaseOrderId, lineId, quantity, storeId?}: needs `purchasing.receive`; the same rules as receiving on
  the web (not more than outstanding unless the web allows it; the line's store).
- **Stock counts** (new): `StockCount` (store, name, status Open → Submitted → Posted, or Cancelled; who/when) with
  `StockCountLine` (part, counted quantity, counted by/at; one line per part, the latest count wins).
  - Office: Inventory › Stock counts — open a count for a store; see lines with expected (on hand now), counted and the
    difference; post (adjustments through the same code as today's stock-take) or cancel. `inventory.transact` to post,
    `inventory.view` to count.
  - Phone: open counts of my stores are in the stock download; `count.line` {countId, partId, quantity} queued offline.
    A count that was posted or cancelled meanwhile rejects the line with a clear message.

### Stops from a job

- `downtime.start` {assetId, at?, reason?} and `downtime.end` {assetId, at?}: `workorders.execute`. Time = the chosen
  time, else when the change was made; never in the future, not before the stop started. Starting from a job links the
  stop to the job (as "The asset is down" does); ending uses the same code as Back running.
- The job page shows the asset's stop with Start/End and an editable time, also offline.

### More on a job

- `tasks.fromPm` {pmTaskId}: appends that PM task's steps to the job's checklist (`workorders.execute`).
- Files: `/api/tech/files` accepts videos (mp4, mov, webm) and documents (pdf, Word, Excel, text) up to 50 MB, photos stay
  up to 10 MB; queued offline like photos **[call: 50 MB]**.
- New permission `workorders.logForOthers` ("Log hours for others"), given to Maintenance Manager in new organizations
  **[call]**; `labor.add` accepts an employee id when the person has it (any active employee) **[call]**.
- Cancel from the phone: `status.change` to Cancelled with a reason and note, needing `workorders.close` (as on the web).
- `workorder.create` {title, description, priority, type, siteId, locationId?, assetId?, assignToMe}: needs
  `workorders.create`; numbered on the server; assigned to the technician when asked (default on) **[call]**.

### Unlock with fingerprint or face

- The APK gains a biometric plugin. In the phone app's menu: "Unlock with fingerprint or face" (shown only inside the new
  app on a phone that supports it). When on, opening the app or returning after 5 minutes away shows a lock screen
  that asks for the fingerprint/face; "Sign out" is offered there. The setting lives on the phone **[call: 5 minutes]**.

### Push notifications

- The APK gains the push plugin and the Firebase config (copied from `_private/firebase/google-services.json` at build
  time, like the signing key; never committed).
- The phone app registers when it starts inside the new app (Android asks permission once) and sends the token to
  `POST /api/tech/push-token`; it is stored on the person's signed-in session (signing out removes it).
- Server: `PushMessage` outbox rows queued with the change (job assigned — technicians with a CMX login, also through a
  team; request approved — the requester when they have a login), only for people who can see the record, in their
  language; a background sender delivers them through FCM HTTP v1 and retries; tokens Firebase says are gone are removed.
- Configured by `Push:ServiceAccountJson` (the server key file's contents, as a secret on AWS). Not configured = nothing
  is queued or sent **[call]**.

## Constraints

- .NET 10 / EF Core 10 / SQL Server; React 19 / Mantine 8; Capacitor 8 Android; every string in English and Arabic.
- 0.9.0 access applies to every phone download and change; organization-wide decisions use `AllSites()`.
- Older app versions keep working: new bundle fields are optional, new change types are only sent by the new web app.
- The server key never reaches the repository, logs or API answers.

## Left to the builder

Screen layouts on the phone, plugin choice for biometrics (a Capacitor 8 compatible one), query shapes, test organisation.
