# E-mail notifications (0.1.0): design

*19 November 2026. Item WO-1 (with PU-3) of the DimoMaint comparison, the first minor release on the agreed release path.*

## Intent and why

Maintenance teams miss work they aren't told about. Today 4C CMMS sends no e-mail at all:
- requesters never hear what happened to their request
- technicians must open the app to find new jobs
- buyers and approvers chase each other about purchase orders

DimoMaint e-mails each party at every step. 0.1.0 adds e-mail notifications for the moments that matter most, and lets a PO be e-mailed to the supplier.

## Decisions taken with the user

- Mail is sent through **Amazon SES** from an address on **the company's own domain**. The domain is verified in SES with DKIM records, and the AWS account leaves the SES sandbox. The user does both; we write the steps.
- **Queued sending:** an e-mail is saved to an outbox table in the same database save as the action that caused it. A background sender delivers it, with retries and a log.
- **First set of e-mails** (see the table below). There is an organization on/off per e-mail, and each user can opt out of any of them.
- **From:** `"<Organization> via 4C CMMS" <configured address>`. **Reply-To** is the user who acted, when that user has an e-mail address.
- Every e-mail **links to its record** in the app.
- **PO to supplier** carries the whole order in the e-mail body. No PDF in 0.1.0.

## Goals

1. Each event in the table queues one e-mail per recipient, with these exceptions:
   - recipients without an address are skipped
   - the person who did the action is skipped, because nobody needs an e-mail about their own click
   - a user who opted out of that event is skipped
   - nothing is queued when the organization switched e-mail or that event off
2. A background sender delivers queued e-mail within about a minute. It retries failures and keeps a log the admin can read.
3. Admins control e-mail per organization. Users control their own opt-outs.
4. It works on AWS (SES through the task role, so no stored secret) and locally (SMTP to a test mailbox), and tests use an in-memory sender.

## Non-goals (later)

- PDF attachments.
- SMS or push notifications.
- Daily digests.
- Routing requests by site or trade.
- Bounce and complaint handling.
- Unsubscribe links for people without an account (requesters, suppliers).
- Localized or customizable templates.
- Comment e-mails.
- Overdue reminders.

## Anti-goals

- **No surprise e-mail.** Existing organizations, including the imported 4CSQL ones, send nothing until an admin switches e-mail on. That matches 0.0.2's "off until switched on".
- **No duplicates.** One event sends one e-mail per person, even when a person qualifies twice (for example, assignee and creator).
- **No injected markup or headers.** User text (titles, descriptions, reasons, names) is HTML-encoded in bodies, and stripped of line breaks in subjects and display names.
- **E-mail trouble never blocks work.** A missing or failing mail service never makes a save fail.

## Events

| Key | When | To |
|---|---|---|
| `request.submitted` | A work request is created | Active users with `requests.manage` |
| `request.approved` | A request is approved into a work order | The requester |
| `request.rejected` | A request is rejected | The requester, with the reason |
| `request.completed` | A work order that came from a request is completed | The requester |
| `workorder.assigned` | Employees are added to a non-PM work order (on create, edit or request approval) | The added employees |
| `pm.assigned` | A PM work order is created with assignees (manually or by the scheduler) | Its assignees |
| `workorder.completed` | A work order is completed | Its creator |
| `po.submitted` | A purchase order is submitted for approval | Active users with `purchasing.approve` whose limit covers the order's base-currency total (or who have no limit) |
| `po.approved`, `po.rejected` | A PO is approved or rejected | The PO's creator; a rejection includes the reason |
| `po.sent` | A user clicks **Send to supplier** on an approved PO | The supplier's e-mail address |
| `parts.received` | A receipt completes or part-receives PO lines charged to a work order | That work order's assignees |

**Who "the requester" is:**
1. The request's `RequesterEmail`, if set.
2. Otherwise the creating user's e-mail.

