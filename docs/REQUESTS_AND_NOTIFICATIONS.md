# Requests that close the loop

From 0.10.0 a request goes to the right people, the requester hears what happens and says whether the work is done, and
an administrator decides which actions need a comment and who gets which e-mail. Nothing changes for existing
organizations until you set something, except that cancelling a work order now asks for a reason (four reasons are
already in the list).

## Request types

Settings › Request types. Each type (for example "Air conditioning", "Plumbing", "IT") has:

- **Default trade** and **default priority**. The priority is filled in when someone picks the type. The trade is filled
  in when the request is approved.
- **Told about new requests**: people added to the site's request recipients for requests of this type.
- **Approve automatically**: requests of this type become work orders at once, for the default trade, and for the team
  you choose when that team can work at the request's site. Requests raised on the phone are approved the same way. The
  people told about new requests still hear about it, and the requester hears it was approved.

A type in use can't be deleted. Mark it inactive instead.

## Who is told about a new request

The site's request recipients, plus the type's, plus anyone the requester added under **Keep informed** (up to 10 people
who can see the place). When neither the site nor the type names anyone, the people who can manage requests are told, as
before.

## The requester accepts or refuses the work

When a work order raised from a request is completed, the requester is asked to accept or refuse it:

- If the requester raised the request themselves with their CMX login, they answer on the request (Work requests ›
  open it). The e-mail links there.
- Anyone else gets a link in the e-mail. The page opens without signing in, shows only that request, and works once. It
  stops working after 30 days, after it is used, or when maintenance reopens the work.

**Accept** needs a rating (1 to 5 stars) and closes the request (the work order stays completed until maintenance closes
it). **Refuse** reopens the work order with the reason ("Refused by …: …") and the team is told. When it is completed again,
the requester is asked again.

The answers show on the request and on the work order. **Reports › Requester satisfaction** counts the answers, accepted
and refused, and the average rating, by month, request type, trade, team or site.

## Comments on requests

Each request has a comment thread between the requester and maintenance. A requester's comment goes by e-mail to the
request's recipients (and, if you switch them on, the technicians). A maintenance comment goes to the requester. A
requester without a login gets the comment by e-mail and can reply to it by e-mail (the reply goes to whoever wrote it).

The work order shows the request's comments, read-only.

## Required comments

Settings › Organization › Required comments. Choose which actions need a comment:

| Action | Where the comment goes |
|---|---|
| Rejecting a request | the decision note (on by default, as before) |
| Refusing finished work | the requester's answer (on by default) |
| Putting work on hold | the status change note |
| Reopening work | the status change note |
| Cancelling work | the status change note |
| Completing work | the "work done" notes (the existing setting) |

The rules hold on the web, for many work orders at once, and on the phone.

## Cancellation reasons

Settings › Cancel reasons is an editable list. Cancelling a work order asks for a reason from it (when the list has an
active reason) and a note. The reason shows on the work order, in the work order list (column and filter) and in
**Reports › Cancelled work**. Reopening clears it.

## Access windows

On a request or a work order: one or more times when the work may be done (from, to, note), for example when a room is
free. A request's times go to its work order when it is approved. The work order, the phone job and the planning board
show them. The work order and the board warn when the work is planned on a day outside every time, and the board warns
when you drop a card on such a day (the move still happens).

## Only while stopped

On an asset: **Maintenance only while stopped**. It is copied to the asset's new requests, work orders and PM schedules
(and the work orders they generate), where it can be changed. Such work has a badge, and the work order list and the
planning board can show only that work, so planners can book a shutdown.

## Who gets which e-mail

Settings › Notifications is a grid. Rows are e-mails, columns are who gets them:

- **Requester**: the name and e-mail on the request.
- **Request recipients**: the site's, the type's and the request's own.
- **Technicians**: the in-house people on the work and the members of its team.
- **Contractors**: the same, for people marked as contractors.
- **Issuer**: whoever created the work order (for e-mails about a request only, whoever raised it).

Each row has its own on/off switch, and each person can still switch e-mails off for themselves (My e-mails). Out of the
box, every e-mail goes where it went before 0.10.0. The new ones go to:

- **Comments on requests**: the requester and the request recipients (the author never).
- **Finished work refused**: the request recipients, the technicians, the contractors and the issuer.
- **Work cancelled**: the requester, the technicians and the contractors.
- **Work overdue**: once, when a work order passes its due time, to the technicians, the contractors and the issuer.
  Work that was already more than two days late when e-mail was switched on is not e-mailed.

People only get e-mails about records they can see (0.9.0 access).
