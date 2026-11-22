# Sites and access

From 0.9.0 each person can be limited to some sites, or to some parts of the location tree, and each site can have
settings of its own. Nothing changes until you set something: a person with no sites sees every site, and a site with no
settings of its own uses the organization's.

## Limiting a person

Settings › Users › edit a user:

- **Sites**: the sites they see. Leave empty for every site.
- **Limit to parts of the tree**: places (a building, a floor, a production line…). They see those places and everything
  under them, and only the sites those places are in. New places added under them are included automatically.

The limit holds everywhere: lists, records, pickers, reports, exports, the import and its "current data" file, the
dashboard, the alert bell, e-mails and the technician app. A record outside it answers "not found".

- People limited to parts of the tree don't see records with no location (work at site level, assets not placed), and
  must choose a location when they add or edit assets, requests, work orders and PM schedules (for work orders and
  requests, an asset is enough: they take its location).
- Shared lists stay shared by every site: parts (each person sees the stock of their own sites' stores), suppliers, the
  PM task library, trades, failure codes, categories and custom fields.
- Someone whose own access is limited can only give access inside it, can only change people whose access is inside
  theirs (their details, password and signed-in devices), and can't add new sites.
- A person whose places were all deleted keeps seeing nothing (the user form shows "(deleted place)" until new places
  are given).
- People limited to parts of the tree add places only under places they see, and can't change the site's own record or
  settings (that's for people who see the whole site).
- Decisions that concern the whole organization still count every site: a part's average cost, and whether a shared
  record (part, supplier, PM task, trade, category, person) is in use before it can be deleted.
- Downloads (list exports) follow the site switch too.
- Codes and numbers stay unique across the whole organization. A clash with a record in a site the person can't see is
  refused with "… is used in a site you can't see." (in the import: that row is a problem).
- Changes apply at once: the person doesn't need to sign in again.
- Background work (PM generation, e-mail sending) is never limited.

## The site switch and the dashboard

People who see more than one site get a **site switch** in the header: *All sites*, or one site at a time. The choice
is kept in that browser. The technician app always shows every site the person may see.

The dashboard then shows **Work by site**: open work, late work, completed in the last 30 days and requests waiting,
for each site. Each number opens that list, filtered to the site.

## A site's own settings

Locations › edit a site › **This site's own settings** (leave anything empty to use the organization's):

| Setting | What it does |
|---|---|
| Number prefixes (work orders, requests, purchase orders, assets) | A prefix of its own starts its own count, for example A-WO-00001. Numbers already given don't change. Assets added by the Excel import keep the organization's numbering. |
| Default store | Planned and issued parts on the site's work start from this store. |
| Told about new requests | These people get the "new request" e-mail instead of everyone who can manage requests. They must see the whole site. |
| E-mail sender name | E-mails about the site's records come from "<name> via CMX". |
| Working hours | The weekly pattern for people who work only at this site and have no hours of their own. |

Whoever gets an e-mail must be able to see the record's site: people limited to other sites are left out.

## Teams

People › **Teams**: a code, a name, a site (or every site), a colour and the members.

- A work order or PM schedule can be given to a team (work generated from the schedule gets its team). Only active teams
  of the work's site, or of every site, can be chosen.
- The team's members get the usual "assigned to you" e-mail, and see the team's open work on their phones (with
  "Team: …" on the job).
- Work orders: a **Team** filter (More filters) and a Team column, also in the export.
- Planning board: a **Team** filter shows the team's members and the work given to the team or to its members.
- Reports › Labour hours and Maintenance costs can be grouped by team.
- A team with open work or PM schedules can't be deleted; mark it inactive instead. Its site can't change away from
  that work, and people with limited access give their teams a site.
- A PM round keeps its assets in parts of the site the editor can't see; such a round can't be turned into a single-asset
  or location schedule by them.

## Working hours

- Settings › Organization › **Working hours**: the weekly pattern for everyone (before 0.9.0 there was only "hours per
  day"; until you save a pattern, every day counts from 08:00 for that many hours).
- A site's own working hours (above), and a person's own (People › edit › Working hours).
- A person with no hours of their own uses their site's when they work at exactly one site that has its own; otherwise the
  organization's.
- The **planning board** shows each person's load against their hours for that day; a day off counts as 0, so any work
  planned on it shows as over.
- Targets by priority still count every hour; working hours only drive planning.
