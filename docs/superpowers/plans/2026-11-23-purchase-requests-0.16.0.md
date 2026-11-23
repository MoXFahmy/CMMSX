# Purchase requests and approval levels (0.16.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Purchase requests with a one-step approval that become draft orders, plus cumulative approval levels on orders by amount and order type, and a "My approvals" page.

**Architecture:** Purchasing's own tables: `PurchaseRequest*` for requests; `OrderType` and `ApprovalLevel` as settings; on submit, the levels an order needs are copied to `PurchaseOrderApprovalStep` rows for the order's round and signatures recorded as `PurchaseOrderSignature` rows. A pure resolver picks the levels; a signing service in the API applies them. Organizations without levels keep today's single-approver rule untouched.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server, named Tenant/Site filters), React 19 + Mantine 8 + mantine-datatable + TanStack Query + i18next, xUnit integration tests against SQL Server, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-purchase-requests-0.16.0-design.md`

## Global Constraints

- No new NuGet or npm packages.
- Arabic for every new screen text (web `en.json` + `ar.json`, Arabic plurals `_zero/_one/_two/_few/_many/_other`); every new server message through `Translator.T` with an entry in `server/FourC.Cmms.Api/Localization/ar.json`.
- Site access everywhere: new site-owned entities get the `Site` query filter; organization-wide decisions (numbering, in-use checks) use `AllSites()`.
- Organizations with no approval levels: ordering works exactly as today — every existing purchasing test stays green unchanged.
- Order amount = `PurchaseOrderMath.TotalInBase(po)` (incl. tax and shipping, base currency); a level applies when `MinAmount <= total`.
- A level has 1–5 approvers and rule *Any* or *All*; levels add up, signed in ascending minimum.
- Statuses exactly: requests `Draft, Submitted, Approved, Rejected, PartlyOrdered, Ordered, Closed, Cancelled`.
- Permissions exactly: `purchasing.request` "Raise purchase requests", `purchasing.approveRequests` "Approve purchase requests"; `purchasing.approve` description "Approve purchase orders (by approval levels, or up to the user's limit)".
- New numbers `PR-00001` (organization prefix `PR-`, a site's own prefix like the other numbers), 5 digits.
- E-mail switch keys exactly: `pr.submitted`, `pr.approved`, `pr.rejected`, `pr.ordered`, `pr.received`, `po.approvalWaiting` (purchasing switches, fixed recipients, on by default, personal opt-outs apply).
- Commands (scratchpad `bin` on PATH, `export WT=/Users/mohamedfahmy/Projects/CMMSX/.worktrees/purchase-requests-0.16.0`): server tests `wt-test [filter]`, web tests `wt-vitest`, web build `wt-web`, migration `wt-ef <Name>`.

## Review Focus

1. An approver who lost `purchasing.approve`, site access or their account while listed on the waiting step must not sign; the order must report that nobody can sign, and Restart must pick up fixed levels — test `Approver_who_lost_access_cannot_sign_and_restart_picks_up_new_levels` (Task 5).
2. Two people signing an *any* step at the same moment: one signature counts, the other is told it's already signed off — test `Two_signatures_at_once_on_an_any_step_count_once` (Task 5).
3. A foreign-currency order exactly at a level's minimum in base currency needs that level — tests `Exactly_at_the_minimum_in_base_currency_needs_the_level` (Task 4, pure) and `Foreign_currency_order_at_the_minimum_waits_for_that_level` (Task 5).
4. A request line whose order is cancelled, or whose draft order is deleted, returns to "to order" and the request status follows (Ordered → PartlyOrdered → Approved) — test `Cancelled_or_deleted_order_puts_request_lines_back_to_order` (Task 3).
5. Site access in My approvals, the bell and e-mails, and a site's own set replacing the organization's — tests `My_approvals_and_bell_only_count_sites_I_can_see` (Task 6) and `Site_set_replaces_the_organization_set` (Task 4, pure).

---

### Task 1: Data model, migration, permissions and order types

**Files:**
- Create: `server/FourC.Cmms.Core/Purchasing/PurchaseRequests.cs`, `server/FourC.Cmms.Core/Purchasing/Approvals.cs`
- Modify: `server/FourC.Cmms.Core/Purchasing/Purchasing.cs` (PO + line fields), `server/FourC.Cmms.Core/Locations/Locations.cs` (`SiteSettings`), `server/FourC.Cmms.Core/Tenancy/TenantSettings.cs` (or wherever `PurchaseOrderNumberPrefix` lives), `server/FourC.Cmms.Core/Services/Numbering.cs`, `server/FourC.Cmms.Core/Identity/Identity.cs` (permissions), `server/FourC.Cmms.Core/Services/TenantProvisioning.cs`, `server/FourC.Cmms.Core/Data/CmmsDbContext.cs`, `server/FourC.Cmms.Api/Features/PeopleEndpoints.cs` (order types lookup), new migration `PurchaseRequests`
- Test: `server/FourC.Cmms.Tests/PurchaseRequestSetupTests.cs`

**Interfaces:**
- Produces (Core, namespace `FourC.Cmms.Core.Purchasing`):
  - `enum PurchaseRequestStatus { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, PartlyOrdered = 5, Ordered = 6, Closed = 7, Cancelled = 8 }`
  - `class PurchaseRequest : TenantEntity` — `Number`, `SiteId`, `Site?`, `Status`, `RequestedById Guid?`, `RequestedByName string?`, `NeededBy DateOnly?`, `Notes string?`, `RecipientIds List<Guid>`, `SubmittedAt`, `DecidedAt`, `DecidedById`, `DecidedByName`, `RejectReason`, `CancelledAt`, `CancelReason`, `ClosedAt`, `Lines List<PurchaseRequestLine>`
  - `class PurchaseRequestLine : Entity` — `PurchaseRequestId`, `LineNumber int`, `PartId Guid?`, `Part?`, `Description string`, `UnitOfMeasure string?`, `Quantity decimal`, `NeededBy DateOnly?`, `WorkOrderId Guid?`, `AssetId Guid?`, `StoreId Guid?`, `EstimatedUnitPrice decimal?`, `Reason string?`
  - `class PurchaseRequestComment : Entity, ITenantOwned` — `TenantId`, `PurchaseRequestId`, `Body`, `UserId Guid?`, `UserName string?`, `CreatedAt`
  - `class OrderType : TenantEntity, ICodeLookup` — `Code`, `Name`, `IsActive`
  - `enum ApprovalRule { Any = 1, All = 2 }`
  - `class ApprovalLevel : TenantEntity` — `SiteId Guid?` (null = organization set), `OrderTypeId Guid?` (null = all types), `Name`, `MinAmount decimal`, `Rule ApprovalRule`, `ApproverIds List<Guid>`
  - `enum ApprovalStepStatus { Pending = 1, Waiting = 2, Approved = 3, Rejected = 4, Stopped = 5 }`
  - `class PurchaseOrderApprovalStep : Entity, ITenantOwned` — `TenantId`, `PurchaseOrderId`, `Round int`, `Step int`, `Name`, `MinAmount`, `Rule`, `ApproverIds List<Guid>`, `Status`, `DoneAt DateTimeOffset?`
  - `class PurchaseOrderSignature : Entity, ITenantOwned` — `TenantId`, `PurchaseOrderId`, `Round`, `Step`, `UserId Guid`, `UserName`, `Approved bool`, `Comment string?`, `At DateTimeOffset`
  - `PurchaseOrder.OrderTypeId Guid?`, `PurchaseOrder.ApprovalRound int` (0 = never submitted under levels), `PurchaseOrder.SubmittedById Guid?` (set on submit from Task 5 on); `PurchaseOrderLine.PurchaseRequestLineId Guid?`
  - `SiteSettings.PurchaseRequestNumberPrefix string?`, `SiteSettings.PurchaseRequestRecipientIds List<Guid>`; `TenantSettings.PurchaseRequestNumberPrefix string = "PR-"`, `TenantSettings.PurchaseRequestRecipientIds List<Guid>`
  - `NumberKind.PurchaseRequest` → sequence `"PurchaseRequest"`, prefixes from the settings above
  - `Permissions.PurchasingRequest = "purchasing.request"`, `Permissions.PurchasingApproveRequests = "purchasing.approveRequests"`
  - `TenantProvisioning.DefaultOrderTypes = [("PARTS","Parts"), ("SERVICES","Services"), ("CAPITAL","Capital")]`
  - DbSets: `PurchaseRequests`, `PurchaseRequestLines`, `PurchaseRequestComments`, `OrderTypes`, `ApprovalLevels`, `PurchaseOrderApprovalSteps`, `PurchaseOrderSignatures`
  - API: `/api/order-types` via `MapLookup` (manage: `settings.manage`; in use when any order has the type or any level names it)

- [ ] **Step 1: Write the failing tests** in `PurchaseRequestSetupTests` (class fixture `ApiFixture`, register a fresh org per test as `InventoryTests.SetupAsync` does):

```csharp
[SkippableFact] public async Task New_organization_has_parts_services_and_capital_order_types()
// GET /api/order-types → codes exactly ["CAPITAL","PARTS","SERVICES"] (sorted), all active
[SkippableFact] public async Task Purchase_request_permissions_are_listed_and_given_to_built_in_roles()
// GET /api/permissions contains "purchasing.request" and "purchasing.approveRequests";
// GET /api/roles: Technician has purchasing.request and not purchasing.approveRequests; Maintenance Manager has both
[SkippableFact] public async Task Order_type_in_use_by_an_order_cannot_be_deleted()
// create a draft PO (Task 1 only needs the column; set it through the DbContext in the test via fx.Factory services), DELETE /api/order-types/{parts} → 409/400 with the lookup's in-use message
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test PurchaseRequestSetupTests`
Expected: FAIL (404 on `/api/order-types`, permissions missing)

- [ ] **Step 3: Add the entities, settings, numbering kind, permissions (with the `All` entries and the new `purchasing.approve` description), provisioning (Technician gets `purchasing.request`; order types seeded at sign-up), DbContext config** — enums as strings, money `(18,2)` (`MinAmount`), quantities and prices like PO lines, `ApproverIds`/`RecipientIds` as JSON like `RequestType.RecipientIds`, indexes `(TenantId, Status)` on requests and `(TenantId, PurchaseOrderId, Round)` on steps/signatures, unique `(TenantId, Number)` on requests (filtered `IsDeleted = 0`). Query filters: `PurchaseRequest` site filter on `SiteId`; lines and comments via their request (like `PurchaseOrderLine`); steps and signatures via their order. `ApprovalLevel` gets the tenant filter only (settings, not site-owned data).

- [ ] **Step 4: Generate the migration and add its data steps**

Run: `wt-ef PurchaseRequests`
Then add to `Up` after the schema: insert the three order types for every tenant (`Tenants` rows, new GUIDs, `IsActive = 1`); `UPDATE PurchaseOrders SET OrderTypeId = <that tenant's PARTS>`; append `purchasing.request` and `purchasing.approveRequests` to the `Permissions` JSON of every role named `Administrator` or `Maintenance Manager`, and `purchasing.request` to roles named `Technician` (only when not already present).
Expected: migration files created; `wt-test PurchaseRequestSetupTests` builds.

- [ ] **Step 5: Run the tests**

Run: `wt-test PurchaseRequestSetupTests` then `wt-test` (whole suite)
Expected: PASS; whole suite green.

- [ ] **Step 6: Commit**

```bash
git add server && git commit -m "feat(purchasing): purchase request, order type and approval level data"
```

---

### Task 2: Purchase requests — raise, approve, reject, cancel, comments, files, e-mails

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PurchaseRequestEndpoints.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (map), `server/FourC.Cmms.Api/Features/SupportEndpoints.cs` (attachments: entity type `PurchaseRequest`), `server/FourC.Cmms.Api/Features/EmailOutbox.cs`, `server/FourC.Cmms.Api/Features/EmailTemplates.cs`, `server/FourC.Cmms.Core/Notifications/OutboundEmail.cs` (`EmailEvents`), `server/FourC.Cmms.Api/Features/LocationEndpoints.cs` + the settings PUT (recipients, prefix), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/PurchaseRequestTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `Permissions.PurchasingRequest/ApproveRequests`, `NumberKind.PurchaseRequest`.
- Produces (routes under `/api/purchase-requests`):
  - `GET /` list — query `PrFilter { Search, Status (comma list), SiteId, Mine bool?, WaitingForMe bool?, Sort, Dir, Page, PageSize }`; items `PrListItem(Id, Number, Status, SiteName, RequestedByName, NeededBy, Lines, EstimatedTotal decimal?, SubmittedAt, IsLate)`; `GET /export` (xlsx/csv like `/purchase-orders/export`)
  - `GET /{id}` detail with `lines[]` (each: part, description, quantity, unit, neededBy, workOrder {id, number}, asset, store, estimatedUnitPrice, reason, `order {id, number, lineId, status}?`, `ordered decimal`, `received decimal`), `comments[]`, and `actions { edit, submit, approve, reject, cancel, delete, order, close, comment }`
  - `POST /` and `PUT /{id}` with `PrInput(Guid SiteId, DateOnly? NeededBy, string? Notes, List<Guid>? RecipientIds, List<PrLineInput> Lines, string? RowVersion)`; `PrLineInput(Guid? Id, Guid? PartId, string? Description, string? UnitOfMeasure, decimal Quantity, DateOnly? NeededBy, Guid? WorkOrderId, Guid? AssetId, Guid? StoreId, decimal? EstimatedUnitPrice, string? Reason)`
  - `POST /{id}/submit`, `POST /{id}/approve`, `POST /{id}/reject` (`ReasonInput`), `POST /{id}/cancel` (`ReasonInput`), `DELETE /{id}`, `POST /{id}/comments` (`{ body }`)
  - `EmailOutbox.PrSubmittedAsync(db, ctx, PurchaseRequest r, ct)`, `PrDecidedAsync(db, ctx, r, bool approved, string? reason, ct)`
  - `EmailEvents.PrSubmitted = "pr.submitted"`, `PrApproved = "pr.approved"`, `PrRejected = "pr.rejected"` (added to `All`)

- [ ] **Step 1: Write the failing tests** in `PurchaseRequestTests` (helpers: `OrgAsync()` → admin client, site, store, part; `UserAsync(admin, roleName, perms?)` making a role with exactly the given permissions when needed):

```csharp
[SkippableFact] public async Task Technician_raises_a_request_numbered_PR_and_submits_it()
// POST lines: one part line (store set) and one free-text line ("Crane hire", qty 1); number starts "PR-0000"; status Draft
// submit → Submitted, SubmittedAt set; requester = the technician
[SkippableFact] public async Task Submit_needs_a_line_a_store_for_parts_and_positive_quantities()
// each case → 400 with messages "Add at least one line.", "Line 1: choose the store this part should arrive in.", "Line 1: quantity must be more than 0."
[SkippableFact] public async Task Approver_approves_or_rejects_with_a_reason_and_nobody_approves_their_own()
// manager approves → Approved, DecidedByName; a second request: reject with "" → 400 "Give a reason."; reject "Too early" → Rejected, RejectReason
// requester (who also holds approveRequests in this test) approving own → 400 "You can't approve your own request."
[SkippableFact] public async Task Rejected_request_is_fixed_and_submitted_again()
// Rejected → PUT works → submit → Submitted; Approved → PUT 400 "This request can't be changed any more."
[SkippableFact] public async Task Requester_sees_only_their_own_others_see_their_sites()
// a second technician: GET /api/purchase-requests excludes the first's request, GET /{id} → 404; manager sees both
[SkippableFact] public async Task Draft_is_deleted_later_requests_are_cancelled_with_a_reason()
// delete Draft → 204; delete Submitted → 400; cancel Submitted with "Not needed" → Cancelled
[SkippableFact] public async Task Work_order_on_a_line_must_be_at_the_request_site_and_fills_its_asset()
// WO at another site → 400 "Line 1: the work order is at another site."; WO with an asset → line.asset = that asset
[SkippableFact] public async Task Submitted_request_emails_the_site_recipients_and_decisions_email_the_requester()
// emailEnabled on; site PurchaseRequestRecipientIds = [buyer]; submit → OutboundEmails row "pr.submitted" to buyer only; approve → "pr.approved" to requester
// with no recipients set → everyone with purchasing.approveRequests at the site
[SkippableFact] public async Task Comments_and_files_on_a_request()
// POST comment → appears in detail.comments with author; POST /api/attachments entityType "PurchaseRequest" → 200
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test PurchaseRequestTests`
Expected: FAIL (404s)

- [ ] **Step 3: Implement the endpoints and rules from the spec** — visibility: requester's own always; all at their sites with `purchasing.view`, `purchasing.create` or `purchasing.approveRequests`; raise/edit with `purchasing.request`; editable in Draft/Rejected; submit from Draft/Rejected; approve/reject from Submitted by `purchasing.approveRequests`, not the requester; cancel by requester or approver while no line is on a live order; delete Draft by requester. Numbering through `Numbering.For(..., NumberKind.PurchaseRequest)` + `Numbering.NextAsync` with `AllSites()` existence checks. Recipients: site's `PurchaseRequestRecipientIds`, else organization's, else users with `purchasing.approveRequests` and access to the site; plus `r.RecipientIds`; never the actor (outbox rule). Site settings and organization settings accept and return the new prefix and recipients (validated like `RequestRecipientIds`).

- [ ] **Step 4: Run the tests**

Run: `wt-test PurchaseRequestTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server && git commit -m "feat(purchasing): purchase requests with one-step approval, comments, files and e-mails"
```

---

### Task 3: Requests into orders

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/PurchaseRequestEndpoints.cs`, `server/FourC.Cmms.Api/Features/PurchasingEndpoints.cs` (order detail shows request numbers; cancel/delete/receive hooks), `server/FourC.Cmms.Core/Purchasing/PurchaseRequests.cs` (status helper), `EmailOutbox.cs`, `EmailTemplates.cs`, `OutboundEmail.cs`, `ar.json`
- Test: `server/FourC.Cmms.Tests/PurchaseRequestOrderTests.cs`, `server/FourC.Cmms.Tests/PurchaseRequestStatusTests.cs` (pure)

**Interfaces:**
- Consumes: Task 2 routes; `PurchasingEndpoints.CreateDraftAsync` (make `internal`), `PoInput`, `LineInput` (add optional `Guid? PurchaseRequestLineId = null` as the last parameter).
- Produces:
  - `public static PurchaseRequestStatus PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus current, int lines, int orderedLines)` — only moves among Approved / PartlyOrdered / Ordered; any other status is returned unchanged
  - `POST /api/purchase-requests/order` (`purchasing.create`) with `OrderFromRequestsInput(List<OrderRequestLine> Lines)`, `OrderRequestLine(Guid RequestLineId, Guid? SupplierId, decimal? Quantity, decimal? UnitPrice)` → `[{ id, number, supplier, lines }]`
  - `POST /api/purchase-requests/{id}/close` (`purchasing.create`) → Closed
  - `internal static Task PurchaseRequestSync.SyncAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, IEnumerable<Guid> requestIds, CancellationToken ct)` (API, in `PurchaseRequestEndpoints.cs`) — recomputes statuses after orders are made, cancelled or deleted, using `PurchaseRequestRules.StatusAfterOrdering`
  - `EmailOutbox.PrOrderedAsync(db, ctx, PurchaseRequest r, IReadOnlyList<string> orderNumbers, ct)`, `PrReceivedAsync(db, ctx, PurchaseRequest r, IReadOnlyList<string> lines, ct)`; keys `pr.ordered`, `pr.received`
  - Order detail lines gain `request { id, number }?`

- [ ] **Step 1: Write the failing tests**

```csharp
// PurchaseRequestStatusTests (pure, no DB)
[Fact] public void Ordering_moves_approved_to_partly_ordered_and_ordered()
// (Approved, 3, 1) → PartlyOrdered; (PartlyOrdered, 3, 3) → Ordered; (Ordered, 3, 2) → PartlyOrdered; (PartlyOrdered, 3, 0) → Approved
[Fact] public void Other_statuses_are_left_alone()
// (Closed, 3, 0) → Closed; (Cancelled, 2, 0) → Cancelled; (Submitted, 1, 0) → Submitted

