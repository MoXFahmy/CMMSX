# 0.4.0 Technician app (MO-1, AS-1, WO-9) — design

Source: DimoMaint report items MO-1 (technician app), AS-1 (QR labels and scanning) and WO-9 (signatures), planned as
release 0.4. Agreed with the user on 2026-11-20. Lines marked *(builder's call)* were settled by the builder and can be
checked.

## Why

Technicians work where the signal is poor or missing: plant floors, basements, remote sites. Today the Android app is a
shell around the cloud app and shows "you're offline" when there is no connection, so work is recorded later on paper
or not at all. 0.4.0 gives technicians a phone-first view that keeps working offline and sends their work when signal
returns, opens machines by scanning a QR label, takes photos and signatures, and lets an administrator sign a lost
phone out.

## Decisions made with the user

- **It must work offline and sync later** ("they work in places with poor or no signal … and need to keep working
  offline and sync later").
- **What works offline and what stays online**, as played back and accepted ("seems good start, go ahead") — see
  *Scope* §2 and §3.
- The user left the building approach to the builder: **approach A** (below).

## Scope

### 1. The technician view

A phone-first part of the web app at `/tech`, for people whose account is linked to an employee record (`Employee.UserId`).

- **My jobs:** open work orders (Open, In progress, On hold) assigned to me — overdue first, then due today, then the
  next 7 days, then the rest without a due date *(builder's call on the 7-day window)*. Each row: number, title, asset,
  priority, due date, status, and a mark when it has unsent changes. Search by number, title or asset.
- **Job page:**
  - number, title, priority, status, due date; asset and location with their notes to the technician; description;
  - checklist (tick, reading, remarks; grouped per asset on PM rounds);
  - planned parts (read only, for information);
  - my hours: log date, hours, overtime, notes;
  - photos: take with the camera or pick from the gallery; existing attachments listed;
  - comments: read and add;
  - actions: **Start**, **Put on hold** (reason), **Complete** (completion notes, cause, failure code, asset was down and
    downtime, the organization's completion rules, signatures — §5).
- **Scan:** live camera QR/barcode scanning, plus typing an asset number. It opens the asset or location from the phone's
  copy: its open jobs (mine first), its meters (enter a reading), and **Raise a request** (title, description, priority).
  An unknown code says so.
- **Sync:** a badge in the header shows how many changes are waiting. The sync page lists them (waiting, sending,
  couldn't send + reason) with *Retry* and *Discard* for the ones that couldn't send, the time of the last refresh,
  and *Refresh now*.
- **Getting there:** a *Technician view* entry in the user menu for anyone linked to an employee; the Android app
  opens it by default for such users, and every device remembers the last choice *(builder's call)*. The technician
  view has a link back to the full app.

### 2. Offline

- **On the phone:** the app itself (cached for offline start), and for the signed-in user: my jobs with everything the
  job page shows, the assets and locations of the sites I work at (number, name, location path, notes to technician,
  meters with their last reading), failure codes and other lists the forms need. Refreshed whenever there is signal:
  on opening, every 15 minutes while open, and after each sync *(builder's call on 15 minutes)*.
- **Works offline:** everything in §1 except what is listed as online only. Each change goes into a queue on the phone.
- **Online only:** issuing parts (stock is checked live), creating work orders, changing plans, every office page.
  Opening a part's QR label offline says it opens when online. In the technician view, the job page has **Issue parts**
  (online only): it opens the existing issue dialog prefilled from the planned parts, with the 0.3.0 reservation warning.
  Creating a work order links to the full app's form *(builder's call: no phone-specific screens for these)*.
- **The queue:**
  - is kept on the phone (survives closing the app and restarting the phone, for days);
  - is sent automatically, oldest first, whenever there is signal; a change is sent once even if the connection drops
    halfway (the server recognizes a repeat by its id);
  - is tied to the user who made the changes: if someone else signs in on the phone, the queue waits for its owner;
  - photos are shrunk on the phone before queueing (longest side 1,600 px, JPEG) *(builder's call)*.
- **Applying changes on the server:** with the same rules as the full app (permissions, status workflow, completion
  rules, meter checks). Ticks, hours, photos, comments and readings add to the job, so they never clash with other
  people's work. A change that is no longer allowed when it arrives (the job was cancelled, a step was removed, a
  completion rule fails) stays on the phone as **couldn't send** with the server's reason; the technician can fix it
  and retry (e.g. complete again with a failure code) or discard it. **Nothing is dropped silently.**
- **Checklist steps are shared fields** (tick, reading, remarks). A queued change to a step applies only the fields the
  technician changed, and only if nobody changed that step on the server after the technician's change time; otherwise
  it becomes *couldn't send — changed by {name} at {time}*, with *Apply mine anyway* and *Discard* *(builder's call; steps
  get an UpdatedAt/UpdatedBy to tell)*.
- **Completing a job someone already completed** (two assignees, both offline) isn't rejected: the second completion's
  notes are added as a comment ("Completion notes from {name}") and its signatures and photos are attached, and the
  phone shows it as sent with that note *(builder's call: nothing typed is lost)*. Other moves that are no longer
  allowed (e.g. starting a cancelled job) are *couldn't send*.
- **When it happened:** a change keeps the time the technician made it (checklist tick, labour date, reading time,
  start and completion times), not the time it synced — but never later than the server's clock and never more than
  30 days back *(builder's call)*.
- **Staying signed in:** a sign-in from the Android app, or from the technician view's sign-in, with "Keep me signed in"
  ticked lasts 30 days of not using the app. Every other sign-in keeps today's 12 hours, so office browsers see no
  change *(builder's call)*. If the sign-in has ended, the queue waits for the same user to sign in again.
- **A phone out of contact:** after 30 days without reaching the server, the technician view locks its copy of jobs and
  assets until the user signs in again (the queue is kept). When the server answers that the device was signed out, the
  phone deletes its copy of jobs and assets; unsent changes stay, tied to their owner *(builder's call)*.

### 3. QR labels (AS-1)

- **Print labels** from the asset list (ticked rows), the location tree (a location and, optionally, everything under
  it) and the parts list (ticked rows): an A4 sheet, 3 × 8 labels of 70 × 37 mm, each with a QR code, the number and
  the name (assets also show their location) *(builder's call on the label size; one size in 0.4.0)*. At most 500
  labels per print.
- **What a QR code holds:** a link to the app, `<app address>/q/asset/<id>`, `/q/location/<id>`, `/q/part/<id>`, using
  the server's public address (`App:PublicUrl`, today the CloudFront address). Scanned in the technician view it opens
  the record from the phone's copy whatever the address in the link (only the `/q/…` part is read), so labels survive a
  change of domain there; scanned with any phone camera it opens the page in the browser (after sign-in), which needs
  the printed address to keep working. The deployment notes say to set up any custom domain before printing labels at
  scale *(builder's call)*.
- **Existing barcodes:** scanning a code that isn't one of these links looks it up as an asset number, then as an
  asset's or part's *Barcode* field.

### 4. Photos

- Taken in the technician view (camera or gallery) and attached to the work order as files, like attachments today.
  Shown on the work order's Files tab in the full app.

### 5. Signatures (WO-9)

- When completing in the technician view, the technician can sign, and a second person (customer or supervisor) can
  sign with their name. Both are optional *(builder's call: no organization rule to require them in 0.4.0)*.
- Saved as PNG files attached to the work order, marked as signatures, shown on the work order page and printed on the
  job card's signature lines.

### 6. Signed-in devices and sign-out (MO-1)

- Every sign-in is recorded as a device: a name from the browser or app ("Android app 0.4.0", "Chrome on Windows"),
  when it signed in and when it was last used. No IP address is stored.
- **My devices** in the user menu: list my devices and sign one out, or all others.
- **Administrators** see and sign out any user's devices from the user's page in Settings › Users.
- **Sign-ins made before 0.4.0** are not signed out: the first request after the upgrade records them as a device
  ("Signed in before 0.4.0" with the browser's name) and adds the session to the cookie, so every device can be listed
  and signed out *(builder's call)*.
- A signed-out device stops working within a minute. Changes still queued on it stay on the phone (they are never sent
  under another identity); they are sent only if the same user signs in on that phone again.

#### 7. The Android app

- Asks for the camera (live scanning, photos).
- Opens at `/tech`; people who aren't technicians are sent on to the full app. The service worker is registered with the
  scope `/tech/`, so only technician-view pages are served from the phone's copy and office pages behave as today
  *(builder's call; this also keeps a faulty cache from ever affecting the office app)*.

## Non-goals

- Offline issuing of parts, offline creation of work orders, offline office pages, other people's jobs offline.
- An anonymous request form behind the QR code (scanning still needs a signed-in user).
- Push notifications, NFC tags, an iPhone app (iPhones use the technician view in Safari).
- Pairing phones by QR code (DimoMaint's way); signing in with a password is enough.
- More label sizes, label designer, printing more than 500 labels at once.
- Requiring signatures by organization setting.

## Anti-goals

- **Never lose a technician's entry silently**, and never apply a queued change under someone else's identity.
- **Office users see no change** except the new menu items, label printing, signatures on work orders and the devices list.
- **No reinstall per release:** new releases reach phones through the cloud app.

## Approaches

1. **Offline technician mode inside the cloud web app (chosen).** A service worker keeps the app available offline, the
   phone's browser storage holds the copy of jobs and the queue, and it runs in the Android app and any phone browser.
   Every release reaches phones without a reinstall. Risk: the Android app opening with no signal at all relies on the
   phone's WebView serving the cached app; this must be checked on a real phone (the user's check; there is no emulator).
2. **Bundle the technician app into the APK** with native plugins (storage, camera, ML Kit scanner). The app always
   starts offline and scanning is native, but it is Android only, every web change needs an APK install on every phone,
   and sign-in must move from cookies to tokens. **Fallback** if the check in 1 fails.
3. **A separate native app** (Kotlin/Flutter): a second codebase for the same screens. Rejected.

## How it is built *(builder's calls unless noted)*

- **Server:**
  - `GET /api/tech/bundle` — everything the phone keeps for the signed-in employee (my jobs with details; assets,
    locations, meters of my sites; lookups), with a version so an unchanged bundle isn't re-sent.
  - `POST /api/tech/actions` — one queued change `{ clientId, at, type, workOrderId?, payload }`; idempotent per user and
    `clientId` (a table of processed ids with their result). Types: `task.update`, `labor.add`, `comment.add`,
    `photo.add` (multipart), `reading.add`, `status.change` (start, hold, complete with completion fields and
    signatures), `request.create`. Each reuses the logic of the existing endpoint (e.g. `ChangeStatusAsync`, meter
    readings that trigger PM generation). Answers: done, rejected with reason (4xx), or retry (network/5xx/401).
  - Device sessions: a `UserSession` table (user, device name, created, last seen, revoked) and a session id claim in the
    auth cookie, checked on each request (cached briefly); endpoints for my devices and, with *Manage users*, any
    user's devices.
  - Remember-me sign-in persists 30 days sliding.
- **Web:** `/tech` routes; a hand-written or plugin-built service worker caching the app shell for the current release
  and never caching `/api`; IndexedDB for the bundle and the queue; a sync engine (send loop, idempotent ids,
  retry with backoff, couldn't-send handling) written as plain TypeScript with unit tests — **add Vitest** as a
  dev-only test runner for this logic; QR decoding and encoding with small libraries; a signature pad on a canvas.
- **Android app:** `CAMERA` permission; default route for technicians.
- CHANGELOG `[0.4.0]`, `docs/` note on the technician view and the offline check; release via `scripts/release.sh 0.4.0`
  (versionCode 4001).

## Constraints

- Runs in the Android app (Capacitor 8, Android WebView) and current Chrome on Android and Safari on iOS.
- Multi-tenant query filters on every new table; server tests use `ApiFixture`; strings in `en.json`.
- Existing behaviour and tests keep working.

## Left to the builder

Screen layouts and wording; bundle shape and size limits; IndexedDB layout; service-worker strategy details; library
choices for QR and storage; refresh cadence beyond the above; how photos already on the server are cached.
