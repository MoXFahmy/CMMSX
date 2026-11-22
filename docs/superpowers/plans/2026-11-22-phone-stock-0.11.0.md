# Phone App: Parts, Stock and Stops (0.11.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parts used on jobs, stock work and counts, stops, checklists, files, crew hours, cancel and create on the phone (offline); fingerprint unlock and push notifications in the Android app.

**Architecture:** New phone change types in `TechActions` (applied once per client id, same rules as the web through shared services); a versioned `/api/tech/stock` download; `StockCount` for counts the office posts; a `PushMessage` outbox sent through FCM HTTP v1 by a background service (inert until configured); two Capacitor plugins in the APK reached from the web app through `window.Capacitor`.

**Tech Stack:** ASP.NET Core 10, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19 + Mantine 8 (phone app under `web/src/tech`), Vitest; Capacitor 8 Android in Docker.

**Spec:** `docs/superpowers/specs/2026-11-22-phone-stock-0.11.0-design.md`

## Global Constraints

- Every phone change: `POST /api/tech/actions` with a client id, applied once; answers "done" (with an optional message) or "rejected" with the reason.
- Phone rules equal web rules: `StockLedger` for every stock move; 0.9.0 access on every download and change; organization-wide checks `AllSites()`.
- Older app versions keep working: new bundle/stock fields optional; old change types unchanged.
- Photos ≤ 10 MB (unchanged); videos (mp4, mov, webm) and documents (pdf, doc/docx, xls/xlsx, txt, csv) ≤ 50 MB.
- New permission key `workorders.logForOthers` (label "Log hours for others"); new organizations give it to Maintenance Manager.
- Push: config `Push:ServiceAccountJson`; unset ⇒ nothing queued or sent; the key never logged or returned.
- Lock screen after 5 minutes away; fingerprint setting stored on the phone only.
- Messages (exact): "Only {0} in the store; the rest is on the store keeper's list." · "This took parts reserved for {0}." · "This count was already posted or cancelled." · "The asset isn't down." · "The asset is already down." · "The time can't be in the future." · "{0} is larger than 50 MB."
- Every user-visible string in English and Arabic.
- Helpers: `wt-test <filter>`, `wt-vitest`, `wt-web`, `wt-ef <Name>`; APK: `mobile/build-apk.sh debug`.

## Review Focus

1. The same phone change sent twice (retry after a lost answer) for `part.use`, `count.line`, `po.receive` — applied once (Task 3 `Part_use_is_applied_once`).
2. A part used offline while the store copy is stale (stock fell to 0 meanwhile) — what exists is issued, the rest becomes short, nothing negative (Task 3 `Short_stock_becomes_a_planned_line`).
3. A count line arriving after the office posted the count — rejected with the message, stock untouched (Task 5 `Lines_after_posting_are_rejected`).
4. A technician limited to site A using `stock.issue` / `part.use` with a site-B store or work order — refused (Task 3 `Other_sites_are_refused`).
5. Push with no server key, or with a token Firebase says is gone — nothing queued / token removed, no error surfaced to users (Task 9 `Nothing_is_queued_without_a_key`, `Gone_tokens_are_removed`).

---

### Task 1: Model, migration, permission

**Files:** Create `server/FourC.Cmms.Core/Inventory/StockCount.cs`, `server/FourC.Cmms.Core/Notifications/PushMessage.cs`; modify `Identity/UserSession.cs` (`PushToken` string(512)?), `Identity/Identity.cs` (permission + label + default role), `Data/CmmsDbContext.cs`; migration `PhoneStock`; test `server/FourC.Cmms.Tests/PhoneModelTests.cs`.

**Produces:** `StockCount : TenantEntity { Guid StoreId; string Name; StockCountStatus Status (Open=1, Posted=2, Cancelled=3); DateTimeOffset? PostedAt; Guid? PostedById; List<StockCountLine> Lines }`, `StockCountLine : Entity, ITenantOwned { StockCountId, PartId, decimal CountedQuantity, Guid? CountedById, string? CountedByName, DateTimeOffset CountedAt }` (unique StockCountId+PartId); `PushMessage : Entity, ITenantOwned { UserId, Title, Body, string? Link, EntityType?, EntityId?, Status (Pending/Sent/Failed), Attempts, NextAttemptAt, LockedUntil?, SentAt?, LastError?, CreatedAt }`; `Permissions.WorkOrdersLogForOthers = "workorders.logForOthers"`. Site filter: StockCount by its store's site (child of Store).