// PurchaseRequestOrderTests
[SkippableFact] public async Task Approved_lines_become_draft_orders_grouped_by_supplier_and_site()
// two approved requests, three lines: two for supplier A (one defaulted from the part's preferred supplier), one free-text for supplier B
// → 2 draft orders; each order line has purchaseRequestLineId, the request line's store and work order; notes contain both PR numbers
// requests → Ordered / PartlyOrdered as appropriate; requester gets "pr.ordered" listing the order numbers
[SkippableFact] public async Task Only_approved_unordered_lines_can_be_ordered()
// a Submitted request's line → 400 "PR-00001 isn't approved."; the same line twice → 400 "PR-00001 line 1 is already on PO-00001."
// free-text line without supplier → 400 "Choose a supplier for PR-00001 line 2."
[SkippableFact] public async Task Cancelled_or_deleted_order_puts_request_lines_back_to_order()
// order both lines → Ordered; delete the draft for one → PartlyOrdered and that line's order is null; cancel the other order → Approved
[SkippableFact] public async Task Receiving_tells_the_requester_and_shows_received_on_the_request()
// approve + receive 2 of 5 on the order → request line received = 2; "pr.received" e-mail to the requester
[SkippableFact] public async Task Parts_ordered_for_a_job_are_issued_to_it_on_receipt()
// request line with a work order → order line has it → receive → work order parts show the issue (existing behaviour, now reached from a request)
[SkippableFact] public async Task Buyer_closes_a_partly_ordered_request()
// close → Closed; the remaining line can't be ordered any more (400)
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test "PurchaseRequestOrderTests|PurchaseRequestStatusTests"`
Expected: FAIL

- [ ] **Step 3: Implement** — resolve every line first (approved, not on a live order, supplier = given ?? part's preferred supplier, quantity default the request line's, price default estimated ?? part last purchase price ?? average cost), then in one `InTransactionAsync` create drafts grouped by `(SupplierId, SiteId)` through `CreateDraftAsync` (RequestedBy = distinct requester names joined ", "; Notes = "From PR-…, PR-…"), then `PurchaseRequestSync.SyncAsync`. "Live order" = order line whose order isn't Cancelled. Hook `SyncAsync` into PO delete and cancel. Hook `PrReceivedAsync` into `ReceiveCoreAsync` for lines with `PurchaseRequestLineId`.

- [ ] **Step 4: Run the tests and the purchasing suite**

Run: `wt-test "PurchaseRequest|Inventory|PhoneParts|Email"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server && git commit -m "feat(purchasing): turn approved request lines into draft orders, status follows the orders"
```

---

### Task 4: Order types on orders and approval level settings

**Files:**
- Create: `server/FourC.Cmms.Core/Purchasing/ApprovalLevelRules.cs`, `server/FourC.Cmms.Api/Features/ApprovalLevelEndpoints.cs`
- Modify: `PurchasingEndpoints.cs` (PoInput gains `Guid? OrderTypeId` with default null as a named optional parameter before `RowVersion`; list filter `OrderTypeId`; detail/list show the type), `Program.cs`, `ar.json`
- Test: `server/FourC.Cmms.Tests/ApprovalLevelRulesTests.cs` (pure), `server/FourC.Cmms.Tests/ApprovalLevelSettingsTests.cs`

**Interfaces:**
- Produces:
  - `public static IReadOnlyList<ApprovalLevel> ApprovalLevelRules.Required(IReadOnlyCollection<ApprovalLevel> all, Guid siteId, Guid? orderTypeId, decimal totalInBase)` — set = levels with `SiteId == siteId` if any exist, else `SiteId == null`; within the set, levels with `OrderTypeId == orderTypeId` if `orderTypeId` is set and any exist, else `OrderTypeId == null`; then `MinAmount <= totalInBase` ordered by `MinAmount`
  - `GET /api/approval-levels` (`settings.manage` or `purchasing.approve`) → `[{ id, siteId, siteName, orderTypeId, orderTypeName, name, minAmount, rule, approvers: [{ id, name, ok, problem? }] }]` where `problem` is `"no-permission" | "no-site-access" | "inactive"`
  - `POST /api/approval-levels`, `PUT /api/approval-levels/{id}`, `DELETE /api/approval-levels/{id}` (`settings.manage`) with `ApprovalLevelInput(Guid? SiteId, Guid? OrderTypeId, string Name, decimal MinAmount, ApprovalRule Rule, List<Guid> ApproverIds)`

- [ ] **Step 1: Write the failing tests**

```csharp
// ApprovalLevelRulesTests
[Fact] public void Levels_add_up_lowest_first()
// org levels 0, 10000, 100000; total 50000 → names [L0, L10k]; total 200000 → [L0, L10k, L100k]; total 0 → [L0]
[Fact] public void Exactly_at_the_minimum_in_base_currency_needs_the_level()
// total 10000.00 → includes L10k
[Fact] public void Below_every_minimum_needs_none()
// org levels starting at 5000; total 4999.99 → empty
[Fact] public void Site_set_replaces_the_organization_set()
// org 0/10000; site S has its own 0 only → total 50000 at S → [S0]; at another site → [org0, org10k]
[Fact] public void Type_levels_replace_all_types_levels_within_the_set()
// all-types 0; Capital 0 and 50000 → Capital order 60000 → [Cap0, Cap50k]; Parts order 60000 → [All0]; order with no type → [All0]

