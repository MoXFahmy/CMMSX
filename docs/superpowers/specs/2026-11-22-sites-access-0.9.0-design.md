# CMX 0.9.0 — Sites and access (design)

Status: the user said "go ahead with the roadmap as written" (2026-11-22) and opted out of design questions, so every
choice below that the roadmap doesn't fix is marked **(call)**: it was made by the builder and can be changed later.

## Why

Every signed-in user sees every site today. Organizations with several plants or hotels need each person to see their own
site or building, and managers to see all sites side by side. The roadmap (artifact LRih8qcUuJGSA9CcCufPEq, release 0.9.0)
lists six parts: users limited to their sites, limits by part of the location tree, per-site settings, a group view across
sites, teams, and working hours per person.

## Goals

1. A signed-in person can be limited to some sites, or to some branches of the location tree. Nothing changes for existing
   organizations: no list means every site.
2. The limit holds everywhere: lists, records, pickers, reports, exports, the import, the dashboard, the alert bell, e-mails
   and the technician app's bundle. A record outside it answers "not found".
3. Each site can override the organization's numbering prefixes, working hours, default store, request recipients and
   e-mail sender name.
4. People who see several sites get per-site counters on the dashboard and a site switch in the header.
5. Named technician teams can be given work orders and PM schedules, and lists, the planning board and reports filter by team.
6. Each person can have a weekly working pattern; the planning board uses it for their capacity.

## Non-goals

