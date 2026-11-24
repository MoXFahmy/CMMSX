# 0.26.0 — API keys and background imports

The second of the four releases split from the roadmap's "security, sign-on, API and admin" (agreed 2026-11-24):
0.25.0 security and the activity log (live), **0.26.0 API keys and background imports (this document)**, 0.27.0 company
sign-in, 0.28.0 bilingual data.

## Why

Customers want other systems to work with CMX: an ERP reading work orders and costs or creating purchase orders, a BI tool
reading history, a sensor gateway posting meter readings or raising requests. Today CMX only accepts a signed-in browser or
phone, and its API documentation (`/api/docs`) works only in a signed-in browser. DimoMaint has API keys.

Imports are the other way data comes in, and big ones fail: a check or an import runs inside the upload request and stops at
45 seconds (the 60-second limit in front of the app), at 5,000 rows a sheet and at 10 MB. The person waits on the page with no
progress, and nothing records what was imported.

## Goals

1. **Service accounts and API keys:** a non-person account with its own roles and sites; keys that act as it, each shown once,
   optionally read-only and expiring, with "last used" and Revoke.
2. **The API works with a key** wherever the account's roles and sites allow, with a per-key rate limit, and its changes show
   in the activity log with the source **API**.
3. **API documentation** describes how to authenticate with a key and can be read by anyone signed in or holding a key.
4. **Background imports:** every check and import runs in the background with a progress bar; a history of imports; an e-mail
   when a long one finishes.
5. **Bigger imports:** 20,000 rows a sheet (50,000 a file in all) and 25 MB a file, still one all-or-nothing transaction.

## Non-goals

- Webhooks (CMX calling other systems when something changes).
- Company sign-in (single sign-on): 0.27.0.
- OAuth client credentials, signed requests, IP allow-lists for keys.
- Personal keys acting as a person; a separate permission list per key.
- Importing without the all-or-nothing transaction (sheet by sheet).
- A public, unauthenticated API description.

## Anti-goals

- **A key is a secret shown once.** CMX stores only a hash; nobody can read a key back, not even an administrator.
- **A key never does more than its account may.** Roles, site limits and branch limits apply exactly as for a person.
- **A key can't manage access.** No key can create or revoke keys, manage service accounts, change security settings or
  anonymise anyone.
- **Service accounts aren't people.** They never get e-mail or push, never appear where a person is chosen (approvers,
  recipients, assignment), and can't sign in to the app.
- **A background import never half-saves.** A restart mid-import saves nothing and says so.
- **Nothing changes for an organization that doesn't use these**, except that imports now show progress instead of waiting.

## Approaches (picked by the user, 2026-11-24)

**Who a key acts as**

- **A. (picked) Service accounts:** a non-person account (a user record marked as a service account) with its own roles and
  sites, listed under Settings › Users. Keys belong to it, so a key keeps working when people leave.
- B. Personal keys acting as the person who made them. Rejected: keys break when the person leaves, and a person's access is
  rarely what an integration should have.

**What a key can do**

- **A. (picked)** Everything its account's roles and sites allow, with an optional "read only" tick per key.
- B. Each key with its own permission list. Rejected: a second permission system to keep straight.

**Which imports run in the background**

- **A. (picked)** All of them, checks and imports alike: one way of working.
- B. Only files too big for the old way. Rejected: two ways to learn and test.

**Size and safety**

- **A. (picked)** Keep one all-or-nothing transaction, so a check still shows exactly what the import will do. Raise the limits
  to 20,000 rows a sheet and 25 MB a file.
- B. Save sheet by sheet with no row limit. Rejected: a failure halfway leaves some sheets imported.

## The design

### 1. Service accounts

