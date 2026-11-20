# Technician app

The technician view (`/tech`, and what the Android app opens) is for people who do the work: it shows their open jobs
and keeps working with no signal. Office users keep using the full app.

## Who can use it

An account linked to an employee (People › edit the person › *Linked login*) with permission to view work orders. *My jobs*
shows the open work orders that employee is assigned to (up to 300, earliest due first). Accounts with no employee see
"Your account isn't linked to an employee"; *Technician view* appears in the user menu only for linked accounts.

## Getting started on a phone

1. Install the Android app (or open `/tech` in the phone's browser).
2. Sign in with *Remember me* ticked while you have a connection. The phone then keeps a copy of your jobs, their
   checklists, planned parts, recent comments, and the assets, locations and meters of the sites you work at (People ›
   the person's sites; all sites when none are set).
3. From then on the app opens from that copy, with or without signal.

## What works offline

| You can | Offline |
|---|---|
| See my jobs, steps, parts, notes for the technician | Yes (from the last copy) |
| Tick steps, enter readings and remarks | Yes |
| Log hours, add comments, take photos or pick them from the gallery | Yes (photos are shrunk to 1600 px) |
| Start, put on hold, complete with signatures | Yes |
| Scan or type an asset/location number; meter readings; raise a request | Yes |
| Issue parts from the store | No: *Issue parts* opens the full app |

Changes wait on the phone (also when the app is closed) and are sent in order as soon as it is online again: the app
tries every 30 seconds, at once when the signal returns, and when you tap *Refresh now* on the **Sync** page. The copy of
your jobs is refreshed every 15 minutes while online.

**When the office changed the same thing.** If someone else changed a checklist step after you did offline, your change
is held on the Sync page with "Changed by …". *Apply mine anyway* sends yours; *Discard* drops it. Anything the server
refuses (for example a job that was cancelled or closed in the meantime) shows there with the reason, with *Retry* and
*Discard*. Completing a job someone already completed adds your notes as a comment instead; a *Start* or *Hold* sent
after the job was completed doesn't reopen it. If the server itself fails on a change 5 times, that change is set aside
the same way so the rest can go.

**Times.** Work done offline is recorded at the time you did it (up to 30 days back), not when it was sent: steps,
readings, comments, hours, the start and the completion of the job.

## Safety

- Each change is sent once, even if the connection drops halfway and the phone tries again.
- Changes belong to the person who made them: if someone else signs in on the same phone, the first person's changes
  wait until that person signs in again, and the server refuses to apply them under anyone else's name.
- **Signing out** (the door icon at the top of the technician view, or *Log out* in the full app) removes your jobs from
  the phone; changes not yet sent stay on it and are sent when you sign in there again. Signing out needs a connection.
- *Signed-in devices* (user menu; administrators: Settings › Users) lists phones and browsers with *Sign out*. A
  signed-out phone stops working within a minute; its waiting changes stay on it.
- A phone that has had no contact with the server for 30 days locks until you sign in again.

## QR labels

*Print labels* on the asset list and the parts list (select rows first; selecting needs permission to edit assets or
parts), and on a location with the QR button (just that location, or it and everything below it). Each page is an A4
sheet of 3 × 8 labels of 70 × 37 mm, edge to edge (standard label stock), at most 500 labels at a time. In the print
dialog choose 100 % ("actual size") and margins *None*.

Each QR code holds a link to the record (`<app address>/q/asset/<id>`, and so on). Scanned in the technician view it
opens the record from the phone's copy; with any other phone camera it opens the page after signing in.

**Before printing labels in bulk, give the app its own address** (a custom domain). The links use the address the app
has when the labels are printed (the `App__PublicUrl` setting; on AWS this is the CloudFront address until a custom
domain is set). If that address ever changes, labels printed before then stop opening the page (the technician view
still recognises them).

## Upgrading from 0.3.1

Phones that signed in before 0.4.0 keep their old sign-in, which lasts 12 hours without use. Sign out and in again once
(with *Remember me*) to get the 30-day sign-in that lets the phone stay offline for days.

## Checking it on a phone

1. Install the APK, sign in with *Remember me*, open a job.
2. Turn on airplane mode. Start the job, tick a step, add a photo (camera and gallery), log hours, complete the job
   with a signature.
3. Close the app, open it again: the job and the waiting changes are still there.
4. Turn airplane mode off: within a few seconds the Sync page is empty and the work order in the full app shows the
   changes, with the signature on the job card.
5. Scan a printed label with the app's *Scan* tab.
