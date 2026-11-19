# E-mail notifications 0.1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 4C CMMS e-mails requesters, technicians, creators, approvers, buyers and suppliers at the key moments, through a queued sender (SES on AWS, SMTP elsewhere).

**Architecture:**
- **`EmailOutbox`** is a stateless singleton. Endpoints and the PM generator call it to add rendered `OutboundEmail` rows to the current `DbContext`, so they commit with the business change.
- **`EmailSenderService`** (a singleton plus a hosted service) claims due rows across tenants, sends them through `IEmailSender` (SES or SMTP), and retries or fails them.
- **Settings and preferences:** organization switches live in `TenantSettings` (JSON); user opt-outs live in `AppUser.EmailOptOuts`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 (SQL Server), AWSSDK.SimpleEmailV2, MailKit, xUnit + SkippableFact integration tests, React 19 + Mantine 8.

**Spec:** `docs/superpowers/specs/2026-11-19-email-notifications-0.1.0-design.md`

**Tooling** (no .NET or Node on the Mac). The helpers live in the scratchpad `bin/` and are pointed at this worktree (`REPO=$HOME/Projects/CMMSX/.worktrees/email-notifications-0.1.0`):
- `wt-test [filter]` runs the server tests.
- `wt-web` runs the web build.
- **`wt-ef <Name>`** adds an EF migration: it runs `dotnet-ef migrations add <Name> --project FourC.Cmms.Core` in the SDK container, with `server/` mounted **read-write**.

Docker Desktop and `cmmsx-sql-1` must be running.

## Global Constraints

