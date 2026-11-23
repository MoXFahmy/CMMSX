# CMX 0.20.0 — Cost centres and budgets

Roadmap release 0.20.0 (cost centres, budgets and editable lists), split at the user's request: this release is cost centres and
budgets; editable lists, brands and models, asset groups and yearly numbering become 0.21.0, and later releases move up one.
Status: design for approval.

## Why

Finance asks for maintenance cost by department or cost centre, against a budget. CMX knows the cost of every work order —
labour, parts, contractors and other costs — but not whose cost it is: purchase order lines carry only a free-text account
code, and nothing compares spend with a budget. DimoMaint has an account-assignment tree with inheritance, budgets and budget
analyses.

## Decisions taken with the user

- **Split** (A): cost centres and budgets now; lists and numbering in 0.21.0.
- **Budget shape** (A): one number per cost centre per month.
- "Go with your picks" for the rest — the calls below marked *(my call)*.

## Goals

1. A **cost-centre tree**, set on locations and assets and passed down to the work and purchases raised for them.
2. Every cost — labour, parts, contractors, other costs on work orders, parts issued straight to a location, purchase order
   lines — **belongs to a cost centre**, so cost can be reported by cost centre.
3. **Budgets** per cost centre per month, and a **budget report** comparing budget, committed spend and actual cost.

## Non-goals (0.20.0)

- Budgets split by work type, kind of cost or asset category; separate purchase budgets; budgets in other currencies.
- Charging rules, overhead allocation, or splitting one work order between cost centres.
- Approvals or alerts on overspend; links to accounting systems (an export sheet is the bridge).
- Editable lists, brands and models, asset groups, yearly numbering (0.21.0).

## Anti-goals

