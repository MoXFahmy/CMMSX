# 0.25.0 — Security and the activity log

Roadmap release 0.25.0 ("security, sign-on, API and admin") was split into four releases (agreed 2026-11-24):

- **0.25.0 Security and the activity log (this document):** password rules, one session per person, anonymising someone who has left, an activity log for administrators, a storage overview.
- 0.26.0 API keys and background imports.
- 0.27.0 Company sign-in (single sign-on).
- 0.28.0 Bilingual data.

## Why

Larger customers ask about password rules, personal data, and who changed what. CMX already keeps a change history on each record, lists each person's signed-in devices, requires 8-character passwords and locks an account after 8 failed attempts. What it lacks:

- password rules an organization can set;
- a way to stop one account being used on several devices at once;
- a way to remove a departed person's name;
- one place where an administrator can see every change, sign-in and settings change, with where it came from;
- a view of the space files take.

DimoMaint has password management, one session per named user (R57), anonymisation, a global audit trail with retention and masking, and files administration.

## Goals

1. **Password rules per organization:** minimum length, expiry, and "not one of the last N passwords", applied wherever a password is set.
2. **One session per person (optional):** signing in on another device asks whether to end the other session.
3. **Anonymise a person who has left:** their name is replaced everywhere it was copied, their contact details are cleared, and the history stays.
4. **Activity log:** one searchable log of every change, with:
   - who, when, the old and new values;
   - where it came from: web, phone, import or an automatic job;
   - a change number shared by the changes one action saved;
   - filters and export, retention, masking of sensitive values;
   - who last changed each settings section.
5. **Storage overview:** space used by files, by type and by record, with the largest files and a search.

## Non-goals

- API keys and service accounts: 0.26.0. Its "API" source on the log comes then.
- Single sign-on: 0.27.0.
- Two-factor authentication, IP allow-lists, password complexity classes beyond length: not planned.
- A storage limit per plan: CMX has no plans or quotas.
- Logging reads (who viewed what): only changes and security events are logged.
- The stock ledger, unit movements and meter readings are already immutable, dated, named records and stay out of the log. Their own pages show them.

## Anti-goals

- **The activity log must not be a second copy of history that can drift from the record's History tab.** One store, two views.
- **No secret ever reaches the log:** password hashes, security stamps, tokens and push tokens.
- **Anonymising must not leave the old name findable** in records, comments, signatures, readings or the log's old/new values.
- **One-session mode must never sign someone out without asking**, and never lose a phone's unsent changes silently.
- **Nothing changes for an organization that doesn't touch the new settings:**
  - passwords stay "at least 8";
  - sessions stay unlimited;
  - the log keeps everything.

## Approaches

**Activity log**

- **A. (picked)** Build the log on the existing change entries (`AuditEntry`, written in `CmmsDbContext.SaveChangesAsync` for every `TenantEntity`). Add the source and change number, and add entries for what isn't recorded today. The History tab and the log read the same rows.
- **B.** A separate log table written alongside. Rejected: it duplicates every record's history and can drift from it.

**One session per person**

- **A. (picked)** Ask on sign-in: "You're signed in on ‹device›. Sign in here and end that session?"
- **B.** Silently sign the other device out. Rejected: it loses unsent work and can happen by accident.

**Anonymising**

- **A. (picked)** Replace the name wherever it was copied: by person id where the copy carries one, and by exact name where only the name was stored. Clear contact details and close the account. Rewrite the old name inside past change entries.
- **B.** Delete the person. Rejected: it breaks history and costs.

## The design

### 1. Password rules

- **Settings › Security** (Manage settings), organization-wide:
  - **Minimum length:** 8–64, default 8.
  - **Expire after (days):** 0 = never (default); 30–730 otherwise.
  - **Can't reuse the last N passwords:** 0 = no check (default), up to 24.
