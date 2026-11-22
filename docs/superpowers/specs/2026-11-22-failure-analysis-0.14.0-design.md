# CMX 0.14.0 — Failure codes and maintenance analysis (design)

Roadmap release 0.14.0 (DimoMaint plan, version 5). Medium–large. Builds on 0.8.0 downtime, 0.12.0 PM forecast and
0.13.0 checklists; needs nothing new from other releases.

## Intent and why

One failure code can't separate what people saw from what was wrong and what was done about it. Splitting it into
**symptom → cause → remedy** gives managers a failure tree they can act on, and gives the AI assistant in 0.15.0 clean
history to learn from. Alongside that, managers want the analysis DimoMaint offers (failure tree, responsiveness per
order, PM completion, a PM year plan, work history, breakdowns), the yearly cost of each asset including what its
downtime cost, and every report as a PDF with the organization's logo to send or file.

## Goals

1. **Three code lists.** Symptoms and remedies are new editable lists; today's failure codes become **causes**.
   Settings › Lists, like failure codes today (code, name, active; `settings.manage`).
2. **Recorded where people know them.**
   - Symptom: on a request (web form, phone *New request*), copied to the work order when the request is approved or
     auto-approved; on a work order (form, detail, phone *New work order*), editable until it is closed.
   - Cause and remedy: on completion (web completion dialog, a work group's report, the phone's completion), and
     editable on the work order's completion section like the failure code today.
   - All three show on the work order page, the phone job, the printed job card, the work order list (columns and
     filters) and its export; the symptom also shows on the request page and as a request list column.
   - As with failure codes today, a code in use can't be deleted (mark it inactive); pickers offer active codes, and a
     record keeps an inactive code it already has. A work group's report sets cause and remedy on its work orders, as
     it sets the failure code today.
3. **Smart ordering.** Pickers list, first, the causes most often recorded with the chosen symptom, and the remedies
   most often recorded with the chosen cause (from completed work of the last two years); then everything else in code
   order. On the phone the same ordering works offline from counts in the bundle.
4. **Reports** (all in the existing report shape and screen):
   - **Failure tree** (new, Reliability): symptom → cause → remedy with work orders, labour hours, downtime hours and
     cost at each level, "Not recorded" where a code is missing. Each line opens the work order list filtered to those
     work orders.
   - **Responsiveness** (new, Work): each request and work order with a response target in the period — response target,
     responded at, response time, met / missed / pending — and its completion target the same way. Group by *Order*
     (the list, default), *Priority* or *Trade* (averages and % met). Each order row opens its record.
   - **PM annual plan** (new, Work): one row per PM, twelve month columns for a chosen year. Past months show what
     happened (done on time, late, missed, cancelled); this and future months show what is planned, from the 0.12.0
     forecast. Site filter.
   - **Asset work history** (new, Assets): one asset (optionally with its components) — every work order in the period
     with date, type, title, symptom / cause / remedy, labour hours, cost and downtime hours, with totals.
   - **PM compliance** gains *Asset category* grouping (it already has month, PM and trade): this is the roadmap's
     "PM completion rate".
   - **Maintenance costs** gains labour hours as a column and *Technician* grouping (each person's labour hours and
     cost, and the work orders they logged hours on; parts and other costs aren't per person, so they are blank there):
     this is the roadmap's "breakdowns".
5. **Asset costs by year, with downtime cost.**
   - *Downtime cost per hour* on an asset category (new); an asset without its own uses its category's. The
     Reliability report and the new figures use that effective cost.
   - Asset › **Costs** tab: per year (the last five years and the current one, plus any older year with costs) labour,
     parts, other costs, downtime hours, downtime cost and total, with a stacked chart; *Include components* adds the
     asset's children (all levels).
6. **Print to PDF.**
   - Settings › Organization › **Logo** (PNG, JPEG or WebP, up to 1 MB; remove to go back to none).
   - Every report's print layout: logo, organization name, report title, period and the filters chosen (site, group
     by, year, asset), printed date and by whom, then the totals, the chart and the full table; A4 landscape; table
     headers repeat on each page; in the person's language and direction. The button reads *Print or save as PDF*.
   - The printed job card shows the logo too.
7. **Import and export.** Three new sheets, *Symptoms*, *Causes* (today's failure codes) and *Remedies* (code, name,
   active; added and updated by code, like *Trades*); the *Categories* sheet gains *Downtime cost per hour*.

## Non-goals

- Linked code lists (causes tied to symptoms, remedies to causes) — chosen against, see Approaches.
- A server-made PDF file, e-mailed or scheduled reports.
- Costs of round work orders split per asset (rounds keep their costs on the round, as in Maintenance costs today).
- Contractor costs as their own column (they are *Other costs* until contracts in 0.17.0).
- New completion rules: the existing *require a failure code on breakdowns* stays as it is, now worded *require a
  cause on breakdown work*; symptom and remedy are optional everywhere.