**Who an employee is:** the employee's `Email`, if set. Otherwise the e-mail of the user linked to them (`Employee.UserId`).

**Opt-outs:**
- Opt-outs belong to users. An employee linked to a user follows that user's opt-outs.
- People without an account (requesters by address only, suppliers, unlinked employees) can't opt out.
- `po.sent` is a deliberate user action, so it ignores the organization toggle. It does need e-mail switched on and a supplier address. Without an address it is refused with a clear message.

## Approaches considered

1. **Outbox table + background sender (chosen).**
   - The e-mail row is saved with the business change, so either both happen or neither does.
   - A hosted service sends and retries.
   - It costs one migration and a small worker.
2. **Send inline during the request.**
   - Simplest, but every save waits on SES, and a send failure either loses the e-mail or fails the user's action.
   - Rejected.
3. **AWS SQS queue.**
   - Durable, but it adds AWS setup and cost, and it is AWS-only, while the Azure template and local Docker still need e-mail.
   - Rejected.

**Sending channel:** an `IEmailSender` interface with three providers.
- **`Ses`:** the AWS SDK SESv2 `SendEmail`, credentials from the ECS task role.
- **`Smtp`:** for local Docker with a Mailpit container, for Azure, and for anyone with an SMTP relay.
- **None:** when not configured, e-mail is unavailable. Nothing is queued, and Settings explains why.

## Design

### Data
- **New entity `OutboundEmail`** (tenant-owned).
  - Fields: `Id`, `TenantId`, `EventKey`, `ToAddress`, `ToName`, `ReplyTo`, `Subject`, `HtmlBody`, `TextBody`, `EntityType`, `EntityId`, `Status` (`Pending | Sent | Failed`), `Attempts`, `NextAttemptAt`, `LockedUntil`, `SentAt`, `LastError`, `CreatedAt`.
  - Index on `(Status, NextAttemptAt)`.
- **`AppUser.EmailOptOuts`:** a list of event keys, stored as JSON like other lists.
- **`TenantSettings`** (JSON, no migration):
  - `EmailEnabled` (default **false**)
  - `DisabledEmails`: a list of event keys, default empty, so every event is on once e-mail is enabled
- **One EF migration** adds the `OutboundEmails` table and the `EmailOptOuts` column.

### Configuration (per server)
- `Email:Provider`: `Ses`, `Smtp` or empty.
- `Email:From`: the address.
- `Email:FromName`: default `4C CMMS`.
- For `Ses`: `Email:Region`.
- For `Smtp`: `Email:Smtp:Host`, `Port`, `User`, `Password`, `UseTls`.
- `App:PublicUrl`: the base of links, e.g. `https://dxvjhwlc6echh.cloudfront.net`.
- `Email:PollSeconds`: default 15.

### Queuing
- **`EmailOutbox`** (an API service) builds and adds `OutboundEmail` rows to the current `DbContext`, and the caller's own `SaveChangesAsync` commits them. It:
  - resolves recipients
  - applies the organization switch and toggles, user opt-outs, actor suppression and de-duplication
  - renders the templates
- **Call sites:**
  - the work request create, approve, reject and cancel endpoints
  - the work order create and update endpoints, comparing assignees before and after
  - the status endpoint, on Completed
  - PM generation (`PmGenerator.CreateWorkOrderAsync`), using the scheduler's tenant context
  - the PO submit, approve, reject and receive endpoints
  - a new `POST /purchase-orders/{id}/send` (permission `purchasing.create`, PO status Approved or later)

### Sending
- `EmailSenderService` (a `BackgroundService`) wakes every `PollSeconds`.
- **Claiming:** it claims up to 20 due rows across all tenants by setting `LockedUntil = now + 5 min`, using a conditional update, so two app instances (for example during a deploy) never send the same row.
- **Sending:** it sends each claimed row and marks it Sent.
- **On failure:**
  - it sets `Attempts++`, `LastError`, and `NextAttemptAt` = 1, 5, 30 or 120 minutes
  - after 5 attempts the row is `Failed`
