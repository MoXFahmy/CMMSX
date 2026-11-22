# Requests That Close the Loop (0.10.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Request types and recipients, requester accept/refuse with ratings, request comments, required comments and cancel reasons, access windows, "only while stopped", and an e-mail grid with an overdue e-mail.

**Architecture:** New tenant lookups (`RequestType`, `CancelReason`) and two tenant-owned tables (`WorkRequestComment`, `RequestFeedback`); JSON columns for windows and recipient lists; one shared e-mail routing table (event → recipient kinds) used by every work/request e-mail; one service for asking/answering feedback used by the in-app and the anonymous single-use-link endpoints; status rules stay in `WorkOrderEndpoints.ChangeStatusAsync` so web, bulk and phone obey them.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), xUnit `SkippableFact` + `ApiFixture`; React 19 + Mantine 8 + TanStack Query + i18next (en/ar), Vitest.

**Spec:** `docs/superpowers/specs/2026-11-22-requests-loop-0.10.0-design.md`

## Global Constraints

- Every user-facing read is scoped by 0.9.0 access; e-mails go only to people who can see the record (`EmailOutbox.QueueAsync` siteId/locationId); organization-wide decisions (numbering, in-use checks, uniqueness) use `AllSites()`.
- Anonymous endpoints: only health, auth and `/api/public/feedback/{token}` (GET, POST).
- Grid defaults equal today's behaviour for existing events (see the spec's table); `EmailRecipients` missing key = defaults.
- `CommentRequiredFor` default `["reject","refuse"]`; "complete" stays `RequireCompletionNotes`.
- Messages (exact): "Say why the work is on hold." · "Say why the work is reopened." · "Say why the work is cancelled." · "Choose why it is cancelled." · "Choose the trade or team the work goes to." · "Choose people who can see this place." · "This link was already used or has expired." · "This item is in use. Mark it inactive instead."
- Feedback token: 32 random bytes, base64url in the link, only SHA-256 hex stored, 30-day expiry, single use.
- Access windows: at most 10, End after Start, note ≤ 200. Rating 1–5. Comment ≤ 4000 (request comments), ≤ 2000 (feedback).
- Overdue: every 30 min (`Email:OverdueIntervalMinutes`), skip-and-mark when due > 48 h ago, ≤ 200 per organization per run.
- Every user-visible string in English and Arabic (server `Localization/ar.json` via `ar-add.py`; web `en`/`ar` via `loc-add.py`).
- Helpers: tests `scratchpad/bin/wt-test <filter>`, web tests `wt-vitest`, web type-check/build `wt-web`, migration `wt-ef RequestsLoop`.

## Review Focus

1. A requester without a login opening the link twice, after expiry, or after maintenance reopened the work — must get "This link was already used or has expired.", and nothing changes (Task 5 test `LinkIsSingleUseAndVoidedByReopen`).
2. A limited user (0.9.0 site access) as an extra recipient, type recipient or grid recipient for a record outside their access — no e-mail, and the request form refuses them as an extra recipient (Task 2 `ExtraRecipientMustSeeThePlace`, Task 3 `GridStillFiltersByAccess`).
3. Turning e-mail on for an organization with old overdue work — no flood (Task 8 `OldOverdueWorkIsMarkedNotSent`).
4. Bulk cancel / bulk hold with required comments or cancel reasons — rows skipped with the message, not a 500 (Task 6 `BulkCancelNeedsAReason`).
5. A refusal on a work order maintenance already Closed — reopens it although the requester has no close permission (Task 5 `RefuseReopensClosedWork`).

---

### Task 1: Data model, migration, settings, shared test helpers

**Files:**
- Create: `server/FourC.Cmms.Core/Work/Requests.cs` (RequestType, CancelReason, WorkRequestComment, RequestFeedback, CommentActions)
- Create: `server/FourC.Cmms.Core/Work/AccessWindows.cs`
- Modify: `server/FourC.Cmms.Core/Work/Work.cs` (WorkRequest, WorkOrder fields; `WorkRequestStatus.Closed = 5`), `Assets/Assets.cs`, `Pm/Pm.cs`, `Tenancy/Tenant.cs`, `Notifications/OutboundEmail.cs` (event keys + kinds + grid), `Data/CmmsDbContext.cs`, `Services/TenantProvisioning.cs`
- Modify: `server/FourC.Cmms.Api/Features/AdminEndpoints.cs` (settings PUT validation)
- Create: migration `RequestsLoop` (with seed + overdue marking SQL)
- Create: `server/FourC.Cmms.Tests/Loop.cs` (shared helpers), `server/FourC.Cmms.Tests/LoopModelTests.cs`

