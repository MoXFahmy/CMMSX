# CMX 0.10.0 — Requests that close the loop (design)

Status: approved to build as the roadmap describes it (the user said "go ahead" and opted out of questions).
Calls I made on the user's behalf are marked **[call]**.

## Why

Today a requester hears "approved" and then nothing; every request goes to the same people; nobody can say when a room
or area may be worked in; and work can be refused, cancelled or reopened without a word. 0.10.0 closes the loop between
the person who asked and maintenance, and lets an administrator decide who hears what.

## Goals

1. **Request types** — an editable list (e.g. "Air conditioning", "Plumbing", "IT"). Each type has a default trade,
   a default priority, recipients told about new requests, and optionally auto-approval into a work order for a set
   trade or team.
2. **Extra recipients** on a request. Type recipients and extra recipients are **added to** the site's request
   recipients from 0.9.0 **[call, announced]**.
3. **Requester accepts or refuses finished work** — rating 1–5 and a comment. Refuse reopens the work order with the
   reason; Accept closes the request. Shown on the request, on the work order, and in a new satisfaction report.
4. **Comments on requests** — a thread visible to the requester and maintenance; each new comment is e-mailed to the
   other side; the work order shows the request's comments read-only (not copied).
5. **Required comments** — an administrator chooses which actions need a comment: reject a request, refuse finished
   work, put on hold, reopen, cancel, complete. Enforced on the server, so web and phone both obey.
6. **Cancellation reasons** — an editable list; cancelling a work order asks for a reason plus a note. The reason shows
   in the work-order list (column and filter) and in a new "Cancelled work" report.
7. **Access windows** — on a request or work order, one or more windows (start, end, note) when the work may be done.
   Shown on the work order, the phone job and the planning board, with a warning when work is planned outside every
   window.
8. **"Maintenance only while stopped"** — an asset flag, copied to its requests, work orders and PMs; a badge and a
   filter so planners book shutdowns.
9. **Who gets which e-mail** — Settings › Notifications becomes a grid: rows are events, columns are recipients
   (requester, request recipients, technicians, contractors, issuer). Includes a new overdue e-mail.

## Non-goals

- No automatic acceptance after N days of silence; a request whose requester never answers stays "waiting for the
  requester" (managers see it). **[call]**
- Accepting does not close the work order; closing stays a maintenance step (needs the close permission). **[call]**
- Requesters without a CMX login cannot comment in CMX; they receive comments by e-mail and reply by e-mail (Reply-To is
  the commenter). The single-use link page does only accept/refuse. **[call, announced]**
- No SMS/WhatsApp; no per-site e-mail grids (one grid per organization).
- No import columns for the new fields.

## Anti-goals (would make it a failure even if it "works")

- Existing organizations suddenly get e-mails they did not get before. The grid's defaults equal today's behaviour;
  new events default to sensible recipients but the overdue e-mail never floods with old backlog.
- The public answer page leaking anything beyond the one request, or working twice.
- A limited user (0.9.0 site/branch access) seeing or being e-mailed about records outside their access.

## Approaches considered

- **Answering finished work.** (a) In-app only — fails requesters without a login. (b) Signed link for everyone — a
  logged-in requester would answer outside the app and the link becomes a credential for people who already have one.
  (c) **Chosen:** the request's creator answers on the request page; any other requester gets a single-use link
  (random 256-bit token, only its SHA-256 stored, expires after 30 days) to a page that needs no sign-in and only
  accepts/refuses.
- **E-mail grid storage.** (a) One bool per event×recipient in settings — rigid and noisy. (b) **Chosen:** a dictionary
  event → list of recipient kinds; a missing event means "defaults", so existing organizations keep today's behaviour
  with no data migration. The existing per-event switch (`DisabledEmails`) stays as the row's "On" switch and personal
  opt-outs stay per event.
- **Access windows storage.** A child table vs **a JSON list column** on the request and the work order (like working
  hours in 0.9.0). Chosen JSON: windows are always read and written with their record and never queried on their own.

## Design

### Data (one migration, `RequestsLoop`)

