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

1. A **cost-centre tree**, set on locations and assets and passed down to the work raised for them.
2. Every maintenance cost — labour, parts, contractors and other costs on work orders, and parts issued straight to a
   location — **belongs to a cost centre**, so cost can be reported by cost centre.
3. **Budgets** per cost centre per month, and a **budget report** comparing budget, committed spend and actual cost.

## Non-goals (0.20.0)

- Budgets split by work type, kind of cost or asset category; separate purchase budgets; budgets in other currencies.
- Charging rules, overhead allocation, or splitting one work order between cost centres.
- Approvals or alerts on overspend; links to accounting systems (an export sheet is the bridge).
- Issuing parts to a location from the phone (the phone issues to work orders only, as today) *(my call)*.
- Cost centres from the 4CSQL legacy database *(my call; "Fill in past work" covers history)*.
- Editable lists, brands and models, asset groups, yearly numbering (0.21.0).

## Anti-goals

- **Cost counted twice or in two cost centres.**
- **The Maintenance costs report changing its totals**: cost centres only filter and group it.
- **History rewritten by accident**: changing an asset's or location's cost centre never changes existing work; only "Fill in
  past work" (blanks only) and an explicit change on a work order or issue do.
- **Silent no-ops**: a refused action says why.

## Approaches considered

1. **Cost centre on the work order (chosen)**: labour, parts, contractors, other costs and purchase commitments for a work
   order follow its cost centre; direct issues carry their own. Fixing a wrong cost centre on a work order moves all its cost,
   which is what someone correcting a mistake expects.
2. **Stamped on every cost line when recorded**: history never moves, but a mistake is fixed line by line over four kinds of
   line, and reports read four places.
3. **Derived at report time from the asset or location**: no new fields on work, but every reorganization of the tree rewrites
   the past (the anti-goal above).

## What CMX has today (and keeps)

- Work orders with labour (`WorkOrderLabor`, by work date), parts issued from stores (`StockTransaction` type Issue with a
  `WorkOrderId`, by date; returns credit them), contractor lines (`WorkOrderContractor`, cost by `DoneOn`) and other costs
  (`WorkOrderCost`, dated when recorded). The Maintenance costs report (`ReportEndpoints.CostLinesAsync`) counts exactly these.
- All new work orders — form, request conversion, PM generation (single and group), work groups, follow-ups, the phone — go
  through one create routine in `WorkOrderEndpoints`.
- On the web, parts can be issued to (and returned from) a location without a work order (`StockTransaction.LocationId`). These
  are **not** in the Maintenance costs report today. The phone issues to work orders only.
- Purchase orders: lines may name a work order; receiving a work-order line records its cost on the work order; stock lines go
  into stores (cost when later issued). Orders can be edited only in draft; closed and cancelled work orders only after reopening.
- Excel import and "current data" export of every list; sites and access (0.9.0).

## Design

### The cost-centre tree

