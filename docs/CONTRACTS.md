# Contracts and contractors (0.17.0)

Lifts, chillers, fire systems and generators are usually serviced under contract. CMX keeps the **companies** you work
with, records **contractor work on work orders** at the company's rates, and tracks **service contracts** — their terms,
renewals, covered assets and the work done under them — with a warning before a renewal or end date passes.
Organizations that use none of this see no change: reports show a Contractor column of zeros.

## Who may do what

| Permission | Can |
| --- | --- |
| *View contracts* (`contracts.view`) | See contracts, their files and the "contracts needing attention" line in the bell. |
| *Create and edit contracts* (`contracts.manage`) | Create, edit, amend, terminate and delete contracts; edit contract types. |
| *Manage suppliers* (`suppliers.manage`) | Edit companies (roles, contacts, addresses, rates) and contractor categories. |
| *Edit work orders* (`workorders.edit`) | Add, change and remove contractor lines and their planned hours. |
| *Execute work orders* (`workorders.execute`) | Record the hours done, the date and the person on existing contractor lines. |

Built-in roles: Technician and Viewer see contracts; Maintenance Manager and Administrator also manage them.

## Companies

Purchasing › **Companies** (was Suppliers) lists every company with a role filter.

- **Roles**: *Supplier* (purchase orders and requests may pick it), *Contractor* (contractor lines, contracts and
  employees may name it), *Manufacturer* — any combination, at least one. Existing suppliers keep the Supplier role, and
  the old contractor tick became the Contractor role.
- The **main contact and address** stay as before (orders and "Send to supplier" use them). **Other contacts** (name,
  role, phone, e-mail) and **other addresses** (label, address, city, country) are lists on the company.
- **Contractor categories** (Settings › Contractor categories): kinds of contractor work with a **default hourly rate** in
  the base currency. A contractor company can have **its own rate per category** (edited on the company).
- **Employees** (People) can be linked to a contractor company. Their logged hours then count as **contractor cost** in
  every report. The old free-text company still shows when no company is linked.
- A company that loses a role or is made inactive keeps everything that already names it (orders, lines, contracts,
  employees); pickers stop offering it and it can't be newly chosen. A company that is in use can't be deleted.

## Contractor lines on work orders

A work order's **Contractors** tab lists the contractor work: company, category, person (one of the company's contacts,
or a name), planned hours, hours done, date done, hourly rate and cost.

- The **rate** is copied onto the line when it is added (the company's rate for the category, else the category's
  default, else 0) and again only when the line's company or category changes. Later rate changes never alter a line.
- **Cost** = hours done × rate, dated by the date done (the day it was recorded when no date is given). Planned cost =
  planned hours × rate.
- Closed or cancelled work can't change. The job card prints the lines.
- The work order's **totals** and **plan vs actual** gain *Contractor*: the lines plus the labour of company-linked
  employees (which leaves *Labour*, so nothing is counted twice). Work groups roll contractor cost up the same way.

## How costs count

Every cost report is built on one sum per work order and month: Labour, Parts, Other and **Contractor**. Contractor is
contractor lines (by date done) plus labour logged by employees linked to a company. *Maintenance costs* has a Contractor
column and chart series; the asset's Costs tab, the failure tree and asset work history include it in their totals.
*Maintenance costs by technician* stays per person (their labour only). The "Labour hours" columns stay labour hours.

## Service contracts

Maintenance › **Contracts** (numbered `CON-00001` for the whole organization).

- **Record**: title, company (Contractor role), manager (a user), type (Settings › Contract types: Maintenance,
  Inspection, Lease, Warranty to start with), trade, site (empty = the whole organization), notes and files.
- **Terms**: start and end dates, value and currency, payment terms, expected work orders per year, quarter or month,
  **automatic renewal** with its length in months, the **notice period** (days before the end by which the renewal must be
  stopped) and the **alert** (days before that deadline, or before the end, that the contract needs attention; default 60).
- **Covered assets**, each with an optional yearly cost. A contract with a site may only cover that site's assets.
- **Periods**: the first is the start–end dates and value; each renewal adds one. **Amendments** record a change with a
  date, a description and optionally a new value (it applies to the current period and later renewals). After a renewal
  the value and start date can only change by amendment.
- **Terminate** (date and reason) stops renewal and alerts at once; the contract shows as *Terminated* from that date.
  A contract named on work orders or PMs can't be deleted — terminate it.
- **Status** comes from the dates: *Upcoming* (starts later), *Active*, *Expired* (past its end, not renewing),
  *Terminated*.

### Renewal

A background pass (hourly) renews active contracts with automatic renewal whose end has passed: each new period starts
the day after the previous end and runs for the renewal length (start + N months − 1 day; when the months land in a
shorter month, the period ends on that month's last day — a one-month period from 31 January ends on 28/29 February).
It repeats until the end is today or later, so a server that was off for months catches up. The value is copied from
the latest period. Renewal runs whether or not e-mail is on.

### Alerts

- **Deadline**: with automatic renewal, the last day to stop it (end − notice days); otherwise the end date.
- From *alert* days before the deadline up to the deadline, the contract **needs attention**: it is counted in the
  **bell** for people who may view contracts (only contracts at sites they can see) and listed by Contracts › *Needing
  attention*.
- **One e-mail per deadline** (switch *Contract needs attention*, `contract.alert`; personal opt-outs apply) goes to the
  contract's manager and the people named in Settings › Contracts. With neither, everyone who may manage contracts is
  told. A site's contract only reaches people who may see that site. After a renewal the new deadline alerts again.

## Links

- Work orders and PMs have a **Contract** field: active and upcoming contracts usable at their site, those covering the
  asset first. A PM passes its contract to the work orders it creates. A contract for another site, or one that has
  ended, can't be newly named; work that already names one keeps it.
- A work order or request whose asset is covered by an active contract shows **Covered by CON-00003 · Lift Co**.
- The asset page lists the contracts covering it.

## Contract report

Reports › **Contracts** (period, default the last 12 months; optional site): per contract the company, type, status,
**value** of its periods overlapping the period (in the contract's currency), **spend** (all costs of work orders naming
the contract, dated in the period, in the base currency), work orders **completed** in the period and the **expected**
number scaled to the part of the period the contract runs. With a site: that site's and organization-wide contracts,
counting only that site's work.

## Not in 0.17.0

The supplier catalogue per part and purchase units (next release), contractor lines on the phone app, import sheets for
contracts and companies' contacts, and a contractor portal. The asset's own warranty / service-contract field is unchanged.
