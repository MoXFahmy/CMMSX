# 0.4.0 Technician App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An offline-capable technician view (`/tech`) with my jobs, checklist, hours, photos, comments, start/hold/complete
with signatures, QR scanning, meter readings and requests, a sync queue; QR label sheets; signed-in devices with sign-out.

**Architecture:** The server gains device sessions (session id kept in the auth cookie's properties), a technician bundle
(`/api/tech/bundle`, `/api/tech/places`), and an idempotent action endpoint (`/api/tech/actions`, `/api/tech/files`) that
reuses the existing work-order logic. The web app's `/tech` area (already served offline by `public/tech-sw.js` from
0.3.1) keeps the bundle and a queue of changes in IndexedDB; a pure TypeScript sync engine (unit-tested with Vitest)
sends the queue. The full app gains labels, signatures and devices.

**Tech Stack:** ASP.NET Core 10, EF Core 10, xUnit/SkippableFact + `ApiFixture`; React 19, Mantine 8, TanStack Query 5,
i18next; new web packages: `idb` (IndexedDB), `qrcode` (QR encode), `@zxing/browser` + `@zxing/library` (QR/barcode
decode, loaded lazily), dev: `vitest`.

**Spec:** `docs/superpowers/specs/2026-11-20-technician-app-0.4.0-design.md`

## Global Constraints

- Nothing a technician enters is dropped silently; a queued change is never sent under another user's identity.
- Office users see no change except: new menu items, label printing, signatures on work orders, the devices list.
- Only pages under `/tech` are served from the phone's copy (service worker scope `/tech`, from 0.3.1); `/api` is never cached.
- 30-day sliding sign-in only for sign-ins from the Android app (`4CCMMS-Android` in the user agent) or with
  `technician: true`, and only with "Keep me signed in"; all other sign-ins keep 12 hours.
- Queued change times (`at`) are clamped to [server now − 30 days, server now].
- Labels: A4, 3 × 8 labels of 70 × 37 mm; at most 500 per print; QR holds `<App:PublicUrl>/q/{asset|location|part}/{id}`.
- Photos shrunk on the phone to longest side 1,600 px, JPEG.
- After 30 days without reaching the server the technician view locks (queue kept); on a remote sign-out answer the
  phone deletes its copy of jobs and places (queue kept).
- Strings in `web/src/locales/en.json`; multi-tenant filters on every new table; existing tests keep passing.
- Server tests: `<scratchpad>/bin/wt-test [filter]` (defaults to this worktree after Task 1 step 0). Web build:
  `<scratchpad>/bin/wt-web`. Web unit tests: `<scratchpad>/bin/wt-vitest` (created in Task 4). Migrations:
  `<scratchpad>/bin/wt-ef <Name>`. `<scratchpad>` = `/private/tmp/claude-501/-Users-mohamedfahmy-Library-CloudStorage-GoogleDrive-engfahmy-gmail-com-My-Drive-01-CMMS-1000-CMX/3981ff7e-d5cc-4bc4-ab6b-ddb873a8b5d0/scratchpad`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A sign-in refresh (password change, security-stamp refresh every 5 min) must keep the device session** —
   otherwise every device silently drops off the list or gets re-adopted → test in Task 1.
2. **The same queued change sent twice** (connection lost after the server saved it) is applied once and answers the
   same result both times → test in Task 3.