- A failure on one row never stops the batch.
- Sent rows older than 90 days are deleted.

### Templates
- One small `EmailTemplates` class: per event, a subject, a short HTML body (heading, two to four lines of facts, a button link to the record, a footer naming the organization and "Change your e-mail settings in 4C CMMS"), and a plain-text twin.
- The `po.sent` body is a table of the order lines and totals, with no link, since the supplier has no account.

### UI
- **Settings, new Notifications tab** (needs `settings.manage`):
  - the master switch
  - one switch per event, with a plain description
  - "Send me a test e-mail"
  - the e-mail log: the last 100 messages with time, to, subject, status and error
  - when the server has no provider, a notice explains e-mail isn't set up on this server
- **User menu, "E-mail notifications":** each event as a switch for the signed-in user, saved to their opt-outs.
- **PO page:** a "Send to supplier" button (approved POs) that confirms the address first.

### AWS (infra/aws/cmms.yml and the deploy workflow)
- **New parameters:**
  - `MailFromAddress`, default empty
  - `MailDomain`, default empty
- **The workflow passes them** from GitHub repository variables `MAIL_FROM_ADDRESS` and `MAIL_DOMAIN`.
- **When `MailDomain` is set:**
  - an `AWS::SES::EmailIdentity` with Easy DKIM is created
  - the DKIM CNAME records appear in the stack outputs
  - the task role gets `ses:SendEmail` on that identity
- **When `MailFromAddress` is set:**
  - `Email__Provider=Ses`, `Email__From` and `Email__Region` are set
  - `App__PublicUrl` is always set from the CloudFront domain
- **`docs/DEPLOY_AWS.md` gains steps:**
  1. Set the two variables.
  2. Deploy.
  3. Add the three DKIM CNAMEs at the DNS host.
  4. Request SES production access.
  5. Switch e-mail on in Settings.

### Local
- `docker-compose.yml` gains a `mailpit` service: SMTP on 1025, web UI on 8025.
- The app service uses `Email__Provider=Smtp` pointing at it.

## Tests

Integration tests use an in-memory `IEmailSender`, registered in the test fixture, that records messages:
- each event queues the right recipients
- organization off, event off, opt-out and actor-suppression cases queue nothing
- de-duplication
- HTML-encoding of user text and line breaks stripped from subjects
- the sender marks rows Sent, then retries and finally fails on errors from a failing test sender
- `po.sent` needs a supplier address
- two concurrent claim passes never send a row twice

## Review focus (failure modes to check)

1. **A burst of PM generation** (catch-up after downtime) queues many e-mails. The sender drains them in batches without timeouts.
2. **An employee without an address and without a linked user** is skipped silently, without an error.
3. **Request approve/reject** must still succeed when the requester address is malformed. Bad addresses fail at send time, are logged, and are never retried more than 5 times.
4. **The scheduler** (no signed-in user) is never treated as an "actor" to suppress.
5. **Tenant isolation:** the sender reads all tenants' rows, but the admin log only shows the organization's own.

## Calls made while checking the design (not discussed with the user)
- **"Send me a test e-mail"** works whenever the server has a provider, even with the organization switch off, so an admin can check the setup before switching e-mail on.
- **When `App:PublicUrl` is not set** (some local setups), e-mails carry no link rather than a broken one.
- **Two e-mails, not one:** someone who is both a request's requester and its work order's creator gets the two different e-mails. De-duplication applies within one event.
- **PM work created by the scheduler** has no creator, so it sends no `workorder.completed` e-mail.

## Left to the builder

- Exact wording of e-mails and labels.
- The HTML layout (inline CSS, about 600 px wide).
- The claiming SQL.
- The test structure.
- The SMTP library: MailKit preferred, or `System.Net.Mail` if adding a package is a problem.