- **Event keys are exactly:** `request.submitted`, `request.approved`, `request.rejected`, `request.completed`, `workorder.assigned`, `pm.assigned`, `workorder.completed`, `po.submitted`, `po.approved`, `po.rejected`, `po.sent`, `parts.received`. They live in a constant list `EmailEvents.All`.
- **When something is queued:** nothing is queued unless a provider is configured (`Email:Provider` is `Ses` or `Smtp`) **and** `TenantSettings.EmailEnabled` is true. `EmailEnabled` defaults to **false**.
- **Who is skipped per event:**
  - events in `TenantSettings.DisabledEmails` (default empty) send nothing
  - the actor (`ctx.UserId`, or the actor's e-mail address) is never a recipient
  - one e-mail per address per event, compared case-insensitively
  - a user whose `EmailOptOuts` contains the event key is skipped
- **Exception for `po.sent`:** it ignores `DisabledEmails` and opt-outs, but it still needs a provider and `EmailEnabled`.
- **From and Reply-To:**
  - From display name: `"{tenant name} via {Email:FromName ?? "4C CMMS"}"`. From address: `Email:From`.
  - Reply-To: the actor's user e-mail, when there is one.
- **Escaping:**
  - all user text in HTML bodies goes through `System.Net.WebUtility.HtmlEncode`
  - subjects and display names have CR/LF replaced by spaces and are cut to 200 characters
- **Links:** `{App:PublicUrl}{path}`. When `App:PublicUrl` is empty, there is no link.
- **Retry:**
  - delays after failure: 1, 5, 30 and 120 minutes
  - the row becomes `Failed` after the 5th failed attempt
  - claim lock: 5 minutes; batch size: 20
  - `Sent` rows older than 90 days are deleted
- **One EF migration**, named `EmailNotifications`.
- **No new permission codes.** Admin endpoints use `settings.manage`. "Send to supplier" uses `purchasing.create`.
- **UI text:** all strings go through `t()`, with English in `en.json`.
- **Commits:** one commit per task on `feature/email-notifications-0.1.0`, with the attribution trailer. Never commit to `main`, never push.

## Review Focus

1. **The scheduler creates PM work with no signed-in user** (`ctx.UserId == null`). Nothing is suppressed as "actor", and the e-mails still queue. Test: `Pm_assignees_hear_about_generated_work` (Task 3).
2. **An employee without an address and without a linked user** is skipped, and the work order save still succeeds. Test: inside `Assignees_hear_once_and_the_actor_never_does` (Task 3).
3. **A requester address that is not a valid e-mail** (e.g. `"bob"`). Approve still succeeds. The row fails at send time, after at most 5 attempts. Test: `Sender_retries_then_fails_and_never_blocks_the_action` (Task 2/3).
4. **A burst of 50 queued e-mails** drains in batches of 20 over successive runs, with no row sent twice when two runs overlap. Test: `Concurrent_runs_send_each_email_once` (Task 2).
5. **The admin e-mail log** shows only the organization's own messages. Test: `Email_log_is_per_organization` (Task 5).

---

### Task 1: Data model, settings and migration

**Files:**
- Create: `server/FourC.Cmms.Core/Notifications/OutboundEmail.cs` (entity, `EmailStatus` enum, `EmailEvents` constants)
- Modify:
  - `server/FourC.Cmms.Core/Identity/Identity.cs` (AppUser)
  - `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (TenantSettings)
  - `server/FourC.Cmms.Core/Data/CmmsDbContext.cs` (DbSet, config)
- Create: the migration `EmailNotifications`, via `wt-ef`
- Test: `server/FourC.Cmms.Tests/TenantSettingsTests.cs`

**Interfaces:**
- Produces:
  - `public enum EmailStatus { Pending = 1, Sent = 2, Failed = 3 }`
  - `public static class EmailEvents { public const string RequestSubmitted = "request.submitted"; ... ; public static readonly IReadOnlyList<string> All }`, with the 12 keys in Global Constraints order
  - `public class OutboundEmail : Entity, ITenantOwned`, with:
    - `TenantId`, `EventKey`, `ToAddress`, `ToName?`, `ReplyTo?`, `Subject`, `HtmlBody`, `TextBody`
    - `EntityType?`, `EntityId?`
    - `Status`, `Attempts`, `NextAttemptAt`, `LockedUntil?`, `SentAt?`, `LastError?`, `CreatedAt`
  - `AppUser.EmailOptOuts : List<string> = []`
  - `TenantSettings.EmailEnabled : bool` and `TenantSettings.DisabledEmails : List<string> = []`
  - `CmmsDbContext.OutboundEmails`

- [ ] **Step 1: Failing test.** Extend `TenantSettingsTests.Settings_saved_before_0_0_2_read_as_off` (rename it to `Settings_saved_before_0_1_0_read_as_off`) with `Assert.False(s.EmailEnabled); Assert.Empty(s.DisabledEmails);`.
- [ ] **Step 2:** Run `wt-test "FullyQualifiedName~TenantSettingsTests"`. Expected: compile errors on the missing members.
- [ ] **Step 3: Implement the model.**
  - Lengths: `EventKey` 40, `ToAddress` 320, `ToName` 200, `ReplyTo` 320, `Subject` 300, `EntityType` 40, `LastError` 1000. `HtmlBody` and `TextBody` are `nvarchar(max)`.
  - Index `(Status, NextAttemptAt)`.
  - It falls under the tenant query filter (it is `ITenantOwned`).
- [ ] **Step 4: Add the migration** with `wt-ef EmailNotifications`. Check that it creates `OutboundEmails` and adds `AspNetUsers.EmailOptOuts`, which must be non-null and default `'[]'`, so existing users read as no opt-outs.
- [ ] **Step 5:** Run `wt-test`. Expected: all pass (all integration tests apply the migration on startup).
- [ ] **Step 6: Commit** `E-mail: outbox table, user opt-outs and organization switches`.

### Task 2: Sending: providers and the background sender

**Files:**
- Create:
  - `server/FourC.Cmms.Api/Infrastructure/Email.cs`: `EmailOptions`, `IEmailSender`, `EmailMessage`, `SesEmailSender`, `SmtpEmailSender`
  - `server/FourC.Cmms.Api/Features/EmailSenderService.cs`
- Modify:
  - `server/FourC.Cmms.Api/FourC.Cmms.Api.csproj`: add `AWSSDK.SimpleEmailV2` (same 4.x line as AWSSDK.S3) and `MailKit`
  - `server/FourC.Cmms.Api/Program.cs`: registrations
- Test:
  - Create `server/FourC.Cmms.Tests/RecordingEmailSender.cs`
  - Modify `ApiFixture` in `ApiTests.cs`
  - Create `server/FourC.Cmms.Tests/EmailTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record EmailMessage(string To, string? ToName, string From, string FromName, string? ReplyTo, string Subject, string Html, string Text)`
  - `public interface IEmailSender { Task SendAsync(EmailMessage m, CancellationToken ct); }`
  - `public sealed class EmailOptions`, bound from `Email`, with: `Provider`, `From`, `FromName = "4C CMMS"`, `Region`, `PollSeconds = 15`, `AutoSend = true`, and `Smtp { Host, Port = 587, User, Password, UseTls = true }`. Its `bool Available` is true when `Provider` is `ses` or `smtp` (case-insensitive) and `From` is set.
  - `public sealed class EmailSenderService : BackgroundService`, with `public Task<int> RunOnceAsync(DateTimeOffset? now = null, CancellationToken ct = default)`, which returns the number sent. It is registered as a singleton and as a hosted service (via `AddHostedService(sp => sp.GetRequiredService<EmailSenderService>())`).
  - **Registration:** `IEmailSender` → `SesEmailSender` or `SmtpEmailSender` by provider, only when `Available`. `EmailSenderService` loops only when `Available && AutoSend`.
- Test double: `RecordingEmailSender : IEmailSender`.
  - It records into `ConcurrentQueue<EmailMessage> Sent`.
  - It throws `InvalidOperationException("mailbox rejected")` for addresses without `@` or containing `fail`.
  - `ApiFixture` sets `Email:Provider=Smtp`, `Email:From=noreply@cmms.test`, `Email:AutoSend=false` and `App:PublicUrl=https://cmms.test`, and replaces `IEmailSender` with one shared `RecordingEmailSender` exposed as `fx.Emails`.
  - The fixture's `WithWebHostBuilder` uses `ConfigureTestServices`.

- [ ] **Step 1: Failing tests in `EmailTests`.** They insert rows directly through a scope's `CmmsDbContext` with `IgnoreQueryFilters`, for one tenant, using a helper `QueueRaw(int count, string to)`.
  - **`Sender_sends_due_emails_and_marks_them_sent`:** queue 3 → `RunOnceAsync()` returns 3, `fx.Emails.Sent` contains the 3 addresses, and the rows are `Sent` with `SentAt` set.
  - **`Sender_retries_then_fails_and_never_blocks_the_action`:** queue 1 to `"bob"`.
    - `RunOnceAsync()` → `Attempts == 1`, `Status == Pending`, `NextAttemptAt ≈ now + 1 min`, `LastError` contains `"mailbox rejected"`.
    - Then call `RunOnceAsync(now)` with `now` advanced past each delay (+2 min, +6 min, +31 min, +121 min) → after the 5th call it is `Failed` with `Attempts == 5`.
  - **`Concurrent_runs_send_each_email_once`:** queue 50 → `await Task.WhenAll(svc.RunOnceAsync(), svc.RunOnceAsync())`, then two more runs → `fx.Emails.Sent` has exactly 50 messages to the 50 distinct addresses.
- [ ] **Step 2:** Run `wt-test "FullyQualifiedName~EmailTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement the providers.**
  - **SES:** `AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(Region))` and `SendEmailAsync`, with `Simple` content (Html + Text), `ReplyToAddresses`, and From `"{FromName}" <From>` built through `MimeKit.MailboxAddress` formatting.
  - **SMTP:** MailKit `SmtpClient`, with `SecureSocketOptions.StartTlsWhenAvailable` when `UseTls`, otherwise `None`. Authenticate only when `User` is set.
- [ ] **Step 4: Implement `RunOnceAsync`.** Use a root `CmmsDbContext` (`new CmmsDbContext(options, new FixedTenantContext(null))`) with `IgnoreQueryFilters()`.
  1. **Claim:** `ExecuteUpdateAsync` sets `LockedUntil = now + 5 min` on the ids picked by `Where(Status == Pending && NextAttemptAt <= now && (LockedUntil == null || LockedUntil < now)).OrderBy(NextAttemptAt).Take(20)`, adding the condition `LockedUntil == null || LockedUntil < now` again in the update so a concurrent run can't double-claim.
  2. **Load** the rows the update claimed: re-select the ids whose `LockedUntil == thisClaim` (use the exact timestamp plus a random millisecond offset as the claim marker).
  3. **Send** each row. **On success:** set `Sent`, `SentAt` and `LockedUntil = null`. **On failure:**
     - `Attempts++`
     - `LastError` = the message, cut to 1000 characters
     - `NextAttemptAt` = now + delay[Attempts − 1]
     - when `Attempts >= 5`, set `Failed`
     - `LockedUntil = null`
  4. **Clean up:** delete `Sent` rows older than 90 days.
  - The From display name uses the tenant name: load the tenant names for the batch.
- [ ] **Step 5:** Run `wt-test`. Expected: all pass.
- [ ] **Step 6: Commit** `E-mail: SES and SMTP senders and a queued background sender with retries`.

### Task 3: The outbox, templates and the work events

**Files:**
- Create:
  - `server/FourC.Cmms.Api/Features/EmailOutbox.cs`
  - `server/FourC.Cmms.Api/Features/EmailTemplates.cs`
- Modify:
  - `WorkRequestEndpoints.cs` (create, approve, reject)
  - `WorkOrderEndpoints.cs`: `CreateAsync`, update (assignee diff) and status Completed
  - `PmGenerator.cs`: thread the outbox through `RunAsync` → `GenerateForAsync` → `CreateWorkOrderAsync`, and through `OnReadingAsync`
  - `PmEndpoints.cs` and the `PmSchedulerService` callers
  - `Program.cs`: `services.AddSingleton<EmailOutbox>()`
- Test: `EmailTests.cs`

**Interfaces:**
- Consumes: Task 1 entities and keys; Task 2 `EmailOptions`, `EmailSenderService.RunOnceAsync`, `fx.Emails`.
- Produces: `public sealed class EmailOutbox(IOptions<EmailOptions> options, IConfiguration config)` with:
  - `bool Available`
  - `Task WorkOrderCreatedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)`: assignees → `workorder.assigned`, or `pm.assigned` when `w.PmScheduleId != null`
  - `Task AssigneesAddedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)`
  - `Task WorkOrderCompletedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, CancellationToken ct)`: the creator gets `workorder.completed`; when `w.WorkRequestId` is set, the requester gets `request.completed`
  - `Task RequestSubmittedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, CancellationToken ct)`
  - `Task RequestDecidedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, string? workOrderNumber, CancellationToken ct)`: approved or rejected, chosen by `r.Status`
  - Plus an internal core, `QueueAsync(db, ctx, string eventKey, IEnumerable<EmailRecipient> to, EmailContent content, string? entityType, Guid? entityId, CancellationToken ct, bool ignoreToggles = false)`, which applies every Global Constraints rule.
- Also produces:
  - `public sealed record EmailRecipient(string? Email, string? Name, Guid? UserId)`
  - `public sealed record EmailContent(string Subject, string Html, string Text)`
  - The `EmailTemplates` static methods, one per event, each returning `EmailContent`. HTML layout: a heading, a facts list, a button link (when a URL is known), and a footer `"{org} · Change your e-mail settings in 4C CMMS"`.
- **Optional parameters:** `CreateAsync`, `PmGenerator.RunAsync/CreateWorkOrderAsync/OnReadingAsync` gain an optional trailing `EmailOutbox? outbox = null`. Endpoints and the scheduler pass the singleton.

- [ ] **Step 1: Failing tests in `EmailTests`.** Each test gets its own organization via an `OrgAsync(bool enable = true)` helper that registers, and when `enable` is true sets `emailEnabled = true` through `TestSettings`. Every address is unique (a `{guid}@cmms.test` helper). Each test calls `RunOnceAsync()`, then looks at `fx.Emails.Sent` filtered to its addresses.
  - **`Nothing_is_queued_until_the_organization_switches_email_on`:** create a work order with an assignee who has an e-mail, in an org with `enable: false` → no message to that address.
  - **`Assignees_hear_once_and_the_actor_never_does`:**
    - The admin creates employee X (with an e-mail), employee Y (with no e-mail and no user) and employee Z (linked to the admin user, no e-mail of its own).
    - The admin creates a work order assigned to X, Y and Z → exactly 1 e-mail, to X. Its subject contains the WO number and its HTML contains `https://cmms.test/work-orders/{id}`.
    - Editing the WO to add employee W sends 1 e-mail, to W only.
  - **`Requester_hears_the_decision_and_completion`:**
    - A Requester user (with an e-mail) submits a request. The admin (actor) approves it.
    - Before the approval, `request.submitted` goes to a second user with the "Maintenance manager" role, not to the requester.
    - The approval sends `request.approved` to the requester, containing the WO number.
    - Completing the WO sends `request.completed` to the requester.
    - A second request, rejected with note `"<b>no</b>"`, sends `request.rejected`, whose HTML contains `&lt;b&gt;no&lt;/b&gt;`.
  - **`Opt_outs_and_disabled_events_are_respected`:**
    - The technician user (linked employee) opts out of `workorder.assigned`, via `PUT /api/auth/me/email-opt-outs` — that endpoint is added in Task 5, so here set the user's opt-out through the test db scope instead. Assigning them sends nothing.
    - An org with `disabledEmails = ["workorder.completed"]` sends no completion e-mail to the creator.
  - **`Pm_assignees_hear_about_generated_work`:**
    - The schedule has assignee X. `PmSchedulerService.RunOnceAsync` is resolved from `fx.Factory.Services` (no user).
    - Expect 1 `pm.assigned` e-mail to X.
  - **`Subjects_never_carry_line_breaks`:** a work order title `"Leak\r\nBcc: evil@x"` → the subject contains no `\r` or `\n`.