- Renaming the `FailureCode` table or API route (screens say *Cause*; the code keeps its name).

## Anti-goals

- An existing organization meeting a new required field, or losing anything: failure codes and their history carry
  over as causes, untouched.
- A printed report that differs from the screen, or Arabic that prints wrong.
- A report link that opens a list showing different work orders from the report's count.
- Codes picked on the phone offline that are lost or refused because the phone's list was older (an inactive code is
  still accepted on a record that already had it, as failure codes are today).

## Constraints

- House rules: tenant and site filters on every query (0.9.0 site access: reports, the Costs tab, pickers and the
  phone bundle see only allowed sites); the same checks on screens, the phone and the import; Arabic for every new
  text (web locales and server `ar.json`); tests written first; EF lessons (new child rows marked Added; no
  `IgnoreQueryFilters()` inside a subquery).
- Permissions: lists with `settings.manage`; each report with its existing permission (`workorders.view`, `pm.view`,
  `assets.view`); the Costs tab with `workorders.view`; the logo with `settings.manage` to change, any signed-in user
  to view.
- Times: report periods use the organization's time zone as today; nothing defaults to a time later than now.
- One migration; nullable columns only; no data rewrite.

## Approaches

**Code lists.** (A) three separate lists; (B) linked lists, where pickers offer only codes linked to the previous
choice; (C) separate lists with smart ordering from history. B means building and maintaining links for every code,
extra import sheets, and old 4CSQL history that doesn't fit the tree. **Chosen: A with C's ordering** — no setup,
history carries over, and the ordering nudges people towards consistent answers.

**PDF.** (A) a proper print layout and the browser's *Save as PDF*; (B) a server-made PDF file (a PDF library —
QuestPDF is free only under $1M revenue — bundled Arabic fonts, charts dropped or redrawn); (C) a headless browser on the
server (heavy). **Chosen: A** — Arabic and charts come out exactly as on screen, no new library or licence; it is two
clicks instead of one.

## Decided details

- **Failure tree scope:** work orders completed in the period (Completed or Closed, organization time zone) that have a
  symptom, a cause or a remedy, plus breakdowns (breakdown type or asset was down) without any code. Levels: symptom
  (sorted by work orders, most first), then its causes, then each cause's remedies. Cost = labour + parts + other of
  those work orders (all dates); downtime hours as in the Failure analysis report (a shared stop counted once). The
  chart shows the symptoms only. Sorting columns is off for this report (it would break the tree).
- **Responsiveness:** the same targets and outcomes as *Targets met* (requests: respond by → decided; work orders not
  raised from a request: start by → responded; completion: due → completed; pending while not past and not done;
  imported history with no time recorded isn't judged). Rows are orders whose response or completion deadline falls
  in the period; times in hours. A request's trade is its work order's once approved, else its request type's, else
  *No trade*.
- **PM annual plan:** a cell for a past month counts the work orders that PM generated with a due date in that month,
  by outcome as PM compliance decides it; a cell for this or a later month adds what the forecast still plans. A PM
  with no occurrence in the year isn't listed. A PM on several assets is one row, its asset column reading *Round of N
  assets* or *Group of N assets*. Rows are sorted by asset number, then PM code. Year: the current one by
  default, 2000–2100.
- **Asset work history:** asset picker (required); period; *Include components*; work orders on the asset itself
  (rounds that only list it in their steps aren't included, as in Maintenance costs).
- **Costs tab years:** a cost belongs to the year of the labour date, the stock transaction date and the other cost's
  creation, as in Maintenance costs; downtime hours are the asset's stops measured over its running time in that year
  (as the Downtime log does); downtime cost = hours × effective cost per hour (none when there's no cost per hour).
- **Smart ordering counts:** pairs (symptom, cause) and (cause, remedy) from work orders completed in the last two
  years, top 20 per symptom or cause; recomputed on request (cheap grouped query), not stored.
- **Phone:** the bundle carries symptoms, remedies and the pair counts; *New request* and *New work order* take a
  symptom; completion takes a remedy next to the cause; the job shows all three. A phone from before 0.14.0 keeps
  working (it simply sends no symptom or remedy).
- **Logo:** stored like attachments (file storage, one per organization, replaced on upload); served at a stable URL
  with caching; organization settings carry its presence.

## Left to the builder

- Layout of the print header and the month cells of the PM annual plan (a compact count per outcome is enough).
- How report rows carry their drill-down link (a row field the web reads, like `assetId` today) and the work order
  list's new filters (symptom, cause, remedy, *not recorded*, completed to).
- The report parameter for an asset (a new parameter type with the existing asset picker) and for a year.
- Exact wording of new texts (house style: short, plain, Arabic alongside).