// ApprovalLevelSettingsTests
[SkippableFact] public async Task Level_needs_a_name_one_to_five_approvers_and_a_unique_minimum()
// 0 approvers → 400 "Choose at least one approver."; 6 → 400 "A level can have at most 5 approvers."; negative min → 400 "The minimum can't be below 0.";
// same set + type + minimum twice → 400 "Another level in this set already starts at 10,000."
[SkippableFact] public async Task Levels_list_flags_approvers_who_cant_sign()
// approver without purchasing.approve → problem "no-permission"; site set with an approver limited to another site → "no-site-access"
[SkippableFact] public async Task Orders_carry_an_order_type_defaulting_to_parts_in_the_form()
// POST order with orderTypeId Services → detail.orderType.code "SERVICES"; list filter orderTypeId returns it; order without a type is allowed (null)
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test "ApprovalLevelRulesTests|ApprovalLevelSettingsTests"`
Expected: FAIL

- [ ] **Step 3: Implement** the resolver, the settings endpoints (validation messages as above, translated) and the order type on orders (must be an active type when set).

- [ ] **Step 4: Run the tests**

Run: `wt-test "ApprovalLevel|Inventory"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server && git commit -m "feat(purchasing): order types on orders and approval level settings"
```

---

### Task 5: Signing orders by levels

**Files:**
- Create: `server/FourC.Cmms.Api/Features/PurchaseApprovals.cs`
- Modify: `PurchasingEndpoints.cs` (submit / approve / reject / detail / restart), `EmailOutbox.cs` (`PoApprovalWaitingAsync`), `EmailTemplates.cs`, `OutboundEmail.cs`, `ar.json`
- Test: `server/FourC.Cmms.Tests/PurchaseApprovalTests.cs`

**Interfaces:**
- Consumes: `ApprovalLevelRules.Required`, Task 1 step/signature entities, `PurchaseOrderMath.TotalInBase`.
- Produces (`internal static class PurchaseApprovals` in `FourC.Cmms.Api.Features`):
  - `Task<bool> StartAsync(CmmsDbContext db, ITenantContext ctx, PurchaseOrder po, CancellationToken ct)` — on submit: computes levels; none → returns false (today's rule); else refuses when a level has nobody who can sign today (`DomainException` "Level '{0}' has nobody who can sign orders for {1}. Ask an administrator to fix the approval levels."), increments `ApprovalRound`, writes steps (first Waiting, rest Pending), returns true
  - `Task<IReadOnlyList<Guid>> CanSignNowAsync(CmmsDbContext db, PurchaseOrder po, PurchaseOrderApprovalStep step, CancellationToken ct)` — listed, active, has `purchasing.approve`, site access, didn't submit (`po.SubmittedById`), hasn't signed this step
  - `Task SignAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, Guid orderId, bool approve, string? comment, CancellationToken ct)` — inside `InTransactionAsync`, locks the order row (`SELECT ... WITH (UPDLOCK, ROWLOCK)` on `PurchaseOrders` by id), re-reads the waiting step, checks the signer, records the signature, advances or finishes (Approved), or on reject stops the round and returns the order to Draft
  - `Task RestartAsync(CmmsDbContext db, ITenantContext ctx, EmailOutbox mail, Guid orderId, CancellationToken ct)`
  - Submit sets `PurchaseOrder.SubmittedById` (column from Task 1) for every order, with or without levels
  - Order detail gains `approval { round, steps: [{ step, name, minAmount, rule, status, approvers: [{ id, name, signed, approved, at, comment, canSign }] }], earlierRounds: [...], nobodyCanSign bool }` and `actions.restartApproval`, `actions.sign` (true when I can sign the waiting step); `POST /api/purchase-orders/{id}/restart-approval` (`purchasing.approve`)
  - `EmailOutbox.PoApprovalWaitingAsync(db, ctx, PurchaseOrder po, IReadOnlyList<Guid> userIds, string levelName, ct)`; key `po.approvalWaiting`

- [ ] **Step 1: Write the failing tests** (helper: org with base currency, levels `L1` min 0 [manager A, any], `L2` min 10,000 [B, C, all]; a buyer who submits):

```csharp
[SkippableFact] public async Task Without_levels_ordering_works_as_today()
// no levels: submit then approve by an approver within limit → Approved; approval is null in detail
[SkippableFact] public async Task Small_order_signed_by_level_one_is_approved()
// 5,000 order: submit → step 1 Waiting; A approves → Approved, ApprovedByName "A"; personal limit 100 of A ignored
[SkippableFact] public async Task Big_order_needs_every_level_in_order_and_all_on_an_all_level()
// 50,000: B signing before step 1 → 400 "It isn't your turn to sign this order."; A signs → step 2 Waiting + "po.approvalWaiting" to B and C;
// B signs → still Submitted; C signs → Approved
[SkippableFact] public async Task Submitter_cannot_sign_and_approve_from_draft_is_not_offered()
// A submits own order → A's approve → 400 "You submitted this order, so someone else must sign it."; draft approve with levels → 400 "Submit the order for approval first."
[SkippableFact] public async Task Reject_at_any_level_returns_the_order_to_draft_and_resubmitting_starts_again()
// step 2 rejected by B with "Too dear" → Draft, creator gets "po.rejected" with the comment; resubmit → round 2 at step 1; earlierRounds has round 1 with B's comment
[SkippableFact] public async Task Submit_is_refused_when_a_level_has_nobody_who_can_sign()
// L2 approvers both without site access → submit 400 naming "L2" and the site
[SkippableFact] public async Task Approver_who_lost_access_cannot_sign_and_restart_picks_up_new_levels()
// order waiting at L2 (any) with only D; remove D's purchasing.approve → D signing 400; detail.approval.nobodyCanSign true; change L2 to [E] → restart → round 2, E signs
[SkippableFact] public async Task Two_signatures_at_once_on_an_any_step_count_once()
// L1 any [A, A2]: Task.WhenAll(A approve, A2 approve) → exactly one 200 and one 400 "This step is already signed off."; one signature row for round 1 step 1
[SkippableFact] public async Task Foreign_currency_order_at_the_minimum_waits_for_that_level()
// EUR order, exchange rate 50, total 200 EUR = 10,000 base → steps [L1, L2]
[SkippableFact] public async Task Orders_submitted_before_levels_finish_under_the_old_rule()
// submit with no levels, then add levels → approve by limit works; approval null
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test PurchaseApprovalTests`
Expected: FAIL

- [ ] **Step 3: Implement** `PurchaseApprovals` and wire it: submit calls `StartAsync` (true → e-mail the first step's signers with `po.approvalWaiting` instead of `po.submitted`); approve/reject route to `SignAsync` when the order has steps for its round, else today's code; detail builds `approval` and actions; the old `ApprovalLimitExceeded` flag stays for today's rule only.

- [ ] **Step 4: Run the tests and the purchasing suite**

Run: `wt-test "PurchaseApproval|Inventory|Email|SiteAccess|ReviewFix"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server && git commit -m "feat(purchasing): orders signed by cumulative approval levels, restart and history"
```

---

### Task 6: My approvals and the bell

**Files:**
- Create: `server/FourC.Cmms.Api/Features/MyApprovalsEndpoints.cs`
- Modify: `AlertEndpoints.cs`, `Program.cs`
- Test: `server/FourC.Cmms.Tests/MyApprovalsTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/approvals/mine` (any signed-in user) → `{ requests: [...], orders: [...], sentBack: [...] }`; request items `{ id, number, siteName, requestedByName, neededBy, lines, estimatedTotal, submittedAt }`; order items `{ id, number, siteName, supplierName, total, currency, totalInBase, levelName?, signedBefore: [names], next: [names], submittedAt }`; sentBack items `{ kind: "request"|"order", id, number, reason, at }`
  - `internal static Task<int> MyApprovals.CountAsync(CmmsDbContext db, ITenantContext ctx, PermissionService perms, CancellationToken ct)` used by the bell
  - Bell response gains `WaitingForMyApproval int?` (null when the user may approve nothing)

- [ ] **Step 1: Write the failing tests**

```csharp
[SkippableFact] public async Task My_approvals_lists_requests_and_orders_waiting_for_me()
// request Submitted at my site (not mine) + order waiting at a step listing me → both listed; after I sign → gone; next approver now sees it
[SkippableFact] public async Task Old_rule_orders_show_for_approvers_within_their_limit()
// no levels, 500 order Submitted; approver with limit 1,000 sees it, approver with limit 100 doesn't
[SkippableFact] public async Task Sent_back_lists_my_rejected_requests_and_orders()
// my request rejected "Wrong part" and my order rejected at L1 "Too dear" → sentBack has both reasons
[SkippableFact] public async Task My_approvals_and_bell_only_count_sites_I_can_see()
// approver limited to site S1; requests at S1 and S2 → list and bell count only S1
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-test MyApprovalsTests`
Expected: FAIL

- [ ] **Step 3: Implement** with the same checks as Task 2 (`approve` action) and Task 5 (`CanSignNowAsync`), so the list never shows what the person can't act on.

- [ ] **Step 4: Run the tests**

Run: `wt-test "MyApprovals|Alert"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server && git commit -m "feat(purchasing): my approvals list and bell count"
```

---

### Task 7: Web — requests

**Files:**
- Create: `web/src/pages/purchasing/purchaseRequests.ts`, `web/src/pages/purchasing/purchaseRequests.test.ts`, `web/src/pages/purchasing/RequestsTab.tsx`, `web/src/pages/purchasing/RequestFormPage.tsx`, `web/src/pages/purchasing/RequestDetailPage.tsx`, `web/src/pages/purchasing/OrderFromRequestsModal.tsx`
- Modify: `web/src/pages/purchasing/PurchasingPage.tsx` (Requests tab first for request-only users; Re-order "Request" button), `web/src/App.tsx` (routes `purchasing/requests/new`, `purchasing/requests/:id`, `purchasing/requests/:id/edit`), `web/src/components/AppLayout.tsx` (Purchasing visible with `purchasing.view` or `purchasing.request`), `web/src/auth/AuthContext.tsx` (`purchasingRequest`, `purchasingApproveRequests`), `web/src/pages/work/WorkOrderPlanning.tsx` (Request purchase), `web/src/pages/inventory/CountsAndShortages.tsx` (Request purchase), `web/src/api/types.ts`, `web/src/locales/en.json`, `web/src/locales/ar.json`

**Interfaces:**
- Consumes: Task 2 and Task 3 routes and shapes.
- Produces (`purchaseRequests.ts`):
  - types `PurchaseRequestStatus`, `PurchaseRequestListItem`, `PurchaseRequestDetail`, `PurchaseRequestLine`
  - `requestStatusColor(s: PurchaseRequestStatus): string` — Draft gray, Submitted blue, Approved teal, Rejected red, PartlyOrdered yellow, Ordered green, Closed dark, Cancelled gray
  - `orderableLines(d: PurchaseRequestDetail): PurchaseRequestLine[]` — status Approved or PartlyOrdered and `order == null`
  - `previewGroups(lines: { supplierId: string | null; siteId: string }[]): number` — distinct supplier+site pairs ignoring null suppliers
  - `draftFromShortages(rows: { partId; storeId; siteId; short; workOrderId?; dueDate? }[]): { siteId: string; lines: PrLineDraft[] }[]` — one draft per site
  - `isLate(neededBy: string | null, today: string, status): boolean` — before today and status not Ordered/Closed/Cancelled

- [ ] **Step 1: Write the failing tests** in `purchaseRequests.test.ts`:

```ts
it('colours each status', () => { expect(requestStatusColor('Rejected')).toBe('red'); expect(requestStatusColor('Ordered')).toBe('green'); });
it('offers only approved, unordered lines for ordering', () => { /* Approved with one ordered line → 1 of 2; Submitted → 0 */ });
it('counts one draft order per supplier and site', () => { expect(previewGroups([{ supplierId: 'a', siteId: 's' }, { supplierId: 'a', siteId: 's' }, { supplierId: 'b', siteId: 's' }, { supplierId: null, siteId: 's' }])).toBe(2); });
it('splits short parts into one request per site with the work order and its due date', () => { /* 3 rows, 2 sites → 2 drafts; line.neededBy = dueDate, quantity = short */ });
it('marks requests late only while still open', () => { expect(isLate('2026-11-20', '2026-11-23', 'Approved')).toBe(true); expect(isLate('2026-11-20', '2026-11-23', 'Ordered')).toBe(false); });
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-vitest`
Expected: FAIL (module not found)

- [ ] **Step 3: Implement the helpers, then the screens** — list tab (filters status, site, mine, waiting for me; export), form (site, needed-by, notes, extra recipients, line editor with part picker or free text, store, work order, estimated price, reason), detail (lines with order number/ordered/received, comments, files via the existing attachments component, buttons from `actions`, reject/cancel modals asking for a reason, Order modal with supplier per line and the group count), Request purchase on short planned parts and Short for work (navigate to the form with the draft in router state), Re-order "Request" for users without `purchasing.create`. All texts in `en.json` and `ar.json` under `pr.*`.

- [ ] **Step 4: Run tests and build**

Run: `wt-vitest` then `wt-web`
Expected: all tests pass; build clean.

- [ ] **Step 5: Commit**

```bash
git add web && git commit -m "feat(purchasing): purchase requests on the web"
```

---

### Task 8: Web — approval levels, order approval panel, My approvals

**Files:**
- Create: `web/src/pages/purchasing/approvals.ts`, `web/src/pages/purchasing/approvals.test.ts`, `web/src/pages/purchasing/ApprovalPanel.tsx`, `web/src/pages/approvals/MyApprovalsPage.tsx`, `web/src/pages/settings/PurchasingSettings.tsx`
- Modify: `web/src/pages/purchasing/PoFormPage.tsx` (order type, PARTS preselected), `web/src/pages/purchasing/PoDetailPage.tsx` (panel, sign/reject with comment, restart, request numbers on lines, print: last round's signatures), `web/src/pages/purchasing/PurchasingPage.tsx` (type column/filter), `web/src/pages/settings/SettingsPage.tsx` (Purchasing tab), site settings form and Organization settings (PR prefix, request recipients), Notifications switches (six new keys), `web/src/components/AlertBell.tsx`, `web/src/components/AppLayout.tsx` (My approvals under Materials when the user holds `purchasing.approve` or `purchasing.approveRequests`), `web/src/App.tsx` (`approvals`), `web/src/api/types.ts`, locales

**Interfaces:**
- Consumes: Tasks 4–6 routes and shapes.
- Produces (`approvals.ts`):
  - `levelsFor(levels: LevelRow[], siteId: string | null, orderTypeId: string | null, amount: number): LevelRow[]` — the same rule as `ApprovalLevelRules.Required` (site set replaces organization set; type levels replace all-types levels; `minAmount <= amount`, ascending)
  - `exampleSentence(t, levels: LevelRow[], amount: number, fmt: (n: number) => string): string` — e.g. "An order of 50,000 needs: Maintenance manager, then Plant manager." / "An order of 500 needs no level: approvers sign within their own limit."
  - `stepState(step): 'done' | 'waiting' | 'pending' | 'stopped'`
  - `whoIsNext(approval): string[]` — names on the waiting step who haven't signed

- [ ] **Step 1: Write the failing tests** in `approvals.test.ts`:

```ts
it('adds levels up from the lowest, at or above the minimum', () => { /* 0/10000/100000 with 10000 → 2 levels */ });
it('lets a site set replace the organization set and type levels replace all-types levels', () => {});
it('writes the example sentence for the settings page', () => { /* with t stub returning key + params, checks keys 'approvals.example' and 'approvals.exampleNone' */ });
it('names who is next on the waiting step', () => { /* all-rule step with B signed, C not → ['C'] */ });
```

- [ ] **Step 2: Run them to see them fail**

Run: `wt-vitest`
Expected: FAIL

- [ ] **Step 3: Implement the helpers, then the screens** — settings tab (order types list using the existing code-list editor; levels table grouped by set then type with add/edit modal: set, type, name, minimum, rule, up to five approvers with the server's `problem` shown as a warning badge; the example sentence with an amount box; purchase-request recipients), order page panel (steps, signatures, earlier rounds folded, nobody-can-sign alert with Restart), sign/reject buttons from `actions`, print block "Approvals" (level · name · date · comment), My approvals page (three lists with links; approve / reject with comment in place), bell entry. Texts under `approvals.*` and `po.*` in both locales.

- [ ] **Step 4: Run tests and build**

Run: `wt-vitest` then `wt-web`
Expected: pass; build clean.

- [ ] **Step 5: Commit**

```bash
git add web && git commit -m "feat(purchasing): approval levels settings, order approval panel, my approvals page"
```

---

### Task 9: Docs and full verification

**Files:**
- Create: `docs/PURCHASE_REQUESTS.md`
- Modify: `README.md` (feature line), `docs/superpowers/plans/2026-11-23-purchase-requests-0.16.0.md` (deferred minors appended after the final review)

- [ ] **Step 1: Write `docs/PURCHASE_REQUESTS.md`** — requests and their statuses, who may do what (the two new permissions), raising from a work order / Short for work / Re-order, turning lines into orders, order types, approval levels (sets, types, adding up, today's rule when none apply, nobody-can-sign and Restart), My approvals, e-mails and their switches, what isn't in 0.16.0.

- [ ] **Step 2: Full verification**

Run: `wt-test`, `wt-vitest`, `wt-web`
Expected: server all green (2 skipped as before), web tests green, build clean.

- [ ] **Step 3: Commit**

```bash
git add docs README.md && git commit -m "docs(0.16.0): purchase requests and approval levels guide"
```

---

## Deferred minors (final review, 23 Nov 2026)

- "order waiting for your signature" e-mails also go to listed approvers who lost "Approve purchases" (recipient list not filtered by CanSignNow).
- the order's approval panel lists every unsigned approver as "waiting for", without marking who can't sign now.
- "Restart approval" has no confirmation; one click drops the round's signatures and re-e-mails the first level.
- LineInput.PurchaseRequestLineId is accepted from any client on POST/PUT /purchase-orders without validation or sync (a crafted call can double-link a request line).
- two buyers ordering the same request line at the same moment can create two live order lines (the "already on an order" check runs before the transaction).
- organization-wide "any one" levels made of site-limited managers who together cover every site show "no-site-access" warning badges.
- orders returned under today's rule don't appear in "Sent back to me" (docs don't say so).
- the legacy importer and sample data create orders with no order type.
- editing an order whose type has since been deactivated fails with "Choose an active order type" unless the type is changed.
- request form — a part line's store defaults to the site's first store, not the site's default store; no asset picker (work order only).
- Short for work silently drops selected lines whose store isn't in the stores list.
- approval-level PUT ignores the RowVersion it returns (last write wins).