3. **A queued change whose work order the user can no longer see** (deleted, another tenant's id) is rejected with a
   reason, never a 500 that would retry forever → test in Task 3.
4. **The queue of user A is never sent while user B is signed in on the phone** → Vitest in Task 4.
5. **A photo far larger than the limit, or a non-image file in `photo.add`**, is refused with a reason and stays
   "couldn't send" (not retried forever) → test in Task 3 and engine test in Task 4 (4xx = couldn't send).

---

### Task 1: Device sessions and longer technician sign-in

**Files:**
- Create: `server/FourC.Cmms.Core/Identity/UserSession.cs`, `server/FourC.Cmms.Api/Infrastructure/Sessions.cs`,
  `server/FourC.Cmms.Api/Features/DeviceEndpoints.cs`
- Modify: `CmmsDbContext.cs` (DbSet `UserSessions`), `Program.cs` (cookie events, `MapDeviceEndpoints`),
  `Features/AuthEndpoints.cs` (login), migration `TechnicianApp` (this task creates it; later tasks add to the model and
  re-generate it before their first commit with `wt-ef` after removing the previous one — one migration for 0.4.0)
- Test: `server/FourC.Cmms.Tests/DeviceTests.cs`

**Interfaces:**
- Produces:
  - `class UserSession : Entity, ITenantOwned { Guid TenantId; Guid UserId; string DeviceName (≤ 120); DateTimeOffset CreatedAt; DateTimeOffset LastSeenAt; DateTimeOffset? RevokedAt }`
  - `static class Sessions { const string Key = "sid"; string DeviceName(string? userAgent); Task<UserSession> StartAsync(CmmsDbContext db, AppUser user, string? userAgent, CancellationToken ct); Task ValidateAsync(CookieValidatePrincipalContext ctx) }`
    — the session id lives in `AuthenticationProperties.Items["sid"]` (survives `RefreshSignInAsync` and the
    security-stamp refresh). `ValidateAsync`: no `sid` → start a session named "{device} (signed in before 0.4.0)",
    set the item, `ShouldRenew = true`; revoked or missing row → `RejectPrincipal()`, sign out, response header
    `X-Session-Revoked: 1`; else touch `LastSeenAt` at most every 10 minutes. Revocation state cached ≤ 60 s.
  - `DeviceName`: `4CCMMS-Android/x.y.z` → "Android app x.y.z"; else "{Chrome|Edge|Firefox|Safari} on {Windows|macOS|Android|iPhone|iPad|Linux}", fallback "Browser".
  - `LoginRequest` gains `bool Technician = false`. Login: `CheckPasswordSignInAsync(lockout)`, `Sessions.StartAsync`,
    `SignInWithClaimsAsync(user, props)` with `IsPersistent = RememberMe` and, when RememberMe and (Android UA or
    Technician), `ExpiresUtc = now + 30 days`.
  - Routes: `GET /api/auth/me/devices` → `[{ id, deviceName, createdAt, lastSeenAt, current }]` (not revoked);
    `POST /api/auth/me/devices/{id}/sign-out`; `POST /api/auth/me/devices/sign-out-others`;
    `GET /api/users/{id}/devices`, `POST /api/users/{id}/devices/{deviceId}/sign-out` (Manage users).

- [ ] **Step 0:** point the scratchpad helpers (`wt-test`, `wt-ef`, `wt-web`) at `.worktrees/technician-app-0.4.0`.
- [ ] **Step 1: Write the failing tests** (`DeviceTests`, `[SkippableFact]`)

```csharp
Signing_in_records_a_device_named_after_the_browser_or_app()
  // login with User-Agent "…4CCMMS-Android/0.4.0" → GET /api/auth/me/devices → one item { deviceName "Android app 0.4.0", current true }
Signing_a_device_out_stops_it_within_a_minute_and_says_so()
  // two clients A, B for one user; A signs out B's device → B GET /api/auth/me → 401 with header X-Session-Revoked: 1; A still 200
Signing_out_other_devices_keeps_this_one()
A_password_change_keeps_the_device_session()          // Review Focus 1: change-password → devices still has 1 item, same id, current
Sign_ins_from_before_the_upgrade_are_adopted_as_devices()
  // /api/auth/register signs in without a session → next GET /api/auth/me/devices lists 1 device whose name ends "(signed in before 0.4.0)"
Technician_sign_ins_last_30_days_and_others_12_hours()
  // login Android UA + rememberMe → Set-Cookie expires ≥ now + 29 days; desktop UA + rememberMe → expires ≤ now + 13 hours
Administrators_see_and_sign_out_any_users_devices()   // a user without Manage users gets 403 on /api/users/{id}/devices
```
- [ ] **Step 2: Run** `wt-test DeviceTests` → Expected: FAIL (404 / no devices).
- [ ] **Step 3: Implement** the entity, `Sessions`, cookie wiring in `Program.cs`: `o.Events.OnValidatePrincipal = async c => { await SecurityStampValidator.ValidatePrincipalAsync(c); if (c.Principal is not null) await Sessions.ValidateAsync(c); }`, login change, endpoints, migration `TechnicianApp`.
- [ ] **Step 4: Run** `wt-test DeviceTests` → PASS (7); **Step 5:** `wt-test` → all pass.
- [ ] **Step 6: Commit** `feat(technician app): signed-in devices and longer technician sign-in`.

### Task 2: The technician bundle

**Files:**
- Create: `server/FourC.Cmms.Api/Features/TechEndpoints.cs` (bundle + places now; actions in Task 3)
- Modify: `Work/Work.cs` (`WorkOrderTask.UpdatedAt`, `UpdatedById`, `UpdatedByName`), `Files/Attachment` (`bool IsSignature`, `string? SignedBy` ≤ 200),
  `WorkOrderEndpoints.cs` (task PUT sets the three new fields; detail returns `tasks[].updatedAt`, attachments unchanged),
  `AuthEndpoints.cs` (`/me` returns `employeeId`), migration `TechnicianApp`
- Test: `server/FourC.Cmms.Tests/TechTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/tech/bundle` (WorkOrdersView) → `{ employeeId, userId, generatedAt, jobs: TechJob[], failureCodes: [{id, code, name}], completionRules: { notes, labour, allSteps, failureCodeOnBreakdown } }`;
    404 `{ title: "Your account isn't linked to an employee." }` when the user has no active employee.
  - `TechJob`: `{ id, number, title, description, type, priority, status, holdReason, dueDate, plannedStart, siteId,
    asset {id, assetNumber, name, notesToTechnician}?, location {id, path, notesToTechnician}?, roundAssets [{id, assetNumber, name}],
    tasks [{id, sequence, description, isDone, reading, remarks, assetId, updatedAt, updatedByName}],
    plannedParts [{partNumber, partName, unitOfMeasure, quantity, storeCode}], files [{id, fileName, contentType, isSignature}],
    comments [{body, userName, createdAt}] (latest 20), myLabor [{workDate, hours, overtimeHours, notes}] }`.
    Jobs = Open/InProgress/OnHold work orders where an assignee's `Employee.UserId` = me; at most 300, ordered by due date (nulls last).
  - `GET /api/tech/places` (AssetsView) → `{ version, assets [{id, assetNumber, name, barcode, siteId, locationId, notesToTechnician}],
    locations [{id, name, path, siteId, notesToTechnician}], meters [{id, assetId, name, unit, currentReading, lastReadAt}] }`
    for the employee's sites (`Employee.SiteIds`; empty = all sites), assets not Disposed; answers `ETag: "<version>"` and 304 for `If-None-Match` equal to it.

- [ ] **Step 1: Write the failing tests**

```csharp
The_bundle_holds_my_open_jobs_with_everything_the_job_page_shows()
  // two employees; WO1 assigned to me (Open, tasks 2, planned part, comment), WO2 assigned to the other, WO3 mine but Completed
  // → jobs == [WO1]; tasks.Count 2 with updatedAt; plannedParts[0].partNumber; comments[0].body
A_user_without_an_employee_record_gets_a_clear_answer()      // 404, title contains "linked to an employee"
Places_cover_my_sites_and_answer_304_when_unchanged()
  // employee SiteIds [A]; assets on A and B → assets only A; second call with If-None-Match → 304
Ticking_a_step_records_who_and_when()                         // PUT task isDone → detail tasks[0].updatedAt set
```
- [ ] **Step 2: Run** `wt-test TechTests` → FAIL. **Step 3: Implement.** **Step 4:** PASS (4). **Step 5:** `wt-test` → all pass.
- [ ] **Step 6: Commit** `feat(technician app): technician bundle and places`.

### Task 3: Queued changes from the phone

**Files:**
- Create: `server/FourC.Cmms.Core/Work/TechAction.cs`; extend `TechEndpoints.cs`
- Modify: `WorkOrderEndpoints.cs` (extract `internal static` helpers `UpdateTaskAsync`, `AddLaborAsync`, `AddCommentAsync`
  used by both the existing routes and Task 3), `PmEndpoints.cs` (extract `internal static RecordReadingAsync`),
  `WorkRequestEndpoints.cs` (extract `internal static CreateAsync`), `SupportEndpoints.cs` (extract the file-save part as
  `internal static SaveAttachmentAsync`), migration `TechnicianApp`
- Test: `TechTests.cs`

**Interfaces:**
- Produces:
  - `class TechAction : Entity, ITenantOwned { Guid TenantId; Guid UserId; Guid ClientId; string Type; string Status ("done"|"rejected"); string? Message; DateTimeOffset At; DateTimeOffset ReceivedAt }` — unique (`TenantId`, `UserId`, `ClientId`).
  - `POST /api/tech/actions` body `{ clientId, at, type, workOrderId?, payload }` → 200 `{ status: "done"|"rejected", message?, code? }`.
    A repeat `clientId` returns the stored answer without applying again. Types and payloads:
    - `task.update` `{ taskId, isDone?, reading?, remarks?, force? }` — conflict when the step's `UpdatedAt` > `at` and it was
      updated by someone else and not `force` → rejected, `code: "conflict"`, message "Changed by {name} at {time}".
    - `labor.add` `{ workDate, hours, overtimeHours, notes }` (employee = me) · `comment.add` `{ body }`
    - `reading.add` `{ meterId, value, note, confirm }` (meter spike guard → rejected with its message unless `confirm`)
    - `status.change` `{ status: "InProgress"|"OnHold"|"Completed", holdReason?, note?, completionNotes?, causeNotes?, failureCodeId?, assetWasDown?, downtimeHours? }`
      via `ChangeStatusAsync`; `ActualEnd` = clamped `at` for Completed. If the job is already Completed/Closed and the
      change is Completed → adds comment "Completion notes from {name}: {notes}" and answers done with `message` saying so.
    - `request.create` `{ title, description, priority, assetId?, locationId?, siteId }`
  - `POST /api/tech/files` multipart `{ clientId, at, workOrderId, kind: "photo"|"signature", signedBy?, file }` → same answer
    shape; images only (jpeg/png/webp), ≤ 10 MB; signatures stored with `IsSignature = true`, `SignedBy`.
  - Errors from the reused logic (`DomainException`, validation problems, 403, missing work order) become `rejected` with
    the message; only unexpected server errors return 5xx (the phone retries those).

- [ ] **Step 1: Write the failing tests**

```csharp
The_same_change_sent_twice_is_applied_once()                  // Review Focus 2: labor.add twice same clientId → 1 labour row; both answers "done"
A_step_changed_in_the_office_after_the_technician_is_a_conflict()
  // office ticks step at T; phone task.update with at = T − 1 min → rejected code "conflict"; same with force → done
Changes_keep_the_technicians_time_within_limits()             // task.update at = now − 2 h → DoneAt ≈ now − 2 h; at = now + 1 day → clamped to now; now − 40 days → now − 30 days
Completing_twice_offline_keeps_the_second_technicians_notes()  // complete by A, then complete by B → status Completed once, comment contains B's notes, answer done with message
Moves_that_are_no_longer_allowed_come_back_with_the_reason()   // start a Cancelled job → rejected, message non-empty
Changes_to_work_I_cannot_see_are_rejected_not_retried()        // Review Focus 3: another tenant's workOrderId → rejected (200 with status rejected)
Meter_readings_and_requests_from_the_phone()                   // reading spike → rejected unless confirm; request.create → request exists with asset
Photos_and_signatures_attach_once_and_bad_files_are_refused()  // Review Focus 5: photo twice same clientId → 1 attachment; signature IsSignature + SignedBy; text/plain → rejected; 11 MB → rejected
```
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (extract helpers first, keeping existing routes' behaviour; existing tests must stay green).
- [ ] **Step 4:** PASS (8). **Step 5:** `wt-test` → all pass. **Step 6: Commit** `feat(technician app): queued changes from the phone`.

### Task 4: Web tooling, phone storage and the sync engine

**Files:**
- Modify: `web/package.json` (+ `idb`, `qrcode`, `@types/qrcode`, `@zxing/browser`, `@zxing/library`, dev `vitest`; script `"test": "vitest run"`), lock file (install in the Node container)
- Create: `<scratchpad>/bin/wt-vitest` (like `wt-web`, runs `npx vitest run`), `web/src/tech/store.ts`, `web/src/tech/outbox.ts`,
  `web/src/tech/outbox.test.ts`, `web/src/tech/api.ts`, `web/src/tech/photos.ts`

**Interfaces:**
- Produces:
  - `type QueuedChange = { clientId: string; userId: string; seq: number; at: string; type: string; workOrderId?: string; payload: unknown; fileKey?: string; state: 'waiting'|'sending'|'failed'; message?: string; code?: string; tries: number; nextTryAt?: number }`
  - `interface OutboxStore { list(userId): Promise<QueuedChange[]>; put(c): Promise<void>; remove(clientId): Promise<void>; file(key): Promise<Blob|undefined>; removeFile(key): Promise<void> }`
  - `type SendResult = { kind: 'done'; message?: string } | { kind: 'rejected'; message: string; code?: string } | { kind: 'retry' } | { kind: 'signedOut'; revoked: boolean }`
  - `async function drain(store: OutboxStore, userId: string, send: (c: QueuedChange, file?: Blob) => Promise<SendResult>, now: () => number): Promise<{ sent: number; failed: number; stoppedBy?: 'retry'|'signedOut' }>`
    — oldest `seq` first; `failed` items are skipped (they wait for Retry/Discard); `retry` stops the drain and sets
    `nextTryAt` with backoff 5 s · 2^tries capped at 5 min; `signedOut` stops without touching the item.
  - `function retryNow(c: QueuedChange, force?: boolean): QueuedChange` (back to waiting; `force` sets `payload.force = true`)
  - `store.ts`: IndexedDB `cmms-tech` v1 — stores `bundle` (key userId), `places` (key userId), `outbox` (key clientId, index `byUser`), `files` (key), `meta` (`lastContactAt`, `userId`); `idbOutboxStore(): OutboxStore`; `wipeCopy(userId)` (bundle + places only).
  - `api.ts`: `fetchBundle()`, `fetchPlaces(etag?)`, `sendChange(c, file?) : Promise<SendResult>` (fetch → 200 maps the JSON answer, 401 → signedOut with `revoked` from `X-Session-Revoked`, 4xx → rejected with the problem title, network/5xx → retry).
  - `photos.ts`: `shrink(file: Blob, maxSide = 1600): Promise<Blob>` (canvas → `image/jpeg` 0.85).

- [ ] **Step 1: Write the failing Vitest tests** (`outbox.test.ts`, in-memory `OutboxStore`)

```ts
it('sends oldest first and removes what the server took')
it('keeps a rejected change as failed with the reason and goes on to the next')
it('stops on retry and backs off 5 s, 10 s, 20 s … up to 5 minutes')
it('never sends another user\'s changes')                       // Review Focus 4: store has A's and B's; drain(store, 'B', …) sends only B's
it('stops without losing anything when signed out')
it('retryNow with force marks the payload')
```
- [ ] **Step 2: Run** `wt-vitest` → FAIL (module missing). **Step 3: Implement.** **Step 4:** PASS (6); `wt-web` → builds.
- [ ] **Step 5: Commit** `feat(technician app): phone storage and sync engine`.

### Task 5: The technician view

**Files:**
- Create: `web/src/tech/TechApp.tsx` (layout: header with sync badge and online dot; bottom nav My jobs / Scan / Sync;
  routes), `useTech.ts` (bundle from IndexedDB, refresh on open / every 15 min / after a drain; drain on `online` events
  and every 30 s while waiting), `MyJobsPage.tsx`, `JobPage.tsx`, `CompleteSheet.tsx`, `SignaturePad.tsx`, `SyncPage.tsx`, `LockedPage.tsx`
- Modify: `web/src/App.tsx` (`/tech/*` → `TechApp`; remove `TechCheckPage` routing), `web/src/tech/TechCheckPage.tsx` (keep
  `registerTechWorker`, delete the page), `web/src/pages/AuthPages.tsx` (login sends `technician: true` when `next` starts with `/tech`), `en.json`

**Interfaces:**
- Consumes: Task 4 (`drain`, stores, api, `shrink`), Task 2/3 shapes.
- Behaviour pinned by the spec: start page — online and signed in with an employee and no device choice "full" → stay;
  no employee → `/`; not signed in → `/login?next=/tech/`; offline → open from the copy. Local edits show immediately
  (the job page overlays the queue on the bundle). Lock after 30 days without contact; wipe copy on `revoked`.
  Complete sheet: completion notes, cause, failure code (required when the org's rule says so and type is Breakdown),
  asset was down + downtime, technician signature, second signature with name; queues files first, then the status change.
  Sync page: list with states, Retry / Apply mine anyway (conflict) / Discard (confirm), last refresh time, Refresh now.
  Job page, online only: *Issue parts* opens the existing `StockTxModal` prefilled from the job's planned parts (0.3.0
  warning included); *New work order* links to the full app's form. Both are hidden while offline.

- [ ] **Step 1: Implement**; **Step 2:** `wt-web` → builds; `wt-vitest` → still green.
- [ ] **Step 3: Hands-on check** (local container on 8093, desktop Chrome via Claude in Chrome — the built-in pane can't run
  service workers): sign in as a technician, open `/tech`, see my jobs; stop the server; tick a step, log hours, add a
  photo, complete with a signature → queue shows 4+; start the server → queue drains; full app shows the changes and
  signature. Conflict path: office ticks a step after the phone's change time → Sync shows "Changed by …".
- [ ] **Step 4: Commit** `feat(technician app): technician view`.

### Task 6: Scanning, places, meters and requests

**Files:**
- Create: `web/src/tech/scan.ts` (`parseCode(text): { kind: 'asset'|'location'|'part'; id: string } | { kind: 'code'; code: string }` — reads only the `/q/{type}/{guid}` path, any host), `web/src/tech/scan.test.ts`, `ScanPage.tsx` (lazy `@zxing/browser`, camera environment, typing a number), `PlacePage.tsx` (asset/location card: open jobs here — mine first, meters with *Enter reading*, *Raise a request*)
- Modify: `TechApp.tsx` routes, `en.json`

**Interfaces:**
- Consumes: places from Task 2, `reading.add` / `request.create` from Task 3.
- `lookupCode(places, code)`: asset number (case-insensitive), then asset barcode; part codes and part links → "opens when online" (online: open the full app's part page).

- [ ] **Step 1: Failing Vitest** `scan.test.ts`:
```ts
it('reads asset, location and part links on any host')      // https://x.cloudfront.net/q/asset/<guid> and https://cmms.cworks.com.eg/q/location/<guid>
it('treats anything else as a code')                         // "AST-00012" → { kind: 'code', code: 'AST-00012' }
it('finds a code as an asset number, then a barcode')
```
- [ ] **Step 2:** FAIL → **Step 3: Implement** → **Step 4:** PASS; `wt-web` builds. **Step 5:** hands-on: scan a QR shown on screen (desktop camera or the printed label from Task 7) and type a number. **Step 6: Commit** `feat(technician app): scanning, places, meters and requests`.

### Task 7: Labels, signatures, devices and the full app's entry points

**Files:**
- Create: `server/FourC.Cmms.Api/Features/LabelEndpoints.cs` (`POST /api/labels` `{ type: "asset"|"location"|"part", ids, includeChildren? }` → `[{ id, number, name, detail, url }]`, ≤ 500, permission by type), `web/src/pages/labels/LabelsPage.tsx` (A4 3 × 8, 70 × 37 mm, QR via `qrcode`), `web/src/pages/settings/DevicesModal.tsx`, `web/src/pages/QrRedirect.tsx` (`/q/:type/:id` → asset / location / part page)
- Modify: `AssetsPage.tsx` (bulk card: *Print labels*), `InventoryPage.tsx` PartsTab (BulkBar action *Print labels*), `LocationsPage.tsx` (*Print labels*, with *Include sub-locations*), `AppLayout.tsx` (user menu: *Technician view* when `me.employeeId`, *My devices*), `SettingsPage.tsx` users (row action *Devices*), `WorkOrderDetailPage.tsx` (signatures card on Details), `WorkOrderPrintPage.tsx` (signature images on the signature lines), `CHANGELOG.md` (`[0.4.0]`), `docs/TECHNICIAN_APP.md` (offline behaviour, the phone check, "set up a custom domain before printing labels at scale")
- Test: `server/FourC.Cmms.Tests/TechTests.cs` (labels)

- [ ] **Step 1: Failing tests**
```csharp
Labels_carry_the_public_link_and_respect_the_limit()
  // 2 assets → url == "https://cmms.test/q/asset/{id}", number, name, detail = location path; 501 ids → 400
Location_labels_can_include_everything_below()               // parent + child + grandchild with includeChildren → 3
```
- [ ] **Step 2:** FAIL → **Step 3: Implement** → **Step 4:** PASS; `wt-test` all pass; `wt-web` builds.
- [ ] **Step 5: Hands-on:** print labels for 3 assets (print preview), scan one in the technician view, signatures visible on the work order and job card, *My devices* signs out a second browser.
- [ ] **Step 6: Commit** `feat(technician app): labels, signatures, devices; changelog`.