- **Where:** Settings › Users (Manage users), "New service account". A service account has a name (for example "SAP
  integration"), roles, sites and branches like a person, and an active switch. It has no e-mail, password, phone or job title.
- **Shown as** "Service account" (a badge) in the users list; its row offers Edit, API keys and Delete/Deactivate, never
  password, devices or anonymise.
- **It can't sign in:** sign-in, renewing a password and password resets refuse it; it has no password.
- **Not a person anywhere:**
  - left out of every list where people are chosen (approvers, recipients, request-type people, buyers, assignees, report
    "who" filters that list users) and of e-mail and push recipients;
  - not linkable to an employee;
  - left out of password rules, one session per person and anonymising.
- **Deactivating** a service account stops all its keys at once. Reactivating brings back the keys that aren't revoked or
  expired.
- **Its changes** carry its name as the "who" everywhere (activity log, History tabs, "created by" fields).

### 2. API keys

- **Where:** Settings › API keys (Manage users). The list shows name, service account, the key's first characters
  ("cmx_4f9k…"), read only, created by and when, expiry, last used, and status (active, expired, revoked, account inactive).
- **Creating a key:** choose the service account, a name ("Power BI nightly"), read only (on or off), and an optional expiry
  date. The key is shown once, with a copy button and "You won't see this key again." It looks like `cmx_` followed by 40
  random letters and digits.
- **Revoke** ends a key at once (confirmed). A revoked or expired key stays in the list, greyed, for the record.
- **Using a key:** `Authorization: Bearer cmx_…` (or the header `X-Api-Key`). A request with a key is treated exactly like the
  service account signed in: its roles, sites and branches decide what it sees and may change.
- **Read only:** a read-only key may only read (GET); anything else answers 403 "This key is read-only."
- **Rate limit:** 120 requests a minute per key; over it, 429 with how long to wait.
- **What keys can't reach:** signing in and out, passwords and devices, the technician app's sync, API key and service
  account management, security settings and anonymising (403 "API keys can't do this.").
- **Failures:** a missing, wrong, revoked or expired key, or an inactive account or organization, answers 401 with
  "The API key isn't valid." (one message, so nothing about the key leaks).
- **Last used** is kept to the minute.
- **The activity log:** changes made with a key have the source **API** and the service account's name; creating and revoking
  keys and service accounts are logged like any other change (the key's hash never shows).

### 3. API documentation

- `/api/docs` and the API description work for anyone signed in, and for a request carrying a valid key.
- The description declares the key (bearer) scheme, so "Try it" in the docs accepts a key.
- A short guide, `docs/API.md`: creating a service account and a key, the headers, read only, the rate limit, paging and
  errors, and three examples (read open work orders, post a meter reading, create a work request).

### 4. Background imports

- **Every check and import is queued:** the upload returns at once with an import number, and Settings › Import shows its
  progress: "Checking Assets… 1,240 of 3,000 rows", then the result exactly as today (counts per sheet, problems, the problems
  workbook).
- **History:** Settings › Import lists past checks and imports (file name, check or import, who, when, how long, result:
  imported, problems found, failed), newest first, the last 100. Each opens its result and its problems workbook.
- **Leaving the page** is fine: the work goes on. Coming back shows it. When a check or import that took more than a minute
  finishes, the person who started it gets an e-mail with the result and a link.
- **One at a time on each server:** a second upload, from any organization, waits its turn ("Waiting for another import to
  finish").
- **Limits:** 20,000 rows a sheet, 50,000 rows a file in all, 25 MB a file. There is no 45-second limit; a run that goes on for more than 30 minutes stops
  and saves nothing ("This import took too long. Split it into smaller files.").
- **All or nothing, as today:** an import saves everything or nothing; a check saves nothing.
- **A server restart** during a check or import saves nothing; the import shows "The server restarted during this import.
  Nothing was saved. Try again."
- **The uploaded file** is kept 30 days for the problems workbook, then removed; the history line stays.
- **With a key:** `POST /api/import/check` and `/api/import/run` answer 202 with the import number, and
  `GET /api/import/jobs/{id}` gives its status and result. Their changes have the source **Import**.
- **Site limits:** a check or import runs with the access of the person (or service account) who started it.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage users* | Service accounts and API keys. |
| *Manage settings* | Imports and their history, as today. |

No new permissions.

## Testing

- **Keys:**
  - a key reads and writes what its account's roles allow, and is refused (403) beyond them;
  - site limits apply to a key's account;
  - a read-only key can read and is refused when it writes;
  - wrong, revoked, expired keys and an inactive account answer 401 with the same message;
  - the rate limit answers 429 after 120 requests in a minute;
  - keys can't reach the excluded areas;
  - a change made with a key has source API and the account's name in the log;
  - the key is returned once and only its hash is stored.
- **Service accounts:**
  - can't sign in;
  - are left out of people lists and e-mail;
  - deactivating one stops its keys.
- **Imports:**
  - a check and an import run in the background and report progress and the same result as before;
  - a second upload waits for the first;
  - an import that fails saves nothing;
  - a job left running by a restart ends as failed with nothing saved;
  - 20,000 rows import;
  - the e-mail goes after a long run;
  - the history lists past runs;
  - the problems workbook comes from a finished check.
- Every new label and message in English and Arabic.

## My calls (from the builder check)

1. **Server capacity.** Production runs one small server (half a CPU, 1 GB of memory), and an import keeps its rows in memory
   until it commits. So:
   - **One check or import at a time on each server**, across organizations. Another organization's upload waits its turn
     ("Waiting for another import to finish"). Imports are occasional, so waits are rare and short.
   - **A file may hold 50,000 rows in all** (still at most 20,000 a sheet). The plan measures a 20,000-row import's memory on a
     1 GB container and lowers the total if it needs to.
   - No bigger server, so no extra monthly cost.
2. **Keys can't change who has access.** Besides the areas listed above, a key can't create, edit, delete or deactivate users
   or service accounts, change roles, or set anyone's password (403 "API keys can't do this."). It may read the users and roles
   lists if its account's roles allow. A service account may hold any role, Administrator included; these blocks apply anyway.
   Creating CMX users from an HR system is left for later.
3. **The rest of CMX stays usable during a long import.** Reading never waits (the database reads without waiting for unsaved
   work). Saving must not wait either, so:
   - **Numbers** (asset numbers and any other numbering an import uses) are taken in a short step of their own at the start of
     each sheet and kept at once, not held for the whole run. If the import then fails, those numbers are skipped (a gap),
     which is shown nowhere and harms nothing.
   - **A check takes no numbers**: it shows the numbers the import would take without reserving them.
   - **Rows the import edits** stay locked until it finishes, as with any save; someone editing the same asset at that moment
     waits. Brand and model clean-up runs at the end, just before the import saves.
   - **The 30-minute limit stays.** Other organizations are never blocked.

## Left to the builder

- Table and column names and the migration.
- How the key scheme sits beside the cookie (for example a policy scheme that picks the key handler when a key header is
  present).
- How a background job reproduces the starter's access (sites and branches) and source.
- How a job is claimed so exactly one server runs it, and how a stale job is detected.
- How progress is reported from the import run.
- The e-mail's wording and link.
