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
| Use parts on a job, give them back, look up stock, count a store, receive a delivery | Yes (see *Parts and stock*) |
| Mark the asset stopped or back running, add a PM checklist, attach a video or document | Yes |
| Log hours for the crew, cancel the job, create a work order | Yes, with the permission (see below) |

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

## Parts and stock

The phone keeps a copy of the parts and stock of the stores at your sites, refreshed with your jobs. **Stock** (top of
*My jobs*) searches it by part number, name or barcode; scanning a part's QR label opens the part.

- **On a job, *Parts*:** *Used* on a planned part, or *Add a part*. The part is in your hand, so it is issued straight
  away, even if it takes parts reserved for another job (the answer names that job). What the store's stock can't
  cover is added to the job's planned parts: it shows as short on the store keeper's list (*Inventory › Short for
  work*) and the answer says "Only … in the store; the rest is on the store keeper's list." Stock never goes below
  zero unless the part allows it, as in the office. *Give back* returns parts to the store (never more than were
  issued to the job). A shelf-life part is added to the job's parts instead: the store keeper issues it with its batch.
- **Without permission to issue stock** (*Inventory: transact*), *Used* asks for the parts instead: they are added to
  the job's planned parts and the store keeper issues them.
- **A part's page:** stock per store and bin, what is reserved, and *Issue to a job* (store keepers).
- **Stock counts:** the office opens a count for a store (*Inventory › Stock counts › New stock count*). It appears on
  the phone under *Open stock counts*; scan or search each part and enter the quantity counted, offline too. Several
  people can count the same store; the latest count of a part wins. Back in the office, the count shows what is in
  stock, what was counted and the difference; *Post* turns the differences into stock adjustments, *Cancel count*
  drops it with nothing changed. Stock received or issued after a part was counted is kept: posting sets the part to
  what was counted plus what moved since (the drawer shows that figure under the count). Shelf-life parts are counted
  per batch in the office, not on the phone.
- **Receive a delivery** (people who may receive purchase orders): pick the order and the line, enter the quantity,
  never more than is still to come.

## On the job

- **The asset's stop:** *Asset stopped* and *Back running*, with the time it happened (not in the future). If the
  asset is already down from another job, yours joins that stop with a note.
- **Add a checklist:** copies a PM checklist's steps into the job.
- **Video or document:** MP4, MOV, WebM, PDF, Word, Excel or text, up to 50 MB each. They wait on the phone like
  photos and are sent when there is a signal. One that doesn't get through on a slow connection after 3 tries is set
  aside on the Sync page (*Retry* on Wi-Fi, or *Discard*), so the changes after it still go.
- **Hours for the crew:** with *Log hours for others* (Maintenance Managers have it), *Log hours* asks whose hours;
  pick anyone on the job's crew.
- **Cancel the job:** with permission to close work orders; pick the reason when the organization uses cancel reasons.
- **Smart checklists (0.13.0):** each step asks for what it needs — OK / Not OK / N/A, a number (with its level shown as you type),
  a meter reading (pick the meter if the step doesn't name one) or text — all offline. Safety steps come first: the other steps and
  *Start* wait until they are answered. *All OK* answers the remaining plain steps; the camera button on a step takes a photo for that
  step (needed before completing when the step asks for one). Follow-up work for a Not OK answer or an alert/alarm reading is raised
  when the change reaches the office, and shows on the step after the next refresh.
- **Group jobs (0.12.0):** a job that belongs to a work group shows *Group GRP-00012* and the group's comments
  (read-only; write them on the web). Its due date comes from the group.
- **Work template documents (0.12.0):** a job made from a work template lists the template's manuals and procedures
  under its files; they open when the phone is online and are not kept for offline use.
- **New work order** (under *My jobs*, and at the bottom of a job), for people who may create work orders: what needs doing, priority, and the
  asset or location. It is assigned to you and works offline.

## Fingerprint unlock and notifications (Android app 0.11.0)

- **Unlock with fingerprint or face:** switch it on under **Sync**. The app then asks for it when it opens and after 5
  minutes away. It doesn't replace the sign-in: you stay signed in for 30 days as before.
- **Notifications:** the app asks once for permission. You then get a notification when a job is assigned to you or
  your request is approved; tapping it opens the job. This needs the 0.11.0 app and the server set up for push (see
  *Push notifications* in docs/DEPLOY_AWS.md).

Both need the Android app from 0.11.0 on; older apps and the browser keep working without them.

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
6. From 0.11.0, offline on a job: mark the asset stopped and back running, use a part (more than the store has, to see
   the "store keeper's list" answer), give one back, attach a short video. In the office: the part is short under
   *Inventory › Short for work* and the stop is on the asset.
7. Open a stock count in the office, count two parts on the phone, post the count and check the adjustments.
8. Switch on *Unlock with fingerprint or face* (Sync page), close the app, open it: it asks for the fingerprint. With
   push set up on the server, assign a job to the phone's person in the office: the notification opens the job.