- Limits per role (the roadmap's "or per role") **(call)**: limits are per user only; roles stay permission sets.
- Separate data per site for the shared catalogues: parts, suppliers, PM task library, trades, failure codes, categories and
  custom fields stay shared by all sites **(call)**.
- Business-hours SLA timing: working hours drive planning capacity only; response and completion targets keep counting
  calendar time.
- A teams sheet in the Excel import (teams are set up on the People page) **(call)**.

## Anti-goals

- A leak: a limited user must never see another site's record by id, by a report, an export, an e-mail or an attachment link.
- A slowdown or behaviour change for organizations that don't use limits.
- Duplicate numbers or codes because a check couldn't see another site's records.

## Approach

Three ways were considered:

- **A. One shared database filter (chosen).** The organization filter already sits on every entity in `CmmsDbContext`.
  A second named filter ("Site") on the entities that belong to a site covers all ~200 endpoints at once, including reads
  nobody remembers to check. EF Core 10 named filters let uniqueness checks and numbering look past it on purpose.
- **B. A check in each endpoint.** Explicit, but every list, report and lookup would need its own guard, and any one missed
  is a leak.
- **C. Separate databases or schemas per site.** Strong isolation, but cross-site views, shared catalogues and transfers
  between sites become very hard.

## Design

### Access model

- `AppUser.SiteIds` (list, empty = every site) and `AppUser.LocationIds` (branches of the location tree, empty = no branch
  limit).
- **(call)** With branches set, the person sees only those branches (in whatever sites they are); their sites are the
  branches' sites. With only sites set, they see those sites whole.
- Records without a location (site-level work, assets not placed) belong to no branch, so branch-limited people don't see
  them **(call)**, and must choose a location in their branches when they create or edit assets, requests, work orders and
  PM schedules.
- New locations added under an allowed branch are included automatically (the filter matches on the location path).

### Effective scope per request

- A middleware after authentication works out the person's scope and keeps it for the request: site ids (or every site)
  and the materialized paths of their branches. It is cached per user for 60 seconds and dropped when the user is saved
  (like permissions).
- The header switch sends `X-Cmx-Site: <site id>`. When that site is in the person's scope, the request is narrowed to it;
  otherwise the header is ignored. The technician app doesn't send it, so a phone always gets every allowed site.
- `ITenantContext` gains `SiteIds` and `BranchPaths` (default null = no limit), so background jobs and tools stay unlimited.

### The filter

- Named filters: "Tenant" (the existing organization + soft-delete filter) and "Site".
- On a site id: `Site` (its own id), `Location`, `Asset`, `WorkRequest`, `WorkOrder`, `PmSchedule`, `Store`,
  `PurchaseOrder`, `Team` (a team with no site belongs to all).
- Branch rule for `Location` (its path) and for `Asset`, `WorkRequest`, `WorkOrder`, `PmSchedule` (their location's path):
  the path starts with one of the person's branch paths.
- Child rows read on their own (labour, parts, costs, comments, status changes, tasks, assignees, work-order assets,
  stock levels and transactions, meters and readings, downtime, asset parts and transfers, PM schedule tasks and assets,
  PO lines) get a "Site" filter that their parent is visible, written as `scope is null || parent visible` so it falls
  away when there is no limit.
- `AllSites()` (= `IgnoreQueryFilters(["Site"])`) is used, and only used, for numbering, uniqueness checks and reading the
  names of things already linked to a visible record (e.g. a transfer's "from" site).

### Writes

- Validation already looks sites, locations and assets up through the filter, so ids outside the scope are refused with the
  existing "Choose a site" / "must belong to the selected site" messages.
- Branch-limited people must give a location (new message "Choose a location in your part of the site.").
- Code and number checks look past the Site filter, with a clear message when the clash is in a site the person can't see.

### User administration

- The user form gets **Sites** (empty = all) and **Limit to parts of the tree** (locations).
- **(call)** A person whose own access is limited can only give access inside it: they can't leave Sites empty (all), and
  can't pick sites or branches outside their scope. Unchanged values are always accepted.
- `/api/auth/me` returns the person's sites (for the switch), whether they are limited, and the site the request was
  narrowed to.

### Site switch and group view

- Header select, shown when the person can see more than one site: "All sites" and each site. Kept in the browser
  (`localStorage`), sent on every request, cleared when the server says it no longer applies. Changing it refreshes the
  page's data.
- Dashboard: with more than one visible site, a "Sites" card lists each site with open work, late work, completed in the
  last 30 days and requests waiting; each number opens that list filtered to the site.

### Per-site settings

Stored as JSON on the site (`Site.Settings`); each value empty means "use the organization's".

- Numbering prefixes for assets, work orders, requests and purchase orders. A site with its own prefix counts in its own
  sequence (`<sequence>:<site id>`); numbers already issued don't change.
- Working hours (a weekly pattern), used as the default for people who work only at that site.
- Default store: planned parts and the issue form on that site's work start from it.
- Request recipients (users): told about new requests at the site instead of everyone who can manage requests; either way,
  only people who can see the site.
- E-mail sender name: shown as "<name> via CMX" on e-mails about the site's records.

### Teams

- `Team`: code, name, optional site, colour, members (employees), active.
- People page › Teams tab (people.view to see, people.manage to edit).
- `WorkOrder.TeamId` and `PmSchedule.TeamId`; PM work inherits the schedule's team. Forms offer teams of the work's site
  (or of every site).
- When work is given to a team, its members get the usual "assigned to you" e-mail **(call)**.
- Work order list: team filter and column (also in export); planning board: team filter (people = members, work = the
  team's or its members'); reports that filter by trade also filter by team.
- Technician app: a person's jobs include open work given to a team they belong to **(call)**.

### Working hours

- `WorkingDay { Day, Start, End }`; a pattern is a list of them (one span per day).
- Organization pattern in settings replaces "Hours per day" (existing organizations: every day, 08:00 for their old
  number of hours, so the board shows what it did before).
- Site pattern (per-site settings), person pattern (on the employee). A person with none uses their site's when they work
  at exactly one site that has one, else the organization's.
- Planning board: each person's capacity per day comes from their pattern (0 on a day off).

## Constraints

- One version for the cloud app and the Android app: 0.9.0, CHANGELOG section, release through `scripts/release.sh`.
- Every new string in `en.json` and `ar.json`; server messages through `Translator` with Arabic entries.
- Additive migration only; existing data keeps working with no limits set.
- Tests run in the SDK container against the compose SQL Server.

## Left to the builder

Exact component layout, the order of endpoints touched, message wording beyond what is above, and how child filters are
grouped in code.
