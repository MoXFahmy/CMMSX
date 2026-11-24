# Security and the activity log (0.25.0)

An organization can set its own password rules, allow each person only one signed-in device at a time, anonymise people who
have left, and see every change and sign-in in one **activity log**. **Settings › Storage** shows the space files take.

Nothing changes for an organization until an administrator sets these: the default password rules are the ones CMX has always
had, a person can be signed in on several devices, and the activity log keeps everything.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage settings* | **Settings › Security** (password rules, one session per person, how long the activity log keeps changes), **Settings › Activity log** and **Settings › Storage**. |
| *Manage users* | Anonymise an inactive employee or an inactive sign-in. |

There are no new permissions. An administrator limited to some sites sees, in the activity log, the changes to records at their
sites and the organization-wide changes (settings, users, parts…), but not the changes to another site's records.

## Password rules

**Settings › Security › Passwords**:

- **Minimum length**: 8 to 64 characters (8 by default). It applies to new users, password resets, "Change password" and renewals.
  At sign-in, the password typed is checked against the current minimum: a password that is now too short must be changed then
  ("Your organization's password rules changed. Choose a new password."). Phones signed in for 30 days meet it at their next sign-in.
- **Passwords expire after**: 0 (never, the default) or 30 to 730 days. An expired password must be renewed at sign-in ("Your
  password has expired. Choose a new one."). Expiry also reaches devices already signed in: their next request is refused, the web
  goes to the sign-in page with that notice and back to the page afterwards, and the phone shows the same while keeping its waiting
  changes, sending them once the person is back.
- **Recent passwords that can't be reused**: 0 to 24. The current password can never be chosen again, even at 0 ("You used this
  password recently. Choose another one.").

CMX's own strength rules (upper and lower case and a digit) still apply.

## One session per person

**Settings › Security › Sessions › One session per person** (off by default). With it on, signing in while still signed in on
another device asks first: "You're signed in on Chrome on Windows (last used 5 minutes ago). Sign in here and end that session?"

- **Sign in here** ends the other device's session; its next request is signed out. A phone signed out this way keeps its
  waiting changes and sends them after its owner signs in again.
- **Cancel** ends nothing.

With the setting off, nothing asks. Administrators can still sign a device out from the user's page, as before.

## Anonymising a person who has left

For someone who has left, their name can be removed from CMX while their history, hours and costs stay.

- **Where**: in **People**, open an **inactive** employee and choose **Anonymise**; or in **Settings › Users**, the anonymise
  button on an **inactive** sign-in (for a sign-in with no employee record, such as the administrator created at registration).
- **Refused** for an active employee ("Make the person inactive first."), for an employee whose sign-in is still active ("Make
  their sign-in inactive first.") and for someone already anonymised. Anonymising never closes a working account by itself.
- **The confirmation** says what will change ("Their name will be replaced by "Former employee E-012" in 37 records, their e-mail,
  phone and notes cleared, and their sign-in closed. This can't be undone.") and asks you to type the employee code (for a sign-in
  with no employee record, the six characters in its placeholder).

What happens:

- **The employee**: the name becomes "Former employee ‹code›" ("موظف سابق ‹code›" in an Arabic organization); e-mail, phone, job
  title and notes are cleared; hourly rates stay (cost history needs them). The list shows them as **Anonymised**.
- **The sign-in**: closed for good (inactive, every device signed out, push notifications stopped). Its e-mail becomes
  `anonymised-‹id›@invalid`, so the old address can be used for someone new. Its name becomes the same placeholder ("Former user
  ‹6 characters›" for a sign-in with no employee record). It can't be edited or given a password again.
- **Copies of the name**: CMX copies a person's name onto what they did: comments, status changes, checklist steps, readings,
  stock movements and counts, approvals and signatures, requested-by fields and more. Where a person's id was kept with the name,
  the copy is replaced. Where only the name was typed (requested by, signed by, read by, counted by, contract amendments…), it is
  replaced where it equals the person's name exactly (ignoring case and surrounding spaces). A request whose requester e-mail is
  theirs loses that e-mail and phone.
- **Someone else with the same name**: the confirmation lists them, and names kept only as text are then left unchanged (they
  might be the other person's). Copies linked to this person are still replaced.
- **The activity log**: their name is replaced where they made a change; in the history of their own employee and user records,
  every personal value (name, e-mail, user name, phone, job title, notes) shows as ••••; elsewhere, any value equal to their name or
  e-mail is replaced. The entry recording the anonymisation says only "Anonymised".
- **Not changed**: names typed inside descriptions, comments and notes; sent e-mails.

## The activity log

**Settings › Activity log** lists every change and security event, newest first.

- **Every change**: records and their lines (a work order's labour, parts, costs, checklist steps and comments; purchase order
  lines, approval steps and signatures; contract periods and amendments; stock levels and count lines; files), users and roles,
  organization settings (each changed setting with its old and new value), and security settings.
- **Security events**: signed in, sign-in failed (for a known user, never with the password typed), locked out, signed out, device
  signed out (and by whom), password changed, password renewed, other sessions ended by one session per person, anonymised.
- **Not in the log**: the stock ledger, unit movements, meter readings, device sessions, password history, e-mails sent and other
  system rows; they have their own screens.
- **Source**: **Web**, **Phone** (the technician view), **Import** (an Excel import) or **Automatic** (PM generation, e-mails and
  other work done without a signed-in person).
- **One action, one change number**: everything one save changed shares it. Expand a row and choose **Show everything this action
  changed** (for example a work order with its checklist steps).
- **Changes made in bulk** are logged too: deleting a work order's lines logs each line; a merge or rename that updates many
  records writes one summary on the record that changed ("Brand merged into Grundfos: 14 assets").
- **Masking**: password hashes, security stamps, tokens and other secrets never show: their field is listed with •••• as the value.
- **Filters**: dates, person, source, record type, action and a search (record number or name, person, changed values). Export to
  Excel or CSV.
- **History tab**: each record's History tab shows the same entries, its lines' entries included, with the source when it wasn't
  the web.

**Keeping changes**: **Settings › Security › Activity log › Keep changes for** 12 to 120 months, or empty to keep everything (the
default). Each night, older changes are deleted; they also leave each record's History tab. Newer changes are never touched, and
other organizations' settings don't affect yours.

The settings pages show who last changed a security setting and when.

## Storage

**Settings › Storage** shows:

- the space used and the number of files;
- the space by kind: images, videos, documents, signatures and other;
- the space by record type: work orders, assets, requests, parts, purchasing and so on;
- **Deleted records**: files kept with records that were deleted;
- the largest files, 100 at a time, with a search by file name, record and uploaded by; each opens its record.

CMX has no storage limit.

## For administrators upgrading

- Two database changes run on start: the activity log gains its source, change number and parent columns (older entries become
  **Web**, each its own change), and users gain the date their password was last set (set to the upgrade time, so expiry counts
  from then) and an anonymised date.
- Existing passwords keep working: only an organization that raises the minimum length or turns expiry on asks people to change
  theirs.