- `RequestType` (tenant lookup, `ICodeLookup`): Code, Name, IsActive, `DefaultTradeId?`, `DefaultPriority?`,
  `RecipientIds` (users, JSON), `AutoApprove`, `TeamId?` (auto-approval's team).
- `CancelReason` (tenant lookup, `ICodeLookup`): Code, Name, IsActive. Seeded for new organizations and, by the
  migration, for existing ones: DUP "Duplicate", NOTNEEDED "No longer needed", ERROR "Raised in error", OTHER "Other".
- `WorkRequest` gains: `RequestTypeId?`, `ExtraRecipientIds` (users, JSON), `AccessWindows` (JSON),
  `OnlyWhenStopped`, `AwaitingFeedbackSince?`, `FeedbackTokenHash?` (indexed), `FeedbackTokenExpiresAt?`.
  `WorkRequestStatus` gains `Closed = 5`.
- `WorkOrder` gains: `CancelReasonId?`, `AccessWindows` (JSON), `OnlyWhenStopped`, `OverdueNotifiedFor?` (the due
  time already e-mailed about).
- `Asset` and `PmSchedule` gain `OnlyWhenStopped`.
- `WorkRequestComment` (tenant-owned): WorkRequestId, Body (≤ 4000), UserId?, UserName, `ByRequester`, CreatedAt.
- `RequestFeedback` (tenant-owned): WorkRequestId, WorkOrderId, `Accepted`, `Rating?` (1–5), Comment (≤ 2000),
  AnsweredAt, UserId?, ByName, `ViaLink`. One row per answer (a refusal then an acceptance is two rows).
- `AccessWindow` record: Start, End (DateTimeOffset), Note (≤ 200). At most 10 per record; End after Start.
- `TenantSettings`: `CommentRequiredFor` (list of `reject|refuse|hold|reopen|cancel`, default `["reject","refuse"]`
  — reject needs a reason today, so the default keeps that), `EmailRecipients` (event → recipient kinds; empty).
  "Complete" in the required-comments group **is** the existing `RequireCompletionNotes` switch **[call]**.
- The migration marks every active work order already past its due time as notified (`OverdueNotifiedFor = DueAt`) so
  the first overdue run sends nothing old.

### Request types and recipients

- `/api/request-types`: list (any signed-in user), create/update/delete with `settings.manage`. Validation: code unique;
  trade/team exist; recipients are active users of the organization; auto-approve needs a trade or a team ("Choose
  the trade or team the work goes to."). Delete when used → "This item is in use. Mark it inactive instead."
- The request form offers active types; choosing one pre-fills the priority (web). Approving pre-fills the trade
  from the type (web).
- Extra recipients: up to 10 active users who can see the request's place (site and location, 0.9.0 access) —
  "Choose people who can see this place." The picker lists active users who can see the site
  (`GET /api/work-requests/people?siteId=`, names only, needs `requests.create`).
- "Request recipients" for a request = site recipients ∪ type recipients ∪ extra recipients. When the site and type lists
  are both empty, the people who can manage requests are used as today, plus any extra recipients.
- **Auto-approve:** a request of an auto-approve type is approved the moment it is created (web or phone), inside the
  same transaction: the work order gets the type's trade, and its team when the team can work at the request's site
  (else no team) **[call]**; decided by nobody, note "Approved automatically (type: X)". The usual e-mails go out
  (recipients hear of the new request, the requester hears it was approved).

### Accept or refuse

- When a work order raised from a request is **completed** and the request is Approved, the request starts
  "waiting for the requester" (`AwaitingFeedbackSince`). If the requester is not the request's creator-with-login, a
  new single-use token is made. The "work finished" e-mail to the requester carries Accept/Refuse: the request page
  link for a creator with a login, else `/feedback/{token}`.
- Maintenance reopening or cancelling the work order ends the wait (and voids the token). Closing it does not.
- The requester answers: the creator on the request page (`POST /api/work-requests/{id}/feedback`), others on the
  public page (`GET`/`POST /api/public/feedback/{token}`, anonymous). The rating is required to accept and optional to
  refuse **[call]**; the comment is required to refuse when "refuse" is in `CommentRequiredFor`.
- **Accept:** request → Closed; feedback row; the wait and the token end.
- **Refuse:** feedback row; the work order (Completed or Closed) → In progress with the note
  "Refused by <name>: <comment>", done as the system on the requester's behalf (no close permission needed, the reopen
  comment rule is met by that note); request stays Approved; the "refused" e-mail goes out. Completing it again asks
  again.
- A used, unknown or expired link answers 404 with "This link was already used or has expired." The public GET shows
  only: organization name, request number and title, place, work-order number, completed time and completion notes,
  and the organization's language.
- Shown: the request drawer lists answers (accepted/refused, stars, comment, who, when) and "waiting for the
  requester"; the work order shows the latest answer; the request list shows Closed.
- **Satisfaction report** (`requester-satisfaction`, Work): answers in the period, grouped by month (default), request
  type, trade, team or site; columns answers, accepted, refused, average rating; KPIs answers, average rating, accepted
  %. Rows come through the user's visible requests (0.9.0 access).

### Comments on requests

- `GET`/`POST /api/work-requests/{id}/comments`, for whoever can see the request (its creator, or `requests.view` /
  `requests.manage`). `ByRequester` = the author is the request's creator.
- The work order detail includes the request's comments read-only.
- E-mail "new comment" goes to **the other side**: a requester's comment goes to the maintenance-side recipients
  turned on in the grid; a maintenance comment goes to the requester (if on). The author never hears.

### Required comments and cancellation reasons

- Enforced in one place for each action:
  - reject (request reject endpoint): reason required when `reject` is on;
  - refuse (feedback): comment required when `refuse` is on;
  - hold (`ChangeStatusAsync` → On hold): note required when `hold` is on — "Say why the work is on hold.";
  - reopen (Completed/Closed/Cancelled → Open/In progress): note required when `reopen` is on — "Say why the work is
    reopened.";
  - cancel (→ Cancelled): note required when `cancel` is on — "Say why the work is cancelled.";
  - complete: the existing completion-notes rule.
  Bulk changes and the phone go through the same code. The phone bundle's `CompletionRules` gains `HoldNote`.
- Cancelling needs a reason from the list whenever the list has an active reason — "Choose why it is cancelled."
  `CancelReasonId` is stored on the work order and cleared when it is reopened; the note goes on the status change.
- `/api/cancel-reasons` via the shared lookup endpoints (`settings.manage`).
- Work-order list: cancel reason column, `cancelReasonId` filter. Detail shows the reason.
- **Cancelled work report** (`cancelled-work`, Work): work orders cancelled in the period (by the status change's
  time), grouped by reason (default), month, trade or site.

### Access windows

- Request input and work-order input take `accessWindows`; on approval (manual or automatic) the request's windows
  are copied to the work order. For a work-order update, a missing list means unchanged.
- `AccessWindows.Covers(windows, day, zone)` — true when there are no windows or one overlaps that local day.
  "Outside every window" = has windows and the planned day is not covered.
- Work-order detail: windows and an "outside every window" warning for the planned start. Planning board jobs carry
  windows, `OutsideWindows` (for the card's day) and the card shows a warning; dropping a card on a day outside every
  window shows a warning (the move still happens). Phone job lists the windows.

### Only while stopped

- Asset form/detail: "Maintenance only while stopped". Requests copy it from their asset. Work orders: create takes the
  input when given, else the request's or asset's flag; editable on the form. PM schedules: input when given, else the
  asset's; generated work orders copy the schedule's.
- Badge on work-order list and detail, planning board card, phone job and PM list. Filters: work-order list
  (`onlyWhenStopped`) and planning board.

### Who gets which e-mail

- Recipient kinds: `requester` (the request's contact), `requestRecipients` (above), `technicians` (in-house assignees
  and members of the given team), `contractors` (assignees and team members marked contractor), `issuer` (who created
  the work order; for request-only events, who created the request).
- Events, the kinds that apply, and defaults (bold = on by default):

  | Event | Requester | Request recipients | Technicians | Contractors | Issuer |
  |---|---|---|---|---|---|
  | request.submitted (new request) | yes | **yes** | – | – | – |
  | request.approved | **yes** | yes | yes | yes | – |
  | request.rejected | **yes** | yes | – | – | – |
  | request.comment (new) | **yes** | **yes** | yes | yes | – |
  | workorder.assigned | – | – | **yes** | **yes** | – |
  | pm.assigned (PM work order created) | – | – | **yes** | **yes** | – |
  | workorder.completed | – | yes | – | – | **yes** |
  | request.completed (work finished: accept or refuse) | **yes** | – | – | – | – |
  | request.refused (new) | – | **yes** | **yes** | **yes** | **yes** |
  | workorder.cancelled (new) | **yes** | yes | **yes** | **yes** | yes |
  | workorder.overdue (new) | – | yes | **yes** | **yes** | **yes** |
  | parts.received | – | – | **yes** | **yes** | – |

  Purchasing e-mails keep their fixed recipients and their on/off switches below the grid.
- Existing rules still apply to every row: e-mail on for the organization, the row switched on, personal opt-outs,
  never the person who acted, one e-mail per address, only people who can see the record (0.9.0).
- `/api/email/status` returns, per event, the applicable kinds and the defaults, for the grid.
- **Overdue:** a background service (every 30 minutes, `Email:OverdueIntervalMinutes`) looks at active work orders past
  their due time whose `OverdueNotifiedFor` differs from their due time, for organizations with e-mail on. Due more
  than 48 hours ago → marked without sending; otherwise queued and marked. At most 200 per organization per run.
  Changing the due time makes a later overdue notice possible again.

## Constraints

- .NET 10 / EF Core 10 / SQL Server; React 19 / Mantine 8; everything translated to Arabic (server `ar.json`, web
  `en`/`ar`).
- 0.9.0 access: every user-facing read is scoped; e-mails filtered by access; organization-wide decisions use
  `AllSites()`.
- Public endpoints: only health, auth and the two feedback endpoints are anonymous.

## Left to the builder

Screen layouts, component structure, exact wording beyond the messages above, query shapes, test organisation.