- [ ] Failing tests: `New_permission_is_listed_and_given_to_managers` (GET /api/permissions contains the key; new org's Maintenance Manager role has it); `Counts_follow_store_access` (a count on a site-B store is invisible to a site-A user via DB-backed list endpoint once Task 5 exists — here: model round-trip test through DbAsync).
- [ ] Run → FAIL. Implement, `wt-ef PhoneStock`. Run → PASS; full `wt-test` green. Commit.

### Task 2: Phone downloads (`/api/tech/stock`, bundle lines)

**Files:** `TechEndpoints.cs`; test `PhoneStockTests.cs`.

**Produces:** `GET /api/tech/stock` (versioned/304 like places; `inventory.view` not required — a technician gets parts of their sites' stores): `{ stores:[{id,code,name,siteId}], parts:[{id,partNumber,name,unit,hasPhoto,isShelfItem,levels:[{storeId,onHand,reservedForOthers,bin}]}], cancelReasons:[{id,name}], pmTasks:[{id,code,name,steps}], counts:[{id,name,storeId}] (open counts of visible stores), purchaseOrders?:[{id,number,supplier,lines:[{id,partId,description,outstanding,storeId}]}] (only with purchasing.receive) }`; bundle job += `plannedLines:[{id,partId,storeId,quantity,issued}]`, `defaultStoreId`.

- [ ] Failing tests: `Stock_download_covers_my_sites` (site-A user sees site-A store parts only, levels with bin); `Purchase_orders_need_receive_permission`; `Bundle_has_planned_lines`.
- [ ] Implement; tests PASS; commit.

### Task 3: Parts used on a job and stock issue

**Files:** Create `server/FourC.Cmms.Api/Features/PhoneStock.cs` (shared service); modify `TechActions.cs`, `InventoryEndpoints.cs` (Short for work: `GET /api/inventory/shortages?storeId=`); test `PhonePartsTests.cs`.

**Produces:** `PhoneStock.UseAsync(db, ctx, workOrder, partId, storeId, quantity, lineId?, ct) → Task<string?>` (message); `PhoneStock.ReturnAsync(...)`; change types `part.use`, `part.return`, `stock.issue`; `GET /api/inventory/shortages` → `[{workOrderId, workOrderNumber, dueDate, partId, partNumber, partName, storeId, storeCode, short}]` (`inventory.view`).

- [ ] Failing tests: `Using_a_planned_part_issues_against_the_reservation`; `Part_use_is_applied_once`; `Short_stock_becomes_a_planned_line` (message "Only 2 in the store; …", planned line for the rest, shortage listed); `Taking_others_reserved_stock_is_issued_with_a_note`; `Return_is_limited_to_what_was_issued`; `Other_sites_are_refused`; `Stock_issue_needs_transact`.
- [ ] Implement; PASS; full suite; commit.

### Task 4: Receive against a PO line on the phone

**Files:** `TechActions.cs`, `PurchasingEndpoints.cs` (extract the receive core to an `internal static` used by both); test in `PhonePartsTests.cs`.

- [ ] Failing tests: `Po_receive_from_the_phone` (stock up, line received qty up, PO status partly/received); `Po_receive_needs_permission`; `Po_receive_is_applied_once`.
- [ ] Implement; PASS; commit.

### Task 5: Stock counts

**Files:** Create `server/FourC.Cmms.Api/Features/StockCountEndpoints.cs`; `TechActions.cs` (`count.line`); `InventoryEndpoints.cs` (extract stock-take posting `internal static PostCountAsync`); test `StockCountTests.cs`.

**Produces:** `/api/stock-counts`: GET list (store, status), GET `{id}` (lines with expected=on hand now, counted, difference), POST {storeId, name} (`inventory.transact`), POST `{id}/post` (`inventory.transact`; adjustments as stock-take), POST `{id}/cancel`; `count.line` {countId, partId, quantity} (`inventory.view`).

- [ ] Failing tests: `Count_from_the_phone_then_post` (two lines, one differing; post → one adjustment, count Posted); `Latest_count_of_a_part_wins`; `Lines_after_posting_are_rejected` (message exact); `Counts_follow_store_access`; `Negative_counts_are_refused`.
- [ ] Implement; PASS; commit.

### Task 6: Stops from a job

**Files:** `TechActions.cs` (`downtime.start`, `downtime.end`); test `PhoneStopTests.cs`.

- [ ] Failing tests: `Start_and_end_a_stop_offline_with_chosen_times` (stop linked to the job, times as sent); `Future_times_are_refused`; `Ending_when_not_down_is_refused` ("The asset isn't down."); `Starting_twice_is_refused` ("The asset is already down.").
- [ ] Implement with `Downtime.StartOrJoinAsync` / `Downtime.EndOpenAsync`; PASS; commit.

### Task 7: Checklist from PM, crew hours, cancel, create on the phone

**Files:** `TechActions.cs`, `Identity.cs` already has the permission; test `PhoneJobTests.cs`.

- [ ] Failing tests: `Checklist_from_a_pm_task`; `Crew_leader_logs_hours_for_another` (with permission ok; without → rejected); `Cancel_from_the_phone_needs_close_and_a_reason`; `Create_a_work_order_on_the_phone` (numbered, assigned to me, needs workorders.create; applied once).
- [ ] Implement; PASS; commit.

### Task 8: Videos and documents

**Files:** `TechActions.cs` (`/tech/files`: kinds photo|signature|file; size by type; `RequestSizeLimit(52 MB)`); test in `PhoneJobTests.cs`.

- [ ] Failing tests: `A_pdf_and_a_video_attach_to_the_job`; `Files_over_50_MB_are_refused` (message); `Photos_stay_at_10_MB`.
- [ ] Implement; PASS; commit.

### Task 9: Push notifications (server)

**Files:** Create `server/FourC.Cmms.Api/Features/PushOutbox.cs` (queue), `PushSenderService.cs` (BackgroundService + `RunOnceAsync`), `IPushTransport` (FCM HTTP v1 via `Google.Apis.Auth` service-account token; test double in tests); `TechEndpoints.cs` (`POST /api/tech/push-token`); hook into `EmailOutbox.AssigneesAddedAsync` callers and `RequestDecidedAsync` (approved); test `PushTests.cs`.

- [ ] Failing tests: `Nothing_is_queued_without_a_key`; `Assigned_technician_with_a_token_gets_a_push` (test transport records it); `Request_approved_pushes_to_the_requester`; `Gone_tokens_are_removed`; `Pushes_respect_site_access`; `Sign_out_removes_the_token`.
- [ ] Implement (fixture config `Push:ServiceAccountJson` = test value + test transport); PASS; commit.

### Task 10: Phone web — parts and stock

**Files:** `web/src/tech/` (types, `useTech` stock download, `JobPage` parts section, new `PartPage.tsx`, `CountPage.tsx`, `ReceivePage.tsx`, scan routing for part labels), helper `web/src/tech/stock.ts` (+ test: `available`, `shortWarning`, count merge).

- [ ] Failing Vitest `stock.test.ts`; implement; `wt-vitest` + `wt-web` green; commit.

### Task 11: Phone web — stops, checklist, files, crew hours, cancel, create

**Files:** `JobPage.tsx`, new `NewWorkOrderPage.tsx`, `photos.ts` → files, helper `web/src/tech/files.ts` (+ test: kind and size limits).

- [ ] Failing Vitest `files.test.ts`; implement; green; commit.

### Task 12: Office web — Short for work, Stock counts

**Files:** `web/src/pages/inventory/InventoryPage.tsx` (tabs), new `StockCountsPanel.tsx`, `ShortagesPanel.tsx`; types; locales.

- [ ] Build + vitest green; commit.

### Task 13: Android — fingerprint and push

**Files:** `mobile/package.json` (biometric plugin compatible with Capacitor 8, `@capacitor/push-notifications`), `mobile/android` (Firebase gradle plugin applied only when `google-services.json` is present), `mobile/build-apk.sh` + `scripts/release.sh` (copy `_private/firebase/google-services.json` into the build copy when present), `mobile/test-config.mjs`; web `web/src/tech/native.ts` (+ test with a fake bridge: lock timing, plugin absent), lock screen, push registration.

- [ ] Failing Vitest `native.test.ts`; implement; `mobile/build-apk.sh debug` builds; commit.

### Task 14: Arabic, docs, verification

**Files:** `ar.json`, web `ar`, `docs/TECHNICIAN_APP.md` (update), README row, `docs/DEPLOY_AWS.md` (push key).

- [ ] Arabic coverage test for the Global Constraints messages; docs; full `wt-test`, `wt-vitest`, `wt-web`, debug APK; commit.

## Deferred minors (final review)

- Two phones using the last part at the same moment: the second is refused "Not enough stock" instead of becoming short (retry by hand works).
- A count line can land on a count while it is being posted (narrow race); it is then never applied.
- Two partial receipts on the same order line at once can both pass the outstanding check (older than 0.11.0).
- For custom roles without stock permission the phone still offers "Issue to a job" and the counts list (Give back is now hidden).
- Receiving on the phone offers order lines with no store and shelf-life lines, which the server refuses.
- A stop's time isn't limited to 30 days back; the stop card only changes after the next refresh.
- The fingerprint lock replaces the app, so an open completion sheet is lost after 5 minutes away.
- Hours logged for a crew mate show under "My hours" until the next refresh.
- Firebase answers 400 INVALID_ARGUMENT / 403 SENDER_ID_MISMATCH aren't treated as dead tokens (5 retries each); push messages are never cleaned up.
- The "no key, nothing queued" push test would pass without the key check (only the acting user is assigned).
- "Request approved" pushes link to /tech/requests, which the phone has no page for; requesters without an employee link never register; the guide says the tap opens the job.
- The phone's stock copy downloads in full on most refreshes, uncompressed; a failed stock download marks the whole refresh failed.
- A planned line shows the part's id when the part isn't in the phone's stock copy.
- Cleanups: SuggestedQuantity lost its summary comment; PushMessage.Link comment says /tech/job; count posting is dated by the UTC day.
- AWS template: the "never in an environment variable" comment is inaccurate; the IAM grant needs the exact full ARN; a customer-managed KMS key would need kms:Decrypt.