- **Where they apply:** every place a password is set:
  - registering (the organization's first administrator: the default rules);
  - an administrator creating or editing a user with a password;
  - "Change password";
  - the new "choose a new password" step after expiry.
  The messages say what's wrong: "Use at least 12 characters." and "You used this password recently. Choose another one."
- **History:** the last 24 password hashes per user are kept in a new table (user, hash, when). They are never shown, logged or returned. A new password is checked against the last N.
- **Expiry:**
  - Each user keeps when their password was last set (`PasswordChangedAt`, new). Existing users get the migration date, so nobody's password expires on release day.
  - A sign-in with an expired password (when expiry is on) doesn't sign in. It answers "Your password has expired. Choose a new one.", and the sign-in page shows new-password fields.
  - The new password is checked against the rules and history and then saved. The person is signed in.
  - This works on the web and the phone sign-in.
  - Passwords set by an administrator count as set at that moment.
- **Lockout:** unchanged (8 attempts, 10 minutes).

### 2. One session per person

- **Settings › Security:** "One session per person" (off by default).
- **When on, and the person has another active device session** (not revoked, not expired):
  - the sign-in answers 409 with the other device's name and when it was last used;
  - the sign-in page asks "You're signed in on ‹Chrome on Windows› (last used ‹time›). Sign in here and end that session?";
  - "Sign in here" resends with `endOtherSessions: true`. That revokes the other sessions, as "Sign out this device" does today, and then signs in.
- **The phone:** its sign-in page asks the same. A phone whose session was ended keeps its queued changes on the device. On its next contact it shows "You were signed out because you signed in on another device"; after signing in again, it sends them.
- **Turning the setting on** doesn't sign anyone out; it applies from each person's next sign-in.
- **Ending sessions:** administrators can still end any device's session from the user's page, as today.

### 3. Anonymise a person who has left

- **Who and where:** on an **inactive** employee's page, "Anonymise" (Manage users).
- **Confirmation:**
  - It shows what will change: "Their name will be replaced by ‹Former employee E-012› in N records, their e-mail, phone and notes cleared, and their sign-in closed. This can't be undone."
  - It asks the person to type the employee code to confirm.
- **What changes:**
  - **The employee:**
    - full name becomes "Former employee ‹code›" (Arabic: "موظف سابق ‹code›");
    - e-mail, phone, job title and notes are cleared;
    - hourly rates are kept (costs history needs them);
    - they're marked anonymised (`AnonymisedAt`, new), and the page shows a badge.
  - **Their user account, if linked:**
    - it can't sign in again: inactive, sessions revoked;
    - e-mail and user name are replaced by a unique placeholder (`anonymised-‹id›@invalid`), so the e-mail can be used for someone new;
    - full name and phone are replaced or cleared, and push tokens are removed.
  - **Copies of the name:** every copied name across CMX is replaced.
    - **Where a person id is stored with the name** (created-by fields, user ids on comments, change entries, sessions): replaced by that id.
    - **Where only the name text was stored** (requested by, signed by, read by, counted by, decided by and similar): replaced where the text equals the person's full name or user full name exactly, ignoring case and surrounding spaces, within the organization.
    The builder inventories every such column. The inventory is part of the plan and is checked by a test that fails when a new name column appears without being listed.
  - **The change entries:**
    - the person's name is replaced in the user name column;
    - inside the old/new values, any value exactly equal to the name or e-mail is replaced;
    - the entry recording the anonymisation itself shows "Anonymised", not the old values.
- **Not touched:**
  - work request "requester name/e-mail" fields typed by outside requesters, unless they match exactly;
  - free text inside descriptions and comments' bodies. The confirmation says "Names typed inside descriptions and comments aren't changed."
- **Refused** for an active employee ("Make the person inactive first.") and for one already anonymised.

### 4. Activity log

- **Every change entry gains:**
  - **Source:** Web, Phone, Import, Job. API comes in 0.26.0. Phone means the technician app (`/api/tech/...`); Import means `/api/import/run`; Job means work done without a signed-in person (PM generation, e-mail sending, notifiers, reconcile routines).
  - **Change number:** one per web request, phone change or job run. Everything one action saved shares it.
- **Newly recorded:**
  - **Users and roles:** created, updated (name, e-mail, active, roles, sites, branches, limits), deleted.
  - **Organization settings and site settings:** each changed key with old and new value.
  - **Security events:**
    - signed in, sign-in failed (known user only), locked out;
    - signed out, device signed out (by whom), password changed, password expired and renewed;
    - other sessions ended by one-session mode;
    - anonymised.
- **Masking:**
  - Values of sensitive fields are written as "••••". These are password hashes, security and concurrency stamps, tokens, push tokens and any field marked sensitive. The field name shows; the value doesn't.
  - The list is in one place (core) and covered by a test.
- **Settings › Activity log** (Manage settings):
  - a list, newest first, of when, who, source, record (type, its name or number, and a link), action, and a summary of changes;
  - expanding a row shows each field's old and new value;
  - **filters:** date range, person, source, record type, action, change number, and a text search over record number/name and changed fields;
  - "Same change" opens every entry with that change number;
  - **export** to CSV and Excel through the list toolkit, capped like other exports;
  - site-limited administrators see only entries for records they can see, plus their own sign-ins.
- **Retention:**
  - Settings › Security: "Keep activity for N months". Empty = keep everything (default), otherwise 12–120.
  - A nightly job deletes older entries. The setting says "Older changes also leave each record's History tab."
- **"Last changed":** each settings page section shows "Last changed by ‹name›, ‹when›" from the log.
- **The record's History tab** stays as it is; it reads the same entries and now shows the source.

### 5. Storage overview

- **Settings › Storage** (Manage settings):
  - total space and file count;
  - space by kind: images, videos, documents, signatures, other;
  - space by record type: work orders, assets, requests, parts, purchasing and so on;
  - the 100 largest files, with a search by file name, record and uploaded by. Each opens its record.
- **Deleted records' files:** if CMX keeps them, they're counted under "Deleted records".
- **No storage limit:** none is shown, because CMX has none.

## Permissions

| Permission | Can |
| --- | --- |
| *Manage settings* | Settings › Security (password rules, one session per person, retention), the activity log and the storage overview. |
| *Manage users* | Anonymise an inactive employee. |

No new permissions: existing organizations' administrator roles wouldn't pick up a new one automatically.

## Behaviour to verify

- **Rules:** a password shorter than the minimum, or one of the last N, is refused everywhere a password is set.
- **Expiry:** an expired password signs in only after a new one is chosen.
- **Untouched organizations:** nothing changes for them.
- **One-session mode:**
  - a second sign-in asks; confirming ends the first device's session, and its next request is signed out;
  - with the setting off, nothing asks.
- **Anonymising:**
  - the name is gone from every listed name column and from change entries;
  - costs and history counts are unchanged;
  - the e-mail can be reused for a new user;
  - refused for active people.
- **Log entries:**
  - every entry has a source and a change number;
  - phone changes say Phone, imports say Import, PM generation says Job;
  - users, roles, settings and sign-ins are recorded;
  - sensitive values are masked.
- **Log page and retention:** filters, "same change", export, and site limits on the log; retention deletes only older entries.
- **Storage:** totals match the files.
- Every new label and message in English and Arabic.

## My calls (from the builder check)

1. **"Every change" is every change.** Today only `TenantEntity` rows get change entries. The log also records the tenant-owned rows that have no history yet:
   - work order labour, parts, costs, contractor lines, checklist steps and comments;
   - request comments;
   - attachments (uploads, deletes, signatures);
   - purchase order approval steps and signatures;
   - contract periods, amendments and covered assets;
   - stock levels and count lines.

   How these entries work:
   - Each entry names its **parent record** (type and id), so the log shows "WO-00012 › Labour" and links to the work order. The record's History tab includes its children's entries.
   - **Out of the log:** the stock ledger, unit movements, meter readings, sessions, password history, the e-mail outbox and other system rows.
   - **An inventory test** lists every tenant-owned entity as either recorded or left out, and fails when a new one appears in neither list.
   - **Bulk operations:** about 30 of them bypass the save pipeline (`ExecuteUpdate`/`ExecuteDelete`). Deleting child lines (labour, tasks, costs) becomes tracked deletes, so each line is logged. Cascades (unit renames, brand merges, cost-centre reassignment) write one summary entry on the record that was changed, with how many rows followed ("Brand merged into ‹name›: 14 assets").
2. **Anonymising a person with a sign-in but no employee record** (such as the administrator created at registration):
   - From the Users page, for an **inactive** user, "Anonymise" (Manage users) with the same confirmation.
   - Their placeholder name is "Former user ‹first 6 characters of the user id›" (Arabic "مستخدم سابق ‹…›").
   - An employee with a linked sign-in can be anonymised only when both the employee and the sign-in are inactive ("Make their sign-in inactive first."). Anonymising never closes a working account by itself.
3. **Same name as someone else:**
   - When another current or former person (employee or user) has the same name, ignoring case and spaces, the confirmation says so.
   - Names stored **only as text** (requested by, signed by, read by, counted by, decided by…) are then **left unchanged**, and the confirmation says which kinds. Copies linked by id are still replaced.
4. **The person's own history:** in change entries of the person's own employee and user records, every personal value is masked (name, e-mail, user name, phone, job title, notes). Elsewhere in the log, values exactly equal to their name or e-mail are replaced.
5. **How far password rules reach:**
   - **Expiry applies to sessions already signed in.** When expiry is on and a person's password has expired, their next request answers 401 with the code `password-expired`. The web shows "choose a new password" and returns to the page; the phone shows the same and keeps its queued changes, sending them after.
   - **At every sign-in, the typed password is checked against the current minimum length.** A password that no longer meets it must be changed then ("Your organization's password rules changed. Choose a new password."). Phones signed in for 30 days meet it at their next sign-in.

## Left to the builder

- Exact table and column names and the migration.
- How the source and change number reach the save pipeline. The request-scoped context is the natural place: a job run gets its own.
- The settings-diff format.
- The inventory of name columns (with its test).
- The list toolkit columns of the log.
- Paging sizes.