- [ ] **Step 2:** Run `wt-test "FullyQualifiedName~EmailTests"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement `EmailOutbox` and `EmailTemplates`.**
  - **Recipient resolution:**
    - Employees: `Email`, or else the linked user's `Email`; `UserId` = `Employee.UserId`.
    - The requester: `r.RequesterEmail`, or else the creating user.
    - The creator: the `CreatedById` user.
    - Request managers: active users whose roles' `Permissions` contain `requests.manage`, read in memory from the tenant's roles.
  - **Hook the call sites:**
    - **Work order create:** `CreateAsync` calls `WorkOrderCreatedAsync` before its save.
    - **Work order update:** compute the added employee ids before `SyncAssignees`, call `AssigneesAddedAsync`, then save.
    - **Status → Completed:** call `WorkOrderCompletedAsync` before the save.
    - **Request create:** `RequestSubmittedAsync`.
    - **Request approve and reject:** `RequestDecidedAsync`.
- [ ] **Step 4:** Run `wt-test`. Expected: all pass.
- [ ] **Step 5: Commit** `E-mail: work request and work order notifications`.

### Task 4: Purchasing e-mails and Send to supplier

**Files:**
- Modify:
  - `EmailOutbox.cs`, `EmailTemplates.cs`
  - `server/FourC.Cmms.Api/Features/PurchasingEndpoints.cs`: submit, approve, reject, receive, and a new `POST /{id}/send`
- Test: `EmailTests.cs`

**Interfaces:**
- Produces, on `EmailOutbox`:
  - `PoSubmittedAsync(db, ctx, PurchaseOrder po, ct)`: approvers are active users with `purchasing.approve` whose `PurchaseApprovalLimit` is null or ≥ `PurchaseOrderMath.TotalInBase(po)`
  - `PoDecidedAsync(db, ctx, PurchaseOrder po, string? reason, ct)`: goes to `po.CreatedById`
  - `PartsReceivedAsync(db, ctx, PurchaseOrder po, IReadOnlyCollection<(Guid WorkOrderId, string Line)> received, ct)`: one e-mail per work order to its assignees, listing that WO's received lines
  - `Task<string?> PoToSupplierAsync(db, ctx, PurchaseOrder po, ct)`: returns an error message, or null when queued. The errors are "E-mail isn't set up." and "The supplier has no e-mail address."
- Endpoint: `POST /api/purchase-orders/{id}/send`
  - needs `purchasing.create`
  - the PO must be Approved, PartiallyReceived, Received or Closed; otherwise 400 "Only approved orders can be sent."
  - it saves, then returns `{ queued: true, to }`, or 400 with the error

- [ ] **Step 1: Failing tests.**
  - **`Approvers_hear_about_orders_within_their_limit`:** approver A (limit 1000), approver B (limit 100), the admin creates and submits a PO with a 500 total → only A gets `po.submitted`.
  - **`Buyer_hears_approval_and_rejection`:** a buyer user creates and submits; the admin approves → `po.approved` to the buyer. Another PO is rejected with reason "Too dear" → `po.rejected`, containing it.
  - **`Supplier_gets_the_order_when_sent`:**
    - Supplier with an e-mail and an approved PO: `POST /send` → 200, and 1 e-mail to the supplier whose HTML contains each line description and the formatted total.
    - A supplier without an e-mail → 400 "The supplier has no e-mail address."
    - A draft PO → 400.
  - **`Technicians_hear_when_parts_for_their_job_arrive`:**
    - A PO line charged to a work order with assignee X (via the service-line/WO purchase path used in `InventoryTests`). Receive it fully.
    - Expect 1 `parts.received` e-mail to X, listing the line.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3: Implement.** Hook the calls before each endpoint's `SaveChangesAsync`. Collect the received lines in the receive endpoint as it processes the input lines.
- [ ] **Step 4:** Run `wt-test`. Expected: pass.
- [ ] **Step 5: Commit** `E-mail: purchase order notifications and Send to supplier`.

### Task 5: Admin and user screens

**Files:**
- Create: `server/FourC.Cmms.Api/Features/EmailEndpoints.cs`, with:
  - `GET /api/email/status` → `{ available, from, enabled }` (authorized)
  - `POST /api/email/test` (`settings.manage`): queues one e-mail to the caller, ignoring the toggles and `EmailEnabled`, but needs `Available`. It returns 400 "E-mail isn't set up on this server." otherwise.
  - `GET /api/email/log?take=100` (`settings.manage`): the tenant's messages, newest first: `{ id, createdAt, sentAt, eventKey, toAddress, subject, status, attempts, lastError }`
  - `GET` and `PUT /api/auth/me/email-opt-outs` (authorized): a body of event keys; unknown keys are dropped
- Modify: `Program.cs` (`api.MapEmailEndpoints()`)
- Create:
  - `web/src/pages/settings/NotificationSettings.tsx`, a new tab `notifications` in `SettingsPage.tsx`
  - `web/src/components/EmailPreferences.tsx`, a modal opened from a new user-menu item in `AppLayout.tsx`
- Modify:
  - `web/src/pages/purchasing/PoDetailPage.tsx`: a "Send to supplier" button with a confirm dialog showing the supplier address; it is shown for approved and later statuses when the user may create POs
  - `web/src/api/types.ts`, `en.json`
- Test: `EmailTests.cs`

**Interfaces:**
- Consumes: `EmailOutbox.Available`, `EmailEvents.All`, `TenantSettings.EmailEnabled/DisabledEmails`, and the PO send endpoint (Task 4).

- [ ] **Step 1: Failing tests.**
  - **`Test_email_reaches_the_admin_even_before_email_is_switched_on`:** an org with e-mail off → `POST /api/email/test` → after a run, 1 message to the admin's address.
  - **`Email_log_is_per_organization`:** orgs P and Q each queue one test e-mail → P's log shows only P's message.
  - **`Users_choose_their_own_email_opt_outs`:** `PUT ["workorder.assigned","nonsense"]` → `GET` returns `["workorder.assigned"]`, and assigning that user's linked employee sends nothing.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3: Implement the endpoints.**
- [ ] **Step 4: Web.**
  - **Notifications tab:**
    - a status card ("E-mail isn't set up on this server" when unavailable; otherwise "Sending from {from}")
    - the master switch (`settings.emailEnabled`)
    - one switch per event, with a label and a one-line description; switching one off adds it to `disabledEmails`
    - "Send me a test e-mail"
    - the log table (time, to, subject, a status badge, the error) with refresh
  - Saving the switches uses the existing `PUT /settings` with the whole settings object, the same as the Organization tab.
  - **User-menu "E-mail notifications" modal:** one switch per event except `po.sent`, saved through the opt-out endpoint.
  - **PO "Send to supplier":** `modals.openConfirmModal` with the body "Send {number} to {email}?", then a success or error notification.
  - Strings go under `email.*`.
- [ ] **Step 5:** Run `wt-test` and `wt-web`. Expected: pass and `exit=0`.
- [ ] **Step 6: Commit** `E-mail: notification settings, log, test e-mail and personal opt-outs`.

### Task 6: AWS, local mailbox and docs

**Files:**
- Modify:
  - `infra/aws/cmms.yml`
  - `.github/workflows/deploy-aws.yml`
  - `docker-compose.yml`
  - `docs/DEPLOY_AWS.md`
  - `CHANGELOG.md`

- [ ] **Step 1: The template.**
  - Add parameters `MailFromAddress` and `MailDomain` (String, default `""`), with conditions `HasMailFrom` and `HasMailDomain`.
  - Add an `AWS::SES::EmailIdentity` (`EmailIdentity: !Ref MailDomain`, Easy DKIM enabled), conditioned on `HasMailDomain`.
  - Task role: add `ses:SendEmail` and `ses:SendRawEmail` on `arn:aws:ses:${Region}:${Account}:identity/${MailDomain}`, under `HasMailDomain`.
  - Container environment:
    - `App__PublicUrl = https://${Distribution.DomainName}`, always
    - `Email__Provider = Ses`, `Email__From = MailFromAddress` and `Email__Region = AWS::Region`, via `!If HasMailFrom`
  - Outputs: `DkimRecord1`–`3`, each as `"<name> CNAME <value>"` from the identity's `DkimDNSTokenName1`–`3` / `DkimDNSTokenValue1`–`3`, under `HasMailDomain`.