**Interfaces — Produces:**
- `record AccessWindow(DateTimeOffset Start, DateTimeOffset End, string? Note)`; `static class AccessWindows { const int Max = 10; string? Problem(IReadOnlyList<AccessWindow>? list); bool Covers(IReadOnlyList<AccessWindow>? list, DateOnly day, TimeZoneInfo zone); }`
- `RequestType : TenantEntity, ICodeLookup { Guid? DefaultTradeId; WorkPriority? DefaultPriority; List<Guid> RecipientIds; bool AutoApprove; Guid? TeamId; }`; `CancelReason : TenantEntity, ICodeLookup`
- `WorkRequestComment : Entity, ITenantOwned { WorkRequestId, Body, UserId?, UserName?, ByRequester, CreatedAt }`
- `RequestFeedback : Entity, ITenantOwned { WorkRequestId, WorkOrderId, Accepted, Rating?, Comment?, AnsweredAt, UserId?, ByName?, ViaLink }`
- `static class CommentActions { Reject="reject", Refuse="refuse", Hold="hold", Reopen="reopen", Cancel="cancel"; IReadOnlyList<string> All }`
- WorkRequest: `RequestTypeId?, ExtraRecipientIds (List<Guid>), AccessWindows (List<AccessWindow>), OnlyWhenStopped, AwaitingFeedbackSince?, FeedbackTokenHash? (string(64), indexed), FeedbackTokenExpiresAt?`
- WorkOrder: `CancelReasonId?, AccessWindows, OnlyWhenStopped, OverdueNotifiedFor?`; Asset/PmSchedule: `OnlyWhenStopped`
- TenantSettings: `List<string> CommentRequiredFor = ["reject","refuse"]`, `Dictionary<string, List<string>> EmailRecipients = []`
- `EmailEvents.RequestComment="request.comment"`, `RequestRefused="request.refused"`, `WorkOrderCancelled="workorder.cancelled"`, `WorkOrderOverdue="workorder.overdue"` (added to `All`); `static class EmailRecipientKinds { Requester="requester", RequestRecipients="requestRecipients", Technicians="technicians", Contractors="contractors", Issuer="issuer"; All }`; `EmailEvents.Grid : IReadOnlyDictionary<string, (IReadOnlyList<string> Applicable, IReadOnlyList<string> Defaults)>` (the spec's table, exactly); `EmailEvents.KindsOn(TenantSettings s, string eventKey) → IReadOnlySet<string>` (configured ∩ applicable, else defaults)
- DbSets: `RequestTypes, CancelReasons, WorkRequestComments, RequestFeedback`. JSON conversions like `Team.MemberIds` / `WorkingHours`.
- `TenantProvisioning.DefaultCancelReasons` = DUP Duplicate, NOTNEEDED No longer needed, ERROR Raised in error, OTHER Other.

- [ ] **Step 1: Write the failing tests** in `LoopModelTests`:
  - `NewOrganizationHasDefaultsAndCancelReasons`: register → GET `/api/settings` has `commentRequiredFor == ["reject","refuse"]` and empty `emailRecipients`; DB has 4 cancel reasons for the tenant (codes above).
  - `SettingsRejectUnknownCommentActionOrEmailKind`: `TrySetSettingsAsync` with `commentRequiredFor=["nap"]` → 400; with `emailRecipients={"request.submitted":["boss"]}` → 400; with `{"nope":["requester"]}` → 400; with `{"request.submitted":["requester","requestRecipients"]}` → 200 and round-trips.
  - `AccessWindowRules` (pure): `Problem` null for empty/valid; message for End ≤ Start, for 11 windows, for a 201-char note; `Covers` true for none, true when a window overlaps the local day (Cairo zone, window 22:00–23:00 UTC the previous day counts for the next local day), false otherwise.
  - `KindsOnUsesDefaultsUntilConfigured` (pure): `KindsOn(new TenantSettings(), "workorder.completed")` = {issuer}; configured `["issuer","technicians"]` → {issuer} (technicians not applicable).
- [ ] **Step 2: Run** `wt-test LoopModelTests` — Expected: FAIL (types missing / compile).
- [ ] **Step 3: Implement** the entities, settings, grid table, DbContext config (tenant filter on the four new sets; `RequestTypes`/`CancelReasons` are org-wide lookups; comment/feedback scoped through their request), provisioning seed, settings PUT validation (messages: "Unknown action: X." / "Unknown e-mail: X." / "Unknown recipient: X."), then `wt-ef RequestsLoop`; add to the migration's `Up`: seed `CancelReasons` for every existing tenant (INSERT…SELECT FROM Tenants, NEWID()), and `UPDATE WorkOrders SET OverdueNotifiedFor = DueAt WHERE Status IN (1,2,3) AND DueAt < SYSUTCDATETIME()`.
- [ ] **Step 4: Create `Loop.cs`** — `public static class Loop` with `Ok`, `Id`, `Addr`, `OrgAsync(ApiFixture, bool emailOn = true)`, `UserAsync(ApiFixture, HttpClient admin, string role, string email)`, `RowsAsync(ApiFixture, params string[] to)` (copied from `EmailTests`, made shared).
- [ ] **Step 5: Run** `wt-test LoopModelTests` — Expected: 4 pass. Then `wt-test` (full) — Expected: all pass (470+ green, 2 skipped).
- [ ] **Step 6: Commit** `feat(0.10.0): data model for requests loop`.

### Task 2: Request types, extra recipients, auto-approve

**Files:**
- Create: `server/FourC.Cmms.Api/Features/RequestTypeEndpoints.cs`; register in `Program.cs`
- Modify: `WorkRequestEndpoints.cs` (input, validation, detail/list fields, `requestTypeId` filter, `/people`, `ApproveCoreAsync`), `EmailOutbox.cs` (`RequestRecipientsAsync`), `TechActions.cs` (request.create passes through `CreateAsync`, unchanged signature)
- Test: `server/FourC.Cmms.Tests/RequestTypeTests.cs`

**Interfaces:**
- Consumes: Task 1 entities.
- Produces: `RequestTypeInput(string Code, string Name, bool IsActive, Guid? DefaultTradeId, WorkPriority? DefaultPriority, List<Guid>? RecipientIds, bool AutoApprove, Guid? TeamId)`; GET `/api/request-types` → `{ id, code, name, isActive, defaultTradeId, defaultPriority, recipientIds, autoApprove, teamId }[]`; `RequestInput` += `Guid? RequestTypeId = null, List<Guid>? ExtraRecipientIds = null, List<AccessWindow>? AccessWindows = null`; `internal static Task<(Validator V, WorkOrder? Wo)> ApproveCoreAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, WorkRequest r, ApproveInput input, string? autoNote, CancellationToken ct)` (used by `/approve` and auto-approve; copies windows and `OnlyWhenStopped`); `internal static Task<List<EmailRecipient>> EmailOutbox.RequestRecipientsAsync(CmmsDbContext db, WorkRequest r, CancellationToken ct)`; GET `/api/work-requests/people?siteId=` → `{ id, name }[]`. Detail adds `requestType {id,name}`, `extraRecipients [{id,name}]`, `accessWindows`, `onlyWhenStopped`; list adds `requestTypeName`.

- [ ] **Step 1: Failing tests** (`RequestTypeTests`):
  - `TypeCrudAndRules`: create type → listed; duplicate code → 400; auto-approve without trade/team → 400 "Choose the trade or team the work goes to."; unknown recipient → 400; delete a used type → 409/400 "This item is in use…"; non-admin (Technician role) POST → 403.
  - `TypeAndExtraRecipientsAreAddedToSiteRecipients`: site recipients = [A]; type recipients = [B]; request with type + extra [C] → `request.submitted` rows for A, B and C (not the requester).
  - `ManagersStillHearWhenOnlyExtraRecipientsAreSet`: no site/type recipients, extra [C] → rows for the admin (requests.manage) and C.
  - `ExtraRecipientMustSeeThePlace`: user limited to site 2 as extra recipient on a site-1 request → 400 "Choose people who can see this place."; `/people?siteId=site1` omits them.
  - `AutoApproveCreatesTheWorkOrder`: type auto-approve, trade T, team limited to another site → request Approved, decision note starts "Approved automatically", work order has trade T, no team, the request's access windows, priority as requested; requester gets `request.approved`.
  - `PhoneRequestOfAutoTypeIsApproved`: `/api/tech/actions` `request.create` with `requestTypeId` (payload gains `RequestTypeId`) → Approved with a work order.
- [ ] **Step 2: Run** `wt-test RequestTypeTests` — Expected: FAIL.
- [ ] **Step 3: Implement.** Type endpoints follow `PeopleEndpoints.MapLookup` style (custom input). Request validation: type exists and is active ("Choose a request type." when unknown/inactive); extra recipients ≤ 10, active users whose access (`AccessService.GetAsync` + `Access.Covers(site, location path)`) covers the place; windows via `AccessWindows.Problem`. `CreateAsync` sets `OnlyWhenStopped` from the asset; when the type auto-approves, run `ApproveCoreAsync` in the same `InTransactionAsync` with trade = type trade, team = type team only when `TeamEndpoints.CheckForWorkAsync` accepts it for the site. `RequestSubmittedAsync` uses `RequestRecipientsAsync`.
- [ ] **Step 4: Run** `wt-test RequestTypeTests` — Expected: 6 pass; `wt-test Email` and `wt-test Tech` stay green.
- [ ] **Step 5: Commit** `feat(0.10.0): request types, extra recipients, auto-approve`.

### Task 3: E-mail routing by recipient kind

**Files:**
- Modify: `EmailOutbox.cs` (every work/request event resolves recipients through `KindsOn`), `EmailEndpoints.cs` (`/email/status` adds `grid`)
- Test: `server/FourC.Cmms.Tests/EmailGridTests.cs`

**Interfaces:**
- Consumes: `EmailEvents.Grid`, `KindsOn`, `RequestRecipientsAsync`.
- Produces: `internal Task<Dictionary<string, List<EmailRecipient>>> EmailOutbox.GroupsAsync(CmmsDbContext db, string eventKey, WorkOrder? w, WorkRequest? r, IReadOnlyCollection<Guid>? onlyEmployees, CancellationToken ct)` — only the kinds switched on for the event; technicians = in-house assignees + active in-house team members, contractors = the same people with `IsContractor`; `onlyEmployees` limits both to newly added people (assigned events). `/email/status` → `grid: { key, applicable: string[], defaults: string[] }[]` (work/request events only).

- [ ] **Step 1: Failing tests** (`EmailGridTests`):
  - `DefaultsMatchTodayForExistingEvents`: run the existing `EmailTests` (unchanged) — they are the regression net; plus `StatusListsTheGrid` (`grid` contains `workorder.completed` with applicable [requestRecipients, issuer] and defaults [issuer]).
  - `RequesterReceiptWhenSwitchedOn`: `emailRecipients["request.submitted"] = ["requestRecipients","requester"]`; a user creates a request with a typed requester e-mail → a `request.submitted` row for that address.
  - `IssuerOffStopsCompletedMail`: `workorder.completed = []` → completing produces no `workorder.completed` row.
  - `ContractorsColumnSeparatesContractors`: assignees in-house X and contractor Y; `workorder.assigned=["technicians"]` → only X.
  - `GridStillFiltersByAccess`: `workorder.completed=["issuer","requestRecipients"]`, a request recipient limited to another site → no row for them.
- [ ] **Step 2: Run** `wt-test EmailGridTests` — Expected: FAIL.
- [ ] **Step 3: Implement** `GroupsAsync`; route `AssigneesAddedAsync`, `WorkOrderCompletedAsync`, `RequestSubmittedAsync`, `RequestDecidedAsync`, `PartsReceivedAsync` through it (requester keeps its own template/link; others the event's template).
- [ ] **Step 4: Run** `wt-test EmailGridTests` then `wt-test Email` — Expected: 5 pass; all EmailTests pass.
- [ ] **Step 5: Commit** `feat(0.10.0): e-mail recipients by kind`.

### Task 4: Comments on requests

**Files:**
- Modify: `WorkRequestEndpoints.cs` (GET/POST `/{id}/comments`), `WorkOrderEndpoints.cs` (detail `request.comments`), `EmailOutbox.cs` (`RequestCommentAsync`), `EmailTemplates.cs` (`RequestComment`)
- Test: `server/FourC.Cmms.Tests/RequestCommentTests.cs`

**Interfaces:** Produces `GET /api/work-requests/{id}/comments → { id, body, userName, byRequester, createdAt }[]` (oldest first); `POST {body}`; `Task EmailOutbox.RequestCommentAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, WorkRequestComment c, CancellationToken ct)` — a requester's comment goes to the maintenance-side kinds switched on (requestRecipients, technicians, contractors), a maintenance comment goes to the requester when `requester` is on; the requester link is the request page only when the requester is the creator with a login, otherwise no link. WO detail `request` gains `comments` (same shape).

- [ ] **Step 1: Failing tests:**
  - `RequesterAndMaintenanceShareTheThread`: requester (Requester role) comments; admin sees it, replies; requester sees both, `byRequester` flags right.
  - `OthersCannotSeeTheThread`: another Requester-role user → 404 on GET and POST.
  - `CommentGoesToTheOtherSide`: requester's comment → `request.comment` row for the site recipient, not the requester; admin's comment → row for the requester only.
  - `WorkOrderShowsRequestCommentsReadOnly`: approve; GET `/api/work-orders/{id}` → `request.comments` has the thread; technician (workorders.view, no requests.view) sees them.
  - `EmptyOrLongCommentRejected`: "" → 400; 4001 chars → 400.
- [ ] **Step 2: Run** `wt-test RequestCommentTests` — Expected: FAIL. **Step 3: Implement.** **Step 4: Run** — Expected: 5 pass.
- [ ] **Step 5: Commit** `feat(0.10.0): comments on requests`.

### Task 5: Requester accepts or refuses

**Files:**
- Create: `server/FourC.Cmms.Api/Features/RequestFeedback.cs` (service + endpoints); register in `Program.cs`
- Modify: `WorkOrderEndpoints.ChangeStatusAsync` (ask on completion; end the wait on reopen/cancel), WO detail (`request.feedback` latest, `request.awaitingFeedback`), `WorkRequestEndpoints` detail (`feedback[]`, `awaitingFeedback`, `canAnswer`), `EmailOutbox` (`WorkOrderCompletedAsync(..., string? feedbackToken)`, `RequestRefusedAsync`), `EmailTemplates` (`RequestCompleted` gets Accept/Refuse link, `RequestRefused`)
- Test: `server/FourC.Cmms.Tests/RequestFeedbackTests.cs`

**Interfaces:**
- Produces: `record FeedbackInput(bool Accepted, int? Rating, string? Comment)`; `static class RequestFeedbackService { Task<string?> AskAsync(CmmsDbContext db, WorkOrder w, DateTimeOffset now, CancellationToken ct); void EndWait(WorkRequest r); Task<Validator> AnswerAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, WorkRequest r, FeedbackInput input, bool viaLink, string? byName, CancellationToken ct); string Hash(string token); }`; `POST /api/work-requests/{id}/feedback` (creator only, else 403; not awaiting → 400 "Nothing is waiting for your answer."); `GET /api/public/feedback/{token}` → `{ organization, language, number, title, place, workOrderNumber, completedAt, completionNotes }`; `POST /api/public/feedback/{token}` (FeedbackInput) → 200; unknown/used/expired → 404 `{ detail: "This link was already used or has expired." }`.
- The anonymous POST looks the request up with `IgnoreQueryFilters()` by hash, then works in a new `CmmsDbContext` with `FixedTenantContext(tenantId, null, requesterName)`.

- [ ] **Step 1: Failing tests:**
  - `CreatorAcceptsOnTheRequestPage`: request by a Requester-role user → approve → complete → request `awaitingFeedback` true, `canAnswer` true for the creator, false for admin; creator POSTs accept rating 4 → request `Closed`, feedback row, WO detail `request.feedback.rating == 4`; second answer → 400.
  - `AcceptNeedsARating`: accept without rating → 400; rating 6 → 400.
  - `RefuseReopensWithTheReason`: refuse with comment → WO `InProgress`, latest status change note "Refused by <name>: <comment>", request still Approved, not awaiting; `request.refused` rows for technicians and the issuer; complete again → awaiting again.
  - `RefuseCommentFollowsTheSetting`: refuse with no comment → 400; remove "refuse" from `commentRequiredFor` → refuse with no comment → 200.
  - `RefuseReopensClosedWork`: admin closes the completed WO; requester refuses → WO `InProgress`.
  - `RequesterWithoutLoginGetsALink`: request created by admin with typed requester e-mail R → complete → `request.completed` row to R whose body contains `/feedback/`; extract token; anonymous GET shows number and organization and no technician names; anonymous POST accept → Closed, feedback `viaLink`.
  - `LinkIsSingleUseAndVoidedByReopen`: second POST → 404 message; a fresh link, then admin reopens the WO → GET 404; a link whose `FeedbackTokenExpiresAt` is set in the past (DB) → 404.
  - `NoAskWithoutARequest`: completing a plain WO touches no request and sends no `request.completed`.
- [ ] **Step 2: Run** `wt-test RequestFeedbackTests` — Expected: FAIL. **Step 3: Implement** (token = `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`). **Step 4: Run** — Expected: 8 pass; `wt-test Email`, `wt-test Work` green.
- [ ] **Step 5: Commit** `feat(0.10.0): requester accepts or refuses finished work`.

### Task 6: Required comments, cancellation reasons, cancelled e-mail

**Files:**
- Modify: `WorkOrderEndpoints.cs` (`StatusInput.CancelReasonId`, rules in `ChangeStatusAsync`, list column/filter, detail `cancelReason`), `WorkRequestEndpoints` reject (rule from settings), `PeopleEndpoints` (`MapLookup("/cancel-reasons", …, SettingsManage, inUse: work orders AllSites)`), `TechEndpoints` (`CompletionRules.HoldNote`), `TechActions` (StatusPayload unchanged; rules come from `ChangeStatusAsync`), `EmailOutbox.WorkOrderCancelledAsync`, `EmailTemplates.WorkOrderCancelled`
- Test: `server/FourC.Cmms.Tests/RequiredCommentTests.cs`

**Interfaces:** `StatusInput` += `Guid? CancelReasonId = null`; `WorkOrderFilter.CancelReasonId`; `WorkOrderListItem` += `string? CancelReasonName`; detail `cancelReason {id, code, name}`; `Task EmailOutbox.WorkOrderCancelledAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, string? reason, string? note, CancellationToken ct)`.

- [ ] **Step 1: Failing tests:**
  - `HoldNoteFollowsTheSetting`: hold without note → 200; add "hold" → 400 "Say why the work is on hold."; with note → 200.
  - `ReopenNoteFollowsTheSetting`: "reopen" on; Completed → InProgress without note → 400 "Say why the work is reopened."
  - `CancelNeedsAReasonFromTheList`: cancel without reason → 400 "Choose why it is cancelled."; with reason → WO `cancelReason` set, list filter `cancelReasonId` finds it, `cancelReasonName` in list; reopen clears it; with no active reasons (all marked inactive) cancel without reason → 200.
  - `CancelNoteFollowsTheSetting`: "cancel" on, reason but no note → 400 "Say why the work is cancelled."
  - `RejectReasonFollowsTheSetting`: default reject with no note → 400; remove "reject" → 200.
  - `BulkCancelNeedsAReason`: bulk cancel of 2 without reason → 200 with both skipped, message "Choose why it is cancelled."; with reason → updated 2.
  - `PhoneHoldNeedsNoteWhenRequired`: "hold" on; bundle `completionRules.holdNote` true; `status.change` OnHold without note → rejected answer with the message.
  - `CancelledMailGoesToTechniciansAndRequester`: WO from a request, assignee X; cancel → `workorder.cancelled` rows for X and the requester.
- [ ] **Step 2: Run** `wt-test RequiredCommentTests` — Expected: FAIL. **Step 3: Implement.** **Step 4: Run** — Expected: 8 pass; `wt-test Bulk`, `wt-test Tech`, `wt-test Work` green.
- [ ] **Step 5: Commit** `feat(0.10.0): required comments and cancellation reasons`.

### Task 7: Access windows and "only while stopped" (server)

**Files:**
- Modify: `WorkOrderEndpoints.cs` (input, create/update, detail `accessWindows`, `outsideWindows`, `onlyWhenStopped`; filter `onlyWhenStopped`; list item), `PmEndpoints.cs` (`ScheduleInput.OnlyWhenStopped`), `PmGenerator.cs` (copy flag), `AssetEndpoints.cs` (`AssetInput.OnlyWhenStopped`, detail), `PlanningBoardEndpoints.cs` (BoardJob fields, `onlyWhenStopped` filter), `TechEndpoints.cs` (job `accessWindows`, `onlyWhenStopped`)
- Test: `server/FourC.Cmms.Tests/AccessWindowTests.cs`

**Interfaces:** `WorkOrderInput` += `List<AccessWindow>? AccessWindows = null, bool? OnlyWhenStopped = null` (update: null = unchanged); `ScheduleInput` += `bool? OnlyWhenStopped = null`; `AssetInput` += `bool OnlyWhenStopped = false`; `BoardJob` += `List<AccessWindow> AccessWindows, bool OutsideWindows, bool OnlyWhenStopped`; list items (WO, PM schedules) += `OnlyWhenStopped`.

- [ ] **Step 1: Failing tests:**
  - `WindowsCopiedOnApproval`: request with 2 windows → approve → WO has them; detail `outsideWindows` false with no planned start, true with a planned start outside both, false inside.
  - `BadWindowsRejected`: end before start → 400 on request and WO.
  - `WorkOrderUpdateKeepsWindowsWhenOmitted`: PUT without `accessWindows` keeps; with `[]` clears.
  - `BoardFlagsWorkOutsideWindows`: job planned on a day outside its windows → board job `outsideWindows` true; inside → false.
  - `StoppedFlagFlowsFromAsset`: asset flagged → request, WO (no input), PM schedule (no input) and its generated WO all `onlyWhenStopped`; WO input `false` overrides; WO list filter `onlyWhenStopped=true` returns only flagged; board filter too.
  - `PhoneJobCarriesWindowsAndFlag`: bundle job has `accessWindows` and `onlyWhenStopped`.
- [ ] **Step 2: Run** `wt-test AccessWindowTests` — Expected: FAIL. **Step 3: Implement** (zone = `Sla.Zone(settings.TimeZone)`). **Step 4: Run** — Expected: 6 pass; `wt-test Planning`, `wt-test Pm`, `wt-test Tech` green.
- [ ] **Step 5: Commit** `feat(0.10.0): access windows and only-while-stopped`.

### Task 8: Overdue e-mail

**Files:**
- Create: `server/FourC.Cmms.Api/Features/OverdueNotifier.cs`; register in `Program.cs`; `ApiTests.cs` fixture config `Email:OverdueAutoRun=false`
- Modify: `EmailOutbox.WorkOrderOverdueAsync`, `EmailTemplates.WorkOrderOverdue`
- Test: `server/FourC.Cmms.Tests/OverdueTests.cs`

**Interfaces:** `sealed class OverdueNotifier(IServiceScopeFactory, IConfiguration, ILogger<OverdueNotifier>) : BackgroundService { Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct); }` (registered as singleton + hosted, like `EmailSenderService`); returns e-mails queued.

- [ ] **Step 1: Failing tests:**
  - `RecentlyOverdueWorkIsMailedOnce`: WO due 2 h ago, assignee X → `RunOnceAsync` queues `workorder.overdue` for X and the issuer; second run queues nothing.
  - `OldOverdueWorkIsMarkedNotSent`: WO due 3 days ago → nothing queued, `OverdueNotifiedFor` set.
  - `NewDueTimeCanNotifyAgain`: after a notice, move due to 1 h ago (DB) → next run queues again.
  - `NoMailWhenEmailIsOff`: organization with e-mail off → nothing queued and the WO not marked.
- [ ] **Step 2: Run** `wt-test OverdueTests` — Expected: FAIL. **Step 3: Implement.** **Step 4: Run** — Expected: 4 pass.
- [ ] **Step 5: Commit** `feat(0.10.0): overdue e-mail`.

### Task 9: Satisfaction and cancelled-work reports

**Files:** Modify `ReportEndpoints.cs` (catalog + two builders); Test `server/FourC.Cmms.Tests/LoopReportTests.cs`

**Interfaces:** catalog entries `requester-satisfaction` ("Work", "Requester satisfaction", groupBy month|type|trade|team|site, Range, SiteP, `WorkOrdersView`) and `cancelled-work` ("Work", "Cancelled work", groupBy reason|month|trade|site, Range, SiteP, `WorkOrdersView`).

- [ ] **Step 1: Failing tests:**
  - `SatisfactionCountsAnswers`: two answers (accept 4, refuse 2) → KPIs answers 2, average rating 3.0, accepted 50 %; groupBy trade puts them in their trades.
  - `SatisfactionRespectsSiteAccess`: a user limited to site 2 sees no rows for site-1 answers.
  - `CancelledWorkByReason`: 2 cancelled DUP, 1 OTHER in range → rows DUP 2, OTHER 1; one cancelled before the range not counted.
- [ ] **Step 2–4:** run `wt-test LoopReportTests` (FAIL → implement → 3 pass).
- [ ] **Step 5: Commit** `feat(0.10.0): satisfaction and cancelled-work reports`.

### Task 10: Web — settings (types, reasons, required comments, e-mail grid)

**Files:**
- Create: `web/src/pages/settings/RequestTypesSettings.tsx`, `web/src/pages/settings/emailGrid.ts` (+ `emailGrid.test.ts`)
- Modify: `SettingsPage.tsx` (tabs; cancel reasons via the existing lookup editor used for failure codes; "Required comments" card holding the existing completion-notes switch and the five new checkboxes), `NotificationSettings.tsx` (grid), `api/types.ts`, locales en/ar

**Interfaces:** `kindsOn(settings: TenantSettings, row: GridRow): string[]`; `withKind(map: Record<string,string[]> | undefined, row: GridRow, kind: string, on: boolean): Record<string,string[]>` (starts from defaults when the event has no entry); `GridRow = { key: string; applicable: string[]; defaults: string[] }`.

- [ ] **Step 1: Failing Vitest** `emailGrid.test.ts`: `kindsOn` returns defaults when unset; `withKind` adds/removes from defaults; ignores non-applicable kinds; saving keeps other events untouched.
- [ ] **Step 2: Run** `wt-vitest emailGrid` — FAIL. **Step 3: Implement** helpers and screens (grid: rows events with the row "On" switch, cells checkboxes, "—" where not applicable; purchasing switches below; the save queue as today). **Step 4: Run** `wt-vitest` (all) and `wt-web` — PASS / clean build.
- [ ] **Step 5: Commit** `feat(0.10.0): web settings for requests loop`.

### Task 11: Web — requests and the public answer page

**Files:**
- Create: `web/src/pages/work/RequestFeedbackPage.tsx` (route `/feedback/:token` outside the signed-in layout, in both route sets of `App.tsx`), `web/src/components/AccessWindowsEditor.tsx`, `web/src/components/accessWindows.ts` (+ `accessWindows.test.ts`)
- Modify: `WorkRequestsPage.tsx` (form: type select pre-filling priority, extra recipients from `/work-requests/people`, windows; drawer: type, recipients, windows, stopped badge, comments thread, feedback panel/answers, Closed status, reject reason required per setting, approve pre-fills trade from type; list: type column/filter), `api/types.ts`, locales

**Interfaces:** `windowProblem(list): string | null` and `isOutside(list, day: string, zone?): boolean` mirroring the server rules (tested); the page `RequestFeedbackPage` uses anonymous `get`/`post` to `/public/feedback/{token}` and switches i18n to the returned `language`.

- [ ] **Step 1: Failing Vitest** `accessWindows.test.ts` (End ≤ Start problem, 11 windows problem, outside/inside day).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** `wt-vitest`, `wt-web` — PASS.
- [ ] **Step 5: Commit** `feat(0.10.0): web requests, comments and answers`.

### Task 12: Web — work orders, planning, PM, assets, phone

**Files:** Modify `WorkOrderDetailPage.tsx` (windows card + warning, stopped badge, cancel reason, request answer + comments read-only; status modal: cancel reason select, notes required per `commentRequiredFor`), `WorkOrderFormPage.tsx` (windows, stopped checkbox), `WorkOrdersPage.tsx` (badge, stopped filter, cancel reason column/filter), `PlanningPage.tsx` (badge, window warning icon, stopped filter, warning notification on drop outside windows), `PmScheduleFormPage.tsx`/`PmPage.tsx` (checkbox, badge), `AssetFormPage.tsx`/`AssetDetailPage.tsx` (flag), `tech/JobPage.tsx` (windows, badge, hold note required by `completionRules.holdNote`), `tech` types, locales.

- [ ] **Step 1: Failing Vitest** for the status-note rule helper `noteRequired(settings, from, to): 'hold'|'reopen'|'cancel'|null` in `web/src/pages/work/statusRules.ts` (+ test).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** `wt-vitest`, `wt-web` — PASS.
- [ ] **Step 5: Commit** `feat(0.10.0): web work orders, planning, PM, assets and phone`.

### Task 13: Arabic, docs, full verification

**Files:** `server/FourC.Cmms.Api/Localization/ar.json` (every new server message/e-mail string), web `ar` locale parity, `docs/REQUESTS_AND_NOTIFICATIONS.md` (new; how types, answers, comments, required comments, windows, stopped and the grid work), `docs/ROADMAP.md` (0.10.0 done).

- [ ] **Step 1:** Add a `LanguageTests`-style check `NewMessagesHaveArabic` listing the Global Constraints messages and the new e-mail subjects → FAIL until translated; translate; PASS.
- [ ] **Step 2:** Web: the existing locale-parity test (or `wt-vitest`) passes with no missing `ar` keys.
- [ ] **Step 3:** Write the docs.
- [ ] **Step 4:** Full runs: `wt-test` (all) and `wt-vitest`, `wt-web` — all green; record counts in the ledger.
- [ ] **Step 5: Commit** `docs(0.10.0): requests and notifications guide`.