- **Cost counted twice or in two cost centres** (a work order's parts both on the work order and as a location issue).
- **Reports that change their totals**: the Maintenance costs report's totals stay what they are today; cost centres only
  group and filter them.
- **History rewritten by accident**: changing an asset's or location's cost centre never changes existing work; only the
  explicit "fill in" action, or editing a work order, does.
- **Silent no-ops**: a refused action says why.

## Approaches considered

1. **Cost centre on the work order (chosen)**: labour, parts, contractors and other costs follow their work order's cost centre;
   direct issues and purchase order lines carry their own. Fixing a wrong cost centre on a work order moves all its cost, which
   is what someone correcting a mistake expects.
2. **Stamped on every cost line when recorded**: history never moves, but a mistake is fixed line by line over four kinds of
   line, and reports must read four places.
3. **Derived at report time from the asset or location**: no new fields on work, but every reorganization of the tree rewrites
   the past, the anti-goal above.

## What CMX has today (and keeps)

- Work orders with labour (`WorkOrderLabor`, by work date), parts issued from stores (`StockTransaction` type Issue with
  `WorkOrderId`, by date; returns credit them), contractor lines (`WorkOrderContractor`, cost by `DoneOn`), other costs
  (`WorkOrderCost`, dated when recorded). All new work orders — form, request conversion, PM generation, work groups, follow-ups,
  the phone — go through one create routine in `WorkOrderEndpoints`.
- Parts can be issued without a work order, to a location (`StockTransaction.LocationId`), on the web and the phone.
- Purchase requests (lines with work order, asset, store) and purchase orders (lines with work order, store and a free-text
  `AccountCode`); orders are approved, then received.
- The Maintenance costs report and the asset's Costs tab; Excel import and "current data" export of every list.
- Sites and access (0.9.0): people may be limited to sites; lists may belong to one site.

## Design

### The cost-centre tree

- **Cost centres** (Settings › Cost centres): code (unique), name, **parent** (optional, no cycles), **site** (optional: a
  cost centre for one site, else for all), active. Edited with *Manage settings* *(my call)*; seen by everyone who sees work.
- Shown and picked as a tree ("Production › Line 1 › Packing"). A cost centre in use can't be deleted (make it inactive);
  inactive ones are not offered for new records but stay on old ones.
- Sites: a site-limited person sees cost centres of their sites and those for all sites, as with teams.

### Where it is set and how it passes down

- **Locations** and **assets** gain a cost centre (optional).
- The cost centre **in force** for an asset is its own, else its parent asset's (up the asset tree), else its location's (up the
  location tree). For a location: its own, else its parent's. *(my call)*
- **Requests, work orders and PM schedules** copy the cost centre in force for their asset (else location) when created, and can
  be changed. A PM passes its own to the work it generates; a request passes its own to the work order made from it; work
  groups, follow-ups and phone-created work orders take it from their asset or location like the form.
- **Purchase request and order lines** take their work order's cost centre, else their asset's; the buyer can change it on the
  order. Orders made from requests keep each line's. The free-text account code stays.
- **Direct issues** (parts issued to a location without a work order, web and phone) can name a cost centre, defaulting to the
  location's in force.
- Changing an asset's or location's cost centre never touches existing work, purchases or issues.

### Fill in existing work

- On Settings › Cost centres, **"Fill in past work"** *(my call)*: for work orders (and direct issues) created on or after a
  chosen date that have **no** cost centre, set the one in force for their asset or location today. It shows how many it will
  change before it does, never overwrites one that is set, and can be run again.

### What counts as cost, and when

The same figures the Maintenance costs report uses today, attributed to a cost centre:

- **Labour**: hours × rate, by work date, to the work order's cost centre.
- **Parts**: issues to a work order (less returns), by transaction date, to the work order's cost centre; direct issues to a
  location, by date, to the issue's cost centre.
- **Contractors**: hours done × rate, by done date, to the work order's cost centre.
- **Other costs**: by the date recorded, to the work order's cost centre.
- **Committed**: approved purchase order lines not yet fully received (open quantity × net price × exchange rate, in the base
  currency), by the order's expected date, else its order date, to the line's cost centre.

Cost with no cost centre is shown as **"No cost centre"**, so totals always match the Maintenance costs report.

### Budgets

- **Budgets** (Reports › Budgets *(my call)*): for a chosen **period** — a year, or
  any run of whole months — one amount per **cost centre per month**, in the organization's currency.
- Entered in a grid (cost centres down, months across, a total column; a value typed in the total spreads evenly over the
  months *(my call)*), or by the import sheet. Edited with *Manage settings*; seen with *View reports*.
- One budget figure per cost centre and month: periods can't overlap for the same cost centre *(my call)*.

### Budget report

- **Reports › Budget against actual**: for a period (default: this year to date), per cost centre and month: **budget**,
  **committed**, **actual** (labour, parts, contractors, other) and **variance** (budget − actual − committed), with totals.
- Parent cost centres add up their children (their own budget and cost plus their children's) and can be expanded.
- Filters: site, cost centre (with its children). Print/PDF and CSV like other reports.

### Other reports

- The **Maintenance costs** report gains a cost-centre filter (with children) and a "by cost centre" grouping. Its totals are
  unchanged.

### Import and export

- New sheets: **Cost centres** (code, name, parent code, site code, active) and **Budgets** (cost centre code, year, month,
  amount; one row per month — re-importing replaces that month's amount *(my call)*).
- The **Locations** and **Assets** sheets gain a "Cost centre code" column. All included in the "current data" export.

### Permissions

No new permissions: cost centres, budgets and "fill in" with *Manage settings*; budgets and the budget report seen with *View
reports*; picking a cost centre on work, purchases and issues with the permission that edits them.

### Data (one migration, `CostCentres`)

- New `CostCentres` (tenant, code unique per tenant, name, parent, site, active) and `Budgets` (tenant, cost centre, year,
  month, amount; unique per cost centre, year and month).
- `CostCentreId` (nullable) on `Locations`, `Assets`, `WorkRequests`, `WorkOrders`, `PmSchedules`, `PurchaseRequestLines`,
  `PurchaseOrderLines` and `StockTransactions` (direct issues only).
- Every new text in Arabic.

## Review focus (most likely to bite)

1. Totals by cost centre (including "No cost centre") equal the Maintenance costs report's totals for the same period.
2. The cost centre in force through the asset tree and location tree, and copying it into every way work is created.
3. "Fill in past work" never overwrites, is repeatable, and counts what it will change.
4. Committed spend: partly received lines, foreign currency, cancelled and draft orders left out.
5. A site-limited person sees and picks only their sites' cost centres and those for all sites.

## Left to the builder

Endpoint shapes, component structure, query details, test layout, exact wording beyond what's above.