- [ ] **Step 2: The workflow** passes `MailFromAddress="${{ vars.MAIL_FROM_ADDRESS }}"` and `MailDomain="${{ vars.MAIL_DOMAIN }}"` in `--parameter-overrides`.
- [ ] **Step 3: Compose.**
  - Add `mailpit` (`axllent/mailpit:v1.21`, ports `8025:8025`).
  - The app gets `Email__Provider: Smtp`, `Email__From: noreply@cmms.local`, `Email__Smtp__Host: mailpit`, `Email__Smtp__Port: "1025"`, `Email__Smtp__UseTls: "false"` and `App__PublicUrl: http://localhost:8080`.
- [ ] **Step 4: Docs.** Add a "Sending e-mail" section to `DEPLOY_AWS.md`:
  1. Set the GitHub repository variables `MAIL_DOMAIN` and `MAIL_FROM_ADDRESS`.
  2. Push or re-run the deploy.
  3. Add the three CNAMEs from the stack outputs at the DNS host.
  4. Wait for SES to show the domain as verified.
  5. Request production access: SES → Account dashboard → Request production access (mail type: transactional).
  6. Switch e-mail on in Settings → Notifications and send a test.
- [ ] **Step 5: Changelog.** Add `## [0.1.0] - <date>`, with an Added section in user words, and a note that e-mail is off per organization until switched on and that the server needs the AWS steps.
- [ ] **Step 6: Verify the template.** Run `docker run --rm -v "$REPO/infra":/w python:3.12-slim sh -c "pip install -q cfn-lint && cfn-lint /w/aws/cmms.yml"`. Expected: no errors (warnings are OK if they existed before).
- [ ] **Step 7:** Run `wt-test` and `wt-web`. Expected: pass.
- [ ] **Step 8: Commit** `E-mail: AWS SES identity and permissions, local test mailbox, setup guide, changelog`.

