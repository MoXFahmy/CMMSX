# Cost centres and budgets (0.20.0)

Finance wants maintenance cost charged to the organization's own cost centres and compared with a budget. CMX adds:

- a **cost-centre tree** ("Production › Line 1 › Packing"), for one site or for all;
- a cost centre on **locations and assets** that every new **work order** takes, so its labour, parts, contractors, other
  costs and purchase commitments are charged to it;
- **budgets**: one amount per cost centre per month;
- the **Budget against actual** report, and a cost-centre filter and grouping on the Maintenance costs report.

Organizations that set up no cost centres see no change: work has no cost centre and the reports show it as
"No cost centre".

## Who may do what

| Permission | Can |
| --- | --- |
| *View work orders* | See the cost-centre tree and each work order's cost centre. |
| *Manage settings* | Add, change and delete cost centres; enter budgets; fill in past work. |
| *View reports* | See budgets and the Budget against actual report. |
| *Edit work orders* | Change a work order's cost centre, in any status, one at a time or from the list. |
| *Manage inventory* | Change the cost centre of parts issued straight to a location. |

There are no new permissions.

## The tree

**Settings › Cost centres**: code (unique), name, **parent** (optional; a cost centre can't sit under itself or one of its
own children), **site** (optional: one site's, else for every site) and active. A cost centre in use can't be deleted; make
it inactive: it is no longer offered for new records but stays on old ones.

Someone limited to some sites sees the cost centres of their sites and those for all sites, as with teams.

## Where it is set and how it passes down

- **Locations and assets** have an optional cost centre.
- The cost centre **in force** for an asset is its own, else its parent asset's (up the asset tree), else its location's
  (up the location tree). For a location: its own, else its parent location's.
- **Requests and PM schedules** have an optional cost centre that **overrides** the asset's or location's. Empty means
  "follow the asset or location", so reorganizing the tree needs no PM changes.
- **Every new work order** gets its cost centre when it is created: the one chosen on the form, the request's or PM's
  override, else the one in force for its asset, else for its location. Each work order of a group PM takes the PM's
  override, else its own asset's. The work order page shows it.
- **Parts issued straight to a location** (or returned from one) without a work order can name a cost centre; empty takes
  the location's.
- Changing an asset's or location's cost centre **never changes existing work or issues**. Use *Change cost centre* or
  *Fill in past work* for those.

## Correcting cost centres

- **Change cost centre** on a work order (its ⋯ menu) works in any status, closed and cancelled included, without
  reopening it. On the work orders list, select several and use **Change cost centre** in the bar. The change shows in
  each work order's history, and its labour, parts, contractors, other costs and purchase commitments move with it.
- A direct issue's cost centre is changed from **Inventory › Stock movements** (the pencil beside it).
- **Fill in past work** (Settings › Cost centres): work orders created on or after a date, and direct issues dated on or
  after it, that have **no** cost centre get the one in force today. It counts what it will change first, never
  overwrites, and can be run again.

## What counts as cost, and when

- **Actual, on work orders**: exactly what the Maintenance costs report counts, charged to the work order's cost centre:
  labour by work date, parts issued less returned by transaction date, contractors by done date, other costs by the date
  recorded.
- **Actual, direct issues**: parts issued to a location less parts returned from one, by date, to the issue's cost centre.
  They have a column of their own, so the budget report's actual is the Maintenance costs report plus direct issues.
- **Committed**: the open part of approved (or partly received) purchase order lines **for a work order**: open quantity ×
  net price × exchange rate, in the organization's currency, dated by the order's expected date, else its order date, and
  charged to the work order's cost centre. Draft, rejected and cancelled orders don't count. Stock lines without a work
  order aren't commitments: they become cost when issued.

## Budgets

**Reports › Budgets** (also in the menu under Insights): choose a year; cost centres run down the page and months across,
with a total. Type an amount in a month, or type a total to spread it evenly over the months (rounded to the cent, with what
is left over on December). An empty month or 0 means no budget. *Save* keeps the changes. Budgets can also be imported (below).

A parent's row is its own budget. In the report, a parent adds up its own budget and all its children's.

## Budget against actual

**Reports › Budget against actual**, for a period (default: this year to date):

| Column | Meaning |
| --- | --- |
| Budget | Budgets for the months in the period. |
| Committed | Open order lines for work orders (above). |
| Labour, Parts, Contractor, Other | Actual cost on work orders. |
| Direct issues | Parts issued straight to locations. |
| Actual | All of the above actual columns. |
| Variance | Budget − actual − committed. |

- **By cost centre** (default): one row per cost centre, children indented under their parents. A parent's figures include
  its children's; children the person can't see are added in but not listed. A last row shows cost with **no cost
  centre**. The totals add up the top level and "No cost centre".
- **By month**: one row per month.
- **Cost centre** filter: that cost centre and those under it.
- A cost centre's figures cover **all its work at every site**; there is no site filter.
- Print/PDF and CSV like the other reports.

The **Maintenance costs** report gains the same cost-centre filter (with children) and a **Cost centre** grouping; its
totals for the same filters don't change.

## Import and export

Settings › Import has two new sheets, and the "current data" export includes them:

- **Cost centres**: code, name, parent code, site code (empty = every site), active. Parents can come later in the same sheet.
- **Budgets**: cost centre code, year, month (1–12), amount. Importing a month again replaces its amount; 0 removes it.

The **Locations**, **Assets** and **PM schedules** sheets gain a **Cost centre code** column.

## Not in this version

- Cost centres on the phone app.
- Cost centres from the 4CSQL legacy database ("Fill in past work" covers history once the tree is set up).
- A cost centre of their own on purchase orders and requests (they show the work order's).