- **Cost centres** (Settings › Cost centres): code (unique), name, **parent** (optional, no cycles), **site** (optional: one
  site's, else for all), active. Edited with *Manage settings* *(my call)*; seen by everyone who sees work.
- Shown and picked as a tree ("Production › Line 1 › Packing"). A cost centre in use can't be deleted (make it inactive);
  inactive ones aren't offered for new records but stay on old ones.
- A site-limited person sees cost centres of their sites and those for all sites, as with teams.

### Where it is set and how it passes down

- **Locations** and **assets** gain a cost centre (optional).
- The cost centre **in force** for an asset is its own, else its parent asset's (up the asset tree), else its location's (up the
  location tree). For a location: its own, else its parent's.
- **Requests and PM schedules** have an optional cost centre that **overrides**; empty means "follow the asset or location"
  *(my call: so reorganizing the tree needs no PM edits)*.
- **Every work order** gets its cost centre when created: the one given (the form's choice, the request's or PM's override),
  else the one in force for its asset, else its location. Each work order of a group PM takes the PM's override, else its own
  asset's *(my call)*. The form shows the resolved one and lets it be changed.
- **Direct issues and returns** to a location (web) can name a cost centre, defaulting to the location's in force.
- Changing an asset's or location's cost centre never touches existing work or issues.

### Correcting cost centres

- **Change cost centre** on a work order works in **any status**, closed and cancelled included, without reopening, for people
  who can edit work orders *(my call)*; also as a bulk action on the work orders list. The change is logged in the work order's
  history. Its labour, parts, contractors, other costs and purchase commitments move with it.
- A direct issue's cost centre is changed from the stock movement list (*Manage inventory*).
- **Fill in past work** (Settings › Cost centres): work orders and direct issues dated on or after a chosen date that have **no**
  cost centre get the one in force today. It shows how many it will change first, never overwrites, and can be run again.

### What counts as cost, and when

- **Actual, on work orders** — exactly what the Maintenance costs report counts, to the work order's cost centre: labour by work
  date; parts issued less returned, by transaction date; contractors by done date; other costs by the date recorded.
- **Actual, direct issues** — parts issued to a location less parts returned from one, by date, to the issue's cost centre. Shown
  as their own column, so the budget report's actual = **Maintenance costs report + direct issues** *(my call: the Maintenance
  costs report keeps its totals)*.
- **Committed** — approved purchase order lines **for a work order** not yet fully received (open quantity × net price ×
  exchange rate, in the base currency; partly received lines count their open part; draft, rejected and cancelled orders don't
  count), by the order's expected date, else its order date, to the **work order's** cost centre. Stock lines without a work
  order aren't commitments: they become cost when issued *(my call)*. Purchase order and request lines therefore need no cost
  centre of their own, and the order shows the work order's.
- Cost with no cost centre is shown as **"No cost centre"**.

### Budgets

- **Budgets** (Reports › Budgets *(my call)*): one amount per **cost centre per month**, for any months, in the organization's
  currency. Entered in a grid (cost centres down, months of a chosen year across, a total column; a value typed in the total
  spreads evenly over the months), or by the import sheet. Edited with *Manage settings*; seen with *View reports*.

### Budget report

- **Reports › Budget against actual**: for a period (default: this year to date), per cost centre and month: **budget**,
  **committed**, **actual** (labour, parts, contractors, other, direct issues) and **variance** (budget − actual − committed),
  with totals.
- A cost centre's figures cover **all its work, at every site** — its budget is for the whole cost centre *(my call)*. There is
  no site filter; a site-limited person sees the cost centres of their sites and those for all sites (with their whole
  figures), never another site's cost centres.
- Parent cost centres add up their own figures and all their children's; children the viewer can't see are added in but not
  listed.
- Filter: cost centre (with its children). Print/PDF and CSV like other reports.

### Other reports

- The **Maintenance costs** report gains a cost-centre filter (with children) and a "by cost centre" grouping; its totals for
  the same filters are unchanged.

### Import and export

- New sheets: **Cost centres** (code, name, parent code, site code, active) and **Budgets** (cost centre code, year, month,
  amount; re-importing replaces that month's amount).
- The **Locations**, **Assets** and **PM schedules** sheets gain a "Cost centre code" column. All in the "current data" export.

### Permissions

No new permissions: cost centres, budgets and "Fill in past work" with *Manage settings*; budgets and the budget report seen
with *View reports*; changing a work order's cost centre with *Edit work orders*; a direct issue's with *Manage inventory*.

### Data (one migration, `CostCentres`)

- New `CostCentres` (tenant, code unique per tenant, name, parent, site, active) and `Budgets` (tenant, cost centre, year,
  month, amount; unique per cost centre, year and month).
- `CostCentreId` (nullable) on `Locations`, `Assets`, `WorkRequests`, `PmSchedules`, `WorkOrders` and `StockTransactions`
  (direct issues and returns).
- Every new text in Arabic.

## Builder's questions, settled *(my calls)*

- **Direct issues** count as actual in the budget report, in their own column; the Maintenance costs report keeps its totals.
- **Committed** counts only order lines for a work order, under the work order's cost centre; stock purchases become cost when
  issued.
- **The phone** keeps issuing to work orders only.
- **PMs and requests** have an optional override; empty follows the asset or location when the work order is made.
- **Budget report and sites**: whole-cost-centre figures; site-limited people see their sites' and shared cost centres only.
- **Corrections** work on closed work orders without reopening, singly or in bulk, and are logged.
- **Group PMs**: each work order takes the PM's override, else its own asset's.
- **4CSQL cost centres** aren't imported in this release.

## Review focus (most likely to bite)

1. Actual on work orders by cost centre (with "No cost centre") equals the Maintenance costs report for the same period.
2. The cost centre in force through the asset and location trees, and given to every work order however it is created.
3. "Fill in past work" never overwrites, is repeatable, and counts what it will change.
4. Committed spend: partly received lines, foreign currency, draft and cancelled orders left out, moving with the work order.
5. A site-limited person sees and picks only their sites' cost centres and those for all sites.

## Left to the builder

Endpoint shapes, component structure, query details, test layout, exact wording beyond what's above.