### Task 7: Hands-on check, review and release

- [ ] **Step 1: Run a local stack.**
  - Build the worktree image.
  - Run it on port 8081, plus a `mailpit` container on `cmmsx_default` (UI on host port 8025), using a fresh database `CmmsEmailTest`, `Email__Provider=Smtp`, `Email__Smtp__Host=mailpit`, `Email__Smtp__Port=1025`, `Email__Smtp__UseTls=false` and `App__PublicUrl=http://localhost:8081`.
- [ ] **Step 2: In the browser:**
  1. Register a test organization and load sample data.
  2. Switch e-mail on and send a test.
  3. Create a work order assigned to an employee with an address.
  4. Submit and approve a PO, then Send to supplier.
  5. Check each message in Mailpit (localhost:8025): rendering, links, the From display name and Reply-To.
  6. Check the log in Settings.
- [ ] **Step 3: Remove** the containers and the test database.
- [ ] **Step 4: Final review.** A fresh reviewer reads the whole branch. Fix any Critical or Important findings test-first; ledger the minors.
- [ ] **Step 5: Hand over.** Merging, the release (`scripts/release.sh 0.1.0`), and the AWS setup the user does (domain, DNS, production access) need the user's OK. That includes asking for the domain and address for `MAIL_DOMAIN` and `MAIL_FROM_ADDRESS`.
